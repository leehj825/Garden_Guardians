using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>Where a piece of Food came from — purely cosmetic, it's worth the same either way.</summary>
public enum FoodShardKind
{
    /// <summary>Passive Foraging: a wild Berry. Red.</summary>
    Berry,

    /// <summary>Dropped by a hunted Grub or a slain Wolf Spider. Orange.</summary>
    Meat,
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

    /// <summary>Where it came from. Only affects colour.</summary>
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
    /// <see cref="World.IsAvailable"/>. Released the moment that Bramblekin
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
        DespawnTimer = DespawnLifespan;
        IsActive = true;
    }

    /// <summary>Object Pooling: returns this slot to the pool — eaten or rotted away. See <see cref="World.FoodShards"/>.</summary>
    public void Deactivate()
    {
        IsActive = false;
        IsCarried = false;
        ClaimedBy = null;
    }

    /// <summary>Draws the Food resting on the ground at (or carried above) <paramref name="groundPoint"/>.</summary>
    public void Draw(Vector3 groundPoint)
    {
        Color color = Kind == FoodShardKind.Berry ? new Color(210, 40, 45, 255) : new Color(245, 150, 45, 255);
        Raylib.DrawSphere(groundPoint + new Vector3(0, Radius, 0), Radius, color);
    }
}
