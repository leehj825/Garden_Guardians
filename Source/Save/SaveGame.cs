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
    public List<LooseSave> Food { get; set; } = new();
    public List<LooseSave> Twigs { get; set; } = new();
    public List<RelationSave> Relations { get; set; } = new();
    public List<TributeSave> Tributes { get; set; } = new();
    public List<ChronicleEntry> Chronicle { get; set; } = new();
    public List<HistorySample> History { get; set; } = new();
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
    public V3 Position { get; set; }
    public int Health { get; set; }
    public float Hunger { get; set; }
    public float Age { get; set; }
    public float Lifespan { get; set; }
    public float TimeHere { get; set; }
    public bool BornHere { get; set; }
    public int Generation { get; set; }
    public int? MotherId { get; set; }
    public int? FatherId { get; set; }
    public string? MotherName { get; set; }
    public string? FatherName { get; set; }
    public int Children { get; set; }
    public Guid? GroupId { get; set; }
    public int? Home { get; set; }
    public KinJob Job { get; set; }
    public float Loyalty { get; set; }
    public float Reputation { get; set; }
    public bool HasLeftGroup { get; set; }
    public List<Guid> FormerGroups { get; set; } = new();
    public Dictionary<int, RelationshipState> KnownKins { get; set; } = new();
    public int? Partner { get; set; }
    public bool Widowed { get; set; }
    public float Mourning { get; set; }
    public bool KnowsFarming { get; set; }
    public float JoinedAt { get; set; }
    public List<PlaceSave> Dangers { get; set; } = new();
    public V3? FoodMemory { get; set; }
    public bool CarryingFood { get; set; }
    public ErrandSave? Errand { get; set; }
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
    public bool Returning { get; set; }
}

public sealed class GroupSave
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public int? Leader { get; set; }
    public int? Home { get; set; }
    public List<int> Annexes { get; set; } = new();
    public float HomeSiteRetryTimer { get; set; }
    public GroupGoal Goal { get; set; }
    public SharingRule Sharing { get; set; }
    public float BirthCooldown { get; set; }
    public float DecisionTimer { get; set; }
    public V3? SettleTarget { get; set; }
    public int Dowry { get; set; }
    public float NextRaidAt { get; set; }
    public float Martial { get; set; }
    public float Hunting { get; set; }
    public float Farming { get; set; }
    public List<PlaceSave> Dangers { get; set; } = new();
    public List<PlaceSave> FoodSpots { get; set; } = new();
}

public sealed class BushSave
{
    public V3 Position { get; set; }
    public Guid? GroupId { get; set; }
    public float Growth { get; set; }
    public int Fruit { get; set; }
    public float FruitTimer { get; set; }
    public float WildSeconds { get; set; }
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
