using Raylib_cs;

namespace GardenGuardians;

public static partial class Game
{
    /// <summary>Everyone's children (by ID, from the life records), rebuilt only as lives are added — see <see cref="ChildrenIndex"/>.</summary>
    private static Dictionary<int, List<int>> _childrenIndex = new();
    private static int _childrenIndexLives = -1;
    private static World? _childrenIndexWorld;

    private static Dictionary<int, List<int>> ChildrenIndex(World world)
    {
        if (_childrenIndexWorld == world && _childrenIndexLives == world.Lives.Count)
            return _childrenIndex;
        var index = new Dictionary<int, List<int>>();
        foreach (LifeRecord life in world.Lives.Values)
        {
            foreach (int? parent in new[] { life.MotherId, life.FatherId })
            {
                if (parent is not { } id)
                    continue;
                if (!index.TryGetValue(id, out List<int>? children))
                    index[id] = children = new List<int>();
                children.Add(life.Id);
            }
        }
        _childrenIndex = index;
        _childrenIndexLives = world.Lives.Count;
        _childrenIndexWorld = world;
        return index;
    }

    /// <summary>How many descendants — children, their children and so on — <paramref name="id"/> has had.</summary>
    private static int Descendants(Dictionary<int, List<int>> index, int id)
    {
        var seen = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(id);
        while (queue.Count > 0)
        {
            if (!index.TryGetValue(queue.Dequeue(), out List<int>? children))
                continue;
            foreach (int child in children)
            {
                if (seen.Add(child))
                    queue.Enqueue(child);
            }
        }
        return seen.Count;
    }

    /// <summary>A life's figures — the living Bramblekin's current ones, else as they stood when it died.</summary>
    private static (int Children, float Age, float Reign, int Spiders) Figures(LifeRecord life, IReadOnlyDictionary<int, Bramblekin> living) =>
        life.Died is null && living.TryGetValue(life.Id, out Bramblekin? kin)
            ? (kin.Children, kin.AgeInYears, kin.LeaderSeconds, kin.SpiderKills)
            : (life.Children, life.AgeYears, life.LeaderSeconds, life.SpiderKills);

    private static List<(string Title, string Holder)> _hallOfFame = new();
    private static float _hallOfFameAt = float.NegativeInfinity;
    private static World? _hallOfFameWorld;

    /// <summary>"Beli Rowanvale" — plus "(died year 17)" if dead.</summary>
    private static string LifeName(LifeRecord life) =>
        life.Died is { } died ? $"{life.Name} (died year {(int)(died / Bramblekin.SecondsPerYear) + 1})" : life.Name;

    /// <summary>
    /// The hall of fame: the garden's record holders, living or dead —
    /// longest reign, most children, most descendants, oldest, top Wolf
    /// Spider slayer — plus its biggest family and oldest clan today.
    /// </summary>
    private static List<(string Title, string Holder)> HallOfFame(World world)
    {
        // Reworked every few seconds of game time, not every frame.
        if (_hallOfFameWorld == world && MathF.Abs(world.ElapsedSeconds - _hallOfFameAt) < 5f)
            return _hallOfFame;
        _hallOfFameWorld = world;
        _hallOfFameAt = world.ElapsedSeconds;

        var fame = _hallOfFame = new List<(string, string)>();
        List<LifeRecord> lives = world.Lives.Values.ToList();
        if (lives.Count == 0)
            return fame;
        Dictionary<int, List<int>> index = ChildrenIndex(world);
        Dictionary<int, Bramblekin> living = world.Colony.Where(k => !k.IsDead).ToDictionary(k => k.ID);

        void Best(string title, Func<LifeRecord, float> measure, Func<LifeRecord, float, string> describe)
        {
            LifeRecord best = lives.MaxBy(measure)!;
            float value = measure(best);
            if (value > 0f)
                fame.Add((title, describe(best, value)));
        }

        Best("Longest reign", life => Figures(life, living).Reign,
            (life, seconds) => $"{LifeName(life)}, {seconds / Bramblekin.SecondsPerYear:0.0} years as Leader");
        Best("Most children", life => Figures(life, living).Children, (life, n) => $"{LifeName(life)}, {n:0} children");
        Best("Most descendants", life => index.ContainsKey(life.Id) ? Descendants(index, life.Id) : 0,
            (life, n) => $"{LifeName(life)}, {n:0} descendants");
        Best("Oldest", life => Figures(life, living).Age, (life, years) => $"{LifeName(life)}, {years:0.0} years");
        Best("Spider slayer", life => Figures(life, living).Spiders, (life, n) => $"{LifeName(life)}, {n:0} Wolf Spiders");

        var family = world.Colony.Where(k => !k.IsDead).GroupBy(k => k.FamilyName).MaxBy(f => f.Count());
        if (family is not null && family.Count() > 1)
            fame.Add(("Biggest family", $"The {family.Key}s, {family.Count()} living"));
        var oldestClan = world.Groups
            .Select(g => (Clan: g, Founding: world.FoundingOf(g.Id)))
            .Where(c => c.Founding is not null)
            .MinBy(c => c.Founding!.Time);
        if (oldestClan.Clan is not null)
            fame.Add(("Oldest clan", $"{oldestClan.Clan.CapitalTitle}, founded year {oldestClan.Founding!.Year}"));
        return fame;
    }

