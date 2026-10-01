namespace GardenGuardians;

/// <summary>
/// A kingdom: three or more allied villages under a king (see World.Realms and Garden_Guardians_Society_Design.md, section 4.4). One village is
/// its capital and its headman is the king; the others are vassal villages that send tribute to the capital's stores and pledge soldiers to
/// each other's defence. It dissolves if fewer than three villages remain.
/// </summary>
public sealed class Kingdom
{
    public Kingdom(Guid id, string name, Guid capital, float foundedAt)
    {
        Id = id;
        Name = name;
        Capital = capital;
        FoundedAt = foundedAt;
    }

    public Guid Id { get; }

    /// <summary>"Thornreach" — the realm's own name.</summary>
    public string Name { get; set; }

    /// <summary>The capital village (by ID).</summary>
    public Guid Capital { get; set; }

    /// <summary>The Bramblekin (by ID) wearing the crown — the capital's headman — or null while there is none.</summary>
    public int? KingId { get; set; }

    public float FoundedAt { get; }

    /// <summary>Every village in it, the capital included.</summary>
    public List<Guid> VillageIds { get; } = new();

    /// <summary>Pieces of food vassal villages have sent to the capital's stores, in all.</summary>
    public int TributePaid { get; set; }

    /// <summary>Soldiers of its villages sworn to march to a sister village's defence, now.</summary>
    public int Pledged { get; set; }

    /// <summary>Counts the look-overs, so a vassal pays tribute every few.</summary>
    public int Looks { get; set; }
}
