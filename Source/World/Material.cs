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

    /// <summary>A grey river stone, or a long thorny branch lying on the grass (Assets/Models/Props/Loose.glb).</summary>
    public void Draw()
    {
        if (Kind == MaterialKind.Stone)
        {
            DrawStone(Position - new Vector3(0f, 0.02f, 0f));
            return;
        }
        LooseModels.Draw(LooseModels.Kind.Branch, Position + new Vector3(0f, 0.01f, 0f), -_rotation * 180f / MathF.PI, 1f, Color.White);
    }

    /// <summary>Carried: a stone held up in front, or a branch dragged along behind.</summary>
    public void DrawCarried(Vector3 feet, float bodyHeight, Vector2 facing)
    {
        var forward = new Vector3(facing.X, 0f, facing.Y);
        if (Kind == MaterialKind.Stone)
        {
            DrawStone(feet + new Vector3(0f, bodyHeight * 0.55f - LooseModels.StoneHeight / 2f, 0f) + forward * 0.22f);
            return;
        }
        Vector3 hands = feet + new Vector3(0f, bodyHeight * 0.5f, 0f);
        Vector3 end = World.Grounded(feet - forward * BranchLength, 0.05f);
        LooseModels.DrawBetween(LooseModels.Kind.Branch, hands, end, Color.White);
    }

    /// <summary>The stone standing on <paramref name="ground"/> (its foot), turned its own way (no two alike).</summary>
    private void DrawStone(Vector3 ground) => LooseModels.Draw(LooseModels.Kind.Stone, ground, _rotation * 180f / MathF.PI, 1f, Color.White);
}
