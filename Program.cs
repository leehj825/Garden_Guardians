// =============================================================================
//  Garden Guardians — Emergent Survival Simulation
// -----------------------------------------------------------------------------
//  Design (see Garden_Guardians_Roadmap.md/Garden_Guardians_Design.md):
//    * A fixed isometric camera looking down at a 100 m x 100 m patch of
//      procedurally-hilly terrain, with a mobile-friendly one-finger-pan/
//      two-finger-pinch camera controller layered on top.
//    * No factions, no villages, no economy. The map is just the terrain and
//      whatever loose things live on it: wild Berries (Food), Hornet swarms,
//      a Wolf Spider, burrowing Grubs, and the Bramblekin themselves.
//    * Every Bramblekin is an individual agent with its own randomly rolled
//      Personality (Aggression, Sociability, Intelligence) and a strict
//      hierarchy of needs: Hunger first, then Safety, then Social.
//    * Groups are emergent, not assigned: two Bramblekin that cross paths
//      resolve the encounter from their situation and traits — a starving,
//      aggressive one may rob the other; two sociable ones (or two that are
//      both being hunted) may band together under a shared GroupId, led by
//      whichever member is the most Intelligent.
//    * The player has no lever on the world; the only tap left is inspecting
//      a single Bramblekin (WorldTapInput).
//
//  Safety: entities are created and destroyed constantly (arrivals, predator
//  kills, deaths in combat or to starvation), so every list that can change
//  size mid-frame is either walked with a reverse for-loop or mutated through
//  a deferred pending-add/pending-remove queue processed once at the end of
//  the frame, never directly inside another entity's Update().
//
//  Scale convention: 1 world unit = 1 meter. The terrain is a 100 m x 100 m plane
//  centred on the origin, and "up" is +Y.
//
//  Everything lives in this single file for now; each class is small and
//  self-contained so it can be lifted into its own file once the project
//  graduates into a fuller project structure.
// =============================================================================

using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Desktop entry point. Android starts the game from MainActivity instead
/// (see Platforms/Android/MainActivity.cs); both end up in <see cref="Game.Run"/>.
///
/// <c>--headless [seconds] [--seed N]</c> skips the window entirely and
/// steps the simulation on its own, printing periodic population reports —
/// a quick way to check the survival loop end to end without a GPU.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        int headlessIndex = Array.IndexOf(args, "--headless");
        if (headlessIndex < 0)
        {
            Game.Run(GamePlatform.Desktop);
            return;
        }

        float seconds = 600f;
        if (headlessIndex + 1 < args.Length &&
            float.TryParse(args[headlessIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSeconds))
            seconds = parsedSeconds;

        int? seed = null;
        int seedIndex = Array.IndexOf(args, "--seed");
        if (seedIndex >= 0 && seedIndex + 1 < args.Length && int.TryParse(args[seedIndex + 1], out int parsedSeed))
            seed = parsedSeed;

        Game.RunHeadless(seconds, seed);
    }
}

/// <summary>Which host is running the game; controls a few window settings.</summary>
public enum GamePlatform
{
    Desktop,
    Android,
}

/// <summary>
/// Owns the window and the main loop: reads input, steps the <see cref="World"/>,
/// and draws it with the UI on top. Platform-independent.
/// </summary>
public static class Game
{
    // Window settings for Desktop. Android instead opens at its native
    // screen size (see Run's InitWindow call) so the game fills the whole
    // display with no letterboxing; landscape is locked independently, via
    // MainActivity's ScreenOrientation attribute, not by these numbers.
    private const int ScreenWidth = 1280;
    private const int ScreenHeight = 720;
    private const int TargetFps = 60;

    /// <summary>How many solitary Bramblekin are scattered across the map at the start.</summary>
    private const int InitialKinCount = 20;

    /// <summary>
    /// Largest time step the game simulates in one frame. A hitch (window
    /// dragged, app resumed) would otherwise teleport walkers past obstacles.
    /// </summary>
    private const float MaxDeltaTime = 1f / 20f;

    /// <summary>Debug Time Scale: the speeds the corner +/- buttons step through, clamped at either end.</summary>
    private static readonly float[] TimeScaleSteps = { 1f, 2f, 5f, 10f, 20f };

    /// <summary>
    /// Debug Time Scale. Rather than feeding World.Update() an oversized
    /// deltaTime at high multiples (which would let walkers skip past
    /// obstacles in a single giant step), the main loop below instead calls
    /// World.Update() this many
    /// times per rendered frame, each with its own normal, clamped
    /// deltaTime — every timer, cooldown and movement speed inside it ends
    /// up advancing exactly TimeScale times faster in wall-clock terms,
    /// without ever destabilizing the physics.
    /// </summary>
    private static float _timeScale = 1f;

    /// <summary>
    /// Responsive UI: the screen width every hardcoded UI pixel constant
    /// (the Debug Time Scale buttons' geometry, in particular) was
    /// originally designed/tuned against. <see cref="UiScale"/> divides the
    /// CURRENT screen width by this to get a single scale factor those
    /// constants are multiplied by, so the UI stays proportionally sized —
    /// and, crucially, stays tappable in exactly the place it's drawn — on
    /// any screen instead of only the one it was designed for.
    /// </summary>
    private const float ReferenceScreenWidth = 1920f;

    /// <summary>Current screen width divided by <see cref="ReferenceScreenWidth"/> — see its doc comment.</summary>
    private static float UiScale => Raylib.GetScreenWidth() / ReferenceScreenWidth;

    /// <summary>
    /// The large font size, at the <see cref="ReferenceScreenWidth"/>, that
    /// the bottom stats bar (<see cref="DrawHud"/>) and — a size down — the
    /// Kin Inspector panel (<see cref="DrawKinPanel"/>) are both derived from
    /// via <see cref="ScaledFontSize"/>, so the two can never silently drift
    /// out of sync with each other.
    /// </summary>
    private const int BroadcastFontSize = 48;

    /// <summary><see cref="BroadcastFontSize"/> scaled to the current screen (see <see cref="UiScale"/>) and by <paramref name="factor"/>, never below a legible minimum.</summary>
    private static int ScaledFontSize(float factor = 1f) => Math.Max(14, (int)(BroadcastFontSize * UiScale * factor));

    /// <summary>
    /// On-Screen Debug Console: a rolling log of recent notable events
    /// (alliances, robberies, predator kills, arrivals) rendered directly on
    /// screen (see <see cref="DrawDebugConsole"/>) so a developer/tester can
    /// see what the autonomous simulation is doing without needing adb
    /// logcat or a desktop console attached.
    /// </summary>
    private static readonly List<string> _debugLogs = new();

    /// <summary>Oldest-entries-dropped cap for <see cref="_debugLogs"/> — see <see cref="AddEventLog"/>.</summary>
    private const int DebugLogCapacity = 15;

    /// <summary>True while <see cref="RunHeadless"/> is driving the simulation — event logs go to stdout instead of the on-screen console.</summary>
    private static bool _isHeadless;

    /// <summary>
    /// Appends <paramref name="message"/> to the on-screen debug console
    /// (<see cref="_debugLogs"/>/<see cref="DrawDebugConsole"/>), dropping
    /// the oldest entry once past <see cref="DebugLogCapacity"/>. In
    /// headless mode it's printed to stdout instead.
    /// </summary>
    public static void AddEventLog(string message)
    {
        if (_isHeadless)
        {
            Console.WriteLine("  " + message);
            return;
        }

        _debugLogs.Add(message);
        while (_debugLogs.Count > DebugLogCapacity)
            _debugLogs.RemoveAt(0);
    }

    public static void Run(GamePlatform platform)
    {
        if (platform == GamePlatform.Desktop)
        {
            // MSAA smooths the edges of the spheres and grid lines; resizable
            // lets us test different aspect ratios. Both are skipped on Android,
            // where not every GPU offers a 4x MSAA surface.
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow);
        }

        // Full Screen: Desktop still opens at the fixed ScreenWidth/Height
        // (the window is resizable from there). On Android, passing 0x0
        // tells raylib to use the device's actual native surface size
        // instead of a fixed 1280x720 virtual canvas letterboxed to fit —
        // every UI/culling call already reads back Raylib.GetScreenWidth()/
        // GetScreenHeight() rather than the ScreenWidth/ScreenHeight
        // constants, so it fills whatever size that turns out to be.
        if (platform == GamePlatform.Android)
            Raylib.InitWindow(0, 0, "Garden Guardians");
        else
            Raylib.InitWindow(ScreenWidth, ScreenHeight, "Garden Guardians");
        Raylib.SetTargetFPS(TargetFps);

        // --- Build the world -------------------------------------------------
        // The God-Camera: pulled back and up far enough to take in the
        // entire 100x100 map at once. Terrain is centered on the origin
        // (it spans -50..50 on X/Z — see Terrain.Contains), so the true
        // map centre is Vector3.Zero.
        var camera = new Camera3D
        {
            Target = Vector3.Zero,
            Position = new Vector3(0.0f, 120.0f, 100.0f),
            Up = Vector3.UnitY,
            FovY = 45f,
            Projection = CameraProjection.Perspective,
        };
        var world = new World(new Terrain(size: 100f), new Random(), InitialKinCount);
        var input = new WorldTapInput();
        var touchCamera = new TouchCameraController();

        // --- Main loop -------------------------------------------------------
        while (!Raylib.WindowShouldClose())
        {
            float rawDeltaTime = MathF.Min(Raylib.GetFrameTime(), MaxDeltaTime);

            // 0) Spectator Camera: one finger (or a held mouse button) drags
            //    to pan, two fingers twist to rotate around the current
            //    Target and pinch to zoom. Runs before the tap input below
            //    so the rest of the frame sees an already-settled camera.
            touchCamera.Update(ref camera, world.Terrain.Size / 2f);

            // Responsive UI: the Debug Time Scale buttons' geometry (and the
            // "Nx" label panel between them) is recomputed from the CURRENT
            // screen size every frame, via the shared UiScale factor, rather
            // than fixed once at startup. Both the click-detection below and
            // the Draw calls further down use these SAME rectangles, so the
            // visible buttons and their tappable areas can never drift apart.
            float uiScale = UiScale;
            int speedButtonWidth = (int)(150 * uiScale);
            int speedButtonHeight = (int)(132 * uiScale);
            int speedButtonGap = (int)(180 * uiScale);
            int speedButtonMargin = (int)(20 * uiScale);
            var speedDownButton = new UiButton(new Rectangle(speedButtonMargin, speedButtonMargin, speedButtonWidth, speedButtonHeight));
            var speedUpButton = new UiButton(new Rectangle(speedButtonMargin + speedButtonWidth + speedButtonGap, speedButtonMargin, speedButtonWidth, speedButtonHeight));
            var speedLabelBounds = new Rectangle(speedButtonMargin + speedButtonWidth, speedButtonMargin, speedButtonGap, speedButtonHeight);

            // 1) Input: the player has no lever on the world. The only tap
            //    left is inspecting a single Bramblekin (see WorldTapInput,
            //    which only fires on a clean release that never turned into
            //    a pan). The Debug Time Scale +/- buttons are checked first
            //    and, if hit, swallow the click so it never also lands as a
            //    ground tap.
            bool mousePressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
            Vector2 mousePosition = Raylib.GetMousePosition();
            if (mousePressed && speedDownButton.Contains(mousePosition))
                DecreaseTimeScale();
            else if (mousePressed && speedUpButton.Contains(mousePosition))
                IncreaseTimeScale();
            else
                input.Update(camera, world);

            // 2) Simulation: TimeScale runs World.Update() several times per
            //    rendered frame (see _timeScale's own doc comment) rather
            //    than scaling deltaTime itself.
            int simulationSteps = Math.Max(1, (int)MathF.Round(_timeScale));
            for (int step = 0; step < simulationSteps; step++)
                world.Update(rawDeltaTime);

            // 3) Rendering.
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(135, 190, 235, 255)); // Sky blue.

            Raylib.BeginMode3D(camera);
            world.Draw(camera);
            Raylib.EndMode3D();

            // 2D overlay (UI) is drawn after EndMode3D so it sits on top.
            DrawStatusBars(camera, world);
            DrawFloatingTexts(camera, world);
            speedDownButton.Draw("-", highlighted: false, disabled: _timeScale <= TimeScaleSteps[0]);
            DrawSpeedLabel(speedLabelBounds, uiScale);
            speedUpButton.Draw("+", highlighted: false, disabled: _timeScale >= TimeScaleSteps[^1]);
            DrawKinPanel(world);
            DrawHud(world);
            DrawDebugConsole();

            Raylib.EndDrawing();

            // 4) Deferred spawns/removals: applied once here, after this
            //    frame's Update() and Draw() have both fully run, so no
            //    entity list ever changes size while something is iterating
            //    it (an arrival mid-Colony-update, a kill mid-pounce, etc).
            world.CommitPendingChanges();
        }

        Raylib.CloseWindow();
    }

    /// <summary>
    /// Headless Simulation: steps a fresh <see cref="World"/> at a fixed
    /// 60 Hz for <paramref name="simulatedSeconds"/> of game time with no
    /// window (and no raylib native calls at all), printing a population
    /// report every <c>reportInterval</c> seconds and every logged event as
    /// it happens. Handy for checking the survival loop on a machine with no
    /// display, and for tuning — pass a <paramref name="seed"/> to replay
    /// the exact same run.
    /// </summary>
    public static void RunHeadless(float simulatedSeconds, int? seed)
    {
        _isHeadless = true;
        const float step = 1f / 60f;
        const float reportInterval = 30f;

        var world = new World(new Terrain(size: 100f), seed is { } s ? new Random(s) : new Random(), InitialKinCount);
        Console.WriteLine($"Garden Guardians headless run: {simulatedSeconds:0}s simulated, seed {(seed?.ToString() ?? "random")}");
        PrintReport(world);

        float reportTimer = 0f;
        while (world.ElapsedSeconds < simulatedSeconds)
        {
            world.Update(step);
            world.CommitPendingChanges();

            reportTimer += step;
            if (reportTimer >= reportInterval)
            {
                reportTimer -= reportInterval;
                PrintReport(world);
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        PrintReport(world);
        Console.WriteLine(
            $"Food eaten {world.FoodEaten}, shared {world.FoodShared}, stolen {world.Thefts}; " +
            $"alliances {world.AlliancesFormed}; grubs hunted {world.GrubsKilled}, hornets swatted {world.HornetsKilled}, spiders slain {world.SpidersKilled}.");
    }

    /// <summary>One line of headless-mode population stats — see <see cref="RunHeadless"/>.</summary>
    private static void PrintReport(World world)
    {
        List<Bramblekin> living = world.Colony.Where(b => !b.IsDead).ToList();
        float averageHunger = living.Count > 0 ? living.Average(b => b.Hunger) : 0f;
        int solitary = living.Count(b => b.GroupId is null);
        int largestGroup = world.Groups.Count > 0 ? world.Groups.Max(g => g.Members.Count) : 0;
        Console.WriteLine(
            $"[t={world.ElapsedSeconds,6:0}s] kin {living.Count,3} (solitary {solitary}, groups {world.Groups.Count}, largest {largestGroup}) " +
            $"avg hunger {averageHunger,5:0.0}  food on map {world.LooseFoodCount,3}  " +
            $"arrived {world.Arrivals}  died: starved {world.DeathsByStarvation}, predators {world.DeathsByPredator}, kin {world.DeathsByKin}");
    }

    /// <summary>Debug Time Scale: steps down to the previous speed in <see cref="TimeScaleSteps"/>, clamped at 1x.</summary>
    private static void DecreaseTimeScale()
    {
        int index = Array.IndexOf(TimeScaleSteps, _timeScale);
        _timeScale = TimeScaleSteps[Math.Max(0, index - 1)];
    }

    /// <summary>Debug Time Scale: steps up to the next speed in <see cref="TimeScaleSteps"/>, clamped at the fastest.</summary>
    private static void IncreaseTimeScale()
    {
        int index = Array.IndexOf(TimeScaleSteps, _timeScale);
        _timeScale = TimeScaleSteps[Math.Min(TimeScaleSteps.Length - 1, index + 1)];
    }

    /// <summary>The current speed ("5x"), on a small panel in the gap between the +/- buttons — gold once sped up.</summary>
    private static void DrawSpeedLabel(Rectangle bounds, float uiScale)
    {
        // Responsive UI: bounds is the exact same, freshly-scaled rectangle
        // Run computes each frame for this gap between speedDownButton and
        // speedUpButton (see UiScale), so this panel keeps filling that gap
        // exactly regardless of screen size; only the font size (not tied to
        // a UiButton's own height like the +/- labels are) needs its own
        // scale multiply here.
        int fontSize = (int)(64 * uiScale);
        int x = (int)bounds.X, y = (int)bounds.Y, width = (int)bounds.Width, height = (int)bounds.Height;
        Raylib.DrawRectangle(x, y, width, height, PanelFill);
        Raylib.DrawRectangleLines(x, y, width, height, PanelInk);

        string text = $"{(int)_timeScale}x";
        int textWidth = Raylib.MeasureText(text, fontSize);
        Color color = _timeScale != 1f ? new Color(230, 190, 60, 255) : PanelInk;
        Raylib.DrawText(text, x + (width - textWidth) / 2, y + (height - fontSize) / 2, fontSize, color);
    }

    private static readonly Color PanelFill = new(255, 250, 235, 220);
    private static readonly Color PanelInk = new(110, 70, 35, 255);
    private static readonly Color HungerBarColor = new(235, 150, 40, 255);

    /// <summary>
    /// Health and hunger bars for every living Bramblekin and the Wolf
    /// Spider, each projected from its 3D position into 2D screen space. A
    /// health bar only appears while it's actually missing Health, and a
    /// hunger bar only while the Bramblekin is hungry, so the map doesn't
    /// get cluttered by default.
    /// </summary>
    private static void DrawStatusBars(Camera3D camera, World world)
    {
        for (int i = 0; i < world.Colony.Count; i++)
        {
            Bramblekin b = world.Colony[i];
            // Raylib Culling: a Bramblekin entirely outside the camera's
            // current view has no business drawing a status bar either.
            if (b.IsDead || !IsPointOnScreen(camera, b.Position))
                continue;

            Vector3 barAnchor = b.Position + new Vector3(0, Bramblekin.BodyHeight + 0.15f, 0);
            if (b.Health < Bramblekin.MaxHealth)
                DrawBar(camera, barAnchor, 0, (float)b.Health / Bramblekin.MaxHealth, Color.Green);
            if (b.IsHungry)
                DrawBar(camera, barAnchor, 7, 1f - b.Hunger / Bramblekin.MaxHunger, HungerBarColor);
        }

        if (world.Spider is { IsDead: false } spider && spider.Health < WolfSpider.MaxHealth)
            DrawBar(camera, spider.Position + new Vector3(0, WolfSpider.BodyRadius * 2f + 0.3f, 0), 0, (float)spider.Health / WolfSpider.MaxHealth, Color.Green);
    }

    /// <summary>Basic bounds check: true unless <paramref name="worldPosition"/> projects to a screen point entirely outside the camera's current viewport — used to skip status-bar/UI draw calls for off-screen entities.</summary>
    private static bool IsPointOnScreen(Camera3D camera, Vector3 worldPosition)
    {
        const float margin = 40f;
        Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);
        return screen.X >= -margin && screen.X <= Raylib.GetScreenWidth() + margin &&
               screen.Y >= -margin && screen.Y <= Raylib.GetScreenHeight() + margin;
    }

    /// <summary>A small red-background bar filled to <paramref name="fraction"/> at <paramref name="worldPosition"/>'s projected screen point, <paramref name="yOffset"/> pixels below it.</summary>
    private static void DrawBar(Camera3D camera, Vector3 worldPosition, int yOffset, float fraction, Color fillColor)
    {
        Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);
        const int width = 34, height = 5;
        var back = new Rectangle(screen.X - width / 2f, screen.Y - height / 2f + yOffset, width, height);
        Raylib.DrawRectangleRec(back, Color.Red);
        var fill = back with { Width = back.Width * Math.Clamp(fraction, 0f, 1f) };
        Raylib.DrawRectangleRec(fill, fillColor);
        Raylib.DrawRectangleLinesEx(back, 1f, Color.Black);
    }

    /// <summary>
    /// Social pop-ups ("+Ally", "Stolen!", "Shared"): each rises and fades
    /// above the Bramblekin it happened to over its lifetime.
    /// </summary>
    private static void DrawFloatingTexts(Camera3D camera, World world)
    {
        const int fontSize = 20;
        foreach (var text in world.FloatingTexts)
        {
            float age = World.FloatingTextDuration - text.TimeLeft;
            Vector3 worldPosition = text.Position + new Vector3(0, Bramblekin.BodyHeight + 0.4f + age * 0.6f, 0);
            if (!IsPointOnScreen(camera, worldPosition))
                continue;

            Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);
            byte alpha = (byte)(255 * Math.Clamp(text.TimeLeft / World.FloatingTextDuration, 0f, 1f));
            var color = new Color(text.Color.R, text.Color.G, text.Color.B, alpha);
            int width = Raylib.MeasureText(text.Text, fontSize);
            Raylib.DrawText(text.Text, (int)(screen.X - width / 2f), (int)screen.Y, fontSize, color);
        }
    }

    /// <summary>
    /// Kin Inspector: the selected Bramblekin's (tap one — see
    /// <see cref="World.TrySelectKinAt"/>) vitals, Personality, group role
    /// and relationships, top right. Replaces the old per-faction ledger;
    /// with nothing selected it's just a one-line hint.
    /// </summary>
    private static void DrawKinPanel(World world)
    {
        int fontSize = ScaledFontSize(0.7f);
        int lineHeight = fontSize + fontSize / 5;
        const int topPadding = 20, margin = 30, inset = 12;

        Bramblekin? kin = world.SelectedKin;
        if (kin is null || kin.IsDead)
        {
            const string hint = "Tap a Bramblekin to inspect it";
            int hintWidth = Raylib.MeasureText(hint, fontSize);
            int hintX = Raylib.GetScreenWidth() - hintWidth - margin;
            Raylib.DrawRectangle(hintX - inset, topPadding, hintWidth + inset * 2, fontSize + inset * 2, PanelFill);
            Raylib.DrawText(hint, hintX, topPadding + inset, fontSize, PanelInk);
            return;
        }

        KinGroup? group = world.GroupOf(kin);
        Color fill = group is null ? PanelFill : BlendToward(PanelFill, group.Color, 0.35f);
        Color ink = group is null ? PanelInk : BlendToward(PanelInk, group.Color, 0.35f);

        string role = group is null ? "Solitary" : group.Leader == kin ? "Leader" : "Follower";
        int friends = kin.KnownKins.Values.Count(r => r == RelationshipState.Friend);
        int enemies = kin.KnownKins.Values.Count(r => r == RelationshipState.Enemy);
        int neutral = kin.KnownKins.Values.Count(r => r == RelationshipState.Neutral);

        var lines = new List<(string Text, Color Color)>
        {
            ($"Bramblekin #{kin.ID} ({role})", ink),
            ($"State: {kin.State}", ink),
            ($"Health: {kin.Health} / {Bramblekin.MaxHealth}", ink),
            ($"Hunger: {(int)kin.Hunger}%{(kin.IsStarving ? " STARVING" : kin.IsHungry ? " (hungry)" : "")}{(kin.HasFood ? "  +food" : "")}",
                kin.IsStarving ? new Color(170, 60, 40, 255) : ink),
            ($"Aggression:   {kin.Personality.Aggression:0.00}", new Color(185, 60, 45, 255)),
            ($"Sociability:  {kin.Personality.Sociability:0.00}", new Color(60, 130, 70, 255)),
            ($"Intelligence: {kin.Personality.Intelligence:0.00} ({kin.DetectionRadius:0}m)", new Color(60, 100, 170, 255)),
            (group is null ? "Group: none" : $"Group {group.ShortId}: {group.Members.Count} members", ink),
            ($"Known: {friends} friend, {enemies} enemy, {neutral} neutral", ink),
        };

        // Sized to its widest line, so no stat ever runs off the panel.
        int width = lines.Max(line => Raylib.MeasureText(line.Text, fontSize));
        int height = lineHeight * lines.Count - (lineHeight - fontSize);
        int x = Raylib.GetScreenWidth() - width - margin;
        Raylib.DrawRectangle(x - inset, topPadding, width + inset * 2, height + inset * 2, fill);
        Raylib.DrawRectangleLines(x - inset, topPadding, width + inset * 2, height + inset * 2, ink);
        for (int i = 0; i < lines.Count; i++)
            Raylib.DrawText(lines[i].Text, x, topPadding + inset + lineHeight * i, fontSize, lines[i].Color);
    }

    /// <summary>Blends <paramref name="baseColor"/> toward <paramref name="tint"/> by <paramref name="amount"/> (0 = unchanged, 1 = fully tint), keeping <paramref name="baseColor"/>'s own alpha.</summary>
    private static Color BlendToward(Color baseColor, Color tint, float amount) => new(
        (byte)Math.Clamp(baseColor.R + (tint.R - baseColor.R) * amount, 0, 255),
        (byte)Math.Clamp(baseColor.G + (tint.G - baseColor.G) * amount, 0, 255),
        (byte)Math.Clamp(baseColor.B + (tint.B - baseColor.B) * amount, 0, 255),
        baseColor.A);

    /// <summary>Sim status, who's alive and in which groups, what they're doing right now, and the running tallies, in a bar along the bottom of the screen.</summary>
    private static void DrawHud(World world)
    {
        int Count(BramblekinState state) => world.Colony.Count(b => !b.IsDead && b.State == state);

        int living = world.Colony.Count(b => !b.IsDead);
        int solitary = world.Colony.Count(b => !b.IsDead && b.GroupId is null);
        int largestGroup = world.Groups.Count > 0 ? world.Groups.Max(g => g.Members.Count) : 0;

        // Short lines rather than one or two wide ones, so the bar fits the
        // screen at any size (the font scales with UiScale).
        string[] lines =
        {
            $"Speed {_timeScale}x   FPS {Raylib.GetFPS()}   Food on map {world.LooseFoodCount}   Spider: {SpiderStatus(world)}",
            $"Bramblekin {living}: {solitary} solitary, {world.Groups.Count} groups (largest {largestGroup})",
            $"Foraging {Count(BramblekinState.Foraging) + Count(BramblekinState.Hunting)}   Eating {Count(BramblekinState.Eating)}   " +
            $"Fleeing {Count(BramblekinState.Fleeing)}   Fighting {Count(BramblekinState.Fighting)}   Robbing {Count(BramblekinState.Attacking)}",
            $"Arrived {world.Arrivals}   Starved {world.DeathsByStarvation}   Killed by predators {world.DeathsByPredator}, by kin {world.DeathsByKin}   Thefts {world.Thefts}",
        };

        // UI Text Scaling: a background bar goes underneath, sized off
        // fontSize/lineHeight, so the text stays legible over a busy map.
        int fontSize = ScaledFontSize();
        int lineHeight = fontSize + fontSize / 6;
        int barHeight = lineHeight * lines.Length + 20;
        int y = Raylib.GetScreenHeight() - barHeight + 10;
        Raylib.DrawRectangle(0, y - 10, Raylib.GetScreenWidth(), barHeight, new Color(0, 0, 0, 90));
        for (int i = 0; i < lines.Length; i++)
            Raylib.DrawText(lines[i], 20, y + lineHeight * i, fontSize, Color.RayWhite);
    }

    private static string SpiderStatus(World world) =>
        world.Spider is { IsDead: false } spider
            ? spider.State.ToString()
            : $"slain ({MathF.Ceiling(world.SpiderRespawnTimer)}s)";

    /// <summary>
    /// On-Screen Debug Console: renders <see cref="_debugLogs"/> (see
    /// <see cref="AddEventLog"/>) as a small, semi-transparent panel on the
    /// middle-left of the screen — a running history of recent notable
    /// events for on-device debugging. Newest entry at the bottom, oldest at
    /// top, matching the natural reading order of a scrolling log.
    /// </summary>
    private static void DrawDebugConsole()
    {
        if (_debugLogs.Count == 0)
            return;

        // Readability: scaled by UiScale like the rest of this file's
        // responsive UI rather than a fixed pixel size, so it stays legible
        // at any screen size.
        int fontSize = (int)(18 * UiScale);
        int lineHeight = fontSize + 4;

        // Word-Wrap: each stored entry is greedily word-wrapped at render
        // time into as many visual lines as it takes to stay under maxWidth.
        int maxWidth = (int)(400 * UiScale);
        var wrappedLines = new List<string>();
        for (int i = 0; i < _debugLogs.Count; i++)
            WrapLine(_debugLogs[i], fontSize, maxWidth, wrappedLines);

        // Only the newest lines that fit in the middle of the screen, so a
        // burst of long entries never grows the panel into the HUD below.
        int maxLines = Math.Max(1, (int)(Raylib.GetScreenHeight() * 0.45f) / lineHeight);
        if (wrappedLines.Count > maxLines)
            wrappedLines.RemoveRange(0, wrappedLines.Count - maxLines);

        int widestLine = 0;
        for (int i = 0; i < wrappedLines.Count; i++)
            widestLine = Math.Max(widestLine, Raylib.MeasureText(wrappedLines[i], fontSize));

        int width = widestLine + 16;
        int height = wrappedLines.Count * lineHeight + 16;
        int x = 10;
        int y = (Raylib.GetScreenHeight() - height) / 2;

        Raylib.DrawRectangle(x, y, width, height, new Color(0, 0, 0, 150));
        Raylib.DrawRectangleLines(x, y, width, height, new Color(255, 255, 255, 60));

        for (int i = 0; i < wrappedLines.Count; i++)
            Raylib.DrawText(wrappedLines[i], x + 8, y + 8 + i * lineHeight, fontSize, Color.RayWhite);
    }

    /// <summary>
    /// A simple greedy word-wrap for <see cref="DrawDebugConsole"/> —
    /// accumulates whitespace-separated words from <paramref name="line"/>
    /// into a single visual line until adding the next word would push its
    /// measured width past <paramref name="maxWidth"/>, then starts a new
    /// one. A single word wider than <paramref name="maxWidth"/> on its own
    /// is still emitted whole rather than dropped or clipped.
    /// </summary>
    private static void WrapLine(string line, int fontSize, int maxWidth, List<string> output)
    {
        string[] words = line.Split(' ');
        var current = new System.Text.StringBuilder();
        foreach (string word in words)
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && Raylib.MeasureText(candidate, fontSize) > maxWidth)
            {
                output.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
            else
            {
                current.Clear();
                current.Append(candidate);
            }
        }
        if (current.Length > 0)
            output.Add(current.ToString());
    }
}