    /// <summary>
    /// The selected Bramblekin's family, from the life records: parents and
    /// grandparents (living or dead), partner, children, and how many
    /// grandchildren and descendants it has.
    /// </summary>
    private static List<string> FamilyLines(World world, Bramblekin kin)
    {
        Dictionary<int, List<int>> index = ChildrenIndex(world);
        LifeRecord? Life(int? id) => id is { } known && world.Lives.TryGetValue(known, out LifeRecord? life) ? life : null;
        string Named(int? id) => Life(id) is { } life ? LifeName(life) : "unknown";
        string Couple(LifeRecord? child) =>
            child?.MotherId is null ? "wandered in" : $"{Named(child.MotherId)} & {Named(child.FatherId)}";

        LifeRecord? self = Life(kin.ID);
        var lines = new List<string>
        {
            $"{kin.Name}, generation {kin.Generation}, {kin.AgeInYears:0.0} years",
            $"Parents: {(kin.ParentIds is null ? "wandered in from the edge" : Couple(self))}",
        };
        if (kin.ParentIds is { } parents)
        {
            lines.Add($"Mother's parents: {Couple(Life(parents.Mother))}");
            lines.Add($"Father's parents: {Couple(Life(parents.Father))}");
        }
        lines.Add($"Partner: {(kin.Partner is { IsDead: false } partner ? partner.Name : kin.IsWidowed ? "widowed" : "none")}");

        List<int> children = index.GetValueOrDefault(kin.ID) ?? new List<int>();
        lines.Add($"Children ({children.Count}):");
        foreach (int child in children)
            lines.Add("   " + Named(child));
        int grandchildren = children.Sum(child => index.GetValueOrDefault(child)?.Count ?? 0);
        lines.Add($"Grandchildren: {grandchildren}   Descendants: {Descendants(index, kin.ID)}");
        if (kin.LeaderSeconds > 0f)
            lines.Add($"Has led for {kin.LeaderSeconds / Bramblekin.SecondsPerYear:0.0} years");
        if (kin.SpiderKills > 0)
            lines.Add($"Wolf Spiders slain: {kin.SpiderKills}");
        return lines;
    }

    /// <summary>The History screen's Heroes tab: the hall of fame, and the selected Bramblekin's family beside it.</summary>
    private static void DrawHeroes(World world, Rectangle area, int textSize, int lineHeight)
    {
        int gap = (int)(24 * UiScale);
        bool twoColumns = area.Width >= 900 * UiScale;
        int columnWidth = twoColumns ? ((int)area.Width - gap) / 2 : (int)area.Width;

        var left = new List<(string Text, bool Heading)> { ("Hall of fame", true) };
        foreach (var (title, holder) in HallOfFame(world))
        {
            left.Add((title, true));
            left.Add(("   " + holder, false));
        }

        if (world.Ancestors.Count > 0)
        {
            left.Add(("Hall of ancestors", true));
            foreach (LifeRecord ancestor in world.Ancestors.Take(12))
                left.Add(($"   {ancestor.Name} - {ancestor.Honour}{(ancestor.Clan is { } clan ? $", {clan}" : "")}, {ancestor.AgeYears:0.0} years", false));
        }

        int reached = Enumerable.Range(0, World.Achievements.Length).Count(world.HasAchieved);
        left.Add(($"Achievements ({reached} of {World.Achievements.Length})", true));
        for (int i = 0; i < World.Achievements.Length; i++)
            left.Add(($"   {(world.HasAchieved(i) ? "[x]" : "[ ]")} {World.Achievements[i].Name}: {World.Achievements[i].Goal}", false));

        var right = new List<(string Text, bool Heading)> { ("Family", true) };
        if (world.SelectedKin is { IsDead: false } kin)
            right.AddRange(FamilyLines(world, kin).Select(line => (line, false)));
        else
            right.Add(("Tap a Bramblekin to see its family tree", false));

        List<List<(string Text, bool Heading)>> columns = twoColumns
            ? new() { left, right }
            : new() { left.Append(("", false)).Concat(right).ToList() };

        int visibleRows = Math.Max(1, (int)area.Height / lineHeight);
        UpdateChronicleScroll(Math.Max(1, columns.Max(c => c.Count) - visibleRows + 1), lineHeight);
        int first = (int)_chronicleScroll;
        for (int c = 0; c < columns.Count; c++)
        {
            int x = (int)area.X + c * (columnWidth + gap);
            int y = (int)area.Y;
            for (int row = first; row < columns[c].Count && y + lineHeight <= area.Y + area.Height; row++)
            {
                var (text, heading) = columns[c][row];
                Raylib.DrawText(Fit(text, textSize, columnWidth), x, y, textSize, heading ? StatsHeadingColor : PanelInk);
                y += lineHeight;
            }
        }
    }
}
