using System.Numerics;

namespace GardenGuardians;

/// <summary>The three pieces a village wall is made of (see Tools/convert_walls.py): a straight length, and the two ends that cap it either side of a gate.</summary>
public enum WallKind
{
    /// <summary>A straight length of wall, origin at its middle.</summary>
    Straight = 0,

    /// <summary>A wall's end, origin where it joins the wall, running out along +x to its tip.</summary>
    EndA = 1,

    /// <summary>The other end (turned half a circle), likewise.</summary>
    EndB = 2,
}

/// <summary>
/// One piece of a clan's stone wall (see World.Walls): raised a piece at a time by the clan once it reaches the Kingdom Age, round
/// its homes, with a gate — an opening between two ends — wherever feet have worn a path through the line. Once built it is solid:
/// an obstacle to everything that walks, and blocked ground in the walkers' route grid, so they find their way round it, through
/// the gates.
/// </summary>
public sealed class WallPiece
{
    /// <summary>Lengths (m) of the model's three pieces, its thickness, and how tall it stands (see Tools/convert_walls.py).</summary>
    public const float StraightLength = 3.38f, EndALength = 1.61f, EndBLength = 1.17f, Thickness = 0.56f, Height = 1.1f;

    public WallPiece(Vector3 position, float yaw, WallKind kind, Guid? groupId, bool isBuilt, float scale = 1f)
    {
        Scale = scale;
        Position = position;
        Yaw = yaw;
        Kind = kind;
        GroupId = groupId;
        IsBuilt = isBuilt;
    }

    /// <summary>Where it stands, on the ground: its middle (a straight length) or the edge it joins the wall by (an end).</summary>
    public Vector3 Position { get; }

    /// <summary>The way its length runs (radians from +x towards +z): along the wall, or out from the wall into the gate.</summary>
    public float Yaw { get; }

    public WallKind Kind { get; }

    /// <summary>The clan that raised it — null once that clan is gone (the wall stays).</summary>
    public Guid? GroupId { get; }

    public bool IsBuilt { get; set; }

    /// <summary>How far gone it is, 0 sound to 1 fallen: a wall nobody keeps up crumbles, sinking and slumping, then is gone (see World.Walls).</summary>
    public float Ruin { get; set; }

    /// <summary>How much longer (or shorter) than the model a straight length is stretched, so a run of wall meets its ends exactly (ends are not stretched).</summary>
    public float Scale { get; }

    public float Length => Kind switch { WallKind.Straight => StraightLength * Scale, WallKind.EndA => EndALength, _ => EndBLength };

    private Vector2 Direction => new(MathF.Cos(Yaw), MathF.Sin(Yaw));

    /// <summary>The line it stands along, end to end (x, z).</summary>
    public (Vector2 A, Vector2 B) Segment
    {
        get
        {
            var p = new Vector2(Position.X, Position.Z);
            return Kind == WallKind.Straight ? (p - Direction * (Length / 2f), p + Direction * (Length / 2f)) : (p, p + Direction * Length);
        }
    }

    /// <summary>Centres of the circles that make it solid to a walker: a few along its length, each a little fatter than the wall is thick.</summary>
    public IEnumerable<Vector2> ObstacleCentres(float spacing)
    {
        (Vector2 a, Vector2 b) = Segment;
        int n = Math.Max(1, (int)MathF.Ceiling(Length / spacing));
        for (int i = 0; i <= n; i++)
            yield return Vector2.Lerp(a, b, i / (float)n);
    }
}
