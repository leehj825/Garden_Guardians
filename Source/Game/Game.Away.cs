using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// While you were away: the garden only runs while the app is open, so a garden loaded after a break catches up first — the time away (up to
/// <see cref="AwayCapSeconds"/> of garden time) is simulated in the background of the first frames, with a progress bar (a tap skips the rest) —
/// and then a few banners say what happened.
/// </summary>
public static partial class Game
{
    /// <summary>Less than this much real time away and the garden doesn't catch up.</summary>
    private const float AwayMinSeconds = 120f;

    /// <summary>The most garden time one catch-up simulates, however long the break was.</summary>
    private const float AwayCapSeconds = 600f;

    /// <summary>Wall-clock time one frame gives the catch-up: it keeps the screen alive while it works.</summary>
    private const double CatchUpFrameSeconds = 0.05;

    private static float _catchUpLeft, _catchUpTotal;
    private static float _awayStartTime;
    private static int _awayBirths, _awayCasualties;

    /// <summary>A garden has just loaded: if it was saved a while ago, the time since is to be simulated.</summary>
    private static void BeginCatchUp(World world)
    {
        double away = (DateTime.UtcNow - SaveSystem.LastLoadedAt).TotalSeconds;
        _catchUpLeft = _catchUpTotal = away >= AwayMinSeconds ? MathF.Min((float)away, AwayCapSeconds) : 0f;
        _awayStartTime = world.ElapsedSeconds;
        _awayBirths = world.Births;
        _awayCasualties = world.Casualties;
    }

    /// <summary>One frame's share of the catch-up (see <see cref="StepSimulation"/>).</summary>
    private static void CatchUp(World world)
    {
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            _catchUpLeft = 0f; // skip the rest
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        while (_catchUpLeft > 0f)
        {
            world.Update(SimulationStep);
            world.CommitPendingChanges();
            _catchUpLeft -= SimulationStep;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds > CatchUpFrameSeconds)
                break;
        }
        world.TakeMoments(); // (the headlines of the time away are told in the summary, not one by one)
        if (_catchUpLeft > 0f)
            return;
        _catchUpLeft = 0f;
        _simulationBacklog = 0f;
        SummariseAway(world);
    }

    /// <summary>"While you were away" banners: the headlines of the break, then the tally.</summary>
    private static void SummariseAway(World world)
    {
        float simulated = world.ElapsedSeconds - _awayStartTime;
        if (simulated < 30f)
            return;
        int born = world.Births - _awayBirths, lost = world.Casualties - _awayCasualties;
        var story = world.ChronicleEntries.Where(e => e.Time > _awayStartTime && e.Title is not null).TakeLast(3).ToList();
        _bannerQueue.Clear();
        _banner = null;
        foreach (ChronicleEntry entry in story)
            _bannerQueue.Add(new Moment("While you were away", entry.Text, null, false));
        string tally = $"{simulated / 60f:0} min passed: {born} born, {lost} lost";
        _bannerQueue.Add(new Moment("While you were away", tally, null, false));
        AddEventLog($"[AWAY] {tally}");
    }

    /// <summary>The progress bar over the garden while it catches up.</summary>
    private static void DrawCatchUp()
    {
        if (_catchUpLeft <= 0f)
            return;
        int width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight();
        int text = ScaledFontSize(0.8f), barWidth = Math.Min((int)(width * 0.6f), (int)(700 * UiScale)), barHeight = (int)(26 * UiScale);
        int barX = (width - barWidth) / 2, barY = height / 2;
        Raylib.DrawRectangle(0, 0, width, height, new Color(20, 30, 20, 110));
        DrawCentred("The garden moves on while you were away...", width / 2, barY - text * 2, text, Color.White);
        Raylib.DrawRectangle(barX, barY, barWidth, barHeight, new Color(255, 255, 255, 90));
        float done = 1f - _catchUpLeft / MathF.Max(1f, _catchUpTotal);
        Raylib.DrawRectangle(barX, barY, (int)(barWidth * Math.Clamp(done, 0f, 1f)), barHeight, new Color(150, 215, 110, 255));
        DrawCentred("Tap to skip", width / 2, barY + barHeight + text / 2, (int)(text * 0.7f), new Color(255, 255, 255, 200));
    }
}
