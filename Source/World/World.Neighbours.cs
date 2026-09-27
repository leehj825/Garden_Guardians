using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>How one group stands toward another — see World.Neighbours.</summary>
public enum GroupStance
{
    Neutral,

    /// <summary>Defend each other, send food to an ally in need, teach farming, and marry across.</summary>
    Allied,

    /// <summary>Drive off each other's members near home, and send raiding parties after each other's stores.</summary>
    AtWar,
}

public sealed partial class World
{
    /// <summary>Two groups whose main homes are this close (m) are neighbours, and may ally.</summary>
    private const float NeighbourRadius = 30f;

    /// <summary>A Leader sends raiders after an enemy store within this many meters of home.</summary>
    private const float WarRaidRange = 60f;

    // Grievances: what one group holds against another (shared both ways), fading with time.
    private const float KillingGrievance = 3f;
    private const float RaidGrievance = 1f;
    private const float RobberyGrievance = 1f;
    private const float SplinterGrievance = 2f;
    private const float ExileGrievance = 1.5f;
    private const float GrievanceFadePerSecond = 0.004f;

    /// <summary>An aggrieved group's Leader (Aggression ≥ <see cref="WarAggression"/>) may declare war once the grievance reaches this.</summary>
    private const float WarGrievance = 10f;

    private const float WarAggression = 0.4f;

    /// <summary>Per decision, at Aggression 1.</summary>
    private const float WarChance = 0.1f;

    /// <summary>A war may end once the grievance has faded below this…</summary>
    private const float PeaceGrievance = 1.5f;

    /// <summary>…with this chance per decision, plus <see cref="PeaceChancePerCalm"/> × (1 − Aggression).</summary>
    private const float PeaceChance = 0.02f;

    private const float PeaceChancePerCalm = 0.08f;

    /// <summary>After this long (three seasons), a war may end however bitter it still is.</summary>
    private const float WarWeariness = 450f;

    /// <summary>Neighbours ally only while the grievance between them is below this…</summary>
    private const float AllianceMaxGrievance = 1f;

    /// <summary>…and both Leaders are at least this Sociable…</summary>
    private const float AllianceSociability = 0.35f;

    /// <summary>…with this chance per decision × their average Sociability (three times that for family or friends).</summary>
    private const float AllianceChance = 0.02f;

    /// <summary>A group keeps at most this many allies.</summary>
    private const int MaxAllies = 2;

    /// <summary>An alliance breaks once the grievance between the allies reaches this.</summary>
    private const float RiftGrievance = 3f;

    /// <summary>A group with at least this much stored, and half-full stores, sends food to an ally that's run out…</summary>
    private const int AidMinStore = 5;

    /// <summary>…this much at a time.</summary>
    private const int AidAmount = 5;

    /// <summary>A farming group teaches an ally with this chance per decision.</summary>
    private const float TeachFarmingChance = 0.05f;

    private sealed class GroupRelation
    {
        public GroupStance Stance;
        public float Grievance;
        public float Since;

        /// <summary>At war: how well each side (the first and second in its key) is doing — see World.WarOutcomes.</summary>
        public float FirstScore;
        public float SecondScore;
    }

    private readonly Dictionary<(Guid, Guid), GroupRelation> _relations = new();

    public int AlliancesMade { get; private set; }
    public int WarsDeclared { get; private set; }
    public int PeacesMade { get; private set; }
    public int AidSent { get; private set; }
    public int FoodAided { get; private set; }
    public int WarRaids { get; private set; }

    public int CurrentAlliances => _relations.Values.Count(r => r.Stance == GroupStance.Allied);
    public int CurrentWars => _relations.Values.Count(r => r.Stance == GroupStance.AtWar);

    private static (Guid, Guid) RelationKey(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);

    private GroupRelation RelationBetween(Guid a, Guid b)
    {
        var key = RelationKey(a, b);
        if (!_relations.TryGetValue(key, out GroupRelation? relation))
        {
            relation = new GroupRelation { Since = ElapsedSeconds };
            _relations[key] = relation;
        }
        return relation;
    }

    /// <summary>How the groups <paramref name="a"/> and <paramref name="b"/> stand toward each other (Neutral if either is null, or they're the same).</summary>
    public GroupStance StanceBetween(Guid? a, Guid? b) =>
        a is { } x && b is { } y && x != y && _relations.TryGetValue(RelationKey(x, y), out GroupRelation? relation)
            ? relation.Stance
            : GroupStance.Neutral;

    public bool AreAllied(Guid? a, Guid? b) => StanceBetween(a, b) == GroupStance.Allied;

    public bool AreAtWar(Guid? a, Guid? b) => StanceBetween(a, b) == GroupStance.AtWar;

    /// <summary>Something one group's member did to another's: a killing, a raid, a robbery. Grievances fade (see <see cref="GrievanceFadePerSecond"/>) — enough of them lead to war.</summary>
    private void AddGrievance(Guid? a, Guid? b, float amount)
    {
        if (a is { } x && b is { } y && x != y && _groups.ContainsKey(x) && _groups.ContainsKey(y))
            RelationBetween(x, y).Grievance += amount;
    }

