using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>At each Leader decision, a clan ready for a new craft works it out with odds this × its cleverest member's Intelligence.</summary>
    private const double CraftInsightChance = 0.006;

    /// <summary>The crafts a clan can work out after farming, in the order an ally teaches them.</summary>
    private static readonly Craft[] LaterCrafts =
    {
        Craft.Granary, Craft.Spears, Craft.Palisade, Craft.Grain, Craft.Mushrooms, Craft.Cress, Craft.Fishing, Craft.Stonework, Craft.Cisterns,
        Craft.Wells, Craft.Slings, Craft.Hearth, Craft.Snares, Craft.Herbalism, Craft.Herding, Craft.Smoking, Craft.Shields, Craft.Tools, Craft.Roads,
    };

    /// <summary>A clan whose main home is further than this (m) from the water works out cisterns — necessity being the mother of invention.</summary>
    private const float CisternThirstReach = 25f;

    /// <summary>A clan with a home within this far (m) of the shore can work out fishing.</summary>
    private const float FishingSettleReach = 20f;

    private static readonly Color CraftTextColor = new(120, 90, 200, 255);

    /// <summary>Crafts worked out (other than farming — see <see cref="FarmingDiscoveries"/>).</summary>
    public int CraftsDiscovered { get; private set; }

    /// <summary>Crafts (other than farming) taught by one clan to its allies.</summary>
    public int CraftsTaught { get; private set; }

    /// <summary>True if anyone in <paramref name="group"/> knows <paramref name="craft"/>.</summary>
    public static bool Knows(KinGroup group, Craft craft)
    {
        foreach (Bramblekin member in group.Members)
        {
            if (!member.IsDead && member.Knows(craft))
                return true;
        }
        return false;
    }

    /// <summary>Every craft anyone in <paramref name="group"/> knows.</summary>
    public static Craft CraftsOf(KinGroup group)
    {
        Craft known = Craft.None;
        foreach (Bramblekin member in group.Members)
        {
            if (!member.IsDead)
                known |= member.Crafts;
        }
        return known;
    }

    /// <summary>
    /// At each Leader decision: a clan teaches every craft any member knows
    /// to all of them, builds what they know onto its homes (granaries), and
    /// — once ready for one or more (see <see cref="ReadyFor"/>) — may work
    /// out one of them, picked at random.
    /// </summary>
    private void UpdateCrafts(KinGroup group)
    {
        Craft known = CraftsOf(group);
        foreach (Bramblekin member in group.Members)
            member.Learn(known);
        foreach (Shelter home in GroupHomes(group))
        {
            home.HasGranary = (known & Craft.Granary) != 0;
            home.HasCistern = (known & Craft.Cisterns) != 0;
            home.HasHearth = (known & Craft.Hearth) != 0;
            home.HasWorkshop = (known & Craft.Tools) != 0;
        }
        UpdateEra(group);

        Craft[] ready = LaterCrafts.Where(c => (known & c) == 0 && ReadyFor(group, c)).ToArray();
        if (ready.Length == 0)
            return;
        if (group.Members.Where(m => !m.IsDead && !m.IsYoung).MaxBy(m => m.Personality.Intelligence) is not { } thinker)
            return;
        if (Rng.NextDouble() >= CraftInsightChance * thinker.Personality.Intelligence)
            return; // One idea at a time.

        Craft craft = ready[Rng.Next(ready.Length)];
        foreach (Bramblekin member in group.Members)
            member.Learn(craft);
        if (craft == Craft.Grain)
            LearnedGrain(group);
        CraftsDiscovered++;
        string what = Describe(craft);
        QueueFloatingText(thinker.Position, $"Idea: {craft.ToString().ToLowerInvariant()}!", CraftTextColor);
        Game.AddEventLog($"[CRAFT] {thinker.Name} of {group.Title} worked out how to {what}");
        Headline("Discovery", $"{thinker.Name} of {group.Title} worked out how to {what}", thinker.Position, false, group);
    }

    /// <summary>
    /// What a clan needs before it can work out <paramref name="craft"/>: a
    /// granary takes farming and a House; spears, a hunting tradition or a
    /// Wolf Spider brought down; a palisade, a House and either a martial
    /// tradition or a memory of danger close to home; grain, farming;
    /// mushrooms, farming and a House; cress beds, farming and a home near
    /// the pond; fishing, just a home near the pond; stonework, a House;
    /// cisterns, a House far from the water; wells, stonework and a House
    /// far from the water; slings, spears; a hearth, a House and the cold
    /// of autumn or winter to set them thinking about fire; snares, a House; herb-lore, farming,
    /// a House and someone sick to try it on; herding, farming, a House and
    /// the aphids of spring or summer thick on the stems; smoking the bees,
    /// a hearth and a comb of honey taken already (and the stings to go with it);
    /// shields, spears and a hunting or martial tradition (beetle shells to hand).
    /// </summary>
    private bool ReadyFor(KinGroup group, Craft craft)
    {
        bool hasHouse = false, nearPond = false;
        foreach (Shelter home in GroupHomes(group))
        {
            hasHouse |= home is { IsBuilt: true, Tier: ShelterTier.House };
            nearPond |= home.IsBuilt && NearestShoreSpot(home.Position, FishingSettleReach, usual: true) is not null;
        }
        bool farms = Knows(group, Craft.Farming);
        return craft switch
        {
            Craft.Granary => hasHouse && farms,
            Craft.Spears => group.Culture.Hunting >= 0.1f || group.SpidersSlain > 0,
            Craft.Palisade => hasHouse && (group.Culture.Martial >= 0.1f || group.Dangers.Count >= 3),
            Craft.Grain => farms,
            Craft.Mushrooms => farms && hasHouse,
            Craft.Cress => farms && nearPond,
            Craft.Fishing => nearPond,
            Craft.Stonework => hasHouse,
            Craft.Cisterns => hasHouse && group.Home is { } main && WaterMap.UsualDistanceToWater(main.Position.X, main.Position.Z) > CisternThirstReach,
            Craft.Wells => hasHouse && Knows(group, Craft.Stonework) && group.Home is { } home &&
                           WaterMap.UsualDistanceToWater(home.Position.X, home.Position.Z) > WellNeedReach,
            Craft.Slings => Knows(group, Craft.Spears),
            Craft.Hearth => hasHouse && CurrentSeason is Season.Autumn or Season.Winter,
            Craft.Snares => hasHouse,
            Craft.Herbalism => hasHouse && farms && group.Members.Any(m => !m.IsDead && m.IsSick),
            Craft.Herding => hasHouse && farms && CurrentSeason is Season.Spring or Season.Summer,
            Craft.Smoking => Knows(group, Craft.Hearth) && group.HoneyTaken > 0,
            Craft.Shields => Knows(group, Craft.Spears) && (group.Culture.Hunting >= 0.2f || group.Culture.Martial >= 0.2f),
            Craft.Tools => hasHouse && EraOf(group) >= Era.FarmingAge,
            Craft.Roads => EraOf(group) >= Era.VillageAge,
            _ => false,
        };
    }

    private static string Describe(Craft craft) => craft switch
    {
        Craft.Farming => "grow berry bushes from seed",
        Craft.Granary => "build a granary",
        Craft.Spears => "sharpen twigs into spears",
        Craft.Palisade => "raise a palisade",
        Craft.Grain => "sow seed grass",
        Craft.Mushrooms => "grow mushrooms in the shade of their walls",
        Craft.Cress => "grow cress on the shore",
        Craft.Fishing => "fish from the shore",
        Craft.Stonework => "raise a house on a stone footing",
        Craft.Cisterns => "catch the rain in an acorn-cup cistern",
        Craft.Wells => "dig a well",
        Craft.Slings => "make slings and loose pebbles",
        Craft.Hearth => "keep a fire burning in a hearth",
        Craft.Snares => "set baited snares for grubs",
        Craft.Herbalism => "tend the sick with herbs",
        Craft.Herding => "herd aphids for their honeydew",
        Craft.Smoking => "smoke out the bees before taking their honey",
        Craft.Shields => "make shields of beetle shell",
        Craft.Tools => "build a workbench and make tools",
        Craft.Roads => "pave their paths into roads",
        _ => craft.ToString().ToLowerInvariant(),
    };

    /// <summary>An ally teaches the first craft it knows that <paramref name="ally"/> doesn't — farming first (odds <see cref="TeachFarmingChance"/> per decision).</summary>
    private void TryTeachCraft(KinGroup teacher, KinGroup ally)
    {
        Craft missing = CraftsOf(teacher) & ~CraftsOf(ally);
        float persuasion = teacher.Leader is { } leader ? 0.6f + 0.8f * leader.Personality.Persuasiveness : 1f;
        if (missing == Craft.None || Rng.NextDouble() >= TeachFarmingChance * persuasion)
            return;
        Craft craft = (missing & Craft.Farming) != 0 ? Craft.Farming : LaterCrafts.First(c => (missing & c) != 0);
        foreach (Bramblekin member in ally.Members)
            member.Learn(craft);
        if (craft == Craft.Farming)
        {
            FarmingTaught++;
            Game.AddEventLog($"[FARMING] {teacher.CapitalTitle} taught their allies, {ally.Title}, to grow berry bushes");
            Chronicle($"{teacher.CapitalTitle} taught {ally.Title} to farm", teacher, ally);
            return;
        }
        CraftsTaught++;
        if (craft == Craft.Grain)
            LearnedGrain(ally);
        Game.AddEventLog($"[CRAFT] {teacher.CapitalTitle} taught their allies, {ally.Title}, how to {Describe(craft)}");
        Chronicle($"{teacher.CapitalTitle} taught {ally.Title} how to {Describe(craft)}", teacher, ally);
    }

    /// <summary>True if <paramref name="point"/> lies inside a palisade (see <see cref="Craft.Palisade"/>).</summary>
    public bool IsInsidePalisade(Vector3 point)
    {
        foreach (Shelter shelter in Shelters)
        {
            if (shelter.HasPalisade && !shelter.IsCollapsed &&
                GroundMover.HorizontalDistanceSquared(point, shelter.Position) <= shelter.PalisadeRadius * shelter.PalisadeRadius)
                return true;
        }
        return false;
    }
}
