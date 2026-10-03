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

    /// <summary>The spiders still to come, wave by wave (the sizes), and when the next wave sets out.</summary>
    public Queue<int> WavesToCome { get; } = new();
    public float NextWaveAt { get; set; }
    public int WavesTotal { get; set; } = 1;
    public int WaveNumber { get; set; }

    /// <summary>When the latest wave set out (the clock for giving up runs from it).</summary>
    public float LastWaveAt { get; set; }

    /// <summary>The villages it is aimed at, the capital first.</summary>
    public List<Guid> Targets { get; } = new();

    /// <summary>The bearing (radians, from a village's middle) each target's spiders come in along, once chosen: out at the edge of the map.</summary>
    public Dictionary<Guid, float> Bearings { get; } = new();
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

    public const int LightMin = 3, LightMax = 12, HardMin = 10, HardMax = 30;

    /// <summary>A village's strength for an invasion's sake: each grown soldier counts one, every other grown kin a quarter.</summary>
    public float DefenceOf(Village village)
    {
        float strength = 0f;
        foreach (Bramblekin kin in ClansOf(village).SelectMany(c => c.Members))
        {
            if (kin.IsDead || kin.IsYoung)
                continue;
            strength += kin.Job == KinJob.Swordsman || kin.VillageJob == KinJob.Swordsman ? 1f : 0.25f;
        }
        return strength;
    }

    /// <summary>A light invasion's size: 3 to 5 against a small village with few soldiers, up to 12 against a large one, a spider for every four points of its defence.</summary>
    public int LightInvasionSize(Village village) =>
        Math.Clamp(LightMin + (int)MathF.Round(DefenceOf(village) / 4f) + Rng.Next(-1, 2), LightMin, LightMax);

    /// <summary>A hard invasion's size: 10 or more, and one more for every two points of its villages' defence together (at most 30).</summary>
    public int HardInvasionSize(Kingdom kingdom) =>
        Math.Clamp(HardMin + (int)MathF.Round(VillagesOf(kingdom).Sum(DefenceOf) / 2f) + Rng.Next(-1, 2), HardMin, HardMax);

    /// <summary>A spider is rewarded with this much meat where it falls.</summary>
    private const int InvaderCarcassFood = 1;

    /// <summary>An invasion that has not been beaten after this long (s) withdraws.</summary>
    private const float InvasionLingerSeconds = 300f;

    /// <summary>Spiders appear this far (m) from their village's middle, if there is room.</summary>
    private const float InvaderSpawnDistance = 38f;

    /// <summary>A hard invasion comes in this many waves; a light one in two once it is big enough (<see cref="TwoWaveSize"/>), else one.</summary>
    private const int HardWaves = 3, TwoWaveSize = 6;

    /// <summary>The next wave sets out this long (s) after the last, or a few seconds after the last was wiped out, whichever is first.</summary>
    private const float WaveGapSeconds = 45f, WaveAfterClearSeconds = 8f;

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
            if (!invasion.Withdrawing && invasion.WavesToCome.Count > 0)
            {
                if (alive == 0)
                    invasion.NextWaveAt = MathF.Min(invasion.NextWaveAt, ElapsedSeconds + WaveAfterClearSeconds);
                if (ElapsedSeconds >= invasion.NextWaveAt)
                {
                    SendWave(invasion);
                    continue;
                }
            }
            if (alive == 0 && invasion.WavesToCome.Count == 0 || alive == 0 && invasion.Withdrawing)
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
            else if (!invasion.Withdrawing && invasion.WavesToCome.Count == 0 && ElapsedSeconds - invasion.LastWaveAt > InvasionLingerSeconds)
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
            StartKingdomInvasion(PickKingdom(), -1);
        else
            StartVillageInvasion(Villages[Rng.Next(Villages.Count)], -1);
    }

    private Kingdom PickKingdom() => Realms[Rng.Next(Realms.Count)];

    private IEnumerable<KinGroup> VillageClans(Invasion invasion) =>
        Villages.Where(v => invasion.VillageIds.Contains(v.Id)).SelectMany(ClansOf);

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    /// <summary>A light invasion: <paramref name="count"/> small spiders against <paramref name="village"/>.</summary>
    public Invasion StartVillageInvasion(Village village, int count = -1)
    {
        if (count < 0)
            count = LightInvasionSize(village);
        var invasion = new Invasion { TargetName = $"the village of {village.Name}", StartedAt = ElapsedSeconds, Size = count };
        invasion.VillageIds.Add(village.Id);
        invasion.Targets.Add(village.Id);
        return Launch(invasion, count, count >= TwoWaveSize ? 2 : 1, village.Centre);
    }

    /// <summary>A hard invasion: <paramref name="count"/> spiders in waves against <paramref name="kingdom"/>, most at its capital, the rest at its other villages.</summary>
    public Invasion StartKingdomInvasion(Kingdom kingdom, int count = -1)
    {
        if (count < 0)
            count = HardInvasionSize(kingdom);
        List<Village> targets = VillagesOf(kingdom).ToList();
        Village capital = CapitalOf(kingdom) ?? targets[0];
        var invasion = new Invasion { TargetName = $"the kingdom of {kingdom.Name}", StartedAt = ElapsedSeconds, Size = count, KingdomId = kingdom.Id };
        invasion.Targets.Add(capital.Id);
        invasion.Targets.AddRange(targets.Where(v => v != capital).Select(v => v.Id));
        foreach (Guid id in invasion.Targets)
            invasion.VillageIds.Add(id);
        return Launch(invasion, count, HardWaves, capital.Centre);
    }

    /// <summary>Splits <paramref name="count"/> into waves, sends the first now and announces it.</summary>
    private Invasion Launch(Invasion invasion, int count, int waves, Vector3 where)
    {
        invasion.WavesTotal = waves;
        int left = count;
        for (int w = 0; w < waves; w++)
        {
            int share = left / (waves - w);
            left -= share;
            invasion.WavesToCome.Enqueue(Math.Max(1, share));
        }
        Invasions.Add(invasion);
        InvasionsStarted++;
        SendWave(invasion);
        // The banner and the camera's spotlight go to where the swarm comes in, so it can be watched marching on the village.
        if (Invaders.LastOrDefault(sp => sp.InvasionId == invasion.Id) is { } first)
            where = first.Position;
        string size = invasion.IsHard ? "A great swarm" : "A swarm";
        string inWaves = waves > 1 ? $", in {waves} waves" : "";
        if (invasion.Targets.Count > 0 && invasion.Bearings.TryGetValue(invasion.Targets[0], out float bearing))
            inWaves += $", from the {Compass(bearing)}";
        Game.AddEventLog($"[INVASION] {size} of {count} small spiders marches on {invasion.TargetName}{inWaves}");
        Headline(invasion.IsHard ? "Invasion!" : "Spiders!", $"{size} of {count} small spiders marches on {invasion.TargetName}{inWaves}", where, true, VillageClans(invasion).ToArray());
        return invasion;
    }

    /// <summary>Sets the next wave out: its spiders split over the target villages, the capital (first) taking two fifths.</summary>
    private void SendWave(Invasion invasion)
    {
        if (!invasion.WavesToCome.TryDequeue(out int count))
            return;
        invasion.WaveNumber++;
        invasion.LastWaveAt = ElapsedSeconds;
        invasion.NextWaveAt = ElapsedSeconds + WaveGapSeconds;
        List<Village> targets = invasion.Targets.Select(id => Villages.FirstOrDefault(v => v.Id == id)).Where(v => v is not null).Select(v => v!).ToList();
        if (targets.Count == 0)
        {
            invasion.WavesToCome.Clear();
            return;
        }
        int atFirst = targets.Count == 1 ? count : (int)MathF.Ceiling(count * 0.4f);
        int remaining = count - atFirst;
        Spawn(invasion, targets[0], atFirst);
        for (int i = 1; i < targets.Count; i++)
        {
            int share = remaining / (targets.Count - i);
            remaining -= share;
            if (share > 0)
                Spawn(invasion, targets[i], share);
        }
        if (invasion.WaveNumber > 1)
            Game.AddEventLog($"[INVASION] Wave {invasion.WaveNumber} of {invasion.WavesTotal}: {count} more spiders against {invasion.TargetName}");
    }

    private void Spawn(Invasion invasion, Village village, int count)
    {
        Vector3 at = EdgeSpawnPoint(invasion, village) ?? NearSpawnPoint(village);
        for (int i = 0; i < count; i++)
        {
            Vector3 p = Grounded(at + new Vector3((float)(Rng.NextDouble() - 0.5) * 3f, 0f, (float)(Rng.NextDouble() - 0.5) * 3f));
            if (IsBlocked(p, InvaderSpider.BodyRadius) || !Terrain.Contains(p, 1f))
                p = at;
            Invaders.Add(new InvaderSpider(p, village.Centre, invasion.Id, village.Id, Rng));
        }
    }

    /// <summary>
    /// Where the swarm comes in: out at the edge of the map, along a bearing from the village with a long run of open ground (the
    /// same bearing for every wave of an invasion). Null if no bearing offers one.
    /// </summary>
    private Vector3? EdgeSpawnPoint(Invasion invasion, Village village)
    {
        const int Bearings = 16;
        float EdgeRun(float angle)
        {
            var dir = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            float last = 0f;
            for (float r = 10f; r <= 250f; r += 4f)
            {
                if (!Terrain.Contains(village.Centre + dir * r, 2.5f))
                    break;
                last = r;
            }
            return last;
        }
        Vector3? PointAt(float angle)
        {
            var dir = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            for (float r = EdgeRun(angle); r >= 24f; r -= 2f)
            {
                Vector3 p = Grounded(village.Centre + dir * r);
                if (!IsBlocked(p, InvaderSpider.BodyRadius + 0.3f) && !IsWater(p))
                    return p;
            }
            return null;
        }

        if (!invasion.Bearings.TryGetValue(village.Id, out float chosen))
        {
            float jitter = (float)(Rng.NextDouble() * MathF.Tau / Bearings);
            var runs = new List<(float Angle, float Run)>();
            for (int k = 0; k < Bearings; k++)
            {
                float angle = jitter + k * MathF.Tau / Bearings;
                runs.Add((angle, EdgeRun(angle)));
            }
            float longest = runs.Max(r => r.Run);
            if (longest < 26f)
                return null;
            List<(float Angle, float Run)> far = runs.Where(r => r.Run >= longest * 0.8f).ToList();
            chosen = far[Rng.Next(far.Count)].Angle;
            invasion.Bearings[village.Id] = chosen;
        }
        return PointAt(chosen);
    }

    /// <summary>Open ground about <see cref="InvaderSpawnDistance"/> from the village, clear of homes where it can be: the fallback when no edge is reachable.</summary>
    private Vector3 NearSpawnPoint(Village village)
    {
        float start = (float)(Rng.NextDouble() * MathF.Tau);
        foreach (float clear in new[] { 20f, 12f, 6f, 0f })
        {
            for (float r = InvaderSpawnDistance; r >= 18f; r -= 2f)
            {
                for (int k = 0; k < 12; k++)
                {
                    float angle = start + k * MathF.Tau / 12f;
                    Vector3 p = Grounded(village.Centre + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * r);
                    if (Terrain.Contains(p, 3f) && !IsBlocked(p, InvaderSpider.BodyRadius + 0.3f) && !IsWater(p) && (clear <= 0f || !NearHomesOrKin(p, clear)))
                        return p;
                }
            }
        }
        return village.Centre;
    }

    /// <summary>"north-east" for a bearing (+X east, -Z north).</summary>
    private static string Compass(float angle)
    {
        string[] names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
        float degrees = MathF.Atan2(-MathF.Sin(angle), MathF.Cos(angle)) * 180f / MathF.PI;
        int index = (int)MathF.Round((degrees < 0 ? degrees + 360f : degrees) / 45f) % 8;
        return names[index];
    }

    /// <summary>True if a standing home or a living Bramblekin is within <paramref name="distance"/> of <paramref name="point"/> (invaders do not appear in anyone's lap).</summary>
    private bool NearHomesOrKin(Vector3 point, float distance) =>
        Shelters.Any(h => h is { IsBuilt: true, IsCollapsed: false } && GroundMover.HorizontalDistance(h.Position, point) < distance) ||
        Colony.Any(k => !k.IsDead && GroundMover.HorizontalDistance(k.Position, point) < distance * 0.6f);

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
