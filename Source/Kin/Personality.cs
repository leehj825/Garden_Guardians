using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A Bramblekin's nature: seven traits, each 0..1, rolled once when it
/// wanders in (or inherited from its parents when born) and fixed for life.
/// Each one drives something you can watch it do.
/// </summary>
public readonly struct Personality
{
    /// <summary>Odds of turning on another Bramblekin (to rob it, in a feud, in a war), how hard it hits, and a Leader's appetite for war.</summary>
    public float Aggression { get; }

    /// <summary>Extrovert (high) or introvert (low): seeking out and banding together with others, sharing, courting — or keeping its distance.</summary>
    public float Sociability { get; }

    /// <summary>Scales how far it can detect food, threats and other Bramblekin; a clever mind plans ahead and works out crafts.</summary>
    public float Intelligence { get; }

    /// <summary>Rebellious (high) or obedient (low): how fast its loyalty drains under a Leader it doesn't like, when it stops obeying, and how readily it walks out, splits off or challenges.</summary>
    public float Rebelliousness { get; }

    /// <summary>Persuasive (high) or passive (low): its pull as a Leader — winning the role, keeping followers loyal, drawing a faction after it, winning over neighbours and loners.</summary>
    public float Persuasiveness { get; }

    /// <summary>Brave (high) or cautious (low): standing up to predators and big game rather than fleeing, holding its nerve, and how long it gives a place of danger a wide berth.</summary>
    public float Courage { get; }

    /// <summary>Diligent (high) or idle (low): how briskly it goes about its work, and how long it dawdles and rests between jobs.</summary>
    public float Diligence { get; }

    /// <summary>Born strong (high) or frail (low): how hard it hits and how well it stands a blow. A soldier's job, and a shield, build on it (see Bramblekin.StrikeDamage).</summary>
    public float Strength { get; }

    public Personality(float aggression, float sociability, float intelligence,
                       float rebelliousness = 0.5f, float persuasiveness = 0.5f, float courage = 0.5f, float diligence = 0.5f, float strength = 0.5f)
    {
        Strength = Math.Clamp(strength, 0f, 1f);
        Aggression = Math.Clamp(aggression, 0f, 1f);
        Sociability = Math.Clamp(sociability, 0f, 1f);
        Intelligence = Math.Clamp(intelligence, 0f, 1f);
        Rebelliousness = Math.Clamp(rebelliousness, 0f, 1f);
        Persuasiveness = Math.Clamp(persuasiveness, 0f, 1f);
        Courage = Math.Clamp(courage, 0f, 1f);
        Diligence = Math.Clamp(diligence, 0f, 1f);
    }

    /// <summary>A fresh, uniformly random Personality.</summary>
    public static Personality Roll(Random rng) =>
        new((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(),
            (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());

    /// <summary>How far (±) a child's trait may stray from its parents' average.</summary>
    public const float InheritanceVariation = 0.15f;

    /// <summary>A child's Personality: each trait the average of its parents', give or take up to <see cref="InheritanceVariation"/>.</summary>
    public static Personality Inherit(Personality a, Personality b, Random rng)
    {
        float Mix(float x, float y) => (x + y) / 2f + ((float)rng.NextDouble() * 2f - 1f) * InheritanceVariation;
        return new Personality(
            Mix(a.Aggression, b.Aggression), Mix(a.Sociability, b.Sociability), Mix(a.Intelligence, b.Intelligence),
            Mix(a.Rebelliousness, b.Rebelliousness), Mix(a.Persuasiveness, b.Persuasiveness), Mix(a.Courage, b.Courage),
            Mix(a.Diligence, b.Diligence), Mix(a.Strength, b.Strength));
    }

    /// <summary>A trait this far from the middle (either way) is worth a word — see <see cref="Describe"/>.</summary>
    private const float Notable = 0.2f;

    /// <summary>"brave, persuasive, rebellious" — the words for its strongest leanings (or "even-tempered" if none stand out).</summary>
    public string Describe()
    {
        var words = new List<string>();
        void Word(float value, string high, string low)
        {
            if (value >= 0.5f + Notable)
                words.Add(high);
            else if (value <= 0.5f - Notable)
                words.Add(low);
        }
        Word(Aggression, "aggressive", "gentle");
        Word(Sociability, "extrovert", "introvert");
        Word(Intelligence, "clever", "simple");
        Word(Rebelliousness, "rebellious", "obedient");
        Word(Persuasiveness, "persuasive", "passive");
        Word(Courage, "brave", "cautious");
        Word(Diligence, "diligent", "idle");
        Word(Strength, "strong", "frail");
        return words.Count > 0 ? string.Join(", ", words) : "even-tempered";
    }

    /// <summary>The single most striking word for it ("rebellious"), for the chronicle — or null if nothing stands out.</summary>
    public string? Epithet()
    {
        (float Distance, string Word)[] leanings =
        {
            (Rebelliousness - 0.5f, Rebelliousness > 0.5f ? "rebellious" : "obedient"),
            (Persuasiveness - 0.5f, Persuasiveness > 0.5f ? "persuasive" : "passive"),
            (Courage - 0.5f, Courage > 0.5f ? "brave" : "cautious"),
            (Aggression - 0.5f, Aggression > 0.5f ? "fierce" : "gentle"),
            (Intelligence - 0.5f, Intelligence > 0.5f ? "clever" : "simple"),
            (Diligence - 0.5f, Diligence > 0.5f ? "diligent" : "idle"),
        };
        var strongest = leanings.MaxBy(l => MathF.Abs(l.Distance));
        return MathF.Abs(strongest.Distance) >= Notable + 0.1f ? strongest.Word : null;
    }
}
