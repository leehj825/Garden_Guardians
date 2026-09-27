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
    /// <summary>A tradition this strong (and the strongest) is what a clan is known for.</summary>
    public const float KnownFor = 0.4f;

    public float Martial { get; set; }
    public float Hunting { get; set; }
    public float Farming { get; set; }

    /// <summary>What the clan is known for, if anything.</summary>
    public Tradition Leading
    {
        get
        {
            float best = MathF.Max(Martial, MathF.Max(Hunting, Farming));
            if (best < KnownFor)
                return Tradition.None;
            return best == Martial ? Tradition.Warlike : best == Hunting ? Tradition.Hunting : Tradition.Farming;
        }
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
    }

    /// <summary>A child raised in the clan leans its way: bolder in a warlike or hunting clan, sharper in a farming one.</summary>
    public Personality Nudge(Personality child) => new(
        child.Aggression + 0.12f * Martial + 0.06f * Hunting,
        child.Sociability + 0.06f * Hunting,
        child.Intelligence + 0.12f * Farming);
}