// =============================================================================
//  Camera
// =============================================================================

/// <summary>
/// The Spectator Camera: a Google Maps-style controller for the fixed
/// overhead view — Pure Simulation means the player has no lever on the
/// world any more, just on how they're looking at it. One finger (or a
/// held left mouse button, for testing on desktop) drags to pan across the
/// terrain's X/Z plane; two fingers twisting around each other rotates the
/// whole world around the camera's own Target on the Y axis; two fingers
/// pinching in/out zooms; and two fingers sliding up or down together
/// tilts the camera's pitch. <see cref="WorldTapInput"/> still gets a
/// clean, undragged tap for inspecting a Bramblekin — see its own
/// drag-threshold check — so this and that never fight over the same
/// touch.
/// </summary>
public sealed class TouchCameraController
{
    /// <summary>Closest the camera may zoom in, in meters from its Target.</summary>
    private const float MinZoomDistance = 8f;

    /// <summary>
    /// Furthest the camera may zoom out, in meters from its Target. Must
    /// stay above the God-Camera's starting distance (~156m — see
    /// Game.Run's initial Camera3D) or the very first pinch clamps the
    /// camera to this ceiling immediately, which reads as a sudden
    /// snap-zoom-in that a further pinch-out can never undo.
    /// </summary>
    private const float MaxZoomDistance = 220f;

    /// <summary>How many meters of pinch-distance change it takes to move the camera one meter.</summary>
    private const float PinchZoomSensitivity = 0.05f;

    /// <summary>Keeps the Target from panning off the playable terrain, in meters from its edge.</summary>
    private const float PanEdgeMargin = 5f;

    /// <summary>
    /// Camera Sensitivity Tuning: the single knob on One-Finger Panning's
    /// overall feel — multiplies the already-distance-scaled screen delta
    /// (see <see cref="Pan"/>) before it's ever added to camera.Position/
    /// camera.Target. Turn this down if panning still feels too fast at
    /// every zoom level, up if it feels sluggish.
    /// </summary>
    private const float PanSensitivity = 0.12f;

    /// <summary>
    /// Camera Sensitivity Tuning: the single knob on Two-Finger Rotation's
    /// overall feel — multiplies the raw angle delta between the two touch
    /// points (see <see cref="Rotate"/>) before it's applied. Kept low: the
    /// raw angle between two close-together fingers swings wildly for even
    /// a small physical movement, so without this a twist gesture rotates
    /// the world far more than the fingers actually moved.
    /// </summary>
    private const float RotationSensitivity = 0.3f;

    // Camera-gesture state, tracked frame to frame. One controller is
    // constructed once in Game.Run and lives for the whole session, so
    // instance fields here serve exactly the same purpose static fields
    // would in a single long-running loop, without reaching for actual
    // global/static mutable state.
    private Vector2 _lastTouchPos;
    private bool _isOneFingerGesture;

    private float _lastTouchAngle;
    private float _lastPinchDistance;
    private float _lastTwoFingerMidpointY;
    private bool _isTwoFingerGesture;

    // The Flip Fix: raylib reports touch points by index (0, 1, ...), but
    // which physical finger gets which index is NOT stable frame to frame —
    // the OS/driver can silently swap them mid-gesture. Since the angle
    // between the two points flips by ~180° the instant "first" and
    // "second" swap (Atan2 of a negated vector), that swap alone was
    // enough to make the world appear to suddenly flip during a twist —
    // and, because a real vertical two-finger drag is never perfectly
    // symmetric, during a tilt too. UpdateTwoFingerGesture instead matches
    // this frame's two points to whichever of last frame's it's actually
    // closest to, so "first"/"second" stay tied to the same physical
    // finger regardless of what order raylib reports them in.
    private Vector2 _lastFirstPos;
    private Vector2 _lastSecondPos;

    /// <summary>
    /// Radians of camera tilt (pitch) per pixel the two-finger midpoint
    /// moves vertically. Camera Sensitivity Tuning: the up/down half of
    /// two-finger orbiting, so it's damped by the same <see cref="RotationSensitivity"/>
    /// as the left/right twist — see <see cref="Tilt"/>.
    /// </summary>
    private const float TiltSensitivity = 0.005f;

    /// <summary>
    /// Steepest the camera may tilt down toward the horizon, in radians
    /// above it. Kept well clear of 0 (dead level, which would put the
    /// horizon in frame and let Position dip toward/through the ground)
    /// and of a perfect 90° top-down (where azimuth becomes meaningless).
    /// </summary>
    private const float MinPitch = 0.26f; // ~15 degrees.

    /// <summary>Flattest the camera may tilt toward straight-down.</summary>
    private const float MaxPitch = 1.48f; // ~85 degrees.

    public void Update(ref Camera3D camera, float worldHalfSize)
    {
        int touchCount = Raylib.GetTouchPointCount();

        if (touchCount >= 2)
        {
            UpdateTwoFingerGesture(ref camera);
            _isOneFingerGesture = false; // A second finger landing mid-pan shouldn't jump-pan once it lifts back to one.
        }
        else
        {
            UpdateOneFingerPan(ref camera, touchCount);
            _isTwoFingerGesture = false;
        }

        ClampTargetToWorld(ref camera, worldHalfSize);
    }

    /// <summary>
    /// One-Finger Panning: follows a single touch, or (for desktop testing)
    /// a held left mouse button — Raylib maps a primary touch to the left
    /// mouse button anyway, so touchCount == 1 and IsMouseButtonDown both
    /// read true together on an actual phone; this just means either is
    /// enough to drive it.
    /// </summary>
    private void UpdateOneFingerPan(ref Camera3D camera, int touchCount)
    {
        bool isDown = touchCount == 1 || Raylib.IsMouseButtonDown(MouseButton.Left);
        if (!isDown)
        {
            _isOneFingerGesture = false;
            return;
        }

        Vector2 currentPos = touchCount == 1 ? Raylib.GetTouchPosition(0) : Raylib.GetMousePosition();
        if (_isOneFingerGesture)
            Pan(ref camera, currentPos - _lastTouchPos);

        _lastTouchPos = currentPos;
        _isOneFingerGesture = true;
    }

    /// <summary>
    /// Two-Finger Rotation + Tilt + the old pinch-zoom, all read off the
    /// same two touch points: the angle between them drives yaw, the
    /// distance between them drives zoom (as before), and — since both of
    /// those are already relative-to-each-other measures — the midpoint's
    /// own vertical movement (both fingers sliding up or down together) is
    /// free to drive pitch without fighting either one.
    /// </summary>
    private void UpdateTwoFingerGesture(ref Camera3D camera)
    {
        Vector2 pointA = Raylib.GetTouchPosition(0);
        Vector2 pointB = Raylib.GetTouchPosition(1);

        // The Flip Fix: raylib's index-to-finger assignment isn't stable
        // frame to frame, so pick whichever of the two possible pairings
        // (A/B as-is, or swapped) keeps each point closest to where it
        // already was last frame, rather than trusting index order.
        Vector2 first = pointA;
        Vector2 second = pointB;
        if (_isTwoFingerGesture)
        {
            float straight = Vector2.DistanceSquared(pointA, _lastFirstPos) + Vector2.DistanceSquared(pointB, _lastSecondPos);
            float swapped = Vector2.DistanceSquared(pointA, _lastSecondPos) + Vector2.DistanceSquared(pointB, _lastFirstPos);
            if (swapped < straight)
            {
                first = pointB;
                second = pointA;
            }
        }

        float angle = MathF.Atan2(second.Y - first.Y, second.X - first.X);
        float distance = Vector2.Distance(first, second);
        float midpointY = (first.Y + second.Y) / 2f;

        if (_isTwoFingerGesture)
        {
            // The Other Flip: Atan2 only ever returns a value in (-π, π],
            // so the instant the two-finger vector swings past that
            // branch cut (pointing due "west" on screen — easily crossed
            // mid-rotation), raw angle jumps from just under +π to just
            // over -π (or back), a spurious ~2π delta that would otherwise
            // get applied as a huge, instant rotation. Wrapping the delta
            // back into (-π, π] turns that into the tiny real delta it
            // actually was.
            float rawDelta = angle - _lastTouchAngle;
            float angleDelta = rawDelta - MathF.Tau * MathF.Round(rawDelta / MathF.Tau);

            // Negated: dragging clockwise should turn the world clockwise
            // beneath the camera, not the reverse.
            Rotate(ref camera, -angleDelta * RotationSensitivity);
            Zoom(ref camera, distance - _lastPinchDistance);
            Tilt(ref camera, midpointY - _lastTwoFingerMidpointY);
        }

        _lastTouchAngle = angle;
        _lastPinchDistance = distance;
        _lastTwoFingerMidpointY = midpointY;
        _lastFirstPos = first;
        _lastSecondPos = second;
        _isTwoFingerGesture = true;
    }

    /// <summary>Translates Position and Target together across the ground plane, following the drag.</summary>
    private static void Pan(ref Camera3D camera, Vector2 screenDelta)
    {
        if (screenDelta == Vector2.Zero)
            return;

        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));

        // Flatten both basis vectors onto the X/Z plane: dragging the finger
        // should slide the camera across the terrain, not up into the sky.
        var forwardXZ = new Vector3(forward.X, 0, forward.Z);
        var rightXZ = new Vector3(right.X, 0, right.Z);
        if (forwardXZ.LengthSquared() > 1e-6f) forwardXZ = Vector3.Normalize(forwardXZ);
        if (rightXZ.LengthSquared() > 1e-6f) rightXZ = Vector3.Normalize(rightXZ);

        // Scale by how far back the camera is sitting, so a zoomed-out view
        // (which shows more ground per pixel) still pans at a matching
        // on-screen speed instead of feeling sluggish.
        float distance = Vector3.Distance(camera.Position, camera.Target);
        float metersPerPixel = distance * 0.0016f;

        // Dragging a finger right/up should slide the world the same way
        // under it, which means moving the camera left/back. Camera
        // Sensitivity Tuning: PanSensitivity is the final overall-feel
        // multiplier, applied on top of the distance-based scaling above.
        Vector3 worldDelta = (-rightXZ * screenDelta.X + forwardXZ * screenDelta.Y) * metersPerPixel * PanSensitivity;
        camera.Position += worldDelta;
        camera.Target += worldDelta;
    }

    /// <summary>
    /// Two-Finger Rotation: spins Position around Target strictly on the Y
    /// axis by <paramref name="angleDelta"/> radians (standard 2D rotation
    /// applied to the X/Z offset) — the world appears to turn beneath a
    /// camera that stays locked on the same focus point, height unchanged.
    /// </summary>
    private static void Rotate(ref Camera3D camera, float angleDelta)
    {
        if (angleDelta == 0f)
            return;

        Vector3 offset = camera.Position - camera.Target;
        float cos = MathF.Cos(angleDelta);
        float sin = MathF.Sin(angleDelta);
        var rotatedOffset = new Vector3(
            offset.X * cos - offset.Z * sin,
            offset.Y,
            offset.X * sin + offset.Z * cos);
        camera.Position = camera.Target + rotatedOffset;
    }

    /// <summary>
    /// Two-Finger Tilt: both fingers sliding up or down together changes
    /// the camera's pitch (its elevation angle above the Target) while
    /// holding its distance and azimuth (compass direction around the
    /// Target) fixed — dragging down flattens toward a top-down view,
    /// dragging up tilts it into a lower, more oblique angle. Clamped to
    /// [MinPitch, MaxPitch] so it can never flatten past dead-level (which
    /// would put the horizon in frame) or flip past straight-down.
    /// </summary>
    private static void Tilt(ref Camera3D camera, float midpointDeltaY)
    {
        if (midpointDeltaY == 0f)
            return;

        Vector3 offset = camera.Position - camera.Target;
        float distance = offset.Length();
        if (distance < 1e-4f)
            return;

        float horizontalDistance = MathF.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
        float azimuth = MathF.Atan2(offset.Z, offset.X);
        float pitch = Math.Clamp(MathF.Atan2(offset.Y, horizontalDistance) + midpointDeltaY * TiltSensitivity * RotationSensitivity, MinPitch, MaxPitch);

        float newHorizontalDistance = distance * MathF.Cos(pitch);
        camera.Position = camera.Target + new Vector3(
            newHorizontalDistance * MathF.Cos(azimuth),
            distance * MathF.Sin(pitch),
            newHorizontalDistance * MathF.Sin(azimuth));
    }

    /// <summary>Moves Position along the Target->Position axis: fingers spreading apart zooms in.</summary>
    private static void Zoom(ref Camera3D camera, float pinchDistanceDelta)
    {
        if (pinchDistanceDelta == 0f)
            return;

        Vector3 offset = camera.Position - camera.Target;
        float distance = offset.Length();
        if (distance < 1e-4f)
            return;

        Vector3 direction = offset / distance;
        float newDistance = Math.Clamp(distance - pinchDistanceDelta * PinchZoomSensitivity, MinZoomDistance, MaxZoomDistance);
        camera.Position = camera.Target + direction * newDistance;
    }

    /// <summary>Keeps the camera's Target from drifting off the playable terrain.</summary>
    private static void ClampTargetToWorld(ref Camera3D camera, float worldHalfSize)
    {
        float limit = worldHalfSize + PanEdgeMargin;
        var clampedTarget = new Vector3(
            Math.Clamp(camera.Target.X, -limit, limit),
            camera.Target.Y,
            Math.Clamp(camera.Target.Z, -limit, limit));

        Vector3 correction = clampedTarget - camera.Target;
        if (correction == Vector3.Zero)
            return;

        camera.Target += correction;
        camera.Position += correction;
    }
}


// =============================================================================
//  Terrain
// =============================================================================

/// <summary>
/// The flat backyard ground: a green plane lying on y = 0 with a grid overlay
/// so that scale (1 cell = 1 meter) is easy to read.
/// </summary>
public sealed class Terrain
{
    /// <summary>The terrain surface height. Everything rests on this plane.</summary>
    public const float GroundHeight = 0f;

    /// <summary>Edge length of the square terrain, in meters.</summary>
    public float Size { get; }

    public Terrain(float size) => Size = size;

    /// <summary>True if the (x, z) point lies on the terrain surface.</summary>
    public bool Contains(Vector3 point)
    {
        float half = Size / 2f;
        return point.X >= -half && point.X <= half && point.Z >= -half && point.Z <= half;
    }

    /// <summary>
    /// True if the (x, z) point is on the terrain and at least
    /// <paramref name="margin"/> meters away from every edge.
    /// </summary>
    public bool Contains(Vector3 point, float margin)
    {
        float half = Size / 2f - margin;
        return point.X >= -half && point.X <= half && point.Z >= -half && point.Z <= half;
    }

    /// <summary>A uniformly random ground point, keeping <paramref name="margin"/> meters from the edges.</summary>
    public Vector3 RandomPoint(Random rng, float margin)
    {
        float half = Size / 2f - margin;
        float x = (float)(rng.NextDouble() * 2 - 1) * half;
        float z = (float)(rng.NextDouble() * 2 - 1) * half;
        return new Vector3(x, GroundHeight, z);
    }

    /// <summary>
    /// Intersects a ray with the infinite horizontal plane y = GroundHeight.
    /// Returns null if the ray is parallel to the plane, points away from it,
    /// or hits it outside the terrain bounds.
    /// </summary>
    public Vector3? Raycast(Ray ray)
    {
        Vector3? hit = RaycastGroundPlane(ray);
        return hit is not null && Contains(hit.Value) ? hit : null;
    }

    /// <summary>
    /// Intersects a ray with the infinite horizontal plane y = GroundHeight,
    /// with no bound on where that point falls — unlike <see cref="Raycast"/>,
    /// a hit off the edge of the terrain (or well beyond it) still counts.
    /// </summary>
    public Vector3? RaycastGroundPlane(Ray ray)
    {
        // Plane: y = GroundHeight. Ray: P(t) = origin + t·direction.
        // Solve origin.y + t·direction.y = GroundHeight for t.
        if (MathF.Abs(ray.Direction.Y) < 1e-6f)
            return null; // Parallel to the ground — never intersects.

        float t = (GroundHeight - ray.Position.Y) / ray.Direction.Y;
        if (t < 0f)
            return null; // Intersection is behind the camera.

        return ray.Position + ray.Direction * t;
    }

    /// <summary>
    /// Part 3, The Lush 3D Lawn: edge length (m) of each ground cell the
    /// hilly lawn is drawn in — 2x2m, per the spec.
    /// </summary>
    private const float CellSize = 2f;

    /// <summary>Forest Green — the lawn's single base grass color, height-tinted per cell (see <see cref="Draw"/>) rather than alternated in a checkerboard.</summary>
    private static readonly Color GrassBase = new(34, 139, 34, 255);

    /// <summary>Yellow-Green — sunlit tint blended in for a cell's higher (peak) ground.</summary>
    private static readonly Color GrassPeak = new(154, 205, 50, 255);

    /// <summary>Dark shadow-green — blended in for a cell's lower (valley) ground.</summary>
    private static readonly Color GrassValley = new(20, 80, 20, 255);

    /// <summary>Brown dirt patch color, scattered deterministically across the lawn as a rare, sparse embellishment.</summary>
    private static readonly Color Dirt = new(120, 85, 55, 255);

    /// <summary>
    /// The height function's total amplitude (sum of its three stacked
    /// sine/cosine terms' coefficients — see <see cref="World.GetHeightAt"/>),
    /// used to normalize a cell's average height into a -1..1 tint factor.
    /// </summary>
    private const float HeightAmplitude = 5.5f;

