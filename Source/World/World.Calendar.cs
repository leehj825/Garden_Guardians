using System.Numerics;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>A clan with a calendar stone keeps the solstice once this far (0..1) into midsummer and midwinter.</summary>
    private const float SolsticeProgress = 0.5f;

    /// <summary>Loyalty every member gains at a solstice festival.</summary>
    private const float SolsticeLoyalty = 0.12f;

    /// <summary>Neighbours at peace within this far (m) of the host share its festival, and its grievance against them eases by this much.</summary>
    private const float SolsticeReach = 40f;
    private const float SolsticeGrudgeRelief = 2f;

    /// <summary>A neutral neighbour that joins a solstice festival becomes an ally with these odds.</summary>
    private const double SolsticeAllianceChance = 0.12;

    private float _calendarTimer;

    /// <summary>Solstice festivals held (for the headless report).</summary>
    public int SolsticesKept { get; private set; }

    /// <summary>
    /// A calendar stone (see <see cref="Craft.Calendar"/>) tells a clan when the sun turns: at midsummer and
    /// midwinter it holds a solstice festival — spirits lift (loyalty), old grudges with friendly neighbours ease,
    /// and a neutral neighbour may become an ally — once each per year.
    /// </summary>
    private void UpdateCalendar(float deltaTime)
    {
        _calendarTimer -= deltaTime;
        if (_calendarTimer > 0f)
            return;
        _calendarTimer = 1f;

        Season season = CurrentSeason;
        if (season is not (Season.Summer or Season.Winter) || SeasonProgress < SolsticeProgress || IsNight)
            return;
        int stamp = Year * 4 + (int)season;
        foreach (KinGroup group in _groups.Values)
        {
            if (group.LastSolstice == stamp || group.Home is not { IsBuilt: true, IsCollapsed: false } home || !Knows(group, Craft.Calendar))
                continue;
            group.LastSolstice = stamp;
            KeepSolstice(group, home, season);
        }
    }

    private void KeepSolstice(KinGroup group, Shelter home, Season season)
    {
        SolsticesKept++;
        foreach (Bramblekin member in group.Members)
        {
            if (!member.IsDead)
                member.SetLoyalty(member.Loyalty + SolsticeLoyalty);
        }

        int joined = 0;
        foreach (KinGroup other in _groups.Values)
        {
            if (other == group || other.Home is not { } theirs || GroundMover.HorizontalDistance(theirs.Position, home.Position) > SolsticeReach)
                continue;
            GroupStance stance = StanceBetween(group.Id, other.Id);
            if (stance == GroupStance.AtWar)
                continue;
            joined++;
            GroupRelation relation = RelationBetween(group.Id, other.Id);
            relation.Grievance = MathF.Max(0f, relation.Grievance - SolsticeGrudgeRelief);
            if (stance == GroupStance.Neutral && Rng.NextDouble() < SolsticeAllianceChance)
            {
                SetStance(group, other, GroupStance.Allied);
                Game.AddEventLog($"[ALLY] {group.CapitalTitle} and {other.Title} became allies at the solstice");
            }
        }

        string sun = season == Season.Summer ? "midsummer" : "midwinter";
        string guests = joined == 0 ? "" : $", sharing it with {joined} neighbouring {(joined == 1 ? "clan" : "clans")}";
        QueueFloatingText(home.Position + new Vector3(0f, 2.6f, 0f), "Solstice!", FeastTextColor);
        Game.AddEventLog($"[CALENDAR] {group.CapitalTitle} keeps {sun}{guests}");
        Headline("Solstice", $"{group.CapitalTitle} keeps {sun}{guests}", home.Position, false, group);
        Carve(group, $"kept {sun}");
    }
}
