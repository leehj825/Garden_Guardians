using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>One line of the garden's history — see World.Chronicle.</summary>
public sealed record ChronicleEntry(float Time, int Year, Season Season, Guid[] Clans, string Text);

/// <summary>A snapshot of the garden, taken every <see cref="World.HistoryInterval"/> seconds for the history chart.</summary>
public readonly record struct HistorySample(float Time, int Population, int Groups, int FarmingGroups, int Bushes, int Alliances, int Wars, int Stored);

public sealed partial class World
{
    /// <summary>Seconds between history snapshots.</summary>
    public const float HistoryInterval = 30f;

    /// <summary>The chronicle keeps at most this many entries (the oldest go first).</summary>
    private const int ChronicleCapacity = 4000;

    private float _historyTimer;

    /// <summary>
    /// The Chronicle: the story of the garden's clans — foundings and
    /// endings, Leaders, wars and peaces, alliances, conquests, farming, new
    /// villages, famous hunts and notable deaths — plus the seasons'
    /// weather. Shown on the History screen (see Game.DrawChronicle).
    /// </summary>
    public List<ChronicleEntry> ChronicleEntries { get; } = new();

    /// <summary>The garden over time, for the history chart.</summary>
    public List<HistorySample> History { get; } = new();

    /// <summary>Adds <paramref name="text"/> to the chronicle, filed under each of <paramref name="clans"/> (none for garden-wide news).</summary>
    public void Chronicle(string text, params KinGroup?[] clans)
    {
        Guid[] ids = clans.Where(c => c is not null).Select(c => c!.Id).Distinct().ToArray();
        ChronicleEntries.Add(new ChronicleEntry(ElapsedSeconds, Year, CurrentSeason, ids, text));
        if (ChronicleEntries.Count > ChronicleCapacity)
            ChronicleEntries.RemoveAt(0);
    }

    /// <summary>Entries filed under <paramref name="clan"/>, oldest first.</summary>
    public IEnumerable<ChronicleEntry> ChronicleOf(Guid clan) => ChronicleEntries.Where(e => e.Clans.Contains(clan));

    /// <summary>When <paramref name="clan"/> was founded (its first chronicle entry), if the chronicle remembers.</summary>
    public ChronicleEntry? FoundingOf(Guid clan) => ChronicleEntries.FirstOrDefault(e => e.Clans.Contains(clan));

    private void UpdateHistory(float deltaTime)
    {
        _historyTimer -= deltaTime;
        if (_historyTimer > 0f)
            return;
        _historyTimer = HistoryInterval;
        History.Add(new HistorySample(
            ElapsedSeconds,
            Colony.Count(k => !k.IsDead),
            _groups.Count,
            _groups.Values.Count(KnowsFarming),
            Bushes.Count,
            CurrentAlliances,
            CurrentWars,
            Shelters.Sum(s => s.StoredFood)));
    }
}
