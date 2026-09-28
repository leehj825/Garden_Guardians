using System.Numerics;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Wild food beyond the berries ----------------------------------------------------------

    /// <summary>Watercress springs up on the shore every this many seconds (at its season's pace)…</summary>
    private const float CressSpawnInterval = 7f;

    /// <summary>…until this many sprigs (at its season's pace) lie there.</summary>
    private const int MaxWildCress = 6;

    /// <summary>A mushroom springs up in the shade every this many seconds (at its season's pace, twice as fast after rain)…</summary>
    private const float MushroomSpawnInterval = 9f;

    private const int MaxWildMushrooms = 6;

    /// <summary>Rain brings the mushrooms up for this long after a storm.</summary>
    private const float AfterRainSeconds = 60f;

    /// <summary>A seed head sheds on the open lawn every this many seconds (at its season's pace)…</summary>
    private const float SeedSpawnInterval = 8f;

    private const int MaxWildSeeds = 8;

    /// <summary>The shore: this close to the pond…</summary>
    private const float ShoreNear = 0.8f;

    /// <summary>…but no further than this.</summary>
    private const float ShoreFar = 1.8f;

    /// <summary>Spots on the pond's shore, a meter or so from the water: where watercress grows, cress beds go, and fishers stand.</summary>
    public static readonly Vector3[] ShoreSpots = FindShoreSpots();

    private float _cressTimer, _mushroomTimer, _seedTimer, _sinceRain = AfterRainSeconds;

    private static Vector3[] FindShoreSpots()
    {
        var spots = new List<Vector3>();
        for (float x = -48f; x <= 48f; x += 1f)
        {
            for (float z = -48f; z <= 48f; z += 1f)
            {
                var point = new Vector3(x, 0f, z);
                if (!IsWaterNear(point, ShoreNear) && IsWaterWithin(point, ShoreFar))
                    spots.Add(Grounded(point));
            }
        }
        return spots.ToArray();
    }

    /// <summary>True if the pond lies within <paramref name="reach"/> of <paramref name="point"/> (looking out in twelve directions, near and far).</summary>
    public static bool IsWaterWithin(Vector3 point, float reach)
    {
        if (IsWater(point))
            return true;
        for (int i = 0; i < 12; i++)
        {
            float angle = i * MathF.Tau / 12f;
            var direction = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            if (IsWater(point + direction * reach) || IsWater(point + direction * reach * 0.5f))
                return true;
        }
        return false;
    }

    /// <summary>The shore spot nearest <paramref name="from"/>, if one lies within <paramref name="reach"/>.</summary>
    public static Vector3? NearestShoreSpot(Vector3 from, float reach)
    {
        Vector3? best = null;
        float bestDistance = reach * reach;
        foreach (Vector3 spot in ShoreSpots)
        {
            float distance = GroundMover.HorizontalDistanceSquared(from, spot);
            if (distance <= bestDistance)
            {
                best = spot;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>A random shore spot within <paramref name="reach"/> of <paramref name="from"/>, if there's any.</summary>
    public Vector3? RandomShoreSpot(Vector3 from, float reach)
    {
        int count = 0;
        foreach (Vector3 spot in ShoreSpots)
        {
            if (GroundMover.HorizontalDistanceSquared(from, spot) <= reach * reach)
                count++;
        }
        if (count == 0)
            return null;
        int pick = Rng.Next(count);
        foreach (Vector3 spot in ShoreSpots)
        {
            if (GroundMover.HorizontalDistanceSquared(from, spot) <= reach * reach && pick-- == 0)
                return spot;
        }
        return null;
    }

    /// <summary>Minnows and tadpoles landed from the shore.</summary>
    public int FishCaught { get; private set; }

    /// <summary>A fisher lands a catch: a Fish, already in hand.</summary>
    public FoodShard? CatchFish(Bramblekin fisher)
    {
        if (ActivateFood(fisher.Position, FoodShardKind.Fish) is not { } fish)
            return null;
        PickUpFood(fish);
        FishCaught++;
        if (FishCaught == 1)
            Game.AddEventLog($"[FISHING] {fisher.Name} landed the first catch from the pond");
        return fish;
    }

    /// <summary>How much <paramref name="kind"/> of loose food lies about (or is on its way).</summary>
    private int LooseFood(FoodShardKind kind)
    {
        int count = 0;
        foreach (FoodShard food in FoodShards)
        {
            if (food is { IsActive: true, IsCarried: false } && food.Kind == kind)
                count++;
        }
        foreach (var (_, pendingKind) in _pendingFoodSpawns)
        {
            if (pendingKind == kind)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Wild food besides the berries: watercress on the shore (best in
    /// spring, never minding a drought), mushrooms in the oak's shade and
    /// by the rocks (best in autumn, and after rain), and grass seed shed on
    /// the open lawn (high summer into autumn).
    /// </summary>
    private void UpdateWildFood(float deltaTime)
    {
        _sinceRain = IsStorming ? 0f : _sinceRain + deltaTime;

        float cressPace = Crop.SeasonPace(CropKind.Cress, CurrentSeason);
        if (Tick(ref _cressTimer, CressSpawnInterval, cressPace, deltaTime) && LooseFood(FoodShardKind.Cress) < MaxWildCress * cressPace &&
            ShoreSpots.Length > 0)
        {
            Vector3 spot = ShoreSpots[Rng.Next(ShoreSpots.Length)];
            if (!IsBlocked(spot, FoodShard.Radius + 0.1f))
                _pendingFoodSpawns.Add((spot, FoodShardKind.Cress));
        }

        float mushroomPace = Crop.SeasonPace(CropKind.Mushroom, CurrentSeason) * WeatherFoodFactor * (_sinceRain < AfterRainSeconds ? 2f : 1f);
        if (Tick(ref _mushroomTimer, MushroomSpawnInterval, mushroomPace, deltaTime) && LooseFood(FoodShardKind.Mushroom) < MaxWildMushrooms * mushroomPace &&
            ShadySpot() is { } shade)
            _pendingFoodSpawns.Add((shade, FoodShardKind.Mushroom));

        float seedPace = Crop.SeasonPace(CropKind.Grain, CurrentSeason) * WeatherFoodFactor;
        if (Tick(ref _seedTimer, SeedSpawnInterval, seedPace, deltaTime) && LooseFood(FoodShardKind.Seed) < MaxWildSeeds * seedPace)
        {
            // A seed head shatters: two or three grains close together.
            Vector3 head = RandomFreePoint(FoodShard.Radius + 0.5f, edgeMargin: 2f);
            if (GroundMover.HorizontalDistance(head, OakCenter) > OakRadius + 22f)
                ScatterFoodAround(head, 2 + Rng.Next(2), 0.35f, FoodShardKind.Seed);
        }
    }

    /// <summary>Counts <paramref name="timer"/> down at <paramref name="pace"/>; true (and rewound to <paramref name="interval"/>) when it runs out. Never at a pace of 0.</summary>
    private static bool Tick(ref float timer, float interval, float pace, float deltaTime)
    {
        if (pace <= 0f)
            return false;
        timer -= deltaTime * pace;
        if (timer > 0f)
            return false;
        timer = interval;
        return true;
    }

    /// <summary>Somewhere shady for a mushroom: in the oak's shade (most often), else at the foot of a rock.</summary>
    private Vector3? ShadySpot()
    {
        Vector3 spot;
        if (Rng.NextDouble() < 0.6)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float distance = OakRadius + 2f + (float)Rng.NextDouble() * 16f;
            spot = OakCenter + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance;
        }
        else
        {
            GardenProp[] rocks = GardenProps.Where(p => p.Kind == GardenPropKind.Pebble).ToArray();
            if (rocks.Length == 0)
                return null;
            GardenProp rock = rocks[Rng.Next(rocks.Length)];
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            spot = rock.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (rock.FootprintRadius + 0.4f + (float)Rng.NextDouble() * 0.8f);
        }
        spot = Grounded(spot);
        return Terrain.Contains(spot, 1f) && !IsBlocked(spot, FoodShard.Radius + 0.1f) ? spot : null;
    }
}
