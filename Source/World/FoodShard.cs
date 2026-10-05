using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>What a piece of Food is — it's worth the same either way; what differs is where and when it turns up.</summary>
public enum FoodShardKind
{
    /// <summary>Passive Foraging: a wild Berry, or one off a bush. Red.</summary>
    Berry,

    /// <summary>Dropped by a hunted Grub or a slain Wolf Spider. Orange.</summary>
    Meat,

    /// <summary>Fallen from the Giant Oak in autumn (see World.Oak). An acorn in its cap.</summary>
    Acorn,

    /// <summary>Grass seed, shed on the open lawn from high summer into autumn, or off a grain patch. A little golden cluster.</summary>
    Seed,

    /// <summary>A mushroom, sprung up in the oak's shade or by the rocks — most in autumn and after rain — or off a mushroom bed.</summary>
    Mushroom,

    /// <summary>Watercress from the pond's shore, or a cress bed. A green sprig.</summary>
    Cress,

    /// <summary>A minnow or tadpole caught from the shore (see <see cref="Craft.Fishing"/>). Silver.</summary>
    Fish,

    /// <summary>A drop of honeydew from a clan's aphids (see <see cref="Craft.Herding"/>): sweet, and slow to spoil. Amber.</summary>
    Honeydew,

}

/// <summary>
/// Loose Food: a single bite — a wild Berry or a scrap of meat — lying on
/// the ground for any Bramblekin (or Grub) to find. A Bramblekin either eats
/// it on the spot or carries one as a reserve, which is exactly what a
/// starving, aggressive neighbour may try to steal.
/// </summary>
public sealed class FoodShard
{
    public const float Radius = 0.18f;

    /// <summary>Resting spot on the ground. Ignored while carried.</summary>
    public Vector3 Position { get; set; }

    /// <summary>True while a Bramblekin is holding it; carried Food is hidden from the map and from every search.</summary>
    public bool IsCarried { get; set; }

    /// <summary>Where it came from. Only affects how it looks.</summary>
    public FoodShardKind Kind { get; private set; }

    /// <summary>
    /// Object Pooling: false for a pool slot that isn't currently real Food
    /// on the map. World pre-allocates a fixed pool of these at startup (see
    /// <see cref="World.FoodShards"/>) instead of constructing and destroying
    /// one per spawn/pickup/despawn; every rendering and targeting loop over
    /// the pool must skip anything with this false.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Dibs: the one Bramblekin currently walking to this Food, if any — see
    /// <see cref="World.IsAvailable(FoodShard, Bramblekin?)"/>. Released the moment that Bramblekin
    /// stops foraging for it, and force-released after
    /// <see cref="World.FoodClaimTimeoutSeconds"/> as a failsafe.
    /// </summary>
    public Bramblekin? ClaimedBy { get; set; }

    /// <summary>Seconds since <see cref="ClaimedBy"/> was last set. Reset to 0 on every new claim; ticked and enforced by World.</summary>
    public float ClaimTimer { get; set; }

    /// <summary>Seconds uncarried Food sits on the map before it rots away — see <see cref="DespawnTimer"/>.</summary>
    public const float DespawnLifespan = 60f;

    /// <summary>Counts down from <see cref="DespawnLifespan"/> while this Food lies on the ground uncarried; World removes it at 0.</summary>
    public float DespawnTimer { get; set; } = DespawnLifespan;

    /// <summary>Constructs an inactive pool slot — see <see cref="World.FoodShards"/>. Call <see cref="Activate"/> to actually spawn one.</summary>
    public FoodShard()
    {
    }

    /// <summary>Object Pooling: reuses this pool slot as freshly spawned Food at <paramref name="groundPoint"/>, resetting every bit of its previous state.</summary>
    public void Activate(Vector3 groundPoint, FoodShardKind kind)
    {
        Position = World.Grounded(groundPoint); // Snap onto the hilly terrain.
        Kind = kind;
        IsCarried = false;
        ClaimedBy = null;
        ClaimTimer = 0f;
        DespawnTimer = kind switch
        {
            FoodShardKind.Honeydew => DespawnLifespan * 4f,
            _ => DespawnLifespan,
        };
        IsActive = true;
    }

    /// <summary>Object Pooling: returns this slot to the pool — eaten or rotted away. See <see cref="World.FoodShards"/>.</summary>
    public void Deactivate()
    {
        IsActive = false;
        IsCarried = false;
        ClaimedBy = null;
    }

    /// <summary>Draws the Food resting on the ground at (or carried above) <paramref name="groundPoint"/>: a model from Assets/Models/Props/Loose.glb (the mushroom and the cress are still drawn plainly).</summary>
    public void Draw(Vector3 groundPoint)
    {
        // Each piece lies its own way (no two berries in a row look copied), the same every frame.
        float yaw = (Position.X * 91.7f + Position.Z * 57.3f) % 360f;
        switch (Kind)
        {
            case FoodShardKind.Acorn:
                LooseModels.Draw(LooseModels.Kind.Acorn, groundPoint, yaw, 1f, Color.White);
                return;
            case FoodShardKind.Seed:
                LooseModels.Draw(LooseModels.Kind.Seed, groundPoint, yaw, 1f, Color.White);
                return;
            case FoodShardKind.Mushroom:
                // A pale stem under a russet cap.
                Raylib.DrawCylinder(groundPoint, 0.05f, 0.06f, 0.16f, 6, new Color(235, 225, 205, 255));
                Raylib.DrawCylinder(groundPoint + new Vector3(0, 0.14f, 0), 0.02f, Radius, 0.14f, 10, new Color(170, 95, 60, 255));
                return;
            case FoodShardKind.Cress:
                // A sprig of round green leaves.
                Detail.Sphere(groundPoint + new Vector3(0, 0.1f, 0), 0.11f, new Color(90, 185, 70, 255));
                Detail.Sphere(groundPoint + new Vector3(0.09f, 0.08f, 0.04f), 0.08f, new Color(120, 205, 85, 255));
                Detail.Sphere(groundPoint + new Vector3(-0.08f, 0.08f, -0.05f), 0.08f, new Color(120, 205, 85, 255));
                return;
            case FoodShardKind.Fish:
                LooseModels.Draw(LooseModels.Kind.Fish, groundPoint, yaw, 1f, Color.White);
                return;
            case FoodShardKind.Honeydew:
                LooseModels.Draw(LooseModels.Kind.Honeydew, groundPoint, yaw, 1f, Color.White);
                return;
            case FoodShardKind.Berry:
                LooseModels.Draw(LooseModels.Kind.Berry, groundPoint, yaw, 1f, Color.White);
                return;
        }
        LooseModels.Draw(LooseModels.Kind.Meat, groundPoint, yaw, 1f, Color.White);
    }
}
