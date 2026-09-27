namespace GardenGuardians;

/// <summary>A clan's leading tradition — see <see cref="ClanCulture"/>.</summary>
public enum Tradition
{
    None,
    Warlike,
    Hunting,
    Farming,
}

/// <summary>
/// Clan culture: traditions a clan grows into by what it does — going to
/// war and defending itself (Martial), hunting big game (Hunting), tending
/// berry bushes (Farming) — each 0..1, slowly fading if neglected. They
/// outlast any one Leader: a clan's traditions sway its Leaders' decisions
/// whoever they are, nudge its children's natures, and pass to the groups
/// that bud or split off from it. See World.Culture.
/// </summary>
public sealed class ClanCulture
{
    /// <summary>A tradition this strong (and the strongest) is what a clan is known for…</summary>
    public const float KnownFor = 0.4f;

    /// <summary>…until it fades below this…</summary>
    public const float Forgotten = 0.3f;

    /// <summary>…or another overtakes it by this much — so a clan's name doesn't flip back and forth between two close traditions.</summary>
    public const float Overtake = 0.1f;

    public float Martial { get; set; }
    public float Hunting { get; set; }
    public float Farming { get; set; }

    /// <summary>What the clan is known for, if anything — see <see cref="UpdateLeading"/>.</summary>
    public Tradition Leading { get; set; }

    private float Strength(Tradition tradition) => tradition switch
    {
        Tradition.Warlike => Martial,
        Tradition.Hunting => Hunting,
        Tradition.Farming => Farming,
        _ => 0f,
    };

    /// <summary>
    /// Re-weighs what the clan is known for, with some stickiness: the
    /// strongest tradition once it reaches <see cref="KnownFor"/>; a clan
    /// known for one keeps its name until that fades below
    /// <see cref="Forgotten"/> or another beats it by <see cref="Overtake"/>.
    /// Returns true if that changed.
    /// </summary>
    public bool UpdateLeading()
    {
        Tradition strongest = Martial >= Hunting && Martial >= Farming ? Tradition.Warlike
            : Hunting >= Farming ? Tradition.Hunting
            : Tradition.Farming;
        float best = Strength(strongest);
        Tradition next = Leading;
        if (Leading == Tradition.None || Strength(Leading) < Forgotten)
            next = best >= KnownFor ? strongest : Tradition.None;
        else if (strongest != Leading && best >= KnownFor && best >= Strength(Leading) + Overtake)
            next = strongest;
        if (next == Leading)
            return false;
        Leading = next;
        return true;
    }

    /// <summary>"warlike", "hunting" or "farming" (or null) for the HUD and the chronicle.</summary>
    public string? Label => Leading switch
    {
        Tradition.Warlike => "warlike",
        Tradition.Hunting => "hunting",
        Tradition.Farming => "farming",
        _ => null,
    };

    /// <summary>A daughter or splinter clan starts with its parent's traditions.</summary>
    public void CopyFrom(ClanCulture parent)
    {
        Martial = parent.Martial;
        Hunting = parent.Hunting;
        Farming = parent.Farming;
        Leading = parent.Leading;
    }

    /// <summary>A child raised in the clan leans its way: bolder in a warlike or hunting clan, sharper in a farming one.</summary>
    public Personality Nudge(Personality child) => new(
        child.Aggression + 0.12f * Martial + 0.06f * Hunting,
        child.Sociability + 0.06f * Hunting,
        child.Intelligence + 0.12f * Farming);
}
