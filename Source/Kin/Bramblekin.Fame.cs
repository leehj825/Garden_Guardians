namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>How long it has led a group, all told (seconds) — for the hall of fame.</summary>
    public float LeaderSeconds { get; private set; }

    /// <summary>Wolf Spiders it has dealt the final blow to.</summary>
    public int SpiderKills { get; private set; }

    public void AddLeaderTime(float seconds) => LeaderSeconds += seconds;

    public void NoteSpiderKill() => SpiderKills++;

    /// <summary>Contests of champions it has won for its clan (see World.Champions).</summary>
    public int ChampionWins { get; private set; }

    public void NoteChampionWin() => ChampionWins++;

    /// <summary>Marked by the player to be followed in the news: its children, illness, leadership, mastery and death make a headline (see World.NoteFavourite).</summary>
    public bool IsFavourite { get; set; }

    // --- Infamy: reputation between individuals ---------------------------------------

    /// <summary>Infamy never climbs past this.</summary>
    public const float MaxInfamy = 3f;

    /// <summary>Infamy fades on its own, like a grievance — see <see cref="DecayInfamy"/>.</summary>
    private const float InfamyFadePerSecond = 0.001f;

    /// <summary>At or above this, word has got around: even a stranger who's never met it is warier — see <see cref="World.IsWaryOf"/>.</summary>
    private const float NotorietyThreshold = 1.2f;

    /// <summary>
    /// Standing lost by deeds everyone fears — a robbery, a killing, a war
    /// raid — unlike <see cref="Reputation"/> (earned within its own group),
    /// this travels: it colours how strangers who've never met it react.
    /// </summary>
    public float Infamy { get; private set; }

    /// <summary>Known far and wide as trouble.</summary>
    public bool IsNotorious => Infamy >= NotorietyThreshold;

    public void AddInfamy(float amount) => Infamy = Math.Min(MaxInfamy, Infamy + amount);

    public void DecayInfamy(float deltaTime) => Infamy = MathF.Max(0f, Infamy - InfamyFadePerSecond * deltaTime);
}
