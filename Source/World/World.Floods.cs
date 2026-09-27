using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>A spring or autumn storm is a downpour this often — and floods the low ground.</summary>
    private const double DownpourChance = 0.4;

    /// <summary>At its height a flood covers about this much of the garden, lowest ground first.</summary>
    private const float FloodedFraction = 0.15f;

    /// <summary>After the rain stops, the water drains away over this long.</summary>
    private const float FloodRecedeSeconds = 60f;

    /// <summary>A flooded Tent is swept away with these odds (it always loses its store); a flooded House loses half its store.</summary>
    private const double TentSweptAwayChance = 0.5;

    private static readonly Color WaterColor = new(70, 120, 190, 140);

    /// <summary>The lowest ground in the garden, and how high a flood reaches (see <see cref="FloodedFraction"/>) — the terrain never changes, so worked out once.</summary>
    private static readonly (float Lowest, float Peak) FloodHeights = MeasureFloodHeights();

    private bool _downpour;

    /// <summary>How far the water has risen: 0 dry, 1 at its height.</summary>
    private float _flood;

    private float _floodSweepTimer;
    private readonly HashSet<int> _floodedHomes = new();

    public int Floods { get; private set; }
    public int HomesFlooded { get; private set; }
    public int FoodWashedAway { get; private set; }

    public bool IsFlooded => _flood > 0f;

    /// <summary>The water's surface (world Y) while a flood is on.</summary>
    public float WaterLevel => FloodHeights.Lowest + (FloodHeights.Peak - FloodHeights.Lowest) * _flood;

    private static (float Lowest, float Peak) MeasureFloodHeights()
    {
        var heights = new List<float>();
        for (float x = -49f; x <= 49f; x += 2f)
        {
            for (float z = -49f; z <= 49f; z += 2f)
                heights.Add(GetHeightAt(x, z));
        }
        heights.Sort();
        return (heights[0], heights[(int)(heights.Count * FloodedFraction)]);
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
        foreach (BerryBush bush in Bushes)
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

    /// <summary>The flood: a sheet of water at <see cref="WaterLevel"/> — the hills stand out of it, the hollows fill.</summary>
    private void DrawFlood()
    {
        if (!IsFlooded)
            return;
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Raylib.DrawPlane(new Vector3(0f, WaterLevel, 0f), new Vector2(Terrain.Size, Terrain.Size), WaterColor);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }
}
