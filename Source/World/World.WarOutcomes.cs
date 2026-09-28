using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>A killing in war scores this much for the killer's side…</summary>
    private const float KillWarScore = 3f;

    /// <summary>…and each piece of food carried off in a war raid this much.</summary>
    private const float RaidWarScore = 1f;

    /// <summary>A side has won once its score is at least this…</summary>
    private const float MinVictoryScore = 4f;

    /// <summary>…and this many times the other side's.</summary>
    private const float VictoryMargin = 2f;

    /// <summary>A beaten side (see <see cref="IsBeaten"/>) sues for peace with this much extra chance per decision.</summary>
    private const float SurrenderChance = 0.1f;

    /// <summary>A beaten group this small (with a winner at least twice its size) is absorbed rather than made to pay tribute.</summary>
    private const int AbsorbMaxMembers = 5;

    /// <summary>A beaten group pays this much food in tribute each season…</summary>
    private const int TributeAmount = 3;

    /// <summary>…for this many seasons (a year).</summary>
    private const int TributeSeasons = 4;

    private sealed class Tribute
    {
        public required Guid Payer;
        public required Guid Receiver;
        public int SeasonsLeft;
        public float NextDue;
    }

    private readonly List<Tribute> _tributes = new();
    private readonly List<(KinGroup Winner, KinGroup Loser)> _pendingConquests = new();

    /// <summary>Wars won by absorbing the beaten group.</summary>
    public int Conquests { get; private set; }

    /// <summary>Wars ended with the beaten group agreeing to pay tribute.</summary>
    public int TributesAgreed { get; private set; }

    /// <summary>Food delivered in tribute.</summary>
    public int TributeDelivered { get; private set; }

    /// <summary>Tribute payments missed (a grievance for the victors).</summary>
    public int TributesMissed { get; private set; }

    /// <summary>Something that went <paramref name="winning"/>'s way in its war with <paramref name="losing"/>.</summary>
    private void AddWarScore(Guid? winning, Guid? losing, float amount)
    {
        if (winning is not { } w || losing is not { } l || w == l)
            return;
        var key = RelationKey(w, l);
        if (!_relations.TryGetValue(key, out GroupRelation? relation) || relation.Stance != GroupStance.AtWar)
            return;
        if (key.Item1 == w)
            relation.FirstScore += amount;
        else
            relation.SecondScore += amount;
    }

    private (float Mine, float Theirs) WarScores(KinGroup mine, KinGroup theirs)
    {
        var key = RelationKey(mine.Id, theirs.Id);
        if (!_relations.TryGetValue(key, out GroupRelation? relation))
            return (0f, 0f);
        return key.Item1 == mine.Id ? (relation.FirstScore, relation.SecondScore) : (relation.SecondScore, relation.FirstScore);
    }

    /// <summary>True if <paramref name="group"/> is clearly losing its war with <paramref name="enemy"/>.</summary>
    private bool IsBeaten(KinGroup group, KinGroup enemy)
    {
        var (mine, theirs) = WarScores(group, enemy);
        return theirs >= MinVictoryScore && theirs >= VictoryMargin * mine;
    }

    /// <summary>
    /// Peace — on terms. If one side clearly won (see <see cref="MinVictoryScore"/>,
    /// <see cref="VictoryMargin"/>), a small beaten group is absorbed into
    /// the winner (queued, since groups are being walked); a bigger one pays
    /// tribute every season for a year. Otherwise it's a plain peace.
    /// </summary>
    private void EndWar(KinGroup group, KinGroup other, Bramblekin leader)
    {
        var (mine, theirs) = WarScores(group, other);
        (KinGroup? winner, KinGroup? loser) =
            mine >= MinVictoryScore && mine >= VictoryMargin * theirs ? (group, other)
            : theirs >= MinVictoryScore && theirs >= VictoryMargin * mine ? (other, group)
            : (null, null);

        GroupRelation relation = RelationBetween(group.Id, other.Id);
        relation.Stance = GroupStance.Neutral;
        relation.Since = ElapsedSeconds;
        relation.FirstScore = relation.SecondScore = 0f;
        PeacesMade++;

        if (winner is null || loser is null)
        {
            Game.AddEventLog($"[PEACE] {leader.Name} made peace between {group.Title} and {other.Title}");
            Headline("Peace", $"{group.CapitalTitle} and {other.Title} made peace", PlaceOf(group), false, group, other);
            return;
        }

        if (loser.Members.Count <= AbsorbMaxMembers && winner.Members.Count >= 2 * loser.Members.Count)
        {
            _pendingConquests.Add((winner, loser));
            return;
        }

        relation.Grievance *= 0.5f; // Terms settle some of the bitterness.
        _tributes.Add(new Tribute { Payer = loser.Id, Receiver = winner.Id, SeasonsLeft = TributeSeasons, NextDue = ElapsedSeconds });
        TributesAgreed++;
        Game.AddEventLog($"[PEACE] {loser.CapitalTitle} lost the war with {winner.Title}, and must pay {TributeAmount} food a season in tribute for a year");
        Headline("Tribute", $"{loser.CapitalTitle} lost the war with {winner.Title}, and must pay tribute for a year", PlaceOf(loser), false, winner, loser);
    }

    /// <summary>A beaten group's survivors are taken into the winner's: nearby homes become part of its village, the rest are left behind.</summary>
    private void ProcessConquests()
    {
        foreach (var (winner, loser) in _pendingConquests)
        {
            if (!_groups.ContainsKey(winner.Id) || !_groups.ContainsKey(loser.Id) || winner.Leader is not { } victor)
                continue;

            int taken = 0;
            foreach (Bramblekin member in loser.Members.ToList())
            {
                if (member.IsDead)
                    continue;
                member.JoinGroup(winner.Id);
                member.SetLoyalty(ConqueredLoyalty);
                winner.Members.Add(member);
                taken++;
            }
            loser.Members.Clear();
            AbsorbHomes(winner, loser);
            Disband(loser.Id);

            Conquests++;
            QueueFloatingText(victor.Position, "Conquest!", HostileTextColor);
            Game.AddEventLog($"[CONQUEST] {winner.CapitalTitle} won the war and took in the {taken} survivors of {loser.Title}");
            Headline("Conquest", $"{winner.CapitalTitle} conquered {loser.Title} and took in its {taken} survivors", PlaceOf(winner), true, winner, loser);
        }
        _pendingConquests.Clear();
    }

    /// <summary>The conquered start out only grudgingly loyal to their new Leader.</summary>
    private const float ConqueredLoyalty = 0.35f;

    /// <summary>Tribute falls due each season: the beaten group sends a runner with it, or the victors hold it against them.</summary>
    private void UpdateTributes()
    {
        for (int i = _tributes.Count - 1; i >= 0; i--)
        {
            Tribute tribute = _tributes[i];
            if (!_groups.TryGetValue(tribute.Payer, out KinGroup? payer) || !_groups.TryGetValue(tribute.Receiver, out KinGroup? receiver) ||
                tribute.SeasonsLeft <= 0)
            {
                _tributes.RemoveAt(i);
                continue;
            }
            if (ElapsedSeconds < tribute.NextDue)
                continue;

            tribute.NextDue = ElapsedSeconds + SeasonLength;
            tribute.SeasonsLeft--;
            if (receiver.Home is { IsBuilt: true } home && SendRunner(ErrandKind.Tribute, payer, receiver, home, TributeAmount) is { } runner)
            {
                Game.AddEventLog($"[TRIBUTE] {runner.Name} set out from {payer.Title} with {runner.SackLoad} food in tribute to {receiver.Title}");
                continue;
            }

            TributesMissed++;
            AddGrievance(payer.Id, receiver.Id, 2f);
            Game.AddEventLog($"[TRIBUTE] {payer.CapitalTitle} couldn't pay this season's tribute to {receiver.Title}");
        }
    }
}
