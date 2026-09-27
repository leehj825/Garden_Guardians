using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Building material: a fallen twig lying on the lawn, mostly around the
/// big Twig props (see <see cref="World.UpdateTwigSpawn"/>). A Bramblekin
/// carries one at a time to a <see cref="Shelter"/> under construction.
/// A twig nobody picks up decomposes after <see cref="DespawnLifespan"/>,
/// so the supply keeps turning over across the map instead of piling up,
/// capped, in places nobody goes.
/// </summary>
public sealed class Twig
{
    /// <summary>Length (m) of a twig lying on the ground.</summary>
    public const float Length = 0.45f;

    private const float Thickness = 0.03f;

    private static readonly Color BarkColor = new(115, 80, 45, 255);

    /// <summary>Resting spot on the ground. Ignored while carried.</summary>
    public Vector3 Position { get; set; }

    /// <summary>True while a Bramblekin is holding it; a carried twig is hidden from the map and from every search.</summary>
    public bool IsCarried { get; set; }

    /// <summary>Object Pooling: false for a pool slot that isn't currently a real twig — see <see cref="World.Twigs"/>.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Dibs: the one Bramblekin walking to pick this up, if any — same claim/timeout pattern as <see cref="FoodShard.ClaimedBy"/>.</summary>
    public Bramblekin? ClaimedBy { get; set; }

    /// <summary>Seconds since <see cref="ClaimedBy"/> was last set; World force-releases stale claims.</summary>
    public float ClaimTimer { get; set; }

    /// <summary>Seconds a loose twig lies on the ground before it decomposes.</summary>
    public const float DespawnLifespan = 240f;

    /// <summary>Counts down while it lies loose; World removes it at 0.</summary>
    public float DespawnTimer { get; set; }

    /// <summary>Which way it lies (radians).</summary>
    private float _rotation;

    /// <summary>Constructs an inactive pool slot. Call <see cref="Activate"/> to actually spawn one.</summary>
    public Twig()
    {
    }

    public void Activate(Vector3 groundPoint, float rotation)
    {
        Position = World.Grounded(groundPoint);
        _rotation = rotation;
        IsCarried = false;
        ClaimedBy = null;
        ClaimTimer = 0f;
        DespawnTimer = DespawnLifespan;
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
        IsCarried = false;
        ClaimedBy = null;
    }

    /// <summary>Draws it lying on the ground at <see cref="Position"/>.</summary>
    public void Draw()
    {
        var half = new Vector3(MathF.Cos(_rotation), 0f, MathF.Sin(_rotation)) * (Length / 2f);
        Vector3 center = Position + new Vector3(0f, Thickness, 0f);
        Raylib.DrawCylinderEx(center - half, center + half, Thickness, Thickness * 0.7f, 5, BarkColor);
    }

    /// <summary>Draws it held across a Bramblekin's body at <paramref name="at"/>, pointing along <paramref name="facing"/>.</summary>
    public static void DrawCarried(Vector3 at, Vector2 facing)
    {
        var across = new Vector3(-facing.Y, 0.15f, facing.X) * (Length / 2f);
        Raylib.DrawCylinderEx(at - across, at + across, Thickness, Thickness * 0.7f, 5, BarkColor);
    }
}
