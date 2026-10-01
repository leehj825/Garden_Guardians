using System.Numerics;

namespace GardenGuardians;

/// <summary>One invasion: a swarm of small spiders sent against a village (a light one, 3 to 5) or a kingdom (a hard one, 10 or more).</summary>
public sealed class Invasion
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>"the village of Mossbridge" / "the kingdom of Thornreach".</summary>
    public string TargetName { get; init; } = "";

    public List<Guid> VillageIds { get; } = new();
    public Guid? KingdomId { get; init; }
    public float StartedAt { get; init; }
    public int Size { get; init; }
    public int Slain { get; set; }
    public bool Withdrawing { get; set; }
    public bool IsHard => KingdomId is not null;
}

public sealed partial class World
{
    // --- Invasions: swarms of small spiders against a village or a kingdom --------------------------------------------------
    // From a while into the game, now and then a swarm of small spiders (a knee-high cousin of the Wolf Spider) comes in from the edge
    // of a village's ground and marches on its middle, biting any Bramblekin it finds outside. A light invasion (3 to 5) is aimed at a
    // village; a hard one (10 or more) at a kingdom, split among its villages with the most at the capital. Soldiers and the village's
    // clans fight them off (a kingdom's pledged soldiers march to a sister village's alarm); one that is not beaten in a few minutes
    // gives up and withdraws. Invasions are not saved: a loaded garden starts with its spiders gone.

    /// <summary>A testing aid: with invasions off, none are ever sent (StartInvasion still works).</summary>
    public static bool InvasionsEnabled { get; set; } = true;

    /// <summary>The first invasion comes no sooner than this (s) into the game, and only once a village stands.</summary>
    private const float FirstInvasionAt = 420f;

    /// <summary>Between invasions: this long (s) at least, and up to this much more.</summary>
    private const float InvasionGapMin = 300f, InvasionGapRandom = 240f;

    /// <summary>With a kingdom standing, this share of invasions is a hard one against it.</summary>
    private const double HardInvasionShare = 0.4;

    public const int LightMin = 3, LightMax = 5, HardMin = 10, HardRandom = 5;

    /// <summary>A spider is rewarded with this much meat where it falls.</summary>
    private const int InvaderCarcassFood = 1;

    /// <summary>An invasion that has not been beaten after this long (s) withdraws.</summary>
    private const float InvasionLingerSeconds = 300f;

    /// <summary>Spiders appear this far (m) from their village's middle, if there is room.</summary>
    private const float InvaderSpawnDistance = 38f;

    private float _invasionTimer = FirstInvasionAt;

    public List<Invasion> Invasions { get; } = new();
    public List<InvaderSpider> Invaders { get; } = new();
    private readonly List<InvaderSpider> _pendingInvaderRemovals = new();

    public int InvasionsStarted { get; private set; }
    public int InvasionsRepelled { get; private set; }
    public int InvasionsWithdrawn { get; private set; }
    public int InvaderSpidersSlain { get; private set; }

    /// <summary>The invasion with living spiders that targets <paramref name="village"/>, if any.</summary>
    public Invasion? InvasionAt(Village village) =>
        Invasions.FirstOrDefault(i => i.VillageIds.Contains(village.Id) && Invaders.Any(s => !s.IsDead && s.InvasionId == i.Id));

    public int InvadersAt(Village village) => Invaders.Count(s => !s.IsDead && s.VillageId == village.Id);