    /// <summary>Component-wise linear interpolation between two colors, alpha fixed at 255.</summary>
    private static Color LerpColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            (byte)255);
    }

    /// <summary>
    /// Deterministic (not System.Random) hash of a cell's integer indices,
    /// so the lawn's dirt-patch pattern is stable and reproducible frame to
    /// frame rather than flickering.
    /// </summary>
    private static uint CellHash(int cx, int cz)
    {
        unchecked
        {
            uint h = (uint)(cx * 374761393 + cz * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    /// <summary>
    /// Part 3 + Part 2: draws the 100x100m lawn as a grid of 2x2m cells from
    /// -50 to 50 on X/Z, using <see cref="World.GetHeightAt"/> for each
    /// corner's elevation so the lawn reads as rolling hills, and skipping
    /// any cell whose center is beyond <paramref name="renderRadius"/> of
    /// <paramref name="cameraTarget"/> (Part 2's mandatory distance cull —
    /// terrain is by far the most expensive thing drawn every frame).
    /// </summary>
    public void Draw(Vector3 cameraTarget, float renderRadius)
    {
        float half = Size / 2f;
        float renderRadiusSq = renderRadius * renderRadius;

        for (float x = -half; x < half; x += CellSize)
        {
            for (float z = -half; z < half; z += CellSize)
            {
                float centerX = x + CellSize / 2f;
                float centerZ = z + CellSize / 2f;
                float dx = centerX - cameraTarget.X;
                float dz = centerZ - cameraTarget.Z;
                if (dx * dx + dz * dz > renderRadiusSq)
                    continue; // Part 2: distance-culled — never drawn, never costs a frame.

                float x0 = x, x1 = x + CellSize, z0 = z, z1 = z + CellSize;
                var p00 = new Vector3(x0, World.GetHeightAt(x0, z0), z0);
                var p10 = new Vector3(x1, World.GetHeightAt(x1, z0), z0);
                var p01 = new Vector3(x0, World.GetHeightAt(x0, z1), z1);
                var p11 = new Vector3(x1, World.GetHeightAt(x1, z1), z1);

                int cx = (int)MathF.Floor(x / CellSize);
                int cz = (int)MathF.Floor(z / CellSize);
                uint hash = CellHash(cx, cz);

                Color color;
                if (hash % 40 == 0)
                {
                    // A rare, sparse dirt patch (~1 cell in 40) — an
                    // occasional embellishment, not a repeating pattern.
                    color = Dirt;
                }
                else
                {
                    // Organic height-based tinting: a single Forest Green
                    // base, lightened toward a sunlit yellow-green on peaks
                    // and darkened toward a shadowed green in valleys — no
                    // checkerboard, just the cell's own average elevation.
                    float avgHeight = (p00.Y + p10.Y + p01.Y + p11.Y) / 4f;
                    float t = Math.Clamp(avgHeight / HeightAmplitude, -1f, 1f);
                    color = t >= 0f
                        ? LerpColor(GrassBase, GrassPeak, t)
                        : LerpColor(GrassBase, GrassValley, -t);
                }

                // Two triangles, upward-facing winding (counter-clockwise
                // when viewed from above/+Y).
                Raylib.DrawTriangle3D(p00, p01, p11, color);
                Raylib.DrawTriangle3D(p00, p11, p10, color);
            }
        }
    }
}


// =============================================================================
//  Input
// =============================================================================

/// <summary>
/// The player has no lever on the world — no miracles, no factions to
/// command. The only tap left inspects a single Bramblekin (see
/// <see cref="World.TrySelectKinAt"/>), whose Personality, needs and
/// relationships then show in the Kin Inspector panel.
/// </summary>
public sealed class WorldTapInput
{
    /// <summary>
    /// One-Finger Panning claimed the left mouse button/primary touch for
    /// the Spectator Camera (see <see cref="TouchCameraController"/>), so a
    /// press that turns into a drag past this many pixels is a pan, not a
    /// tap — <see cref="HandlePress"/> only fires on release, and only if
    /// the press never crossed this threshold.
    /// </summary>
    private const float TapDragThreshold = 12f;

    private Vector2 _pressStartPosition;
    private bool _isPressing;
    private bool _exceededDragThreshold;

    /// <summary>Polls the mouse/touch and handles a clean tap-and-release, if one just finished.</summary>
    public void Update(Camera3D camera, World world)
    {
        // Raylib maps a primary touch to the left mouse button, so the same
        // code path serves desktop clicks and phone taps.
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _isPressing = true;
            _exceededDragThreshold = false;
            _pressStartPosition = Raylib.GetMousePosition();
        }
        else if (_isPressing && Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            if (!_exceededDragThreshold && Vector2.Distance(Raylib.GetMousePosition(), _pressStartPosition) > TapDragThreshold)
                _exceededDragThreshold = true;
        }
        else if (_isPressing && Raylib.IsMouseButtonReleased(MouseButton.Left))
        {
            _isPressing = false;
            if (!_exceededDragThreshold)
                HandlePress(Raylib.GetMousePosition(), camera, world);
        }
    }

    /// <summary>
    /// Handles a completed tap at <paramref name="screenPosition"/> — called
    /// from <see cref="Update"/> on release, once it's confirmed the press
    /// never turned into a pan. Public so input can be driven directly, from
    /// a test harness or an alternate input source.
    /// </summary>
    public void HandlePress(Vector2 screenPosition, Camera3D camera, World world)
    {
        if (PickGround(camera, world.Terrain, screenPosition) is { } tapGround)
            world.TrySelectKinAt(tapGround);
    }

    /// <summary>
    /// Casts a ray from the camera through the given screen position and
    /// returns where it meets the terrain (or null if it misses the terrain,
    /// or the terrain's edge).
    ///
    /// Guards against every way this can go wrong on a phone: a touch
    /// reported before the window/surface has a real size yet (e.g. mid
    /// rotation, or the first frame or two after Android hands the activity
    /// its window), a tap slightly outside the rendered viewport, or a
    /// screen position that is already NaN/Infinity. Any of those would make
    /// GetScreenToWorldRay's projection math hand back a garbage ray; none of
    /// them should ever crash the raycast or the tap that triggered it.
    /// </summary>
    internal static Vector3? PickGround(Camera3D camera, Terrain terrain, Vector2 screenPosition)
    {
        Ray? ray = SafeScreenRay(screenPosition, camera);
        return ray is null ? null : terrain.Raycast(ray.Value);
    }

    /// <summary>
    /// The bounds/NaN/Infinity guards shared by <see cref="PickGround"/>:
    /// validates the screen position and the window before asking raylib to
    /// project it, and validates the ray it gets back.
    /// </summary>
    private static Ray? SafeScreenRay(Vector2 screenPosition, Camera3D camera)
    {
        int width = Raylib.GetScreenWidth();
        int height = Raylib.GetScreenHeight();
        if (width <= 0 || height <= 0)
            return null;

        if (!IsFinite(screenPosition) ||
            screenPosition.X < 0 || screenPosition.X > width ||
            screenPosition.Y < 0 || screenPosition.Y > height)
            return null;

        // GetScreenToWorldRay is raylib 5.5's name for GetMouseRay (the old
        // name still exists but is marked obsolete in Raylib-cs 8).
        Ray ray = Raylib.GetScreenToWorldRay(screenPosition, camera);
        return IsFinite(ray.Position) && IsFinite(ray.Direction) ? ray : null;
    }

    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

// =============================================================================
//  World: everything that lives on the terrain
// =============================================================================

/// <summary>
/// A solid circle on the ground that walkers steer around — a large
/// Pebble's footprint. Coordinates are (x, z).
/// </summary>
public readonly record struct Obstacle(Vector2 Center, float Radius);

/// <summary>
/// The Spatial Grid: divides the map into fixed <see cref="ChunkSize"/>
/// (10m) chunks keyed by (chunk-x, chunk-z), so a nearest-target search
/// can look only at the handful of entities near the searcher instead of
/// scanning every entity on the whole map. Rebuilt from scratch once a
/// frame (see <see cref="World.RebuildSpatialGrids"/>) rather than having
/// each entity push incremental chunk-membership updates as it moves —
/// exactly equivalent, since every entity moves at most once per frame
/// anyway.
/// </summary>
public sealed class SpatialGrid<T>
{
    public const float ChunkSize = 10f;

    private readonly Dictionary<(int X, int Z), List<T>> _cells = new();

    private static (int X, int Z) ChunkOf(Vector3 position) =>
        ((int)MathF.Floor(position.X / ChunkSize), (int)MathF.Floor(position.Z / ChunkSize));

    /// <summary>Empties every chunk, ready for this frame's <see cref="Register"/> calls.</summary>
    public void Clear()
    {
        foreach (var list in _cells.Values)
            list.Clear();
    }

    /// <summary>Registers <paramref name="item"/> under the chunk containing <paramref name="position"/>.</summary>
    public void Register(T item, Vector3 position)
    {
        var key = ChunkOf(position);
        if (!_cells.TryGetValue(key, out List<T>? list))
        {
            list = new List<T>();
            _cells[key] = list;
        }
        list.Add(item);
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with every item
    /// registered in the chunk containing <paramref name="position"/> and
    /// its 8 neighbors — a 30x30m window around the searcher, not the
    /// whole map. Guaranteed to cover at least <see cref="ChunkSize"/> in
    /// every direction.
    /// </summary>
    public void QueryNearby(Vector3 position, List<T> results) => QueryRadius(position, ChunkSize, results);

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with every item in
    /// any chunk overlapping the square of half-size <paramref name="radius"/>
    /// around <paramref name="position"/> — a superset of everything within
    /// <paramref name="radius"/>, which the caller still distance-checks.
    /// Used for Intelligence-scaled perception, which can reach past a
    /// single neighboring chunk.
    /// </summary>
    public void QueryRadius(Vector3 position, float radius, List<T> results)
    {
        results.Clear();
        int minX = (int)MathF.Floor((position.X - radius) / ChunkSize);
        int maxX = (int)MathF.Floor((position.X + radius) / ChunkSize);
        int minZ = (int)MathF.Floor((position.Z - radius) / ChunkSize);
        int maxZ = (int)MathF.Floor((position.Z + radius) / ChunkSize);
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (_cells.TryGetValue((x, z), out List<T>? list))
                    results.AddRange(list);
            }
        }
    }
}

/// <summary>
/// Owns the simulation state — terrain, loose Food, wildlife and every
/// Bramblekin — and steps it in a fixed order. There are no factions and no
/// shared economy: the World only spawns things, resolves what happens when
/// two Bramblekin cross paths (see <see cref="ResolveEncounter"/>), and
/// keeps each emergent group's membership and leader up to date. Every
/// decision about what to actually do lives in each Bramblekin's own
/// <see cref="Bramblekin.Update"/>.
///
///   Food-claim timeouts -> spatial grids -> group bookkeeping -> Hornets ->
///   Grubs -> Bramblekin -> Wolf Spider -> encounters -> spawners (berries,
///   wildlife, arriving wanderers) -> food despawn -> timed effects.
/// </summary>
public sealed class World
{
    /// <summary>
    /// The Terrain Height Function: procedural rolling-hills elevation at
    /// any (x, z) ground coordinate, via a stacked sine/cosine formula.
    /// Deterministic and stateless — the same (x, z) always yields the same
    /// height, so it can be called freely from rendering, spawning and
    /// grounding code alike without ever needing to be cached.
    /// </summary>
    public static float GetHeightAt(float x, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z))
            return 0f;

        return MathF.Sin(x * 0.1f) * 2.0f + MathF.Cos(z * 0.1f) * 2.0f + MathF.Sin((x + z) * 0.05f) * 1.5f;
    }

    /// <summary>
    /// Surface-Normal Tilting: the terrain's outward surface normal at
    /// (x, z), found via finite differences — sampling
    /// <see cref="GetHeightAt"/> a small step to either side on both axes
    /// and using the resulting slope to build a normalized normal vector.
    /// Used to tilt bodies and props (see <see cref="Bramblekin.Draw"/> and
    /// <see cref="GardenProp.Draw"/>) so they sit flush on a hillside
    /// instead of just being lifted straight up out of it.
    /// </summary>
    public static Vector3 GetNormalAt(float x, float z)
    {
        const float offset = 0.1f;
        float L = GetHeightAt(x - offset, z);
        float R = GetHeightAt(x + offset, z);
        float B = GetHeightAt(x, z - offset);
        float F = GetHeightAt(x, z + offset);
        return Vector3.Normalize(new Vector3(L - R, 2.0f * offset, B - F));
    }

    /// <summary>Grounding Entities: snaps <paramref name="position"/>'s Y onto the terrain's height at its (x, z).</summary>
    public static Vector3 Grounded(Vector3 position) => new(position.X, GetHeightAt(position.X, position.Z), position.Z);

    /// <summary>
    /// Same terrain snap as <see cref="Grounded(Vector3)"/>, but with an
    /// extra vertical <paramref name="yOffset"/> added on top — for
    /// overlays (rings, lines) that must float just above a slope instead
    /// of clipping into it.
    /// </summary>
    public static Vector3 Grounded(Vector3 position, float yOffset) =>
        new(position.X, GetHeightAt(position.X, position.Z) + yOffset, position.Z);

    // --- Food ------------------------------------------------------------------

    /// <summary>Object Pooling: fixed number of Food slots, constructed once and reused — see <see cref="ActivateFood"/>.</summary>
    private const int FoodPoolCapacity = 400;

    /// <summary>Berries already on the ground when the world is created, so the first arrivals have something to find.</summary>
    private const int InitialBerries = 45;

    /// <summary>Passive Foraging: seconds between wild Berry spawns.</summary>
    public const float BerrySpawnInterval = 0.6f;

    /// <summary>Wild Berries stop spawning once this many are on the ground.</summary>
    public const int MaxBerries = 100;

    /// <summary>
    /// Berry Patches: how many fixed spots (preferably Dandelions) most
    /// Berries grow around. Clustering the food gives Bramblekin a reason to
    /// converge on the same places — which is where encounters, alliances
    /// and robberies happen.
    /// </summary>
    private const int BerryPatchCount = 8;

    /// <summary>How far (m) from its patch's anchor a patch Berry may grow.</summary>
    private const float BerryPatchRadius = 4f;

    /// <summary>Odds a new Berry grows in a patch rather than at a random spot anywhere on the map.</summary>
    private const double BerryPatchChance = 0.65;

    /// <summary>
    /// Dibs failsafe: a Food claim is force-released after this many
    /// seconds, so a claimant that's stuck never locks everyone else out.
    /// </summary>
    public const float FoodClaimTimeoutSeconds = 15f;

    // --- Wildlife ----------------------------------------------------------------

    /// <summary>Seconds after the Wolf Spider is slain before a new one moves in.</summary>
    public const float SpiderRespawnDelay = 60f;

    /// <summary>A new Wolf Spider never spawns within this many meters of a living Bramblekin.</summary>
    private const float MinSpiderSpawnDistanceFromKin = 15f;

    /// <summary>Food scattered where a slain Wolf Spider falls — the reward for a group that brings one down.</summary>
    private const int SpiderCarcassFood = 6;

    private const float SplatDuration = 6f;

    public const int MaxHornetsOnMap = 12;

    public const int HornetSwarmMinSize = 3, HornetSwarmMaxSize = 5;

    /// <summary>Seconds between checks that top the Hornet population back up toward <see cref="MaxHornetsOnMap"/>, one whole swarm at a time.</summary>
    public const float HornetSpawnInterval = 10f;

    public const int MaxGrubsOnMap = 4;

    /// <summary>Seconds between checks that top the Grub population back up toward <see cref="MaxGrubsOnMap"/>.</summary>
    public const float GrubSpawnInterval = 20f;

    // --- Bramblekin population ------------------------------------------------------

    /// <summary>Wandering Arrivals stop once this many Bramblekin are alive.</summary>
    public const int MaxPopulation = 40;

    /// <summary>
    /// Wandering Arrivals: seconds between new solitary Bramblekin drifting
    /// in from the map's edge (while below <see cref="MaxPopulation"/>) —
    /// the world's only source of new life, so a harsh stretch thins the
    /// population out without ever ending the simulation for good.
    /// </summary>
    public const float ArrivalInterval = 15f;

    /// <summary>How close (m) a tap has to land to a Bramblekin to select it for the Kin Inspector.</summary>
    public const float KinSelectionRadius = 2f;

    // --- Encounters & groups ------------------------------------------------------

    /// <summary>Two Bramblekin closer than this (m) have "crossed paths" — see <see cref="ResolveEncounter"/>.</summary>
    public const float EncounterRadius = 1.2f;

    /// <summary>The same pair of Bramblekin can't resolve another encounter until this many seconds have passed.</summary>
    public const float EncounterCooldown = 12f;

    /// <summary>A group never grows past this many members, by joining or by merging.</summary>
    public const int MaxGroupSize = 6;

    /// <summary>Two Bramblekin both at least this Sociable band together on meeting — see <see cref="ResolveEncounter"/>.</summary>
    public const float AllianceSociabilityThreshold = 0.6f;

    /// <summary>Only a Bramblekin at least this Aggressive will turn on another for its food — see <see cref="TryStartRobbery"/>.</summary>
    public const float HighAggressionThreshold = 0.55f;

    public const float FloatingTextDuration = 1.5f;

    /// <summary>Oversized Garden Props: how many static decorations to scatter across the map.</summary>
    private const int GardenPropCount = 40;

    private static readonly Color FriendlyTextColor = new(60, 170, 80, 255);
    private static readonly Color HostileTextColor = new(210, 50, 40, 255);

    private readonly List<Obstacle> _obstacles = new();
    private readonly List<(Vector3 Position, float TimeLeft)> _splats = new();
    private readonly List<(Vector3 Position, string Text, Color Color, float TimeLeft)> _floatingTexts = new();
    private readonly List<Vector3> _berryPatches = new();

    // Deferred spawns/removals, applied once per frame in CommitPendingChanges.
    private readonly List<Bramblekin> _pendingKinSpawns = new();
    private readonly List<Bramblekin> _pendingKinRemovals = new();
    private readonly List<(Vector3 Position, FoodShardKind Kind)> _pendingFoodSpawns = new();
    private readonly List<Hornet> _pendingHornetSpawns = new();
    private readonly List<Hornet> _pendingHornetRemovals = new();
    private readonly List<Grub> _pendingGrubSpawns = new();
    private readonly List<Grub> _pendingGrubRemovals = new();

    // The Spatial Grid, plus one scratch buffer per kind of query so an
    // outer query's results are never clobbered by an unrelated inner one.
    private readonly SpatialGrid<FoodShard> _foodGrid = new();
    private readonly SpatialGrid<Bramblekin> _colonyGrid = new();
    private readonly List<FoodShard> _foodQueryBuffer = new();
    private readonly List<Bramblekin> _colonyQueryBuffer = new();
    private readonly List<Bramblekin> _kinPerceptionBuffer = new();
    private readonly List<Bramblekin> _encounterBuffer = new();

    private readonly Dictionary<Guid, KinGroup> _groups = new();
    private readonly List<Guid> _groupRemovalBuffer = new();

    /// <summary>When each pair of Bramblekin (lower ID first) last resolved an encounter — see <see cref="EncounterCooldown"/>.</summary>
    private readonly Dictionary<(int, int), float> _lastEncounter = new();
    private readonly List<(int, int)> _encounterExpiryBuffer = new();

    private float _berrySpawnTimer = BerrySpawnInterval;
    private float _hornetSpawnTimer = HornetSpawnInterval;
    private float _grubSpawnTimer = GrubSpawnInterval;
    private float _arrivalTimer = ArrivalInterval;
    private float _encounterCleanupTimer = 30f;

    public Terrain Terrain { get; }
    public Random Rng { get; }

    /// <summary>Game time simulated so far, in seconds (the sum of every Update's deltaTime).</summary>
    public float ElapsedSeconds { get; private set; }

    /// <summary>Every Bramblekin on the map. A dead one lingers (IsDead) until the end of the frame — see <see cref="CommitPendingChanges"/>.</summary>
    public List<Bramblekin> Colony { get; } = new();

    /// <summary>Object Pooling: the fixed pool of Food slots; only the <see cref="FoodShard.IsActive"/> ones are real.</summary>
    public List<FoodShard> FoodShards { get; } = new();

    public WolfSpider? Spider { get; private set; }
    public List<Hornet> Hornets { get; } = new();
    public List<Grub> Grubs { get; } = new();
    public List<GardenProp> GardenProps { get; } = new();

    /// <summary>Every group with at least two living members, keyed by <see cref="Bramblekin.GroupId"/>.</summary>
    public IReadOnlyCollection<KinGroup> Groups => _groups.Values;

    /// <summary>The Bramblekin shown in the Kin Inspector panel, if any — see <see cref="TrySelectKinAt"/>.</summary>
    public Bramblekin? SelectedKin { get; private set; }

    public IReadOnlyList<Obstacle> Obstacles => _obstacles;
    public IReadOnlyList<(Vector3 Position, string Text, Color Color, float TimeLeft)> FloatingTexts => _floatingTexts;

    /// <summary>Loose (active, uncarried) Food on the map, as of the start of this frame.</summary>
    public int LooseFoodCount { get; private set; }

    public float SpiderRespawnTimer { get; private set; }

    // --- Running tallies (HUD / headless reports) -------------------------------------
    public int Arrivals { get; private set; }
    public int DeathsByStarvation { get; private set; }
    public int DeathsByPredator { get; private set; }
    public int DeathsByKin { get; private set; }
    public int Casualties => DeathsByStarvation + DeathsByPredator + DeathsByKin;
    public int FoodEaten { get; private set; }
    public int FoodShared { get; private set; }
    public int Thefts { get; private set; }
    public int AlliancesFormed { get; private set; }
    public int GrubsKilled { get; private set; }
    public int HornetsKilled { get; private set; }
    public int SpidersKilled { get; private set; }

    public World(Terrain terrain, Random rng, int initialKinCount)
    {
        Terrain = terrain;
        Rng = rng;

        SpawnGardenProps();
        RebuildObstacles();
        PickBerryPatches();

        // Object Pooling: every Food slot is constructed once here
        // (inactive) rather than instantiated and destroyed per
        // spawn/pickup/despawn — see ActivateFood.
        for (int i = 0; i < FoodPoolCapacity; i++)
            FoodShards.Add(new FoodShard());
        for (int i = 0; i < InitialBerries; i++)
            ActivateFood(RandomBerrySpot(), FoodShardKind.Berry);

        // Every starting Bramblekin is solitary, with its own freshly
        // rolled Personality (see the Bramblekin constructor) — groups only
        // ever form later, out of encounters.
        for (int i = 0; i < initialKinCount; i++)
            Colony.Add(new Bramblekin(RandomFreePoint(Bramblekin.BodyRadius, Bramblekin.EdgeMargin), rng));

        SpawnSpider();
        RebuildSpatialGrids(); // So LooseFoodCount is right before the first Update.
    }

    // --- Setup -----------------------------------------------------------------

    /// <summary>
    /// Oversized Garden Props: scatters <see cref="GardenPropCount"/>
    /// Pebbles/Twigs/Dandelions randomly across the 100x100 map. Every
    /// prop's Y is snapped onto the terrain the instant it's placed.
    /// </summary>
    private void SpawnGardenProps()
    {
        for (int i = 0; i < GardenPropCount; i++)
        {
            Vector3 candidate = Terrain.RandomPoint(Rng, margin: 1f);
            var kind = (GardenPropKind)Rng.Next(3);
            float rotation = (float)(Rng.NextDouble() * MathF.Tau);
            GardenProps.Add(new GardenProp(Grounded(candidate), kind, rotation, Rng));
        }
    }

    /// <summary>Large Pebbles are the only solid things on the map; built once, since props never move.</summary>
    private void RebuildObstacles()
    {
        _obstacles.Clear();
        foreach (GardenProp prop in GardenProps)
        {
            if (prop.FootprintRadius > 0f)
                _obstacles.Add(new Obstacle(new Vector2(prop.Position.X, prop.Position.Z), prop.FootprintRadius));
        }
    }

    /// <summary>Berry Patches: anchors on Dandelions first (a flowerbed reads naturally as a berry patch), then random open ground.</summary>
    private void PickBerryPatches()
    {
        foreach (GardenProp prop in GardenProps)
        {
            if (_berryPatches.Count >= BerryPatchCount)
                break;
            if (prop.Kind == GardenPropKind.Dandelion)
                _berryPatches.Add(prop.Position);
        }

        while (_berryPatches.Count < BerryPatchCount)
            _berryPatches.Add(RandomFreePoint(BerryPatchRadius * 0.5f, edgeMargin: BerryPatchRadius + 1f));
    }

    /// <summary>Spawns a Wolf Spider somewhere open, at least <see cref="MinSpiderSpawnDistanceFromKin"/> meters from every living Bramblekin.</summary>
    private void SpawnSpider()
    {
        Vector3 position = Vector3.Zero;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            position = Terrain.RandomPoint(Rng, margin: 1.5f);
            if (!IsBlocked(position, WolfSpider.BodyRadius) &&
                Colony.All(b => b.IsDead || GroundMover.HorizontalDistance(position, b.Position) >= MinSpiderSpawnDistanceFromKin))
                break;
        }

        Spider = new WolfSpider(position, Rng);
    }

    // --- Object Pooling ----------------------------------------------------------

    /// <summary>Activates the first inactive slot in <see cref="FoodShards"/> at <paramref name="position"/>, or silently does nothing if the pool is exhausted.</summary>
    private void ActivateFood(Vector3 position, FoodShardKind kind)
    {
        foreach (FoodShard food in FoodShards)
        {
            if (!food.IsActive)
            {
                food.Activate(position, kind);
                return;
            }
        }
    }

    // --- The frame -------------------------------------------------------------------

    public void Update(float deltaTime)
    {
        ElapsedSeconds += deltaTime;
        UpdateFoodClaimTimeouts(deltaTime);
        RebuildSpatialGrids();
        RebuildGroups();

        // Wildlife moves before the colony reacts to it this frame. Reverse
        // for-loops: a Bramblekin's strike (below) can kill a Hornet or Grub,
        // which marks it dead but defers the actual list removal.
        for (int i = Hornets.Count - 1; i >= 0; i--)
            Hornets[i].Update(deltaTime, this);

        for (int i = Grubs.Count - 1; i >= 0; i--)
            Grubs[i].Update(deltaTime, this);

        // Reverse for-loop: a Bramblekin's own Update() can kill another
        // (combat, robbery) — World.Kill only queues the removal, but
        // walking backwards keeps this loop correct even if that changes.
        for (int i = Colony.Count - 1; i >= 0; i--)
            Colony[i].Update(deltaTime, this);

        if (Spider is { IsDead: false } spider)
            spider.Update(deltaTime, this);

        ResolveEncounters();

        UpdateBerrySpawn(deltaTime);
        UpdateSpiderRespawn(deltaTime);
        UpdateHornetSpawn(deltaTime);
        UpdateGrubSpawn(deltaTime);
        UpdateArrivals(deltaTime);
        UpdateFoodDespawn(deltaTime);
        UpdateEncounterCleanup(deltaTime);

        for (int i = _splats.Count - 1; i >= 0; i--)
        {
            var splat = _splats[i];
            splat.TimeLeft -= deltaTime;
            if (splat.TimeLeft <= 0f)
                _splats.RemoveAt(i);
            else
                _splats[i] = splat;
        }

        for (int i = _floatingTexts.Count - 1; i >= 0; i--)
        {
            var text = _floatingTexts[i];
            text.TimeLeft -= deltaTime;
            if (text.TimeLeft <= 0f)
                _floatingTexts.RemoveAt(i);
            else
                _floatingTexts[i] = text;
        }
    }

    /// <summary>
    /// Applies every entity spawned or removed this frame. Called once, at
    /// the very end of the frame after Update() and Draw() have both run, so
    /// nothing is ever adding to or removing from an entity list while
    /// something else might still be iterating it.
    /// </summary>
    public void CommitPendingChanges()
    {
        if (_pendingKinRemovals.Count > 0)
        {
            foreach (Bramblekin dead in _pendingKinRemovals)
            {
                Colony.Remove(dead);
                if (SelectedKin == dead)
                    SelectedKin = null;
            }

            // IDs are never reused, so a dead Bramblekin's entry in everyone
            // else's KnownKins is just clutter from here on.
            foreach (Bramblekin kin in Colony)
            {
                foreach (Bramblekin dead in _pendingKinRemovals)
                    kin.ForgetKin(dead.ID);
            }
            _pendingKinRemovals.Clear();
        }

        if (_pendingKinSpawns.Count > 0)
        {
            Colony.AddRange(_pendingKinSpawns);
            _pendingKinSpawns.Clear();
        }

        if (_pendingFoodSpawns.Count > 0)
        {
            foreach (var (position, kind) in _pendingFoodSpawns)
                ActivateFood(position, kind);
            _pendingFoodSpawns.Clear();
        }

        if (_pendingHornetRemovals.Count > 0)
        {
            foreach (Hornet hornet in _pendingHornetRemovals)
                Hornets.Remove(hornet);
            _pendingHornetRemovals.Clear();
        }

        if (_pendingHornetSpawns.Count > 0)
        {
            Hornets.AddRange(_pendingHornetSpawns);
            _pendingHornetSpawns.Clear();
        }

        if (_pendingGrubRemovals.Count > 0)
        {
            foreach (Grub grub in _pendingGrubRemovals)
                Grubs.Remove(grub);
            _pendingGrubRemovals.Clear();
        }

        if (_pendingGrubSpawns.Count > 0)
        {
            Grubs.AddRange(_pendingGrubSpawns);
            _pendingGrubSpawns.Clear();
        }
    }

    /// <summary>The Spatial Grid: every loose Food and every living Bramblekin, re-registered into its current 10m chunk. Rebuilt fresh once a frame rather than tracked incrementally as each entity moves.</summary>
    private void RebuildSpatialGrids()
    {
        _foodGrid.Clear();
        int looseFood = 0;
        foreach (FoodShard food in FoodShards)
        {
            if (food.IsActive && !food.IsCarried)
            {
                _foodGrid.Register(food, food.Position);
                looseFood++;
            }
        }
        LooseFoodCount = looseFood;

        _colonyGrid.Clear();
        foreach (Bramblekin bramblekin in Colony)
        {
            if (!bramblekin.IsDead)
                _colonyGrid.Register(bramblekin, bramblekin.Position);
        }
    }

    // --- Groups ------------------------------------------------------------------

    /// <summary>The group <paramref name="kin"/> currently belongs to, if any.</summary>
    public KinGroup? GroupOf(Bramblekin kin) =>
        kin.GroupId is { } id && _groups.TryGetValue(id, out KinGroup? group) ? group : null;

    /// <summary>
    /// Group Dynamics bookkeeping: rebuilds every group's member list from
    /// the living Bramblekin's own <see cref="Bramblekin.GroupId"/>s,
    /// dissolves any group left with a single survivor (it's solitary
    /// again), and re-elects each remaining group's Leader — always the
    /// member with the highest Intelligence, so losing a leader simply hands
    /// the role to the next-sharpest member.
    /// </summary>
    private void RebuildGroups()
    {
        foreach (KinGroup group in _groups.Values)
            group.Members.Clear();

        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsDead || kin.GroupId is not { } id)
                continue;

            if (!_groups.TryGetValue(id, out KinGroup? group))
            {
                group = new KinGroup(id);
                _groups[id] = group;
            }
            group.Members.Add(kin);
        }

        _groupRemovalBuffer.Clear();
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Members.Count >= 2)
            {
                Bramblekin? previousLeader = group.Leader;
                group.ElectLeader();
                if (previousLeader is not null && previousLeader != group.Leader)
                    Game.AddEventLog($"[GROUP] #{group.Leader!.ID} now leads group {group.ShortId}");
                continue;
            }

            if (group.Members.Count == 1)
            {
                group.Members[0].LeaveGroup();
                Game.AddEventLog($"[GROUP] Group {group.ShortId} is gone; #{group.Members[0].ID} is alone again");
            }
            _groupRemovalBuffer.Add(group.Id);
        }

        foreach (Guid id in _groupRemovalBuffer)
            _groups.Remove(id);
    }

    // --- Encounters ----------------------------------------------------------------

    /// <summary>
    /// The Encounter: finds every pair of living Bramblekin within
    /// <see cref="EncounterRadius"/> of each other that hasn't met within
    /// the last <see cref="EncounterCooldown"/> seconds, and resolves it.
    /// </summary>
    private void ResolveEncounters()
    {
        bool groupsChanged = false;
        for (int i = 0; i < Colony.Count; i++)
        {
            Bramblekin a = Colony[i];
            if (a.IsDead)
                continue;

            _colonyGrid.QueryNearby(a.Position, _encounterBuffer);
            for (int j = 0; j < _encounterBuffer.Count; j++)
            {
                Bramblekin b = _encounterBuffer[j];
                if (b.ID <= a.ID || b.IsDead)
                    continue; // Each pair once, lower ID first.
                if (GroundMover.HorizontalDistanceSquared(a.Position, b.Position) > EncounterRadius * EncounterRadius)
                    continue;

                var key = (a.ID, b.ID);
                if (_lastEncounter.TryGetValue(key, out float last) && ElapsedSeconds - last < EncounterCooldown)
                    continue;
                _lastEncounter[key] = ElapsedSeconds;

                groupsChanged |= ResolveEncounter(a, b);
            }
        }

        if (groupsChanged)
            RebuildGroups();
    }

    /// <summary>
    /// The social resolution when two Bramblekin cross paths, in priority
    /// order:
    ///   1. Groupmates never fight — a fed one shares its food with a hungry one.
    ///   2. Hostility: a starving, highly Aggressive one may turn on the
    ///      other to steal its food (see <see cref="TryStartRobbery"/>);
    ///      both remember each other as Enemies from then on.
    ///   3. Enemies simply pass each other by.
    ///   4. Friends may share food.
    ///   5. Alliance: if both are threatened by a predator right now, or
    ///      both are highly Sociable, they band together under one GroupId.
    ///   6. Otherwise they just become acquainted: Friends with odds equal
    ///      to the product of their Sociability, Neutral otherwise.
    /// Returns true if group membership changed.
    /// </summary>
    private bool ResolveEncounter(Bramblekin a, Bramblekin b)
    {
        if (a.GroupId is { } groupId && groupId == b.GroupId)
        {
            TryShareFood(a, b, sameGroup: true);
            return false;
        }

        if (TryStartRobbery(a, b) || TryStartRobbery(b, a))
            return false;

        RelationshipState? relationship = a.RelationshipTo(b);
        if (relationship == RelationshipState.Enemy)
            return false;

        if (relationship == RelationshipState.Friend)
            TryShareFood(a, b, sameGroup: false);

        bool bothThreatened = a.IsThreatenedByPredator && b.IsThreatenedByPredator;
        bool bothSociable = a.Personality.Sociability >= AllianceSociabilityThreshold &&
                            b.Personality.Sociability >= AllianceSociabilityThreshold;
        if ((bothThreatened || bothSociable) && TryFormAlliance(a, b, bothThreatened))
            return true;

        if (relationship is null)
        {
            bool friendly = Rng.NextDouble() < a.Personality.Sociability * b.Personality.Sociability;
            SetMutualRelationship(a, b, friendly ? RelationshipState.Friend : RelationshipState.Neutral);
        }
        return false;
    }

    /// <summary>
    /// Hostility: <paramref name="attacker"/> — starving, empty-handed, with
    /// no loose Food in sight, and at least <see cref="HighAggressionThreshold"/>
    /// Aggressive — rolls its Aggression (halved against a Friend) to turn on
    /// <paramref name="victim"/>, who must actually be carrying food worth
    /// stealing. On success the two
    /// are Enemies for good and the attacker starts its robbery (see
    /// <see cref="Bramblekin.BeginRobbery"/>).
    /// </summary>
    private bool TryStartRobbery(Bramblekin attacker, Bramblekin victim)
    {
        if (!attacker.IsStarving || attacker.HasFood || attacker.IsRobbing || attacker.SeesFood || !victim.HasFood)
            return false;
        if (attacker.Personality.Aggression < HighAggressionThreshold)
            return false;

        double chance = attacker.Personality.Aggression;
        if (attacker.RelationshipTo(victim) == RelationshipState.Friend)
            chance *= 0.5;
        if (Rng.NextDouble() >= chance)
            return false;

        DeclareEnemies(attacker, victim);
        attacker.BeginRobbery(victim);
        QueueFloatingText(attacker.Position, "Attack!", HostileTextColor);
        Game.AddEventLog($"[HOSTILITY] Starving #{attacker.ID} turned on #{victim.ID} for its food");
        return true;
    }

    /// <summary>
    /// A fed Bramblekin carrying food hands it to a hungry, empty-handed one:
    /// always within a group, and with odds equal to the giver's
    /// Sociability between Friends (who stay Friends).
    /// </summary>
    private void TryShareFood(Bramblekin a, Bramblekin b, bool sameGroup)
    {
        Bramblekin? giver = null, taker = null;
        if (a.HasFood && !a.IsHungry && b.IsHungry && !b.HasFood)
            (giver, taker) = (a, b);
        else if (b.HasFood && !b.IsHungry && a.IsHungry && !a.HasFood)
            (giver, taker) = (b, a);

        if (giver is null || taker is null)
            return;
        if (!sameGroup && Rng.NextDouble() >= giver.Personality.Sociability)
            return;
        if (giver.SurrenderFood() is not { } food)
            return;

        taker.ReceiveFood(food);
        FoodShared++;
        if (!sameGroup)
            SetMutualRelationship(a, b, RelationshipState.Friend);
        QueueFloatingText(taker.Position, "Shared", FriendlyTextColor);
    }

    /// <summary>
    /// Alliance: puts <paramref name="a"/> and <paramref name="b"/> in the
    /// same group — a brand-new one if neither has a group, the existing one
    /// if only one does, or the larger of the two if both do (the smaller is
    /// merged into it). Refused if the result would exceed
    /// <see cref="MaxGroupSize"/>, or would put anyone in a group with a
    /// known Enemy.
    /// </summary>
    private bool TryFormAlliance(Bramblekin a, Bramblekin b, bool bothThreatened)
    {
        KinGroup? groupA = GroupOf(a);
        KinGroup? groupB = GroupOf(b);
        KinGroup group;

        if (groupA is null && groupB is null)
        {
            group = new KinGroup(Guid.NewGuid());
            _groups[group.Id] = group;
            a.JoinGroup(group.Id);
            b.JoinGroup(group.Id);
            group.Members.Add(a);
            group.Members.Add(b);
        }
        else if (groupA is not null && groupB is not null)
        {
            if (groupA.Members.Count + groupB.Members.Count > MaxGroupSize)
                return false;

            var (larger, smaller) = groupA.Members.Count >= groupB.Members.Count ? (groupA, groupB) : (groupB, groupA);
            foreach (Bramblekin member in smaller.Members)
            {
                if (HasEnemyIn(member, larger))
                    return false;
            }

            foreach (Bramblekin member in smaller.Members)
            {
                member.JoinGroup(larger.Id);
                larger.Members.Add(member);
            }
            smaller.Members.Clear();
            _groups.Remove(smaller.Id);
            group = larger;
        }
        else
        {
            KinGroup existing = groupA ?? groupB!;
            Bramblekin joiner = groupA is null ? a : b;
            if (existing.Members.Count >= MaxGroupSize || HasEnemyIn(joiner, existing))
                return false;

            joiner.JoinGroup(existing.Id);
            existing.Members.Add(joiner);
            group = existing;
        }

        group.ElectLeader();
        SetMutualRelationship(a, b, RelationshipState.Friend);
        AlliancesFormed++;
        QueueFloatingText(a.Position, "+Ally", group.Color);
        Game.AddEventLog(
            $"[ALLIANCE] #{a.ID} and #{b.ID} banded together ({(bothThreatened ? "both hunted" : "kindred spirits")}) - " +
            $"group {group.ShortId} is {group.Members.Count} strong, led by #{group.Leader!.ID}");
        return true;
    }

    private static bool HasEnemyIn(Bramblekin kin, KinGroup group)
    {
        foreach (Bramblekin member in group.Members)
        {
            if (kin.RelationshipTo(member) == RelationshipState.Enemy)
                return true;
        }
        return false;
    }

    private static void SetMutualRelationship(Bramblekin a, Bramblekin b, RelationshipState state)
    {
        a.SetRelationship(b, state);
        b.SetRelationship(a, state);
    }

    /// <summary>Any blow struck between two Bramblekin, or a robbery attempt, makes them Enemies for good.</summary>
    public void DeclareEnemies(Bramblekin a, Bramblekin b) => SetMutualRelationship(a, b, RelationshipState.Enemy);

    /// <summary>Forgets encounter cooldowns that have long since expired, so the table doesn't grow forever.</summary>
    private void UpdateEncounterCleanup(float deltaTime)
    {
        _encounterCleanupTimer -= deltaTime;
        if (_encounterCleanupTimer > 0f)
            return;
        _encounterCleanupTimer = 30f;

        _encounterExpiryBuffer.Clear();
        foreach (var (pair, time) in _lastEncounter)
        {
            if (ElapsedSeconds - time >= EncounterCooldown)
                _encounterExpiryBuffer.Add(pair);
        }
        foreach (var pair in _encounterExpiryBuffer)
            _lastEncounter.Remove(pair);
    }

    // --- Queries used by the AI ------------------------------------------------------

    /// <summary>True if a round body of <paramref name="clearance"/> radius at <paramref name="point"/> would overlap an obstacle.</summary>
    public bool IsBlocked(Vector3 point, float clearance)
    {
        var p = new Vector2(point.X, point.Z);
        foreach (var obstacle in _obstacles)
        {
            float reach = obstacle.Radius + clearance;
            if (Vector2.DistanceSquared(p, obstacle.Center) < reach * reach)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Dibs: Food can be taken if it's loose, and it isn't claimed by a
    /// different living Bramblekin actively walking to it. A null
    /// <paramref name="claimant"/> (a Grub) ignores claims altogether —
    /// Grubs don't respect anyone's dibs.
    /// </summary>
    public bool IsAvailable(FoodShard food, Bramblekin? claimant) =>
        food.IsActive && !food.IsCarried &&
        (claimant is null || food.ClaimedBy is null || food.ClaimedBy == claimant || food.ClaimedBy.IsDead);

    /// <summary>The nearest available Food within <paramref name="radius"/> of <paramref name="from"/>, if any.</summary>
    public FoodShard? NearestAvailableFood(Vector3 from, float radius, Bramblekin? claimant)
    {
        _foodGrid.QueryRadius(from, radius, _foodQueryBuffer);
        FoodShard? best = null;
        float bestDistanceSquared = radius * radius;
        for (int i = 0; i < _foodQueryBuffer.Count; i++)
        {
            FoodShard food = _foodQueryBuffer[i];
            if (!IsAvailable(food, claimant))
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, food.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = food;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>The nearest living Grub within <paramref name="radius"/> of <paramref name="from"/>, if any.</summary>
    public Grub? NearestLiveGrub(Vector3 from, float radius)
    {
        Grub? best = null;
        float bestDistanceSquared = radius * radius;
        foreach (Grub grub in Grubs)
        {
            if (grub.IsDead)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, grub.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = grub;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>The nearest living Bramblekin within <paramref name="radius"/> (at most <see cref="SpatialGrid{T}.ChunkSize"/>) of <paramref name="from"/>, if any.</summary>
    public Bramblekin? NearestLivingKinWithin(Vector3 from, float radius)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = radius * radius;
        List<Bramblekin> nearby = QueryNearbyColony(from);
        for (int i = 0; i < nearby.Count; i++)
        {
            Bramblekin kin = nearby[i];
            if (kin.IsDead)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, kin.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = kin;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>
    /// The Spatial Grid: every living Bramblekin registered within 10m
    /// chunks of <paramref name="position"/> (its own chunk plus the 8
    /// neighbors) — used by the Wolf Spider's prey search, a Hornet's aggro
    /// check and a Grub's skittishness. The returned list is a reused
    /// scratch buffer: safe to iterate immediately, but don't hold onto it
    /// past the call that reads it.
    /// </summary>
    public List<Bramblekin> QueryNearbyColony(Vector3 position)
    {
        _colonyGrid.QueryNearby(position, _colonyQueryBuffer);
        return _colonyQueryBuffer;
    }

    /// <summary>
    /// Every Bramblekin in the chunks overlapping <paramref name="radius"/>
    /// of <paramref name="position"/> (a superset — distance-check the
    /// results). Used for a Bramblekin's own Intelligence-scaled perception;
    /// same reused-scratch-buffer caveat as <see cref="QueryNearbyColony"/>,
    /// but a separate buffer, so the two never clobber each other.
    /// </summary>
    public List<Bramblekin> QueryColonyWithin(Vector3 position, float radius)
    {
        _colonyGrid.QueryRadius(position, radius, _kinPerceptionBuffer);
        return _kinPerceptionBuffer;
    }

    /// <summary>A uniformly random unblocked ground point, keeping <paramref name="edgeMargin"/> meters from the edges.</summary>
    public Vector3 RandomFreePoint(float clearance, float edgeMargin)
    {
        Vector3 candidate = Vector3.Zero;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            candidate = Terrain.RandomPoint(Rng, edgeMargin);
            if (!IsBlocked(candidate, clearance))
                return candidate;
        }
        return candidate; // Practically unreachable: obstacles cover a tiny fraction of the map.
    }

    /// <summary>A random unblocked spot along one of the map's four edges — where Grubs burrow in and wandering Bramblekin arrive.</summary>
    private Vector3 RandomEdgeSpot(float clearance, float edgeMargin)
    {
        float half = Terrain.Size / 2f - edgeMargin;
        Vector3 candidate = new(-half, 0f, 0f);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float along = (float)(Rng.NextDouble() * 2.0 - 1.0) * half;
            candidate = Rng.Next(4) switch
            {
                0 => new Vector3(-half, 0f, along),
                1 => new Vector3(half, 0f, along),
                2 => new Vector3(along, 0f, -half),
                _ => new Vector3(along, 0f, half),
            };
            if (!IsBlocked(candidate, clearance))
                return candidate;
        }
        return candidate;
    }

    /// <summary>Kin Inspector: selects the living Bramblekin nearest <paramref name="groundPoint"/> within <see cref="KinSelectionRadius"/>, or clears the selection on a tap at empty ground.</summary>
    public void TrySelectKinAt(Vector3 groundPoint)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = KinSelectionRadius * KinSelectionRadius;
        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsDead)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(groundPoint, kin.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = kin;
                bestDistanceSquared = distanceSquared;
            }
        }
        SelectedKin = best;
    }

    // --- Food handling ------------------------------------------------------------------

    /// <summary>A Bramblekin picks <paramref name="food"/> up: it's hidden from the map (and from every search) until eaten, dropped or handed over.</summary>
    public static void PickUpFood(FoodShard food)
    {
        food.IsCarried = true;
        food.ClaimedBy = null;
        food.ClaimTimer = 0f;
    }

    /// <summary>A Bramblekin finished eating <paramref name="food"/>: its pool slot is freed.</summary>
    public void ConsumeFood(FoodShard food)
    {
        food.Deactivate();
        FoodEaten++;
    }

    /// <summary>Puts carried <paramref name="food"/> back on the ground at <paramref name="position"/>, loose for anyone to find.</summary>
    public static void DropFood(FoodShard food, Vector3 position)
    {
        food.Position = Grounded(position);
        food.IsCarried = false;
        food.DespawnTimer = FoodShard.DespawnLifespan;
    }

    /// <summary>A Grub eats <paramref name="food"/> off the ground. Returns false if someone got to it first.</summary>
    public bool GrubEat(FoodShard food)
    {
        if (!food.IsActive || food.IsCarried)
            return false;

        food.Deactivate();
        return true;
    }

    /// <summary>Hostility pays off: <paramref name="thief"/> takes <paramref name="victim"/>'s carried food on a successful blow.</summary>
    public void StealFood(Bramblekin thief, Bramblekin victim)
    {
        if (victim.SurrenderFood() is not { } food)
            return;

        thief.ReceiveFood(food);
        Thefts++;
        QueueFloatingText(thief.Position, "Stolen!", HostileTextColor);
    }

    /// <summary>Queues <paramref name="count"/> pieces of Food in a ring of radius <paramref name="distance"/> around <paramref name="center"/>.</summary>
    private void ScatterFoodAround(Vector3 center, int count, float distance, FoodShardKind kind)
    {
        float baseAngle = (float)(Rng.NextDouble() * MathF.Tau);
        float half = Terrain.Size / 2f - Bramblekin.EdgeMargin;
        for (int i = 0; i < count; i++)
        {
            float angle = baseAngle + i * MathF.Tau / count;
            var position = new Vector3(
                Math.Clamp(center.X + MathF.Cos(angle) * distance, -half, half),
                Terrain.GroundHeight,
                Math.Clamp(center.Z + MathF.Sin(angle) * distance, -half, half));
            _pendingFoodSpawns.Add((position, kind));
        }
    }

    // --- Deaths -----------------------------------------------------------------------

    /// <summary>
    /// A Bramblekin dies: it drops any carried food and is marked dead
    /// immediately (so nothing keeps targeting it), but its removal from
    /// <see cref="Colony"/> is deferred to the end of the frame so this is
    /// safe to call from inside a Colony iteration (a strike, a pounce).
    /// </summary>
    public void Kill(Bramblekin kin, DeathCause cause, ICombatant? killer)
    {
        if (kin.IsDead)
            return; // Already dead this frame; don't double-count it.

        kin.MarkDead();
        _pendingKinRemovals.Add(kin);

        string how;
        switch (cause)
        {
            case DeathCause.Starvation:
                DeathsByStarvation++;
                how = "starved to death";
                break;
            case DeathCause.Kin:
                DeathsByKin++;
                how = killer is Bramblekin attacker ? $"was killed by #{attacker.ID}" : "was killed by another Bramblekin";
                break;
            default:
                DeathsByPredator++;
                how = killer switch
                {
                    WolfSpider => "was caught by the Wolf Spider",
                    Hornet => "was stung to death by hornets",
                    _ => "was killed by a predator",
                };
                break;
        }
        Game.AddEventLog($"[DEATH] #{kin.ID} {how}");
    }

    /// <summary>
    /// Damages the Wolf Spider and, if that brings its Health to 0, slays
    /// it: a splat, a scatter of Food where it fell (the prize for bringing
    /// it down), and the respawn timer starts.
    /// </summary>
    public void DamageSpider(int amount, Bramblekin attacker)
    {
        if (Spider is not { IsDead: false } spider)
            return;

        spider.TakeDamage(amount);
        if (spider.Health > 0)
            return;

        spider.MarkDead();
        _splats.Add((spider.Position, SplatDuration));
        ScatterFoodAround(spider.Position, SpiderCarcassFood, 0.6f, FoodShardKind.Meat);
        Spider = null;
        SpiderRespawnTimer = SpiderRespawnDelay;
        SpidersKilled++;

        KinGroup? group = GroupOf(attacker);
        Game.AddEventLog(group is null
            ? $"[HUNT] #{attacker.ID} slew the Wolf Spider alone!"
            : $"[HUNT] Group {group.ShortId} brought down the Wolf Spider (final blow by #{attacker.ID})");
    }

    /// <summary>A Hornet swatted out of the air. Removal from <see cref="Hornets"/> is deferred to the end of the frame.</summary>
    public void KillHornet(Hornet hornet)
    {
        if (hornet.IsDead)
            return;

        hornet.MarkDead();
        _pendingHornetRemovals.Add(hornet);
        HornetsKilled++;
    }

    /// <summary>A hunted Grub: drops a bit of Food, plus some of whatever it had eaten. Removal from <see cref="Grubs"/> is deferred to the end of the frame.</summary>
    public void KillGrub(Grub grub)
    {
        if (grub.IsDead)
            return;

        grub.MarkDead();
        _pendingGrubRemovals.Add(grub);
        GrubsKilled++;
        ScatterFoodAround(grub.Position, 1 + Math.Min(grub.FoodEaten, Grub.MaxCarcassFood - 1), 0.3f, FoodShardKind.Meat);
    }

    // --- Spawners -----------------------------------------------------------------------

    /// <summary>Passive Foraging: a wild Berry every <see cref="BerrySpawnInterval"/> seconds, up to <see cref="MaxBerries"/>.</summary>
    private void UpdateBerrySpawn(float deltaTime)
    {
        _berrySpawnTimer -= deltaTime;
        if (_berrySpawnTimer > 0f)
            return;
        _berrySpawnTimer = BerrySpawnInterval;

        int berries = 0;
        foreach (FoodShard food in FoodShards)
        {
            if (food.IsActive && food.Kind == FoodShardKind.Berry)
                berries++;
        }
        berries += _pendingFoodSpawns.Count(f => f.Kind == FoodShardKind.Berry);
        if (berries >= MaxBerries)
            return;

        _pendingFoodSpawns.Add((RandomBerrySpot(), FoodShardKind.Berry));
    }

    /// <summary>Somewhere in a Berry Patch (<see cref="BerryPatchChance"/> of the time), else anywhere open on the map.</summary>
    private Vector3 RandomBerrySpot()
    {
        if (_berryPatches.Count > 0 && Rng.NextDouble() < BerryPatchChance)
        {
            Vector3 anchor = _berryPatches[Rng.Next(_berryPatches.Count)];
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float radius = MathF.Sqrt((float)Rng.NextDouble()) * BerryPatchRadius;
            Vector3 spot = anchor + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            if (Terrain.Contains(spot, 1f) && !IsBlocked(spot, FoodShard.Radius + 0.1f))
                return spot;
        }
        return RandomFreePoint(FoodShard.Radius + 0.3f, edgeMargin: 1f);
    }

    private void UpdateSpiderRespawn(float deltaTime)
    {
        if (Spider is not null || SpiderRespawnTimer <= 0f)
            return;

        SpiderRespawnTimer -= deltaTime;
        if (SpiderRespawnTimer <= 0f)
        {
            SpawnSpider();
            Game.AddEventLog("[PREDATOR] A new Wolf Spider has moved in");
        }
    }

    /// <summary>
    /// The Hornet Swarm's spawner: tops the population up in whole clusters
    /// of <see cref="HornetSwarmMinSize"/>-<see cref="HornetSwarmMaxSize"/>
    /// Hornets at once. Each cluster nests around a randomly chosen
    /// <see cref="GardenProp"/> — which can easily be a Berry Patch's
    /// Dandelion, making that patch a risk worth weighing.
    /// </summary>
    private void UpdateHornetSpawn(float deltaTime)
    {
        _hornetSpawnTimer -= deltaTime;
        if (_hornetSpawnTimer > 0f)
            return;
        _hornetSpawnTimer = HornetSpawnInterval;

        int living = Hornets.Count(h => !h.IsDead) + _pendingHornetSpawns.Count;
        if (living + HornetSwarmMinSize > MaxHornetsOnMap)
            return;

        Vector3 anchor = GardenProps.Count > 0
            ? GardenProps[Rng.Next(GardenProps.Count)].Position
            : RandomFreePoint(Hornet.BodyRadius + 0.1f, Hornet.EdgeMargin);

        int clusterSize = HornetSwarmMinSize + Rng.Next(HornetSwarmMaxSize - HornetSwarmMinSize + 1);
        for (int i = 0; i < clusterSize && living + i < MaxHornetsOnMap; i++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float jitter = (float)Rng.NextDouble() * Hornet.ClusterJitterRadius;
            Vector3 spot = anchor + new Vector3(MathF.Cos(angle) * jitter, 0f, MathF.Sin(angle) * jitter);
            if (!Terrain.Contains(spot, Hornet.EdgeMargin))
                spot = anchor;
            _pendingHornetSpawns.Add(new Hornet(spot, anchor, Rng));
        }
    }

    /// <summary>Grubs burrow in one at a time from the map's edges, up to <see cref="MaxGrubsOnMap"/>.</summary>
    private void UpdateGrubSpawn(float deltaTime)
    {
        _grubSpawnTimer -= deltaTime;
        if (_grubSpawnTimer > 0f)
            return;
        _grubSpawnTimer = GrubSpawnInterval;

        int living = Grubs.Count(g => !g.IsDead) + _pendingGrubSpawns.Count;
        if (living >= MaxGrubsOnMap)
            return;

        _pendingGrubSpawns.Add(new Grub(RandomEdgeSpot(Grub.BodyRadius + 0.1f, Grub.EdgeMargin), Rng));
    }

    /// <summary>
    /// Wandering Arrivals: a new solitary Bramblekin, with its own freshly
    /// randomized Personality, drifts in from a random edge every
    /// <see cref="ArrivalInterval"/> seconds while the population is below
    /// <see cref="MaxPopulation"/>.
    /// </summary>
    private void UpdateArrivals(float deltaTime)
    {
        _arrivalTimer -= deltaTime;
        if (_arrivalTimer > 0f)
            return;
        _arrivalTimer = ArrivalInterval;

        int living = Colony.Count(b => !b.IsDead) + _pendingKinSpawns.Count;
        if (living >= MaxPopulation)
            return;

        var kin = new Bramblekin(RandomEdgeSpot(Bramblekin.BodyRadius, Bramblekin.EdgeMargin + 0.5f), Rng);
        _pendingKinSpawns.Add(kin);
        Arrivals++;
        Personality p = kin.Personality;
        Game.AddEventLog($"[ARRIVAL] #{kin.ID} wandered in (aggr {p.Aggression:0.00}, soc {p.Sociability:0.00}, int {p.Intelligence:0.00})");
    }

    /// <summary>Dibs failsafe — see <see cref="FoodClaimTimeoutSeconds"/>.</summary>
    private void UpdateFoodClaimTimeouts(float deltaTime)
    {
        foreach (FoodShard food in FoodShards)
        {
            if (!food.IsActive || food.ClaimedBy is null)
                continue;

            food.ClaimTimer += deltaTime;
            if (food.ClaimTimer >= FoodClaimTimeoutSeconds)
            {
                food.ClaimedBy = null;
                food.ClaimTimer = 0f;
            }
        }
    }

    /// <summary>Loose Food that nobody picks up rots away after <see cref="FoodShard.DespawnLifespan"/> seconds; carried Food never does.</summary>
    private void UpdateFoodDespawn(float deltaTime)
    {
        foreach (FoodShard food in FoodShards)
        {
            if (!food.IsActive || food.IsCarried)
                continue;

            food.DespawnTimer -= deltaTime;
            if (food.DespawnTimer <= 0f)
                food.Deactivate();
        }
    }

    public void QueueFloatingText(Vector3 position, string text, Color color) =>
        _floatingTexts.Add((position, text, color, FloatingTextDuration));

    // --- Rendering -----------------------------------------------------------------------

    /// <summary>
    /// Raylib Culling: margin (px) added around the screen rectangle when
    /// deciding whether a projected point is "on screen" for
    /// <see cref="IsOnScreen"/> — generous enough that an entity's body
    /// doesn't visibly pop in right at the screen edge.
    /// </summary>
    private const float CullScreenMargin = 40f;

    /// <summary>Basic bounds check: true unless <paramref name="worldPosition"/> projects to a screen point entirely outside the camera's current viewport (plus <see cref="CullScreenMargin"/>).</summary>
    private static bool IsOnScreen(Vector3 worldPosition, Camera3D camera)
    {
        Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);
        return screen.X >= -CullScreenMargin && screen.X <= Raylib.GetScreenWidth() + CullScreenMargin &&
               screen.Y >= -CullScreenMargin && screen.Y <= Raylib.GetScreenHeight() + CullScreenMargin;
    }

    /// <summary>
    /// Frustum/Distance Culling: nothing culled from drawing here is ever
    /// gated in Update — every entity keeps simulating regardless of what
    /// the camera can see. Radius (m), measured in 2D (X/Z) from
    /// <see cref="Camera3D.Target"/>, beyond which things simply aren't drawn.
    /// </summary>
    public const float RenderRadius = 60.0f;

    /// <summary>True if <paramref name="worldPosition"/> is within <see cref="RenderRadius"/> (2D, X/Z) of the camera's target.</summary>
    private static bool IsWithinRenderRadius(Vector3 worldPosition, Camera3D camera)
    {
        float dx = worldPosition.X - camera.Target.X;
        float dz = worldPosition.Z - camera.Target.Z;
        return dx * dx + dz * dz <= RenderRadius * RenderRadius;
    }

    private bool IsVisible(Vector3 worldPosition, Camera3D camera) =>
        IsWithinRenderRadius(worldPosition, camera) && IsOnScreen(worldPosition, camera);

    public void Draw(Camera3D camera)
    {
        Terrain.Draw(camera.Target, RenderRadius);
        for (int i = _splats.Count - 1; i >= 0; i--)
        {
            var (position, timeLeft) = _splats[i];
            // A dark stain that fades out.
            byte alpha = (byte)(200 * Math.Clamp(timeLeft / 2f, 0f, 1f));
            Raylib.DrawCylinder(position + new Vector3(0, 0.012f, 0), 0.9f, 0.9f, 0.005f, 20, new Color(30, 25, 20, (int)alpha));
        }

        for (int i = GardenProps.Count - 1; i >= 0; i--)
        {
            GardenProp prop = GardenProps[i];
            if (IsVisible(prop.Position, camera))
                prop.Draw();
        }

        // Object Pooling: most Food slots sit inactive at any given time, so
        // every loop over the pool must skip anything with IsActive false.
        for (int i = FoodShards.Count - 1; i >= 0; i--)
        {
            FoodShard food = FoodShards[i];
            if (food.IsActive && !food.IsCarried && IsVisible(food.Position, camera))
                food.Draw(food.Position);
        }

        // Reverse for-loops, skipping anything marked dead this frame: its
        // removal is deferred, so without the check a creature killed a
        // moment ago would still be drawn standing there.
        for (int i = Hornets.Count - 1; i >= 0; i--)
        {
            if (!Hornets[i].IsDead && IsVisible(Hornets[i].Position, camera))
                Hornets[i].Draw();
        }

        for (int i = Grubs.Count - 1; i >= 0; i--)
        {
            if (!Grubs[i].IsDead && IsVisible(Grubs[i].Position, camera))
                Grubs[i].Draw();
        }

        // Group tethers: a faint line in the group's colour from every
        // follower's head to its Leader's, so who runs with whom reads at a
        // glance.
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Leader is not { IsDead: false } leader)
                continue;

            Vector3 leaderHead = leader.Position + new Vector3(0, Bramblekin.BodyHeight, 0);
            var tether = new Color(group.Color.R, group.Color.G, group.Color.B, (byte)120);
            foreach (Bramblekin member in group.Members)
            {
                if (member == leader || member.IsDead || !IsWithinRenderRadius(member.Position, camera))
                    continue;
                Raylib.DrawLine3D(member.Position + new Vector3(0, Bramblekin.BodyHeight, 0), leaderHead, tether);
            }
        }

        for (int i = Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin b = Colony[i];
            if (!b.IsDead && IsVisible(b.Position, camera))
                b.Draw(this);
        }

        if (Spider is { IsDead: false } spider)
            spider.Draw();

        // Kin Inspector: ring the selected Bramblekin, and trace its
        // Intelligence-scaled detection radius over the hills.
        if (SelectedKin is { IsDead: false } selected)
        {
            DrawTerrainRing(selected.Position, 0.5f, new Color(255, 230, 60, 255));
            DrawTerrainRing(selected.Position, selected.DetectionRadius, new Color(255, 255, 255, 140));
        }
    }

    /// <summary>A circle of <paramref name="radius"/> around <paramref name="center"/>, drawn as line segments that follow the terrain's height.</summary>
    private static void DrawTerrainRing(Vector3 center, float radius, Color color)
    {
        const int segments = 64;
        Vector3 previous = Grounded(center + new Vector3(radius, 0f, 0f), 0.08f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * MathF.Tau / segments;
            Vector3 point = Grounded(center + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius), 0.08f);
            Raylib.DrawLine3D(previous, point, color);
            previous = point;
        }
    }
}

