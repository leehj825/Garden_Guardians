using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    public const int MaxBeetlesOnMap = 2;

    /// <summary>Seconds between checks that top the Stag Beetle population back up toward <see cref="MaxBeetlesOnMap"/>.</summary>
    public const float BeetleSpawnInterval = 40f;

    private readonly List<StagBeetle> _pendingBeetleSpawns = new();
    private readonly List<StagBeetle> _pendingBeetleRemovals = new();
    private float _beetleSpawnTimer = BeetleSpawnInterval / 2f;

    /// <summary>Meat brought down by hunters, credited to the social status of whoever landed the killing blow.</summary>
    private readonly int[] _meatHuntedByStatus = new int[SurvivalStatusCount];

    public List<StagBeetle> Beetles { get; } = new();

    public int BeetlesKilled { get; private set; }

    /// <summary>Survival trend: pieces of meat hunted per kin-hour lived in <paramref name="status"/>.</summary>
    public double MeatHuntedPerKinHour(SurvivalStatus status)
    {
        double hours = _exposureSeconds[(int)status] / 3600.0;
        return hours > 0 ? _meatHuntedByStatus[(int)status] / hours : double.NaN;
    }

    private void CreditMeat(Bramblekin? hunter, int pieces)
    {
        if (hunter is not null)
            _meatHuntedByStatus[(int)hunter.Status] += pieces;
    }

    /// <summary>The nearest living Stag Beetle within <paramref name="radius"/> of <paramref name="from"/>, if any.</summary>
    public StagBeetle? NearestLiveBeetle(Vector3 from, float radius)
    {
        StagBeetle? best = null;
        float bestDistanceSquared = radius * radius;
        foreach (StagBeetle beetle in Beetles)
        {
            if (beetle.IsDead)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, beetle.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = beetle;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>A Stag Beetle brought down: a big scatter of meat where it fell. Removal is deferred to the end of the frame.</summary>
    public void KillBeetle(StagBeetle beetle, Bramblekin killer)
    {
        if (beetle.IsDead)
            return;

        beetle.MarkDead();
        _pendingBeetleRemovals.Add(beetle);
        BeetlesKilled++;
        ScatterFoodAround(beetle.Position, StagBeetle.MeatYield, 0.7f, FoodShardKind.Meat);
        CreditMeat(killer, StagBeetle.MeatYield);

        int hunters = Colony.Count(k => !k.IsDead && ReferenceEquals(k.CombatTarget, beetle));
        KinGroup? group = GroupOf(killer);
        Game.AddEventLog(hunters > 1 && group is not null
            ? $"[HUNT] Group {group.ShortId} brought down a Stag Beetle ({hunters} hunters)"
            : $"[HUNT] #{killer.ID} brought down a Stag Beetle{(hunters > 1 ? $" with {hunters - 1} others" : " alone!")}");
    }

    private void UpdateBeetleSpawn(float deltaTime)
    {
        _beetleSpawnTimer -= deltaTime;
        if (_beetleSpawnTimer > 0f)
            return;
        _beetleSpawnTimer = BeetleSpawnInterval;

        int living = Beetles.Count(b => !b.IsDead) + _pendingBeetleSpawns.Count;
        if (living < MaxBeetlesOnMap)
            _pendingBeetleSpawns.Add(new StagBeetle(RandomEdgeSpot(StagBeetle.BodyRadius + 0.2f, StagBeetle.EdgeMargin + 1f), Rng));
    }

    private void CommitBeetleChanges()
    {
        foreach (StagBeetle beetle in _pendingBeetleRemovals)
            Beetles.Remove(beetle);
        _pendingBeetleRemovals.Clear();
        Beetles.AddRange(_pendingBeetleSpawns);
        _pendingBeetleSpawns.Clear();
    }
}
