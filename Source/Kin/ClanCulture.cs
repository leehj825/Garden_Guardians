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

    /// <summary>The value a clan's tradition draws its children's traits toward — never all the way to the limit, so a clan keeps its variety over the generations.</summary>
    private const float Ideal = 0.8f;

    /// <summary>
    /// A child raised in the clan leans its way: fiercer and braver in a
    /// warlike clan, braver and more sociable in a hunting one, sharper and
    /// harder-working in a farming one. Each is a pull part of the way
    /// toward <see cref="Ideal"/>, not a fixed push, so a trait favoured
    /// generation after generation settles there instead of piling up at 1.
    /// </summary>
    public Personality Nudge(Personality child)
    {
        static float Toward(float value, float pull) => value + Math.Clamp(pull, 0f, 0.5f) * (Ideal - value);
        return new Personality(
            Toward(child.Aggression, 0.4f * Martial + 0.2f * Hunting),
            Toward(child.Sociability, 0.2f * Hunting),
            Toward(child.Intelligence, 0.4f * Farming),
            child.Rebelliousness,
            child.Persuasiveness,
            Toward(child.Courage, 0.27f * Martial + 0.33f * Hunting),
            Toward(child.Diligence, 0.33f * Farming),
            Toward(child.Strength, 0.25f * Martial));
    }
}
