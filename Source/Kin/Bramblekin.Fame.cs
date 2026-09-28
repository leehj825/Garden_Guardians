namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>How long it has led a group, all told (seconds) — for the hall of fame.</summary>
    public float LeaderSeconds { get; private set; }

    /// <summary>Wolf Spiders it has dealt the final blow to.</summary>
    public int SpiderKills { get; private set; }

    public void AddLeaderTime(float seconds) => LeaderSeconds += seconds;

    public void NoteSpiderKill() => SpiderKills++;
}
