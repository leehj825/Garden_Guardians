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
        foreach (Bramblekin member in group.Members)
        {
            if (!member.IsDead && !member.IsYoung)
                member.UpdateLoyalty(group);
        }

        foreach (Bramblekin member in group.Members)
        {
            if (member == leader || member.IsDead || member.IsDueling || member.IsYoung)
                continue;

            if (member.Loyalty < Bramblekin.RebelThreshold && Rng.NextDouble() < RebelChancePerDecision)
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
            rebel.Personality.Aggression >= ChallengeAggression &&
            rebel.Health > Bramblekin.MaxHealth * 0.6f &&
            ChallengeStrength(rebel) >= ChallengeStrength(leader) * ChallengeOddsNeeded)
        {
            rebel.BeginDuel(leader);
            leader.BeginDuel(rebel);
            QueueFloatingText(rebel.Position, "Challenge!", new Color(230, 120, 30, 255));
            Game.AddEventLog($"[CHALLENGE] #{rebel.ID} challenges #{leader.ID} for the leadership of group {group.ShortId}");
            return;
        }

        if (rebel.Personality.Sociability >= SplinterSociability && TrySplinter(group, rebel))
            return;

        Depart(group, rebel);
    }

    /// <summary>How likely a Bramblekin is to win a leadership duel: its claim to lead, its Aggression, and how healthy it is right now.</summary>
    private static float ChallengeStrength(Bramblekin kin) =>
        kin.LeadershipScore + 0.5f * kin.Personality.Aggression + 0.3f * kin.Health / Bramblekin.MaxHealth;

    /// <summary>The instigator leaves with every other unhappy follower, as a new (homeless) group. False if nobody else wants to go.</summary>
    private bool TrySplinter(KinGroup group, Bramblekin instigator)
    {
        List<Bramblekin> faction = group.Members
            .Where(m => m != group.Leader && !m.IsDead && !m.IsDueling && !m.IsYoung && m.Loyalty < SplinterLoyaltyThreshold)
            .Take(MaxGroupSize)
            .ToList();
        if (faction.Count < 2 || !faction.Contains(instigator))
            return false;

        var splinter = new KinGroup(Guid.NewGuid());
        _groups[splinter.Id] = splinter;
        foreach (Bramblekin member in faction)
        {
            member.SplitOff(splinter.Id);
            splinter.Members.Add(member);
        }
        splinter.ElectLeader();

        Splinters++;
        QueueFloatingText(instigator.Position, "Split off!", splinter.Color);
        Game.AddEventLog($"[SPLIT] #{instigator.ID} led {faction.Count} unhappy members out of group {group.ShortId} into a new group {splinter.ShortId}, led by #{splinter.Leader!.ID}");
        return true;
    }

    /// <summary>A rebel leaves to go it alone: no more shared home, store or defenders.</summary>
    private void Depart(KinGroup group, Bramblekin rebel)
    {
        rebel.Desert();
        Departures++;
        QueueFloatingText(rebel.Position, "Left!", new Color(200, 200, 200, 255));
        Game.AddEventLog($"[LEAVE] #{rebel.ID} walked out on group {group.ShortId} to go it alone");
    }

    /// <summary>The Leader throws <paramref name="outcast"/> out of the group; the two are Enemies from now on.</summary>
    private void Exile(KinGroup group, Bramblekin outcast, string reason)
    {
        outcast.Desert();
        if (group.Leader is { } leader)
            DeclareEnemies(leader, outcast);
        Exiles++;
        QueueFloatingText(outcast.Position, "Exiled!", new Color(210, 50, 40, 255));
        Game.AddEventLog($"[EXILE] #{group.Leader?.ID} threw #{outcast.ID} out of group {group.ShortId} {reason}");
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
            Game.AddEventLog($"[CHALLENGE] The duel for group {group.ShortId} was called off");
            return;
        }

        winner.AddReputation(1f);
        if (group.Leader == loser)
        {
            group.SetLeader(winner);
            winner.SetLoyalty(1f);
            loser.SetLoyalty(0.35f);
            Coups++;
            QueueFloatingText(winner.Position, "New leader!", group.Color);
            Game.AddEventLog($"[COUP] #{winner.ID} beat #{loser.ID} and now leads group {group.ShortId}");
            return;
        }

        FailedChallenges++;
        if (winner.Personality.Aggression >= 0.4f)
        {
            Exile(group, loser, "after a failed challenge");
            return;
        }

        loser.SetLoyalty(0.3f);
        Game.AddEventLog($"[CHALLENGE] #{winner.ID} saw off #{loser.ID}'s challenge for group {group.ShortId}");
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
