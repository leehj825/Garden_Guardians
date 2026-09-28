using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Infamy: reputation between individuals -----------------------------------

    /// <summary>Infamy earned for a successful starving robbery.</summary>
    private const float RobberyInfamy = 0.6f;

    /// <summary>Infamy earned for dealing another Bramblekin its killing blow.</summary>
    private const float KillingInfamy = 1.2f;

    /// <summary>Infamy earned for carrying off a piece of an enemy's store on a war raid.</summary>
    private const float RaidInfamy = 0.3f;

    /// <summary>How much a stranger's notoriety cuts the odds of trusting it — see <see cref="IsWaryOf"/>.</summary>
    private const float NotorietyDistrustChance = 0.7f;

    /// <summary>
    /// Word gets around: with odds <see cref="NotorietyDistrustChance"/>,
    /// either side being <see cref="Bramblekin.IsNotorious"/> makes two
    /// strangers shy away from banding together, taking each other in, or
    /// warming up to one another — even though neither has personally met
    /// the other's trouble before. Never stands in the way of two people
    /// banding together to survive a predator right now.
    /// </summary>
    private bool IsWaryOf(Bramblekin a, Bramblekin b) =>
        (a.IsNotorious || b.IsNotorious) && Rng.NextDouble() < NotorietyDistrustChance;

    // --- Groups ------------------------------------------------------------------

    /// <summary>
    /// The group <paramref name="kin"/> currently belongs to, if any. Asked
    /// constantly, so each Bramblekin remembers the answer, trusted while its
    /// GroupId still matches and the group hasn't been disbanded (see <see cref="Disband"/>).
    /// </summary>
    public KinGroup? GroupOf(Bramblekin kin)
    {
        if (kin.GroupId is not { } id)
            return null;
        if (kin.CachedGroup is { IsDisbanded: false } cached && cached.Id == id)
            return cached;
        KinGroup? group = _groups.TryGetValue(id, out KinGroup? found) ? found : null;
        kin.CachedGroup = group;
        return group;
    }

    /// <summary>
    /// Group Dynamics bookkeeping: rebuilds every group's member list from
    /// the living Bramblekin's own <see cref="Bramblekin.GroupId"/>s,
    /// dissolves any group left with a single survivor (it's solitary
    /// again), and re-elects each remaining group's Leader — always the
    /// member with the highest Intelligence, so losing a leader simply hands
    /// the role to the next-sharpest member.
    /// </summary>
    private void RebuildGroups()
    {
        foreach (KinGroup group in _groups.Values)
            group.Members.Clear();

        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsDead || kin.GroupId is not { } id)
                continue;

            if (!_groups.TryGetValue(id, out KinGroup? group))
            {
                group = new KinGroup(id);
                _groups[id] = group;
            }
            group.Members.Add(kin);
        }

        _groupRemovalBuffer.Clear();
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Members.Count >= 2)
            {
                if (group.HasSittingLeader)
                    continue;

                Bramblekin? previousLeader = group.Leader;
                bool heir = Succeed(group, previousLeader);
                NameGroup(group);
                if (previousLeader is not null && previousLeader != group.Leader)
                {
                    Game.AddEventLog($"[GROUP] {group.Leader!.Name} now leads {group.Title}{(heir ? ", as its named heir" : "")}");
                    Chronicle(heir ? $"{group.Leader.Name} succeeded {previousLeader.Name} as Leader of {group.Title}"
                                   : $"{group.Leader.Name} became Leader of {group.Title}", group);
                }
                continue;
            }

            if (group.Members.Count == 1)
            {
                Bramblekin survivor = group.Members[0];
                survivor.LeaveGroup();
                // The last one standing keeps the group's home as its own.
                if (group.Home is { IsCollapsed: false } home)
                {
                    home.GroupId = null;
                    home.Owner = survivor;
                    survivor.SetHome(home);
                }
                Game.AddEventLog($"[GROUP] {group.CapitalTitle} is gone; {survivor.Name} is alone again");
                Headline("A clan ends", $"{group.CapitalTitle} came to an end; {survivor.Name} was the last of it", survivor.Position, false, group);
            }
            _groupRemovalBuffer.Add(group.Id);
        }

        foreach (Guid id in _groupRemovalBuffer)
            Disband(id);
    }

    /// <summary>Takes a group off the books, marking it so no Bramblekin's cached reference to it is trusted again (see <see cref="GroupOf"/>).</summary>
    private void Disband(Guid id)
    {
        if (_groups.Remove(id, out KinGroup? gone))
            gone.IsDisbanded = true;
    }

    // --- Encounters ----------------------------------------------------------------

    /// <summary>
    /// The Encounter: finds every pair of living Bramblekin within
    /// <see cref="EncounterRadius"/> of each other that hasn't met within
    /// the last <see cref="EncounterCooldown"/> seconds, and resolves it.
    /// </summary>
    private void ResolveEncounters()
    {
        bool groupsChanged = false;
        for (int i = 0; i < Colony.Count; i++)
        {
            Bramblekin a = Colony[i];
            if (a.IsDead)
                continue;

            _colonyGrid.QueryRadius(a.Position, EncounterRadius, _encounterBuffer);
            for (int j = 0; j < _encounterBuffer.Count; j++)
            {
                Bramblekin b = _encounterBuffer[j];
                if (b.ID <= a.ID || b.IsDead)
                    continue; // Each pair once, lower ID first.
                if (GroundMover.HorizontalDistanceSquared(a.Position, b.Position) > EncounterRadius * EncounterRadius)
                    continue;

                var key = (a.ID, b.ID);
                if (_lastEncounter.TryGetValue(key, out float last) && ElapsedSeconds - last < EncounterCooldown)
                    continue;
                _lastEncounter[key] = ElapsedSeconds;

                SpreadSickness(a, b);
                groupsChanged |= ResolveEncounter(a, b);
            }
        }

        if (groupsChanged)
            RebuildGroups();
    }

    /// <summary>
    /// The social resolution when two Bramblekin cross paths, in priority
    /// order:
    ///   1. Groupmates never fight — a fed one shares its food with a hungry
    ///      one — and may become a couple (see <see cref="TryCourt"/>).
    ///   2. Hostility: a starving, highly Aggressive one may turn on the
    ///      other to steal its food (see <see cref="TryStartRobbery"/>);
    ///      both remember each other as Enemies from then on.
    ///   3. Enemies simply pass each other by.
    ///   4. Friends may share food.
    ///   5. Courtship: two singles may become a couple (see <see cref="TryCourt"/>).
    ///   6. Alliance: if both are threatened by a predator right now, or
    ///      both are highly Sociable, they band together under one GroupId.
    ///      Failing that, a struggling loner may ask to join the other's
    ///      settled group (see <see cref="TryJoinSettledGroup"/>).
    ///   7. Otherwise they just become acquainted: Friends with odds equal
    ///      to the product of their Sociability, Neutral otherwise.
    /// Returns true if group membership changed.
    /// </summary>
    private bool ResolveEncounter(Bramblekin a, Bramblekin b)
    {
        // The young stay out of it: at most a groupmate feeds them.
        if (a.IsYoung || b.IsYoung)
        {
            if (a.GroupId is { } youngGroup && youngGroup == b.GroupId)
                TryShareFood(a, b, sameGroup: true);
            else if (!a.IsYoung || !b.IsYoung)
                return TryTakeInOrphan(a.IsYoung ? a : b, a.IsYoung ? b : a); // A lone young one is taken in.
            return false;
        }

        if (a.GroupId is { } groupId && groupId == b.GroupId)
        {
            TryShareFood(a, b, sameGroup: true);
            TryCourt(a, b);
            return false;
        }

        // Neighbours: warring groups' members keep their distance (the fighting
        // happens near home and on raids); allies treat each other as friends.
        GroupStance stance = StanceBetween(a.GroupId, b.GroupId);
        if (stance == GroupStance.AtWar)
            return false;
        if (stance == GroupStance.Allied)
        {
            TryShareFood(a, b, sameGroup: false);
            return TryCourt(a, b);
        }

        if (!AtSameFeast(a, b) && (TryStartRobbery(a, b) || TryStartRobbery(b, a)))
            return false;

        RelationshipState? relationship = a.RelationshipTo(b);
        if (relationship == RelationshipState.Enemy)
            return false;

        if (relationship == RelationshipState.Friend)
            TryShareFood(a, b, sameGroup: false);

        if (TryCourt(a, b))
            return true;

        bool bothThreatened = a.IsThreatenedByPredator && b.IsThreatenedByPredator;
        bool bothSociable = a.Personality.Sociability >= AllianceSociabilityThreshold &&
                            b.Personality.Sociability >= AllianceSociabilityThreshold;
        // Fear of a predator overrides notoriety — nobody turns down help surviving one, right now.
        if (bothThreatened && TryFormAlliance(a, b, bothThreatened))
            return true;
        if (bothSociable && !IsWaryOf(a, b) && TryFormAlliance(a, b, bothThreatened))
            return true;

        if (!IsWaryOf(a, b) && TryJoinSettledGroup(a, b))
            return true;

        if (relationship is null)
        {
            double friendlyOdds = a.Personality.Sociability * b.Personality.Sociability;
            if (a.IsNotorious || b.IsNotorious)
                friendlyOdds *= 1f - NotorietyDistrustChance;
            bool friendly = Rng.NextDouble() < friendlyOdds;
            SetMutualRelationship(a, b, friendly ? RelationshipState.Friend : RelationshipState.Neutral);
        }
        return false;
    }

    /// <summary>
    /// Hostility: <paramref name="attacker"/> — starving, empty-handed, with
    /// no loose Food in sight, and at least <see cref="HighAggressionThreshold"/>
    /// Aggressive — rolls its Aggression (halved against a Friend) to turn on
    /// <paramref name="victim"/>, who must actually be carrying food worth
    /// stealing. On success the two
    /// are Enemies for good and the attacker starts its robbery (see
    /// <see cref="Bramblekin.BeginRobbery"/>).
    /// </summary>
    private bool TryStartRobbery(Bramblekin attacker, Bramblekin victim)
    {
        if (!attacker.IsStarving || attacker.HasFood || attacker.IsRobbing || attacker.SeesFood || !victim.HasFood)
            return false;
        if (attacker.Personality.Aggression < HighAggressionThreshold || attacker.IsCloseKinOf(victim) || AreAllied(attacker.GroupId, victim.GroupId))
            return false;

        double chance = attacker.Personality.Aggression;
        if (attacker.RelationshipTo(victim) == RelationshipState.Friend)
            chance *= 0.5;
        if (Rng.NextDouble() >= chance)
            return false;

        DeclareEnemies(attacker, victim);
        AddGrievance(attacker.GroupId, victim.GroupId, RobberyGrievance);
        attacker.AddInfamy(RobberyInfamy);
        attacker.BeginRobbery(victim);
        QueueFloatingText(attacker.Position, "Attack!", HostileTextColor);
        Game.AddEventLog($"[HOSTILITY] Starving {attacker.Name} turned on {victim.Name} for its food");
        return true;
    }

    /// <summary>
    /// A fed Bramblekin carrying food hands it to a hungry, empty-handed one:
    /// always within a group, and with odds equal to the giver's
    /// Sociability between Friends (who stay Friends).
    /// </summary>
    private void TryShareFood(Bramblekin a, Bramblekin b, bool sameGroup)
    {
        Bramblekin? giver = null, taker = null;
        if (a.HasFood && !a.IsHungry && b.IsHungry && !b.HasFood)
            (giver, taker) = (a, b);
        else if (b.HasFood && !b.IsHungry && a.IsHungry && !a.HasFood)
            (giver, taker) = (b, a);

        if (giver is null || taker is null)
            return;
        if (!sameGroup && Rng.NextDouble() >= giver.Personality.Sociability)
            return;
        if (giver.SurrenderFood() is not { } food)
            return;

        taker.ReceiveFood(food);
        FoodShared++;
        if (!sameGroup)
            SetMutualRelationship(a, b, RelationshipState.Friend);
        QueueFloatingText(taker.Position, "Shared", FriendlyTextColor);
    }

    /// <summary>
    /// Alliance: puts <paramref name="a"/> and <paramref name="b"/> in the
    /// same group — a brand-new one if neither has a group, the existing one
    /// if only one does, or the larger of the two if both do (the smaller is
    /// merged into it). Refused if the result would outgrow the larger
    /// group's housing (see <see cref="GroupSizeLimit"/>), or would put anyone in a group with a
    /// known Enemy.
    /// </summary>
    private bool TryFormAlliance(Bramblekin a, Bramblekin b, bool bothThreatened)
    {
        KinGroup? groupA = GroupOf(a);
        KinGroup? groupB = GroupOf(b);
        KinGroup group;

        if (groupA is null && groupB is null)
        {
            group = new KinGroup(Guid.NewGuid());
            _groups[group.Id] = group;
            NoteRejoin(a);
            NoteRejoin(b);
            a.JoinGroup(group.Id);
            b.JoinGroup(group.Id);
            group.Members.Add(a);
            group.Members.Add(b);
        }
        else if (groupA is not null && groupB is not null)
        {
            if (groupA.Members.Count + groupB.Members.Count > Math.Max(GroupSizeLimit(groupA), GroupSizeLimit(groupB)))
                return false;

            var (larger, smaller) = groupA.Members.Count >= groupB.Members.Count ? (groupA, groupB) : (groupB, groupA);
            foreach (Bramblekin member in smaller.Members)
            {
                if (HasEnemyIn(member, larger) || member.HasLeft(larger.Id))
                    return false;
            }

            foreach (Bramblekin member in smaller.Members)
            {
                member.JoinGroup(larger.Id);
                larger.Members.Add(member);
            }
            smaller.Members.Clear();
            AbsorbHomes(larger, smaller);
            Disband(smaller.Id);
            group = larger;
        }
        else
        {
            KinGroup existing = groupA ?? groupB!;
            Bramblekin joiner = groupA is null ? a : b;
            if (existing.Members.Count >= GroupSizeLimit(existing) || HasEnemyIn(joiner, existing) || joiner.HasLeft(existing.Id))
                return false;

            NoteRejoin(joiner);
            joiner.JoinGroup(existing.Id);
            existing.Members.Add(joiner);
            group = existing;
        }

        if (!group.HasSittingLeader)
            group.ElectLeader();
        NameGroup(group);
        SetMutualRelationship(a, b, RelationshipState.Friend);
        AlliancesFormed++;
        QueueFloatingText(a.Position, "+Ally", group.Color);
        Game.AddEventLog(
            $"[ALLIANCE] {a.Name} and {b.Name} banded together ({(bothThreatened ? "both hunted" : "kindred spirits")}) - " +
            $"{group.Title} is {group.Members.Count} strong, led by {group.Leader!.Name}");
        return true;
    }

    private static bool HasEnemyIn(Bramblekin kin, KinGroup group)
    {
        foreach (Bramblekin member in group.Members)
        {
            if (kin.RelationshipTo(member) == RelationshipState.Enemy)
                return true;
        }
        return false;
    }

    private static void SetMutualRelationship(Bramblekin a, Bramblekin b, RelationshipState state)
    {
        a.SetRelationship(b, state);
        b.SetRelationship(a, state);
    }

    /// <summary>Any blow struck between two Bramblekin, or a robbery attempt, makes them Enemies for good.</summary>
    public void DeclareEnemies(Bramblekin a, Bramblekin b) => SetMutualRelationship(a, b, RelationshipState.Enemy);

    /// <summary>Forgets encounter cooldowns that have long since expired, so the table doesn't grow forever.</summary>
    private void UpdateEncounterCleanup(float deltaTime)
    {
        _encounterCleanupTimer -= deltaTime;
        if (_encounterCleanupTimer > 0f)
            return;
        _encounterCleanupTimer = 30f;

        _encounterExpiryBuffer.Clear();
        foreach (var (pair, time) in _lastEncounter)
        {
            if (ElapsedSeconds - time >= EncounterCooldown)
                _encounterExpiryBuffer.Add(pair);
        }
        foreach (var pair in _encounterExpiryBuffer)
            _lastEncounter.Remove(pair);
    }
}
