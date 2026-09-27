using Raylib_cs;

namespace GardenGuardians;

public static partial class Game
{
    private static readonly Color StatsHeadingColor = new(150, 90, 40, 255);

    /// <summary>
    /// The History screen's Stats tab: the selected Bramblekin's clan (if
    /// any), then the whole garden since it began — population, deaths,
    /// food, homes, clans, neighbours, farming, hunting and weather.
    /// </summary>
    private static List<(string Heading, List<string> Lines)> StatsSections(World world, KinGroup? clan)
    {
        var sections = new List<(string, List<string>)>();
        int alive = world.Colony.Count(k => !k.IsDead);

        if (clan is not null)
            sections.Add((ClanHeading(clan), ClanLines(world, clan)));

        int peak = world.History.Count > 0 ? Math.Max(alive, world.History.Max(h => h.Population)) : alive;
        sections.Add(("Population", new List<string>
        {
            $"Alive now: {alive} (peak {peak})",
            $"Born: {world.Births}",
            $"Wandered in: {world.Arrivals}",
            $"Generations: {world.MaxGeneration}",
            $"Couples: {world.LivingCouples} now, {world.CouplesFormed} ever",
        }));

        int deaths = Math.Max(1, world.Casualties);
        string Share(int count) => $"{count} ({100 * count / deaths}%)";
        sections.Add(("Deaths", new List<string>
        {
            $"All deaths: {world.Casualties}",
            $"Old age: {Share(world.DeathsByOldAge)}",
            $"Starved: {Share(world.DeathsByStarvation)}",
            $"Predators: {Share(world.DeathsByPredator)}",
            $"Killed by kin: {Share(world.DeathsByKin)}",
            $"Sickness: {Share(world.DeathsBySickness)} ({world.SicknessCases} fell ill)",
        }));

        sections.Add(("Food", new List<string>
        {
            $"Eaten: {world.FoodEaten}",
            $"In stores now: {world.Shelters.Sum(s => s.StoredFood)}",
            $"Meals from stores: {world.StoreMeals}",
            $"Shared: {world.FoodShared}",
            $"Stolen: {world.Thefts}, store raids {world.StoreRaids}",
        }));

        sections.Add(("Homes", new List<string>
        {
            $"Tents built: {world.TentsBuilt}",
            $"Houses built: {world.HousesBuilt}",
            $"Fell to ruin: {world.SheltersCollapsed}",
            $"Villages founded: {world.VillagesFounded}",
            $"Daughter clans: {world.Buddings}",
        }));

        int Known(Tradition tradition) => world.Groups.Count(g => g.Culture.Leading == tradition);
        sections.Add(("Clans", new List<string>
        {
            $"Clans now: {world.Groups.Count} ({world.Groups.Count(World.KnowsFarming)} farm)",
            $"Warlike {Known(Tradition.Warlike)}, hunting {Known(Tradition.Hunting)}, farming {Known(Tradition.Farming)}",
            $"Walk-outs: {world.Departures}, splits {world.Splinters}",
            $"Coups: {world.Coups}, exiles {world.Exiles}",
        }));

        sections.Add(("Neighbours", new List<string>
        {
            $"Alliances: {world.AlliancesMade} ({world.CurrentAlliances} now)",
            $"Wars: {world.WarsDeclared} ({world.CurrentWars} now), peaces {world.PeacesMade}",
            $"Conquests: {world.Conquests}, tributes {world.TributesAgreed}",
            $"Aid runners: {world.AidSent} ({world.FoodAided} food)",
            $"Helpers hired: {world.HelpersHired} ({world.LabourFoodPaid} food)",
            $"Food taken in war raids: {world.WarRaids}",
        }));

        sections.Add(("Farming", new List<string>
        {
            $"Worked out: {world.FarmingDiscoveries}, taught {world.FarmingTaught}",
            $"Bushes planted: {world.BushesPlanted}",
            $"Bushes now: {world.Bushes.Count} ({world.Bushes.Count(b => b.GroupId is null)} wild)",
            $"Berries picked: {world.FruitHarvested}",
        }));

        int KnowCraft(Craft craft) => world.Groups.Count(g => World.Knows(g, craft));
        sections.Add(("Crafts", new List<string>
        {
            $"Worked out: {world.CraftsDiscovered}, taught {world.CraftsTaught}",
            $"Clans with granaries: {KnowCraft(Craft.Granary)}",
            $"With spears: {KnowCraft(Craft.Spears)}",
            $"With palisades: {KnowCraft(Craft.Palisade)}",
        }));

        sections.Add(("Pests & plagues", new List<string>
        {
            $"Ill now: {world.SickCount}, ever {world.SicknessCases}",
            $"Died of sickness: {world.DeathsBySickness}",
            world.Anthill is { } hill ? $"Anthill: {hill.Stock} food, {world.Ants.Count} ants out" : "No ants yet",
            $"Stolen by ants: {world.AntThefts}, swatted {world.AntsKilled}",
        }));

        sections.Add(("Hunting", new List<string>
        {
            $"Wolf Spiders slain: {world.SpidersKilled}",
            $"Stag Beetles: {world.BeetlesKilled}",
            $"Grubs: {world.GrubsKilled}",
            $"Hornets swatted: {world.HornetsKilled}",
        }));

        sections.Add(("Weather", new List<string>
        {
            $"Now: {world.WeatherLabel ?? "fair"}",
            $"Bountiful seasons: {world.BountifulSeasons}",
            $"Droughts: {world.Droughts}",
            $"Harsh winters: {world.HarshWinters}",
            $"Storms: {world.Storms}",
            $"Floods: {world.Floods} ({world.HomesFlooded} homes flooded)",
        }));

        return sections;
    }

