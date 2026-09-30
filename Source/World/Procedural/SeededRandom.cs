namespace GardenGuardians;

/// <summary>
/// A small random generator that gives the same numbers for the same seed on every platform and .NET version
/// (SplitMix64), so a garden's seed always regrows the same garden.
/// </summary>
public sealed class SeededRandom
{
    private ulong _state;

    public SeededRandom(int seed) => _state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x1234567UL;

    private ulong NextBits()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A number in [0, 1).</summary>
    public double NextDouble() => (NextBits() >> 11) * (1.0 / (1UL << 53));

    /// <summary>A number in [<paramref name="low"/>, <paramref name="high"/>).</summary>
    public float Uniform(float low, float high) => low + (float)NextDouble() * (high - low);

    /// <summary>A whole number in [0, <paramref name="count"/>).</summary>
    public int Next(int count) => (int)(NextDouble() * count);

    /// <summary>A whole number in [<paramref name="low"/>, <paramref name="high"/>).</summary>
    public int Next(int low, int high) => low + Next(high - low);

    /// <summary>A normally distributed number (mean 0, standard deviation 1).</summary>
    public float Normal()
    {
        double u1 = 1.0 - NextDouble(), u2 = NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