// =============================================================================
//  Food
// =============================================================================

/// <summary>Where a piece of Food came from — purely cosmetic, it's worth the same either way.</summary>
public enum FoodShardKind
{
    /// <summary>Passive Foraging: a wild Berry. Red.</summary>
    Berry,

    /// <summary>Dropped by a hunted Grub or a slain Wolf Spider. Orange.</summary>
    Meat,
}

/// <summary>
/// Loose Food: a single bite — a wild Berry or a scrap of meat — lying on
/// the ground for any Bramblekin (or Grub) to find. A Bramblekin either eats
/// it on the spot or carries one as a reserve, which is exactly what a
/// starving, aggressive neighbour may try to steal.
/// </summary>
public sealed class FoodShard
{
    public const float Radius = 0.18f;

    /// <summary>Resting spot on the ground. Ignored while carried.</summary>
    public Vector3 Position { get; set; }

    /// <summary>True while a Bramblekin is holding it; carried Food is hidden from the map and from every search.</summary>
    public bool IsCarried { get; set; }

    /// <summary>Where it came from. Only affects colour.</summary>
    public FoodShardKind Kind { get; private set; }

    /// <summary>
    /// Object Pooling: false for a pool slot that isn't currently real Food
    /// on the map. World pre-allocates a fixed pool of these at startup (see
    /// <see cref="World.FoodShards"/>) instead of constructing and destroying
    /// one per spawn/pickup/despawn; every rendering and targeting loop over
    /// the pool must skip anything with this false.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Dibs: the one Bramblekin currently walking to this Food, if any — see
    /// <see cref="World.IsAvailable"/>. Released the moment that Bramblekin
    /// stops foraging for it, and force-released after
    /// <see cref="World.FoodClaimTimeoutSeconds"/> as a failsafe.
    /// </summary>
    public Bramblekin? ClaimedBy { get; set; }

