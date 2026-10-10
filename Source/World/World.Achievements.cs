namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>Long-term goals for the garden: each is checked against what the garden has become (see <see cref="Achievements"/>).</summary>
    public sealed record Achievement(string Name, string Goal, Func<World, bool> Reached);

    public static readonly Achievement[] Achievements =
    {
        new("First steps", "a year passes in the garden", w => w.Year >= 2),
        new("Five years", "survive five years", w => w.Year >= 5),
        new("A full garden", "50 Bramblekin alive at once", w => w.Colony.Count(k => !k.IsDead) >= 50),
        new("Cradle of the garden", "100 births", w => w.Births >= 100),
        new("Dynasty", "a fifth generation is born", w => w.MaxGeneration >= 5),
        new("First village", "homes gather into a village", w => w.Villages.Count >= 1),
        new("First kingdom", "villages join under a king", w => w.Realms.Count >= 1),
        new("Spider slayer", "bring down the Wolf Spider", w => w.SpidersKilled >= 1),
        new("Master of a trade", "a Bramblekin becomes a master", w => Enum.GetValues<Skill>().Any(s => w.Masteries(s) > 0)),
        new("The turning sun", "a clan keeps a solstice", w => w.SolsticesKept >= 1),
        new("Spring sowing", "a clan keeps Planting Day", w => w.PlantingDaysKept >= 1),
        new("Trade goods", "make ten cloths or cut stones", w => w.GoodsMade >= 10),
        new("Far shore", "a clan reaches the pond's far side", w => w.FarShoresFound >= 1),
        new("Against the hill", "win an assault on the ant hill", w => w.AssaultsWon >= 1),
        new("Explorer", "find the ant hill, a shrine, a village and the Wolf Spider", w => w.DiscoveredFlags == (int)(Discovery.AntHill | Discovery.Shrine | Discovery.Village | Discovery.WolfSpider)),
    };

    /// <summary>Which of <see cref="Achievements"/> are reached (one bit each; saved with the garden).</summary>
    public int AchievementsMask { get; private set; }

    private float _achievementTimer;

    public bool HasAchieved(int index) => (AchievementsMask & (1 << index)) != 0;

    /// <summary>Every couple of seconds: notices any goal newly reached and announces it.</summary>
    private void UpdateAchievements(float deltaTime)
    {
        _achievementTimer -= deltaTime;
        if (_achievementTimer > 0f)
            return;
        _achievementTimer = 2f;
        for (int i = 0; i < Achievements.Length; i++)
        {
            if (HasAchieved(i) || !Achievements[i].Reached(this))
                continue;
            AchievementsMask |= 1 << i;
            Game.AddEventLog($"[ACHIEVEMENT] {Achievements[i].Name}");
            Headline("Achievement", $"Achievement: {Achievements[i].Name} ({Achievements[i].Goal})", null, false);
        }
    }
}
