using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>A spring or autumn storm is a downpour this often — and floods the low ground.</summary>
    private const double DownpourChance = 0.4;

    /// <summary>At its height a flood covers about this much of the garden, lowest ground first…</summary>
    private const float FloodedFraction = 0.15f;

    /// <summary>…and the pond, always, this much (see <see cref="PondLevel"/>).</summary>
    private const float PondFraction = 0.05f;

    /// <summary>After the rain stops, the water drains away over this long.</summary>
    private const float FloodRecedeSeconds = 60f;

    /// <summary>A flooded Tent is swept away with these odds (it always loses its store); a flooded House loses half its store.</summary>
    private const double TentSweptAwayChance = 0.5;

    private static readonly Color WaterColor = new(70, 120, 190, 150);

    /// <summary>The pond's surface, and how high a flood reaches (see <see cref="PondFraction"/>, <see cref="FloodedFraction"/>) — the terrain never changes, so worked out once.</summary>
    private static readonly (float Lowest, float Peak) FloodHeights = MeasureFloodHeights();

    /// <summary>
    /// The pond: water standing in the lowest hollows of the garden (world
    /// Y). Nothing is built, planted or spawned in it; walkers wade through
    /// at half pace; floods rise out of it.
    /// </summary>
    public static float PondLevel => FloodHeights.Lowest;

    /// <summary>True if <paramref name="point"/> is under the pond.</summary>
    public static bool IsWater(Vector3 point) => GetHeightAt(point.X, point.Z) < PondLevel;

    /// <summary>True if the pond reaches within <paramref name="clearance"/> of <paramref name="point"/> (checked at its centre and four points round it).</summary>
    public static bool IsWaterNear(Vector3 point, float clearance) =>
        IsWater(point) ||
        (clearance > 0.3f &&
         (IsWater(point + new Vector3(clearance, 0f, 0f)) || IsWater(point - new Vector3(clearance, 0f, 0f)) ||
          IsWater(point + new Vector3(0f, 0f, clearance)) || IsWater(point - new Vector3(0f, 0f, clearance))));

    private bool _downpour;

    /// <summary>How far the water has risen: 0 dry, 1 at its height.</summary>
    private float _flood;

    private float _floodSweepTimer;
    private readonly HashSet<int> _floodedHomes = new();

    public int Floods { get; private set; }
    public int HomesFlooded { get; private set; }
    public int FoodWashedAway { get; private set; }

    public bool IsFlooded => _flood > 0f;

    /// <summary>The water's surface (world Y): the pond's, sunk in a drought (see World.Drought) or risen in a flood.</summary>
    public float WaterLevel => WaterMap.SurfaceHeight + (FloodHeights.Peak - WaterMap.SurfaceHeight) * _flood;

    private static (float Lowest, float Peak) MeasureFloodHeights()
    {
        var heights = new List<float>();
        for (float x = -49f; x <= 49f; x += 2f)
        {
            for (float z = -49f; z <= 49f; z += 2f)
                heights.Add(GetHeightAt(x, z));
        }
        heights.Sort();
        return (heights[(int)(heights.Count * PondFraction)], heights[(int)(heights.Count * FloodedFraction)]);
    }

    /// <summary>A storm blowing up in spring or autumn may be a downpour, and flood the low ground.</summary>
    private void MaybeDownpour()
    {
        if (CurrentSeason is not (Season.Spring or Season.Autumn) || Rng.NextDouble() >= DownpourChance)
            return;
        _downpour = true;
        _floodedHomes.Clear();
        Floods++;
        Game.AddEventLog($"[WEATHER] Year {Year}: a downpour - the low ground is flooding");
        Headline("Flood", "A downpour floods the low ground - homes in the hollows are in danger", null, false);
    }

    /// <summary>The water rises through a downpour (peaking before it ends) and drains away after; while it's up it sweeps away whatever lies below it.</summary>
    private void UpdateFlood(float deltaTime)
    {
        if (_downpour && IsStorming)
        {
            _flood = MathF.Min(1f, _flood + deltaTime / (StormDuration * 0.75f));
        }
        else
        {
            _downpour = false;
            if (_flood <= 0f)
                return;
            _flood = MathF.Max(0f, _flood - deltaTime / FloodRecedeSeconds);
        }

        _floodSweepTimer -= deltaTime;
        if (_floodSweepTimer > 0f)
            return;
        _floodSweepTimer = 0.5f;
        SweepFlood();
    }

    /// <summary>
    /// Under water: loose food and twigs float away, ripe berries are lost
    /// from bushes, a flooded home's store is spoiled (all of a Tent's,
    /// half of a House's), and a Tent may be swept away entirely.
    /// </summary>
    private void SweepFlood()
    {
        float level = WaterLevel;
        foreach (FoodShard food in FoodShards)
        {
            if (food is { IsActive: true, IsCarried: false } && GetHeightAt(food.Position.X, food.Position.Z) < level)
            {
                food.Deactivate();
                FoodWashedAway++;
            }
        }
        foreach (Twig twig in Twigs)
        {
            if (twig is { IsActive: true, IsCarried: false } && GetHeightAt(twig.Position.X, twig.Position.Z) < level)
                twig.Deactivate();
        }
        foreach (Crop bush in Crops)
        {
            if (GetHeightAt(bush.Position.X, bush.Position.Z) < level)
                bush.LoseFruit();
        }

        for (int i = Shelters.Count - 1; i >= 0; i--)
        {
            Shelter shelter = Shelters[i];
            if (!shelter.IsBuilt || shelter.IsCollapsed || _floodedHomes.Contains(shelter.ID) ||
                GetHeightAt(shelter.Position.X, shelter.Position.Z) >= level - 0.1f)
                continue;

            _floodedHomes.Add(shelter.ID);
            if (shelter.HasFooting)
                continue; // Raised on its stone footing: the water swirls round, the store stays dry.
            HomesFlooded++;
            int spoiled = shelter.Tier == ShelterTier.Tent ? shelter.StoredFood : shelter.StoredFood / 2;
            for (int n = 0; n < spoiled && shelter.TryWithdraw(); n++)
                FoodWashedAway++;

            string whose = shelter.GroupId is { } id && _groups.TryGetValue(id, out KinGroup? clan) ? clan.Title : shelter.Owner?.Name ?? "an empty";
            if (shelter.Tier == ShelterTier.Tent && Rng.NextDouble() < TentSweptAwayChance)
            {
                shelter.Collapse();
                Shelters.RemoveAt(i);
                SheltersCollapsed++;
                QueueFloatingText(shelter.Position, "Swept away!", HostileTextColor);
                Game.AddEventLog($"[FLOOD] The flood swept away {whose}'s tent");
                if (shelter.GroupId is { } clanId && _groups.TryGetValue(clanId, out KinGroup? owners))
                    Chronicle($"The flood swept away a tent of {owners.Title}", owners);
            }
            else if (spoiled > 0)
            {
                QueueFloatingText(shelter.Position, $"Flooded -{spoiled}", HostileTextColor);
                Game.AddEventLog($"[FLOOD] The flood spoiled {spoiled} food in {whose}'s {shelter.Tier.ToString().ToLowerInvariant()}");
            }
        }
    }

    /// <summary>Size (m) of the squares the water surface is drawn in.</summary>
    private const float WaterCell = 0.5f;

    /// <summary>The squares of the garden the water covers at <see cref="_waterCellsLevel"/> — worked out again only when the level moves.</summary>
    private readonly List<Vector2> _waterCells = new();

    // A render cache, not garden state: saving it would tell a loaded garden its (unsaved, empty) squares were already worked out.
    [NonSerialized]
    private float _waterCellsLevel = float.NaN;

    /// <summary>
    /// The pond — or, in a flood, the risen water: a surface at
    /// <see cref="WaterLevel"/>, drawn only over the squares of the garden
    /// whose ground dips below it, so the hollows fill and nothing shows
    /// past the garden's edge.
    /// </summary>
    private void DrawWater()
    {
        DrawPondBed();
        float level = WaterLevel;
        if (MathF.Abs(level - _waterCellsLevel) > 0.02f || float.IsNaN(_waterCellsLevel))
        {
            _waterCellsLevel = level;
            _waterCells.Clear();
            float half = Terrain.Size / 2f;
            for (float x = -half; x < half; x += WaterCell)
            {
                for (float z = -half; z < half; z += WaterCell)
                {
                    if (GetHeightAt(x, z) < level || GetHeightAt(x + WaterCell, z) < level ||
                        GetHeightAt(x, z + WaterCell) < level || GetHeightAt(x + WaterCell, z + WaterCell) < level)
                        _waterCells.Add(new Vector2(x, z));
                }
            }
        }

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        foreach (Vector2 cell in _waterCells)
        {
            var a = new Vector3(cell.X, level, cell.Y);
            var b = new Vector3(cell.X + WaterCell, level, cell.Y);
            var c = new Vector3(cell.X + WaterCell, level, cell.Y + WaterCell);
            var d = new Vector3(cell.X, level, cell.Y + WaterCell);
            Raylib.DrawTriangle3D(a, d, c, WaterColor);
            Raylib.DrawTriangle3D(a, c, b, WaterColor);
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }
}
