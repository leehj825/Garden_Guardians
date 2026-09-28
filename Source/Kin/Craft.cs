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

    /// <summary>Growing berry bushes from seed (see World.Farming) — the first craft, and the start of every other crop.</summary>
    Farming = 1,

    /// <summary>A granary beside each House: half as much again in its store.</summary>
    Granary = 2,

    /// <summary>Sharpened twigs: half as much again of a blow against big game, Grubs, Hornets and ants.</summary>
    Spears = 4,

    /// <summary>A ring of stakes round each home (once its Builders have dragged in the branches): the Wolf Spider won't hunt inside it, ants can't get at the store, and raiders must break in first.</summary>
    Palisade = 8,

    /// <summary>Sowing seed grass: grain patches, a big harvest from high summer into autumn (see <see cref="CropKind.Grain"/>).</summary>
    Grain = 16,

    /// <summary>Mushroom beds in the damp shade against a House wall, best in autumn and bearing a little through winter (see <see cref="CropKind.Mushroom"/>).</summary>
    Mushrooms = 32,

    /// <summary>Cress beds on the pond's shore, best in spring and never minding a drought (see <see cref="CropKind.Cress"/>).</summary>
    Cress = 64,

    /// <summary>Fishing from the shore for minnows and tadpoles (see Bramblekin.Fishing).</summary>
    Fishing = 128,

    /// <summary>A stone footing under each House (once its Builders have carried in the stones): a dry, stone-floored store that holds more, stays dry in a flood, and ants can't dig into.</summary>
    Stonework = 256,

    /// <summary>An acorn-cup cistern by each House that catches the rain, and the cupfuls its folk carry home from the pond — so they can drink at home (see Bramblekin.Thirst).</summary>
    Cisterns = 512,

    /// <summary>Digging a well by the main home, lined with stones its Builders carry in — water at the door, all year, drought or no (see World.Wells).</summary>
    Wells = 1024,

    /// <summary>Slings of twisted grass that loose pebbles: a Hornet, a frog on the bank or the Heron can be hit from a few paces off (see Bramblekin.Slings).</summary>
    Slings = 2048,

    /// <summary>A hearth out front of each House, kept burning with twigs: food eaten there is cooked (more filling, more healing), and folk wintering in beside it stay warmer (see World.Hearths).</summary>
    Hearth = 4096,

    /// <summary>Baited grass snares set near each House that catch Grubs without a hunt; a Gatherer sets a sprung one again (see World.Snares).</summary>
    Snares = 8192,

    /// <summary>Herb-lore: a Healer tends the clan's sick (shortening the illness) and wounded (see Bramblekin.Healing).</summary>
    Herbalism = 16384,

    /// <summary>Herding aphids in a pen by the main home for their honeydew — steady food that keeps (see World.Herding).</summary>
    Herding = 32768,

    /// <summary>Smoking out the bees with a brand from the hearth before taking their honey: they seldom rouse (see World.Beehive).</summary>
    Smoking = 65536,
}