    private void SetStance(KinGroup a, KinGroup b, GroupStance stance)
    {
        GroupRelation relation = RelationBetween(a.Id, b.Id);
        relation.Stance = stance;
        relation.Since = ElapsedSeconds;
    }

    /// <summary>Main homes within <see cref="NeighbourRadius"/> of each other.</summary>
    private static bool AreNeighbours(KinGroup a, KinGroup b) =>
        a.Home is { IsCollapsed: false } homeA && b.Home is { IsCollapsed: false } homeB &&
        GroundMover.HorizontalDistanceSquared(homeA.Position, homeB.Position) <= NeighbourRadius * NeighbourRadius;

    /// <summary>Grievances fade; relations with groups that are gone are forgotten.</summary>
    private void UpdateRelations(float deltaTime)
    {
        List<(Guid, Guid)>? stale = null;
        foreach (var (key, relation) in _relations)
        {
            if (!_groups.ContainsKey(key.Item1) || !_groups.ContainsKey(key.Item2))
            {
                (stale ??= new()).Add(key);
                continue;
            }
            relation.Grievance = MathF.Max(0f, relation.Grievance - GrievanceFadePerSecond * deltaTime);
        }
        if (stale is not null)
        {
            foreach (var key in stale)
                _relations.Remove(key);
        }
    }

    /// <summary>
    /// At each Leader decision, the Leader looks at every other group it's
    /// neighbours with or has history with:
    ///   * At war: once the grievance has faded — or after a year of it,
    ///     however bitter — it may make peace; calmer Leaders sooner.
    ///   * Neutral: an Aggressive Leader with a big enough grievance against
    ///     a neighbour declares war; with little or none, two Sociable neighbouring Leaders may
    ///     ally (far more readily if they're family or friends).
    ///   * Allied: a grudge that grows too big breaks the alliance; a
    ///     well-stocked group sends food to an ally that's run out, hires
    ///     a helper from a hungry ally to build (labour for food), and a
    ///     farming group may teach an ally to farm.
    /// </summary>
    private void ConsiderNeighbours(KinGroup group, Bramblekin leader)
    {
        foreach (KinGroup other in _groups.Values)
        {
            if (other == group || other.Members.Count == 0)
                continue;

            bool neighbours = AreNeighbours(group, other);
            _relations.TryGetValue(RelationKey(group.Id, other.Id), out GroupRelation? relation);
            if (!neighbours && relation is null)
                continue;

            GroupStance stance = relation?.Stance ?? GroupStance.Neutral;
            float grievance = relation?.Grievance ?? 0f;
            Personality p = leader.Personality;
            switch (stance)
            {
                case GroupStance.AtWar:
                    bool weary = ElapsedSeconds - relation!.Since > WarWeariness;
                    bool beaten = IsBeaten(group, other);
                    if ((grievance < PeaceGrievance || weary || beaten) &&
                        Rng.NextDouble() < (PeaceChance + PeaceChancePerCalm * (1f - p.Aggression)) * (1f - 0.5f * group.Culture.Martial) + (beaten ? SurrenderChance : 0f))
                        EndWar(group, other, leader);
                    break;

                case GroupStance.Neutral:
                    if (neighbours && grievance >= WarGrievance && p.Aggression >= WarAggression &&
                        Rng.NextDouble() < WarChance * p.Aggression * (1f + 2f * group.Culture.Martial))
                    {
                        SetStance(group, other, GroupStance.AtWar);
                        WarsDeclared++;
                        QueueFloatingText(leader.Position, "War!", HostileTextColor);
                        Game.AddEventLog($"[WAR] {leader.Name} led {group.Title} to war against {other.Title}");
                        Headline("War", $"{leader.Name} led {group.Title} to war against {other.Title}", PlaceOf(group), true, group, other);
                    }
                    else if (neighbours && grievance < AllianceMaxGrievance && TryAlly(group, leader, other))
                    {
                        AlliancesMade++;
                    }
                    break;

                case GroupStance.Allied:
                    if (grievance >= RiftGrievance)
                    {
                        SetStance(group, other, GroupStance.Neutral);
                        Game.AddEventLog($"[RIFT] The alliance between {group.Title} and {other.Title} has broken down");
                        Chronicle($"The alliance between {group.Title} and {other.Title} broke down", group, other);
                        break;
                    }
                    TrySendAid(group, other);
                    TryHireHelper(group, other);
                    TryTeachCraft(group, other);
                    break;
            }
        }
    }

