using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>A contest of champions between two clans (see World.Champions): who fights for whom, and what it settles.</summary>
public sealed class ChampionBout
{
    public required KinGroup First { get; init; }
    public required KinGroup Second { get; init; }
    public required Bramblekin FirstChampion { get; init; }
    public required Bramblekin SecondChampion { get; init; }

    /// <summary>True if it settles a war (the loser sues for peace and pays tribute), false if a feud (the loser pays a forfeit).</summary>
    public required bool EndsWar { get; init; }

    public bool Involves(Bramblekin kin) => kin == FirstChampion || kin == SecondChampion;
}

public sealed partial class World
{
    // --- Champions -------------------------------------------------------------------------

    /// <summary>Neutral neighbours with at least this much grievance have a feud a champion can settle.</summary>
    private const float FeudGrievance = 5f;

    /// <summary>Per decision, × (0.5 + the Leader's Persuasiveness): a Leader calls for champions to settle a feud…</summary>
    private const double FeudChampionChance = 0.04;

    /// <summary>…or to end a war.</summary>
    private const double WarChampionChance = 0.05;

    /// <summary>The loser of a feud's contest pays the winner this much from its stores.</summary>
    private const int ChampionForfeit = 3;

    /// <summary>A champion's contest lasts at most this long (s) — long enough to walk over and fight.</summary>
    public const float ChampionBoutSeconds = 70f;

    /// <summary>A champion must be at least this healthy (fraction of full Health).</summary>
    private const float ChampionFitness = 0.7f;

    private readonly List<ChampionBout> _bouts = new();

    public int ChampionBouts { get; private set; }
    public int FeudsSettled { get; private set; }
    public int WarsSettledByChampions { get; private set; }

    /// <summary>The champion bout <paramref name="kin"/> is fighting in, if any.</summary>
    public ChampionBout? BoutOf(Bramblekin kin)
    {
        foreach (ChampionBout bout in _bouts)
        {
            if (bout.Involves(kin))
                return bout;
        }
        return null;
    }

    /// <summary>A clan's best fighter: fit, grown and not old, the healthiest, fiercest, bravest and handiest, better still with a shield.</summary>
    private static Bramblekin? ChampionOf(KinGroup group) => group.Members
        .Where(m => !m.IsDead && !m.IsYoung && !m.IsElder && !m.IsDueling && m.Health >= Bramblekin.MaxHealth * ChampionFitness)
        .MaxBy(ChampionScore);

    private static float ChampionScore(Bramblekin kin) =>
        kin.Health / (float)Bramblekin.MaxHealth + 0.5f * kin.Personality.Aggression + 0.5f * kin.Personality.Courage +
        kin.SkillAt(Skill.Hunting) + (kin.Knows(Craft.Shields) ? 0.3f : 0f);

    /// <summary>
    /// A Leader with a feud (grievance of <see cref="FeudGrievance"/> or more)
    /// with neutral neighbours — or at war with them — may call for each side
    /// to send out its champion instead: the two fight it out between the
    /// villages until one yields. Persuasive Leaders talk the other side
    /// into it more often.
    /// </summary>
    private void TryCallChampions(KinGroup group, Bramblekin leader, KinGroup other, bool atWar)
    {
        _bouts.RemoveAll(b => !b.FirstChampion.IsDueling && !b.SecondChampion.IsDueling); // Both gone some other way.
        if (_bouts.Any(b => b.First == group || b.Second == group || b.First == other || b.Second == other))
            return;
        double chance = (atWar ? WarChampionChance : FeudChampionChance) * (0.5 + leader.Personality.Persuasiveness);
        if (Rng.NextDouble() >= chance)
            return;
        if (ChampionOf(group) is not { } mine || ChampionOf(other) is not { } theirs)
            return;

        mine.BeginDuel(theirs, ChampionBoutSeconds);
        theirs.BeginDuel(mine, ChampionBoutSeconds);
        _bouts.Add(new ChampionBout { First = group, Second = other, FirstChampion = mine, SecondChampion = theirs, EndsWar = atWar });
        ChampionBouts++;
        QueueFloatingText(mine.Position, "Champion!", group.Color);
        QueueFloatingText(theirs.Position, "Champion!", other.Color);
        string stakes = atWar ? "to end their war" : "to settle their feud";
        Game.AddEventLog($"[CHAMPIONS] {mine.Name} of {group.Title} and {theirs.Name} of {other.Title} fight {stakes}");
        Headline("Champions", $"{group.CapitalTitle} and {other.Title} send out champions {stakes}: {mine.Name} against {theirs.Name}",
            Vector3.Lerp(mine.Position, theirs.Position, 0.5f), false, group, other);
    }

    /// <summary>
    /// A champions' contest decided (see <see cref="ResolveDuel"/>): the
    /// winner is a hero; a feud is settled (the grievance forgotten, the
    /// loser pays a forfeit), a war ends with the loser paying tribute. A
    /// contest called off, or a clan gone, settles nothing.
    /// </summary>
    private void ResolveBout(ChampionBout bout, Bramblekin winner, Bramblekin loser, bool timedOut)
    {
        _bouts.Remove(bout);
        winner.EndDuel();
        loser.EndDuel();
        if (timedOut || winner.IsDead || !_groups.ContainsKey(bout.First.Id) || !_groups.ContainsKey(bout.Second.Id))
        {
            Game.AddEventLog($"[CHAMPIONS] The contest between {bout.First.Title} and {bout.Second.Title} settled nothing");
            return;
        }
        KinGroup winners = winner == bout.FirstChampion ? bout.First : bout.Second;
        KinGroup losers = winners == bout.First ? bout.Second : bout.First;
        winner.AddReputation(1f);
        winner.NoteChampionWin();
        GroupRelation relation = RelationBetween(winners.Id, losers.Id);
        relation.Grievance = 0f;
        if (bout.EndsWar)
        {
            relation.Stance = GroupStance.Neutral;
            relation.Since = ElapsedSeconds;
            relation.FirstScore = relation.SecondScore = 0f;
            PeacesMade++;
            WarsSettledByChampions++;
            _tributes.Add(new Tribute { Payer = losers.Id, Receiver = winners.Id, SeasonsLeft = TributeSeasons, NextDue = ElapsedSeconds });
            TributesAgreed++;
        }
        else
        {
            FeudsSettled++;
            int paid = 0;
            for (int i = 0; i < ChampionForfeit && WithdrawFromStores(losers); i++)
                paid++;
            for (int i = 0; i < paid; i++)
            {
                if (!GroupHomes(winners).Any(h => h.TryDeposit()))
                    break;
            }
        }
        QueueFloatingText(winner.Position, "Champion wins!", winners.Color);
        string outcome = bout.EndsWar ? $"ending the war: {losers.Title} must pay tribute" : $"settling the feud: {losers.Title} paid a forfeit";
        Game.AddEventLog($"[CHAMPIONS] {winner.Name} of {winners.Title} beat {loser.Name} of {losers.Title}, {outcome}");
        Headline("Champions", $"{Epithet(winner)}{winner.Name} of {winners.Title} beat {loser.Name} of {losers.Title} in single combat, {outcome}",
            winner.Position, false, winners, losers);
    }
}
