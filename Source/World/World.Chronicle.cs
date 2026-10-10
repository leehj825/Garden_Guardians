using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>One line of the garden's history — see World.Chronicle.</summary>
public sealed record ChronicleEntry(float Time, int Year, Season Season, Guid[] Clans, string Text)
{
    /// <summary>The banner title, for a headline ("War", "Harvest feast"…) — a marker on the history chart's timeline. Null for a plain entry.</summary>
    public string? Title { get; init; }
}

/// <summary>How many members a clan had at a history snapshot.</summary>
public readonly record struct ClanCount(Guid Id, int Members);

/// <summary>A headline: a chronicle entry big enough for a banner (see Game.Banners); <see cref="Urgent"/> ones also slow a fast-forwarded game down to watch.</summary>
public readonly record struct Moment(string Title, string Text, Vector3? Where, bool Urgent);

/// <summary>A snapshot of the garden, taken every <see cref="World.HistoryInterval"/> seconds for the history chart.</summary>
public readonly record struct HistorySample(float Time, int Population, int Groups, int FarmingGroups, int Bushes, int Alliances, int Wars, int Stored)
{
    /// <summary>Every clan's size at the time (null in a save from before they were kept).</summary>
    public ClanCount[]? Clans { get; init; }
}

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
    public void Chronicle(string text, params KinGroup?[] clans) => Chronicle(null, text, clans);

    private void Chronicle(string? title, string text, KinGroup?[] clans)
    {
        Guid[] ids = clans.Where(c => c is not null).Select(c => c!.Id).Distinct().ToArray();
        ChronicleEntries.Add(new ChronicleEntry(ElapsedSeconds, Year, CurrentSeason, ids, text) { Title = title });
        if (ChronicleEntries.Count > ChronicleCapacity)
            ChronicleEntries.RemoveAt(0);
    }

    /// <summary>Headlines not yet shown (the oldest go first past <see cref="MomentCapacity"/>) — see <see cref="TakeMoments"/>.</summary>
    private readonly List<Moment> _moments = new();

    private const int MomentCapacity = 8;

    /// <summary>A big moment: into the chronicle, and up on a banner headed <paramref name="title"/> — at <paramref name="where"/>, if it happened somewhere in particular.</summary>
    public void Headline(string title, string text, Vector3? where, bool urgent, params KinGroup?[] clans)
    {
        Chronicle(title, text, clans);
        _moments.Add(new Moment(title, text, where, urgent));
        if (where is { } spot)
            Spotlight(text, urgent ? 10f : 8f, spot);
        if (_moments.Count > MomentCapacity)
            _moments.RemoveAt(0);
    }

    /// <summary>News of a favourite (see <see cref="Bramblekin.IsFavourite"/>): a headline with its name, if <paramref name="kin"/> is one. Urgent for a death.</summary>
    public void NoteFavourite(Bramblekin kin, string what, bool urgent = false)
    {
        if (!kin.IsFavourite)
            return;
        string text = $"{kin.Name} {what}";
        _moments.Add(new Moment("Favourite", text, kin.Position, urgent)); // (a banner only: the chronicle has its own lines for the big ones)
        Spotlight(text, urgent ? 10f : 8f, kin.Position);
        if (_moments.Count > MomentCapacity)
            _moments.RemoveAt(0);
    }

    /// <summary>The headlines since last asked (for the banners), oldest first.</summary>
    public List<Moment> TakeMoments()
    {
        List<Moment> moments = _moments.ToList();
        _moments.Clear();
        return moments;
    }

    /// <summary>"The rebellious " (or "The brave ", …) — a Bramblekin's most striking trait, to open a chronicle line with; empty if none stands out.</summary>
    private static string Epithet(Bramblekin kin) => kin.Personality.Epithet() is { } word ? $"The {word} " : "";

    /// <summary>Where a clan lives (its main home), or where its Leader is, for a headline.</summary>
    private static Vector3? PlaceOf(KinGroup? clan) =>
        clan?.Home is { IsCollapsed: false } home ? home.Position : clan?.Leader is { IsDead: false } leader ? leader.Position : null;

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
            Crops.Count,
            CurrentAlliances,
            CurrentWars,
            Shelters.Sum(s => s.StoredFood))
        {
            Clans = _groups.Values.Select(g => new ClanCount(g.Id, g.Members.Count)).ToArray(),
        });
    }
}
