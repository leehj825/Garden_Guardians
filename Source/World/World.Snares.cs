using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>A clan that knows snares keeps this many round each House.</summary>
    private const int SnaresPerHouse = 2;

    /// <summary>A snare is set this far (m) from its House…</summary>
    private const float SnareMinReach = 3f;

    private const float SnareMaxReach = 7f;

    /// <summary>…and counts as that House's within this far.</summary>
    private const float SnareHouseReach = 8f;

    /// <summary>Snares stand at least this far (m) apart.</summary>
    private const float SnareSpacing = 1.5f;

    /// <summary>Every snare on the map, set or sprung.</summary>
    public List<Snare> Snares { get; } = new();

    /// <summary>Grubs caught in snares.</summary>
    public int SnareCatches { get; private set; }

    /// <summary>Sprung snares set again by a Gatherer.</summary>
    public int SnaresReset { get; private set; }

    /// <summary>At each Leader decision: a clan that knows snares sets any its Houses are short of.</summary>
    private void PlaceSnares(KinGroup group)
    {
        if (!Knows(group, Craft.Snares))
            return;
        foreach (Shelter home in GroupHomes(group))
        {
            if (home is not { IsBuilt: true, Tier: ShelterTier.House })
                continue;
            int around = Snares.Count(s => s.GroupId == group.Id &&
                                           GroundMover.HorizontalDistanceSquared(s.Position, home.Position) <= SnareHouseReach * SnareHouseReach);
            if (around < SnaresPerHouse && SnareSpot(home.Position) is { } spot)
                Snares.Add(new Snare(spot, group.Id));
        }
    }

    /// <summary>A clear, dry spot for a snare round <paramref name="home"/>: off the paths into homes, clear of crops and other snares.</summary>
    private Vector3? SnareSpot(Vector3 home)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float reach = SnareMinReach + (float)Rng.NextDouble() * (SnareMaxReach - SnareMinReach);
            var spot = home + new Vector3(MathF.Cos(angle) * reach, 0f, MathF.Sin(angle) * reach);
            if (!Terrain.Contains(spot, 1f) || WaterMap.IsWet(spot.X, spot.Z) || IsBlockedOrAntZone(spot, 0.3f))
                continue;
            if (Shelters.Any(s => !s.IsCollapsed && GroundMover.HorizontalDistance(spot, s.Position) < s.Radius + 1f) ||
                Crops.Any(c => GroundMover.HorizontalDistance(spot, c.Position) < Crop.Radius + 0.6f) ||
                Snares.Any(s => GroundMover.HorizontalDistance(spot, s.Position) < SnareSpacing))
                continue;
            return spot;
        }
        return null;
    }

    /// <summary>Snares whose clan is gone rot away.</summary>
    private void UpdateSnares()
    {
        Snares.RemoveAll(s => s.GroupId is not { } id || !_groups.ContainsKey(id));
    }

    /// <summary>The nearest set snare within <paramref name="radius"/> — a Grub smells the bait.</summary>
    public Snare? NearestSetSnare(Vector3 from, float radius)
    {
        Snare? best = null;
        float bestDistanceSquared = radius * radius;
        foreach (Snare snare in Snares)
        {
            if (!snare.IsSet)
                continue;
            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, snare.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = snare;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>A Grub crawls into <paramref name="snare"/>: it's caught and killed, its meat left on the spot.</summary>
    public void SpringSnare(Snare snare, Grub grub)
    {
        if (!snare.IsSet || grub.IsDead)
            return;
        snare.IsSet = false;
        SnareCatches++;
        KillGrub(grub, killer: null);
        QueueFloatingText(snare.Position, "Snared!", FarmTextColor);
    }

    /// <summary>The nearest sprung snare of <paramref name="group"/>'s within <paramref name="range"/> of <paramref name="from"/>.</summary>
    public Snare? SprungSnareNear(KinGroup group, Vector3 from, float range)
    {
        Snare? best = null;
        float bestDistanceSquared = range * range;
        foreach (Snare snare in Snares)
        {
            if (snare.IsSet || snare.GroupId != group.Id)
                continue;
            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, snare.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = snare;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    public void ResetSnare(Snare snare)
    {
        if (snare.IsSet)
            return;
        snare.IsSet = true;
        SnaresReset++;
    }

    private void DrawSnares(Camera3D camera)
    {
        foreach (Snare snare in Snares)
        {
            if (!IsVisible(snare.Position, camera))
                continue;
            Color? color = snare.GroupId is { } id && _groups.TryGetValue(id, out KinGroup? owner) ? owner.Color : null;
            snare.Draw(color);
        }
    }
}