    /// <summary>Seconds since <see cref="ClaimedBy"/> was last set. Reset to 0 on every new claim; ticked and enforced by World.</summary>
    public float ClaimTimer { get; set; }

    /// <summary>Seconds uncarried Food sits on the map before it rots away — see <see cref="DespawnTimer"/>.</summary>
    public const float DespawnLifespan = 60f;

    /// <summary>Counts down from <see cref="DespawnLifespan"/> while this Food lies on the ground uncarried; World removes it at 0.</summary>
    public float DespawnTimer { get; set; } = DespawnLifespan;

    /// <summary>Constructs an inactive pool slot — see <see cref="World.FoodShards"/>. Call <see cref="Activate"/> to actually spawn one.</summary>
    public FoodShard()
    {
    }

    /// <summary>Object Pooling: reuses this pool slot as freshly spawned Food at <paramref name="groundPoint"/>, resetting every bit of its previous state.</summary>
    public void Activate(Vector3 groundPoint, FoodShardKind kind)
    {
        Position = World.Grounded(groundPoint); // Snap onto the hilly terrain.
        Kind = kind;
        IsCarried = false;
        ClaimedBy = null;
        ClaimTimer = 0f;
        DespawnTimer = DespawnLifespan;
        IsActive = true;
    }

    /// <summary>Object Pooling: returns this slot to the pool — eaten or rotted away. See <see cref="World.FoodShards"/>.</summary>
    public void Deactivate()
    {
        IsActive = false;
        IsCarried = false;
        ClaimedBy = null;
    }

    /// <summary>Draws the Food resting on the ground at (or carried above) <paramref name="groundPoint"/>.</summary>
    public void Draw(Vector3 groundPoint)
    {
        Color color = Kind == FoodShardKind.Berry ? new Color(210, 40, 45, 255) : new Color(245, 150, 45, 255);
        Raylib.DrawSphere(groundPoint + new Vector3(0, Radius, 0), Radius, color);
    }
}

// =============================================================================
//  Part 4: Oversized Garden Props
// =============================================================================

/// <summary>Which kind of static <see cref="GardenProp"/> decoration this is.</summary>
public enum GardenPropKind
{
    /// <summary>A large gray pebble/rock — a hemisphere pressed into the lawn.</summary>
    Pebble,

    /// <summary>A long brown twig lying flat, randomly rotated.</summary>
    Twig,

    /// <summary>A tall dandelion/flower — a green stem topped with a large yellow or white puff, towering over Bramblekin scale.</summary>
    Dandelion,
}

/// <summary>
/// Part 4, Oversized Garden Props: a static piece of backyard scenery — a
/// Pebble, a lying Twig, or a towering Dandelion — scattered across the map
/// by <see cref="World.SpawnGardenProps"/>. No Update, only Draw (and
/// Part 2/6's culling/grounding, applied by the caller and at construction
/// respectively). A Pebble is the one solid prop: its
/// <see cref="FootprintRadius"/> becomes an <see cref="Obstacle"/> that
/// walkers steer around.
/// </summary>
public sealed class GardenProp
{
    public Vector3 Position { get; }
    public GardenPropKind Kind { get; }

    /// <summary>Random facing (radians) — mainly meaningful for a Twig lying flat.</summary>
    private readonly float _rotation;

    /// <summary>Twig length (m), randomized per-instance so the map doesn't read as identical copies.</summary>
    private readonly float _twigLength;

    /// <summary>Whether this Dandelion's puff is yellow (a true dandelion) or white (a seed-head/dandelion clock).</summary>
    private readonly bool _isYellow;

    /// <summary>Per-instance size variation (0.8-1.3x), so a field of the same prop kind doesn't look copy-pasted.</summary>
    private readonly float _scale;

    /// <summary>Solid footprint radius (m) — a Pebble's dome; Twigs and Dandelions are walked over/around freely (0).</summary>
    public float FootprintRadius => Kind == GardenPropKind.Pebble ? 0.5f * _scale : 0f;

    public GardenProp(Vector3 groundPosition, GardenPropKind kind, float rotation, Random rng)
    {
        // Follow-up Part 2: the caller already grounded this point via
        // World.Grounded — add a small explicit lift here too so a Pebble/
        // Twig/Dandelion's base doesn't visually sink into a slope.
        Position = groundPosition + new Vector3(0, 0.06f, 0);
        Kind = kind;
        _rotation = rotation;
        _twigLength = 0.6f + (float)rng.NextDouble() * 0.9f;
        _isYellow = rng.NextDouble() < 0.7;
        _scale = 0.8f + (float)rng.NextDouble() * 0.5f;
    }

    public void Draw()
    {
        switch (Kind)
        {
            case GardenPropKind.Pebble:
                DrawPebble();
                break;
            case GardenPropKind.Twig:
                DrawTwig();
                break;
            case GardenPropKind.Dandelion:
                DrawDandelion();
                break;
        }
    }

    /// <summary>
    /// Follow-up Part 3, Surface-Normal Tilting: pushes an Rlgl matrix
    /// translated to this prop's ground position and rotated to match the
    /// terrain's surface normal there, then translated locally up by
    /// <paramref name="halfHeight"/> so the shape's local origin (0,0,0)
    /// rests un-buried on the dirt. Caller draws its primitive(s) at local
    /// origin and then calls <see cref="Rlgl.PopMatrix"/>.
    /// </summary>
    private void PushGroundedTiltMatrix(float halfHeight)
    {
        Vector3 normal = World.GetNormalAt(Position.X, Position.Z);
        Vector3 axis = Vector3.Cross(Vector3.UnitY, normal);
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitY, normal), -1.0f, 1.0f)) * (180.0f / MathF.PI);

        Rlgl.PushMatrix();
        Rlgl.Translatef(Position.X, World.GetHeightAt(Position.X, Position.Z), Position.Z);
        if (axis.Length() > 0.001f)
            Rlgl.Rotatef(angle, axis.X, axis.Y, axis.Z);
        Rlgl.Translatef(0, halfHeight, 0);
    }

    /// <summary>A large gray hemisphere-ish rock, oversized against a Bramblekin.</summary>
    private void DrawPebble()
    {
        float radius = 0.5f * _scale;
        var stone = new Color(130, 130, 135, 255);
        var stoneEdge = new Color(80, 80, 85, 200);

        PushGroundedTiltMatrix(radius * 0.55f);

        // Squash a full sphere into a rock-like dome via Rlgl scaling.
        Rlgl.PushMatrix();
        Rlgl.Scalef(1f, 0.6f, 1f);
        Raylib.DrawSphere(Vector3.Zero, radius, stone);
        Raylib.DrawSphereWires(Vector3.Zero, radius, 8, 8, stoneEdge);
        Rlgl.PopMatrix();

        Rlgl.PopMatrix();
    }

    /// <summary>A long brown cylinder lying flat on the ground, randomly rotated — DrawCylinderEx avoids any manual rotation matrix.</summary>
    private void DrawTwig()
    {
        float length = _twigLength * _scale;
        float radius = 0.05f * _scale;
        var brown = new Color(101, 67, 33, 255);

        PushGroundedTiltMatrix(radius);

        var half = new Vector3(MathF.Cos(_rotation), 0, MathF.Sin(_rotation)) * (length / 2f);
        Vector3 start = -half;
        Vector3 end = half;
        Raylib.DrawCylinderEx(start, end, radius, radius * 0.7f, 8, brown);

        Rlgl.PopMatrix();
    }

    /// <summary>A tall green stem topped with a large fluffy sphere — towers well above Bramblekin scale.</summary>
    private void DrawDandelion()
    {
        float stemHeight = 1.4f * _scale;
        float stemRadius = 0.04f * _scale;
        float puffRadius = 0.35f * _scale;
        var stemColor = new Color(60, 130, 40, 255);
        Color puffColor = _isYellow ? new Color(250, 210, 40, 255) : new Color(245, 245, 235, 220);

        PushGroundedTiltMatrix(stemHeight / 2f);

        var stemBase = new Vector3(0, -stemHeight / 2f, 0);
        var stemTop = new Vector3(0, stemHeight / 2f, 0);
        Raylib.DrawCylinder(stemBase, stemRadius, stemRadius, stemHeight, 8, stemColor);
        Raylib.DrawSphere(stemTop + new Vector3(0, puffRadius * 0.6f, 0), puffRadius, puffColor);

        Rlgl.PopMatrix();
    }
}


// =============================================================================
//  Ground movement shared by every creature
// =============================================================================

/// <summary>
/// Walks a round body across the terrain: steers around obstacles, pushes
/// itself back out of anything it overlaps, stays on the terrain, and takes a
/// short sideways detour if it stops making progress. Used by both the
/// Bramblekin and the Wolf Spider.
/// </summary>
public sealed class GroundMover
{
    /// <summary>
    /// Within this distance of a target counts as "arrived" — snapping
    /// exactly onto it rather than taking one more tiny step. Loosened from
    /// a hair-trigger 0.05 m so a walker settles cleanly instead of
    /// endlessly re-approaching by a few centimeters at a time (e.g. after
    /// PushOutOfObstacles nudges it back off a goal sitting right at an
    /// obstacle's edge) — the "smoothed arrival" half of the High-Speed
    /// Physics fix, alongside the callers' own more forgiving pickup/contact
    /// radii.
    /// </summary>
    private const float ArriveDistance = 0.15f;

    /// <summary>How far ahead (m) the walker looks for obstacles in its path.</summary>
    private const float LookAhead = 1.5f;

    /// <summary>Gap (m) the walker tries to keep between itself and an obstacle while passing it.</summary>
    private const float AvoidMargin = 0.15f;

    /// <summary>How strongly avoidance bends the heading (1 = 45° at most, higher = sharper).</summary>
    private const float SteerStrength = 1.5f;

    /// <summary>Progress is checked this often (s); too little movement means we're stuck.</summary>
    private const float StuckCheckInterval = 1.0f;

    /// <summary>Fraction of the expected distance that must be covered per check to not count as stuck.</summary>
    private const float StuckProgressFraction = 0.3f;

    /// <summary>
    /// Map Bounds Fix: the strict distance from the 100x100 map's center
    /// (0, 0) on either the X or Z axis a walker may not cross — see
    /// <see cref="MoveTowards"/>. Kept a little inside the terrain's own
    /// hard edge (<see cref="ClampToTerrain"/>'s [-50, 50]-ish clamp) so a
    /// unit is steered back toward the garden long before it could ever
    /// walk off the map, rather than relying on the terrain clamp alone to
    /// silently teleport it back onto the edge every frame.
    /// </summary>
    private const float MapBoundaryLimit = 48.0f;

    private readonly Random _rng;
    private readonly float _edgeMargin;
    private Vector3 _progressAnchor;
    private float _progressTimer;
    private Vector3? _detour;

    /// <summary>Feet position on the ground (y = GroundHeight).</summary>
    public Vector3 Position { get; set; }

    /// <summary>Unit (x, z) direction of the last step taken; used for drawing facing.</summary>
    public Vector2 Heading { get; set; } = Vector2.UnitX;

    public float BodyRadius { get; }

    /// <summary>True if the body moved this frame (for walk animations).</summary>
    public bool IsMoving { get; private set; }

    public GroundMover(Vector3 position, float bodyRadius, float edgeMargin, Random rng)
    {
        Position = position;
        BodyRadius = bodyRadius;
        _edgeMargin = edgeMargin;
        _rng = rng;
        ResetProgress();
    }

    /// <summary>Forget any detour and restart stuck detection (call on every change of plan).</summary>
    public void ResetProgress()
    {
        _detour = null;
        _progressTimer = 0f;
        _progressAnchor = Position;
    }

    /// <summary>Marks the body as standing still this frame.</summary>
    public void Idle() => IsMoving = false;

    /// <summary>
    /// Instantly displaces the body by <paramref name="offset"/> — e.g. a
    /// Warning Shove — then re-clamps it to the terrain and pushes it back
    /// out of any obstacle the displacement landed it inside, exactly as
    /// normal movement would. Doesn't touch the current target/detour or
    /// stuck-detection: whatever the body was doing, it keeps doing it from
    /// its new spot.
    /// </summary>
    public void Nudge(Vector3 offset, World world)
    {
        Position += offset;
        PushOutOfObstacles(world.Obstacles);
        ClampToTerrain(world.Terrain);
    }

    /// <summary>
    /// Moves toward <paramref name="target"/> at <paramref name="speed"/>,
    /// steering around obstacles, then pushes the body back out of anything
    /// it still overlaps. <paramref name="isSafeSpot"/> vets detour points.
    /// Returns true on arrival.
    /// </summary>
    public bool MoveTowards(Vector3 target, float speed, float deltaTime, World world, Func<Vector3, bool> isSafeSpot)
    {
        IReadOnlyList<Obstacle> obstacles = world.Obstacles;
        float step = speed * deltaTime;
        CheckIfStuck(target, step, deltaTime, world, isSafeSpot);

        // Head for the detour waypoint first, if we're working our way round something.
        Vector3 goal = _detour ?? target;
        var position = new Vector2(Position.X, Position.Z);
        var toGoal = new Vector2(goal.X, goal.Z) - position;
        float distance = toGoal.Length();

        bool arrived = distance <= MathF.Max(step, ArriveDistance);
        if (arrived)
        {
            position = new Vector2(goal.X, goal.Z);
        }
        else
        {
            Vector2 heading = Steer(position, toGoal / distance, MathF.Min(distance, LookAhead), goal, obstacles);

            // Map Bounds Fix: before applying velocity to X or Z, turn the
            // walker back toward the garden's center the instant it's
            // already past MapBoundaryLimit on that axis, rather than
            // letting it march indefinitely toward the terrain's hard edge
            // (or off it, before ClampToTerrain silently caught it below).
            // Negating just the offending axis's component, not the whole
            // heading, still lets the other axis's steering continue
            // normally. Direction-aware: only flip when the heading is
            // still pointing further away from center on that axis — a
            // blind sign flip could instead reverse a heading that was
            // already correctly steering back in, sending the unit further
            // off the map (units marching out straight and
            // vanishing off the terrain's hard edge).
            if (position.X > MapBoundaryLimit && heading.X > 0f)
                heading.X = -heading.X;
            else if (position.X < -MapBoundaryLimit && heading.X < 0f)
                heading.X = -heading.X;

            if (position.Y > MapBoundaryLimit && heading.Y > 0f)
                heading.Y = -heading.Y;
            else if (position.Y < -MapBoundaryLimit && heading.Y < 0f)
                heading.Y = -heading.Y;

            position += heading * step;
            Heading = heading;
        }

        Vector3 before = Position;
        Position = new Vector3(position.X, Terrain.GroundHeight, position.Y);
        PushOutOfObstacles(obstacles);
        ClampToTerrain(world.Terrain);
        IsMoving = Vector3.DistanceSquared(before, Position) > 1e-8f;

        if (arrived && _detour is not null)
        {
            _detour = null; // Detour done; resume toward the real target next frame.
            return false;
        }
        return arrived;
    }

    /// <summary>
    /// Simple obstacle avoidance. Finds the nearest obstacle whose circle
    /// (grown by our body radius plus a margin) crosses the straight path
    /// ahead, and bends the heading away from it. Close to the obstacle the
    /// sideways push dominates, so the walker slides round its edge instead
    /// of walking into it; once past, the path is clear and it straightens.
    /// </summary>
    private Vector2 Steer(Vector2 position, Vector2 direction, float lookAhead, Vector3 goal,
                          IReadOnlyList<Obstacle> obstacles)
    {
        var goal2 = new Vector2(goal.X, goal.Z);
        Vector2 nearestOffset = Vector2.Zero;
        float nearestAlong = float.MaxValue;
        bool found = false;

        foreach (var obstacle in obstacles)
        {
            // Never avoid the thing we're walking to.
            if (Vector2.DistanceSquared(obstacle.Center, goal2) < 0.01f)
                continue;

            Vector2 toObstacle = obstacle.Center - position;
            float along = Vector2.Dot(toObstacle, direction);           // Distance ahead of us.
            if (along <= 0f || along > lookAhead + obstacle.Radius)
                continue;                                                // Behind us or too far.

            Vector2 offset = toObstacle - direction * along;             // Sideways offset from our path.
            float clearance = obstacle.Radius + BodyRadius + AvoidMargin;
            if (offset.LengthSquared() >= clearance * clearance)
                continue;                                                // Path passes it cleanly.

            if (along < nearestAlong)
            {
                nearestOffset = offset;
                nearestAlong = along;
                found = true;
            }
        }

        if (!found)
            return direction;

        // Push away from the obstacle's side of the path. Dead-centre hits
        // have no side, so always go left for consistency.
        Vector2 away = nearestOffset.LengthSquared() > 1e-6f
            ? -Vector2.Normalize(nearestOffset)
            : new Vector2(-direction.Y, direction.X);

        return Vector2.Normalize(direction + away * SteerStrength);
    }

    /// <summary>Solid collision: slide the body back outside any obstacle it overlaps.</summary>
    private void PushOutOfObstacles(IReadOnlyList<Obstacle> obstacles)
    {
        var position = new Vector2(Position.X, Position.Z);
        foreach (var obstacle in obstacles)
        {
            Vector2 offset = position - obstacle.Center;
            float minDistance = obstacle.Radius + BodyRadius;
            float distanceSquared = offset.LengthSquared();
            if (distanceSquared >= minDistance * minDistance)
                continue;

            float distance = MathF.Sqrt(distanceSquared);
            Vector2 normal = distance > 1e-5f ? offset / distance : Vector2.UnitX;
            position = obstacle.Center + normal * minDistance;
        }
        Position = new Vector3(position.X, Terrain.GroundHeight, position.Y);
    }

    private void ClampToTerrain(Terrain terrain)
    {
        float half = terrain.Size / 2f - BodyRadius;
        Position = new Vector3(
            Math.Clamp(Position.X, -half, half),
            Terrain.GroundHeight,
            Math.Clamp(Position.Z, -half, half));
    }

    /// <summary>
    /// Safety net for when steering alone can't get past something (e.g. two
    /// rocks with a gap narrower than our body). If we covered too little
    /// ground since the last check, head for a sideways waypoint for a bit.
    /// </summary>
    private void CheckIfStuck(Vector3 target, float step, float deltaTime, World world, Func<Vector3, bool> isSafeSpot)
    {
        _progressTimer += deltaTime;
        if (_progressTimer < StuckCheckInterval)
            return;

        float expected = step / deltaTime * StuckCheckInterval;
        float moved = HorizontalDistance(Position, _progressAnchor);
        bool nearTarget = HorizontalDistance(Position, target) < 0.5f;

        if (moved < expected * StuckProgressFraction && !nearTarget && _detour is null)
            _detour = PickDetour(target, world, isSafeSpot);

        _progressTimer = 0f;
        _progressAnchor = Position;
    }

    /// <summary>A free point ~1.5 m to the left or right of the line toward the target.</summary>
    private Vector3? PickDetour(Vector3 target, World world, Func<Vector3, bool> isSafeSpot)
    {
        var forward = new Vector2(target.X - Position.X, target.Z - Position.Z);
        forward = forward.LengthSquared() > 1e-6f ? Vector2.Normalize(forward) : Vector2.UnitX;
        var left = new Vector2(-forward.Y, forward.X);
        float firstSide = _rng.Next(2) == 0 ? 1f : -1f;

        foreach (float side in new[] { firstSide, -firstSide })
        {
            Vector2 offset = left * side * 1.5f - forward * 0.5f;
            var candidate = new Vector3(Position.X + offset.X, Terrain.GroundHeight, Position.Z + offset.Y);
            if (world.Terrain.Contains(candidate, _edgeMargin) && isSafeSpot(candidate))
                return candidate;
        }
        return null;
    }

    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Squared horizontal distance — avoids the <see cref="MathF.Sqrt"/> in <see cref="HorizontalDistance"/> for threshold comparisons (compare against a squared threshold instead).</summary>
    public static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }
}


// =============================================================================
//  Creatures: the Bramblekin
// =============================================================================

/// <summary>
/// Anything a Bramblekin can strike, or be threatened by: another
/// Bramblekin, the Wolf Spider, a Hornet or a Grub.
/// </summary>
public interface ICombatant
{
    Vector3 Position { get; }

    bool IsDead { get; }

    /// <summary>Body radius (m); strike reach is measured edge to edge.</summary>
    float CollisionRadius { get; }

    /// <summary>Takes one strike from <paramref name="attacker"/>.</summary>
    void TakeHit(int damage, Bramblekin attacker, World world);
}

