using System.Numerics;

namespace GardenGuardians;

// Save/Load: plain data mirrors of the world's lasting state, written as
// JSON by SaveSystem. Anything fleeting — what a Bramblekin is looking at
// this second, a Hornet mid-chase — isn't kept: it picks up again within
// moments of loading.

/// <summary>A Vector3 that System.Text.Json can write.</summary>
public readonly record struct V3(float X, float Y, float Z)
{
    public static implicit operator V3(Vector3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator Vector3(V3 v) => new(v.X, v.Y, v.Z);
}

public readonly record struct PlaceSave(V3 Where, float When);

public sealed class SaveGame
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTime SavedAt { get; set; }

    /// <summary>World counters, timers and stats, by name — see World.Save.</summary>
    public Dictionary<string, double> Numbers { get; set; } = new();
    public Dictionary<string, double[]> NumberArrays { get; set; } = new();

    public List<string> FamilyNames { get; set; } = new();
    public List<PropSave> Props { get; set; } = new();
    public List<V3> BerryPatches { get; set; } = new();
    public List<ShelterSave> Shelters { get; set; } = new();
    public List<KinSave> Kin { get; set; } = new();
    public List<GroupSave> Groups { get; set; } = new();
    public List<BushSave> Bushes { get; set; } = new();

    /// <summary>Loose stones and branches; null in a save from before they were kept.</summary>
    public List<MaterialSave>? Materials { get; set; }

    public List<WellSave> Wells { get; set; } = new();

    /// <summary>Pieces of stone wall; null in a save from before walls.</summary>
    public List<WallSave>? Walls { get; set; }
    public List<VillageSave>? Villages { get; set; }
    public List<SnareSave> Snares { get; set; } = new();

    /// <summary>Aphid pens; null in a save from before herding.</summary>
    public List<PenSave>? Pens { get; set; }
    public List<LooseSave> Food { get; set; } = new();
    public List<LooseSave> Twigs { get; set; } = new();
    public List<RelationSave> Relations { get; set; } = new();
    public List<TributeSave> Tributes { get; set; } = new();
    public List<ChronicleEntry> Chronicle { get; set; } = new();
    public List<HistorySample> History { get; set; } = new();
    public List<LifeRecord> Lives { get; set; } = new();
    public V3? Anthill { get; set; }
    public int AnthillStock { get; set; }
}

public sealed class PropSave
{
    public V3 Position { get; set; }
    public GardenPropKind Kind { get; set; }
    public float Rotation { get; set; }
    public float TwigLength { get; set; }
    public bool IsYellow { get; set; }
    public float Scale { get; set; }
}

public sealed class ShelterSave
{
    public int Id { get; set; }
    public V3 Position { get; set; }
    public ShelterTier Tier { get; set; }
    public bool Built { get; set; }
    public bool Upgrading { get; set; }
    public int Twigs { get; set; }
    public int Stored { get; set; }
    public int? Owner { get; set; }
    public Guid? GroupId { get; set; }
    public float AbandonedSeconds { get; set; }
    public float StageStartedAt { get; set; }
    public bool Granary { get; set; }

    /// <summary>A save from before palisades took branches: true if it had one.</summary>
    public bool Palisade { get; set; }

    public int Stakes { get; set; }
    public int Stones { get; set; }
    public bool Cistern { get; set; }
    public int Water { get; set; }
    public bool Hearth { get; set; }
    public float HearthFuel { get; set; }
}

public sealed class KinSave
{
    public int Id { get; set; }
    public string GivenName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public Sex Sex { get; set; }
    public float Aggression { get; set; }
    public float Sociability { get; set; }
    public float Intelligence { get; set; }
    public float? Rebelliousness { get; set; }
    public float? Persuasiveness { get; set; }
    public float? Courage { get; set; }
    public float? Diligence { get; set; }
    public float? Strength { get; set; }
    public V3 Position { get; set; }
    public int Health { get; set; }
    public float Hunger { get; set; }
    public float Thirst { get; set; }
    public float Age { get; set; }
    public float Lifespan { get; set; }
    public float TimeHere { get; set; }
    public bool BornHere { get; set; }
    public int Generation { get; set; }
    public int? MotherId { get; set; }
    public int? FatherId { get; set; }
    public string? MotherName { get; set; }
    public string? FatherName { get; set; }
    public int? GuardianA { get; set; }
    public int? GuardianB { get; set; }
    public string? GuardianAName { get; set; }
    public string? GuardianBName { get; set; }
    public int Children { get; set; }
    public Guid? GroupId { get; set; }
    public int? Home { get; set; }
    public KinJob Job { get; set; }
    public float Loyalty { get; set; }
    public float Reputation { get; set; }
    public float Infamy { get; set; }
    public bool HasLeftGroup { get; set; }
    public List<Guid> FormerGroups { get; set; } = new();
    public Dictionary<int, RelationshipState> KnownKins { get; set; } = new();
    public int? Partner { get; set; }
    public bool Widowed { get; set; }
    public float Mourning { get; set; }
    public bool KnowsFarming { get; set; }
    public Craft Crafts { get; set; }
    public float JoinedAt { get; set; }
    public List<PlaceSave> Dangers { get; set; } = new();
    public V3? FoodMemory { get; set; }
    public bool CarryingFood { get; set; }
    public ErrandSave? Errand { get; set; }
    public float LeaderSeconds { get; set; }
    public int SpiderKills { get; set; }
    public int ChampionWins { get; set; }
    public float Sickness { get; set; }
    public float Immunity { get; set; }