    private bool TryAlly(KinGroup group, Bramblekin leader, KinGroup other)
    {
        if (other.Leader is not { IsDead: false } otherLeader)
            return false;
        if (leader.Personality.Sociability < AllianceSociability || otherLeader.Personality.Sociability < AllianceSociability)
            return false;
        if (leader.RelationshipTo(otherLeader) == RelationshipState.Enemy)
            return false;
        if (AllyCount(group) >= MaxAllies || AllyCount(other) >= MaxAllies)
            return false;

        float chance = AllianceChance * (leader.Personality.Sociability + otherLeader.Personality.Sociability) / 2f *
                       (1f - 0.6f * MathF.Max(group.Culture.Martial, other.Culture.Martial));
        bool close = leader.IsCloseKinOf(otherLeader) || leader.FamilyName == otherLeader.FamilyName ||
                     leader.RelationshipTo(otherLeader) == RelationshipState.Friend;
        if (close)
            chance *= 3f;
        if (Rng.NextDouble() >= chance)
            return false;

        SetStance(group, other, GroupStance.Allied);
        SetMutualRelationship(leader, otherLeader, RelationshipState.Friend);
        Game.AddEventLog($"[ALLIES] {leader.Name} of {group.Title} and {otherLeader.Name} of {other.Title} made an alliance");
        Headline("Alliance", $"{group.CapitalTitle} and {other.Title} became allies", PlaceOf(group), false, group, other);
        return true;
    }

    private int AllyCount(KinGroup group) =>
        _relations.Count(r => r.Value.Stance == GroupStance.Allied && (r.Key.Item1 == group.Id || r.Key.Item2 == group.Id));

    /// <summary>A well-stocked group sends a runner with food to an ally whose stores have run out while its members go hungry.</summary>
    private void TrySendAid(KinGroup giver, KinGroup ally)
    {
        if (StoredFood(giver) < AidMinStore || StoreFill(giver) < 0.4f)
            return;
        if (StoredFood(ally) > 1 || ally.Members.Count(m => m.IsHungry) * 4 < ally.Members.Count)
            return;
        if (ally.Home is not { IsBuilt: true } allyHome || giver.Leader is not { } leader)
            return;
        if (Rng.NextDouble() >= leader.Personality.Sociability)
            return;

        // Aid goes in person: a runner walks it over (see World.Errands).
        if (DispatchAid(giver, ally, allyHome))
            AidSent++;
    }

    /// <summary>The richest store of a group at war with <paramref name="group"/>, within <see cref="WarRaidRange"/> of home — the target for a raiding party.</summary>
    private Shelter? WarRaidTarget(KinGroup group)
    {
        if (group.Home is not { } home)
            return null;

        Shelter? best = null;
        foreach (var (key, relation) in _relations)
        {
            if (relation.Stance != GroupStance.AtWar || (key.Item1 != group.Id && key.Item2 != group.Id))
                continue;
            Guid enemyId = key.Item1 == group.Id ? key.Item2 : key.Item1;
            if (!_groups.TryGetValue(enemyId, out KinGroup? enemy))
                continue;
            foreach (Shelter store in GroupHomes(enemy))
            {
                if (!store.IsBuilt || store.StoredFood <= 0 ||
                    GroundMover.HorizontalDistanceSquared(store.Position, home.Position) > WarRaidRange * WarRaidRange)
                    continue;
                if (best is null || store.StoredFood > best.StoredFood)
                    best = store;
            }
        }
        return best;
    }

    /// <summary>A raider brought home a piece of an enemy's store.</summary>
    public void NoteWarRaid(Bramblekin raider, Shelter store)
    {
        WarRaids++;
        AddWarScore(raider.GroupId, store.GroupId, RaidWarScore);
    }

    /// <summary>Lines between allied (green) and warring (red) groups' main homes.</summary>
    private void DrawRelations(Camera3D camera)
    {
        foreach (var (key, relation) in _relations)
        {
            if (relation.Stance == GroupStance.Neutral)
                continue;
            if (!_groups.TryGetValue(key.Item1, out KinGroup? a) || !_groups.TryGetValue(key.Item2, out KinGroup? b))
                continue;
            if (a.Home is not { } homeA || b.Home is not { } homeB)
                continue;
            if (!IsVisible(homeA.Position, camera) && !IsVisible(homeB.Position, camera))
                continue;

            Color color = relation.Stance == GroupStance.Allied ? new Color(60, 190, 90, 200) : new Color(220, 50, 40, 220);
            var lift = new Vector3(0f, 2.2f, 0f);
            Raylib.DrawLine3D(homeA.Position + lift, homeB.Position + lift, color);
            Raylib.DrawLine3D(homeA.Position + lift + new Vector3(0f, 0.05f, 0f), homeB.Position + lift + new Vector3(0f, 0.05f, 0f), color);
        }
    }

    /// <summary>"allied with 2, at war with 1" for the Kin Inspector, or null if the group has no allies or enemies.</summary>
    public string? DescribeRelations(KinGroup group)
    {
        int allies = 0, wars = 0;
        foreach (var (key, relation) in _relations)
        {
            if (key.Item1 != group.Id && key.Item2 != group.Id)
                continue;
            if (relation.Stance == GroupStance.Allied)
                allies++;
            else if (relation.Stance == GroupStance.AtWar)
                wars++;
        }
        if (allies == 0 && wars == 0)
            return null;
        return string.Join(", ", new[] { allies > 0 ? $"allied with {allies}" : null, wars > 0 ? $"at war with {wars}" : null }.Where(s => s is not null));
    }
}
