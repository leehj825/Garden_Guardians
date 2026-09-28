using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>Each Leader decision, a follower below <see cref="Bramblekin.RebelThreshold"/> rebels with these odds.</summary>
    private const double RebelChancePerDecision = 0.35;

    /// <summary>An Aggressive Leader (≥ <see cref="ExileLeaderAggression"/>) throws out a follower below <see cref="ExileLoyaltyThreshold"/> with these odds each decision.</summary>
    private const double ExileChancePerDecision = 0.25;

    private const float ExileLoyaltyThreshold = 0.15f;
    private const float ExileLeaderAggression = 0.5f;

    /// <summary>Only a rebel at least this Aggressive challenges its Leader…</summary>
    private const float ChallengeAggression = 0.6f;

    /// <summary>…and only if its own strength is at least this fraction of the Leader's — see <see cref="ChallengeStrength"/>.</summary>
    private const float ChallengeOddsNeeded = 0.85f;

    /// <summary>A rebel at least this Sociable tries to take the other unhappy members with it.</summary>
    private const float SplinterSociability = 0.4f;

    /// <summary>Followers below this Loyalty go along with a splinter.</summary>
    private const float SplinterLoyaltyThreshold = 0.4f;

    private readonly List<(KinGroup Group, Bramblekin Kin, bool Exile)> _pendingRebellions = new();

    public int Departures { get; private set; }
    public int Splinters { get; private set; }
    public int Coups { get; private set; }
    public int FailedChallenges { get; private set; }
    public int Exiles { get; private set; }

    /// <summary>Independents (kin that had left or been thrown out of a group) later taken in by another.</summary>
    public int Rejoins { get; private set; }

    /// <summary>
    /// At every Leader decision: each follower re-weighs its loyalty (see
    /// <see cref="Bramblekin.UpdateLoyalty"/>); a deeply disloyal one may
    /// rebel, and an Aggressive Leader may throw one out. The actions
    /// themselves are queued and applied by <see cref="ProcessRebellions"/>,
    /// since a splinter creates a new group mid-iteration otherwise.
    /// </summary>
    private void ReviewLoyalty(KinGroup group, Bramblekin leader)
    {
        bool sharedHardship = CurrentSeason == Season.Winter && StoredFood(group) == 0 && group.Sharing == SharingRule.Equal;
        foreach (Bramblekin member in group.Members)
        {
            if (!member.IsDead && !member.IsYoung)
                member.UpdateLoyalty(group, sharedHardship);
        }

        foreach (Bramblekin member in group.Members)
        {
            if (member == leader || member.IsDead || member.IsDueling || member.IsYoung || member.IsNewMember)
                continue;

            if (member.Loyalty < Bramblekin.RebelThreshold && Rng.NextDouble() < RebelChancePerDecision * (0.4 + 1.2 * member.Personality.Rebelliousness))
                _pendingRebellions.Add((group, member, false));
            else if (leader.Personality.Aggression >= ExileLeaderAggression && member.Loyalty < ExileLoyaltyThreshold &&
                     Rng.NextDouble() < ExileChancePerDecision)
                _pendingRebellions.Add((group, member, true));
        }
    }

    private void ProcessRebellions()
    {
        foreach (var (group, kin, exile) in _pendingRebellions)
        {
            if (kin.IsDead || kin.GroupId != group.Id || kin.IsDueling)
                continue; // Already gone (swept up by a splinter, say).

            if (exile)
                Exile(group, kin, "for disloyalty");
            else
                Rebel(group, kin);
        }
        _pendingRebellions.Clear();
    }

    /// <summary>
    /// A rebel acts on its personality: an Aggressive one strong enough to
    /// stand a chance challenges the Leader to a duel; a Sociable one takes
    /// the other unhappy members off to start a group of their own; anyone
    /// else simply leaves to go it alone.
    /// </summary>
    private void Rebel(KinGroup group, Bramblekin rebel)
    {
        if (group.Leader is { IsDead: false, IsDueling: false } leader &&
            rebel.Personality.Aggression + 0.3f * (rebel.Personality.Rebelliousness - 0.5f) >= ChallengeAggression &&
            rebel.Health > Bramblekin.MaxHealth * 0.6f &&
            ChallengeStrength(rebel) >= ChallengeStrength(leader) * ChallengeOddsNeeded)
        {
            rebel.BeginDuel(leader);
            leader.BeginDuel(rebel);
            QueueFloatingText(rebel.Position, "Challenge!", new Color(230, 120, 30, 255));
            Game.AddEventLog($"[CHALLENGE] {rebel.Name} challenges {leader.Name} for the leadership of {group.Title}");
            return;
        }

        // Walking out into the snow is how loners starve: a sharp mind
        // grumbles and waits for spring.
        if (CurrentSeason == Season.Winter && Rng.NextDouble() < rebel.Personality.Intelligence * (1.3f - 0.6f * rebel.Personality.Rebelliousness))
            return;

        // A sociable — or persuasive — rebel talks the other unhappy members into going with it.
        if (rebel.Personality.Sociability + 0.5f * (rebel.Personality.Persuasiveness - 0.5f) >= SplinterSociability && TrySplinter(group, rebel))
            return;

        Depart(group, rebel);
    }

    /// <summary>How likely a Bramblekin is to win a leadership duel: its claim to lead, its Aggression, and how healthy it is right now.</summary>
    private static float ChallengeStrength(Bramblekin kin) =>
        kin.LeadershipScore + 0.5f * kin.Personality.Aggression + 0.3f * kin.Health / Bramblekin.MaxHealth;

    /// <summary>The instigator leaves with every other unhappy follower, as a new (homeless) group. False if nobody else wants to go.</summary>
    private bool TrySplinter(KinGroup group, Bramblekin instigator)
    {
        // A persuasive instigator sways the wavering too; a passive one only the fed-up.
        float swayed = SplinterLoyaltyThreshold + 0.25f * (instigator.Personality.Persuasiveness - 0.5f);
        List<Bramblekin> faction = group.Members
            .Where(m => m != group.Leader && !m.IsDead && !m.IsDueling && !m.IsYoung && (m == instigator || m.Loyalty < swayed))
            .Take(MaxGroupSize)
            .ToList();
        if (faction.Count < 2 || !faction.Contains(instigator))
            return false;

        // Partners who'd rather go along than stay do; the others split up with them.
        foreach (Bramblekin member in faction.ToList())
        {
            if (member.Partner is { } partner && !faction.Contains(partner) && PartnerGoesAlong(group, member))
                faction.Add(partner);
        }

        var splinter = new KinGroup(Guid.NewGuid());
        _groups[splinter.Id] = splinter;
        foreach (Bramblekin member in faction)
        {
            member.SplitOff(splinter.Id);
            splinter.Members.Add(member);
        }
        splinter.ElectLeader();
        NameGroup(splinter);
        splinter.SettleTarget = FindOpenGround(instigator.Position); // Well away from the group they left.
        splinter.Culture.CopyFrom(group.Culture);
        AddGrievance(group.Id, splinter.Id, SplinterGrievance);

        Splinters++;
        QueueFloatingText(instigator.Position, "Split off!", splinter.Color);
        Game.AddEventLog($"[SPLIT] {instigator.Name} led {faction.Count} unhappy members out of {group.Title} into a new group, {splinter.Title}, led by {splinter.Leader!.Name}");
        Headline("A clan splits", $"{Epithet(instigator)}{instigator.Name} led {faction.Count} unhappy members out of {group.Title} to form {splinter.Title}", instigator.Position, false, group, splinter);
        return true;
    }

    /// <summary>
    /// A rebel leaves to go it alone: no more shared home, store or
    /// defenders — unless its partner is unhappy enough to go too, in which
    /// case they leave together as a household of two (see
    /// <see cref="PartnerGoesAlong"/>).
    /// </summary>
    private void Depart(KinGroup group, Bramblekin rebel)
    {
        Departures++;
        QueueFloatingText(rebel.Position, "Left!", new Color(200, 200, 200, 255));
        if (PartnerGoesAlong(group, rebel))
        {
            Bramblekin partner = rebel.Partner!;
            KinGroup household = LeaveAsCouple(rebel, partner);
            Game.AddEventLog($"[LEAVE] {rebel.Name} walked out on {group.Title} with {partner.Name}; the two set up as {household.Title}");
            return;
        }

        rebel.Desert();
        Game.AddEventLog($"[LEAVE] {rebel.Name} walked out on {group.Title} to go it alone");
    }

    /// <summary>The Leader throws <paramref name="outcast"/> out of the group; the two are Enemies from now on.</summary>
    private void Exile(KinGroup group, Bramblekin outcast, string reason)
    {
        Bramblekin? leader = group.Leader;
        Exiles++;
        QueueFloatingText(outcast.Position, "Exiled!", new Color(210, 50, 40, 255));
        string partnerNote = "";
        if (PartnerGoesAlong(group, outcast))
        {
            Bramblekin partner = outcast.Partner!;
            KinGroup household = LeaveAsCouple(outcast, partner);
            AddGrievance(group.Id, household.Id, ExileGrievance);
            partnerNote = $"; {partner.Name} went with them";
        }
        else
        {
            outcast.Desert();
        }
        if (leader is not null)
            DeclareEnemies(leader, outcast);
        Game.AddEventLog($"[EXILE] {leader?.Name} threw {outcast.Name} out of {group.Title} {reason}{partnerNote}");
    }

    /// <summary>
    /// A leadership duel is over. A challenger who wins takes over as Leader
    /// (and gains Reputation); the deposed Leader stays on, unhappily. A
    /// Leader who wins gains Reputation, and — if at all Aggressive — exiles
    /// the challenger; otherwise the challenger stays, humbled. A duel that
    /// timed out changes nothing, except that the challenger is still
    /// unhappy.
    /// </summary>
    public void ResolveDuel(Bramblekin winner, Bramblekin loser, bool timedOut)
    {
        if (!winner.IsDueling && !loser.IsDueling)
            return; // Already resolved from the other side.
        winner.EndDuel();
        loser.EndDuel();

        KinGroup? group = GroupOf(winner) ?? GroupOf(loser);
        if (group is null || winner.IsDead || loser.IsDead)
            return;

        if (timedOut)
        {
            Bramblekin challenger = group.Leader == winner ? loser : winner;
            challenger.SetLoyalty(0.25f);
            Game.AddEventLog($"[CHALLENGE] The duel for {group.Title} was called off");
            return;
        }

        winner.AddReputation(1f);
        if (group.Leader == loser)
        {
            group.SetLeader(winner);
            group.Heir = null; // A usurper names its own.
            winner.SetLoyalty(1f);
            loser.SetLoyalty(0.35f);
            Coups++;
            QueueFloatingText(winner.Position, "New leader!", group.Color);
            Game.AddEventLog($"[COUP] {winner.Name} beat {loser.Name} and now leads {group.Title}");
            Headline("Coup", $"{Epithet(winner)}{winner.Name} overthrew {loser.Name} as Leader of {group.Title}", winner.Position, false, group);
            return;
        }

        FailedChallenges++;
        if (winner.Personality.Aggression >= 0.4f)
        {
            Exile(group, loser, "after a failed challenge");
            return;
        }

        loser.SetLoyalty(0.3f);
        Game.AddEventLog($"[CHALLENGE] {winner.Name} saw off {loser.Name}'s challenge for {group.Title}");
    }

    private void NoteRejoin(Bramblekin kin)
    {
        if (kin.Status == SurvivalStatus.Independent)
            Rejoins++;
    }

    /// <summary>Group Dynamics: a groupmate is fighting off <paramref name="threat"/> — whoever it was attacking notes it (loyalty).</summary>
    public void NoteDefended(Bramblekin defender, ICombatant threat)
    {
        if (GroupOf(defender) is not { } group)
            return;

        foreach (Bramblekin member in group.Members)
        {
            if (member != defender && !member.IsDead && ReferenceEquals(member.RecentAttacker(this), threat))
                member.NoteDefended();
        }
    }
}
