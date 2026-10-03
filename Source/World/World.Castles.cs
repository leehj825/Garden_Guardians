using System.Numerics;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Castles: a kingdom's capital house is raised into a castle ------------------------------------------------------------
    // The home of the capital's king (else the house nearest the village's middle) is the castle: twice as wide as an acorn house, with a
    // palisade ring, yard and fittings that grow with it. What stood on the ground it takes (crops, scenery, a well, a stretch of wall, a pen,
    // a snare, a neighbouring home) is cleared or moved out. A castle goes back to being a house if its kingdom falls or the capital moves.
    // Garden_Guardians_Design.md, "Villages, Paid Jobs and Kingdoms".

    /// <summary>The ground a castle clears: out to its palisade ring, and this much more (m).</summary>
    private const float CastleClearMargin = 0.3f;

    /// <summary>Castles raised, for the report.</summary>
    public int CastlesRaised { get; private set; }

    /// <summary>The castle of <paramref name="kingdom"/>, if its capital has one.</summary>
    public Shelter? CastleOf(Kingdom kingdom)
    {
        Village? capital = CapitalOf(kingdom);
        return capital is null ? null : ClansOf(capital).SelectMany(c => GroupHomes(c)).FirstOrDefault(h => h.IsCastle);
    }

    /// <summary>For the headless report: what still stands inside each castle's cleared ground (should be nothing).</summary>
    public string DescribeCastleGround()
    {
        var parts = new List<string>();
        foreach (Shelter castle in Shelters.Where(s => s.IsCastle))
        {
            float zone = castle.PalisadeRadius + CastleClearMargin;
            float Distance(Vector3 p) => GroundMover.HorizontalDistance(p, castle.Position);
            parts.Add($"castle at ({castle.Position.X:0},{castle.Position.Z:0}): homes {Shelters.Count(s => s != castle && !s.IsCollapsed && Distance(s.Position) < zone + s.Radius)}, " +
                      $"crops {Crops.Count(c => Distance(c.Position) < zone)}, pens {Pens.Count(p => Distance(p.Position) < zone)}, wells {Wells.Count(w => Distance(w.Position) < zone)}, " +
                      $"wall {WallPieces.Count(w => Distance(w.Position) < zone)}, props {GardenProps.Count(p => Distance(p.Position) < Shelter.CastleRadius)} inside {zone:0.0} m");
        }
        return parts.Count == 0 ? "no castle" : string.Join("; ", parts);
    }

    /// <summary>Each look at the kingdoms: the capitals' castles are raised (and cleared round), those of fallen capitals go back to houses.</summary>
    private void UpdateCastles()
    {
        var wanted = new HashSet<Shelter>();
        foreach (Kingdom kingdom in Realms)
        {
            if (CapitalOf(kingdom) is not { } capital)
                continue;
            var homes = ClansOf(capital).SelectMany(c => GroupHomes(c)).Where(h => h is { IsBuilt: true, IsCollapsed: false, Tier: ShelterTier.House }).ToList();
            if (homes.Count == 0)
                continue;
            Shelter? kings = KingOf(kingdom)?.Home;
            Shelter pick = kings is not null && homes.Contains(kings) ? kings
                : homes.FirstOrDefault(h => h.IsCastle) ?? homes.OrderBy(h => GroundMover.HorizontalDistanceSquared(h.Position, capital.Centre)).First();
            wanted.Add(pick);
        }

        foreach (Shelter shelter in Shelters)
        {
            if (shelter.IsCastle && !wanted.Contains(shelter))
                shelter.IsCastle = false;
        }
        foreach (Shelter castle in wanted)
        {
            if (castle.IsCastle)
                continue;
            castle.IsCastle = true;
            CastlesRaised++;
            ClearCastleGround(castle);
            if (castle.GroupId is { } clanId && _groups.TryGetValue(clanId, out KinGroup? clan))
            {
                Game.AddEventLog($"[BUILD] {clan.CapitalTitle} raises a castle");
                Chronicle($"{clan.CapitalTitle} raised a castle for the capital", clan);
            }
        }
    }

    /// <summary>Clears everything but the castle's own fittings from the ground it now covers.</summary>
    private void ClearCastleGround(Shelter castle)
    {
        float zone = castle.PalisadeRadius + CastleClearMargin;
        Vector3 at = castle.Position;
        float Distance(Vector3 p) => GroundMover.HorizontalDistance(p, at);

        // Scenery (rocks, plants, lying twigs) and plantings under the castle and its yard.
        GardenProps.RemoveAll(p => Distance(p.Position) < Shelter.CastleRadius + 0.8f);
        Crops.RemoveAll(c => Distance(c.Position) < zone + c.Footprint * 0.5f);
        Snares.RemoveAll(s => Distance(s.Position) < zone);

        // A well that is in the way is filled in; pieces of wall in the way are taken down.
        Wells.RemoveAll(w => Distance(w.Position) < zone + Well.Radius);
        WallPieces.RemoveAll(w => Distance(w.Position) < zone + 0.5f);

        // Pens of aphids are moved out to the edge of the yard.
        foreach (AphidPen pen in Pens)
        {
            float d = Distance(pen.Position);
            if (d < zone + AphidPen.Radius)
                pen.Position = Grounded(PushedOut(at, pen.Position, zone + AphidPen.Radius + 0.3f));
        }

        // Neighbouring homes are moved out clear of the castle (and its ring), or, if there is no room, pulled down.
        foreach (Shelter other in Shelters.ToList())
        {
            if (other == castle || other.IsCollapsed)
                continue;
            float need = zone + (other.HasPalisade ? other.PalisadeRadius : other.Radius + 0.5f);
            if (Distance(other.Position) >= need)
                continue;
            Vector3? spot = FreeSpotOutside(at, other.Position, need + 0.3f, other, castle);
            if (spot is { } to)
                other.MoveTo(to);
            else
            {
                int spilled = other.Collapse();
                if (spilled > 0)
                    ScatterFoodAround(at, spilled, zone + 1f, FoodShardKind.Berry);
                Shelters.Remove(other);
            }
        }

        RebuildObstacles();
    }

    /// <summary><paramref name="from"/> pushed out along its bearing from <paramref name="centre"/> to <paramref name="distance"/> (any bearing if it stands on the middle).</summary>
    private Vector3 PushedOut(Vector3 centre, Vector3 from, float distance)
    {
        var dir = new Vector2(from.X - centre.X, from.Z - centre.Z);
        dir = dir.LengthSquared() < 1e-4f ? new Vector2(1f, 0f) : Vector2.Normalize(dir);
        return new Vector3(centre.X + dir.X * distance, 0f, centre.Z + dir.Y * distance);
    }

    /// <summary>A place at least <paramref name="distance"/> from the castle, as near <paramref name="from"/>'s bearing as water, other homes and obstacles allow.</summary>
    private Vector3? FreeSpotOutside(Vector3 centre, Vector3 from, float distance, Shelter moving, Shelter castle)
    {
        var dir = new Vector2(from.X - centre.X, from.Z - centre.Z);
        float baseAngle = dir.LengthSquared() < 1e-4f ? 0f : MathF.Atan2(dir.Y, dir.X);
        for (int ring = 0; ring < 4; ring++)
        {
            float d = distance + ring * 1.5f;
            for (int k = 0; k < 12; k++)
            {
                float angle = baseAngle + (k % 2 == 0 ? 1 : -1) * (k / 2) * MathF.PI / 6f;
                Vector3 spot = Grounded(new Vector3(centre.X + MathF.Cos(angle) * d, 0f, centre.Z + MathF.Sin(angle) * d));
                if (!Terrain.Contains(spot, 3f) || IsBlockedOrAntZone(spot, moving.Radius + 0.3f))
                    continue;
                if (Shelters.Any(s => s != moving && !s.IsCollapsed && GroundMover.HorizontalDistance(s.Position, spot) < (s == castle ? distance : s.Radius + moving.Radius + 1f)))
                    continue;
                return spot;
            }
        }
        return null;
    }
}