    /// <summary>"The Mossbrook clan (farming)".</summary>
    private static string ClanHeading(KinGroup clan) =>
        $"{clan.CapitalTitle}{(clan.Culture.Label is { } label ? $" ({label})" : "")}";

    /// <summary>A clan at a glance — for the Stats tab and the clan card (see DrawKinPanel).</summary>
    private static List<string> ClanLines(World world, KinGroup clan)
    {
        List<Shelter> homes = world.GroupHomes(clan).ToList();
        int houses = homes.Count(h => h.IsBuilt && h.Tier == ShelterTier.House);
        int tents = homes.Count(h => h.IsBuilt && h.Tier == ShelterTier.Tent);
        var lines = new List<string>
        {
            $"Members: {clan.Members.Count} ({clan.Members.Count(m => m.IsYoung)} young, {clan.Members.Count(m => m.IsElder)} elders)",
            $"Leader: {clan.Leader?.Name ?? "nobody"}",
            $"Homes: {houses} {(houses == 1 ? "house" : "houses")}, {tents} {(tents == 1 ? "tent" : "tents")}",
            $"Food stored: {world.StoredFood(clan)}",
            World.KnowsFarming(clan) ? $"Bushes: {world.BushesOf(clan)} of {world.BushAllowance(clan)}" : "Doesn't farm yet",
            $"Wolf Spiders slain: {clan.SpidersSlain}",
            $"Crafts: {CraftList(World.CraftsOf(clan))}",
            $"Martial {Percent(clan.Culture.Martial)}, hunting {Percent(clan.Culture.Hunting)}, farming {Percent(clan.Culture.Farming)}",
        };
        if (world.FoundingOf(clan.Id) is { } founding)
            lines.Insert(2, $"Founded: year {founding.Year}");
        if (world.DescribeRelations(clan) is { } relations)
            lines.Add(char.ToUpperInvariant(relations[0]) + relations[1..]);
        return lines;
    }

    /// <summary>"farming, granary, spears" — or "none".</summary>
    private static string CraftList(Craft crafts)
    {
        string list = string.Join(", ", Enum.GetValues<Craft>().Where(c => c != Craft.None && (crafts & c) == c).Select(c => c.ToString().ToLowerInvariant()));
        return list.Length > 0 ? list : "none";
    }

    private static string Percent(float fraction) => $"{(int)MathF.Round(fraction * 100f)}%";

    /// <summary>
    /// Lays <see cref="StatsSections"/> out in as many columns as fit
    /// (sections kept whole), scrolled by rows with the History screen's
    /// drag/wheel scrolling.
    /// </summary>
    private static void DrawStats(World world, KinGroup? clan, Rectangle area, int textSize, int lineHeight)
    {
        List<(string Heading, List<string> Lines)> sections = StatsSections(world, clan);
        int gap = (int)(24 * UiScale);
        int columns = Math.Clamp((int)area.Width / Math.Max(1, (int)(560 * UiScale)), 1, 4);
        int columnWidth = ((int)area.Width - gap * (columns - 1)) / columns;

        // Sections fill a column to about an even share of the rows, then move on to the next.
        int totalRows = sections.Sum(s => s.Lines.Count + 2);
        int target = (totalRows + columns - 1) / columns;
        var layout = new List<List<(string Text, bool Heading)>> { new() };
        foreach (var (heading, lines) in sections)
        {
            List<(string, bool)> column = layout[^1];
            if (column.Count > 0 && column.Count + lines.Count + 1 > target && layout.Count < columns)
                layout.Add(column = new List<(string, bool)>());
            column.Add((heading, true));
            column.AddRange(lines.Select(line => (line, false)));
            column.Add(("", false));
        }

        int visibleRows = Math.Max(1, (int)area.Height / lineHeight);
        int tallest = layout.Max(c => c.Count);
        UpdateChronicleScroll(Math.Max(1, tallest - visibleRows + 1), lineHeight);
        int first = (int)_chronicleScroll;

        for (int c = 0; c < layout.Count; c++)
        {
            int x = (int)area.X + c * (columnWidth + gap);
            int y = (int)area.Y;
            for (int row = first; row < layout[c].Count && y + lineHeight <= area.Y + area.Height; row++)
            {
                var (text, heading) = layout[c][row];
                Raylib.DrawText(Fit(text, textSize, columnWidth), x, y, textSize, heading ? StatsHeadingColor : PanelInk);
                y += lineHeight;
            }
        }
    }
}
