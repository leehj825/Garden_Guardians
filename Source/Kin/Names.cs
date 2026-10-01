namespace GardenGuardians;

/// <summary>
/// Bramblekin names: a short given name built from two syllables, and a
/// garden-flavoured family name. A newcomer founds a family of its own; a
/// child born here takes the family name of one of its parents, so a
/// family's name spreads (or dies out) with its descendants. Groups are
/// named for the family of the Leader they were founded under.
/// </summary>
public static class Names
{
    private static readonly string[] GivenStarts =
    {
        "Bri", "Mo", "Pip", "Tan", "Fen", "Wil", "Ro", "Sa", "Ki", "Lu", "Ner", "Ash",
        "Bel", "Cor", "Dun", "El", "Fa", "Gil", "Hal", "Iv", "Jun", "Lar", "Mab", "Nim",
        "Ot", "Quil", "Ru", "Syl", "Tam", "Ul", "Vel", "Wren", "Tib", "Pell", "Hob", "Nell",
    };

    private static readonly string[] GivenEnds =
    {
        "a", "o", "i", "el", "en", "in", "y", "wick", "ley", "bry", "ric", "an", "is", "et", "ett", "om", "a", "o",
    };

    private static readonly string[] FamilyStarts =
    {
        "Thorn", "Bramble", "Moss", "Fern", "Clover", "Nettle", "Burr", "Acorn", "Briar", "Pebble",
        "Dew", "Mallow", "Thistle", "Hazel", "Rowan", "Sorrel", "Tansy", "Yarrow", "Sedge", "Rush",
        "Birch", "Alder", "Hollow", "Root", "Leaf", "Seed", "Bark", "Twig", "Petal", "Mint",
    };

    private static readonly string[] FamilyEnds =
    {
        "wood", "foot", "root", "dale", "brook", "leaf", "vale", "shade", "patch", "hollow",
        "bank", "stone", "field", "thatch", "hill", "down",
    };

    private static readonly string[] Numerals = { "", " II", " III", " IV", " V", " VI", " VII", " VIII", " IX", " X" };

    /// <summary>A random given name, e.g. "Pipwick" or "Nella".</summary>
    public static string Given(Random rng)
    {
        string start = GivenStarts[rng.Next(GivenStarts.Length)];
        string end = GivenEnds[rng.Next(GivenEnds.Length)];
        // Avoid doubled letters at the seam ("Bel" + "ley" -> "Beley", not "Belley").
        if (start[^1] == end[0])
            end = end[1..];
        return start + end;
    }

    /// <summary>A random family name, e.g. "Thornwood" or "Mossbrook" — see <see cref="World.NewFamilyName"/> for one nobody has yet.</summary>
    public static string Family(Random rng)
    {
        string start = FamilyStarts[rng.Next(FamilyStarts.Length)];
        string end = FamilyEnds[rng.Next(FamilyEnds.Length)];
        // No "Leafleaf" or "Hollowhollow".
        while (string.Equals(start, end, StringComparison.OrdinalIgnoreCase))
            end = FamilyEnds[rng.Next(FamilyEnds.Length)];
        return start + end;
    }

    /// <summary>
    /// A group's name from its founding Leader's family: "the Thornwood
    /// clan", then "the Thornwood clan II" and so on while an earlier one
    /// with that name is still around (see <paramref name="taken"/>).
    /// </summary>
    public static string Clan(string family, Func<string, bool> taken)
    {
        for (int i = 0; i < Numerals.Length; i++)
        {
            string name = $"{family} clan{Numerals[i]}";
            if (!taken(name))
                return name;
        }
        return $"{family} clan {Guid.NewGuid().ToString("N")[..3]}";
    }

    private static readonly string[] VillageStart = { "Moss", "Fern", "Bramble", "Acorn", "Dew", "Thistle", "Clover", "Hazel", "Willow", "Pebble", "Briar", "Sorrel", "Linden", "Marigold", "Tansy", "Alder" };
    private static readonly string[] VillageEnd = { "hollow", "bridge", "stead", "ford", "wick", "dale", "mere", "gate", "croft", "cross" };

    /// <summary>A village's name ("Mossbridge", "Thistlecroft"), one not <paramref name="taken"/>.</summary>
    public static string Village(Random rng, Func<string, bool> taken)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            string name = VillageStart[rng.Next(VillageStart.Length)] + VillageEnd[rng.Next(VillageEnd.Length)];
            if (!taken(name))
                return name;
        }
        return VillageStart[rng.Next(VillageStart.Length)] + VillageEnd[rng.Next(VillageEnd.Length)] + " " + Guid.NewGuid().ToString("N")[..3];
    }

    private static readonly string[] KingdomEnd = { "reach", "mark", "land", "realm", "wood", "crown" };

    /// <summary>A kingdom's name ("Thornreach", "Hazelmark"), one not <paramref name="taken"/>.</summary>
    public static string Kingdom(Random rng, Func<string, bool> taken)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            string name = VillageStart[rng.Next(VillageStart.Length)] + KingdomEnd[rng.Next(KingdomEnd.Length)];
            if (!taken(name))
                return name;
        }
        return VillageStart[rng.Next(VillageStart.Length)] + KingdomEnd[rng.Next(KingdomEnd.Length)] + " " + Guid.NewGuid().ToString("N")[..3];
    }
}