    private void UpdateInvasions(float deltaTime)
    {
        for (int i = Invaders.Count - 1; i >= 0; i--)
            Invaders[i].Update(deltaTime, this);

        for (int i = Invasions.Count - 1; i >= 0; i--)
        {
            Invasion invasion = Invasions[i];
            int alive = Invaders.Count(s => !s.IsDead && s.InvasionId == invasion.Id);
            if (alive == 0)
            {
                Invasions.RemoveAt(i);
                if (invasion.Withdrawing)
                {
                    InvasionsWithdrawn++;
                    Game.AddEventLog($"[INVASION] The spiders withdraw from {invasion.TargetName}");
                }
                else
                {
                    InvasionsRepelled++;
                    Game.AddEventLog($"[INVASION] {invasion.TargetName} beat the invasion: all {invasion.Size} spiders slain");
                    Chronicle($"{Capitalize(invasion.TargetName)} beat back an invasion of {invasion.Size} spiders", VillageClans(invasion).ToArray());
                }
            }
            else if (!invasion.Withdrawing && ElapsedSeconds - invasion.StartedAt > InvasionLingerSeconds)
            {
                invasion.Withdrawing = true;
                foreach (InvaderSpider spider in Invaders.Where(s => s.InvasionId == invasion.Id))
                {
                    spider.IsWithdrawing = true;
                    spider.ExitPoint = Grounded(spider.Position + Vector3.Normalize(new Vector3(spider.Position.X - spider.Goal.X, 0f, spider.Position.Z - spider.Goal.Z) + new Vector3(0.001f, 0f, 0f)) * 60f);
                }
                Game.AddEventLog($"[INVASION] The {alive} spiders left at {invasion.TargetName} give up and withdraw");
            }
        }

        if (!InvasionsEnabled || Villages.Count == 0 || ElapsedSeconds < FirstInvasionAt)
            return;
        _invasionTimer -= deltaTime;
        if (_invasionTimer > 0f)
            return;
        if (Invasions.Count > 0)
        {
            _invasionTimer = 60f; // One at a time.
            return;
        }
        _invasionTimer = InvasionGapMin + (float)Rng.NextDouble() * InvasionGapRandom;
        if (Realms.Count > 0 && Rng.NextDouble() < HardInvasionShare)
            StartKingdomInvasion(Realms[Rng.Next(Realms.Count)], HardMin + Rng.Next(HardRandom));
        else
            StartVillageInvasion(Villages[Rng.Next(Villages.Count)], LightMin + Rng.Next(LightMax - LightMin + 1));
    }

    private IEnumerable<KinGroup> VillageClans(Invasion invasion) =>
        Villages.Where(v => invasion.VillageIds.Contains(v.Id)).SelectMany(ClansOf);

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    /// <summary>A light invasion: <paramref name="count"/> small spiders against <paramref name="village"/>.</summary>
    public Invasion StartVillageInvasion(Village village, int count)
    {
        var invasion = new Invasion { TargetName = $"the village of {village.Name}", StartedAt = ElapsedSeconds, Size = count };
        invasion.VillageIds.Add(village.Id);
        Spawn(invasion, village, count);
        Invasions.Add(invasion);
        Announce(invasion, village.Centre, count);
        return invasion;
    }

    /// <summary>A hard invasion: <paramref name="count"/> spiders against <paramref name="kingdom"/>, most at its capital, the rest at its other villages.</summary>
    public Invasion StartKingdomInvasion(Kingdom kingdom, int count)
    {
        List<Village> targets = VillagesOf(kingdom).ToList();
        Village capital = CapitalOf(kingdom) ?? targets[0];
        var invasion = new Invasion { TargetName = $"the kingdom of {kingdom.Name}", StartedAt = ElapsedSeconds, Size = count, KingdomId = kingdom.Id };
        int atCapital = targets.Count == 1 ? count : (int)MathF.Ceiling(count * 0.4f);
        int remaining = count - atCapital;
        Spawn(invasion, capital, atCapital);
        invasion.VillageIds.Add(capital.Id);
        List<Village> others = targets.Where(v => v != capital).ToList();
        for (int i = 0; i < others.Count; i++)
        {
            int share = remaining / (others.Count - i);
            remaining -= share;
            if (share > 0)
            {
                Spawn(invasion, others[i], share);
                invasion.VillageIds.Add(others[i].Id);
            }
        }
        Invasions.Add(invasion);
        Announce(invasion, capital.Centre, count);
        return invasion;
    }

