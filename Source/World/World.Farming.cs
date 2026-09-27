using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>
    /// A settled group without farming works it out at a Leader decision
    /// with odds of this × the Intelligence of its cleverest grown member —
    /// a bright group within a year or two, a dull one much later, if ever.
    /// </summary>
    private const float FarmingInsightChance = 0.008f;

    /// <summary>A group may keep this many bushes per finished House…</summary>
    private const int BushesPerHouse = 2;

    /// <summary>…and this many per finished Tent.</summary>
    private const int BushesPerTent = 1;

    /// <summary>A bush is planted between this…</summary>
    private const float PlantMinDistance = 2.5f;

    /// <summary>…and this far (m) from its planter's home.</summary>
    private const float PlantMaxDistance = 6f;

    /// <summary>No two bushes closer than this (m).</summary>
    private const float BushSpacing = 1.4f;

    /// <summary>Planting takes a berry from the stores as seed.</summary>
    public const int SeedCost = 1;

    /// <summary>Never more than this many bushes on the map, planted and wild.</summary>
    private const int MaxBushes = 40;

    private static readonly Color FarmTextColor = new(70, 150, 60, 255);

    /// <summary>Every berry bush on the map, tended or wild.</summary>
    public List<BerryBush> Bushes { get; } = new();

    public int BushesPlanted { get; private set; }

    /// <summary>Berries picked off bushes.</summary>
    public int FruitHarvested { get; private set; }

    /// <summary>Times a group worked farming out for itself (rather than being taught).</summary>
    public int FarmingDiscoveries { get; private set; }

    /// <summary>Times a group learned farming from an ally.</summary>
    public int FarmingTaught { get; private set; }

    /// <summary>A group knows farming if any of its members does.</summary>
    public static bool KnowsFarming(KinGroup group) => group.Members.Any(m => !m.IsDead && m.KnowsFarming);

    /// <summary>How many bushes <paramref name="group"/> may keep: <see cref="BushesPerHouse"/> per House, <see cref="BushesPerTent"/> per Tent.</summary>
    public int BushAllowance(KinGroup group) =>
        GroupHomes(group).Where(h => h.IsBuilt).Sum(h => h.Tier == ShelterTier.House ? BushesPerHouse : BushesPerTent);

    public int BushesOf(KinGroup group) => Bushes.Count(b => b.GroupId == group.Id);

    /// <summary>True while <paramref name="group"/> has room for another bush and a berry to spare as seed — and it isn't winter.</summary>
    public bool WantsToPlant(KinGroup group) =>
        CurrentSeason != Season.Winter && Bushes.Count < MaxBushes && KnowsFarming(group) &&
        BushesOf(group) < BushAllowance(group) && StoredFood(group) > SeedCost;

    /// <summary>
    /// At each Leader decision: a group that knows farming teaches it to
    /// every member (newcomers, joiners, the young); one that doesn't — but
    /// has a House to farm from — may work it out (see
    /// <see cref="FarmingInsightChance"/>).
    /// </summary>
    private void UpdateFarmingKnowledge(KinGroup group)
    {
        if (KnowsFarming(group))
        {
            foreach (Bramblekin member in group.Members)
                member.LearnFarming();
            return;
        }

        if (!GroupHomes(group).Any(h => h is { IsBuilt: true, Tier: ShelterTier.House }))
            return;
        if (group.Members.Where(m => !m.IsDead && !m.IsYoung).MaxBy(m => m.Personality.Intelligence) is not { } thinker)
            return;
        if (Rng.NextDouble() >= FarmingInsightChance * thinker.Personality.Intelligence)
            return;

        foreach (Bramblekin member in group.Members)
            member.LearnFarming();
        FarmingDiscoveries++;
        QueueFloatingText(thinker.Position, "Idea: farming!", FarmTextColor);
        Game.AddEventLog($"[FARMING] {thinker.Name} of {group.Title} worked out how to grow berry bushes from seed");
    }

    /// <summary>A free spot for a new bush near <paramref name="home"/>: open ground, clear of shelters and other bushes. Null if none turns up.</summary>
    public Vector3? FindPlantingSpot(Shelter home)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float distance = PlantMinDistance + (float)Rng.NextDouble() * (PlantMaxDistance - PlantMinDistance);
            Vector3 spot = Grounded(home.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance);
            if (!Terrain.Contains(spot, 3f) || IsBlocked(spot, BerryBush.Radius + 0.2f))
                continue;
            if (Shelters.Any(s => GroundMover.HorizontalDistance(s.Position, spot) < s.Radius + 1f))
                continue;
            if (Bushes.Any(b => GroundMover.HorizontalDistanceSquared(b.Position, spot) < BushSpacing * BushSpacing))
                continue;
            return spot;
        }
        return null;
    }

    /// <summary>A Farmer plants a bush at <paramref name="spot"/> for <paramref name="group"/>, paying a berry from the stores as seed. Null if the group can't (or needn't) plant after all.</summary>
    public BerryBush? PlantBush(Bramblekin farmer, KinGroup group, Vector3 spot)
    {
        if (!WantsToPlant(group))
            return null;

        bool first = BushesOf(group) == 0;
        TakeFromStores(group, SeedCost, preferred: farmer.Home);
        var bush = new BerryBush(spot, group.Id);
        Bushes.Add(bush);
        BushesPlanted++;
        QueueFloatingText(spot, "Planted", FarmTextColor);
        if (first)
            Game.AddEventLog($"[FARMING] {farmer.Name} planted {group.Title}'s first berry bush");
        return bush;
    }

    /// <summary>The nearest bush of <paramref name="group"/>'s with a ripe berry, within <paramref name="range"/> of <paramref name="kin"/>.</summary>
    public BerryBush? NearestRipeBush(Bramblekin kin, KinGroup group, float range)
    {
        BerryBush? best = null;
        float bestDistanceSquared = range * range;
        foreach (BerryBush bush in Bushes)
        {
            if (bush.GroupId != group.Id || bush.Fruit <= 0)
                continue;
            float distanceSquared = GroundMover.HorizontalDistanceSquared(kin.Position, bush.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = bush;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>Picks a ripe berry off <paramref name="bush"/>, already in hand. Null if there's none left.</summary>
    public FoodShard? PickFruit(BerryBush bush)
    {
        if (!bush.TryPick())
            return null;
        FoodShard? food = ActivateFood(bush.Position, FoodShardKind.Berry);
        if (food is null)
            return null; // Pool exhausted: practically unreachable.
        PickUpFood(food);
        FruitHarvested++;
        return food;
    }

    /// <summary>Takes <paramref name="amount"/> Food from <paramref name="group"/>'s stores, <paramref name="preferred"/>'s first.</summary>
    private void TakeFromStores(KinGroup group, int amount, Shelter? preferred)
    {
        while (amount > 0 && preferred is not null && preferred.TryWithdraw())
            amount--;
        foreach (Shelter home in GroupHomes(group))
        {
            while (amount > 0 && home.TryWithdraw())
                amount--;
        }
    }

    /// <summary>
    /// Bushes grow and ripen at the season's pace; an overripe one drops a
    /// Berry on the ground. A bush whose group is gone runs wild, and
    /// withers after <see cref="BerryBush.WildLifespan"/>.
    /// </summary>
    private void UpdateFarming(float deltaTime)
    {
        float abundance = FoodAbundance;
        for (int i = Bushes.Count - 1; i >= 0; i--)
        {
            BerryBush bush = Bushes[i];
            if (bush.GroupId is { } id && !_groups.ContainsKey(id))
                bush.GroupId = null;
            if (bush.GroupId is null)
            {
                bush.WildSeconds += deltaTime;
                if (bush.IsWithered)
                {
                    Bushes.RemoveAt(i);
                    continue;
                }
            }

            if (bush.Update(deltaTime, abundance))
                ScatterFoodAround(bush.Position, 1, BerryBush.Radius + 0.4f, FoodShardKind.Berry);
        }
    }

    /// <summary>A village's bushes nearest one of its homes go with that home — see <see cref="ProcessBuddings"/> and <see cref="AbsorbHomes"/>.</summary>
    private void HandOverBushes(KinGroup from, KinGroup to, Shelter home, Shelter? fromHome)
    {
        foreach (BerryBush bush in Bushes)
        {
            if (bush.GroupId != from.Id)
                continue;
            float toHome = GroundMover.HorizontalDistanceSquared(bush.Position, home.Position);
            float toOther = fromHome is null ? float.MaxValue : GroundMover.HorizontalDistanceSquared(bush.Position, fromHome.Position);
            if (toHome < toOther)
                bush.GroupId = to.Id;
        }
    }
}
