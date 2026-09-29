namespace GardenGuardians;

/// <summary>
/// How far a clan's knowledge has come, worked out from the crafts it has
/// (see <c>World.EraOf</c>). Each age unlocks crafts the one before could not
/// think of yet, and is announced when a clan reaches it.
/// </summary>
public enum Era
{
    /// <summary>Foraging and hunting, tents and burrows.</summary>
    StoneAge = 0,

    /// <summary>The clan grows food: it knows farming.</summary>
    FarmingAge = 1,

    /// <summary>Houses on stone and a handful of crafts: roads and shared work become possible.</summary>
    VillageAge = 2,

    /// <summary>A dozen crafts or more: the clan is a power in the garden.</summary>
    KingdomAge = 3,
}
