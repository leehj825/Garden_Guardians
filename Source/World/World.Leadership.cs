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
    /// <summary>
    /// The only Bramblekin that can be a threat to anyone's home (see
    /// <see cref="ThreatNearHome"/>): those raiding a store or fighting
    /// another Bramblekin, in Colony order. Gathered once a step, rather
    /// than every clan checking every Bramblekin — nothing in the Leaders'
    /// decisions changes who is raiding or fighting whom.
    /// </summary>
    private readonly List<Bramblekin> _possibleThreats = new();

    private void UpdateGroupDecisions(float deltaTime)
    {
        _possibleThreats.Clear();
        foreach (Bramblekin kin in Colony)
        {
            if (!kin.IsDead && (kin.RaidTarget is not null || kin.CombatTarget is Bramblekin))
                _possibleThreats.Add(kin);
        }

        foreach (KinGroup group in _groups.Values)
        {
            if (!group.HasSittingLeader || group.Leader is not { } leader)
                continue;

            _goalSeconds[(int)group.Style, (int)group.Goal] += deltaTime;
            _sharingSeconds[(int)group.Style, (int)group.Sharing] += deltaTime;
            group.BirthCooldown = MathF.Max(0f, group.BirthCooldown - deltaTime);

            group.DecisionTimer -= deltaTime;
            bool emergency = group.Goal != GroupGoal.Defend && ThreatNearHome(group) is not null;
            if (group.DecisionTimer > 0f && !emergency)
                continue;

            group.DecisionTimer = LeaderDecisionInterval;
            UpdateFarmingKnowledge(group);
            UpdateCrafts(group);
            PlaceSnares(group);
            TendHerd(group);
            SendForHoney(group);
            TryHoldFeast(group, leader);
            UpdateBelief(group, leader);
            UpdateWells(group);
            UpdateWalls(group);
            UpdateCulture(group);
            group.Counsel = Counsel(group, leader);
            ConsiderNeighbours(group, leader);
            DecideGroupGoal(group, leader);
            ReviewLoyalty(group, leader);
            AdvancePlot(group, leader);
            ConsiderHeir(group, leader);
            AdoptOrphans(group);
            TryBirth(group);
        }
        ProcessRebellions();
        ProcessPlots();
        ProcessProphecies();
        ProcessConquests();
    }

    /// <summary>
    /// A Leader's decision: scores each goal from the group's situation,
    /// weighted by the Leader's own personality, picks the best, and hands
    /// out jobs to match (and, in a farming group, Farmers — see <see cref="AssignJobs"/>).
    ///   * Defend — a threat near home; always wins, more so for a bold Leader.
    ///   * Settle — something is under construction (see
    ///     <see cref="KinGroup.ConstructionSite"/>): an upgrade or a new home
    ///     in the village (Intelligent Leaders prize it), or the first home,
    ///     which is urgent.
    ///   * Hunt — a Stag Beetle within reach (further for a bolder Leader) and
    ///     at least two to hunt it; Aggressive Leaders prize it, and an
    ///     emptier store makes it more urgent.
    ///   * Stockpile — fill the stores (once there are any); the emptier
    ///     they are, the more urgent.
    ///   * Raid — at war, with an enemy store within reach and at least
    ///     three fit fighters: send a raiding party to carry it off. Only
    ///     Aggressive Leaders really go for it; a party, once sent, keeps at
    ///     it for <see cref="RaidDuration"/> (or until the store is empty),
    ///     and the next can't set out for <see cref="RaidInterval"/>.
    /// The sharing rule follows the Leader's personality: an unsociable,
    /// Aggressive Leader eats first.
    /// </summary>
    private void DecideGroupGoal(KinGroup group, Bramblekin leader)
    {
        Personality p = group.Counsel;
        Shelter? home = group.Home;
        Vector3 center = home?.Position ?? leader.Position;
        float storeFill = StoreFill(group);
        bool hasStore = GroupHomes(group).Any(h => h.IsBuilt);

        group.DefendTarget = ThreatNearHome(group);
        float huntRadius = BaseHuntSearchRadius + HuntSearchRadiusPerAggression * p.Aggression;
        group.HuntTarget = group.Members.Count >= 2 ? NearestLiveBeetle(center, huntRadius) : null;

        // A first home comes before anything but a fight or a beetle: until
        // it's finished there's no store to stock, so Stockpile means nothing.
        float defend = group.DefendTarget is not null ? 5f + 2f * p.Aggression : 0f;
        float settle = group.ConstructionSite is null ? 0f
            : home is { IsBuilt: true } ? 1.5f + 2f * p.Intelligence
            : 3f + 2f * p.Intelligence;
        float hunt = group.HuntTarget is not null ? 0.8f + 2f * p.Aggression + (1f - storeFill) : 0f;
        float stockpile = hasStore ? 1f + 2f * (1f - storeFill) + 0.5f * (1f - p.Aggression) : 0f;

        // Seasons: a far-sighted Leader stocks up through autumn for the
        // winter ahead; in winter's lean months, big game is worth more.
        if (CurrentSeason == Season.Autumn && stockpile > 0f)
            stockpile += 1.5f * p.Intelligence * (1f - storeFill);
        if (CurrentSeason == Season.Winter && hunt > 0f)
            hunt += 1f;

        // War: a raiding party already out keeps at it; otherwise, once the
        // last one is long enough ago, an enemy store in reach is a target.
        bool raiding = group.Goal == GroupGoal.Raid && ElapsedSeconds < group.RaidEndsAt &&
                       group.WarTarget is { IsCollapsed: false, StoredFood: > 0 };
        Shelter? warTarget = raiding ? group.WarTarget : ElapsedSeconds >= group.NextRaidAt ? WarRaidTarget(group) : null;
        int fighters = group.Members.Count(m => !m.IsDead && !m.IsYoung && m.Health > Bramblekin.MaxHealth / 2);
        float raid = warTarget is null || !hasStore || fighters < 3 ? 0f
            : 0.5f + 3f * p.Aggression + (1f - storeFill);

        // Clan culture: its traditions sway any Leader's choices.
        ClanCulture culture = group.Culture;
        if (hunt > 0f)
            hunt += 1.5f * culture.Hunting;
        if (raid > 0f)
            raid += 2f * culture.Martial;
        // Night: the enemy is asleep, and a raid is likelier to get in unseen —
        // so a shrewd Leader waits for dark before sending one.
        if (raid > 0f && !raiding)
            raid += IsNight ? 1f : -1.5f * p.Intelligence;
        if (stockpile > 0f)
            stockpile += 0.5f * culture.Farming;

        GroupGoal goal = GroupGoal.Stockpile;
        float best = stockpile;
        if (settle > best) { goal = GroupGoal.Settle; best = settle; }
        if (hunt > best) { goal = GroupGoal.Hunt; best = hunt; }
        if (raid > best) { goal = GroupGoal.Raid; best = raid; }
        if (defend > best) { goal = GroupGoal.Defend; }

        // A raiding party already out sees it through — unless home itself is threatened.
        if (raiding && defend == 0f)
            goal = GroupGoal.Raid;

        bool newRaid = goal == GroupGoal.Raid && !raiding;
        group.WarTarget = goal == GroupGoal.Raid ? warTarget : null;
        if (newRaid)
        {
            if (IsNight)
                NoteNightRaid();
            group.RaidEndsAt = ElapsedSeconds + RaidDuration;
            group.NextRaidAt = ElapsedSeconds + RaidInterval;
        }

        SharingRule sharing = p.Sociability < 0.4f && p.Aggression >= 0.5f ? SharingRule.LeaderFirst : SharingRule.Equal;
        Personality own = leader.Personality;
        if (sharing == SharingRule.Equal && own.Sociability < 0.4f && own.Aggression >= 0.5f && group.OverruledOnSharing != leader)
        {
            group.OverruledOnSharing = leader; // Told once per Leader.
            SharingOverruled++;
            Game.AddEventLog($"[COUNCIL] {group.CapitalTitle}'s council talked {leader.Name} out of eating first");
        }
        if (sharing != group.Sharing)
        {
            group.Sharing = sharing;
            Game.AddEventLog(sharing == SharingRule.LeaderFirst
                ? $"[LEADER] {leader.Name} claims first share of {group.Title}'s store"
                : $"[LEADER] {leader.Name} shares {group.Title}'s store equally");
        }

        if (goal != group.Goal)
        {
            group.Goal = goal;
            if (goal != GroupGoal.Raid) // A raid is announced below, with its party.
            {
                Game.AddEventLog(goal switch
                {
                    GroupGoal.Defend => $"[LEADER] {leader.Name} rallies {group.Title} to defend home",
                    GroupGoal.Hunt => $"[LEADER] {leader.Name} sends {group.Title} after a Stag Beetle",
                    GroupGoal.Settle => $"[LEADER] {leader.Name} puts {group.Title} to building",
                    _ => $"[LEADER] {leader.Name} has {group.Title} stock the stores",
                });
            }
        }

        AssignJobs(group);

        if (newRaid && warTarget!.GroupId is { } enemyId && _groups.TryGetValue(enemyId, out KinGroup? enemy))
        {
            int raiders = group.Members.Count(m => m.Job == KinJob.Raider);
            QueueFloatingText(leader.Position, "Raid!", HostileTextColor);
            Game.AddEventLog($"[WAR] {leader.Name} sent {raiders} raiders from {group.Title} against {enemy.Title}'s stores");
        }
    }

    /// <summary>A raiding party keeps at it this long (s)…</summary>
    private const float RaidDuration = 45f;

    /// <summary>…and the next can't set out until this long (s) after it did.</summary>
    private const float RaidInterval = 240f;


    /// <summary>
    /// Hands out jobs for the current goal, by fit: Guards from the
    /// Aggressive and healthy, Builders from the Intelligent (everyone, for
    /// a first home), Hunters from the Aggressive; everyone else gathers.
    /// The Leader takes a job too.
    /// </summary>
    private void AssignJobs(KinGroup group)
    {
        foreach (Bramblekin young in group.Members.Where(m => m.IsYoung))
            young.AssignJob(KinJob.None);
        List<Bramblekin> members = group.Members.Where(m => !m.IsDead && !m.IsYoung).ToList();
        if (members.Count == 0)
            return;
        foreach (Bramblekin member in members)
            member.AssignJob(KinJob.Gatherer);

        switch (group.Goal)
        {
            case GroupGoal.Defend:
                foreach (Bramblekin member in members)
                {
                    if ((member.Personality.Aggression + member.Personality.Courage) / 2f >= 0.3f && member.Health > Bramblekin.MaxHealth / 2)
                        member.AssignJob(KinJob.Guard);
                }
                break;

            case GroupGoal.Settle:
                // Everyone builds a first home; an upgrade or a new home takes the most Intelligent half.
                int builders = group.Home is { IsBuilt: false } ? members.Count : Math.Max(1, (members.Count + 1) / 2);
                foreach (Bramblekin member in members.OrderByDescending(m => m.Personality.Intelligence + m.SkillAt(Skill.Building)).Take(builders))
                    member.AssignJob(KinJob.Builder);
                break;

            case GroupGoal.Hunt:
                foreach (Bramblekin member in members.OrderByDescending(m => m.Personality.Courage + 0.5f * m.Personality.Aggression + m.SkillAt(Skill.Hunting))
                             .Take(Math.Max(2, (members.Count + 1) / 2)))
                    member.AssignJob(KinJob.Hunter);
                break;

            case GroupGoal.Raid:
                // The boldest healthy half goes; the rest mind home.
                foreach (Bramblekin member in members.Where(m => m.Health > Bramblekin.MaxHealth / 2)
                             .OrderByDescending(m => m.Personality.Aggression).Take(Math.Max(2, (members.Count + 1) / 2)))
                    member.AssignJob(KinJob.Raider);
                break;

            default:
                if (members.Count >= 4 && group.Home is { IsBuilt: true })
                    members.MaxBy(m => m.Personality.Courage + 0.5f * m.Personality.Aggression + 0.5f * m.Personality.Strength)!.AssignJob(KinJob.Guard);
                break;
        }

        // A farming group keeps some of its sharpest Gatherers on its crops.
        if (group.Goal != GroupGoal.Defend && group.Home is { IsBuilt: true } && KnowsFarming(group))
        {
            int perFarmer = Math.Max(2, FarmersPerMembers - (int)MathF.Round(2f * group.Culture.Farming)); // A farming clan farms more.
            int farmers = Math.Max(1, members.Count / perFarmer);
            foreach (Bramblekin member in members.Where(m => m.Job == KinJob.Gatherer).OrderByDescending(m => m.Personality.Intelligence + m.SkillAt(Skill.Farming)).Take(farmers))
                member.AssignJob(KinJob.Farmer);
        }

        // A clan with herb-lore keeps someone kind and clever tending its sick and wounded.
        if (group.Goal != GroupGoal.Raid && World.Knows(group, Craft.Herbalism) && members.Count >= 2 && members.Any(m => m.NeedsCare))
            members.Where(m => m.Job == KinJob.Gatherer && !m.NeedsCare)
                .MaxBy(m => m.Personality.Intelligence + m.Personality.Sociability + m.SkillAt(Skill.Healing))?.AssignJob(KinJob.Healer);

        // A clan that maps the garden keeps one bold, clever Gatherer scouting the ground it hasn't seen.
        if (group.Goal is not (GroupGoal.Defend or GroupGoal.Raid) && group.Home is { IsBuilt: true } && members.Count >= 4 &&
            World.Knows(group, Craft.Exploration) && group.Known.Fraction < ScoutingDoneFraction)
            members.Where(m => m.Job == KinJob.Gatherer && m.Health > Bramblekin.MaxHealth * 0.6f)
                .MaxBy(m => m.Personality.Courage + m.Personality.Intelligence)?.AssignJob(KinJob.Scout);

        // A clan with a well, a footing or a palisade to finish keeps its most diligent Gatherer fetching stones and branches
        // (a well — water — even in a clan of two).
        if (group.Goal is not (GroupGoal.Defend or GroupGoal.Raid) && group.Home is { IsBuilt: true } home &&
            (members.Count >= 3 || (members.Count >= 2 && WellBeingDug(group) is not null)) &&
            (MaterialTarget(group, MaterialKind.Stone, home.Position) ?? MaterialTarget(group, MaterialKind.Branch, home.Position)) is not null)
            members.Where(m => m.Job == KinJob.Gatherer).MaxBy(m => m.Personality.Diligence + m.SkillAt(Skill.Building))?.AssignJob(KinJob.Builder);

        // A hearth burning low: someone keeps the fire fed.
        if (group.Goal is not (GroupGoal.Defend or GroupGoal.Raid) && members.Count >= 2 && !members.Any(m => m.Job == KinJob.Builder) &&
            AnyHearthNeedsFuel(group))
            members.Where(m => m.Job == KinJob.Gatherer).MaxBy(m => m.Personality.Diligence + m.SkillAt(Skill.Building))?.AssignJob(KinJob.Builder);
    }

    /// <summary>A farming group makes one Farmer for every this many grown members (at least one).</summary>
    private const int FarmersPerMembers = 4;

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
        foreach (Bramblekin kin in _possibleThreats)
        {
            if (kin.IsDead || kin.GroupId == group.Id)
                continue;
            // An outsider defending its own home isn't a threat to this one —
            // otherwise two villages next door each "defend" against the other
            // in an endless brawl. Nor are defenders fighting off this group's
            // own raiding party.
            if (kin.RaidTarget is { } raided && raided.GroupId == group.Id)
                Consider(kin);
            else if (kin.CombatTarget is Bramblekin victim && victim.GroupId == group.Id &&
                     !(group.Goal == GroupGoal.Raid && victim.Job == KinJob.Raider) && !IsDefendingOwnHome(kin))
                Consider(kin);
        }
        return best;
    }

    /// <summary>True while <paramref name="kin"/> is fighting near its own home with its group rallied to defend it.</summary>
    private bool IsDefendingOwnHome(Bramblekin kin) =>
        kin.State == BramblekinState.Fighting && GroupOf(kin) is { Goal: GroupGoal.Defend } &&
        kin.Home is { } home &&
        GroundMover.HorizontalDistanceSquared(kin.Position, home.Position) <= HomeDefenseRadius * HomeDefenseRadius * 2.25f;

    /// <summary>
    /// Whether <paramref name="kin"/> may eat from <paramref name="home"/>'s
    /// store right now: its own home, or any home of its group's, by the
    /// group's sharing rule — under <see cref="SharingRule.LeaderFirst"/> only the
    /// Leader eats while merely hungry. A refusal is remembered (it costs
    /// the Leader loyalty — see Bramblekin.UpdateLoyalty).
    /// </summary>
    public bool MayEatFromStore(Bramblekin kin, Shelter home)
    {
        if (kin.Home != home && !(kin.GroupId is not null && home.GroupId == kin.GroupId))
            return false;
        if (home.GroupId is not { } groupId || !_groups.TryGetValue(groupId, out KinGroup? group))
            return true;
        if (group.Sharing == SharingRule.Equal || group.Leader == kin || kin.IsStarving || kin.IsYoung)
            return true;

        kin.NoteDeniedFood();
        return false;
    }
}