/// <summary>
/// A Bramblekin's DNA: three traits, each 0..1, rolled once when it's
/// spawned into the world and fixed for life.
/// </summary>
public readonly struct Personality
{
    /// <summary>Odds of fighting rather than fleeing a threat, of turning on another Bramblekin when starving, and how hard it hits.</summary>
    public float Aggression { get; }

    /// <summary>Desire to seek out and band together with others (high) versus keeping its distance (low).</summary>
    public float Sociability { get; }

    /// <summary>Scales how far it can detect food, threats and other Bramblekin; the sharpest member of a group leads it.</summary>
    public float Intelligence { get; }

    public Personality(float aggression, float sociability, float intelligence)
    {
        Aggression = Math.Clamp(aggression, 0f, 1f);
        Sociability = Math.Clamp(sociability, 0f, 1f);
        Intelligence = Math.Clamp(intelligence, 0f, 1f);
    }

    /// <summary>A fresh, uniformly random Personality.</summary>
    public static Personality Roll(Random rng) =>
        new((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
}

/// <summary>How one Bramblekin regards another it has met — see <see cref="Bramblekin.KnownKins"/>.</summary>
public enum RelationshipState
{
    Neutral,
    Friend,

    /// <summary>Permanent: once blows (or a robbery) have been traded, no later encounter can undo it.</summary>
    Enemy,
}

/// <summary>What killed a Bramblekin — tallied separately on the HUD.</summary>
public enum DeathCause
{
    Starvation,
    Predator,
    Kin,
}

/// <summary>
/// An emergent band of Bramblekin sharing one <see cref="Bramblekin.GroupId"/>.
/// Membership is owned by the Bramblekin themselves; <see cref="World"/>
/// rebuilds this view of it every frame, dissolving a group down to its
/// last survivor and re-electing the Leader — always the most Intelligent
/// member.
/// </summary>
public sealed class KinGroup
{
    private static readonly Color[] Palette =
    {
        new(60, 120, 220, 255),  // Blue
        new(225, 195, 55, 255),  // Yellow
        new(205, 60, 55, 255),   // Red
        new(150, 80, 195, 255),  // Purple
        new(40, 180, 90, 255),   // Green
        new(240, 130, 40, 255),  // Orange
        new(40, 190, 200, 255),  // Teal
        new(230, 90, 170, 255),  // Pink
    };

    public Guid Id { get; }

    /// <summary>The group's colour: its members' head highlight, its Leader's banner, and the tethers between them.</summary>
    public Color Color { get; }

    /// <summary>First few hex digits of <see cref="Id"/>, for the HUD and event log.</summary>
    public string ShortId => Id.ToString("N")[..4];

    public List<Bramblekin> Members { get; } = new();

    public Bramblekin? Leader { get; private set; }

    public KinGroup(Guid id)
    {
        Id = id;
        Color = Palette[(int)((uint)id.GetHashCode() % (uint)Palette.Length)];
    }

    /// <summary>The living member with the highest Intelligence leads (lowest ID breaks a tie).</summary>
    public void ElectLeader()
    {
        Bramblekin? best = null;
        foreach (Bramblekin member in Members)
        {
            if (member.IsDead)
                continue;
            if (best is null ||
                member.Personality.Intelligence > best.Personality.Intelligence ||
                (member.Personality.Intelligence == best.Personality.Intelligence && member.ID < best.ID))
                best = member;
        }
        Leader = best;
    }
}

/// <summary>What a Bramblekin is currently doing, grouped by the need driving it.</summary>
public enum BramblekinState
{
    // --- Social (fed and safe) ---

    /// <summary>Resting between moves.</summary>
    Idle,

    /// <summary>Roaming to a random nearby point (or milling about near its Leader).</summary>
    Wandering,

    /// <summary>Walking over to meet a stranger it has spotted.</summary>
    Socializing,

    /// <summary>A follower catching up with its group's Leader.</summary>
    Following,

    // --- Critical (hunger) ---

    /// <summary>Hungry with no food in sight: roaming further afield to find some.</summary>
    Searching,

    /// <summary>Walking to a piece of loose Food it has claimed.</summary>
    Foraging,

    /// <summary>Eating the Food it's holding.</summary>
    Eating,

    /// <summary>Chasing down a Grub to eat.</summary>
    Hunting,

    /// <summary>Starving and Aggressive: attacking another Bramblekin to steal its food.</summary>
    Attacking,

    // --- Safety ---

    /// <summary>Running from a threat it chose not to fight.</summary>
    Fleeing,

    /// <summary>Standing its ground against a threat — or defending a groupmate from one.</summary>
    Fighting,
}

/// <summary>
/// An individual survival agent. Each Bramblekin is born solitary with a
/// random <see cref="Personality"/> and, every frame, serves exactly one
/// need, in strict priority order:
///
///   1. Critical — Hunger: once <see cref="IsHungry"/>, it eats what it's
///      carrying, or forages the nearest loose Food it can see, or hunts a
///      Grub, or robs a neighbour (see <see cref="World.ResolveEncounter"/>),
///      or searches further afield. Nothing else matters until it's fed —
///      a hungry Bramblekin will brave a Hornet swarm for a berry.
///   2. Safety: a predator (or a hostile Bramblekin, or anything attacking
///      a groupmate) inside its Intelligence-scaled <see cref="DetectionRadius"/>
///      triggers one Aggression roll per threat — fight or flee.
///   3. Social: fed and safe, a follower stays near its group's Leader;
///      anyone else wanders, pockets a spare piece of Food, and — depending
///      on Sociability — seeks out strangers or keeps its distance.
/// </summary>
public sealed class Bramblekin : ICombatant
{
    private static int _nextId = 0;

    /// <summary>A stable, never-reused identity — what other Bramblekin remember it by in their <see cref="KnownKins"/>.</summary>
    public int ID { get; } = _nextId++;

    // --- Body --------------------------------------------------------------------

    /// <summary>Normal walking speed in m/s.</summary>
    public const float WalkSpeed = 1.5f;

    /// <summary>Flee speed as a multiple of <see cref="WalkSpeed"/> — outruns a Hornet, not a pouncing spider.</summary>
    public const float FleeSpeedMultiplier = 2.2f;

    /// <summary>How long a Bramblekin rests between moves, in seconds (randomized ±50%).</summary>
    public const float PauseDuration = 2f;

    /// <summary>Collision radius in meters: used against Pebbles and other obstacles.</summary>
    public const float BodyRadius = 0.25f;

    /// <summary>Total body height in meters.</summary>
    public const float BodyHeight = 0.9f;

    /// <summary>How far from the terrain edge targets are kept, in meters.</summary>
    public const float EdgeMargin = 0.5f;

    public const int MaxHealth = 30;

    /// <summary>
    /// Cached Body Model: a single cylinder <see cref="Model"/> reused by
    /// every Bramblekin's <see cref="Draw"/> call via
    /// <see cref="Raylib.DrawModelEx"/>, instead of each unit calling
    /// <see cref="Raylib.DrawCylinder"/>/<see cref="Raylib.DrawCapsule"/>
    /// every frame — those immediate-mode calls regenerate their vertex
    /// geometry on the CPU on every single call, which is the real cost at
    /// hundreds of units; a cached <see cref="Model"/>'s mesh is built once
    /// and only re-uploaded to the GPU as a transform, not rebuilt. Lazily
    /// built on first use (not eagerly in a static initializer) so it can
    /// never run before <see cref="Raylib.InitWindow"/> has created a GPU
    /// context — building/uploading a Mesh before that would crash.
    /// </summary>
    private static Model _bodyModel;

    private static bool _bodyModelReady;

    /// <summary>
    /// Builds <see cref="_bodyModel"/> the first time any Bramblekin draws.
    /// A plain cylinder — this raylib-cs build has no GenMeshCapsule — sized
    /// to <see cref="BodyRadius"/>/<see cref="BodyHeight"/>.
    /// </summary>
    private static void EnsureBodyModel()
    {
        if (_bodyModelReady)
            return;

        Mesh mesh = Raylib.GenMeshCylinder(BodyRadius, BodyHeight, 8);
        _bodyModel = Raylib.LoadModelFromMesh(mesh);
        _bodyModelReady = true;
    }

    // --- Metabolism ----------------------------------------------------------------

    public const float MaxHunger = 100f;

    /// <summary>Hunger gained per second — a full belly lasts well under two minutes.</summary>
    public const float HungerPerSecond = 1f;

    /// <summary>At or above this, Hunger is Critical and overrides every other need.</summary>
    public const float HungryThreshold = 60f;

    /// <summary>At or above this, a highly Aggressive Bramblekin may rob whoever it runs into.</summary>
    public const float StarvingThreshold = 80f;

    /// <summary>Hunger removed by eating one piece of Food.</summary>
    private const float FoodNourishment = 40f;

    /// <summary>Health restored by eating one piece of Food — the only way to heal.</summary>
    private const int FoodHealing = 6;

    private const float EatDuration = 1.5f;

    /// <summary>At full Hunger, one point of damage every this many seconds until it eats or dies.</summary>
    private const float StarvationDamageInterval = 1f;

    /// <summary>New Bramblekin arrive with a random Hunger between 0 and this.</summary>
    private const float StartingHungerMax = 40f;

    // --- Senses --------------------------------------------------------------------

    /// <summary>Detection radius (m) at Intelligence 0.</summary>
    public const float BaseDetectionRadius = 5f;

    /// <summary>Extra detection radius (m) at Intelligence 1 — a genius sees 20m, a dullard 5m.</summary>
    public const float DetectionRadiusPerIntelligence = 15f;

    /// <summary>
    /// Seconds between perception scans (food, Grubs, threats). Scans are
    /// staggered per Bramblekin, so the whole colony never scans on the
    /// same frame.
    /// </summary>
    private const float PerceptionInterval = 0.25f;

    /// <summary>A threat or target is let go once it's this many detection radii away.</summary>
    private const float ThreatLeashMultiplier = 1.3f;

    /// <summary>Whoever last hit this Bramblekin stays its top threat for this many seconds.</summary>
    private const float RecentAttackWindow = 4f;

    // --- Combat --------------------------------------------------------------------

    /// <summary>Strike reach (m) beyond both bodies' edges.</summary>
    private const float StrikeReach = 0.35f;

    private const float StrikeCooldownDuration = 1f;

    /// <summary>Strike damage is this, plus up to <see cref="StrikeDamagePerAggression"/> more for a fully Aggressive Bramblekin.</summary>
    private const int BaseStrikeDamage = 5;

    private const float StrikeDamagePerAggression = 6f;

    /// <summary>Chasing speed (fights, robberies) as a multiple of <see cref="WalkSpeed"/>.</summary>
    private const float PursuitSpeedMultiplier = 1.3f;

    /// <summary>Below this fraction of <see cref="MaxHealth"/>, a fighter's nerve breaks and it flees instead.</summary>
    private const float FightBreakHealthFraction = 0.3f;

    /// <summary>Keeps running for at least this long after losing sight of whatever it fled from.</summary>
    private const float FleeMinDuration = 2.5f;

    /// <summary>Groupmates within this many meters embolden a fight-or-flight roll by <see cref="AllySupportBonus"/> each.</summary>
    private const float AllySupportRadius = 6f;

    private const float AllySupportBonus = 0.15f;

    /// <summary>Group Dynamics: added to the fight roll when the threat is attacking a groupmate.</summary>
    private const float GroupDefenseBonus = 0.5f;

    /// <summary>The Wolf Spider is scarier than a Hornet: subtracted from the fight roll.</summary>
    private const float SpiderFearPenalty = 0.25f;

    // --- Social --------------------------------------------------------------------

    /// <summary>How close (m) it must get to Food to pick it up.</summary>
    private const float PickupDistance = 0.5f;

    /// <summary>How far (m) a solitary Bramblekin or a Leader wanders per move; searching for food ranges twice as far.</summary>
    private const float WanderRadius = 12f;

    /// <summary>Leaders amble a little slower so their followers can keep up.</summary>
    private const float LeaderWanderSpeedMultiplier = 0.8f;

    /// <summary>A follower tries to stay within this many meters of its Leader.</summary>
    private const float FollowRadius = 3f;

    /// <summary>A follower that has fallen more than twice <see cref="FollowRadius"/> behind hurries at this multiple of <see cref="WalkSpeed"/>.</summary>
    private const float FollowCatchUpSpeedMultiplier = 1.4f;

    /// <summary>After each rest, odds of going to meet a stranger are Sociability times this.</summary>
    private const float SocialSeekFactor = 0.8f;

    /// <summary>A Bramblekin less Sociable than this walks away from anyone inside its <see cref="PersonalSpaceRadius"/>.</summary>
    private const float LonerThreshold = 0.35f;

    private const float PersonalSpaceRadius = 4f;

    /// <summary>A fed, empty-handed Bramblekin pockets visible Food within this fraction of its detection radius as a reserve.</summary>
    private const float ReserveGrabRadiusFraction = 0.5f;

    /// <summary>Gives up on reaching a stranger after this many seconds.</summary>
    private const float SocializeTimeout = 15f;

    private static readonly Color CalmColor = new(196, 160, 110, 255);       // Bark brown.
    private static readonly Color AggressiveColor = new(150, 60, 45, 255);   // Thorny red-brown, blended in by Aggression.
    private static readonly Color PanicColor = new(225, 85, 60, 255);        // Alarm red.
    private static readonly Color SolitaryHeadColor = new(235, 235, 225, 255);
    private static readonly Color ThornColor = new(120, 55, 40, 255);
    private static readonly Color BloodyThornColor = new(200, 30, 30, 255);
    private static readonly Color BannerPoleColor = new(120, 90, 50, 255);

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private readonly Dictionary<int, RelationshipState> _knownKins = new();

    private Vector3 _wanderTarget;
    private Vector3 _lastThreatPosition;

    /// <summary>Where it last saw Food — the first place it looks when hungry and nothing's in sight.</summary>
    private Vector3? _foodMemory;
    private float _pauseTimer;
    private float _eatTimer;
    private float _strikeCooldown;
    private float _starvationTimer;
    private float _perceptionTimer;
    private float _fleeTimer;
    private float _socializeTimer;
    private float _lastHitTime = float.NegativeInfinity;

    private FoodShard? _carried;
    private FoodShard? _claimedFood;
    private Bramblekin? _robTarget;
    private Bramblekin? _companion;
    private ICombatant? _lastAttacker;

    // Perception results, refreshed every PerceptionInterval.
    private FoodShard? _perceivedFood;
    private Grub? _perceivedGrub;
    private ICombatant? _perceivedThreat;
    private bool _threatIsAllyDefense;

    // Safety: the threat the current fight-or-flight roll was made against.
    private ICombatant? _respondingTo;
    private bool _fightDecision;

    public Bramblekin(Vector3 position, Random rng)
    {
        _rng = rng;
        Personality = Personality.Roll(rng);
        Hunger = (float)rng.NextDouble() * StartingHungerMax;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);
        _perceptionTimer = (float)rng.NextDouble() * PerceptionInterval;

        // Start mid-pause with a random timer so the colony doesn't move in lockstep.
        StartPause();
        _pauseTimer = (float)rng.NextDouble() * PauseDuration;
    }

    public Personality Personality { get; }

    /// <summary>The group this Bramblekin has joined, or null while solitary.</summary>
    public Guid? GroupId { get; private set; }

    /// <summary>Every Bramblekin it has met (by <see cref="ID"/>) and how it regards them.</summary>
    public IReadOnlyDictionary<int, RelationshipState> KnownKins => _knownKins;

    /// <summary>Terrain-aware: Y is snapped to World.GetHeightAt every read.</summary>
    public Vector3 Position => World.Grounded(_mover.Position);

    public BramblekinState State { get; private set; }

    public int Health { get; private set; } = MaxHealth;

    /// <summary>0 (full) to <see cref="MaxHunger"/> (starving to death).</summary>
    public float Hunger { get; private set; }

    public bool IsDead { get; private set; }

    /// <summary>Whatever it's currently fighting, robbing or hunting — other Bramblekin read this to tell who's attacking whom.</summary>
    public ICombatant? CombatTarget { get; private set; }

    public float CollisionRadius => BodyRadius;

    /// <summary>True while it's holding a piece of Food (a reserve, or a meal about to be eaten).</summary>
    public bool HasFood => _carried is not null;

    public bool IsHungry => Hunger >= HungryThreshold;

    public bool IsStarving => Hunger >= StarvingThreshold;

    public bool IsRobbing => _robTarget is not null;

    /// <summary>Intelligence-scaled radius (m) for spotting food, threats and other Bramblekin.</summary>
    public float DetectionRadius => BaseDetectionRadius + DetectionRadiusPerIntelligence * Personality.Intelligence;

    /// <summary>True if it can currently see a living Wolf Spider or Hornet — see <see cref="World.ResolveEncounter"/>'s Alliance rule.</summary>
    public bool IsThreatenedByPredator => _perceivedThreat is { IsDead: false } threat && threat is WolfSpider or Hornet;

    /// <summary>True if it can currently see loose Food it could take — a starving Bramblekin that can doesn't need to rob anyone.</summary>
    public bool SeesFood => _perceivedFood is { IsActive: true, IsCarried: false };

    /// <summary>The Wolf Spider hunts by vibration: a Bramblekin busy with food (or a fight over it) gives itself away.</summary>
    public bool IsVibrating => !IsDead && State is BramblekinState.Foraging or BramblekinState.Eating or BramblekinState.Hunting or BramblekinState.Attacking;

    private int StrikeDamage => BaseStrikeDamage + (int)MathF.Round(StrikeDamagePerAggression * Personality.Aggression);

    // --- Relationships & groups ----------------------------------------------------------

    /// <summary>How this Bramblekin regards <paramref name="other"/>, or null if they've never met.</summary>
    public RelationshipState? RelationshipTo(Bramblekin other) =>
        _knownKins.TryGetValue(other.ID, out RelationshipState state) ? state : null;

    /// <summary>Records how it regards <paramref name="other"/>. Enemy is permanent — nothing overwrites it.</summary>
    public void SetRelationship(Bramblekin other, RelationshipState state)
    {
        if (_knownKins.TryGetValue(other.ID, out RelationshipState current) && current == RelationshipState.Enemy)
            return;
        _knownKins[other.ID] = state;
    }

    /// <summary>Drops a dead Bramblekin from <see cref="KnownKins"/> — see <see cref="World.CommitPendingChanges"/>.</summary>
    public void ForgetKin(int id) => _knownKins.Remove(id);

    public void JoinGroup(Guid groupId) => GroupId = groupId;

    public void LeaveGroup() => GroupId = null;

    /// <summary>Hostility: commits to attacking <paramref name="victim"/> until its food is stolen, it gets away, or this Bramblekin eats.</summary>
    public void BeginRobbery(Bramblekin victim) => _robTarget = victim;

    /// <summary>Hands over whatever food it's holding (to a thief or a hungry friend), interrupting a meal in progress.</summary>
    public FoodShard? SurrenderFood()
    {
        FoodShard? food = _carried;
        _carried = null;
        if (State == BramblekinState.Eating)
            StartPause();
        return food;
    }

    /// <summary>Takes <paramref name="food"/> in hand (stolen or shared). Callers only hand food to an empty-handed Bramblekin.</summary>
    public void ReceiveFood(FoodShard food)
    {
        _carried = food;
        _perceivedFood = null;

        // A robbery ends the moment it pays off.
        _robTarget = null;
        if (CombatTarget is Bramblekin)
            CombatTarget = null;
    }

    // --- Damage & death --------------------------------------------------------------------

    /// <summary>A strike from another Bramblekin — see <see cref="ICombatant"/>.</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world) =>
        TakeDamage(damage, world, DeathCause.Kin, attacker);

    /// <summary>
    /// Reduces Health and, at 0, dies via <see cref="World.Kill"/>. Any hit
    /// with a <paramref name="source"/> makes that source its top threat for
    /// <see cref="RecentAttackWindow"/> seconds (and its groupmates' — see
    /// <see cref="Perceive"/>); a hit from another Bramblekin also makes the
    /// two Enemies for good.
    /// </summary>
    public void TakeDamage(int amount, World world, DeathCause cause, ICombatant? source)
    {
        if (IsDead)
            return;

        Health = Math.Max(0, Health - amount);
        if (source is not null)
        {
            _lastAttacker = source;
            _lastHitTime = world.ElapsedSeconds;
            _perceivedThreat = source;
            _threatIsAllyDefense = false;
        }
        if (source is Bramblekin attacker)
            world.DeclareEnemies(this, attacker);

        if (Health <= 0)
            world.Kill(this, cause, source);
    }

    /// <summary>
    /// Marks this Bramblekin dead: drops any food it was holding right where
    /// it fell (still edible) and releases its Food claim. Called once, from
    /// <see cref="World.Kill"/>; the removal from <see cref="World.Colony"/>
    /// is deferred to the end of the frame.
    /// </summary>
    public void MarkDead()
    {
        if (IsDead)
            return;

        if (_carried is not null)
        {
            World.DropFood(_carried, Position);
            _carried = null;
        }
        ReleaseFoodClaim();
        _robTarget = null;
        _companion = null;
        CombatTarget = null;
        IsDead = true;
    }

    /// <summary>Whoever hit this Bramblekin within the last <see cref="RecentAttackWindow"/> seconds, if it's still alive.</summary>
    public ICombatant? RecentAttacker(World world) =>
        _lastAttacker is { IsDead: false } attacker && world.ElapsedSeconds - _lastHitTime <= RecentAttackWindow ? attacker : null;

    // --- The survival loop --------------------------------------------------------------------

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return;

        _mover.Idle();
        _strikeCooldown = MathF.Max(0f, _strikeCooldown - deltaTime);

        // Metabolism: Hunger always rises; at the very top it starts costing Health.
        Hunger = MathF.Min(MaxHunger, Hunger + HungerPerSecond * deltaTime);
        if (Hunger >= MaxHunger)
        {
            _starvationTimer += deltaTime;
            if (_starvationTimer >= StarvationDamageInterval)
            {
                _starvationTimer -= StarvationDamageInterval;
                TakeDamage(1, world, DeathCause.Starvation, source: null);
                if (IsDead)
                    return;
            }
        }
        else
        {
            _starvationTimer = 0f;
        }

        _perceptionTimer -= deltaTime;
        if (_perceptionTimer <= 0f)
        {
            _perceptionTimer += PerceptionInterval;
            Perceive(world);
        }

        // 1) Critical: Hunger. A meal already under way is always finished.
        if (IsHungry || State == BramblekinState.Eating)
        {
            _fleeTimer = 0f; // Whatever it was running from, food comes first now.
            UpdateHunger(deltaTime, world);
            return;
        }
        _robTarget = null; // Fed again: no reason left to rob anyone.

        // 2) Safety.
        if (UpdateSafety(deltaTime, world))
            return;

        // 3) Social.
        UpdateSocial(deltaTime, world);
    }

    /// <summary>
    /// Perception, scaled by Intelligence: the nearest available Food and
    /// Grub within <see cref="DetectionRadius"/>, and the most pressing
    /// threat — whoever just hit it, else the nearest of: the Wolf Spider,
    /// any Hornet, any Bramblekin attacking it, or (Group Dynamics) whatever
    /// is attacking or fighting one of its groupmates.
    /// </summary>
    private void Perceive(World world)
    {
        float radius = DetectionRadius;
        _perceivedFood = world.NearestAvailableFood(Position, radius, this);
        if (_perceivedFood is not null)
            _foodMemory = _perceivedFood.Position;
        _perceivedGrub = world.NearestLiveGrub(Position, radius);

        float leash = radius * ThreatLeashMultiplier;
        if (RecentAttacker(world) is { } attacker &&
            GroundMover.HorizontalDistanceSquared(Position, attacker.Position) <= leash * leash)
        {
            _perceivedThreat = attacker;
            _threatIsAllyDefense = false;
            return;
        }

        ICombatant? best = null;
        bool bestIsAllyDefense = false;
        float bestDistanceSquared = radius * radius;

        void Consider(ICombatant candidate, bool allyDefense)
        {
            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, candidate.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = candidate;
                bestIsAllyDefense = allyDefense;
                bestDistanceSquared = distanceSquared;
            }
        }

        if (world.Spider is { IsDead: false } spider)
            Consider(spider, allyDefense: false);
        foreach (Hornet hornet in world.Hornets)
        {
            if (!hornet.IsDead)
                Consider(hornet, allyDefense: false);
        }

        List<Bramblekin> nearby = world.QueryColonyWithin(Position, radius);
        for (int i = 0; i < nearby.Count; i++)
        {
            Bramblekin other = nearby[i];
            if (other == this || other.IsDead)
                continue;

            if (ReferenceEquals(other.CombatTarget, this))
            {
                Consider(other, allyDefense: false);
                continue;
            }

            if (GroupId is null || other.GroupId != GroupId)
                continue;

            // Group Dynamics: a groupmate under attack, or already
            // fighting, pulls its foe into this Bramblekin's sights too.
            ICombatant? allyFoe = other.RecentAttacker(world) ??
                                  (other.State == BramblekinState.Fighting ? other.CombatTarget : null);
            if (allyFoe is { IsDead: false } && !ReferenceEquals(allyFoe, this) &&
                !(allyFoe is Bramblekin foeKin && foeKin.GroupId == GroupId))
                Consider(allyFoe, allyDefense: true);
        }

        _perceivedThreat = best;
        _threatIsAllyDefense = bestIsAllyDefense;
    }

    /// <summary>
    /// Critical need: eat what it's holding; else rob the neighbour it
    /// committed to (see <see cref="BeginRobbery"/>); else forage the nearest
    /// visible Food; else hunt a visible Grub; else — a follower borrows its
    /// Leader's sharper senses, or tags along if the Leader is searching too
    /// — else it searches further afield.
    /// </summary>
    private void UpdateHunger(float deltaTime, World world)
    {
        if (State == BramblekinState.Eating)
        {
            _eatTimer -= deltaTime;
            if (_eatTimer <= 0f)
                FinishEating(world);
            return;
        }

        if (_carried is not null)
        {
            StartEating();
            return;
        }

        if (_robTarget is not null)
        {
            if (IsRobberyStillWorthIt())
            {
                SetState(BramblekinState.Attacking);
                CombatTarget = _robTarget;
                PursueAndStrike(_robTarget, WalkSpeed * PursuitSpeedMultiplier, deltaTime, world);
                return;
            }
            _robTarget = null;
        }

        if (ValidPerceivedFood(world) is { } food)
        {
            ApproachFood(food, WalkSpeed * (IsStarving ? 1.25f : 1f), deltaTime, world, eatOnArrival: true);
            return;
        }

        if (_perceivedGrub is { IsDead: false } grub)
        {
            SetState(BramblekinState.Hunting);
            CombatTarget = grub;
            PursueAndStrike(grub, WalkSpeed * 1.2f, deltaTime, world);
            return;
        }

        // Desperation: a starving, highly Aggressive Bramblekin with nothing
        // else in sight stalks the nearest outsider it can see carrying
        // food, to cross paths with it — whether it then attacks is decided
        // by the encounter (see World.ResolveEncounter).
        if (IsStarving && Personality.Aggression >= World.HighAggressionThreshold && NearestFoodCarrier(world) is { } mark)
        {
            SetState(BramblekinState.Searching);
            MoveTo(mark.Position, WalkSpeed * 1.1f, deltaTime, world);
            return;
        }

        // Group Dynamics: a follower that can't see food itself borrows its
        // Leader's sharper senses, and sticks with a Leader that's out
        // searching anyway — but never idles beside a well-fed one while it
        // starves.
        if (world.GroupOf(this)?.Leader is { IsDead: false } leader && leader != this)
        {
            if (leader.FoodSightingFor(this, world) is { } pointedOut)
            {
                _perceivedFood = pointedOut;
                ApproachFood(pointedOut, WalkSpeed * (IsStarving ? 1.25f : 1f), deltaTime, world, eatOnArrival: true);
                return;
            }

            if (leader.State == BramblekinState.Searching)
            {
                FollowLeader(leader, deltaTime, world);
                return;
            }
        }

        Explore(deltaTime, world);
    }

    /// <summary>The Food this Bramblekin can currently see, if <paramref name="groupmate"/> could take it — how a Leader points food out to a hungry follower.</summary>
    public FoodShard? FoodSightingFor(Bramblekin groupmate, World world) =>
        _perceivedFood is { } food && world.IsAvailable(food, groupmate) ? food : null;

    /// <summary>
    /// Safety: responds to the perceived threat, rolling fight-or-flight
    /// once per new threat — Aggression, plus courage from nearby
    /// groupmates, plus a big bonus when defending one, minus fear of the
    /// Wolf Spider. A fighter whose Health drops below
    /// <see cref="FightBreakHealthFraction"/> breaks and flees. Returns false
    /// when there's nothing to fear.
    /// </summary>
    private bool UpdateSafety(float deltaTime, World world)
    {
        ICombatant? threat = _perceivedThreat;
        if (threat is not null)
        {
            float leash = DetectionRadius * ThreatLeashMultiplier;
            if (threat.IsDead || GroundMover.HorizontalDistanceSquared(Position, threat.Position) > leash * leash)
                threat = null;
        }

        if (threat is null)
        {
            _respondingTo = null;
            if (_fleeTimer > 0f)
            {
                // Keep running for a moment after losing sight of it.
                _fleeTimer -= deltaTime;
                SetState(BramblekinState.Fleeing);
                FleeFrom(_lastThreatPosition, deltaTime, world);
                return true;
            }
            return false;
        }

        if (!ReferenceEquals(threat, _respondingTo))
        {
            _respondingTo = threat;
            _fightDecision = RollFightOrFlight(threat, world);
        }
        if (_fightDecision && Health <= MaxHealth * FightBreakHealthFraction)
            _fightDecision = false; // Nerve breaks.

        _lastThreatPosition = threat.Position;
        if (_fightDecision)
        {
            _fleeTimer = 0f;
            SetState(BramblekinState.Fighting);
            CombatTarget = threat;
            PursueAndStrike(threat, WalkSpeed * PursuitSpeedMultiplier, deltaTime, world);
        }
        else
        {
            SetState(BramblekinState.Fleeing);
            _fleeTimer = FleeMinDuration;
            FleeFrom(threat.Position, deltaTime, world);
        }
        return true;
    }

    /// <summary>The Aggression check: true to fight <paramref name="threat"/>, false to flee it.</summary>
    private bool RollFightOrFlight(ICombatant threat, World world)
    {
        if (Health <= MaxHealth * FightBreakHealthFraction)
            return false;

        float chance = Personality.Aggression;
        if (world.GroupOf(this) is { } group)
        {
            foreach (Bramblekin member in group.Members)
            {
                if (member != this && !member.IsDead &&
                    GroundMover.HorizontalDistanceSquared(Position, member.Position) <= AllySupportRadius * AllySupportRadius)
                    chance += AllySupportBonus;
            }
        }
        if (_threatIsAllyDefense)
            chance += GroupDefenseBonus;
        if (threat is WolfSpider)
            chance -= SpiderFearPenalty;

        return _rng.NextDouble() < chance;
    }

    /// <summary>
    /// Social need (fed and safe): pocket a spare piece of Food if one is
    /// close and its hands are empty; a follower stays near its Leader;
    /// anyone else alternates short rests with a move chosen by
    /// <see cref="ChooseSocialAction"/>.
    /// </summary>
    private void UpdateSocial(float deltaTime, World world)
    {
        if (_carried is null && ValidPerceivedFood(world) is { } food &&
            GroundMover.HorizontalDistance(Position, food.Position) <= DetectionRadius * ReserveGrabRadiusFraction)
        {
            ApproachFood(food, WalkSpeed, deltaTime, world, eatOnArrival: false);
            return;
        }

        Bramblekin? leader = world.GroupOf(this)?.Leader;
        if (leader is { IsDead: false } && leader != this)
        {
            FollowLeader(leader, deltaTime, world);
            return;
        }

        switch (State)
        {
            case BramblekinState.Socializing:
                UpdateSocializing(deltaTime, world);
                return;

            case BramblekinState.Wandering:
                float speed = leader == this ? WalkSpeed * LeaderWanderSpeedMultiplier : WalkSpeed;
                if (MoveTo(_wanderTarget, speed, deltaTime, world))
                    StartPause();
                return;

            case BramblekinState.Idle:
                _pauseTimer -= deltaTime;
                if (_pauseTimer <= 0f)
                    ChooseSocialAction(world);
                return;

            default:
                // Coming out of foraging, fleeing or a fight: catch its breath first.
                StartPause();
                return;
        }
    }

    /// <summary>
    /// After each rest: with odds of Sociability × <see cref="SocialSeekFactor"/>
    /// it goes to meet the nearest stranger it can see; a loner (below
    /// <see cref="LonerThreshold"/>) walks away from anyone crowding it;
    /// otherwise it simply wanders.
    /// </summary>
    private void ChooseSocialAction(World world)
    {
        if (_rng.NextDouble() < Personality.Sociability * SocialSeekFactor && NearestStranger(world) is { } stranger)
        {
            _companion = stranger;
            _socializeTimer = SocializeTimeout;
            SetState(BramblekinState.Socializing);
            return;
        }

        if (Personality.Sociability < LonerThreshold && NearestOutsiderWithin(world, PersonalSpaceRadius) is { } crowder)
        {
            _wanderTarget = PointAwayFrom(crowder.Position, WanderRadius * 0.5f, world);
            SetState(BramblekinState.Wandering);
            return;
        }

        _wanderTarget = RandomWanderPoint(world, WanderRadius);
        SetState(BramblekinState.Wandering);
    }

    /// <summary>Walks up to the stranger it spotted; the World resolves the encounter once they're close.</summary>
    private void UpdateSocializing(float deltaTime, World world)
    {
        _socializeTimer -= deltaTime;
        if (_companion is not { IsDead: false } companion || _socializeTimer <= 0f || _knownKins.ContainsKey(companion.ID))
        {
            StartPause(); // Met them (or gave up).
            return;
        }

        float distance = GroundMover.HorizontalDistance(Position, companion.Position);
        if (distance > DetectionRadius * ThreatLeashMultiplier || distance <= World.EncounterRadius * 0.8f)
        {
            StartPause();
            return;
        }

        MoveTo(companion.Position, WalkSpeed, deltaTime, world);
    }

    /// <summary>Group Dynamics: a follower overrides its own wandering to stay within <see cref="FollowRadius"/> of its Leader, milling about near it once there.</summary>
    private void FollowLeader(Bramblekin leader, float deltaTime, World world)
    {
        float distance = GroundMover.HorizontalDistance(Position, leader.Position);
        if (distance > FollowRadius)
        {
            SetState(BramblekinState.Following);
            float speed = WalkSpeed * (distance > FollowRadius * 2f ? FollowCatchUpSpeedMultiplier : 1.1f);
            MoveTo(leader.Position, speed, deltaTime, world);
            return;
        }

        switch (State)
        {
            case BramblekinState.Idle:
                _pauseTimer -= deltaTime;
                if (_pauseTimer <= 0f)
                {
                    _wanderTarget = PointNear(leader.Position, FollowRadius * 0.7f, world);
                    SetState(BramblekinState.Wandering);
                }
                return;

            case BramblekinState.Wandering:
                if (MoveTo(_wanderTarget, WalkSpeed * 0.8f, deltaTime, world))
                    StartPause();
                return;

            default:
                StartPause();
                return;
        }
    }

    /// <summary>
    /// Hungry with nothing in sight: heads back to where it last saw Food
    /// (Berries keep growing in the same patches), then keeps striking out
    /// toward random points twice as far as a normal wander.
    /// </summary>
    private void Explore(float deltaTime, World world)
    {
        if (State != BramblekinState.Searching)
        {
            _wanderTarget = _foodMemory ?? RandomWanderPoint(world, WanderRadius * 2f);
            SetState(BramblekinState.Searching);
        }

        if (MoveTo(_wanderTarget, WalkSpeed, deltaTime, world))
        {
            _foodMemory = null; // Been there, nothing left.
            _wanderTarget = RandomWanderPoint(world, WanderRadius * 2f);
        }
    }

    // --- Food ---------------------------------------------------------------------------

    /// <summary>The Food perception last spotted, if it's still there for the taking (else forces a fresh scan next frame).</summary>
    private FoodShard? ValidPerceivedFood(World world)
    {
        if (_perceivedFood is { } food && world.IsAvailable(food, this))
            return food;

        if (_perceivedFood is not null)
        {
            _perceivedFood = null;
            _perceptionTimer = 0f; // Someone got there first: look again right away.
        }
        return null;
    }

    /// <summary>Claims <paramref name="food"/>, walks to it and picks it up — then eats it straight away, or keeps it as a reserve.</summary>
    private void ApproachFood(FoodShard food, float speed, float deltaTime, World world, bool eatOnArrival)
    {
        ClaimFood(food);
        SetState(BramblekinState.Foraging);

        if (GroundMover.HorizontalDistance(Position, food.Position) <= PickupDistance)
        {
            ReleaseFoodClaim();
            World.PickUpFood(food);
            _carried = food;
            _perceivedFood = null;
            if (eatOnArrival)
                StartEating();
            else
                StartPause();
            return;
        }

        MoveTo(food.Position, speed, deltaTime, world);
    }

    private void StartEating()
    {
        SetState(BramblekinState.Eating);
        _eatTimer = EatDuration;
    }

    private void FinishEating(World world)
    {
        if (_carried is { } food)
        {
            world.ConsumeFood(food);
            _carried = null;
            Hunger = MathF.Max(0f, Hunger - FoodNourishment);
            Health = Math.Min(MaxHealth, Health + FoodHealing);
        }
        _robTarget = null;
        StartPause();
    }

    /// <summary>Dibs: marks <paramref name="food"/> as this Bramblekin's, releasing any previous claim.</summary>
    private void ClaimFood(FoodShard food)
    {
        if (_claimedFood == food)
            return;

        ReleaseFoodClaim();
        food.ClaimedBy = this;
        food.ClaimTimer = 0f;
        _claimedFood = food;
    }

    /// <summary>Dibs: releases this Bramblekin's claim on its Food target, if it still holds one.</summary>
    private void ReleaseFoodClaim()
    {
        if (_claimedFood is not null && _claimedFood.ClaimedBy == this)
            _claimedFood.ClaimedBy = null;
        _claimedFood = null;
    }

    // --- Combat ---------------------------------------------------------------------------

    /// <summary>A robbery is dropped once the victim is dead, empty-handed, joined the robber's group, or got away — or once the robber's own nerve breaks.</summary>
    private bool IsRobberyStillWorthIt()
    {
        if (_robTarget is not { IsDead: false } victim || !victim.HasFood || HasFood)
            return false;
        if (Health <= MaxHealth * FightBreakHealthFraction)
            return false;
        if (GroupId is not null && victim.GroupId == GroupId)
            return false;

        float leash = DetectionRadius * ThreatLeashMultiplier;
        return GroundMover.HorizontalDistanceSquared(Position, victim.Position) <= leash * leash;
    }

    /// <summary>
    /// Closes to strike range of <paramref name="target"/> and strikes on
    /// cooldown. When robbing, the first blow that lands takes the victim's
    /// food (see <see cref="World.StealFood"/>).
    /// </summary>
    private void PursueAndStrike(ICombatant target, float speed, float deltaTime, World world)
    {
        float reach = BodyRadius + target.CollisionRadius + StrikeReach;
        Vector3 targetPosition = target.Position;
        if (GroundMover.HorizontalDistanceSquared(Position, targetPosition) > reach * reach)
        {
            MoveTo(targetPosition, speed, deltaTime, world);
            return;
        }

        var toTarget = new Vector2(targetPosition.X - Position.X, targetPosition.Z - Position.Z);
        if (toTarget.LengthSquared() > 1e-6f)
            _mover.Heading = Vector2.Normalize(toTarget);

        if (_strikeCooldown > 0f)
            return;
        _strikeCooldown = StrikeCooldownDuration;

        if (State == BramblekinState.Attacking && target is Bramblekin victim && victim.HasFood)
            world.StealFood(this, victim);
        target.TakeHit(StrikeDamage, this, world);
    }

    /// <summary>Runs directly away from <paramref name="threatPosition"/>, turning along the map's edge rather than into it.</summary>
    private void FleeFrom(Vector3 threatPosition, float deltaTime, World world)
    {
        var away = new Vector2(Position.X - threatPosition.X, Position.Z - threatPosition.Z);
        away = away.LengthSquared() > 1e-4f ? Vector2.Normalize(away) : _mover.Heading;
        var left = new Vector2(-away.Y, away.X);

        Vector3 best = Position;
        float bestDistanceSquared = -1f;
        foreach (Vector2 direction in stackalloc[] { away, Vector2.Normalize(away + left), Vector2.Normalize(away - left), left, -left })
        {
            Vector3 candidate = Position + new Vector3(direction.X, 0f, direction.Y) * 4f;
            if (!world.Terrain.Contains(candidate, EdgeMargin + 1f))
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(candidate, threatPosition);
            if (distanceSquared > bestDistanceSquared)
            {
                best = candidate;
                bestDistanceSquared = distanceSquared;
            }
        }

        MoveTo(best, WalkSpeed * FleeSpeedMultiplier, deltaTime, world);
    }

    // --- Movement helpers ----------------------------------------------------------------------

    /// <summary>Walks toward <paramref name="target"/>, steering round Pebbles. Returns true on arrival.</summary>
    private bool MoveTo(Vector3 target, float speed, float deltaTime, World world) =>
        _mover.MoveTowards(target, speed, deltaTime, world, p => !world.IsBlocked(p, BodyRadius));

    private void StartPause()
    {
        SetState(BramblekinState.Idle);
        _pauseTimer = PauseDuration * (0.5f + (float)_rng.NextDouble());
    }

    private void SetState(BramblekinState state)
    {
        if (State == state)
            return;

        if (State == BramblekinState.Foraging)
            ReleaseFoodClaim();
        if (State == BramblekinState.Socializing)
            _companion = null;

        State = state;
        if (state is not (BramblekinState.Fighting or BramblekinState.Attacking or BramblekinState.Hunting))
            CombatTarget = null;
        _mover.ResetProgress();
    }

    /// <summary>A random reachable point within <paramref name="radius"/> of where it stands (anywhere on the map as a fallback).</summary>
    private Vector3 RandomWanderPoint(World world, float radius)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float angle = (float)(_rng.NextDouble() * MathF.Tau);
            float distance = MathF.Sqrt((float)_rng.NextDouble()) * radius;
            Vector3 candidate = Position + new Vector3(MathF.Cos(angle) * distance, 0f, MathF.Sin(angle) * distance);
            if (world.Terrain.Contains(candidate, EdgeMargin + 1f) && !world.IsBlocked(candidate, BodyRadius))
                return candidate;
        }
        return world.RandomFreePoint(BodyRadius, EdgeMargin + 1f);
    }

    /// <summary>A point <paramref name="distance"/> meters directly away from <paramref name="from"/>, or a random wander point if that's off the map.</summary>
    private Vector3 PointAwayFrom(Vector3 from, float distance, World world)
    {
        var away = new Vector2(Position.X - from.X, Position.Z - from.Z);
        if (away.LengthSquared() > 1e-4f)
        {
            away = Vector2.Normalize(away);
            Vector3 candidate = Position + new Vector3(away.X, 0f, away.Y) * distance;
            if (world.Terrain.Contains(candidate, EdgeMargin + 1f) && !world.IsBlocked(candidate, BodyRadius))
                return candidate;
        }
        return RandomWanderPoint(world, distance);
    }

    /// <summary>A random reachable point within <paramref name="radius"/> of <paramref name="center"/>, or <paramref name="center"/> itself.</summary>
    private Vector3 PointNear(Vector3 center, float radius, World world)
    {
        float angle = (float)(_rng.NextDouble() * MathF.Tau);
        float distance = (float)_rng.NextDouble() * radius;
        Vector3 candidate = center + new Vector3(MathF.Cos(angle) * distance, 0f, MathF.Sin(angle) * distance);
        return world.Terrain.Contains(candidate, EdgeMargin + 1f) && !world.IsBlocked(candidate, BodyRadius) ? candidate : center;
    }

    /// <summary>The nearest living Bramblekin it can see that it has never met.</summary>
    private Bramblekin? NearestStranger(World world)
    {
        Bramblekin? best = null;
        float radius = DetectionRadius;
        float bestDistanceSquared = radius * radius;
        List<Bramblekin> nearby = world.QueryColonyWithin(Position, radius);
        for (int i = 0; i < nearby.Count; i++)
        {
            Bramblekin other = nearby[i];
            if (other == this || other.IsDead || _knownKins.ContainsKey(other.ID))
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, other.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = other;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>The nearest living Bramblekin it can see, outside its own group and not a Friend, that's carrying food.</summary>
    private Bramblekin? NearestFoodCarrier(World world)
    {
        Bramblekin? best = null;
        float radius = DetectionRadius;
        float bestDistanceSquared = radius * radius;
        List<Bramblekin> nearby = world.QueryColonyWithin(Position, radius);
        for (int i = 0; i < nearby.Count; i++)
        {
            Bramblekin other = nearby[i];
            if (other == this || other.IsDead || !other.HasFood ||
                (GroupId is not null && other.GroupId == GroupId) || RelationshipTo(other) == RelationshipState.Friend)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, other.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = other;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>The nearest living Bramblekin outside its own group within <paramref name="radius"/>.</summary>
    private Bramblekin? NearestOutsiderWithin(World world, float radius)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = radius * radius;
        List<Bramblekin> nearby = world.QueryColonyWithin(Position, radius);
        for (int i = 0; i < nearby.Count; i++)
        {
            Bramblekin other = nearby[i];
            if (other == this || other.IsDead || (GroupId is not null && other.GroupId == GroupId))
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, other.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = other;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    // --- Drawing ---------------------------------------------------------------------------

    /// <summary>
    /// A bark-brown body tinted redder the more Aggressive it is (alarm red
    /// while fleeing), topped with a head in its group's colour (off-white
    /// while solitary). A Leader carries its group's banner; anything
    /// fighting, robbing or hunting holds a thorn out front; carried Food
    /// rides on its head.
    /// </summary>
    public void Draw(World world)
    {
        KinGroup? group = world.GroupOf(this);
        Color color = State == BramblekinState.Fleeing
            ? PanicColor
            : LerpColor(CalmColor, AggressiveColor, Personality.Aggression);

        // A small, dark, semi-transparent drop shadow at this unit's own X/Z
        // on the ground, drawn before the body itself — a flat disc laid on
        // the XZ plane at a tiny epsilon above the terrain to avoid
        // z-fighting with it.
        var shadowCenter = new Vector3(Position.X, Position.Y + 0.02f, Position.Z);
        Raylib.DrawCircle3D(shadowCenter, BodyRadius * 1.3f, new Vector3(1, 0, 0), 90f, new Color(0, 0, 0, 90));

        // Cached-Model body: a cylinder tilted to the terrain's own surface
        // normal. GenMeshCylinder's mesh runs from local y=0 (base) to
        // y=BodyHeight (top), so it pivots flush on the ground at Position.
        EnsureBodyModel();
        Vector3 normal = World.GetNormalAt(Position.X, Position.Z);
        Vector3 axis = Vector3.Cross(Vector3.UnitY, normal);
        float angleDegrees = 0f;
        if (axis.LengthSquared() > 1e-6f)
            angleDegrees = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitY, normal), -1f, 1f)) * (180f / MathF.PI);
        else
            axis = Vector3.UnitY; // Flat ground: any axis is fine at a 0-degree rotation.
        Raylib.DrawModelEx(_bodyModel, Position, axis, angleDegrees, Vector3.One, color);

        var top = Position + new Vector3(0, BodyHeight - BodyRadius, 0);
        Raylib.DrawSphere(top + new Vector3(0, BodyRadius * 0.5f, 0), BodyRadius * 0.35f, group?.Color ?? SolitaryHeadColor);

        Vector2 facing = _mover.Heading.LengthSquared() > 1e-6f ? _mover.Heading : Vector2.UnitX;

        if (group is not null && group.Leader == this)
        {
            var poleBase = Position + new Vector3(0, BodyHeight, 0);
            var poleTop = poleBase + new Vector3(0, 0.4f, 0);
            Raylib.DrawLine3D(poleBase, poleTop, BannerPoleColor);
            var flagCenter = poleTop + new Vector3(-facing.X * 0.12f, -0.07f, -facing.Y * 0.12f);
            Raylib.DrawCube(flagCenter, 0.2f, 0.14f, 0.02f, group.Color);
        }

        if (State is BramblekinState.Fighting or BramblekinState.Attacking or BramblekinState.Hunting)
        {
            Color thornColor = State == BramblekinState.Attacking ? BloodyThornColor : ThornColor;
            var grip = Position + new Vector3(0, BodyHeight * 0.6f, 0);
            var tip = grip + new Vector3(facing.X, 0.55f, facing.Y) * 0.6f;
            Raylib.DrawLine3D(grip, tip, thornColor);
            Raylib.DrawSphere(tip, 0.025f, thornColor);
        }

        _carried?.Draw(Position + new Vector3(0, BodyHeight, 0));
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            (byte)255);
    }
}

