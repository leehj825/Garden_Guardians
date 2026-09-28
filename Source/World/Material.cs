using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>What a piece of building material is (see <see cref="Material"/>).</summary>
public enum MaterialKind
{
    /// <summary>A stone the size of a Bramblekin's head, at the foot of the big rocks: for a House's stone footing (<see cref="Craft.Stonework"/>).</summary>
    Stone,

    /// <summary>A thorny branch, fallen from the Giant Oak in a storm or off the big sticks on the lawn: for a palisade's stakes (<see cref="Craft.Palisade"/>).</summary>
    Branch,
}

/// <summary>
/// Stones and branches: building material bigger than a twig, lying where
/// it fell until a Builder carries (or drags) it home — stones to raise a
/// House on a footing, branches to stake a palisade (see World.Materials).
/// Object-pooled like Food and Twigs.
/// </summary>
public sealed class Material
{
    public const float StoneRadius = 0.16f;
    public const float BranchLength = 1.3f;

    /// <summary>A branch left lying rots away after this long; a stone never does.</summary>
    public const float BranchLifespan = 900f;

    private static readonly Color StoneColor = new(135, 135, 140, 255);
    private static readonly Color BranchColor = new(96, 72, 48, 255);
    private static readonly Color ThornColor = new(128, 58, 44, 255);

    public Vector3 Position { get; set; }
    public MaterialKind Kind { get; private set; }
    public bool IsCarried { get; set; }
    public bool IsActive { get; private set; }

    /// <summary>Dibs: the Builder walking to it, if any.</summary>
    public Bramblekin? ClaimedBy { get; set; }

    /// <summary>Seconds a branch has left before it rots (stones ignore it).</summary>
    public float DespawnTimer { get; set; }

    private float _rotation;

    public void Activate(Vector3 groundPoint, MaterialKind kind, float rotation)
    {
        Position = World.Grounded(groundPoint);
        Kind = kind;
        _rotation = rotation;
        IsCarried = false;
        ClaimedBy = null;
        DespawnTimer = BranchLifespan;
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
        IsCarried = false;
        ClaimedBy = null;
    }

    /// <summary>A grey rounded stone, or a long forked branch lying on the grass, studded with thorns.</summary>
    public void Draw()
    {
        if (Kind == MaterialKind.Stone)
        {
            DrawStone(Position + new Vector3(0f, StoneRadius * 0.6f, 0f));
            return;
        }
        var along = new Vector3(MathF.Cos(_rotation), 0f, MathF.Sin(_rotation));
        Vector3 center = Position + new Vector3(0f, 0.06f, 0f);
        DrawBranch(center - along * (BranchLength / 2f), center + along * (BranchLength / 2f), along);
    }

    /// <summary>Carried: a stone held up in front, or a branch dragged along behind.</summary>
    public void DrawCarried(Vector3 feet, float bodyHeight, Vector2 facing)
    {
        var forward = new Vector3(facing.X, 0f, facing.Y);
        if (Kind == MaterialKind.Stone)
        {
            DrawStone(feet + new Vector3(0f, bodyHeight * 0.55f, 0f) + forward * 0.22f);
            return;
        }
        Vector3 hands = feet + new Vector3(0f, bodyHeight * 0.5f, 0f);
        Vector3 end = World.Grounded(feet - forward * BranchLength, 0.05f);
        DrawBranch(hands, end, -forward);
    }

    private static void DrawStone(Vector3 center)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(center.X, center.Y, center.Z);
        Rlgl.Scalef(1f, 0.7f, 1f);
        Raylib.DrawSphereEx(Vector3.Zero, StoneRadius, 5, 7, StoneColor);
        Rlgl.PopMatrix();
    }

    private static void DrawBranch(Vector3 from, Vector3 to, Vector3 along)
    {
        Raylib.DrawCylinderEx(from, to, 0.06f, 0.035f, 6, BranchColor);
        var side = new Vector3(-along.Z, 0f, along.X);
        Vector3 fork = Vector3.Lerp(from, to, 0.55f);
        Raylib.DrawCylinderEx(fork, fork + (along + side) * 0.3f + new Vector3(0f, 0.05f, 0f), 0.03f, 0.015f, 4, BranchColor);
        for (int i = 1; i < 5; i++)
        {
            Vector3 at = Vector3.Lerp(from, to, i / 5f);
            Vector3 point = (i % 2 == 0 ? side : -side) * 0.1f + new Vector3(0f, 0.05f, 0f);
            Raylib.DrawCylinderEx(at, at + point, 0.02f, 0f, 4, ThornColor);
        }
    }
}
