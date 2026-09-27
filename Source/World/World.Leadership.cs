using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>Seconds between a Leader's decisions.</summary>
    public const float LeaderDecisionInterval = 5f;

    /// <summary>A threat within this many meters of home (or of a homeless group's Leader) is the group's business.</summary>
    public const float HomeDefenseRadius = 10f;

    /// <summary>A Leader sends Hunters after a Stag Beetle within this many meters of home at Aggression 0…</summary>
    public const float BaseHuntSearchRadius = 20f;

    /// <summary>…plus this much more at Aggression 1: a bold Leader ranges further for big game.</summary>
    public const float HuntSearchRadiusPerAggression = 20f;

    private static readonly int GoalCount = Enum.GetValues<GroupGoal>().Length;
    private static readonly int StyleCount = Enum.GetValues<LeaderStyle>().Length;

    /// <summary>Group-seconds spent on each goal, by the style of the Leader who chose it.</summary>
    private readonly double[,] _goalSeconds = new double[StyleCount, GoalCount];

    /// <summary>Group-seconds spent under each sharing rule, by Leader style.</summary>
    private readonly double[,] _sharingSeconds = new double[StyleCount, 2];

    /// <summary>
    /// The share of group-time that groups led by a <paramref name="style"/>
    /// Leader spent on <paramref name="goal"/> — how differently different
    /// Leaders run their groups. NaN if no such group has existed.
    /// </summary>
    public double GoalShare(LeaderStyle style, GroupGoal goal)
    {
        double total = 0;
        for (int g = 0; g < GoalCount; g++)
            total += _goalSeconds[(int)style, g];
        return total > 0 ? _goalSeconds[(int)style, (int)goal] / total : double.NaN;
    }

    /// <summary>The share of group-time that groups led by a <paramref name="style"/> Leader ran on <see cref="SharingRule.LeaderFirst"/>.</summary>
    public double LeaderFirstShare(LeaderStyle style)
    {
        double total = _sharingSeconds[(int)style, 0] + _sharingSeconds[(int)style, 1];
        return total > 0 ? _sharingSeconds[(int)style, (int)SharingRule.LeaderFirst] / total : double.NaN;
    }

    /// <summary>Group-hours led by a <paramref name="style"/> Leader.</summary>
    public double GroupHoursLedBy(LeaderStyle style)
    {
        double total = 0;
        for (int g = 0; g < GoalCount; g++)
            total += _goalSeconds[(int)style, g];
        return total / 3600.0;
    }

    /// <summary>Every group's Leader decides again every <see cref="LeaderDecisionInterval"/> seconds — at once, if a threat turns up near home.</summary>
    private void UpdateGroupDecisions(float deltaTime)
    {
        foreach (KinGroup group in _groups.Values)
        {
            if (!group.HasSittingLeader || group.Leader is not { } leader)
                continue;

            _goalSeconds[(int)group.Style, (int)group.Goal] += deltaTime;
            _sharingSeconds[(int)group.Style, (int)group.Sharing] += deltaTime;

            group.DecisionTimer -= deltaTime;
            bool emergency = group.Goal != GroupGoal.Defend && ThreatNearHome(group) is not null;
            if (group.DecisionTimer > 0f && !emergency)
                continue;

            group.DecisionTimer = LeaderDecisionInterval;
            DecideGroupGoal(group, leader);
            ReviewLoyalty(group, leader);
        }
        ProcessRebellions();
    }

    /// <summary>
    /// A Leader's decision: scores each goal from the group's situation,
    /// weighted by the Leader's own personality, picks the best, and hands
    /// out jobs to match.
    ///   * Defend — a threat near home; always wins, more so for a bold Leader.
    ///   * Settle — the home is being upgraded (Intelligent Leaders prize
    ///     it), or is still a site: a first home is urgent.
    ///   * Hunt — a Stag Beetle within reach (further for a bolder Leader) and
    ///     at least two to hunt it; Aggressive Leaders prize it, and an
    ///     emptier store makes it more urgent.
    ///   * Stockpile — fill the store (once there is one); the emptier it
    ///     is, the more urgent.
    /// The sharing rule follows the Leader's personality: an unsociable,
    /// Aggressive Leader eats first.
    /// </summary>
    private void DecideGroupGoal(KinGroup group, Bramblekin leader)
    {
        Personality p = leader.Personality;
        Shelter? home = group.Home;
        Vector3 center = home?.Position ?? leader.Position;
        float storeFill = home is { IsBuilt: true } ? home.StoredFood / (float)home.StoreCapacity : 0f;

        group.DefendTarget = ThreatNearHome(group);
        float huntRadius = BaseHuntSearchRadius + HuntSearchRadiusPerAggression * p.Aggression;
        group.HuntTarget = group.Members.Count >= 2 ? NearestLiveBeetle(center, huntRadius) : null;

        // A first home comes before anything but a fight or a beetle: until
        // it's finished there's no store to stock, so Stockpile means nothing.
        float defend = group.DefendTarget is not null ? 5f + 2f * p.Aggression : 0f;
        float settle = home is not { NeedsTwigs: true } ? 0f
            : home.IsBuilt ? 1.5f + 2f * p.Intelligence
            : 3f + 2f * p.Intelligence;
        float hunt = group.HuntTarget is not null ? 0.8f + 2f * p.Aggression + (1f - storeFill) : 0f;
        float stockpile = home is { IsBuilt: true } ? 1f + 2f * (1f - storeFill) + 0.5f * (1f - p.Aggression) : 0f;

        // Seasons: a far-sighted Leader stocks up through autumn for the
        // winter ahead; in winter's lean months, big game is worth more.
        if (CurrentSeason == Season.Autumn && stockpile > 0f)
            stockpile += 1.5f * p.Intelligence * (1f - storeFill);
        if (CurrentSeason == Season.Winter && hunt > 0f)
            hunt += 1f;

        GroupGoal goal = GroupGoal.Stockpile;
        float best = stockpile;
        if (settle > best) { goal = GroupGoal.Settle; best = settle; }
        if (hunt > best) { goal = GroupGoal.Hunt; best = hunt; }
        if (defend > best) { goal = GroupGoal.Defend; }

        SharingRule sharing = p.Sociability < 0.4f && p.Aggression >= 0.5f ? SharingRule.LeaderFirst : SharingRule.Equal;
        if (sharing != group.Sharing)
        {
            group.Sharing = sharing;
            Game.AddEventLog(sharing == SharingRule.LeaderFirst
                ? $"[LEADER] #{leader.ID} claims first share of group {group.ShortId}'s store"
                : $"[LEADER] #{leader.ID} shares group {group.ShortId}'s store equally");
        }

        if (goal != group.Goal)
        {
            group.Goal = goal;
            Game.AddEventLog(goal switch
            {
                GroupGoal.Defend => $"[LEADER] #{leader.ID} rallies group {group.ShortId} to defend home",
                GroupGoal.Hunt => $"[LEADER] #{leader.ID} sends group {group.ShortId} after a Stag Beetle",
                GroupGoal.Settle => $"[LEADER] #{leader.ID} puts group {group.ShortId} to building",
                _ => $"[LEADER] #{leader.ID} has group {group.ShortId} stock the store",
            });
        }

        AssignJobs(group);
    }

    /// <summary>
    /// Hands out jobs for the current goal, by fit: Guards from the
    /// Aggressive and healthy, Builders from the Intelligent (everyone, for
    /// a first home), Hunters from the Aggressive; everyone else gathers.
    /// The Leader takes a job too.
    /// </summary>
    private static void AssignJobs(KinGroup group)
    {
        List<Bramblekin> members = group.Members.Where(m => !m.IsDead).ToList();
        foreach (Bramblekin member in members)
            member.AssignJob(KinJob.Gatherer);

        switch (group.Goal)
        {
            case GroupGoal.Defend:
                foreach (Bramblekin member in members)
                {
                    if (member.Personality.Aggression >= 0.3f && member.Health > Bramblekin.MaxHealth / 2)
                        member.AssignJob(KinJob.Guard);
                }
                break;

            case GroupGoal.Settle:
                // Everyone builds a first home; an upgrade takes the most Intelligent half.
                int builders = group.Home is { IsBuilt: false } ? members.Count : Math.Max(1, (members.Count + 1) / 2);
                foreach (Bramblekin member in members.OrderByDescending(m => m.Personality.Intelligence).Take(builders))
                    member.AssignJob(KinJob.Builder);
                break;

            case GroupGoal.Hunt:
                foreach (Bramblekin member in members.OrderByDescending(m => m.Personality.Aggression).Take(Math.Max(2, (members.Count + 1) / 2)))
                    member.AssignJob(KinJob.Hunter);
                break;

            default:
                if (members.Count >= 4 && group.Home is { IsBuilt: true })
                    members.MaxBy(m => m.Personality.Aggression)!.AssignJob(KinJob.Guard);
                break;
        }
    }

    /// <summary>The nearest threat to the group's home (or to its Leader, while homeless): the Wolf Spider, a chasing Hornet, a raider heading for the store, or an outsider attacking a member.</summary>
    private ICombatant? ThreatNearHome(KinGroup group)
    {
        if (group.Leader is not { } leader)
            return null;

        Vector3 center = group.Home?.Position ?? leader.Position;
        ICombatant? best = null;
        float bestDistanceSquared = HomeDefenseRadius * HomeDefenseRadius;
        void Consider(ICombatant candidate)
        {
            float distanceSquared = GroundMover.HorizontalDistanceSquared(center, candidate.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = candidate;
                bestDistanceSquared = distanceSquared;
            }
        }

        if (Spider is { IsDead: false } spider)
            Consider(spider);
        foreach (Hornet hornet in Hornets)
        {
            if (!hornet.IsDead && hornet.IsChasing)
                Consider(hornet);
        }
        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsDead || kin.GroupId == group.Id)
                continue;
            if ((group.Home is not null && kin.RaidTarget == group.Home) ||
                (kin.CombatTarget is Bramblekin victim && victim.GroupId == group.Id))
                Consider(kin);
        }
        return best;
    }

    /// <summary>
    /// Whether <paramref name="kin"/> may eat from <paramref name="home"/>'s
    /// store right now: its own home always; a group home by the group's
    /// sharing rule — under <see cref="SharingRule.LeaderFirst"/> only the
    /// Leader eats while merely hungry. A refusal is remembered (it costs
    /// the Leader loyalty — see Bramblekin.UpdateLoyalty).
    /// </summary>
    public bool MayEatFromStore(Bramblekin kin, Shelter home)
    {
        if (kin.Home != home)
            return false;
        if (home.GroupId is not { } groupId || !_groups.TryGetValue(groupId, out KinGroup? group))
            return true;
        if (group.Sharing == SharingRule.Equal || group.Leader == kin || kin.IsStarving)
            return true;

        kin.NoteDeniedFood();
        return false;
    }
}