// =============================================================================
//  Predators: the Wolf Spider
// =============================================================================

/// <summary>What the Wolf Spider is currently doing.</summary>
public enum SpiderState
{
    /// <summary>Default: ambling slowly between random points.</summary>
    Prowling,

    /// <summary>Stalking a Bramblekin whose footsteps it can feel.</summary>
    Hunting,

    /// <summary>Committed dash at its target; kills any Bramblekin it touches.</summary>
    Pouncing,

    /// <summary>Getting its legs back under it after a pounce.</summary>
    Recovering,

    /// <summary>Dormant: no longer reachable now that the player's pebble-impact distraction has been removed.</summary>
    Investigating,

    /// <summary>Eating a catch: stays put and ignores everything for a while.</summary>
    Feeding,

    /// <summary>Dormant: no longer reachable now that the player's Gust has been removed.</summary>
    Tumbled,
}

/// <summary>
/// The first predator (Garden_Guardians_Design.md, "The Wolf Spider"). It is
/// blind in this prototype and hunts purely by vibration:
///
///   Prowling --feels a busy worker within VibrationRadius--> Hunting
///   Hunting --within PounceRange--> Pouncing (dash; touching = kill)
///   Pouncing --dash over--> Recovering (1 s) --> Hunting or Prowling
///   Pouncing --caught one--> Feeding (20 s, ignores everything) --> Prowling
///
/// Feeding caps how fast it can kill: without it every victim's dropped
/// food lures the next forager in, and the colony dies in a chain.
///
/// Its only counter is the Bramblekin themselves: any Bramblekin whose
/// fight-or-flight roll comes up "fight" (far likelier in a group, see
/// Bramblekin.RollFightOrFlight) closes in and strikes it. A Bramblekin
/// that is Fighting is never caught by a pounce; instead the spider Bites
/// the nearest fighter in range on its own cooldown. Health reaching 0,
/// from either side, is death — and a slain spider leaves a pile of Food
/// behind (see World.DamageSpider).
/// </summary>
public sealed class WolfSpider : ICombatant
{
    /// <summary>Collision radius (m) — twice a Bramblekin's.</summary>
    public const float BodyRadius = Bramblekin.BodyRadius * 2f;

    /// <summary>How far (m) it can feel a gathering/returning Bramblekin's footsteps.</summary>
    public const float VibrationRadius = 7f;

    /// <summary>Dormant: was how far (m) it could feel a pebble slam into the ground, back when the player had a pebble to drop.</summary>
    public const float ImpactHearingRadius = 12f;

    /// <summary>Distance (m) at which a hunting spider launches its pounce.</summary>
    public const float PounceRange = 2.5f;

    private const float ProwlSpeed = 0.6f;
    private const float HuntSpeed = 1.5f;      // Faster than a walking Bramblekin, slower than a fleeing one.
    private const float PounceSpeed = 7f;
    private const float PounceDuration = 0.45f;
    private const float RecoverDuration = 1f;
    private const float ProwlPauseDuration = 1.5f;
    private const float StareDuration = 3f;
    private const float FeedDuration = 20f;

    /// <summary>Dormant: was how long a Gust-tumbled spider was stunned for, in seconds, back when the player had a Gust to cast.</summary>
    public const float TumbledDuration = 4f;

    /// <summary>Hit points out of <see cref="MaxHealth"/>.</summary>
    public const int MaxHealth = 50;

    /// <summary>Sustained Combat: how close a fighting Bramblekin must be for the spider to Bite it.</summary>
    private const float BiteRange = 1.5f;

    /// <summary>Bite damage dealt to the nearest fighting Bramblekin in range.</summary>
    private const int BiteDamage = 10;

    /// <summary>Cooldown (s) between Bites.</summary>
    private const float BiteCooldownDuration = 1.5f;

    /// <summary>
    /// Seconds a hunt continues after the target stops vibrating (e.g. it
    /// panicked and dropped its food) before the spider gives up on it.
    /// </summary>
    private const float ChaseMemory = 2f;

    /// <summary>How close (m) to an impact point it goes before staring.</summary>
    private const float InvestigateStandOff = 1.2f;

    /// <summary>Gives up walking to an impact after this long (s) and just stares from where it is.</summary>
    private const float InvestigateTravelTimeout = 8f;

    private static readonly Color BodyColor = new(45, 42, 40, 255);
    private static readonly Color LegColor = new(30, 28, 26, 255);

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private Vector3 _target;               // Prowl point, or impact point while investigating.
    private Bramblekin? _prey;
    private float _timer;                  // Pause / dash / recover / stare / travel timer, by state.
    private float _sinceVibration;         // Seconds since the prey last vibrated.
    private Vector2 _pounceDirection;
    private bool _staring;
    private float _walkCycle;              // Leg animation phase.
    private float _biteCooldown;

    /// <summary>Terrain-aware, same treatment as Bramblekin — Y is snapped to World.GetHeightAt every read.</summary>
    public Vector3 Position => World.Grounded(_mover.Position);

    public SpiderState State { get; private set; } = SpiderState.Prowling;

    /// <summary>Bramblekin killed so far.</summary>
    public int Kills { get; private set; }

    /// <summary>Hit points out of <see cref="MaxHealth"/>.</summary>
    public int Health { get; private set; } = MaxHealth;

    /// <summary>True once slain — see <see cref="World.DamageSpider"/>. Bramblekin still holding a reference to it (as a threat or a fight target) check this.</summary>
    public bool IsDead { get; private set; }

    public float CollisionRadius => BodyRadius;

