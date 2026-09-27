using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public static partial class Game
{
    /// <summary>True while the History screen (chart and chronicle) is open.</summary>
    private static bool _showChronicle;

    /// <summary>How far down the chronicle list it's scrolled, in entries.</summary>
    private static float _chronicleScroll;

    private static float? _chronicleDragY;

    private static readonly Color ChartPopulationColor = new(150, 90, 40, 255);
    private static readonly Color ChartGroupsColor = new(60, 140, 80, 255);
    private static readonly Color ChartGridColor = new(200, 185, 160, 255);

    private static void ToggleChronicle()
    {
        _showChronicle = !_showChronicle;
        _chronicleScroll = 0f;
        _chronicleDragY = null;
    }

    /// <summary>The History screen's scrolling: the mouse wheel, or dragging the list up and down.</summary>
    private static void UpdateChronicleScroll(int entryCount, int lineHeight)
    {
        _chronicleScroll -= Raylib.GetMouseWheelMove() * 3f;
        if (Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            float y = Raylib.GetMousePosition().Y;
            if (_chronicleDragY is { } last)
                _chronicleScroll -= (y - last) / lineHeight;
            _chronicleDragY = y;
        }
        else
        {
            _chronicleDragY = null;
        }
        _chronicleScroll = Math.Clamp(_chronicleScroll, 0f, MathF.Max(0f, entryCount - 1));
    }

    /// <summary>The entries the History screen shows: the selected Bramblekin's clan's, or the whole garden's — newest first.</summary>
    private static List<ChronicleEntry> ChronicleShown(World world, out KinGroup? clan)
    {
        clan = world.SelectedKin is { IsDead: false } kin ? world.GroupOf(kin) : null;
        IEnumerable<ChronicleEntry> entries = clan is null ? world.ChronicleEntries : world.ChronicleOf(clan.Id);
        return entries.Reverse().ToList();
    }

    /// <summary>
    /// The History screen, between the top buttons and the HUD: a header
    /// (the selected Bramblekin's clan, or the garden), a chart of the
    /// population and number of groups over time, and the chronicle,
    /// newest first.
    /// </summary>
    private static void DrawChronicle(World world, int top, int bottom)
    {
        int margin = (int)(20 * UiScale);
        var panel = new Rectangle(margin, top, Raylib.GetScreenWidth() - margin * 2, bottom - top - margin);
        if (panel.Height < 100)
            return;
        Raylib.DrawRectangleRec(panel, PanelFill with { A = 245 });
        Raylib.DrawRectangleLinesEx(panel, 2f, PanelInk);

        int headerSize = ScaledFontSize(0.75f);
        int textSize = ScaledFontSize(0.58f);
        int lineHeight = textSize + textSize / 4;
        int x = (int)panel.X + margin;
        int y = (int)panel.Y + margin / 2;
        int width = (int)panel.Width - margin * 2;

        List<ChronicleEntry> entries = ChronicleShown(world, out KinGroup? clan);
        string header = clan is null
            ? $"The garden, year {world.Year}: {world.Colony.Count(k => !k.IsDead)} Bramblekin in {world.Groups.Count} groups"
            : $"{clan.CapitalTitle}{(clan.Culture.Label is { } label ? $" ({label})" : "")}: {clan.Members.Count} members, led by {clan.Leader?.Name ?? "nobody"}" +
              (world.FoundingOf(clan.Id) is { } founding ? $", founded year {founding.Year}" : "") +
              (world.DescribeRelations(clan) is { } relations ? $", {relations}" : "");
        Raylib.DrawText(Fit(header, headerSize, width), x, y, headerSize, PanelInk);
        y += headerSize + margin / 2;

        // The chart: population (and groups) over the whole run.
        int chartHeight = (int)(panel.Height * 0.3f);
        DrawHistoryChart(world, new Rectangle(x, y, width, chartHeight), textSize);
        y += chartHeight + margin / 2;

        UpdateChronicleScroll(entries.Count, lineHeight);
        int listBottom = (int)(panel.Y + panel.Height) - margin / 2;
        if (entries.Count == 0)
        {
            Raylib.DrawText("Nothing has happened yet.", x, y, textSize, PanelInk);
            return;
        }

        for (int i = (int)_chronicleScroll; i < entries.Count && y + lineHeight <= listBottom; i++)
        {
            ChronicleEntry entry = entries[i];
            string prefix = $"Year {entry.Year} {entry.Season}: ";
            foreach (string line in Wrap(prefix + entry.Text, textSize, width))
            {
                if (y + lineHeight > listBottom)
                    break;
                Raylib.DrawText(line, x, y, textSize, PanelInk);
                y += lineHeight;
            }
        }
    }

    /// <summary>Population (brown) and number of groups (green, own scale) over time, with a mark at each new year.</summary>
    private static void DrawHistoryChart(World world, Rectangle area, int fontSize)
    {
        Raylib.DrawRectangleLinesEx(area, 1f, ChartGridColor);
        List<HistorySample> history = world.History;
        if (history.Count < 2)
        {
            Raylib.DrawText("(the chart fills in as the years go by)", (int)area.X + 8, (int)area.Y + 8, fontSize, PanelInk);
            return;
        }

        float endTime = MathF.Max(history[^1].Time, 1f);
        int maxPopulation = Math.Max(10, history.Max(h => h.Population));
        int maxGroups = Math.Max(3, history.Max(h => h.Groups));
        float X(float time) => area.X + area.Width * time / endTime;
        float Y(float value, float max) => area.Y + area.Height - area.Height * value / (max * 1.1f);

        for (float year = Bramblekin.SecondsPerYear; year < endTime; year += Bramblekin.SecondsPerYear)
            Raylib.DrawLine((int)X(year), (int)area.Y, (int)X(year), (int)(area.Y + area.Height), ChartGridColor);

        for (int i = 1; i < history.Count; i++)
        {
            HistorySample a = history[i - 1], b = history[i];
            Raylib.DrawLineEx(new Vector2(X(a.Time), Y(a.Groups, maxGroups)), new Vector2(X(b.Time), Y(b.Groups, maxGroups)), 2f, ChartGroupsColor);
            Raylib.DrawLineEx(new Vector2(X(a.Time), Y(a.Population, maxPopulation)), new Vector2(X(b.Time), Y(b.Population, maxPopulation)), 3f, ChartPopulationColor);
        }

        int labelX = (int)area.X + 8, labelY = (int)area.Y + 6;
        Raylib.DrawText($"Bramblekin (up to {maxPopulation})", labelX, labelY, fontSize, ChartPopulationColor);
        Raylib.DrawText($"Groups (up to {maxGroups})", labelX, labelY + fontSize + 4, fontSize, ChartGroupsColor);
    }

    /// <summary>Splits <paramref name="text"/> into lines no wider than <paramref name="width"/> pixels.</summary>
    private static IEnumerable<string> Wrap(string text, int fontSize, int width)
    {
        string line = "";
        foreach (string word in text.Split(' '))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Raylib.MeasureText(candidate, fontSize) > width)
            {
                yield return line;
                line = "   " + word;
            }
            else
            {
                line = candidate;
            }
        }
        if (line.Length > 0)
            yield return line;
    }

    /// <summary><paramref name="text"/>, cut short with "..." if it's wider than <paramref name="width"/> pixels.</summary>
    private static string Fit(string text, int fontSize, int width)
    {
        if (Raylib.MeasureText(text, fontSize) <= width)
            return text;
        while (text.Length > 4 && Raylib.MeasureText(text + "...", fontSize) > width)
            text = text[..^1];
        return text + "...";
    }

    /// <summary>Headless summary: the chronicle's tally, and its most recent entries.</summary>
    private static void PrintChronicle(World world)
    {
        Console.WriteLine($"Chronicle: {world.ChronicleEntries.Count} entries; the last few:");
        foreach (ChronicleEntry entry in world.ChronicleEntries.TakeLast(8))
            Console.WriteLine($"  Year {entry.Year} {entry.Season}: {entry.Text}");
    }
}
