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
    private const float PlantMinDistance = 3.8f;

    /// <summary>…and this far (m) from its planter's home.</summary>
    private const float PlantMaxDistance = 7.5f;

    /// <summary>A mushroom bed goes this close (m) to its House's wall, in the damp shade.</summary>
    private const float MushroomBedReach = 1.2f;

    /// <summary>A cress bed goes on a stretch of shore within this far (m) of its planter's home.</summary>
    public const float CressBedReach = 14f;

    /// <summary>No two crops closer than this (m).</summary>

    /// <summary>Planting takes a piece of food from the stores as seed.</summary>
    public const int SeedCost = 1;

    /// <summary>Never more than this many crops on the map, planted and wild.</summary>
    private static int MaxCrops => Scaled(60);

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
    public static bool KnowsFarming(KinGroup group)
    {
        foreach (Bramblekin member in group.Members)
        {
            if (!member.IsDead && member.KnowsFarming)
                return true;
        }
        return false;
    }

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

    // Plain loops, not LINQ: every Farmer asks these every step.
    public int CropsOf(KinGroup group)
    {
        int count = 0;
        foreach (Crop crop in Crops)
        {
            if (crop.GroupId == group.Id)
                count++;
        }
        return count;
    }

    public int CropsOf(KinGroup group, CropKind kind)
    {
        int count = 0;
        foreach (Crop crop in Crops)
        {
            if (crop.GroupId == group.Id && crop.Kind == kind)
                count++;
        }
        return count;
    }

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
    /// knows how to grow and has ground (or seed) for — grain its seed corn,
    /// mushrooms a House wall, cress a shore within <see cref="CressBedReach"/>
    /// — the one it has fewest of, so its fields spread across the seasons.
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
        Consider(CropKind.Grain, (known & Craft.Grain) != 0 && group.SeedCorn >= GrainSeedCost);
        Consider(CropKind.Mushroom, (known & Craft.Mushrooms) != 0 && home.Tier == ShelterTier.House);
        Consider(CropKind.Cress, (known & Craft.Cress) != 0 && NearestShoreSpot(home.Position, CressBedReach, usual: true, creek: true) is not null);
        return best;
    }

    /// <summary>
    /// True if something of <paramref name="radius"/> (its picture's reach) set down at <paramref name="spot"/> would overlap a crop, an aphid pen
    /// or a well already there, whatever size each has grown to.
    /// </summary>
    private bool OverlapsLayout(Vector3 spot, float radius, float gap = 0.15f) =>
        Crops.Any(c => GroundMover.HorizontalDistance(c.Position, spot) < c.Footprint + radius + gap) ||
        Pens.Any(p => GroundMover.HorizontalDistance(p.Position, spot) < AphidPen.DrawRadius + radius + gap) ||
        Wells.Any(w => GroundMover.HorizontalDistance(w.Position, spot) < Well.DrawRadius + radius + gap);

    /// <summary>A free spot near <paramref name="home"/> for a <paramref name="kind"/> crop: open ground (or shore, for cress), clear of homes and other crops. Null if none turns up.</summary>
    public Vector3? FindPlantingSpot(Shelter home, CropKind kind)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector3 spot;
            if (kind == CropKind.Cress)
            {
                if (RandomShoreSpot(home.Position, CressBedReach, usual: true, creek: true) is not { } shore)
                    return null;
                spot = shore;
            }
            else
            {
                float angle = (float)(Rng.NextDouble() * MathF.Tau);
                float distance = kind == CropKind.Mushroom
                    ? HomeYard(home) + Crop.FootprintFor(kind, wild: false) * 0.85f + 0.1f + (float)Rng.NextDouble() * MushroomBedReach
                    : PlantMinDistance + (float)Rng.NextDouble() * (PlantMaxDistance - PlantMinDistance);
                spot = Grounded(home.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance);
            }

            if (!Terrain.Contains(spot, 3f) || IsBlocked(spot, Crop.Radius + 0.2f) || IsCramped(spot))
                continue;
            if (Shelters.Any(s => IsInHomeYard(s, spot, kind, home)))
                continue;
            if (OverlapsLayout(spot, Crop.FootprintFor(kind, wild: false)))
                continue;
            return spot;
        }
        return null;
    }

    /// <summary>True if <paramref name="spot"/> is too close to <paramref name="shelter"/> for a <paramref name="kind"/> crop: inside its wall, its palisade ring and the yard round it. A mushroom bed may hug its own <paramref name="home"/>.</summary>
    private static bool IsInHomeYard(Shelter shelter, Vector3 spot, CropKind kind, Shelter? home) =>
        GroundMover.HorizontalDistance(shelter.Position, spot) < HomeYard(shelter) + Crop.FootprintFor(kind, wild: false) * 0.85f;

    /// <summary>How far from a home's middle nothing else may stand: its palisade ring (which it may or may not have yet), with a little air.</summary>
    private static float HomeYard(Shelter shelter) => shelter.PalisadeRadius + 0.3f;

    private float _yardSweepTimer;

    /// <summary>
    /// Every few seconds, whatever was set down where something else now stands is cleared: a crop is ploughed under if it overlaps a home
    /// (and its palisade ring), a well, an aphid pen or a bigger crop, and an aphid pen is moved off a home, a well or the plots. Homes grow
    /// (tent to house) and gain palisades after a crop is in, so this keeps everything built apart, whenever it was built.
    /// </summary>
    private void ClearHomeYards(float deltaTime)
    {
        _yardSweepTimer -= deltaTime;
        if (_yardSweepTimer > 0f)
            return;
        _yardSweepTimer = 5f;

        var homes = Shelters.Where(s => !s.IsCollapsed).ToList();
        for (int i = Crops.Count - 1; i >= 0; i--)
        {
            Crop crop = Crops[i];
            float reach = crop.Footprint * 0.85f;
            bool overlaps =
                homes.Any(s => GroundMover.HorizontalDistance(s.Position, crop.Position) < HomeYard(s) + reach) ||
                Wells.Any(w => GroundMover.HorizontalDistance(w.Position, crop.Position) < Well.DrawRadius + reach) ||
                Pens.Any(p => GroundMover.HorizontalDistance(p.Position, crop.Position) < AphidPen.DrawRadius + reach) ||
                Crops.Any(o => o != crop && GroundMover.HorizontalDistance(o.Position, crop.Position) < (o.Footprint + crop.Footprint) * 0.8f &&
                               (o.Growth > crop.Growth || (o.Growth == crop.Growth && Crops.IndexOf(o) < i)));
            if (overlaps)
                Crops.RemoveAt(i);
        }

        foreach (AphidPen pen in Pens)
        {
            bool overlaps =
                homes.Any(s => GroundMover.HorizontalDistance(s.Position, pen.Position) < HomeYard(s) + AphidPen.DrawRadius * 0.85f) ||
                Wells.Any(w => GroundMover.HorizontalDistance(w.Position, pen.Position) < Well.DrawRadius + AphidPen.DrawRadius * 0.85f) ||
                Pens.Any(o => o != pen && GroundMover.HorizontalDistance(o.Position, pen.Position) < AphidPen.DrawRadius * 1.7f && Pens.IndexOf(o) < Pens.IndexOf(pen));
            if (overlaps && FindPenSpot(pen.Position, pen) is { } spot)
                pen.Position = Grounded(spot);
        }
    }

    /// <summary>A Farmer plants a <paramref name="kind"/> crop at <paramref name="spot"/> for <paramref name="group"/>, paying a piece of food from the stores as seed — or, for grain, <see cref="GrainSeedCost"/> of its seed corn. Null if the group can't (or needn't) plant after all.</summary>
    public Crop? PlantCrop(Bramblekin farmer, KinGroup group, Vector3 spot, CropKind kind)
    {
        if (!WantsToPlant(group))
            return null;

        bool first = CropsOf(group, kind) == 0;
        if (kind == CropKind.Grain)
        {
            if (!SowSeedCorn(group))
                return null;
        }
        else
        {
            TakeFromStores(group, SeedCost, preferred: farmer.Home);
        }
        var crop = new Crop(spot, group.Id, kind) { IsWatered = IsWatered(spot, kind) };
        Crops.Add(crop);
        BushesPlanted++;
        QueueFloatingText(spot, "Planted", FarmTextColor);
        if (first)
            Game.AddEventLog($"[FARMING] {farmer.Name} planted {group.Title}'s first {crop.Name}");
        return crop;
    }

    /// <summary>True if a <paramref name="kind"/> crop at <paramref name="spot"/> would be watered: cress always is; anything else within <see cref="WateredReach"/> of the pond, or beside a dug well.</summary>
    private bool IsWatered(Vector3 spot, CropKind kind) => kind == CropKind.Cress || IsWaterWithin(spot, WateredReach) || IsWellNear(spot);

    /// <summary>Each clan's crops with something ripe, in <see cref="Crops"/> order — see <see cref="IndexRipeCrops"/>.</summary>
    private readonly Dictionary<Guid, List<Crop>> _ripeCrops = new();

    /// <summary>
    /// Works out each clan's ripe crops once a step, just before the
    /// Bramblekin move — rather than every farmer, gatherer and hungry
    /// member scanning every crop in the garden, several times a step. Crops
    /// only ripen, change hands or appear ripe outside that part of the
    /// step, so this is exactly the scan it replaces (a crop picked bare
    /// meanwhile is skipped below).
    /// </summary>
    private void IndexRipeCrops()
    {
        foreach (List<Crop> ripe in _ripeCrops.Values)
            ripe.Clear();
        foreach (Crop crop in Crops)
        {
            if (crop.GroupId is not { } id || crop.Fruit <= 0)
                continue;
            if (!_ripeCrops.TryGetValue(id, out List<Crop>? ripe))
                _ripeCrops[id] = ripe = new List<Crop>();
            ripe.Add(crop);
        }
    }

    /// <summary>The nearest crop of <paramref name="group"/>'s with something ripe, within <paramref name="range"/> of <paramref name="kin"/>.</summary>
    public Crop? NearestRipeCrop(Bramblekin kin, KinGroup group, float range)
    {
        Crop? best = null;
        float bestDistanceSquared = range * range;
        if (!_ripeCrops.TryGetValue(group.Id, out List<Crop>? ripe))
            return null;
        foreach (Crop crop in ripe)
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
        ClearHomeYards(deltaTime);
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