    public WolfSpider(Vector3 position, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, edgeMargin: 1f, rng);
        _target = position;
        _timer = ProwlPauseDuration;
    }

    /// <summary>
    /// Sustained Combat: a Bramblekin strike's damage. Purely a Health
    /// mutation — never touches State — so it can never wake a Tumbled
    /// spider early (see the hard lock at the top of Update()). Death itself
    /// (Health reaching 0) is World's call, not this method's: see
    /// World.DamageSpider.
    /// </summary>
    public void TakeDamage(int amount) => Health = Math.Max(0, Health - amount);

    /// <summary>A Bramblekin's strike — routed through World so a killing blow is handled in one place.</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world) => world.DamageSpider(damage, attacker);

    /// <summary>Called once, by World.DamageSpider, when Health reaches 0.</summary>
    public void MarkDead() => IsDead = true;

    public void Update(float deltaTime, World world)
    {
        _mover.Idle();

        // Tumbled is a dormant hard lock (nothing triggers it any more, now
        // that the player's Gust is gone), checked and handled before
        // anything else in this method — the prey safety net, the Bite
        // retaliation below, every bit of vision/AI. Nothing can
        // re-target, re-notice, retaliate or otherwise step on the stun
        // early — not even taking strike damage (TakeDamage is a pure Health
        // mutation that never touches State); the only way out is the timer
        // counting all the way down to zero on its own. A dedicated early
        // return makes that structurally impossible to short-circuit,
        // rather than relying on every future addition to remember to check
        // for it.
        if (State == SpiderState.Tumbled)
        {
            _timer -= deltaTime;
            if (_timer <= 0f)
                StartProwling();
            return;
        }

        // Sustained Combat: while awake, retaliate against the nearest
        // fighting Bramblekin in range on its own cooldown, regardless of
        // what else it's otherwise doing (prowling, hunting, even
        // mid-pounce) — a reflex, not a deliberate target choice the way
        // Hunt/Pounce are.
        _biteCooldown = MathF.Max(0f, _biteCooldown - deltaTime);
        if (_biteCooldown <= 0f && NearestFighterInRange(world, BiteRange) is { } target)
        {
            target.TakeDamage(BiteDamage, world, DeathCause.Predator, this);
            _biteCooldown = BiteCooldownDuration;
        }

        // Safety net: if the Bramblekin we're tracking died or vanished by
        // any means since last frame, drop the reference immediately rather
        // than move toward or read a dead target. Only forces the state back
        // to Prowling out of an active Hunt — a Pounce already in flight
        // doesn't use _prey for its hit test, so it's left to finish (and,
        // on a kill, sets Feeding itself).
        if (_prey is not null && (_prey.IsDead || !world.Colony.Contains(_prey)))
        {
            _prey = null;
            if (State == SpiderState.Hunting)
                StartProwling();
        }

        switch (State)
        {
            case SpiderState.Prowling:
                // Busy workers give themselves away.
                if (FindPrey(world) is { } prey)
                {
                    StartHunting(prey);
                    break;
                }
                Prowl(deltaTime, world);
                break;

            case SpiderState.Hunting:
                Hunt(deltaTime, world);
                break;

            case SpiderState.Pouncing:
                Pounce(deltaTime, world);
                break;

            case SpiderState.Recovering:
                _timer -= deltaTime;
                if (_timer <= 0f)
                {
                    if (FindPrey(world) is { } next)
                        StartHunting(next);
                    else
                        StartProwling();
                }
                break;

            case SpiderState.Investigating:
                Investigate(deltaTime, world);
                break;

            case SpiderState.Feeding:
                _timer -= deltaTime;
                if (_timer <= 0f)
                    StartProwling();
                break;

            // SpiderState.Tumbled is handled by the early return above.
        }

        if (_mover.IsMoving)
            _walkCycle += deltaTime * (State == SpiderState.Pouncing ? 30f : 12f);
    }

    // --- States ---------------------------------------------------------------------

    private void Prowl(float deltaTime, World world)
    {
        // Short pause at each point, then pick another.
        if (_timer > 0f)
        {
            _timer -= deltaTime;
            if (_timer <= 0f)
            {
                _target = world.RandomFreePoint(BodyRadius + 0.1f, 1f);
                _mover.ResetProgress();
            }
            return;
        }

        if (_mover.MoveTowards(_target, ProwlSpeed, deltaTime, world, p => !world.IsBlocked(p, BodyRadius)))
            _timer = ProwlPauseDuration;
    }

    private void Hunt(float deltaTime, World world)
    {
        // Keep chasing while the prey is alive, in range, and either still
        // vibrating or only recently gone quiet. Otherwise switch to another
        // busy worker if there is one, or give up. IsDead is checked
        // explicitly: a killed Bramblekin's removal from Colony is deferred
        // to the end of the frame, so Contains() alone can't tell it's gone.
        if (_prey is null || _prey.IsDead || !world.Colony.Contains(_prey) ||
            GroundMover.HorizontalDistanceSquared(Position, _prey.Position) > (VibrationRadius * 1.5f) * (VibrationRadius * 1.5f))
        {
            _prey = null;
        }
        else
        {
            _sinceVibration = _prey.IsVibrating ? 0f : _sinceVibration + deltaTime;
            if (_sinceVibration > ChaseMemory)
                _prey = null;
        }

        if (_prey is null)
        {
            if (FindPrey(world) is { } other)
                StartHunting(other);
            else
                StartProwling();
            return;
        }

        float distance = GroundMover.HorizontalDistance(Position, _prey.Position);
        if (distance <= PounceRange)
        {
            // Commit to a straight dash at where the prey is right now; a
            // quick Bramblekin can still sidestep it.
            var toPrey = new Vector2(_prey.Position.X - Position.X, _prey.Position.Z - Position.Z);
            _pounceDirection = toPrey.LengthSquared() > 1e-6f ? Vector2.Normalize(toPrey) : _mover.Heading;
            _timer = PounceDuration;
            SetState(SpiderState.Pouncing);
            return;
        }

        _mover.MoveTowards(_prey.Position, HuntSpeed, deltaTime, world, p => !world.IsBlocked(p, BodyRadius));
    }

    private void Pounce(float deltaTime, World world)
    {
        // Dash along the committed direction (still solid against rocks).
        var dashTarget = Position + new Vector3(_pounceDirection.X, 0, _pounceDirection.Y) * (PounceSpeed * deltaTime + 0.01f);
        _mover.MoveTowards(dashTarget, PounceSpeed, deltaTime, world, _ => false);
        _mover.Heading = _pounceDirection;

        // Anything it touches mid-pounce is caught — except a Bramblekin
        // that's Fighting: it's braced for the spider, so it doesn't block
        // or interrupt the pounce, and the Strike/Bite exchange handles that
        // fight instead. Reverse for-loop: World.Kill only queues the
        // removal, so Colony never actually changes size during this walk,
        // but the pattern stays consistent everywhere.
        Bramblekin? caught = null;
        for (int i = world.Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = world.Colony[i];
            if (bramblekin.IsDead || bramblekin.State == BramblekinState.Fighting)
                continue;

            if (GroundMover.HorizontalDistance(Position, bramblekin.Position) >= BodyRadius + Bramblekin.BodyRadius)
                continue;

            caught = bramblekin;
            break;
        }

        if (caught is not null)
        {
            world.Kill(caught, DeathCause.Predator, this);
            Kills++;
            _prey = null;
            _timer = FeedDuration;
            SetState(SpiderState.Feeding);
            return;
        }

        _timer -= deltaTime;
        if (_timer <= 0f)
        {
            // Dash ended without catching anyone: drop the stale target
            // reference rather than leave it dangling through Recovering.
            _prey = null;
            _timer = RecoverDuration;
            SetState(SpiderState.Recovering);
        }
    }

    private void Investigate(float deltaTime, World world)
    {
        if (!_staring)
        {
            _timer += deltaTime;
            bool closeEnough = GroundMover.HorizontalDistance(Position, _target) <= InvestigateStandOff;
            if (closeEnough || _timer > InvestigateTravelTimeout)
            {
                _staring = true;
                _timer = StareDuration;
            }
            else
            {
                _mover.MoveTowards(_target, HuntSpeed, deltaTime, world, p => !world.IsBlocked(p, BodyRadius));
            }
            return;
        }

        // Stare: face the impact point and don't move.
        var toImpact = new Vector2(_target.X - Position.X, _target.Z - Position.Z);
        if (toImpact.LengthSquared() > 1e-6f)
            _mover.Heading = Vector2.Normalize(toImpact);

        _timer -= deltaTime;
        if (_timer <= 0f)
            StartProwling();
    }

    // --- Transitions ----------------------------------------------------------------

    private void StartProwling()
    {
        _prey = null;
        _timer = ProwlPauseDuration;
        SetState(SpiderState.Prowling);
    }

    private void StartHunting(Bramblekin prey)
    {
        _prey = prey;
        _sinceVibration = 0f;
        SetState(SpiderState.Hunting);
    }

    private void StartInvestigating(Vector3 impactPoint)
    {
        _prey = null;
        _target = impactPoint;
        _staring = false;
        _timer = 0f;
        SetState(SpiderState.Investigating);
    }

    private void SetState(SpiderState state)
    {
        State = state;
        _mover.ResetProgress();
    }

    /// <summary>The nearest vibrating Bramblekin within <see cref="VibrationRadius"/>, if any.</summary>
    private Bramblekin? FindPrey(World world)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = VibrationRadius * VibrationRadius;
        // The Spatial Grid: only the Colony chunks around this spider.
        List<Bramblekin> nearby = world.QueryNearbyColony(Position);
        for (int i = nearby.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = nearby[i];
            // IsVibrating is already false for a dead Bramblekin; checked
            // again explicitly so this never targets one even if that changes.
            if (bramblekin.IsDead || !bramblekin.IsVibrating)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, bramblekin.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = bramblekin;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>The nearest living, Fighting Bramblekin within <paramref name="range"/>, if any — the Bite's target.</summary>
    private Bramblekin? NearestFighterInRange(World world, float range)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = range * range;
        // The Spatial Grid: only the Colony chunks around this spider.
        List<Bramblekin> nearby = world.QueryNearbyColony(Position);
        for (int i = nearby.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = nearby[i];
            if (bramblekin.IsDead || bramblekin.State != BramblekinState.Fighting)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, bramblekin.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = bramblekin;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    // --- Drawing ----------------------------------------------------------------------

    /// <summary>
    /// A squat two-part body (big abdomen behind, smaller head in front) with
    /// eight jointed legs, all drawn in the spider's local frame: +X forward,
    /// +Z to its right. Eye colour shows its mood: dim when prowling, red when
    /// hunting, yellow when investigating.
    /// </summary>
    public void Draw()
    {
        float yawDegrees = -MathF.Atan2(_mover.Heading.Y, _mover.Heading.X) * 180f / MathF.PI;

        Rlgl.PushMatrix();
        Rlgl.Translatef(Position.X, Position.Y, Position.Z);
        Rlgl.Rotatef(yawDegrees, 0, 1, 0);

        // Tumbled: roll onto its side (tips over in the first moment, then a
        // small dazed wobble) rather than standing upright.
        if (State == SpiderState.Tumbled)
        {
            float elapsed = TumbledDuration - _timer;
            float fallIn = MathF.Min(elapsed / 0.3f, 1f);
            float wobble = MathF.Sin(elapsed * 6f) * 6f;
            Rlgl.Rotatef(fallIn * 80f + wobble, 1, 0, 0);
        }

        // Abdomen: a flattened, elongated sphere.
        Rlgl.PushMatrix();
        Rlgl.Translatef(-0.28f, 0.34f, 0);
        Rlgl.Scalef(1.25f, 0.62f, 1f);
        Raylib.DrawSphere(Vector3.Zero, 0.36f, BodyColor);
        Rlgl.PopMatrix();

        // Head (cephalothorax).
        Rlgl.PushMatrix();
        Rlgl.Translatef(0.2f, 0.3f, 0);
        Rlgl.Scalef(1.1f, 0.7f, 1f);
        Raylib.DrawSphere(Vector3.Zero, 0.24f, BodyColor);
        Rlgl.PopMatrix();

        // Eyes.
        Color eyeColor = State switch
        {
            SpiderState.Hunting or SpiderState.Pouncing => new Color(230, 40, 30, 255),
            SpiderState.Investigating => new Color(240, 210, 60, 255),
            SpiderState.Tumbled => new Color(175, 190, 220, 220),
            _ => new Color(120, 110, 100, 255),
        };
        Raylib.DrawSphere(new Vector3(0.43f, 0.38f, -0.08f), 0.045f, eyeColor);
        Raylib.DrawSphere(new Vector3(0.43f, 0.38f, 0.08f), 0.045f, eyeColor);

        // Legs: four per side, fanning from front to back.
        float[] attachX = { 0.28f, 0.2f, 0.1f, 0.0f };
        float[] reachX = { 0.55f, 0.2f, -0.2f, -0.55f };
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < 4; i++)
            {
                // Alternate legs lift in turn while walking.
                float phase = _walkCycle + i * MathF.PI / 2f + (side > 0 ? MathF.PI : 0f);
                float lift = _mover.IsMoving ? MathF.Max(0f, MathF.Sin(phase)) * 0.12f : 0f;

                var hip = new Vector3(attachX[i], 0.3f, side * 0.16f);
                var knee = new Vector3(attachX[i] + reachX[i] * 0.55f, 0.62f + lift, side * 0.62f);
                var foot = new Vector3(attachX[i] + reachX[i], lift * 0.5f, side * 0.95f);
                Raylib.DrawCylinderEx(hip, knee, 0.045f, 0.035f, 5, LegColor);
                Raylib.DrawCylinderEx(knee, foot, 0.035f, 0.02f, 5, LegColor);
            }
        }

        Rlgl.PopMatrix();

        // While staring at an impact, a faint line shows what it is looking at.
        if (State == SpiderState.Investigating && _staring)
            Raylib.DrawLine3D(Position + new Vector3(0, 0.4f, 0), _target + new Vector3(0, 0.05f, 0), new Color(240, 210, 60, 160));
    }
}


// =============================================================================
//  Wildlife: the Hornet Swarm and the Grub
// =============================================================================

/// <summary>
/// The Hornet Swarm: a small, fast, genuinely (if mildly) hostile
/// predator — a weaker, faster, group version of the Wolf Spider's own
/// concept. Spawns in
/// clusters of <see cref="World.HornetSwarmMinSize"/>-<see cref="World.HornetSwarmMaxSize"/>
/// (see <see cref="World.UpdateHornetSpawn"/>) around a shared anchor point
/// and wanders erratically near it — each Hornet's own aggro check runs
/// independently every frame rather than through any shared swarm-wide
/// coordination, but because they spawn clustered together this still
/// reads as "the whole swarm reacts together" the instant any one
/// Bramblekin strays within <see cref="AggroRadius"/> of any one of them.
///
/// Damage Model: Bramblekin genuinely has hit points (<see cref="Bramblekin.Health"/>/
/// <see cref="Bramblekin.TakeDamage"/>) — the Wolf Spider's own Pounce
/// simply chooses to call <see cref="World.Kill"/> outright rather than
/// damage through it. A Hornet instead deals a small
/// <see cref="BiteDamage"/> per bite, on its own cooldown, so a Bramblekin
/// it catches takes several bites to actually die rather than being
/// one-shot the way the Spider's Pounce is — "low attack damage" reads
/// literally, through the same Health/TakeDamage plumbing every other
/// damage source in this file already uses, rather than a percentage
/// chance or some other approximation.
///
/// A Hornet counts as a threat to any Bramblekin that can see it (see
/// Bramblekin.Perceive) — a sharp-eyed one gives a swarm a wide berth, a
/// dull or hungry one may blunder right into it.
/// </summary>
public sealed class Hornet : ICombatant
{
    /// <summary>Collision/body radius in meters.</summary>
    public const float BodyRadius = 0.1f;

    /// <summary>How far from the terrain edge it wanders, in meters.</summary>
    public const float EdgeMargin = 0.3f;

    /// <summary>How far (m) from its cluster's own spawn anchor a Hornet may land at spawn time, or wander to while idle.</summary>
    public const float ClusterJitterRadius = 1.2f;

    private const float WanderSpeed = 0.4f;

    /// <summary>Faster than a walking Bramblekin's own <see cref="Bramblekin.WalkSpeed"/> (1.5), slower than a fleeing one's (4.5) — genuinely hard to simply outrun, but not an inescapable predator either.</summary>
    private const float ChaseSpeed = 2.6f;

    private const float WanderPauseDuration = 1.2f;

    /// <summary>How close (m) a Bramblekin has to wander to any one Hornet in a cluster to aggro the whole thing (see this class's own doc comment).</summary>
    public const float AggroRadius = 3f;

    /// <summary>Gives up the chase once its target has out-run this far (m) past <see cref="AggroRadius"/> — otherwise a single fast Bramblekin could drag a Hornet clean across the map.</summary>
    private const float ChaseLeashRadius = AggroRadius * 3f;

    private const float BiteRange = 0.35f;

    /// <summary>Low Attack Damage: a small fraction of a Bramblekin's own <see cref="Bramblekin.MaxHealth"/> (30) per bite — several bites to actually kill, not the Wolf Spider's one-touch Pounce.</summary>
    private const int BiteDamage = 3;

    private const float BiteCooldownDuration = 1f;

    /// <summary>Hit points out of this — low, so a single Bramblekin strike swats one out of the air.</summary>
    public const int MaxHealth = 4;

    private static readonly Color StripeColorYellow = new(230, 190, 20, 255);
    private static readonly Color StripeColorBlack = new(30, 25, 20, 255);
    private static readonly Color WingColor = new(230, 230, 235, 90);

    private readonly Random _rng;
    private readonly GroundMover _mover;

    /// <summary>This Hornet's cluster's shared spawn anchor — see <see cref="World.UpdateHornetSpawn"/>. Wandering (while not chasing) stays within <see cref="ClusterJitterRadius"/> of this point.</summary>
    private readonly Vector3 _anchor;

    private Vector3 _target;
    private float _pauseTimer;
    private float _biteCooldown;
    private Bramblekin? _chaseTarget;

    /// <summary>Terrain-aware, same treatment as Bramblekin — Y is snapped to World.GetHeightAt every read.</summary>
    public Vector3 Position => World.Grounded(_mover.Position);

    /// <summary>True once swatted by a Bramblekin. Removal from World.Hornets is deferred to the end of the frame.</summary>
    public bool IsDead { get; private set; }

    /// <summary>Hit points out of <see cref="MaxHealth"/>.</summary>
    public int Health { get; private set; } = MaxHealth;

    public float CollisionRadius => BodyRadius;

    public Hornet(Vector3 position, Vector3 anchor, Random rng)
    {
        _rng = rng;
        _anchor = anchor;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);
        _target = position;
        _pauseTimer = (float)rng.NextDouble() * WanderPauseDuration;
    }

    /// <summary>Marks it caught. Called once, from World.KillHornet.</summary>
    public void MarkDead() => IsDead = true;

    /// <summary>A Bramblekin's strike: at 0 Health it's swatted out of the air (see <see cref="World.KillHornet"/>).</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        Health = Math.Max(0, Health - damage);
        if (Health <= 0)
            world.KillHornet(this);
    }

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return;

        _mover.Idle();

        // Safety net: the Bramblekin we're chasing may have died, or
        // simply out-run the leash, since last frame.
        if (_chaseTarget is { } stale && (stale.IsDead || !world.Colony.Contains(stale) ||
            GroundMover.HorizontalDistanceSquared(Position, stale.Position) > ChaseLeashRadius * ChaseLeashRadius))
        {
            _chaseTarget = null;
        }

        // The Hornet Swarm's aggro: an independent per-Hornet check every
        // frame — see this class's own doc comment for why this alone is
        // enough to make a whole cluster read as reacting together.
        if (_chaseTarget is null)
        {
            Bramblekin? threat = NearestBramblekinWithin(world, AggroRadius);
            if (threat is not null)
                _chaseTarget = threat;
        }

        if (_chaseTarget is { } target)
        {
            if (GroundMover.HorizontalDistance(Position, target.Position) <= BiteRange)
            {
                _biteCooldown -= deltaTime;
                if (_biteCooldown <= 0f)
                {
                    target.TakeDamage(BiteDamage, world, DeathCause.Predator, this);
                    _biteCooldown = BiteCooldownDuration;
                }
                return;
            }

            _mover.MoveTowards(target.Position, ChaseSpeed, deltaTime, world, p => world.Terrain.Contains(p, EdgeMargin));
            return;
        }

        // Idle: a tight random wander that never strays far from this
        // cluster's own anchor point.
        if (_pauseTimer > 0f)
        {
            _pauseTimer -= deltaTime;
            if (_pauseTimer <= 0f)
            {
                float angle = (float)(_rng.NextDouble() * MathF.Tau);
                float radius = (float)_rng.NextDouble() * ClusterJitterRadius;
                _target = _anchor + new Vector3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);
            }
            return;
        }

        if (_mover.MoveTowards(_target, WanderSpeed, deltaTime, world, p => world.Terrain.Contains(p, EdgeMargin)))
            _pauseTimer = WanderPauseDuration;
    }

    private Bramblekin? NearestBramblekinWithin(World world, float radius)
    {
        Bramblekin? nearest = null;
        float bestDistanceSquared = radius * radius;
        // The Spatial Grid: only the Colony chunks around this Hornet.
        List<Bramblekin> nearby = world.QueryNearbyColony(Position);
        for (int i = nearby.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = nearby[i];
            if (bramblekin.IsDead)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, bramblekin.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                nearest = bramblekin;
                bestDistanceSquared = distanceSquared;
            }
        }
        return nearest;
    }

    /// <summary>A tiny yellow/black striped body with a pair of thin wing lines — cheap enough to draw many at once.</summary>
    public void Draw()
    {
        var bottom = Position + new Vector3(0, BodyRadius * 0.6f, 0);
        var top = Position + new Vector3(0, BodyRadius * 1.6f, 0);
        Raylib.DrawCapsule(bottom, top, BodyRadius, 5, 3, StripeColorYellow);
        Raylib.DrawCapsuleWires(bottom, top, BodyRadius, 5, 3, StripeColorBlack);

        // A single dark stripe band around the middle of the body reads as
        // its namesake stripe without a second, more expensive shape.
        Vector3 mid = Position + new Vector3(0, BodyRadius * 1.1f, 0);
        Raylib.DrawCircle3D(mid, BodyRadius * 1.02f, new Vector3(1, 0, 0), 90f, StripeColorBlack);

        // A pair of thin, near-transparent wing lines flicking out to the sides.
        Vector3 wingBase = mid;
        Raylib.DrawLine3D(wingBase, wingBase + new Vector3(BodyRadius * 2f, BodyRadius * 0.5f, 0), WingColor);
        Raylib.DrawLine3D(wingBase, wingBase + new Vector3(-BodyRadius * 2f, BodyRadius * 0.5f, 0), WingColor);
    }
}

/// <summary>
/// A food competitor and easy prey: burrows in from the map's edge (see
/// <see cref="World.UpdateGrubSpawn"/>), sniffs out the nearest loose Food
/// within <see cref="SmellRadius"/> and eats it — claimed or not, Grubs
/// don't respect anyone's dibs — growing fatter with every bite. Skitters
/// away from any Bramblekin that gets close, but it's slower than one
/// walking, so a hungry Bramblekin that can't see any Food will run it
/// down (see Bramblekin.UpdateHunger); killed, it drops a little Food
/// plus some of whatever it ate (see <see cref="World.KillGrub"/>).
/// </summary>
public sealed class Grub : ICombatant
{
    /// <summary>Collision/body radius in meters.</summary>
    public const float BodyRadius = 0.18f;

    /// <summary>How far from the terrain edge it may wander/spawn, in meters.</summary>
    public const float EdgeMargin = 0.3f;

    /// <summary>How far (m) away it can smell loose Food.</summary>
    public const float SmellRadius = 12f;

    /// <summary>At most this much Food drops when it dies, however much it ate.</summary>
    public const int MaxCarcassFood = 4;

    public const int MaxHealth = 12;

    private const float CrawlSpeed = 0.9f;
    private const float SkitterSpeed = 1.1f;

    /// <summary>It skitters away from any Bramblekin closer than this (m).</summary>
    private const float SkittishRadius = 2.5f;

    /// <summary>How close (m) it must get to Food to eat it.</summary>
    private const float EatDistance = 0.4f;

    private const float WanderPauseDuration = 2f;

    private static readonly Color BodyColor = new(120, 95, 60, 255);
    private static readonly Color SnoutColor = new(90, 65, 40, 255);

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private FoodShard? _targetFood;
    private Vector3 _wanderTarget;
    private float _pauseTimer;

    public Grub(Vector3 position, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);
        _wanderTarget = position;
    }

    /// <summary>Terrain-aware, same treatment as Hornet/Bramblekin — Y is snapped to World.GetHeightAt every read.</summary>
    public Vector3 Position => World.Grounded(_mover.Position);

    /// <summary>True once killed by a Bramblekin. Removal from World.Grubs is deferred to the end of the frame.</summary>
    public bool IsDead { get; private set; }

    public int Health { get; private set; } = MaxHealth;

    /// <summary>Pieces of Food eaten so far — it grows with each one, and drops some of it back on death.</summary>
    public int FoodEaten { get; private set; }

    public float CollisionRadius => BodyRadius * Girth;

    /// <summary>Visual and collision scale: fattens with every piece of Food eaten.</summary>
    private float Girth => 1f + 0.12f * Math.Min(FoodEaten, 5);

    /// <summary>Marks it dead. Called once, from World.KillGrub.</summary>
    public void MarkDead() => IsDead = true;

    /// <summary>A Bramblekin's strike: at 0 Health it dies (see <see cref="World.KillGrub"/>).</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        Health = Math.Max(0, Health - damage);
        if (Health <= 0)
            world.KillGrub(this);
    }

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return;

        _mover.Idle();

        // Skittish: a Bramblekin too close sends it scurrying straight away.
        if (world.NearestLivingKinWithin(Position, SkittishRadius) is { } kin)
        {
            var away = new Vector2(Position.X - kin.Position.X, Position.Z - kin.Position.Z);
            away = away.LengthSquared() > 1e-4f ? Vector2.Normalize(away) : Vector2.UnitX;
            Vector3 fleeTarget = Position + new Vector3(away.X, 0f, away.Y) * 2f;
            if (!world.Terrain.Contains(fleeTarget, EdgeMargin))
                fleeTarget = Position + new Vector3(-away.Y, 0f, away.X) * 2f;
            _mover.MoveTowards(fleeTarget, SkitterSpeed, deltaTime, world, p => world.Terrain.Contains(p, EdgeMargin));
            return;
        }

        if (_targetFood is null || !world.IsAvailable(_targetFood, claimant: null))
            _targetFood = world.NearestAvailableFood(Position, SmellRadius, claimant: null);

        if (_targetFood is { } food)
        {
            if (GroundMover.HorizontalDistance(Position, food.Position) <= EatDistance)
            {
                if (world.GrubEat(food))
                    FoodEaten++;
                _targetFood = null;
                return;
            }

            _mover.MoveTowards(food.Position, CrawlSpeed, deltaTime, world, p => !world.IsBlocked(p, BodyRadius));
            return;
        }

        // Nothing to smell: a slow random wander.
        if (_pauseTimer > 0f)
        {
            _pauseTimer -= deltaTime;
            if (_pauseTimer <= 0f)
                _wanderTarget = world.RandomFreePoint(BodyRadius, EdgeMargin + 1f);
            return;
        }

        if (_mover.MoveTowards(_wanderTarget, CrawlSpeed * 0.6f, deltaTime, world, p => !world.IsBlocked(p, BodyRadius)))
            _pauseTimer = WanderPauseDuration * (0.5f + (float)_rng.NextDouble());
    }

    /// <summary>A small brown/tan mole-like silhouette: a low capsule body with a darker snout, fattening as it eats.</summary>
    public void Draw()
    {
        float radius = BodyRadius * Girth;
        var bottom = Position + new Vector3(0, radius * 0.5f, 0);
        var top = Position + new Vector3(0, radius * 1.3f, 0);
        Raylib.DrawCapsule(bottom, top, radius, 6, 3, BodyColor);
        Raylib.DrawCapsuleWires(bottom, top, radius, 6, 3, new Color(40, 30, 20, 255));

        Vector2 heading = _mover.Heading.LengthSquared() > 1e-6f ? _mover.Heading : Vector2.UnitX;
        Vector3 snout = Position + new Vector3(heading.X, radius * 0.6f, heading.Y) * radius;
        Raylib.DrawSphere(snout, radius * 0.4f, SnoutColor);
    }
}

// =============================================================================
//  UI
// =============================================================================

/// <summary>A minimal clickable rectangle with a centred text label.</summary>
public sealed class UiButton
{
    public Rectangle Bounds { get; }

    public UiButton(Rectangle bounds) => Bounds = bounds;

    public bool Contains(Vector2 point) => Raylib.CheckCollisionPointRec(point, Bounds);

    public void Draw(string label, bool highlighted, bool disabled = false)
    {
        bool hovered = Contains(Raylib.GetMousePosition());

        Color fill = highlighted ? new Color(230, 190, 60, 255)   // Gold when armed.
                   : disabled ? new Color(185, 175, 170, 255)     // Greyed out when unaffordable.
                   : hovered ? new Color(245, 245, 245, 255)      // Light on hover.
                   : new Color(220, 220, 220, 255);               // Default.

        Raylib.DrawRectangleRec(Bounds, fill);
        Raylib.DrawRectangleLinesEx(Bounds, 2f, Color.DarkGray);

        // UI Text Scaling: sized off the button's own height rather than a
        // fixed constant, so a bigger button (see the 3x-scaled Debug Time
        // Scale buttons) automatically gets bigger, still-centred text
        // instead of a tiny label lost in a large rectangle.
        int fontSize = (int)(Bounds.Height * 0.5f);
        int textWidth = Raylib.MeasureText(label, fontSize);
        int x = (int)(Bounds.X + (Bounds.Width - textWidth) / 2f);
        int y = (int)(Bounds.Y + (Bounds.Height - fontSize) / 2f);
        Raylib.DrawText(label, x, y, fontSize, disabled && !highlighted ? new Color(90, 80, 75, 255) : Color.Black);
    }
}