    /// <summary>Hunting, farming, building and fishing skill; null in a save from before skills.</summary>
    public float[]? Skills { get; set; }
}

public sealed class ErrandSave
{
    public ErrandKind Kind { get; set; }
    public Guid From { get; set; }
    public Guid To { get; set; }
    public int Destination { get; set; }
    public int Load { get; set; }
    public int TwigsOwed { get; set; }
    public int Payment { get; set; }
    public MaterialKind Material { get; set; }
    public bool Returning { get; set; }
}

public sealed class GroupSave
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public int? Leader { get; set; }
    public int? Heir { get; set; }
    public int? Home { get; set; }
    public List<int> Annexes { get; set; } = new();
    public float HomeSiteRetryTimer { get; set; }
    public GroupGoal Goal { get; set; }
    public SharingRule Sharing { get; set; }
    public float BirthCooldown { get; set; }
    public float DecisionTimer { get; set; }
    public V3? SettleTarget { get; set; }
    public int Dowry { get; set; }
    public int? SeedCorn { get; set; }
    public int Cloth { get; set; }
    public int CutStone { get; set; }
    public float NextRaidAt { get; set; }
    public float Martial { get; set; }
    public float Hunting { get; set; }
    public float Farming { get; set; }
    public Tradition Leading { get; set; }
    public int SpidersSlain { get; set; }
    public Belief Belief { get; set; }
    public V3? Shrine { get; set; }
    public float ShrineRaised { get; set; }
    public List<PlaceSave> Dangers { get; set; } = new();
    public List<PlaceSave> FoodSpots { get; set; } = new();
}

public sealed class BushSave
{
    public V3 Position { get; set; }
    public CropKind Kind { get; set; }
    public float Age { get; set; }
    public Guid? GroupId { get; set; }
    public float Growth { get; set; }
    public int Fruit { get; set; }
    public float FruitTimer { get; set; }
    public float WildSeconds { get; set; }
}

public sealed class WellSave
{
    public V3 Position { get; set; }
    public Guid? GroupId { get; set; }
    public int StonesNeeded { get; set; }
    public int StonesLaid { get; set; }
}

public sealed class VillageSave
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public V3 Centre { get; set; }
    public float FoundedAt { get; set; }
    public List<Guid> Clans { get; set; } = new();
    public int? Headman { get; set; }
}

public sealed class WallSave
{
    public V3 Position { get; set; }
    public float Yaw { get; set; }
    public int Kind { get; set; }
    public Guid? GroupId { get; set; }
    public bool Built { get; set; }
    public float Scale { get; set; } = 1f;
    public float Ruin { get; set; }
}

public sealed class SnareSave
{
    public V3 Position { get; set; }
    public Guid? GroupId { get; set; }
    public bool IsSet { get; set; }
}

public sealed class PenSave
{
    public V3 Position { get; set; }
    public Guid? GroupId { get; set; }
    public int Aphids { get; set; }
    public float HoneydewTimer { get; set; }
    public float BreedTimer { get; set; }
}

public sealed class MaterialSave
{
    public V3 Position { get; set; }
    public MaterialKind Kind { get; set; }
    public float DespawnTimer { get; set; }
}

public sealed class LooseSave
{
    public V3 Position { get; set; }
    public FoodShardKind Kind { get; set; }
    public float DespawnTimer { get; set; }
}

public sealed class RelationSave
{
    public Guid First { get; set; }
    public Guid Second { get; set; }
    public GroupStance Stance { get; set; }
    public float Grievance { get; set; }
    public float Since { get; set; }
    public float FirstScore { get; set; }
    public float SecondScore { get; set; }
}

public sealed class TributeSave
{
    public Guid Payer { get; set; }
    public Guid Receiver { get; set; }
    public int SeasonsLeft { get; set; }
    public float NextDue { get; set; }
}
