namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>Crafts (any kind) a clan needs to know for each age above the Farming Age.</summary>
    private const int VillageAgeCrafts = 7, KingdomAgeCrafts = 12;

    /// <summary>Clans that reached each age (for the headless report), indexed by <see cref="Era"/>.</summary>
    public int[] EraTransitions { get; } = new int[4];

    private static int CountCrafts(Craft crafts)
    {
        int n = 0;
        for (int bits = (int)crafts; bits != 0; bits &= bits - 1)
            n++;
        return n;
    }

    /// <summary>The age <paramref name="group"/> has reached: farming makes the Farming Age; stonework and seven crafts the Village Age; twelve crafts the Kingdom Age.</summary>
    public static Era EraOf(KinGroup group)
    {
        Craft known = CraftsOf(group);
        int crafts = CountCrafts(known);
        if (crafts >= KingdomAgeCrafts && (known & Craft.Stonework) != 0)
            return Era.KingdomAge;
        if (crafts >= VillageAgeCrafts && (known & Craft.Stonework) != 0)
            return Era.VillageAge;
        return (known & Craft.Farming) != 0 ? Era.FarmingAge : Era.StoneAge;
    }

    private static string EraName(Era era) => era switch
    {
        Era.FarmingAge => "Farming Age",
        Era.VillageAge => "Village Age",
        Era.KingdomAge => "Kingdom Age",
        _ => "Stone Age",
    };

    /// <summary>At each Leader decision: announce a clan that has moved into a new age.</summary>
    private void UpdateEra(KinGroup group)
    {
        Era era = EraOf(group);
        if (era <= group.Era)
            return;
        group.Era = era;
        EraTransitions[(int)era]++;
        Game.AddEventLog($"[ERA] {group.CapitalTitle} entered the {EraName(era)}");
        Headline("A new age", $"{group.CapitalTitle} entered the {EraName(era)}", group.Leader?.Position, false, group);
        Chronicle($"{group.CapitalTitle} entered the {EraName(era)}", group);
    }
}
