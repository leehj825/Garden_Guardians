using System.Diagnostics;

namespace GardenGuardians;

/// <summary>
/// A development aid: with GARDEN_PROFILE set, <see cref="Mark"/> adds the time since the last mark to a named bucket
/// and <see cref="Report"/> prints where a run's time went. Costs one bool check when it's off.
/// </summary>
public static class Prof
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("GARDEN_PROFILE") is not null;

    private static readonly Dictionary<string, double> Totals = new();
    private static long _last;

    public static void Begin()
    {
        if (Enabled)
            _last = Stopwatch.GetTimestamp();
    }

    public static void Mark(string name)
    {
        if (!Enabled)
            return;
        long now = Stopwatch.GetTimestamp();
        Totals[name] = Totals.GetValueOrDefault(name) + Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        _last = now;
    }

    public static ProfScope Scope(string name) => Enabled ? new ProfScope(name) : default;

    public static void Add(string name, double ms) => Totals[name] = Totals.GetValueOrDefault(name) + ms;

    public static void Report(int steps)
    {
        if (!Enabled)
            return;
        double all = Totals.Values.Sum();
        Console.WriteLine($"Profile over {steps} steps: {all / steps:0.000} ms per step; allocated {GC.GetTotalAllocatedBytes() / 1048576} MB ({GC.GetTotalAllocatedBytes() / steps} bytes per step), GCs {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
        foreach (var (name, ms) in Totals.OrderByDescending(t => t.Value).Take(25))
            Console.WriteLine($"  {name,-28} {ms / steps,8:0.0000} ms/step {100 * ms / all,5:0.0}%");
    }
}

/// <summary>Times a block (inclusive of what it calls): <c>using var _ = Prof.Scope("name");</c>.</summary>
public readonly struct ProfScope : IDisposable
{
    private readonly string? _name;
    private readonly long _start;

    public ProfScope(string name)
    {
        _name = name;
        _start = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        if (_name is not null)
            Prof.Add(_name, Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
    }
}
