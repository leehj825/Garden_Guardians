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

    /// <summary>A clan may keep this many crops per finished House…</summary>
    private const int CropsPerHouse = 2;

    /// <summary>…and this many per finished Tent.</summary>
    private const int CropsPerTent = 1;

    /// <summary>A berry bush or grain patch is planted between this…</summary>
    private const float PlantMinDistance = 2.5f;

    /// <summary>…and this far (m) from its planter's home.</summary>
    private const float PlantMaxDistance = 6f;

    /// <summary>A mushroom bed goes this close (m) to its House's wall, in the damp shade.</summary>
    private const float MushroomBedReach = 1.2f;

    /// <summary>A cress bed goes on a stretch of shore within this far (m) of its planter's home.</summary>
    public const float CressBedReach = 14f;

    /// <summary>No two crops closer than this (m).</summary>
    private const float CropSpacing = 1.4f;

    /// <summary>Planting takes a piece of food from the stores as seed.</summary>
    public const int SeedCost = 1;

    /// <summary>Never more than this many crops on the map, planted and wild.</summary>
    private const int MaxCrops = 60;

    /// <summary>A crop this close (m) to the pond is watered: it grows and bears this much faster…</summary>
    private const float WateredReach = 8f;

    private const float WateredPace = 1.25f;

    private static readonly Color FarmTextColor = new(70, 150, 60, 255);

    /// <summary>Every crop on the map, tended or wild.</summary>
    public List<Crop> Crops { get; } = new();

    /// <summary>Crops planted (the name is from when they were all berry bushes).</summary>
    public int BushesPlanted { get; private set; }

    /// <summary>Pieces picked off crops.</summary>
    public int FruitHarvested { get; private set; }

    /// <summary>Times a group worked farming out for itself (rather than being taught).</summary>
    public int FarmingDiscoveries { get; private set; }

    /// <summary>Times a group learned farming from an ally.</summary>
    public int FarmingTaught { get; private set; }

    /// <summary>A group knows farming if any of its members does.</summary>
    public static bool KnowsFarming(KinGroup group) => group.Members.Any(m => !m.IsDead && m.KnowsFarming);

    /// <summary>How many crops <paramref name="group"/> may keep: <see cref="CropsPerHouse"/> per House, <see cref="CropsPerTent"/> per Tent.</summary>
    public int CropAllowance(KinGroup group)
    {
        int allowance = 0;
        foreach (Shelter home in GroupHomes(group))
        {
            if (home.IsBuilt)
                allowance += home.Tier == ShelterTier.House ? CropsPerHouse : CropsPerTent;
        }
        return allowance;
    }

    public int CropsOf(KinGroup group) => Crops.Count(c => c.GroupId == group.Id);

    public int CropsOf(KinGroup group, CropKind kind) => Crops.Count(c => c.GroupId == group.Id && c.Kind == kind);

    /// <summary>True while <paramref name="group"/> has room for another crop and a piece of food to spare as seed — and it isn't winter.</summary>
    public bool WantsToPlant(KinGroup group) =>
        CurrentSeason != Season.Winter && Crops.Count < MaxCrops && KnowsFarming(group) &&
        CropsOf(group) < CropAllowance(group) && StoredFood(group) > SeedCost;

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
        Headline("Farming", $"{thinker.Name} of {group.Title} worked out how to grow berry bushes from seed", thinker.Position, false, group);
    }

    /// <summary>
    /// What a clan plants next at <paramref name="home"/>: of the crops it
    /// knows how to grow and has ground for — mushrooms need a House wall,
    /// cress a shore within <see cref="CressBedReach"/> — the one it has
    /// fewest of, so its fields spread across the seasons.
    /// </summary>
    public CropKind ChooseCrop(KinGroup group, Shelter home)
    {
        Craft known = CraftsOf(group);
        CropKind best = CropKind.Berry;
        int fewest = CropsOf(group, CropKind.Berry);
        void Consider(CropKind kind, bool possible)
        {
            if (!possible)
                return;
            int count = CropsOf(group, kind);
            if (count < fewest)
            {
                best = kind;
                fewest = count;
            }
        }
        Consider(CropKind.Grain, (known & Craft.Grain) != 0);
        Consider(CropKind.Mushroom, (known & Craft.Mushrooms) != 0 && home.Tier == ShelterTier.House);
        Consider(CropKind.Cress, (known & Craft.Cress) != 0 && NearestShoreSpot(home.Position, CressBedReach) is not null);
        return best;
    }

    /// <summary>A free spot near <paramref name="home"/> for a <paramref name="kind"/> crop: open ground (or shore, for cress), clear of homes and other crops. Null if none turns up.</summary>
    public Vector3? FindPlantingSpot(Shelter home, CropKind kind)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector3 spot;
            if (kind == CropKind.Cress)
            {
                if (RandomShoreSpot(home.Position, CressBedReach) is not { } shore)
                    return null;
                spot = shore;
            }
            else
            {
                float angle = (float)(Rng.NextDouble() * MathF.Tau);
                float distance = kind == CropKind.Mushroom
                    ? home.Radius + Crop.Radius + 0.1f + (float)Rng.NextDouble() * MushroomBedReach
                    : PlantMinDistance + (float)Rng.NextDouble() * (PlantMaxDistance - PlantMinDistance);
                spot = Grounded(home.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance);
            }

            if (!Terrain.Contains(spot, 3f) || IsBlocked(spot, Crop.Radius + 0.2f))
                continue;
            if (Shelters.Any(s => GroundMover.HorizontalDistance(s.Position, spot) < s.Radius + (s == home && kind == CropKind.Mushroom ? Crop.Radius : 1f)))
                continue;
            if (Crops.Any(c => GroundMover.HorizontalDistanceSquared(c.Position, spot) < CropSpacing * CropSpacing))
                continue;
            return spot;
        }
        return null;
    }

    /// <summary>A Farmer plants a <paramref name="kind"/> crop at <paramref name="spot"/> for <paramref name="group"/>, paying a piece of food from the stores as seed. Null if the group can't (or needn't) plant after all.</summary>
    public Crop? PlantCrop(Bramblekin farmer, KinGroup group, Vector3 spot, CropKind kind)
    {
        if (!WantsToPlant(group))
            return null;

        bool first = CropsOf(group, kind) == 0;
        TakeFromStores(group, SeedCost, preferred: farmer.Home);
        var crop = new Crop(spot, group.Id, kind) { IsWatered = IsWatered(spot, kind) };
        Crops.Add(crop);
        BushesPlanted++;
        QueueFloatingText(spot, "Planted", FarmTextColor);
        if (first)
            Game.AddEventLog($"[FARMING] {farmer.Name} planted {group.Title}'s first {crop.Name}");
        return crop;
    }

    /// <summary>True if a <paramref name="kind"/> crop at <paramref name="spot"/> would be watered: cress always is; anything else within <see cref="WateredReach"/> of the pond.</summary>
    private static bool IsWatered(Vector3 spot, CropKind kind) => kind == CropKind.Cress || IsWaterWithin(spot, WateredReach);

    /// <summary>The nearest crop of <paramref name="group"/>'s with something ripe, within <paramref name="range"/> of <paramref name="kin"/>.</summary>
    public Crop? NearestRipeCrop(Bramblekin kin, KinGroup group, float range)
    {
        Crop? best = null;
        float bestDistanceSquared = range * range;
        foreach (Crop crop in Crops)
        {
            if (crop.GroupId != group.Id || crop.Fruit <= 0)
                continue;
            float distanceSquared = GroundMover.HorizontalDistanceSquared(kin.Position, crop.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = crop;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>Picks a ripe piece off <paramref name="crop"/>, already in hand. Null if there's none left.</summary>
    public FoodShard? PickFruit(Crop crop)
    {
        if (!crop.TryPick())
            return null;
        FoodShard? food = ActivateFood(crop.Position, crop.Yields);
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
    /// How fast <paramref name="crop"/> grows and bears right now: its kind's
    /// pace through the year (see <see cref="Crop.SeasonPace"/>) times the
    /// weather's — a watered crop a quarter again as fast, and untouched
    /// by a drought.
    /// </summary>
    private float CropPace(Crop crop)
    {
        float weather = crop.IsWatered && CurrentWeather == Weather.Drought ? 1f : WeatherFoodFactor;
        return Crop.SeasonPace(crop.Kind, CurrentSeason) * weather * (crop.IsWatered ? WateredPace : 1f);
    }

    /// <summary>
    /// Crops grow and ripen at their own pace (see <see cref="CropPace"/>);
    /// an overripe one drops what it bore on the ground. Each wears out in
    /// the end (see <see cref="Crop.Lifespan"/>), making room for a fresh
    /// one; a crop whose clan is gone runs wild, and withers after
    /// <see cref="Crop.WildLifespan"/>.
    /// </summary>
    private void UpdateFarming(float deltaTime)
    {
        for (int i = Crops.Count - 1; i >= 0; i--)
        {
            Crop crop = Crops[i];
            crop.Age += deltaTime;
            if (crop.IsWornOut)
            {
                Crops.RemoveAt(i);
                continue;
            }
            if (crop.GroupId is { } id && !_groups.ContainsKey(id))
                crop.GroupId = null;
            if (crop.GroupId is null)
            {
                crop.WildSeconds += deltaTime;
                if (crop.IsWithered)
                {
                    Crops.RemoveAt(i);
                    continue;
                }
            }

            if (crop.Update(deltaTime, CropPace(crop)))
                ScatterFoodAround(crop.Position, 1, Crop.Radius + 0.4f, crop.Yields);
        }
    }

    /// <summary>A village's crops nearest one of its homes go with that home — see <see cref="ProcessBuddings"/> and <see cref="AbsorbHomes"/>.</summary>
    private void HandOverCrops(KinGroup from, KinGroup to, Shelter home, Shelter? fromHome)
    {
        foreach (Crop crop in Crops)
        {
            if (crop.GroupId != from.Id)
                continue;
            float toHome = GroundMover.HorizontalDistanceSquared(crop.Position, home.Position);
            float toOther = fromHome is null ? float.MaxValue : GroundMover.HorizontalDistanceSquared(crop.Position, fromHome.Position);
            if (toHome < toOther)
                crop.GroupId = to.Id;
        }
    }
}
