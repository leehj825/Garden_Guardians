namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>A Leader's council has at most this many members…</summary>
    private const int CouncilSize = 3;

    /// <summary>…each with at least this much Reputation.</summary>
    private const float CouncilReputation = 0.5f;

    /// <summary>A full council of middling persuasion moves the group's temper this far (of the gap) from the Leader's toward its own.</summary>
    private const float CouncilSway = 0.6f;

    /// <summary>Leader decisions taken with a council, and the sway it held over them — for the average.</summary>
    private int _councilDecisions;

    private float _councilSwayTotal;

    /// <summary>Times a council talked a Leader out of eating first.</summary>
    public int SharingOverruled { get; private set; }

    /// <summary>Times a council held a Leader back from a war it would have declared.</summary>
    public int WarsHeldBack { get; private set; }

    public float AverageCouncilSway => _councilDecisions > 0 ? _councilSwayTotal / _councilDecisions : 0f;

    /// <summary>The Leader's council: the (up to three) most respected grown members besides the Leader.</summary>
    public List<Bramblekin> CouncilOf(KinGroup group) => group.Members
        .Where(m => m != group.Leader && !m.IsDead && !m.IsYoung && m.Reputation >= CouncilReputation)
        .OrderByDescending(m => m.Reputation)
        .ThenBy(m => m.ID)
        .Take(CouncilSize)
        .ToList();

    /// <summary>
    /// The temper a group's decisions are made in: its Leader's Aggression,
    /// Sociability, Intelligence and Courage, drawn toward its council's
    /// average — the more so the more persuasive the council and the less
    /// persuasive the Leader, and the fuller the council. The Leader's own
    /// persuasiveness, diligence and rebelliousness stand.
    /// </summary>
    private Personality Counsel(KinGroup group, Bramblekin leader)
    {
        List<Bramblekin> council = CouncilOf(group);
        Personality own = leader.Personality;
        if (council.Count == 0)
            return own;

        float persuasion = council.Average(m => m.Personality.Persuasiveness);
        float sway = Math.Clamp(CouncilSway * (0.5f + persuasion - own.Persuasiveness) * council.Count / CouncilSize, 0f, 0.5f);
        _councilDecisions++;
        _councilSwayTotal += sway;

        float Blend(float mine, Func<Bramblekin, float> trait) => mine + (council.Average(trait) - mine) * sway;
        return new Personality(
            Blend(own.Aggression, m => m.Personality.Aggression),
            Blend(own.Sociability, m => m.Personality.Sociability),
            Blend(own.Intelligence, m => m.Personality.Intelligence),
            own.Rebelliousness, own.Persuasiveness,
            Blend(own.Courage, m => m.Personality.Courage),
            own.Diligence);
    }
}
