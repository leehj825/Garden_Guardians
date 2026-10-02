using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>Where a Builder takes a stone or branch: a well being dug, or one of its clan's homes (a footing, a palisade).</summary>
public readonly record struct BuildTarget(Shelter? Home, Well? Well)
{
    public Vector3 Position => Well?.Position ?? Home!.Position;

    /// <summary>How close it has to come to hand it over.</summary>
    public float Reach => Well is not null ? Well.Radius + 0.5f : Home!.Radius + 0.4f;
}

public sealed partial class World
{
    // --- Wells ----------------------------------------------------------------------------

    /// <summary>A clan whose main home is further than this (m) from water, walking, digs a well (once it knows how).</summary>
    private const float WellNeedReach = 10f;

    /// <summary>A well goes this far (m) from the main home's edge…</summary>
    private const float WellSiteMin = 1.2f;

    private const float WellSiteMax = 5f;

    /// <summary>…and takes this many stones, plus one for every <see cref="WellDepthPerStone"/> meters the ground there stands above the pond (the water lies deeper under a hill).</summary>
    private const int WellBaseStones = 2;

    private const float WellDepthPerStone = 3f;

    /// <summary>Crops this close (m) to a dug well are watered from it.</summary>
    private const float WellWateringReach = 6f;

    private static readonly Color WellTextColor = new(60, 110, 170, 255);

    /// <summary>Every well dug, or being dug.</summary>
    public List<Well> Wells { get; } = new();

    public int WellsDug { get; private set; }

    /// <summary>Drinks drawn from wells.</summary>
    public int WellDrinks { get; private set; }

    /// <summary>How many stones a well at <paramref name="at"/> takes.</summary>
    public static int WellStones(Vector3 at) =>
        WellBaseStones + (int)MathF.Ceiling(MathF.Max(0f, GetHeightAt(at.X, at.Z) - PondLevel) / WellDepthPerStone);

    /// <summary><paramref name="group"/>'s well, dug or being dug, if it has one.</summary>
    public Well? WellOf(KinGroup group)
    {
        foreach (Well well in Wells)
        {
            if (well.GroupId == group.Id)
                return well;
        }
        return null;
    }

    /// <summary>
    /// At each Leader decision: a clan that knows <see cref="Craft.Wells"/>,
    /// whose main home stands further than <see cref="WellNeedReach"/> from
    /// water and which has no well yet, marks one out beside that home — on
    /// the lowest ground to hand, where the water's nearest.
    /// </summary>
    private void UpdateWells(KinGroup group)
    {
        if (!Knows(group, Craft.Wells) || group.Home is not { IsBuilt: true, IsCollapsed: false } home || WellOf(group) is not null ||
            WaterMap.UsualDistanceToWater(home.Position.X, home.Position.Z) <= WellNeedReach)
            return;

        Vector3? best = null;
        float lowest = float.MaxValue;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float distance = home.Radius + WellSiteMin + (float)Rng.NextDouble() * (WellSiteMax - WellSiteMin);
            Vector3 spot = Grounded(home.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance);
            if (!Terrain.Contains(spot, 3f) || IsBlockedOrAntZone(spot, Well.Radius + 0.3f))
                continue;
            if (Shelters.Any(s => !s.IsCollapsed && GroundMover.HorizontalDistance(s.Position, spot) < HomeYard(s) + Well.DrawRadius))
                continue;
            if (OverlapsLayout(spot, Well.DrawRadius))
                continue;
            if (spot.Y < lowest)
            {
                best = spot;
                lowest = spot.Y;
            }
        }
        if (best is not { } site)
            return;

        var well = new Well(site, group.Id, WellStones(site));
        Wells.Add(well);
        RebuildObstacles();
        Game.AddEventLog($"[BUILD] {group.CapitalTitle} began digging a well ({well.StonesNeeded} stones deep)");
    }

    /// <summary><paramref name="group"/>'s well while it's still being dug.</summary>
    public Well? WellBeingDug(KinGroup group) => WellOf(group) is { IsDug: false } well ? well : null;

    /// <summary>Where a Builder of <paramref name="group"/> should take a <paramref name="kind"/> near <paramref name="near"/>: stones go to a well being dug first (water before walls), else to a home still needing them.</summary>
    public BuildTarget? MaterialTarget(KinGroup group, MaterialKind kind, Vector3 near)
    {
        if (kind == MaterialKind.Stone && WellBeingDug(group) is { } well)
            return new BuildTarget(null, well);
        return HomeNeeding(group, kind, near) is { } home ? new BuildTarget(home, null) : null;
    }

    /// <summary>A Builder brings <paramref name="material"/> to <paramref name="target"/>.</summary>
    public void DeliverMaterial(Bramblekin builder, BuildTarget target, Material material)
    {
        if (target.Well is not { } well)
        {
            DeliverMaterial(builder, target.Home!, material);
            return;
        }

        material.Deactivate();
        well.StonesLaid++;
        StonesLaid++;
        if (!well.IsDug)
            return;

        WellsDug++;
        foreach (Crop crop in Crops)
        {
            if (GroundMover.HorizontalDistance(crop.Position, well.Position) <= WellWateringReach)
                crop.IsWatered = true;
        }
        KinGroup? diggers = well.GroupId is { } owner && _groups.TryGetValue(owner, out KinGroup? owners) ? owners : GroupOf(builder);
        string whose = diggers?.CapitalTitle ?? builder.Name;
        QueueFloatingText(well.Position, "Water!", WellTextColor);
        Game.AddEventLog($"[BUILD] {whose} struck water - their well is dug");
        Headline("A well", $"{whose} struck water: a well at their door", well.Position, false, diggers);
    }

    /// <summary>The nearest dug well <paramref name="kin"/> may drink from — its own clan's, an ally's, or one whose clan is gone — within <paramref name="within"/>.</summary>
    public Well? NearestUsableWell(Bramblekin kin, float within)
    {
        Well? best = null;
        float bestDistance = within * within;
        foreach (Well well in Wells)
        {
            if (!well.IsDug || !(well.GroupId is null || well.GroupId == kin.GroupId || AreAllied(well.GroupId, kin.GroupId)))
                continue;
            float distance = GroundMover.HorizontalDistanceSquared(kin.Position, well.Position);
            if (distance < bestDistance)
            {
                best = well;
                bestDistance = distance;
            }
        }
        return best;
    }

    public void NoteWellDrink() => WellDrinks++;

    /// <summary>A dug well within <see cref="WellWateringReach"/> of <paramref name="spot"/>, if any.</summary>
    private bool IsWellNear(Vector3 spot) =>
        Wells.Any(w => w.IsDug && GroundMover.HorizontalDistance(w.Position, spot) <= WellWateringReach);

    /// <summary>Wells outlive their clans: once a clan is gone, its well is anyone's.</summary>
    private void UpdateWellOwners()
    {
        foreach (Well well in Wells)
        {
            if (well.GroupId is { } id && !_groups.ContainsKey(id))
                well.GroupId = null;
        }
        if (Wells.RemoveAll(w => !w.IsDug && w.GroupId is null) > 0) // A shaft nobody's digging any more is filled in.
            RebuildObstacles();
    }

    private void DrawWells(Camera3D camera)
    {
        foreach (Well well in Wells)
        {
            if (!IsVisible(well.Position, camera))
                continue;
            Color? flag = well.GroupId is { } id && _groups.TryGetValue(id, out KinGroup? clan) ? clan.Color : null;
            well.Draw(flag);
        }
    }
}
