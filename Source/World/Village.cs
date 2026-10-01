using System.Numerics;

namespace GardenGuardians;

/// <summary>
/// A village: homes standing close together, of one clan or several, with a name, a middle and a headman (see World.Villages and
/// Garden_Guardians_Society_Design.md). It is worked out afresh from where the homes stand every few seconds, so it forms, grows,
/// merges and dissolves with them; what it keeps of its own is its name, its founding time and its headman.
/// </summary>
public sealed class Village
{
    public Village(Guid id, string name, Vector3 centre, float foundedAt)
    {
        Id = id;
        Name = name;
        Centre = centre;
        FoundedAt = foundedAt;
    }

    public Guid Id { get; }

    /// <summary>"Mossbridge" — the village's own name (not a clan's).</summary>
    public string Name { get; set; }

    /// <summary>The middle of its homes, on the ground.</summary>
    public Vector3 Centre { get; set; }

    /// <summary>World seconds when it was founded.</summary>
    public float FoundedAt { get; }

    /// <summary>The clans with a home in it (one or several).</summary>
    public List<Guid> ClanIds { get; } = new();

    /// <summary>The Bramblekin (by ID) leading the village — one of its clans' chiefs — or null while there is none.</summary>
    public int? HeadmanId { get; set; }

    /// <summary>How many homes it has.</summary>
    public int Homes { get; set; }

    /// <summary>Food in its homes' stores now (the village's pooled store: every home of its clans).</summary>
    public int Stock { get; set; }

    /// <summary>Pieces of food a second its people have been bringing in lately (smoothed over about a minute and a half).</summary>
    public float IncomeRate { get; set; }

    /// <summary>How many of its soldiers it can feed (see World.Rations), and how many it does.</summary>
    public int AllowedPaid { get; set; }
    public int Paid { get; set; }

    /// <summary>Meals its paid soldiers have eaten from its stores.</summary>
    public int RationMeals { get; set; }

    /// <summary>The headman's own clan.</summary>
    public Guid? HeadmanClan { get; set; }
}
