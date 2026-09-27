namespace GardenGuardians;

/// <summary>
/// Crafts: know-how a clan works out for itself — farming first, then the
/// rest — and that lives in its members (see Bramblekin.Crafts): taught to
/// everyone in the clan, passed on to children, carried along by anyone
/// who leaves, and taught to allies. See World.Crafts.
/// </summary>
[Flags]
public enum Craft
{
    None = 0,

    /// <summary>Growing berry bushes from seed (see World.Farming).</summary>
    Farming = 1,

    /// <summary>A granary beside each House: half as much again in its store.</summary>
    Granary = 2,

    /// <summary>Sharpened twigs: half as much again of a blow against big game, Grubs, Hornets and ants.</summary>
    Spears = 4,

    /// <summary>A ring of stakes round each home: the Wolf Spider won't hunt inside it, ants can't get at the store, and raiders must break in first.</summary>
    Palisade = 8,
}
