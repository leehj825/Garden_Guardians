using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Food handling ------------------------------------------------------------------

    /// <summary>A Bramblekin picks <paramref name="food"/> up: it's hidden from the map (and from every search) until eaten, dropped or handed over.</summary>
    public static void PickUpFood(FoodShard food)
    {
        food.IsCarried = true;
        food.ClaimedBy = null;
        food.ClaimTimer = 0f;
    }

    /// <summary>A Bramblekin finished eating <paramref name="food"/>: its pool slot is freed.</summary>
    public void ConsumeFood(FoodShard food)
    {
        food.Deactivate();
        FoodEaten++;
    }

    /// <summary>Puts carried <paramref name="food"/> back on the ground at <paramref name="position"/>, loose for anyone to find.</summary>
    public static void DropFood(FoodShard food, Vector3 position)
    {
        food.Position = Grounded(position);
        food.IsCarried = false;
        food.DespawnTimer = FoodShard.DespawnLifespan;
    }

    /// <summary>A Grub eats <paramref name="food"/> off the ground. Returns false if someone got to it first.</summary>
    public bool GrubEat(FoodShard food)
    {
        if (!food.IsActive || food.IsCarried)
            return false;

        food.Deactivate();
        return true;
    }

    /// <summary>Hostility pays off: <paramref name="thief"/> takes <paramref name="victim"/>'s carried food on a successful blow.</summary>
    public void StealFood(Bramblekin thief, Bramblekin victim)
    {
        // Food in hand first; else a grab from an errand sack.
        FoodShard? food = victim.SurrenderFood();
        if (food is null && victim.TakeFromSack() && ActivateFood(victim.Position, FoodShardKind.Berry) is { } grabbed)
        {
            PickUpFood(grabbed);
            food = grabbed;
        }
        if (food is null)
            return;

        thief.ReceiveFood(food);
        Thefts++;
        QueueFloatingText(thief.Position, "Stolen!", HostileTextColor);
    }

    /// <summary>Queues <paramref name="count"/> pieces of Food in a ring of radius <paramref name="distance"/> around <paramref name="center"/>.</summary>
    private void ScatterFoodAround(Vector3 center, int count, float distance, FoodShardKind kind)
    {
        float baseAngle = (float)(Rng.NextDouble() * MathF.Tau);
        float half = Terrain.Size / 2f - Bramblekin.EdgeMargin;
        for (int i = 0; i < count; i++)
        {
            float angle = baseAngle + i * MathF.Tau / count;
            var position = new Vector3(
                Math.Clamp(center.X + MathF.Cos(angle) * distance, -half, half),
                Terrain.GroundHeight,
                Math.Clamp(center.Z + MathF.Sin(angle) * distance, -half, half));
            _pendingFoodSpawns.Add((position, kind));
        }
    }

    /// <summary>Memory: a member ran into danger — its whole group remembers the spot.</summary>
    public void NoteDanger(Bramblekin kin, Vector3 where)
    {
        if (GroupOf(kin) is { } group)
            group.Dangers.Remember(where, ElapsedSeconds);
    }

    // --- Deaths -----------------------------------------------------------------------

    /// <summary>This many starving to death in one season is a famine (a headline — see <see cref="Headline"/>).</summary>
    private const int FamineDeaths = 4;

    /// <summary>Starvation deaths so far this season.</summary>
    private int _starvedThisSeason;

    /// <summary>
    /// A Bramblekin dies: it drops any carried food and is marked dead
    /// immediately (so nothing keeps targeting it), but its removal from
    /// <see cref="Colony"/> is deferred to the end of the frame so this is
    /// safe to call from inside a Colony iteration (a strike, a pounce).
    /// </summary>
    public void Kill(Bramblekin kin, DeathCause cause, ICombatant? killer)
    {
        if (kin.IsDead)
            return; // Already dead this frame; don't double-count it.

        RecordDeath(kin, cause); // Before MarkDead, while its status is still its own.
        NoteBereavement(kin);
        if (cause == DeathCause.Predator && GroupOf(kin) is { } mourners)
            mourners.Dangers.Remember(kin.Position, ElapsedSeconds); // Its group won't forget where it fell.
        if (kin.Errand is { } errand)
            AbandonErrand(kin, errand);
        kin.MarkDead();
        _pendingKinRemovals.Add(kin);

        string how;
        switch (cause)
        {
            case DeathCause.Starvation:
                DeathsByStarvation++;
                how = "starved to death";
                if (++_starvedThisSeason == FamineDeaths)
                {
                    Headline("Famine", $"Famine: {FamineDeaths} Bramblekin have starved this {CurrentSeason.ToString().ToLowerInvariant()}",
                        kin.Position, true, GroupOf(kin));
                }
                break;
            case DeathCause.Sickness:
                DeathsBySickness++;
                how = "died of a sickness";
                break;
            case DeathCause.OldAge:
                DeathsByOldAge++;
                how = $"died of old age at {kin.AgeInYears:0.0} years" +
                      (kin.Children > 0 ? $", leaving {kin.Children} {(kin.Children == 1 ? "child" : "children")}" : "");
                break;
            case DeathCause.Kin:
                DeathsByKin++;
                if (killer is Bramblekin slayer)
                {
                    AddGrievance(kin.GroupId, slayer.GroupId, KillingGrievance);
                    AddWarScore(slayer.GroupId, kin.GroupId, KillWarScore);
                }
                how = killer is Bramblekin attacker ? $"was killed by {attacker.Name}" : "was killed by another Bramblekin";
                break;
            default:
                DeathsByPredator++;
                how = killer switch
                {
                    WolfSpider => "was caught by the Wolf Spider",
                    Hornet => "was stung to death by hornets",
                    Ant => "was bitten to death by ants",
                    _ => "was killed by a predator",
                };
                break;
        }
        CloseLife(kin, how);
        Game.AddEventLog($"[DEATH] {kin.Name} {how}");
        if (GroupOf(kin) is { } clan && clan.Leader == kin)
            Chronicle($"Leader {kin.Name} {how}", clan);
        else if (cause == DeathCause.OldAge && kin.Children >= 5)
            Chronicle($"{kin.Name} {how}", GroupOf(kin));
    }

    /// <summary>
    /// Damages the Wolf Spider and, if that brings its Health to 0, slays
    /// it: a splat, a scatter of Food where it fell (the prize for bringing
    /// it down), and the respawn timer starts.
    /// </summary>
    public void DamageSpider(int amount, Bramblekin attacker)
    {
        if (Spider is not { IsDead: false } spider)
            return;

        spider.TakeDamage(amount);
        if (spider.Health > 0)
            return;

        spider.MarkDead();
        _splats.Add((spider.Position, SplatDuration));
        ScatterFoodAround(spider.Position, SpiderCarcassFood, 0.6f, FoodShardKind.Meat);
        CreditMeat(attacker, SpiderCarcassFood);
        attacker.AddReputation(1f);
        attacker.NoteSpiderKill();
        Spider = null;
        SpiderRespawnTimer = SpiderRespawnDelay;
        SpidersKilled++;

        KinGroup? group = GroupOf(attacker);
        Game.AddEventLog(group is null
            ? $"[HUNT] {attacker.Name} slew the Wolf Spider alone!"
            : $"[HUNT] {group.CapitalTitle} brought down the Wolf Spider (final blow by {attacker.Name})");
        if (group is not null)
        {
            // Only the milestones make the chronicle: a clan's first spider, then every fifth.
            int slain = ++group.SpidersSlain;
            if (slain == 1 || slain % 5 == 0)
            {
                Chronicle($"{group.CapitalTitle} brought down its {(slain == 1 ? "first" : Ordinal(slain))} Wolf Spider (final blow by {attacker.Name})", group);
            }
        }
    }

    /// <summary>"5th", "21st", "12th"…</summary>
    private static string Ordinal(int n) =>
        n + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    /// <summary>A Hornet swatted out of the air. Removal from <see cref="Hornets"/> is deferred to the end of the frame.</summary>
    public void KillHornet(Hornet hornet)
    {
        if (hornet.IsDead)
            return;

        hornet.MarkDead();
        _pendingHornetRemovals.Add(hornet);
        HornetsKilled++;
    }

    /// <summary>A hunted Grub: drops a bit of Food, plus some of whatever it had eaten. Removal from <see cref="Grubs"/> is deferred to the end of the frame.</summary>
    public void KillGrub(Grub grub, Bramblekin killer)
    {
        if (grub.IsDead)
            return;

        grub.MarkDead();
        _pendingGrubRemovals.Add(grub);
        GrubsKilled++;
        int meat = 1 + Math.Min(grub.FoodEaten, Grub.MaxCarcassFood - 1);
        ScatterFoodAround(grub.Position, meat, 0.3f, FoodShardKind.Meat);
        CreditMeat(killer, meat);
    }
}
