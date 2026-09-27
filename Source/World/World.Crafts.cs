using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>At each Leader decision, a clan ready for a new craft works it out with odds this × its cleverest member's Intelligence.</summary>
    private const double CraftInsightChance = 0.006;

    /// <summary>The crafts a clan can work out after farming, in the order an ally teaches them.</summary>
    private static readonly Craft[] LaterCrafts = { Craft.Granary, Craft.Spears, Craft.Palisade };

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
    /// to all of them, builds what they know onto its homes (granaries,
    /// palisades), and — once ready for one (see <see cref="ReadyFor"/>) —
    /// may work out a new craft.
    /// </summary>
    private void UpdateCrafts(KinGroup group)
    {
        Craft known = CraftsOf(group);
        foreach (Bramblekin member in group.Members)
            member.Learn(known);
        foreach (Shelter home in GroupHomes(group))
        {
            home.HasGranary = (known & Craft.Granary) != 0;
            home.HasPalisade = (known & Craft.Palisade) != 0 && home.IsBuilt;
        }

        foreach (Craft craft in LaterCrafts)
        {
            if ((known & craft) != 0 || !ReadyFor(group, craft))
                continue;
            if (group.Members.Where(m => !m.IsDead && !m.IsYoung).MaxBy(m => m.Personality.Intelligence) is not { } thinker)
                return;
            if (Rng.NextDouble() >= CraftInsightChance * thinker.Personality.Intelligence)
                return; // One idea at a time.

            foreach (Bramblekin member in group.Members)
                member.Learn(craft);
            CraftsDiscovered++;
            string what = Describe(craft);
            QueueFloatingText(thinker.Position, $"Idea: {craft.ToString().ToLowerInvariant()}!", CraftTextColor);
            Game.AddEventLog($"[CRAFT] {thinker.Name} of {group.Title} worked out how to {what}");
            Headline("Discovery", $"{thinker.Name} of {group.Title} worked out how to {what}", thinker.Position, false, group);
            return;
        }
    }

    /// <summary>
    /// What a clan needs before it can work out <paramref name="craft"/>: a
    /// granary takes farming and a House; spears, a hunting tradition or a
    /// Wolf Spider brought down; a palisade, a House and either a martial
    /// tradition or a memory of danger close to home.
    /// </summary>
    private bool ReadyFor(KinGroup group, Craft craft)
    {
        bool hasHouse = false;
        foreach (Shelter home in GroupHomes(group))
            hasHouse |= home is { IsBuilt: true, Tier: ShelterTier.House };
        return craft switch
        {
            Craft.Granary => hasHouse && Knows(group, Craft.Farming),
            Craft.Spears => group.Culture.Hunting >= 0.1f || group.SpidersSlain > 0,
            Craft.Palisade => hasHouse && (group.Culture.Martial >= 0.1f || group.Dangers.Count >= 3),
            _ => false,
        };
    }

    private static string Describe(Craft craft) => craft switch
    {
        Craft.Farming => "grow berry bushes from seed",
        Craft.Granary => "build a granary",
        Craft.Spears => "sharpen twigs into spears",
        Craft.Palisade => "raise a palisade",
        _ => craft.ToString().ToLowerInvariant(),
    };

    /// <summary>An ally teaches the first craft it knows that <paramref name="ally"/> doesn't — farming first (odds <see cref="TeachFarmingChance"/> per decision).</summary>
    private void TryTeachCraft(KinGroup teacher, KinGroup ally)
    {
        Craft missing = CraftsOf(teacher) & ~CraftsOf(ally);
        if (missing == Craft.None || Rng.NextDouble() >= TeachFarmingChance)
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
