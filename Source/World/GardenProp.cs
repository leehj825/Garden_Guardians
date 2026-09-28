using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>Which kind of static <see cref="GardenProp"/> decoration this is.</summary>
public enum GardenPropKind
{
    /// <summary>A large gray pebble/rock — a hemisphere pressed into the lawn.</summary>
    Pebble,

    /// <summary>A long brown twig lying flat, randomly rotated.</summary>
    Twig,

    /// <summary>A tall dandelion/flower — a green stem topped with a large yellow or white puff, towering over Bramblekin scale.</summary>
    Dandelion,
}

/// <summary>
/// Part 4, Oversized Garden Props: a static piece of backyard scenery — a
/// Pebble, a lying Twig, or a towering Dandelion — scattered across the map
/// by <see cref="World.SpawnGardenProps"/>. No Update, only Draw (and
/// Part 2/6's culling/grounding, applied by the caller and at construction
/// respectively). A Pebble is the one solid prop: its
/// <see cref="FootprintRadius"/> becomes an <see cref="Obstacle"/> that
/// walkers steer around.
/// </summary>
public sealed class GardenProp
{
    public Vector3 Position { get; }
    public GardenPropKind Kind { get; }

    /// <summary>Random facing (radians) — mainly meaningful for a Twig lying flat.</summary>
    private readonly float _rotation;

    /// <summary>Twig length (m), randomized per-instance so the map doesn't read as identical copies.</summary>
    private readonly float _twigLength;

    /// <summary>Whether this Dandelion's puff is yellow (a true dandelion) or white (a seed-head/dandelion clock).</summary>
    private readonly bool _isYellow;

    /// <summary>Per-instance size variation (0.8-1.3x), so a field of the same prop kind doesn't look copy-pasted.</summary>
    private readonly float _scale;

    /// <summary>Solid footprint radius (m) — a Pebble's dome; Twigs and Dandelions are walked over/around freely (0).</summary>
    public float FootprintRadius => Kind == GardenPropKind.Pebble ? 0.5f * _scale : 0f;

    /// <summary>Loading a saved world: a prop exactly as it was (see World.Save).</summary>
    public GardenProp(Vector3 position, GardenPropKind kind, float rotation, float twigLength, bool isYellow, float scale)
    {
        Position = position;
        Kind = kind;
        _rotation = rotation;
        _twigLength = twigLength;
        _isYellow = isYellow;
        _scale = scale;
    }

    public float Rotation => _rotation;
    public float TwigLength => _twigLength;
    public bool IsYellow => _isYellow;
    public float Scale => _scale;

    public GardenProp(Vector3 groundPosition, GardenPropKind kind, float rotation, Random rng)
    {
        // Follow-up Part 2: the caller already grounded this point via
        // World.Grounded — add a small explicit lift here too so a Pebble/
        // Twig/Dandelion's base doesn't visually sink into a slope.
        Position = groundPosition + new Vector3(0, 0.06f, 0);
        Kind = kind;
        _rotation = rotation;
        _twigLength = 0.6f + (float)rng.NextDouble() * 0.9f;
        _isYellow = rng.NextDouble() < 0.7;
        _scale = 0.8f + (float)rng.NextDouble() * 0.5f;
    }

    public void Draw()
    {
        switch (Kind)
        {
            case GardenPropKind.Pebble:
                DrawPebble();
                break;
            case GardenPropKind.Twig:
                DrawTwig();
                break;
            case GardenPropKind.Dandelion:
                DrawDandelion();
                break;
        }
    }

    /// <summary>
    /// Follow-up Part 3, Surface-Normal Tilting: pushes an Rlgl matrix
    /// translated to this prop's ground position and rotated to match the
    /// terrain's surface normal there, then translated locally up by
    /// <paramref name="halfHeight"/> so the shape's local origin (0,0,0)
    /// rests un-buried on the dirt. Caller draws its primitive(s) at local
    /// origin and then calls <see cref="Rlgl.PopMatrix"/>.
    /// </summary>
    private void PushGroundedTiltMatrix(float halfHeight)
    {
        Vector3 normal = World.GetNormalAt(Position.X, Position.Z);
        Vector3 axis = Vector3.Cross(Vector3.UnitY, normal);
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitY, normal), -1.0f, 1.0f)) * (180.0f / MathF.PI);

        Rlgl.PushMatrix();
        Rlgl.Translatef(Position.X, World.GetHeightAt(Position.X, Position.Z), Position.Z);
        if (axis.Length() > 0.001f)
            Rlgl.Rotatef(angle, axis.X, axis.Y, axis.Z);
        Rlgl.Translatef(0, halfHeight, 0);
    }

    /// <summary>A large gray hemisphere-ish rock, oversized against a Bramblekin.</summary>
    private void DrawPebble()
    {
        float radius = 0.5f * _scale;
        var stone = new Color(130, 130, 135, 255);
        var stoneEdge = new Color(80, 80, 85, 200);

        PushGroundedTiltMatrix(radius * 0.55f);

        // Squash a full sphere into a rock-like dome via Rlgl scaling.
        Rlgl.PushMatrix();
        Rlgl.Scalef(1f, 0.6f, 1f);
        Raylib.DrawSphere(Vector3.Zero, radius, stone);
        Raylib.DrawSphereWires(Vector3.Zero, radius, 8, 8, stoneEdge);
        Rlgl.PopMatrix();

        Rlgl.PopMatrix();
    }

    /// <summary>A long brown cylinder lying flat on the ground, randomly rotated — DrawCylinderEx avoids any manual rotation matrix.</summary>
    private void DrawTwig()
    {
        float length = _twigLength * _scale;
        float radius = 0.05f * _scale;
        var brown = new Color(101, 67, 33, 255);

        PushGroundedTiltMatrix(radius);

        var half = new Vector3(MathF.Cos(_rotation), 0, MathF.Sin(_rotation)) * (length / 2f);
        Vector3 start = -half;
        Vector3 end = half;
        Raylib.DrawCylinderEx(start, end, radius, radius * 0.7f, 8, brown);

        Rlgl.PopMatrix();
    }

    /// <summary>A tall green stem topped with a large fluffy sphere — towers well above Bramblekin scale.</summary>
    private void DrawDandelion()
    {
        float stemHeight = 1.4f * _scale;
        float stemRadius = 0.04f * _scale;
        float puffRadius = 0.35f * _scale;
        var stemColor = new Color(60, 130, 40, 255);
        Color puffColor = _isYellow ? new Color(250, 210, 40, 255) : new Color(245, 245, 235, 220);

        PushGroundedTiltMatrix(stemHeight / 2f);

        var stemBase = new Vector3(0, -stemHeight / 2f, 0);
        var stemTop = new Vector3(0, stemHeight / 2f, 0);
        Raylib.DrawCylinder(stemBase, stemRadius, stemRadius, stemHeight, 8, stemColor);
        Raylib.DrawSphere(stemTop + new Vector3(0, puffRadius * 0.6f, 0), puffRadius, puffColor);

        Rlgl.PopMatrix();
    }
}
