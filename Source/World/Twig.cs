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

    /// <summary>Draws it lying on the ground at <see cref="Position"/> (the model in Assets/Models/Props/Loose.glb, <see cref="Length"/> long).</summary>
    public void Draw() => LooseModels.Draw(LooseModels.Kind.Twig, Position, -_rotation * 180f / MathF.PI, 1f, Color.White);

    /// <summary>Draws it held across a Bramblekin's body at <paramref name="at"/>, pointing along <paramref name="facing"/>.</summary>
    public static void DrawCarried(Vector3 at, Vector2 facing) =>
        LooseModels.Draw(LooseModels.Kind.Twig, at - new Vector3(0f, 0.04f, 0f), LooseModels.YawAlong(new Vector2(-facing.Y, facing.X)), 1f, Color.White);
}