    private void Spawn(Invasion invasion, Village village, int count)
    {
        Vector3 at = village.Centre;
        float start = (float)(Rng.NextDouble() * MathF.Tau);
        // Out as far as there is open ground, along one bearing or another — clear of other homes if it can be, less so if the map is crowded.
        bool found = false;
        foreach (float clear in new[] { 20f, 12f, 6f, 0f })
        {
            for (float r = InvaderSpawnDistance; r >= 18f && !found; r -= 2f)
            {
                for (int k = 0; k < 12 && !found; k++)
                {
                    float angle = start + k * MathF.Tau / 12f;
                    Vector3 p = Grounded(village.Centre + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * r);
                    if (Terrain.Contains(p, 3f) && !IsBlocked(p, InvaderSpider.BodyRadius + 0.3f) && !IsWater(p) && (clear <= 0f || !NearHomesOrKin(p, clear)))
                    {
                        at = p;
                        found = true;
                    }
                }
            }
            if (found)
                break;
        }
        for (int i = 0; i < count; i++)
        {
            Vector3 p = Grounded(at + new Vector3((float)(Rng.NextDouble() - 0.5) * 3f, 0f, (float)(Rng.NextDouble() - 0.5) * 3f));
            if (IsBlocked(p, InvaderSpider.BodyRadius) || !Terrain.Contains(p, 1f))
                p = at;
            Invaders.Add(new InvaderSpider(p, village.Centre, invasion.Id, village.Id, Rng));
        }
    }

    /// <summary>True if a standing home or a living Bramblekin is within <paramref name="distance"/> of <paramref name="point"/> (invaders do not appear in anyone's lap).</summary>
    private bool NearHomesOrKin(Vector3 point, float distance) =>
        Shelters.Any(h => h is { IsBuilt: true, IsCollapsed: false } && GroundMover.HorizontalDistance(h.Position, point) < distance) ||
        Colony.Any(k => !k.IsDead && GroundMover.HorizontalDistance(k.Position, point) < distance * 0.6f);

    private void Announce(Invasion invasion, Vector3 where, int count)
    {
        InvasionsStarted++;
        string size = invasion.IsHard ? "A great swarm" : "A swarm";
        Game.AddEventLog($"[INVASION] {size} of {count} small spiders marches on {invasion.TargetName}");
        Headline(invasion.IsHard ? "Invasion!" : "Spiders!", $"{size} of {count} small spiders marches on {invasion.TargetName}", where, true, VillageClans(invasion).ToArray());
    }

    /// <summary>A spider is struck dead by <paramref name="attacker"/>.</summary>
    public void KillInvader(InvaderSpider spider, Bramblekin attacker)
    {
        if (spider.IsDead)
            return;
        spider.MarkDead();
        _pendingInvaderRemovals.Add(spider);
        _splats.Add((spider.Position, SplatDuration));
        ScatterFoodAround(spider.Position, InvaderCarcassFood, 0.4f, FoodShardKind.Meat);
        attacker.AddReputation(0.3f);
        InvaderSpidersSlain++;
        if (Invasions.FirstOrDefault(i => i.Id == spider.InvasionId) is { } invasion)
            invasion.Slain++;
    }

    /// <summary>A withdrawing spider has got away: gone, not slain.</summary>
    public void RemoveInvader(InvaderSpider spider)
    {
        if (spider.IsDead)
            return;
        spider.MarkDead();
        _pendingInvaderRemovals.Add(spider);
    }

    private void CommitInvaderRemovals()
    {
        if (_pendingInvaderRemovals.Count == 0)
            return;
        foreach (InvaderSpider spider in _pendingInvaderRemovals)
            Invaders.Remove(spider);
        _pendingInvaderRemovals.Clear();
    }

    /// <summary>The nearest living invader within <paramref name="radius"/> of <paramref name="from"/>, if any.</summary>
    public InvaderSpider? NearestInvader(Vector3 from, float radius)
    {
        InvaderSpider? best = null;
        float bestDistanceSquared = radius * radius;
        foreach (InvaderSpider spider in Invaders)
        {
            if (spider.IsDead)
                continue;
            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, spider.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = spider;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }
}
