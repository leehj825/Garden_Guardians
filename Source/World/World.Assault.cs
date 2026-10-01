using System.Numerics;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Kingdom assaults on the ant hill (Garden_Guardians_Design.md, "The Ant Hill") -------------------------------------------------
    // A Kingdom with enough fit soldiers may order an assault. The party musters at the capital, marches to a staging point outside the
    // hill's no-go zone and fights the guards. If every guard stands down, the survivors go into the hill for the prize, and carry it home;
    // only when it reaches the capital does the hill's level go up, and the eggs are shared down the ranking of who fought best. The Kingdom
    // calls a retreat when half the party has died or run away; each soldier may also run away on its own.

    /// <summary>A testing aid: with assaults off, a Kingdom never orders one (<see cref="StartAssault"/> still works).</summary>
    public static bool AssaultsEnabled { get; set; } = true;

    /// <summary>At each look at a Kingdom that is ready, the crown orders an assault with these odds.</summary>
    private const double AssaultChance = 0.25;

    /// <summary>No new assault for this long (s) after one ends.</summary>
    private const float AssaultCooldown = SeasonLength;

    /// <summary>Only soldiers at this share of their Health or more, and no hungrier than this, go.</summary>
    private const float FitHealthFraction = 0.8f, FitHungerFraction = 0.6f;

    /// <summary>The party waits this long (s) for stragglers at each gathering, gives up on a fight that drags on this long, and on a prize that has not got home in this long.</summary>
    private const float MusterTimeout = 60f, MarchTimeout = 90f, FightTimeout = 240f, LootTimeout = 30f, ReturnTimeout = 180f, RetreatTimeout = 120f;

    /// <summary>The share of the party that must have arrived before the next phase begins.</summary>
    private const float GatheredShare = 0.8f;

    /// <summary>The staging point lies this far (m) outside the hill's zone.</summary>
    private const float StagingDistance = 8f;

    private float _assaultCooldownUntil;

    /// <summary>The assault under way, if any.</summary>
    public Assault? CurrentAssault { get; private set; }

    public int AssaultsLaunched { get; private set; }
    public int AssaultsWon { get; private set; }
    public int AssaultsCalledOff { get; private set; }
    public int AssaultsLost { get; private set; }
    public int EggsEaten { get; private set; }
    public int Deserters { get; private set; }
    public int HighestHillLevelBeaten { get; private set; }

    /// <summary>The soldiers of <paramref name="kingdom"/>'s villages who could march today.</summary>
    private List<Bramblekin> FitSoldiers(Kingdom kingdom)
    {
        var soldiers = new List<Bramblekin>();
        foreach (Village village in VillagesOf(kingdom))
        {
            foreach (KinGroup clan in ClansOf(village))
            {
                foreach (Bramblekin kin in clan.Members)
                {
                    bool fighter = kin.Job is KinJob.Guard or KinJob.Hunter || kin.VillageJob is KinJob.Guard or KinJob.Hunter;
                    if (!fighter || kin.IsDead || kin.IsYoung || kin.IsElder || kin.IsSick || kin.AssaultParty is not null)
                        continue;
                    if (kin.Health < Bramblekin.MaxHealth * FitHealthFraction || kin.Hunger > Bramblekin.MaxHunger * FitHungerFraction)
                        continue;
                    soldiers.Add(kin);
                }
            }
        }
        return soldiers;
    }

    /// <summary>The soldiers an assault on a hill of this level needs: half as many as it has guards, rounded up.</summary>
    public static int SoldiersNeeded(int hillLevel) => (Anthill.GuardsPerLevel * hillLevel + 1) / 2;

    /// <summary>At each look at a kingdom (see <see cref="RunRealm"/>): the crown may order an assault on the hill.</summary>
    private void ConsiderAssault(Kingdom kingdom, Village capital)
    {
        if (!AssaultsEnabled || Anthill is not { } hill || CurrentAssault is not null || CurrentSeason == Season.Winter || IsNight ||
            ElapsedSeconds < _assaultCooldownUntil || KingOf(kingdom) is null)
            return;
        int needed = SoldiersNeeded(hill.Level);
        List<Bramblekin> soldiers = FitSoldiers(kingdom);
        if (soldiers.Count < needed || Rng.NextDouble() >= AssaultChance)
            return;
        List<Bramblekin> party = soldiers.OrderByDescending(k => k.Strength + k.Personality.Courage).Take(needed).ToList();
        StartAssault(kingdom.Name, capital.Centre, party);
    }

    /// <summary>Sends <paramref name="party"/> against the hill (the Kingdom's order, or a test).</summary>
    public Assault? StartAssault(string name, Vector3 muster, IReadOnlyList<Bramblekin> party)
    {
        if (Anthill is not { } hill || CurrentAssault is not null || party.Count == 0)
            return null;
        Vector3 toMuster = Grounded(muster) - hill.Position;
        toMuster.Y = 0f;
        Vector3 direction = toMuster.LengthSquared() > 1e-4f ? Vector3.Normalize(toMuster) : hill.Facing;
        var assault = new Assault
        {
            Name = name,
            Level = hill.Level,
            Muster = Grounded(muster),
            Staging = Grounded(hill.Position + direction * (Anthill.ZoneRadius + StagingDistance)),
            StartedAt = ElapsedSeconds,
        };
        foreach (Bramblekin kin in party)
        {
            assault.Party.Add(kin);
            kin.AssaultParty = assault;
        }
        CurrentAssault = assault;
        AssaultsLaunched++;
        KinGroup?[] clans = party.Select(GroupOf).Where(c => c is not null).Distinct().ToArray();
        string text = $"The kingdom of {name} sends {party.Count} soldiers against the ant hill (level {hill.Level}, {hill.GuardCount} guards)";
        Game.AddEventLog($"[ANTS] {text}");
        Headline("The ant hill", text, assault.Muster, true, clans);
        return assault;
    }

    private void UpdateAssault(float deltaTime)
    {
        if (CurrentAssault is not { } a || Anthill is not { } hill)
            return;
        a.PhaseSeconds += deltaTime;
        int active = a.Party.Count(a.IsActive);
        int dead = a.Party.Count(k => k.IsDead);
        if (active == 0)
        {
            EndAssault(a, "lost");
            return;
        }

        switch (a.Phase)
        {
            case AssaultPhase.Mustering:
                if (Arrived(a, a.Muster, 8f) >= Math.Ceiling(active * GatheredShare) || a.PhaseSeconds >= MusterTimeout)
                    NextPhase(a, AssaultPhase.Marching);
                break;

            case AssaultPhase.Marching:
                if (Arrived(a, a.Staging, 8f) >= Math.Ceiling(active * GatheredShare) || a.PhaseSeconds >= MarchTimeout)
                    NextPhase(a, AssaultPhase.Fighting);
                break;

            case AssaultPhase.Fighting:
                if (HillGuards.All(g => g.IsDead || g.IsHidden) && AntZoneKin.Any(k => a.Party.Contains(k)))
                {
                    NextPhase(a, AssaultPhase.Looting);
                    string text = $"The guards of the ant hill are down: the soldiers of {a.Name} go in for the prize";
                    Game.AddEventLog($"[ANTS] {text}");
                    Headline("The ant hill", text, hill.Position, false);
                }
                else if (dead + a.Fled.Count >= (a.Size + 1) / 2)
                    CallRetreat(a, $"{dead} dead and {a.Fled.Count} fled of {a.Size}");
                else if (a.PhaseSeconds >= FightTimeout)
                    CallRetreat(a, "the fight drags on");
                break;

            case AssaultPhase.Looting:
                if (a.Carriers.Count >= active || a.PhaseSeconds >= LootTimeout)
                {
                    if (a.Carriers.Count == 0)
                        CallRetreat(a, "nobody reached the prize");
                    else
                        NextPhase(a, AssaultPhase.Returning);
                }
                break;

            case AssaultPhase.Returning:
                if (!a.Party.Any(k => a.Carriers.Contains(k.ID) && !k.IsDead))
                    EndAssault(a, "the prize was lost");
                else if (a.PhaseSeconds >= ReturnTimeout)
                    EndAssault(a, "the prize never got home");
                break;

            case AssaultPhase.Retreating:
                if (Arrived(a, a.Muster, 12f) >= Math.Ceiling(active * GatheredShare) || a.PhaseSeconds >= RetreatTimeout)
                    EndAssault(a, null);
                break;
        }
    }

    private static int Arrived(Assault a, Vector3 point, float within) =>
        a.Party.Count(k => a.IsActive(k) && GroundMover.HorizontalDistanceSquared(k.Position, point) <= within * within);

    private static void NextPhase(Assault a, AssaultPhase phase)
    {
        a.Phase = phase;
        a.PhaseSeconds = 0f;
    }

    private void CallRetreat(Assault a, string why)
    {
        NextPhase(a, AssaultPhase.Retreating);
        AssaultsCalledOff++;
        string text = $"The assault on the ant hill is called off ({why}; {HillGuards.Count(g => !g.IsDead)} of {Anthill?.GuardCount} guards still stand): the soldiers of {a.Name} fall back";
        Game.AddEventLog($"[ANTS] {text}");
        Headline("The ant hill", text, a.Staging, false);
        Chronicle(text);
    }

    /// <summary>Called when a carrier reaches the capital with the prize: the eggs are shared down the ranking and the hill grows.</summary>
    public void AssaultDelivered(Assault a)
    {
        if (a.Ended || Anthill is not { } hill)
            return;
        List<Bramblekin> ranking = a.Party.Where(a.IsActive)
            .OrderByDescending(k => a.Kills.GetValueOrDefault(k.ID)).ThenByDescending(k => k.Health).ToList();
        int eggs = a.Level;
        var winners = new List<string>();
        for (int i = 0; i < eggs && ranking.Count > 0; i++)
        {
            Bramblekin kin = ranking[i % ranking.Count];
            kin.EatEgg(this);
            EggsEaten++;
            if (!winners.Contains(kin.Name))
                winners.Add(kin.Name);
        }
        AssaultsWon++;
        HighestHillLevelBeaten = Math.Max(HighestHillLevelBeaten, a.Level);
        hill.Level = a.Level + 1;
        string text = $"The soldiers of {a.Name} ({a.Party.Count(a.IsActive)} of {a.Size} left) bring home {eggs} {(eggs == 1 ? "egg" : "eggs")} from the ant hill: {string.Join(", ", winners)} {(winners.Count == 1 ? "eats" : "eat")} them. The hill is now level {hill.Level}";
        Game.AddEventLog($"[ANTS] {text}");
        Headline("The ant hill", text, a.Muster, true);
        Chronicle(text);
        EndAssault(a, null);
    }

    private void EndAssault(Assault a, string? failure)
    {
        if (a.Ended)
            return;
        a.Ended = true;
        foreach (Bramblekin kin in a.Party)
        {
            if (ReferenceEquals(kin.AssaultParty, a))
                kin.AssaultParty = null;
        }
        if (failure is not null)
        {
            AssaultsLost++;
            string text = failure == "lost" ? $"The assault of {a.Name} on the ant hill was wiped out" : $"The assault of {a.Name} on the ant hill came to nothing: {failure}";
            Game.AddEventLog($"[ANTS] {text}");
            Chronicle(text);
        }
        CurrentAssault = null;
        _assaultCooldownUntil = ElapsedSeconds + AssaultCooldown;
    }

    /// <summary>
    /// A testing aid for tuning (headless <c>--assault N</c>): sets the hill to <paramref name="level"/> with its full guard and sends the
    /// strongest grown Bramblekin there are, as many as the level needs, against it from where they stand.
    /// </summary>
    public Assault? StartTestAssault(int level)
    {
        if (Anthill is not { } hill)
            return null;
        hill.Level = Math.Max(1, level);
        HillGuards.Clear();
        for (int i = 0; i < hill.GuardCount; i++)
            HillGuards.Add(new HillGuard(hill.Mouth, Rng, hidden: false));
        List<Bramblekin> party = Colony.Where(k => !k.IsDead && !k.IsYoung && !k.IsElder).OrderByDescending(k => k.Strength).Take(SoldiersNeeded(hill.Level)).ToList();
        if (party.Count == 0)
            return null;
        foreach (Bramblekin kin in party)
            kin.MakeTestSoldier();
        return StartAssault("Test", party[0].Position, party);
    }

    /// <summary>A soldier ran away on its own (see <see cref="Bramblekin.UpdateAssault"/>).</summary>
    public void NoteDeserter(Assault a, Bramblekin kin)
    {
        if (a.Fled.Add(kin.ID))
            Deserters++;
    }
}
