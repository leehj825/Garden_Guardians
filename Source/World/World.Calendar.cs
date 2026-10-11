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

    /// <summary>Planting Day falls this far (0..1) into spring, once a year, for every clan that farms.</summary>
    private const float PlantingProgress = 0.3f;

    private int _plantingYear;

    /// <summary>Late in autumn a clan wants this much stored food for each member to see the winter through.</summary>
    private const float WinterFoodPerMember = 3f;

    /// <summary>Autumn is this far (0..1) through when the winter check is made, once a year.</summary>
    private const float WinterWatchProgress = 0.6f;

    private int _winterWatchYear;

    /// <summary>Clans warned of a lean winter, in all (for the headless report).</summary>
    public int WinterWarnings { get; private set; }

    /// <summary>
    /// The winter check: late in autumn each clan's store is weighed against its members. One headline names the clan most short of food and how many
    /// others are too — the cue to help them (rain, aid, a new site: see the Guide) before the cold comes.
    /// </summary>
    private void UpdateWinterWatch()
    {
        if (CurrentSeason != Season.Autumn || SeasonProgress < WinterWatchProgress || _winterWatchYear == Year)
            return;
        _winterWatchYear = Year;
        KinGroup? worst = null;
        float worstShare = 1f;
        int short_ = 0;
        foreach (KinGroup group in _groups.Values)
        {
            int members = group.Members.Count(m => !m.IsDead);
            if (members < 3 || group.Home is not { IsBuilt: true, IsCollapsed: false })
                continue;
            float share = StoredFood(group) / (members * WinterFoodPerMember);
            if (share >= 1f)
                continue;
            short_++;
            if (share < worstShare)
            {
                worstShare = share;
                worst = group;
            }
        }
        if (worst is null)
            return;
        WinterWarnings += short_;
        string others = short_ == 1 ? "" : $", and {short_ - 1} more {(short_ == 2 ? "clan" : "clans")} are short too";
        Game.AddEventLog($"[WINTER] {worst.CapitalTitle} has {worstShare * 100f:0}% of the food it needs for winter{others}");
        Headline("Winter is coming", $"{worst.CapitalTitle} has only {worstShare * 100f:0}% of the food it needs for winter{others}", PlaceOf(worst), worstShare < 0.4f, worst);
    }

    /// <summary>Planting days kept, clan by clan (for the headless report).</summary>
    public int PlantingDaysKept { get; private set; }

    /// <summary>
    /// Planting Day: in spring every farming clan sows the year's first crop together — its grown members practise farming
    /// and spirits lift. One headline names the first clan to do it and how many followed.
    /// </summary>
    private void KeepPlantingDay()
    {
        KinGroup? first = null;
        int clans = 0;
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Home is not { IsBuilt: true, IsCollapsed: false } home || !KnowsFarming(group))
                continue;
            foreach (Bramblekin member in group.Members)
            {
                if (member.IsDead || member.IsYoung)
                    continue;
                member.SetLoyalty(member.Loyalty + 0.05f);
                if (member.Practice(Skill.Farming, 3f))
                    NoteMastery(member, Skill.Farming);
            }
            QueueFloatingText(home.Position + new Vector3(0f, 2.6f, 0f), "Planting day!", FeastTextColor);
            first ??= group;
            clans++;
            PlantingDaysKept++;
        }
        if (first is null)
            return;
        string others = clans == 1 ? "" : $", and {clans - 1} more {(clans == 2 ? "clan" : "clans")} with it";
        Game.AddEventLog($"[CALENDAR] Planting day: {first.CapitalTitle} sows the year's crop{others}");
        Headline("Planting day", $"{first.CapitalTitle} sows the year's crop{others}", PlaceOf(first), false, first);
    }

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

        UpdateWinterWatch();

        if (CurrentSeason == Season.Spring && SeasonProgress >= PlantingProgress && !IsNight && _plantingYear != Year)
        {
            _plantingYear = Year;
            KeepPlantingDay();
        }

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
