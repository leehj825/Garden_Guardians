using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Seed corn -------------------------------------------------------------------------

    /// <summary>Sowing a grain patch takes this much seed corn (not store food, as other crops do).</summary>
    public const int GrainSeedCost = 2;

    /// <summary>A clan that works out (or is taught) how to sow grain starts with this much seed, gathered from the wild grass.</summary>
    private const int FirstSeedCorn = 4;

    private static readonly Color SeedTextColor = new(200, 170, 80, 255);

    /// <summary>Grain (and wild seed) set aside as seed corn, rather than stored to eat.</summary>
    public int SeedCornKept { get; private set; }

    /// <summary>Seed corn sown into grain patches.</summary>
    public int SeedCornSown { get; private set; }

    /// <summary>Seed corn eaten by the starving.</summary>
    public int SeedCornEaten { get; private set; }

    /// <summary>
    /// How much seed corn <paramref name="group"/> wants to keep: enough to
    /// sow its share of grain patches (its crop allowance spread over the
    /// kinds it grows), and one patch more.
    /// </summary>
    public int SeedCornTarget(KinGroup group)
    {
        Craft known = CraftsOf(group);
        if ((known & Craft.Grain) == 0)
            return 0;
        int kinds = 2 + ((known & Craft.Mushrooms) != 0 ? 1 : 0) + ((known & Craft.Cress) != 0 ? 1 : 0);
        int patches = (CropAllowance(group) + kinds - 1) / kinds;
        return GrainSeedCost * (patches + 1);
    }

    /// <summary>A clan has just learned to sow grain: its first seed corn, gathered from the wild grass.</summary>
    private void LearnedGrain(KinGroup group)
    {
        group.SeedCorn = Math.Max(group.SeedCorn, FirstSeedCorn);
    }

    /// <summary>A piece of seed brought home goes into the seed corn instead of the store, while the clan (sowing grain) is short of it. True if it did.</summary>
    private bool TryKeepSeedCorn(Shelter shelter, FoodShard food)
    {
        if (food.Kind != FoodShardKind.Seed || shelter.GroupId is not { } id || !_groups.TryGetValue(id, out KinGroup? group) ||
            group.SeedCorn >= SeedCornTarget(group))
            return false;
        group.SeedCorn++;
        SeedCornKept++;
        _foodByKind[(int)food.Kind]++;
        food.Deactivate();
        return true;
    }

    /// <summary>Sows a grain patch from <paramref name="group"/>'s seed corn. False if there isn't enough.</summary>
    private bool SowSeedCorn(KinGroup group)
    {
        if (group.SeedCorn < GrainSeedCost)
            return false;
        group.SeedCorn -= GrainSeedCost;
        SeedCornSown += GrainSeedCost;
        return true;
    }

    /// <summary>The home a starving member of a clan with seed corn left can eat it at — the last food there is. Null if there's none.</summary>
    public Shelter? SeedCornLoft(Bramblekin kin) =>
        kin.IsStarving && GroupOf(kin) is { SeedCorn: > 0 } && kin.Home is { IsBuilt: true, IsCollapsed: false } home ? home : null;

    /// <summary>Famine: a starving Bramblekin eats a piece of its clan's seed corn (already in hand) — and next year's grain with it. Null if there's none.</summary>
    public FoodShard? EatSeedCorn(Bramblekin kin, Shelter home)
    {
        if (!kin.IsStarving || GroupOf(kin) is not { SeedCorn: > 0 } group)
            return null;
        FoodShard? food = ActivateFood(home.Position, FoodShardKind.Seed);
        if (food is null)
            return null;
        PickUpFood(food);
        group.SeedCorn--;
        SeedCornEaten++;
        if (group.SeedCorn == 0)
        {
            QueueFloatingText(home.Position, "Seed corn gone", SeedTextColor);
            Game.AddEventLog($"[FAMINE] {group.CapitalTitle} ate the last of their seed corn - there'll be no grain to sow");
        }
        return food;
    }
}
