using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A Bramblekin's DNA: three traits, each 0..1, rolled once when it's
/// spawned into the world and fixed for life.
/// </summary>
public readonly struct Personality
{
    /// <summary>Odds of fighting rather than fleeing a threat, of turning on another Bramblekin when starving, and how hard it hits.</summary>
    public float Aggression { get; }

    /// <summary>Desire to seek out and band together with others (high) versus keeping its distance (low).</summary>
    public float Sociability { get; }

    /// <summary>Scales how far it can detect food, threats and other Bramblekin; the sharpest member of a group leads it.</summary>
    public float Intelligence { get; }

    public Personality(float aggression, float sociability, float intelligence)
    {
        Aggression = Math.Clamp(aggression, 0f, 1f);
        Sociability = Math.Clamp(sociability, 0f, 1f);
        Intelligence = Math.Clamp(intelligence, 0f, 1f);
    }

    /// <summary>A fresh, uniformly random Personality.</summary>
    public static Personality Roll(Random rng) =>
        new((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());

    /// <summary>How far (±) a child's trait may stray from its parents' average.</summary>
    public const float InheritanceVariation = 0.15f;

    /// <summary>A child's Personality: each trait the average of its parents', give or take up to <see cref="InheritanceVariation"/>.</summary>
    public static Personality Inherit(Personality a, Personality b, Random rng)
    {
        float Mix(float x, float y) => (x + y) / 2f + ((float)rng.NextDouble() * 2f - 1f) * InheritanceVariation;
        return new Personality(Mix(a.Aggression, b.Aggression), Mix(a.Sociability, b.Sociability), Mix(a.Intelligence, b.Intelligence));
    }
}
