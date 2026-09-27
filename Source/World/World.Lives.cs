namespace GardenGuardians;

public sealed partial class World
{
    private readonly Dictionary<int, LifeRecord> _lives = new();

    /// <summary>Every Bramblekin that has ever lived in the garden, by ID — see <see cref="LifeRecord"/>.</summary>
    public IReadOnlyDictionary<int, LifeRecord> Lives => _lives;

    /// <summary>Starts the record of a Bramblekin's life, as it's born or wanders in.</summary>
    private void RegisterLife(Bramblekin kin)
    {
        if (_lives.ContainsKey(kin.ID))
            return;
        _lives[kin.ID] = new LifeRecord
        {
            Id = kin.ID,
            Name = kin.Name,
            MotherId = kin.ParentIds?.Mother,
            FatherId = kin.ParentIds?.Father,
            Generation = kin.Generation,
            Arrived = ElapsedSeconds,
        };
    }

    /// <summary>Closes the record of a life that just ended, with how it ended and what it amounted to.</summary>
    private void CloseLife(Bramblekin kin, string fate)
    {
        RegisterLife(kin);
        LifeRecord life = _lives[kin.ID];
        life.Died = ElapsedSeconds;
        life.Fate = fate;
        life.Clan = GroupOf(kin)?.Title;
        life.Children = kin.Children;
        life.AgeYears = kin.AgeInYears;
        life.LeaderSeconds = kin.LeaderSeconds;
        life.SpiderKills = kin.SpiderKills;
    }

    /// <summary>Leaders earn their reign, second by second (for the hall of fame).</summary>
    private void UpdateReigns(float deltaTime)
    {
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Leader is { IsDead: false } leader)
                leader.AddLeaderTime(deltaTime);
        }
    }

    /// <summary>The living Bramblekin with <paramref name="id"/>, if it's still alive.</summary>
    public Bramblekin? LivingKin(int id) => Colony.FirstOrDefault(k => k.ID == id && !k.IsDead);
}
