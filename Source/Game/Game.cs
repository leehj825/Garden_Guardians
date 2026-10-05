using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Owns the window and the main loop: reads input, steps the <see cref="World"/>,
/// and draws it with the UI on top. Platform-independent.
/// </summary>
public static partial class Game
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
    /// Longest frame the game catches up on. A hitch (window dragged, app
    /// resumed) is simply lost time rather than a burst of catching up.
    /// </summary>
    private const float MaxDeltaTime = 1f / 20f;

    /// <summary>
    /// The simulation always advances in steps of exactly this — the same
    /// step the headless tuning runs use — at every speed, on every device:
    /// bigger steps played a noticeably different game (at 0.046s a step,
    /// ~50% more starvation), so fast-forward takes more steps instead.
    /// </summary>
    private const float SimulationStep = 1f / 60f;

    /// <summary>Real time a frame may spend simulating; past it, the game runs slower than the chosen speed rather than dropping frames…</summary>
    private const double SimulationBudgetSeconds = 0.028;

    /// <summary>…or at 10x and up, where more speed is worth a few frames a second.</summary>
    private const double FastSimulationBudgetSeconds = 0.045;

    /// <summary>…and at 20x and up (a time-lapse) most of the frame, drawing only a dozen frames a second: the picture matters little at that speed, and this hands nearly all of the phone's time to the simulation.</summary>
    private const double TimeLapseSimulationBudgetSeconds = 0.08;

    /// <summary>Simulated time owed (the chosen speed × real time) but not yet stepped.</summary>
    private static float _simulationBacklog;

    /// <summary>The speed the simulation is actually managing (smoothed) — below the chosen one on a slow device.</summary>
    private static float _achievedSpeed = 1f;

    /// <summary>Smoothed real time (ms) a frame spends simulating and drawing, shown in the status line.</summary>
    private static double _simMs, _drawMs;

    /// <summary>Debug Time Scale: the speeds the corner +/- buttons step through, clamped at either end.</summary>
    private static readonly float[] TimeScaleSteps = { 1f, 2f, 5f, 10f, 20f, 50f };

    /// <summary>
    /// Debug Time Scale: how many seconds of garden time pass per real
    /// second. The main loop covers them in fixed <see cref="SimulationStep"/>s
    /// (see <see cref="StepSimulation"/>), so a faster speed is more steps per
    /// frame, never bigger ones.
    /// </summary>
    private static float _timeScale = 1f;
    private static bool _lootTestDone;
    private static bool _playTestDone;

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

    /// <summary>How much of the event log the player has chosen to see (tap the Log button to change it; kept in <see cref="Preferences"/>).</summary>
    private enum LogView
    {
        /// <summary>Just the newest few entries (<see cref="BriefLogEntries"/>), a line each.</summary>
        Brief,

        /// <summary>No log, only the Log button (with a count of entries missed).</summary>
        Hidden,

        /// <summary>Everything kept (<see cref="DebugLogCapacity"/>), as tall as the screen allows.</summary>
        Full,
    }

    /// <summary>Entries shown in <see cref="LogView.Brief"/>.</summary>
    private const int BriefLogEntries = 3;

    private const string LogViewSetting = "log";

    private static LogView _logView = LogView.Brief;

    /// <summary>Whether the stats bar along the bottom is showing (tap the Stats button to change it; kept in <see cref="Preferences"/>).</summary>
    private enum StatsView
    {
        Shown,
        Hidden,
    }

    private const string StatsViewSetting = "stats";

    private static StatsView _statsView = StatsView.Shown;

    /// <summary>Where the Stats button was drawn last frame, for taps.</summary>
    private static Rectangle _statsButtonBounds;

    /// <summary>A tap on the Stats button shows or hides the stats bar, and remembers the choice. Returns true if the tap was on it.</summary>
    private static bool TapStatsButton(Vector2 point)
    {
        if (!Raylib.CheckCollisionPointRec(point, _statsButtonBounds))
            return false;
        _statsView = _statsView == StatsView.Shown ? StatsView.Hidden : StatsView.Shown;
        Preferences.Set(StatsViewSetting, _statsView);
        return true;
    }

    /// <summary>Entries logged while the log was hidden, shown on the Log button.</summary>
    private static int _unseenLogs;

    /// <summary>Where the Log button was drawn last frame, for taps.</summary>
    private static Rectangle _logButtonBounds;

    private const string OverlaySetting = "overlays";

    /// <summary>Where each clan's name tag was drawn last frame, for tapping it to open the clan card.</summary>
    private static readonly List<(Rectangle Bounds, KinGroup Clan)> _clanLabelBounds = new();

    /// <summary>The three map-guide toggles (clan range, kin links, kin range), stacked under the top buttons.</summary>
    private static (UiButton Button, string Label, MapOverlays Flag)[] OverlayButtons(float uiScale, int top, int margin)
    {
        int width = (int)(250 * uiScale), height = (int)(96 * uiScale), gap = (int)(10 * uiScale);
        (string, MapOverlays)[] rows = { ("Clans", MapOverlays.ClanRange), ("Links", MapOverlays.KinLinks), ("Range", MapOverlays.KinRange), ("Fog", MapOverlays.Fog) };
        var buttons = new (UiButton, string, MapOverlays)[rows.Length];
        for (int i = 0; i < rows.Length; i++)
            buttons[i] = (new UiButton(new Rectangle(margin, top + i * (height + gap), width, height)), rows[i].Item1, rows[i].Item2);
        return buttons;
    }

    /// <summary>A tap on a clan's name tag opens its card. Returns true if it landed on one.</summary>
    private static bool TapClanLabel(Vector2 point, World world)
    {
        foreach (var (bounds, clan) in _clanLabelBounds)
        {
            if (!Raylib.CheckCollisionPointRec(point, bounds))
                continue;
            world.SelectClan(clan);
            return true;
        }
        return false;
    }

    /// <summary>True while <see cref="RunHeadless"/> is driving the simulation — event logs go to stdout instead of the on-screen console.</summary>
    private static bool _isHeadless;

    /// <summary>True when a development run (a screenshot, or GARDEN_MENU=0) goes straight to the garden instead of the start menu; GARDEN_MENU=1 shows the menu even for a screenshot.</summary>
    private static bool SkipMenu => Environment.GetEnvironmentVariable("GARDEN_MENU") is { } menu
        ? menu == "0"
        : Environment.GetEnvironmentVariable("GARDEN_SCREENSHOT") is not null;

    /// <summary>The whole-garden camera: pulled back and up far enough to take in the whole square map (centred on the origin) at once.</summary>
    private static Camera3D OverviewCamera(float mapSize) => new()
    {
        Target = Vector3.Zero,
        Position = new Vector3(0f, 1.2f * mapSize, mapSize),
        Up = Vector3.UnitY,
        FovY = 45f,
        Projection = CameraProjection.Perspective,
    };

    /// <summary>The terrain number in the GARDEN_TERRAIN environment variable, if there is one (0 baked; 1000000 and up grown from a seed): a development aid that overrides the choice for a new garden and headless runs.</summary>
    private static int? ForcedTerrain => int.TryParse(Environment.GetEnvironmentVariable("GARDEN_TERRAIN"), out int terrain) ? terrain : null;

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
        if (_logView == LogView.Hidden)
            _unseenLogs = Math.Min(_unseenLogs + 1, 99);
    }

    /// <summary>A tap on the Log button steps it Brief → Hidden → Full → Brief, and remembers the choice. Returns true if the tap was on it.</summary>
    private static bool TapLogButton(Vector2 point)
    {
        if (!Raylib.CheckCollisionPointRec(point, _logButtonBounds))
            return false;
        _logView = _logView switch
        {
            LogView.Brief => LogView.Hidden,
            LogView.Hidden => LogView.Full,
            _ => LogView.Brief,
        };
        _unseenLogs = 0;
        Preferences.Set(LogViewSetting, _logView);
        return true;
    }

    /// <summary>
    /// Advances the garden by <paramref name="realDeltaTime"/> × the chosen
    /// speed, in fixed <see cref="SimulationStep"/>s — committing each
    /// step's births and deaths as the headless runs do — until it's caught
    /// up or its real-time budget is spent (<see cref="SimulationBudgetSeconds"/>,
    /// <see cref="FastSimulationBudgetSeconds"/>); a
    /// device that can't keep up drops the rest, running slower instead.
    /// </summary>
    private static void StepSimulation(World world, float realDeltaTime)
    {
        // A development aid: GARDEN_LOOT_TEST=level starts the prize-taking of an assault on a hill of that level at once (see World.StartTestLoot).
        if (!_lootTestDone && int.TryParse(Environment.GetEnvironmentVariable("GARDEN_LOOT_TEST"), out int lootLevel))
        {
            _lootTestDone = true;
            world.StartTestLoot(lootLevel);
        }
        _simulationBacklog += realDeltaTime * _timeScale;
        double budget = _timeScale >= 20f ? TimeLapseSimulationBudgetSeconds : _timeScale >= 10f ? FastSimulationBudgetSeconds : SimulationBudgetSeconds;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        float simulated = 0f;
        while (_simulationBacklog >= SimulationStep)
        {
            world.Update(SimulationStep);
            world.CommitPendingChanges();
            _simulationBacklog -= SimulationStep;
            simulated += SimulationStep;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds > budget)
                break;
        }
        _simulationBacklog = MathF.Min(_simulationBacklog, SimulationStep);
        if (realDeltaTime > 0f)
            _achievedSpeed += (simulated / realDeltaTime - _achievedSpeed) * 0.05f;
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
        {
            Raylib.InitWindow(ScreenWidth, ScreenHeight, "Garden Guardians");
            Image icon = Raylib.LoadImage("Assets/icon.png");
            if (icon.Width > 0)
                Raylib.SetWindowIcon(icon);
            Raylib.UnloadImage(icon);
        }
        Raylib.SetTargetFPS(TargetFps);
        if (Environment.GetEnvironmentVariable("GARDEN_STRETCH") is { } stretchClips)
        {
            BramblekinStretch.Run(stretchClips); // (a development aid: see BramblekinStretch)
            Raylib.CloseWindow();
            return;
        }

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
        // Save/Load: the garden carries on where it was left (see SaveSystem).
        Preferences.Load(Preferences.DefaultPath);
        _logView = Preferences.Get(LogViewSetting, LogView.Brief);
        _statsView = Preferences.Get(StatsViewSetting, StatsView.Shown);
        _alertsOn = Preferences.Get(AlertsSetting, AlertsView.On) == AlertsView.On;
        World.Overlays = Preferences.Get(OverlaySetting, MapOverlays.All);
        _gardenSlot = (int)Preferences.Get(GardenSetting, GardenSlot.Garden1);
        TerrainData.GrowNewGardens = Preferences.Get(TerrainSetting, TerrainMode.Fixed) == TerrainMode.Random;
        TerrainData.NewGardenSize = (int)Preferences.Get(MapSizeSetting, MapSize.Small);
        World world;
        if (SkipMenu)
            world = LoadOrCreateWorld(GardenPath);
        else
        {
            MenuChoice? choice = ShowMenu();
            if (choice is null)
            {
                Raylib.CloseWindow();
                return;
            }
            _gardenSlot = choice.Slot;
            Preferences.Set(GardenSetting, (GardenSlot)_gardenSlot);
            if (!choice.Resume)
            {
                TerrainData.GrowNewGardens = choice.GrowTerrain;
                Preferences.Set(TerrainSetting, choice.GrowTerrain ? TerrainMode.Random : TerrainMode.Fixed);
            }
            TerrainData.NewGardenSize = choice.Size;
            _startEra = choice.StartEra;
            world = MakeWorld(() => choice.Resume ? LoadOrCreateWorld(GardenPath) : StartNewGarden(GardenPath));
        }
        camera = OverviewCamera(world.Terrain.Size);
        var input = new WorldTapInput();
        var touchCamera = new TouchCameraController();
        var followCamera = new FollowCamera(camera);
        var director = new Director();
        var play = new PlayControl();
        Camera3D overview = camera;
        DebugShot.Place(ref camera, world);
        float autosaveTimer = AutosaveInterval;

        // --- Main loop -------------------------------------------------------
        while (!Raylib.WindowShouldClose())
        {
            float rawDeltaTime = MathF.Min(Raylib.GetFrameTime(), MaxDeltaTime);

            // 0) Spectator Camera: one finger (or a held mouse button) drags
            //    to pan, two fingers twist to rotate around the current
            //    Target and pinch to zoom. Runs before the tap input below
            //    so the rest of the frame sees an already-settled camera.
            // (The History screen takes over touches and drags while it's open.)
            if (!_playTestDone && Environment.GetEnvironmentVariable("GARDEN_PLAY_TEST") == "1" && world.Colony.FirstOrDefault(k => !k.IsDead && !k.IsYoung && (Environment.GetEnvironmentVariable("GARDEN_PLAY_FEMALE") != "1" || k.Sex == Sex.Female) && (Environment.GetEnvironmentVariable("GARDEN_PLAY_MALE") != "1" || k.Sex == Sex.Male)) is { } testKin)
            {
                _playTestDone = true; // A development aid: start out controlling a kin.
                if (Environment.GetEnvironmentVariable("GARDEN_PLAY_SWORDSMAN") == "1")
                {
                    testKin.CyclePlayerJob();
                    testKin.CyclePlayerJob(); // Hunter, then Swordsman.
                }
                if (Enum.TryParse(Environment.GetEnvironmentVariable("GARDEN_PLAY_JOB"), out KinJob wantedJob))
                {
                    for (int step = 0; step < 6 && testKin.PlayerJob != wantedJob; step++)
                        testKin.CyclePlayerJob();
                }
                world.SelectKin(testKin);
                play.Begin(testKin, camera, world);
            }
            bool playing = play.IsActive;
            if (!_showChronicle && !playing)
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
            var mapButton = new UiButton(new Rectangle(speedButtonMargin * 2 + speedButtonWidth * 2 + speedButtonGap, speedButtonMargin,
                (int)(190 * uiScale), speedButtonHeight));
            var historyButton = new UiButton(new Rectangle(mapButton.Bounds.X + mapButton.Bounds.Width + speedButtonMargin, speedButtonMargin,
                (int)(290 * uiScale), speedButtonHeight));
            UiButton? autoButton = _showChronicle ? null : new UiButton(new Rectangle(historyButton.Bounds.X + historyButton.Bounds.Width + speedButtonMargin, speedButtonMargin,
                (int)(190 * uiScale), speedButtonHeight));
            UiButton? followButton = _showChronicle || playing ? null : FollowButton(world);
            UiButton? controlButton = followButton is null ? null : ControlButton(world, followButton);
            UiButton? newGardenButton = _showChronicle ? NewGardenButton(historyButton, speedButtonMargin) : null;
            UiButton? gardenSlotButton = newGardenButton is null ? null : GardenSlotButton(newGardenButton, speedButtonMargin);
            UiButton? terrainButton = gardenSlotButton is null ? null : TerrainModeButton(gardenSlotButton, speedButtonMargin);
            var overlayButtons = _showChronicle ? null : OverlayButtons(uiScale, speedButtonMargin * 2 + speedButtonHeight, speedButtonMargin);
            _newGardenConfirm = MathF.Max(0f, _newGardenConfirm - Raylib.GetFrameTime());

            // 1) Input: the player has no lever on the world. The only tap
            //    left is inspecting a single Bramblekin (see WorldTapInput,
            //    which only fires on a clean release that never turned into
            //    a pan). The Debug Time Scale +/- buttons are checked first
            //    and, if hit, swallow the click so it never also lands as a
            //    ground tap.
            bool mousePressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
            Vector2 mousePosition = Raylib.GetMousePosition();
            if (playing)
            {
                // PlayControl reads the touches itself (below).
            }
            else if (mousePressed && TapBanner(mousePosition, followCamera))
            {
                director.Stop(); // Flew to the banner's big moment.
            }
            else if (mousePressed && autoButton is not null && autoButton.Contains(mousePosition))
            {
                director.Toggle();
                if (director.IsOn)
                    followCamera.Release();
            }
            else if (mousePressed && speedDownButton.Contains(mousePosition))
                DecreaseTimeScale();
            else if (mousePressed && speedUpButton.Contains(mousePosition))
                IncreaseTimeScale();
            else if (mousePressed && historyButton.Contains(mousePosition))
                ToggleChronicle();
            else if (mousePressed && newGardenButton is not null && newGardenButton.Contains(mousePosition))
            {
                if (ConfirmNewGarden())
                {
                    world = MakeWorld(() => StartNewGarden(GardenPath));
                    ClearBanners();
                    overview = OverviewCamera(world.Terrain.Size);
                    camera = overview;
                    followCamera = new FollowCamera(overview);
                    director.Stop();
                    autosaveTimer = AutosaveInterval;
                }
            }
            else if (mousePressed && terrainButton is not null && terrainButton.Contains(mousePosition))
                ToggleTerrainMode();
            else if (mousePressed && gardenSlotButton is not null && gardenSlotButton.Contains(mousePosition))
            {
                World leaving = world;
                world = MakeWorld(() => SwitchGarden(leaving));
                ClearBanners();
                overview = OverviewCamera(world.Terrain.Size);
                camera = overview;
                followCamera = new FollowCamera(overview);
                director.Stop();
                autosaveTimer = AutosaveInterval;
            }
            else if (_showChronicle)
            {
                // The History screen scrolls instead (see DrawChronicle).
            }
            else if (mousePressed && TapLogButton(mousePosition))
            {
                // Showed more, less or none of the log.
            }
            else if (mousePressed && TapStatsButton(mousePosition))
            {
                // Showed or hid the stats bar.
            }
            else if (mousePressed && TapAlertsButton(mousePosition))
            {
                // Showed or hid the alert banners.
            }
            else if (mousePressed && overlayButtons is not null && overlayButtons.Any(o => o.Button.Contains(mousePosition)))
            {
                World.Overlays ^= overlayButtons.First(o => o.Button.Contains(mousePosition)).Flag;
                Preferences.Set(OverlaySetting, World.Overlays);
            }
            else if (mousePressed && TapClanLabel(mousePosition, world))
            {
                // Opened the clan card from its name tag.
            }
            else if (mousePressed && mapButton.Contains(mousePosition))
            {
                director.Stop();
                followCamera.ShowWholeMap();
            }
            else if (mousePressed && controlButton is not null && controlButton.Contains(mousePosition))
            {
                if (world.SelectedKin is { IsDead: false } chosen)
                {
                    director.Stop();
                    followCamera.Release();
                    _timeScale = 1f;
                    play.Begin(chosen, camera, world);
                    playing = true;
                }
            }
            else if (mousePressed && followButton is not null && followButton.Contains(mousePosition))
                followCamera.ToggleFollow(world);
            else
                input.Update(camera, world);
            if (playing)
            {
                play.Update(ref camera, world, rawDeltaTime);
                if (!play.IsActive)
                {
                    playing = false;
                    if (!followCamera.IsFollowing)
                        followCamera.ToggleFollow(world); // Back to watching it from above.
                }
            }
            else
            {
                followCamera.Update(ref camera, world, rawDeltaTime, touchCamera.DraggedThisGesture);
                director.Update(ref camera, world, rawDeltaTime, touchCamera.DraggedThisGesture || followCamera.IsBusy);
            }

            // 2) Simulation, in fixed steps (see StepSimulation).
            long simStart = System.Diagnostics.Stopwatch.GetTimestamp();
            StepSimulation(world, rawDeltaTime);
            _simMs += (System.Diagnostics.Stopwatch.GetElapsedTime(simStart).TotalMilliseconds - _simMs) * 0.05;
            long drawStart = System.Diagnostics.Stopwatch.GetTimestamp();
            UpdateBanners(world, Raylib.GetFrameTime());

            // 3) Rendering.
            Raylib.BeginDrawing();
            Raylib.ClearBackground(world.SkyColor);

            Raylib.BeginMode3D(camera);
            ProceduralView.Eye = camera.Position;
            ProceduralView.Focus = camera.Target;
            ProceduralView.FovDegrees = camera.FovY;
            ProceduralView.Aspect = Raylib.GetScreenWidth() / (float)Math.Max(1, Raylib.GetScreenHeight());
            world.Draw(camera);
            Raylib.EndMode3D();
            DrawNight(camera, world);

            // 2D overlay (UI) is drawn after EndMode3D so it sits on top.
            DrawClanLabels(camera, world);
            if (!Detail.FarView)
                DrawStatusBars(camera, world);
            DrawNameTag(camera, world);
            DrawFloatingTexts(camera, world);
            if (playing)
            {
                play.DrawHud(world);
                Raylib.EndDrawing();
                _drawMs += (System.Diagnostics.Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds - _drawMs) * 0.05;
                if (DebugShot.Finished())
                    break;
                world.CommitPendingChanges();
                continue;
            }
            speedDownButton.Draw("-", highlighted: false, disabled: _timeScale <= TimeScaleSteps[0]);
            DrawSpeedLabel(speedLabelBounds, uiScale);
            speedUpButton.Draw("+", highlighted: false, disabled: _timeScale >= TimeScaleSteps[^1]);
            mapButton.Draw("Map", highlighted: false);
            historyButton.Draw("History", highlighted: _showChronicle);
            if (overlayButtons is not null)
                foreach (var (button, label, flag) in overlayButtons)
                    button.Draw(label, highlighted: World.Overlays.HasFlag(flag));
            autoButton?.Draw("Auto", highlighted: director.IsOn);
            newGardenButton?.Draw(_newGardenConfirm > 0f ? "Sure?" : "New", highlighted: _newGardenConfirm > 0f);
            gardenSlotButton?.Draw($"Garden {_gardenSlot}", highlighted: false);
            terrainButton?.Draw(TerrainData.GrowNewGardens ? "Random" : "Fixed", highlighted: TerrainData.GrowNewGardens);
            if (!_showChronicle)
                DrawKinPanel(world); // The History screen covers it (its header names the selected clan).
            followButton?.Draw(followCamera.IsFollowing ? "Following" : "Follow", highlighted: followCamera.IsFollowing);
            controlButton?.Draw("Control", highlighted: false);
            int hudTop = DrawHud(world);
            int captionHeight = director.Caption is null ? 0 : ScaledFontSize(0.55f) + 2 * ((int)(10 * UiScale) + 2) + 6;
            DrawBanner((int)(speedButtonMargin * 2 + speedButtonHeight) + captionHeight);
            if (!_showChronicle)
                DrawDirectorCaption(director, (int)(speedButtonMargin * 2 + speedButtonHeight));
            if (_showChronicle)
                DrawChronicle(world, top: speedButtonMargin * 2 + speedButtonHeight, bottom: hudTop);
            else
                DrawDebugConsole(top: speedButtonMargin * 2 + speedButtonHeight, bottom: hudTop);

            Raylib.EndDrawing();
            _drawMs += (System.Diagnostics.Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds - _drawMs) * 0.05;
            if (DebugShot.Finished())
                break;

            // 4) Deferred spawns/removals: applied once here, after this
            //    frame's Update() and Draw() have both fully run, so no
            //    entity list ever changes size while something is iterating
            //    it (an arrival mid-Colony-update, a kill mid-pounce, etc).
            world.CommitPendingChanges();

            // 5) Autosave, between frames (see World.ToSave).
            autosaveTimer -= Raylib.GetFrameTime();
            if (autosaveTimer <= 0f)
            {
                autosaveTimer = AutosaveInterval;
                SaveSystem.Save(world, GardenPath);
            }
        }

        SaveSystem.Save(world, GardenPath);
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
    public static void RunHeadless(float simulatedSeconds, int? seed, string? loadPath = null, string? savePath = null, int assaultLevel = 0, float assaultAt = 60f, int assaultFailures = 0)
    {
        _isHeadless = true;
        const float step = 1f / 60f;
        const float reportInterval = 30f;

        var rng = seed is { } s ? new Random(s) : new Random();
        World world;
        if (loadPath is null)
        {
            world = new World(new Terrain(ForcedTerrain ?? 0), rng, InitialKinCount);
        }
        else if (SaveSystem.TryLoad(loadPath, rng) is { } loaded)
        {
            world = loaded;
            Console.WriteLine($"Loaded {loadPath}: Year {world.Year}, {world.ElapsedSeconds:0}s in");
        }
        else
        {
            Console.WriteLine($"Couldn't load {loadPath}");
            return;
        }
        // A development aid: GARDEN_START_ERA=0..3 grants every clan that age at the start (as the start-age choice in the menu does).
        if (int.TryParse(Environment.GetEnvironmentVariable("GARDEN_START_ERA"), out int startEra) && loadPath is null)
            world.GrantEra((Era)Math.Clamp(startEra, 0, 3));
        Console.WriteLine($"Garden Guardians headless run: {simulatedSeconds:0}s simulated, seed {(seed?.ToString() ?? "random")}");
        PrintReport(world);

        float endTime = world.ElapsedSeconds + simulatedSeconds;
        float reportTimer = 0f;
        int steps = 0;
        bool assaultStarted = assaultLevel <= 0;
        // A development aid: GARDEN_CONTROL_TEST=<seconds> takes the wheel of a clan member at 60 s for that long (as Swordsman), then gives it back.
        float controlFor = float.TryParse(Environment.GetEnvironmentVariable("GARDEN_CONTROL_TEST"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedControl) ? parsedControl : 0f;
        bool stopAtCastle = Environment.GetEnvironmentVariable("GARDEN_STOP_AT_CASTLE") == "1";
        float? castleSeen = null;
        Bramblekin? controlled = null;
        // A development aid: GARDEN_REALM_TEST=1 invades a vassal village of the first kingdom (once it has stood a minute) and reports how many
        // pledged soldiers of its sister villages come to its defence.
        bool realmTest = Environment.GetEnvironmentVariable("GARDEN_REALM_TEST") == "1";
        Village? realmTarget = null;
        float realmStart = 0f, realmLog = 0f;
        int controlPhase = controlFor > 0f ? 0 : 3;
        while (world.ElapsedSeconds < endTime)
        {
            if (realmTest)
            {
                if (realmTarget is null && world.Realms.FirstOrDefault(k => world.ElapsedSeconds - k.FoundedAt > 60f && world.VillagesOf(k).Any(v => v.Id != k.Capital)) is { } realm &&
                    world.VillagesOf(realm).Where(v => v.Id != realm.Capital).OrderByDescending(v => world.VillagesOf(realm).Where(o => o != v).Sum(o => world.ClansOf(o).SelectMany(c => c.Members).Count(world.IsPledged))).First() is { } target)
                {
                    realmTarget = target;
                    realmStart = world.ElapsedSeconds;
                    world.StartVillageInvasion(target, 8);
                    Console.WriteLine($"[REALM-TEST] t={realmStart:0}s: invading {target.Name} of {realm.Name}; pledged elsewhere: {world.VillagesOf(realm).Where(o => o != target).Sum(o => world.ClansOf(o).SelectMany(c => c.Members).Count(world.IsPledged))}");
                }
                else if (realmTarget is not null && world.ElapsedSeconds >= realmLog && world.ElapsedSeconds < realmStart + 120f)
                {
                    realmLog = world.ElapsedSeconds + 10f;
                    Kingdom? kingdom = world.RealmOf(realmTarget);
                    var sisters = kingdom is null ? new List<Village>() : world.VillagesOf(kingdom).Where(o => o != realmTarget).ToList();
                    var pledged = sisters.SelectMany(o => world.ClansOf(o).SelectMany(c => c.Members)).Where(m => !m.IsDead && world.IsPledged(m)).ToList();
                    int near = pledged.Count(m => GroundMover.HorizontalDistance(m.Position, realmTarget.Centre) < 25f);
                    Console.WriteLine($"[REALM-TEST] +{world.ElapsedSeconds - realmStart:0}s: spiders {world.InvadersAt(realmTarget)}, pledged {pledged.Count}, within 25 m of the village {near}");
                }
            }
            if (controlPhase == 0 && world.ElapsedSeconds >= 60f &&
                world.Colony.FirstOrDefault(k => !k.IsDead && !k.IsYoung && world.GroupOf(k) is { } g && g.Members.Count >= 2) is { } pick)
            {
                controlPhase = 1;
                controlled = pick;
                Guid clan = pick.GroupId!.Value;
                pick.CyclePlayerJob();
                pick.CyclePlayerJob(); // Hunter, then Swordsman.
                pick.SetPlayerControlled(true, world);
                Console.WriteLine($"[CONTROL] {pick.Name} taken at {world.ElapsedSeconds:0}s: clan {pick.GroupId?.ToString() ?? "none"} (was {clan}), job {pick.Job}, village job {pick.VillageJob}");
            }
            else if (controlPhase == 1 && controlled is not null && world.ElapsedSeconds >= 60f + controlFor)
            {
                controlPhase = 2;
                world.CommitPendingChanges();
                Console.WriteLine($"[CONTROL] {controlled.Name} before release: dead {controlled.IsDead}, clan {controlled.GroupId?.ToString() ?? "none"}, clan still stands {controlled.AwayGroupId is { } a && world.GroupExists(a)}");
                controlled.SetPlayerControlled(false, world);
                Console.WriteLine($"[CONTROL] {controlled.Name} released: clan {controlled.GroupId?.ToString() ?? "none"}, job {controlled.Job}");
            }
            if (!assaultStarted && world.ElapsedSeconds >= assaultAt)
            {
                assaultStarted = true;
                World.AssaultsEnabled = false; // Only the test one.
                world.StartTestAssault(assaultLevel, assaultFailures);
            }
            world.Update(step);
            world.CommitPendingChanges();
            Prof.Mark("Commit");
            // A development aid: GARDEN_STOP_AT_CASTLE=1 ends the run (and saves, with --save) a minute after the first castle is raised.
            if (stopAtCastle && world.CastlesRaised > 0)
            {
                castleSeen ??= world.ElapsedSeconds;
                if (world.ElapsedSeconds - castleSeen > 60f)
                    break;
            }
            steps++;

            reportTimer += step;
            if (reportTimer >= reportInterval)
            {
                reportTimer -= reportInterval;
                PrintReport(world);
            }
        }

        Console.WriteLine($"[REALMS] kingdoms now {world.Realms.Count}, founded {world.KingdomsFounded}, kings crowned {world.KingsCrowned}, villages named {world.VillagesNamed}, tribute {world.Realms.Sum(k => k.TributePaid)}, pledged {world.Realms.Sum(k => k.Pledged)}");
        Console.WriteLine($"[CASTLE] raised {world.CastlesRaised}; {world.DescribeCastleGround()}");
        Prof.Report(steps);
        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        PrintReport(world);
        Console.WriteLine(
            $"Food eaten {world.FoodEaten}, shared {world.FoodShared}, stolen {world.Thefts}; " +
            $"alliances {world.AlliancesFormed}; grubs hunted {world.GrubsKilled}, beetles {world.BeetlesKilled}, hornets swatted {world.HornetsKilled}, spiders slain {world.SpidersKilled}.");
        Console.WriteLine(
            $"Homes: {world.TentsBuilt} tents and {world.HousesBuilt} houses built, {world.BurrowsDug} burrows dug ({world.Shelters.Count(s => s.IsBuilt && s.IsBurrow)} lived in at the end), {world.SheltersCollapsed} collapsed; " +
            $"{world.StoreMeals} meals eaten from stores, {world.StoreRaids} store raids; " +
            $"{world.VillagesFounded} villages founded, {world.Buddings} daughter groups budded off.");
        Console.WriteLine(
            $"Neighbours: {world.AlliancesMade} alliances made, {world.WarsDeclared} wars declared, {world.PeacesMade} peaces made; " +
            $"{world.AidSent} aid shipments ({world.FoodAided} food delivered, {world.ErrandsLost} sacks lost on the way), " +
            $"{world.HelpersHired} helpers hired ({world.LabourFoodPaid} food paid), {world.MaterialsTraded} stones and branches traded ({world.HaulFoodPaid} food paid), " +
            $"{world.WarRaids} pieces carried off in war raids; " +
            $"at the end {world.CurrentAlliances} alliances and {world.CurrentWars} wars.");
        Console.WriteLine(
            $"Weather: {world.BountifulSeasons} bountiful seasons, {world.Droughts} droughts, {world.HarshWinters} harsh winters, {world.Storms} storms, " +
            $"{world.Floods} floods ({world.HomesFlooded} homes flooded, {world.FoodWashedAway} food washed away).");
        Console.WriteLine(
            $"Ants: {(world.Anthill is { } hill ? $"a level {hill.Level} hill with {hill.Stock} food" : "none yet")}; {world.AntThefts} food stolen from stores, {world.AntsKilled} thief ants swatted. " +
            $"Assaults on the hill: {world.AssaultsLaunched} sent, {world.AssaultsWon} won ({world.EggsEaten} eggs eaten, best level beaten {world.HighestHillLevelBeaten}), {world.AssaultsCalledOff} called off, " +
            $"{world.AssaultsLost} lost; {world.GuardsSlain} guards slain, {world.Deserters} soldiers ran away. " +
            $"The oak dropped {world.AcornsFallen} acorns. Ways found round the pond: {WaterMap.RoutesFound}.");
        Console.WriteLine(
            $"Eras: {world.Groups.Count(g => World.EraOf(g) == Era.StoneAge)} clans in the Stone Age, {world.Groups.Count(g => World.EraOf(g) == Era.FarmingAge)} Farming, {world.Groups.Count(g => World.EraOf(g) == Era.VillageAge)} Village, {world.Groups.Count(g => World.EraOf(g) == Era.KingdomAge)} Kingdom; ages reached: {world.EraTransitions[1]} Farming, {world.EraTransitions[2]} Village, {world.EraTransitions[3]} Kingdom. Paths: {world.PathCells} worn cells, {world.RoadCells} paved. {world.Groups.Count(g => World.Knows(g, Craft.Tools))} clans have tools, {world.Groups.Count(g => World.Knows(g, Craft.Roads))} roads, {world.Groups.Count(g => World.Knows(g, Craft.Writing))} writing ({world.RunesCarved} deeds carved), {world.Groups.Count(g => World.Knows(g, Craft.Watchtowers))} watchtowers ({world.HornsSounded} horns), {world.Groups.Count(g => World.Knows(g, Craft.Calendar))} calendars ({world.SolsticesKept} solstices), {world.Groups.Count(g => World.Knows(g, Craft.Medicine))} medicine ({world.TradeInfections} trade infections), {world.Groups.Count(g => world.IsKingdom(g))} kingdoms ({world.FealtiesSworn} fealties, {world.VassalsFreed} vassals freed), {world.Groups.Count(g => World.Knows(g, Craft.Exploration))} exploring ({world.CellsMapped} cells mapped by scouts, {world.FarShoresFound} far shores reached), {world.Groups.Count(g => World.Knows(g, Craft.Rafts))} rafts ({world.RaftCrossings} crossings, {world.RaftMishaps} capsized).\n" +
            $"Crafts: {world.CraftsDiscovered} worked out, {world.CraftsTaught} taught; at the end {world.Groups.Count(g => World.Knows(g, Craft.Granary))} clans have granaries, " +
            $"{world.Groups.Count(g => World.Knows(g, Craft.Spears))} spears, {world.Groups.Count(g => World.Knows(g, Craft.Palisade))} palisades, " +
            $"{world.Groups.Count(g => World.Knows(g, Craft.Grain))} grain, {world.Groups.Count(g => World.Knows(g, Craft.Mushrooms))} mushrooms, " +
            $"{world.Groups.Count(g => World.Knows(g, Craft.Cress))} cress, {world.Groups.Count(g => World.Knows(g, Craft.Fishing))} fishing, " +
            $"{world.Groups.Count(g => World.Knows(g, Craft.Stonework))} stonework, {world.Groups.Count(g => World.Knows(g, Craft.Slings))} slings. " +
            $"Sickness: {world.SicknessCases} fell ill, {world.DeathsBySickness} died of it; " +
            $"healers tended {world.Tendings} times ({world.HealthTended} health restored, {world.SicknessEased:0}s of sickness eased), " +
            $"{world.Groups.Count(g => World.Knows(g, Craft.Herbalism))} clans know herb-lore.");
        Console.WriteLine(
            $"Culture: {world.Groups.Count(g => g.Culture.Leading == Tradition.Warlike)} warlike, {world.Groups.Count(g => g.Culture.Leading == Tradition.Hunting)} hunting and " +
            $"{world.Groups.Count(g => g.Culture.Leading == Tradition.Farming)} farming clans of {world.Groups.Count} at the end.");
        Console.WriteLine(
            $"War outcomes: {world.Conquests} conquests, {world.TributesAgreed} tribute peaces ({world.TributeDelivered} food delivered, {world.TributesMissed} payments missed).");
        Console.WriteLine(
            $"Farming: worked out {world.FarmingDiscoveries} times, taught {world.FarmingTaught} times; {world.BushesPlanted} crops planted, " +
            $"{world.FruitHarvested} pieces picked; at the end {world.Groups.Count(World.KnowsFarming)} groups farm {world.Crops.Count(b => b.GroupId is not null)} crops " +
            $"({string.Join(", ", Enum.GetValues<CropKind>().Select(k => $"{world.Crops.Count(c => c.Kind == k)} {k.ToString().ToLowerInvariant()}"))}; " +
            $"{world.Crops.Count(b => b.GroupId is null)} wild, {world.Crops.Count(c => c.IsWatered)} watered). " +
            $"Seed corn: {world.SeedCornKept} kept, {world.SeedCornSown} sown, {world.SeedCornEaten} eaten in famine; " +
            $"at the end {world.Groups.Sum(g => g.SeedCorn)} held by {world.Groups.Count(g => g.SeedCorn > 0)} clans.");
        Console.WriteLine(
            $"Food eaten or stored, by kind: {string.Join(", ", Enum.GetValues<FoodShardKind>().Select(k => $"{k.ToString().ToLowerInvariant()} {world.FoodTaken(k)}"))}; " +
            $"{world.FishCaught} fish caught.");
        Console.WriteLine(
            $"Water: {world.DrinksAtPond} drinks at the pond (a {(world.DrinksAtPond > 0 ? world.WaterTrekMeters / world.DrinksAtPond : 0):0}m walk from home on average), " +
            $"{world.CreekDrinks} of them at the creek, {world.CisternDrinks} from cisterns ({world.CupfulsCarried} cupfuls carried home), {world.WellDrinks} from wells ({world.WellsDug} dug, " +
            $"{world.Wells.Count(w => !w.IsDug)} being dug); {world.DeathsByThirst} died of thirst; " +
            $"at the end {world.Groups.Count(g => World.Knows(g, Craft.Cisterns))} clans have cisterns, and the average home is " +
            $"{(world.Shelters.Count(s => s.IsBuilt) > 0 ? world.Shelters.Where(s => s.IsBuilt).Average(s => WaterMap.UsualDistanceToWater(s.Position.X, s.Position.Z)) : 0):0}m from water.");
        Console.WriteLine("  Clans by distance to water: " + string.Join(", ", world.Groups
            .Where(g => g.Home is not null)
            .Select(g => (Group: g, Water: WaterMap.UsualDistanceToWater(g.Home!.Position.X, g.Home.Position.Z)))
            .OrderBy(c => c.Water)
            .Select(c => $"{c.Group.Members.Count} kin at {c.Water:0}m")));
        Console.WriteLine(
            $"Stones and branches: {world.StonesLaid} stones laid ({world.FootingsLaid} footings finished), {world.StakesSet} branches staked " +
            $"({world.PalisadesRaised} palisades raised); at the end {world.Shelters.Count(s => s.HasFooting)} homes on footings, " +
            $"{world.Shelters.Count(s => s.HasPalisade)} palisaded; {world.Materials.Count(m => m is { IsActive: true, Kind: MaterialKind.Stone })} stones and " +
            $"{world.Materials.Count(m => m is { IsActive: true, Kind: MaterialKind.Branch })} branches lying about.");
        Console.WriteLine(
            $"Slings: {world.PebblesLoosed} pebbles loosed, {world.PebbleHits} hits, {world.SlingKills} kills; {world.HornetsKilled} hornets swatted or slung in all.");
        Console.WriteLine(
            $"Hearths: {world.CookedMeals} meals eaten cooked, {world.TwigsBurned} twigs burned; at the end {world.Groups.Count(g => World.Knows(g, Craft.Hearth))} clans keep a hearth, " +
            $"{world.Shelters.Count(s => s.IsHearthLit)} lit. " +
            $"Snares: {world.SnareCatches} grubs caught, {world.SnaresReset} snares set again; at the end {world.Groups.Count(g => World.Knows(g, Craft.Snares))} clans snare, " +
            $"{world.Snares.Count(s => s.IsSet)} of {world.Snares.Count} snares set.");
        Console.WriteLine(
            $"Herding: {world.PensFenced} pens fenced, {world.HoneydewDrops} drops of honeydew, {world.AphidsBred} aphids bred; lost {world.AphidsLostToAnts} to ants, " +
            $"{world.AphidsLostToSpider} to the spider, {world.AphidsRustled} rustled in raids; at the end {world.Groups.Count(g => World.Knows(g, Craft.Herding))} clans herd, " +
            $"{world.Pens.Sum(p => p.Aphids)} aphids in {world.Pens.Count(p => p.Aphids > 0)} pens.");
        Console.WriteLine(
            $"Feasts: {world.FeastsHeld} harvest feasts held, {world.FeastGuests} guests from other clans came, {world.FeastCouples} couples met across clans at one, " +
            $"{world.FeastAlliances} alliances made over one.");
        Console.WriteLine(
            $"Beliefs: {world.BeliefsFound} taken up ({world.Conversions} at a feast), {world.ShrinesRaised} shrines raised, {world.Schisms} schisms; at the end " +
            string.Join(", ", Enum.GetValues<Belief>().Where(b => b != Belief.None).Select(b => $"{world.Groups.Count(g => g.Belief == b)} revere {World.Describe(b)}")) +
            $", {world.Groups.Count(g => g.Belief == Belief.None)} nothing.");
        Console.WriteLine(
            $"Champions: {world.ChampionBouts} contests, {world.FeudsSettled} feuds and {world.WarsSettledByChampions} wars settled by them; " +
            $"{world.Groups.Count(g => World.Knows(g, Craft.Shields))} clans carry shields at the end; the greatest champion alive has won " +
            $"{(world.Colony.Count(k => !k.IsDead) > 0 ? world.Colony.Where(k => !k.IsDead).Max(k => k.ChampionWins) : 0)}.");
        Console.WriteLine(
            $"Nights: {world.NightsPassed} nights; {world.WatchesPosted} watches posted, {world.AlarmsRaised} alarms raised, {world.NightRaids} raids set out in the dark.");
        Console.WriteLine(
            $"Building: a Tent takes {world.AverageTentBuildSeconds:0}s on average, a House upgrade {world.AverageHouseUpgradeSeconds:0}s; " +
            $"{world.StagesOlderThan(600f)} of {world.Shelters.Count(s => !s.IsBuilt || s.IsUpgrading)} construction stages under way have stalled over 10 min.");
        PrintSurvivalTrend(world);
        PrintJobTrend(world);
        PrintLeadership(world);
        PrintRebellion(world);
        PrintLineage(world);
        PrintChronicle(world);

        if (savePath is not null)
            Console.WriteLine(SaveSystem.Save(world, savePath) ? $"Saved to {savePath}" : $"Couldn't save to {savePath}");
    }

    /// <summary>Headless summary: how often followers rebelled, and how the Independents who left are faring.</summary>
    private static void PrintRebellion(World world)
    {
        List<Bramblekin> independents = world.Colony.Where(b => !b.IsDead && b.Status == SurvivalStatus.Independent).ToList();
        Console.WriteLine(
            $"Rebellion: {world.Departures} left, {world.Splinters} splinter groups, {world.Coups} coups " +
            $"({world.FailedChallenges} failed challenges), {world.Exiles} exiles; {world.Rejoins} independents later joined another group.");
        Console.WriteLine(
            $"Succession: {world.HeirsNamed} heirs named, {world.Successions} Leaders succeeded by their heir, {world.LeaderlessScrambles} lost with none named. " +
            $"Plots: {world.PlotsHatched} hatched, {world.PlotsCarried} carried out, {world.PlotsUncovered} uncovered, {world.PlotsAbandoned} given up. " +
            $"Councils: average sway {world.AverageCouncilSway:P0}; talked {world.SharingOverruled} Leaders out of eating first, held {world.WarsHeldBack} back from war.");
        Console.WriteLine(
            $"  {independents.Count} independents alive at the end, {independents.Count(b => b.Home is { IsBuilt: true })} of them with a home of their own.");
    }

    /// <summary>Headless summary: how groups under each style of Leader spent their time, and how often their Leader ate first.</summary>
    private static void PrintLeadership(World world)
    {
        Console.WriteLine("Leadership (share of group-time on each goal, by Leader style):");
        foreach (LeaderStyle style in Enum.GetValues<LeaderStyle>())
        {
            double hours = world.GroupHoursLedBy(style);
            if (hours <= 0)
            {
                Console.WriteLine($"  {style,-9} -");
                continue;
            }
            string goals = string.Join("  ", Enum.GetValues<GroupGoal>().Select(g => $"{g} {world.GoalShare(style, g),4:P0}"));
            Console.WriteLine($"  {style,-9} {goals}   leader-first {world.LeaderFirstShare(style),4:P0}   ({hours:0.0} group-hours)");
        }
    }

    /// <summary>Headless summary: deaths per kin-hour lived in each social status — the survival trend of an individual by how it lives.</summary>
    private static void PrintSurvivalTrend(World world)
    {
        Console.WriteLine("Survival trend (per kin-hour lived in each status):");
        foreach (SurvivalStatus status in Enum.GetValues<SurvivalStatus>())
        {
            double hours = world.KinHoursIn(status);
            string rate = hours > 0 ? $"{world.DeathRatePerKinHour(status),5:0.0}/h" : "    -";
            string meat = hours > 0 ? $"{world.MeatHuntedPerKinHour(status),5:0.0}/h" : "    -";
            Console.WriteLine(
                $"  {status,-12} deaths {rate}   meat hunted {meat}   ({world.DeathsIn(status)} deaths over {hours:0.0} kin-hours: " +
                $"{world.DeathsIn(status, DeathCause.Starvation)} starved, {world.DeathsIn(status, DeathCause.Predator)} to predators, " +
                $"{world.DeathsIn(status, DeathCause.Kin)} to kin, {world.DeathsIn(status, DeathCause.OldAge)} of old age, {world.DeathsIn(status, DeathCause.Sickness)} of sickness, " +
                $"{world.DeathsIn(status, DeathCause.Thirst)} of thirst)");
        }
    }

    /// <summary>Headless summary: grown kin's deaths per kin-hour by job, and how many died to predators and to other kin: how the fighting jobs fare.</summary>
    private static void PrintJobTrend(World world)
    {
        Console.WriteLine("Job trend (grown kin, per kin-hour in each job):");
        foreach (KinJob job in Enum.GetValues<KinJob>())
        {
            double hours = world.KinHoursAs(job);
            if (hours <= 0.05)
                continue;
            Console.WriteLine(
                $"  JOB {job,-11} deaths {world.DeathsAs(job) / hours,5:0.0}/h   predators {world.DeathsAs(job, DeathCause.Predator) / hours,5:0.0}/h   kin {world.DeathsAs(job, DeathCause.Kin) / hours,5:0.0}/h   " +
                $"({world.DeathsAs(job)} deaths over {hours:0.0} kin-hours)");
        }
    }

    /// <summary>One line of headless-mode population stats — see <see cref="RunHeadless"/>.</summary>
    private static void PrintReport(World world)
    {
        List<Bramblekin> living = world.Colony.Where(b => !b.IsDead).ToList();
        float averageHunger = living.Count > 0 ? living.Average(b => b.Hunger) : 0f;
        int solitary = living.Count(b => b.GroupId is null);
        int largestGroup = world.Groups.Count > 0 ? world.Groups.Max(g => g.Members.Count) : 0;
        int villages = world.Groups.Count(g => g.Annexes.Count > 0);
        int tents = world.Shelters.Count(s => s.IsBuilt && s.Tier == ShelterTier.Tent);
        int houses = world.Shelters.Count(s => s.Tier == ShelterTier.House);
        int stored = world.Shelters.Sum(s => s.StoredFood);
        Console.WriteLine(
            $"[t={world.ElapsedSeconds,6:0}s Y{world.Year} {world.CurrentSeason,-6}] kin {living.Count,3} (solitary {solitary}, groups {world.Groups.Count}, largest {largestGroup}, villages {villages}, crops {world.Crops.Count}, allies {world.CurrentAlliances}, wars {world.CurrentWars}) " +
            $"avg hunger {averageHunger,5:0.0}  food on map {world.LooseFoodCount,3}, stored {stored,3}  tents {tents} houses {houses}  " +
            $"arrived {world.Arrivals} born {world.Births}  died: starved {world.DeathsByStarvation}, predators {world.DeathsByPredator}, kin {world.DeathsByKin}, old age {world.DeathsByOldAge}, sickness {world.DeathsBySickness}, thirst {world.DeathsByThirst}");
    }

    /// <summary>Headless summary: births, how far the generations have come, and whether the traits of the living have drifted from the 0.5 average a newcomer brings.</summary>
    private static void PrintLineage(World world)
    {
        List<Bramblekin> living = world.Colony.Where(b => !b.IsDead).ToList();
        int bornHere = living.Count(b => b.Generation > 0);
        Console.WriteLine(
            $"Lineage: {world.Births} births, generations up to {world.MaxGeneration}; of the {living.Count} alive, {bornHere} were born here " +
            $"({living.Count(b => b.Sex == Sex.Female)} female, {living.Count(b => b.Sex == Sex.Male)} male, {living.Count(b => b.IsElder)} elders).");
        Console.WriteLine(
            $"Families: {world.CouplesFormed} couples formed, {world.LivingCouples} together now, {world.Separations} separated; " +
            $"{world.Adoptions} orphans adopted, {world.OrphansTakenIn} lone young taken in; " +
            $"{world.CourtshipGifts} courtship gifts ({world.GiftsWon} won a partner); " +
            $"{world.DeathsByOldAge} died of old age" +
            (living.Count > 0 ? $"; the oldest alive is {living.Max(b => b.AgeInYears):0.0} years." : "."));
        if (living.Count > 0)
        {
            Console.WriteLine(
                $"  Traits of the living (a newcomer averages 0.50): aggression {living.Average(b => b.Personality.Aggression):0.00}, " +
                $"sociability {living.Average(b => b.Personality.Sociability):0.00}, intelligence {living.Average(b => b.Personality.Intelligence):0.00}, " +
                $"rebellion {living.Average(b => b.Personality.Rebelliousness):0.00}, persuasion {living.Average(b => b.Personality.Persuasiveness):0.00}, " +
                $"courage {living.Average(b => b.Personality.Courage):0.00}, diligence {living.Average(b => b.Personality.Diligence):0.00}");
            Console.WriteLine(
                "  Skills: masters made - " + string.Join(", ", Enum.GetValues<Skill>().Select(s => $"{world.Masteries(s)} {Bramblekin.TradeNoun(s)}s")) +
                "; best among the living - " + string.Join(", ", Enum.GetValues<Skill>().Select(s => $"{s.ToString().ToLowerInvariant()} {living.Max(b => b.SkillAt(s)):0.00}")) +
                $"; {world.SkilledHarvests} extra pieces from skilled harvests.");
        }
    }

    /// <summary>One line describing a Bramblekin's home for the Kin Inspector.</summary>
    private static string DescribeHome(Bramblekin kin) => kin.Home switch
    {
        null => "Home: none",
        { IsBuilt: false } site => $"Home: building a Tent ({site.TwigsDelivered}/{site.TwigsNeeded} twigs)",
        { IsUpgrading: true } home => $"Home: Tent -> House ({home.TwigsDelivered}/{home.TwigsNeeded}), store {home.StoredFood}/{home.StoreCapacity}",
        { } home => $"Home: {home.Tier}{(home.GroupId is null ? "" : " (group)")}{HomeWorks(home)}, store {home.StoredFood}/{home.StoreCapacity}",
    };

    /// <summary>", stone footing, palisade 2/3" — what's been built onto a home beyond its walls.</summary>
    private static string HomeWorks(Shelter home) =>
        (home.HasFooting ? ", stone footing" : home.StonesLaid > 0 ? $", footing {home.StonesLaid}/{Shelter.FootingStoneCost}" : "") +
        (home.HasPalisade ? ", palisade" : home.StakesSet > 0 ? $", palisade {home.StakesSet}/{Shelter.PalisadeStakeCost}" : "") +
        (home.HasHearth ? home.IsHearthLit ? ", hearth lit" : ", hearth cold" : "");

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
    /// <summary>What the Director is showing, in a strip under the top buttons.</summary>
    private static void DrawDirectorCaption(Director director, int top)
    {
        if (director.Caption is not { } caption)
            return;
        int size = ScaledFontSize(0.55f);
        int pad = (int)(10 * UiScale) + 2;
        string text = Fit(caption, size, (int)(Raylib.GetScreenWidth() * 0.7f));
        int width = Raylib.MeasureText(text, size) + pad * 2;
        int x = (Raylib.GetScreenWidth() - width) / 2;
        Raylib.DrawRectangle(x, top, width, size + pad * 2, BannerFill);
        Raylib.DrawText(text, x + pad, top + pad, size, PanelInk);
    }

    /// <summary>At its darkest, night lays this much shade over the garden.</summary>
    private const float NightShade = 0.55f;

    /// <summary>Night: the garden darkened, then whatever shines in the dark drawn over it (see World.DrawNightLights).</summary>
    private static void DrawNight(Camera3D camera, World world)
    {
        float darkness = world.Darkness;
        if (darkness <= 0f)
            return;
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(6, 10, 38, (int)(255 * NightShade * darkness)));
        Raylib.BeginMode3D(camera);
        world.DrawNightLights(camera);
        Raylib.EndMode3D();
    }

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
    private static readonly Color ThirstBarColor = new(60, 130, 220, 255);

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
            float width = BarWidth(camera, barAnchor, Bramblekin.BodyRadius * 2f);
            if (BodyPixels(camera, barAnchor, Bramblekin.BodyRadius * 2f) < HideBarsBelowPixels)
                continue; // too far to read a bar: leave it out
            if (b.Health < b.HealthCap)
                DrawBar(camera, barAnchor, 0f, width, (float)b.Health / b.HealthCap, Color.Green);
            float below = width * 0.21f;
            if (b.IsHungry)
            {
                DrawBar(camera, barAnchor, below, width, 1f - b.Hunger / Bramblekin.MaxHunger, HungerBarColor);
                below += width * 0.21f;
            }
            if (b.IsThirsty)
                DrawBar(camera, barAnchor, below, width, 1f - b.Thirst / Bramblekin.MaxThirst, ThirstBarColor);
        }

        foreach (InvaderSpider invader in world.Invaders)
        {
            if (invader.IsDead || invader.Health >= InvaderSpider.MaxHealth)
                continue;
            Vector3 invaderAnchor = invader.Position + new Vector3(0, InvaderSpider.BodyRadius * 2f + 0.2f, 0);
            DrawBar(camera, invaderAnchor, 0f, BarWidth(camera, invaderAnchor, InvaderSpider.BodyRadius * 2f), (float)invader.Health / InvaderSpider.MaxHealth, Color.Green);
        }

        foreach (StagBeetle beetle in world.Beetles)
        {
            if (beetle.IsDead || beetle.Health >= StagBeetle.MaxHealth || !IsPointOnScreen(camera, beetle.Position))
                continue;
            Vector3 anchor = beetle.Position + new Vector3(0, StagBeetle.BodyRadius * 2f + 0.25f, 0);
            DrawBar(camera, anchor, 0f, BarWidth(camera, anchor, StagBeetle.BodyRadius * 2f), (float)beetle.Health / StagBeetle.MaxHealth, Color.Green);
        }
        foreach (Ant ant in world.Ants)
        {
            if (ant.IsDead || ant.Health >= Ant.MaxHealth || !IsPointOnScreen(camera, ant.Position))
                continue;
            Vector3 anchor = ant.Position + new Vector3(0, Ant.BodyRadius * 2f + 0.2f, 0);
            DrawBar(camera, anchor, 0f, BarWidth(camera, anchor, Ant.BodyRadius * 2f), (float)ant.Health / Ant.MaxHealth, Color.Green);
        }
        foreach (HillGuard guard in world.HillGuards)
        {
            if (guard.IsDead || guard.IsHidden || guard.Health >= HillGuard.MaxHealth || !IsPointOnScreen(camera, guard.Position))
                continue;
            Vector3 anchor = guard.Position + new Vector3(0, Ant.BodyRadius * 2f + 0.2f, 0);
            DrawBar(camera, anchor, 0f, BarWidth(camera, anchor, Ant.BodyRadius * 2f), (float)guard.Health / HillGuard.MaxHealth, Color.Green);
        }

        if (world.Spider is { IsDead: false } spider && spider.Health < WolfSpider.MaxHealth)
        {
            Vector3 anchor = spider.Position + new Vector3(0, WolfSpider.BodyRadius * 2f + 0.3f, 0);
            DrawBar(camera, anchor, 0f, BarWidth(camera, anchor, WolfSpider.BodyRadius * 2f), (float)spider.Health / WolfSpider.MaxHealth, Color.Green);
        }
    }

    /// <summary>Below this many pixels across, a Bramblekin's status bars are left out.</summary>
    private const float HideBarsBelowPixels = 9f;

    /// <summary>How wide (px) something <paramref name="bodyWidth"/> metres across looks at <paramref name="anchor"/>.</summary>
    private static float BodyPixels(Camera3D camera, Vector3 anchor, float bodyWidth)
    {
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Vector3 right = Vector3.Cross(forward, camera.Up);
        right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.UnitX;
        return Vector2.Distance(Raylib.GetWorldToScreen(anchor, camera), Raylib.GetWorldToScreen(anchor + right * bodyWidth, camera));
    }

    /// <summary>A status bar is never narrower than this (px, at the reference screen width)…</summary>
    private const float MinBarWidth = 34f;

    /// <summary>…nor wider than this…</summary>
    private const float MaxBarWidth = 150f;

    /// <summary>…and in between it's this many times as wide as the creature looks on screen — so it grows as the camera zooms in.</summary>
    private const float BarWidthPerBody = 2.4f;

    /// <summary>
    /// How wide a status bar over something <paramref name="bodyWidth"/>
    /// meters across at <paramref name="anchor"/> should be: in step with
    /// how big it looks from where the camera is (so zooming in makes the
    /// bars easy to read), between <see cref="MinBarWidth"/> and
    /// <see cref="MaxBarWidth"/> scaled to the screen.
    /// </summary>
    private static float BarWidth(Camera3D camera, Vector3 anchor, float bodyWidth)
    {
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Vector3 right = Vector3.Cross(forward, camera.Up);
        right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.UnitX;
        float onScreen = Vector2.Distance(Raylib.GetWorldToScreen(anchor, camera), Raylib.GetWorldToScreen(anchor + right * bodyWidth, camera));
        float scale = MathF.Max(1f, UiScale);
        return Math.Clamp(onScreen * BarWidthPerBody, MinBarWidth * scale, MaxBarWidth * scale);
    }

    /// <summary>Basic bounds check: true unless <paramref name="worldPosition"/> projects to a screen point entirely outside the camera's current viewport — used to skip status-bar/UI draw calls for off-screen entities.</summary>
    private static bool IsPointOnScreen(Camera3D camera, Vector3 worldPosition)
    {
        const float margin = 40f;
        Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);
        return screen.X >= -margin && screen.X <= Raylib.GetScreenWidth() + margin &&
               screen.Y >= -margin && screen.Y <= Raylib.GetScreenHeight() + margin;
    }

    /// <summary>A red-background bar <paramref name="width"/> pixels wide (and a seventh as tall) filled to <paramref name="fraction"/> at <paramref name="worldPosition"/>'s projected screen point, <paramref name="yOffset"/> pixels below it.</summary>
    private static void DrawBar(Camera3D camera, Vector3 worldPosition, float yOffset, float width, float fraction, Color fillColor)
    {
        Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);
        float height = MathF.Max(5f, width / 7f);
        var back = new Rectangle(screen.X - width / 2f, screen.Y - height / 2f + yOffset, width, height);
        Raylib.DrawRectangleRec(back, Color.Red);
        var fill = back with { Width = back.Width * Math.Clamp(fraction, 0f, 1f) };
        Raylib.DrawRectangleRec(fill, fillColor);
        Raylib.DrawRectangleLinesEx(back, MathF.Max(1f, height / 6f), Color.Black);
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
    /// <see cref="World.TrySelectAt"/>) vitals, Personality, group role
    /// and relationships, top right. Replaces the old per-faction ledger;
    /// with nothing selected it's just a one-line hint.
    /// </summary>
    private static void DrawKinPanel(World world)
    {
        var (fontSize, lineHeight, topPadding, margin, inset) = KinPanelMetrics();

        Bramblekin? kin = world.SelectedKin;
        if ((kin is null || kin.IsDead) && world.SelectedClan is { } clan)
        {
            DrawClanCard(world, clan);
            return;
        }
        if (kin is null || kin.IsDead)
        {
            const string hint = "Tap a Bramblekin or a home";
            int hintWidth = Raylib.MeasureText(hint, fontSize);
            int hintX = Raylib.GetScreenWidth() - hintWidth - margin;
            Raylib.DrawRectangle(hintX - inset, topPadding, hintWidth + inset * 2, fontSize + inset * 2, PanelFill);
            Raylib.DrawText(hint, hintX, topPadding + inset, fontSize, PanelInk);
            return;
        }

        KinGroup? group = world.GroupOf(kin);
        Color fill = group is null ? PanelFill : BlendToward(PanelFill, group.Color, 0.35f);
        Color ink = group is null ? PanelInk : BlendToward(PanelInk, group.Color, 0.35f);
        List<(string Text, Color Color)> lines = KinPanelLines(world, kin, ink);

        // Sized to its widest line — but never over the buttons along the top (a longer line is cut short).
        int width = Math.Min(lines.Max(line => Raylib.MeasureText(line.Text, fontSize)), KinPanelMaxWidth(margin, inset));
        int height = KinPanelHeight(lines.Count);
        int x = Raylib.GetScreenWidth() - width - margin;
        Raylib.DrawRectangle(x - inset, topPadding, width + inset * 2, height, fill);
        Raylib.DrawRectangleLines(x - inset, topPadding, width + inset * 2, height, ink);
        for (int i = 0; i < lines.Count; i++)
            Raylib.DrawText(Fit(lines[i].Text, fontSize, width), x, topPadding + inset + lineHeight * i, fontSize, lines[i].Color);
    }

    /// <summary>The clan card, in the Kin Inspector's place, for a clan picked by tapping one of its homes.</summary>
    private static void DrawClanCard(World world, KinGroup clan)
    {
        var (fontSize, lineHeight, topPadding, margin, inset) = KinPanelMetrics();
        Color fill = BlendToward(PanelFill, clan.Color, 0.35f);
        Color ink = BlendToward(PanelInk, clan.Color, 0.35f);
        var lines = new List<string> { ClanHeading(clan) };
        lines.AddRange(ClanLines(world, clan));
        lines.Add("History shows its story");

        int width = Math.Min(lines.Max(line => Raylib.MeasureText(line, fontSize)), KinPanelMaxWidth(margin, inset));
        int height = KinPanelHeight(lines.Count);
        int x = Raylib.GetScreenWidth() - width - margin;
        Raylib.DrawRectangle(x - inset, topPadding, width + inset * 2, height, fill);
        Raylib.DrawRectangleLines(x - inset, topPadding, width + inset * 2, height, ink);
        for (int i = 0; i < lines.Count; i++)
            Raylib.DrawText(Fit(lines[i], fontSize, width), x, topPadding + inset + lineHeight * i, fontSize, i == lines.Count - 1 ? ink with { A = 160 } : ink);
    }

    /// <summary>
    /// Every village's clan name (and head count) floating over its main
    /// home, on a tag edged in the clan's colour — so the clans named in
    /// the chronicle can be found on the map.
    /// </summary>
    private static void DrawClanLabels(Camera3D camera, World world)
    {
        int fontSize = ScaledFontSize(0.42f);
        KinGroup? highlighted = world.SelectedKin is { IsDead: false } kin ? world.GroupOf(kin) : world.SelectedClan;
        _clanLabelBounds.Clear();
        foreach (KinGroup group in world.Groups)
        {
            if (group.Name is null || group.Home is not { IsCollapsed: false } home)
                continue;
            Vector3 anchor = home.Position + new Vector3(0f, 2.4f, 0f);
            if (!IsPointOnScreen(camera, anchor))
                continue;
            Vector2 screen = Raylib.GetWorldToScreen(anchor, camera);
            string text = $"{group.Name} ({group.Members.Count})";
            int width = Raylib.MeasureText(text, fontSize);
            int x = (int)(screen.X - width / 2f), y = (int)(screen.Y - fontSize);
            byte alpha = group == highlighted ? (byte)240 : (byte)190;
            int pad = fontSize / 2; // A generous tap target, well past the tag itself.
            _clanLabelBounds.Add((new Rectangle(x - pad, y - pad, width + pad * 2, fontSize + pad * 2), group));
            Raylib.DrawRectangle(x - 6, y - 3, width + 12, fontSize + 6, PanelFill with { A = alpha });
            Raylib.DrawRectangle(x - 6, y + fontSize + 1, width + 12, 3, group.Color);
            if (group == highlighted)
                Raylib.DrawRectangleLines(x - 7, y - 4, width + 14, fontSize + 9, group.Color);
            Raylib.DrawText(text, x, y, fontSize, PanelInk);
        }

        // Each village's name, on a gold-edged tag higher over its middle: a village is more than any one clan in it.
        int villageFont = (int)(fontSize * 1.2f);
        foreach (Village village in world.Villages)
        {
            Vector3 anchor = village.Centre + new Vector3(0f, 5.2f, 0f);
            if (!IsPointOnScreen(camera, anchor))
                continue;
            Vector2 screen = Raylib.GetWorldToScreen(anchor, camera);
            string headman = world.HeadmanOf(village) is { } h ? $", headman {h.Name}" : "";
            string siege = world.InvasionAt(village) is not null ? $", UNDER ATTACK by {world.InvadersAt(village)} spiders" : "";
            string realm = world.RealmOf(village) is { } kingdom ? (village.Id == kingdom.Capital ? $", capital of {kingdom.Name}" : $", in {kingdom.Name}") : "";
            string text = $"{village.Name} ({village.ClanIds.Count} {(village.ClanIds.Count == 1 ? "clan" : "clans")}{headman}{realm}{siege})";
            int width = Raylib.MeasureText(text, villageFont);
            int x = (int)(screen.X - width / 2f), y = (int)(screen.Y - villageFont);
            Raylib.DrawRectangle(x - 8, y - 4, width + 16, villageFont + 8, PanelFill with { A = 215 });
            Raylib.DrawRectangle(x - 8, y + villageFont + 3, width + 16, 4, new Color(230, 190, 60, 255));
            Raylib.DrawText(text, x, y, villageFont, PanelInk);
        }
    }

    /// <summary>The right edge (at the reference screen width) of the buttons along the top — speed, Map and History.</summary>
    private const float TopButtonsRight = 1020f;

    /// <summary>The widest the Kin Inspector (or clan card) may be without covering the buttons along the top.</summary>
    private static int KinPanelMaxWidth(int margin, int inset) =>
        Math.Max(200, Raylib.GetScreenWidth() - margin - inset * 2 - (int)(TopButtonsRight * UiScale) - 12);

    /// <summary>The Kin Inspector's font size, line spacing and placement (top-right corner).</summary>
    private static (int FontSize, int LineHeight, int TopPadding, int Margin, int Inset) KinPanelMetrics()
    {
        int fontSize = ScaledFontSize(0.7f);
        return (fontSize, fontSize + fontSize / 5, 20, 30, 12);
    }

    /// <summary>The Kin Inspector's full height (padding included) for <paramref name="lineCount"/> lines.</summary>
    private static int KinPanelHeight(int lineCount)
    {
        var (fontSize, lineHeight, _, _, inset) = KinPanelMetrics();
        return lineHeight * lineCount - (lineHeight - fontSize) + inset * 2;
    }

    /// <summary>
    /// The "Follow" toggle, just under the Kin Inspector, while a
    /// Bramblekin is selected — see <see cref="FollowCamera"/>.
    /// </summary>
    private static UiButton? FollowButton(World world)
    {
        if (world.SelectedKin is not { IsDead: false } kin)
            return null;

        var (_, _, topPadding, margin, inset) = KinPanelMetrics();
        int lineCount = KinPanelLines(world, kin, PanelInk).Count;
        float height = 90 * UiScale;
        float width = 330 * UiScale;
        float x = Raylib.GetScreenWidth() - margin + inset - width;
        float y = topPadding + KinPanelHeight(lineCount) + 10 * UiScale;
        return new UiButton(new Rectangle(x, y, width, height));
    }

    /// <summary>The "Control" button, just left of "Follow": take the selected Bramblekin's wheel (see <see cref="PlayControl"/>).</summary>
    private static UiButton ControlButton(World world, UiButton follow) =>
        new(new Rectangle(follow.Bounds.X - follow.Bounds.Width - 10 * UiScale, follow.Bounds.Y, follow.Bounds.Width, follow.Bounds.Height));

    /// <summary>What the Kin Inspector says about <paramref name="kin"/>, line by line.</summary>
    private static List<(string Text, Color Color)> KinPanelLines(World world, Bramblekin kin, Color ink)
    {
        KinGroup? group = world.GroupOf(kin);
        string role = group is null ? "Solitary" : group.Leader == kin ? "Leader"
            : group.Heir == kin ? "Follower, heir" : world.CouncilOf(group).Contains(kin) ? "Follower, councillor"
            : group.Plot is { } plot && (plot.Instigator == kin || plot.Conspirators.Contains(kin)) ? "Follower, plotting" : "Follower";
        int friends = kin.KnownKins.Values.Count(r => r == RelationshipState.Friend);
        int enemies = kin.KnownKins.Values.Count(r => r == RelationshipState.Enemy);
        int neutral = kin.KnownKins.Values.Count(r => r == RelationshipState.Neutral);

        return new List<(string Text, Color Color)>
        {
            ($"{kin.Name}  ({kin.Sex.ToString().ToLowerInvariant()}, {role}{(kin.IsYoung ? ", young" : kin.IsElder ? ", elder" : "")})", ink),
            ($"Age {kin.DescribeAge()}, generation {kin.Generation}", ink),
            ((kin.ParentNames is { } parents ? $"Child of {parents.Mother} & {parents.Father}" : "Wandered in from the edge") +
                (kin.GuardianNames is { } guardians ? $", raised by {guardians.A} & {guardians.B}" : ""), ink),
            (kin.DescribeFamily(), kin.Partner is not null ? new Color(190, 70, 120, 255) : ink),
            ($"State: {kin.State}   Health: {kin.Health} / {kin.HealthCap}", ink),
            ($"Job: {(kin.IsYoung ? "none (young)" : group is null ? "none (on its own)" : kin.Job.ToString())}{(kin.VillageJob != KinJob.None ? $"  (village: {kin.VillageJob}{(kin.IsPaid ? ", paid" : ", unpaid")})" : "")}{(world.Realms.FirstOrDefault(k => k.KingId == kin.ID) is { } crown ? $"  KING of {crown.Name}" : "")}", ink),
            ($"Hunger: {(int)kin.Hunger}%{(kin.IsStarving ? " STARVING" : kin.IsHungry ? " (hungry)" : "")}{(kin.HasFood ? "  +food" : "")}{(kin.IsSick ? "  SICK" : "")}",
                kin.IsStarving || kin.IsSick ? new Color(170, 60, 40, 255) : ink),
            ($"Thirst: {(int)kin.Thirst}%{(kin.Thirst >= Bramblekin.MaxThirst ? " PARCHED" : kin.IsThirsty ? " (thirsty)" : "")}   " +
             (group is not null && world.WellOf(group) is { IsDug: true } ? "a well at home" :
              (WaterMap.DistanceToWater((kin.Home?.Position ?? kin.Position).X, (kin.Home?.Position ?? kin.Position).Z) is var waterDistance and < 10000f ? $"water {waterDistance:0}m from {(kin.Home is null ? "here" : "home")}" : "no water in the garden now")),
                kin.Thirst >= Bramblekin.MaxThirst ? new Color(170, 60, 40, 255) : kin.IsThirsty ? ThirstBarColor : ink),
            ($"Nature: {kin.Personality.Describe()}", ink),
            ($"Aggression {kin.Personality.Aggression:0.00}   Sociability {kin.Personality.Sociability:0.00}", new Color(185, 60, 45, 255)),
            ($"Intelligence {kin.Personality.Intelligence:0.00} ({kin.DetectionRadius:0}m)   Courage {kin.Personality.Courage:0.00}", new Color(60, 100, 170, 255)),
            ($"Rebellion {kin.Personality.Rebelliousness:0.00}  Persuasion {kin.Personality.Persuasiveness:0.00}  Diligence {kin.Personality.Diligence:0.00}", new Color(60, 130, 70, 255)),
            ($"{kin.DescribeStrength()}", new Color(150, 80, 40, 255)),
            ($"Skills: {kin.DescribeSkills()}{(kin.DescribeTrade() is { } trade ? $"  ({trade})" : "")}", new Color(150, 100, 40, 255)),
            (group is null ? "Group: none" : $"Group: {group.Name ?? group.ShortId}, {group.Members.Count} members" +
                (DescribeClan(world, group) is { } about ? $" ({about})" : ""), ink),
            (DescribeHome(kin), ink),
            (kin.Errand is { } errand ? DescribeErrand(world, errand)
                : group is null ? "Job: none" : $"Job: {kin.Job} (group goal: {group.Goal}{(group.Sharing == SharingRule.LeaderFirst ? ", leader eats first" : "")})", ink),
            (group is null || group.Leader == kin
                ? $"Reputation: {kin.Reputation:0.0}{(kin.Status == SurvivalStatus.Independent ? "  (independent)" : "")}"
                : $"Loyalty: {kin.Loyalty:0.00}{(kin.Loyalty < Bramblekin.ObedienceThreshold ? " (disobedient)" : "")}   Reputation: {kin.Reputation:0.0}",
                kin.GroupId is not null && group?.Leader != kin && kin.Loyalty < Bramblekin.ObedienceThreshold ? new Color(170, 60, 40, 255) : ink),
            ($"Known: {friends} friend, {enemies} enemy, {neutral} neutral" +
                (kin.IsNotorious ? "   Notorious!" : kin.Infamy > 0 ? $"   Infamy: {kin.Infamy:0.0}" : ""),
                kin.IsNotorious ? new Color(170, 60, 40, 255) : ink),
        };
    }

    /// <summary>"Errand: carrying 5 food in aid to the Mossbrook clan" for the Kin Inspector.</summary>
    private static string DescribeErrand(World world, Errand errand)
    {
        string to = world.Groups.FirstOrDefault(g => g.Id == errand.To)?.Title ?? "its allies";
        return errand switch
        {
            { Returning: true } => $"Errand: heading home with {errand.Load} food in pay",
            { Kind: ErrandKind.Aid } => $"Errand: carrying {errand.Load} food in aid to {to}",
            { Kind: ErrandKind.Tribute } => $"Errand: carrying {errand.Load} food in tribute to {to}",
            _ => $"Errand: helping {to} build ({errand.TwigsOwed} twigs to go)",
        };
    }

    /// <summary>"warlike; allied with 1" for the Kin Inspector — the clan's tradition and its neighbours — or null if there's nothing to say.</summary>
    private static string? DescribeClan(World world, KinGroup group)
    {
        string?[] parts = { group.Culture.Label, world.DescribeRelations(group) };
        string joined = string.Join("; ", parts.Where(p => p is not null));
        return joined.Length > 0 ? joined : null;
    }

    /// <summary>The selected Bramblekin's name, floating above it (and its partner's, fainter, so a couple is easy to spot).</summary>
    private static void DrawNameTag(Camera3D camera, World world)
    {
        if (world.SelectedKin is not { IsDead: false } kin)
            return;

        DrawTag(kin, ScaledFontSize(0.55f), 255);
        if (kin.Partner is { IsDead: false } partner)
            DrawTag(partner, ScaledFontSize(0.45f), 170);

        void DrawTag(Bramblekin who, int fontSize, byte alpha)
        {
            Vector3 anchor = who.Position + new Vector3(0, Bramblekin.BodyHeight + 0.6f, 0);
            if (!IsPointOnScreen(camera, anchor))
                return;
            Vector2 screen = Raylib.GetWorldToScreen(anchor, camera);
            string text = who.GivenName;
            int width = Raylib.MeasureText(text, fontSize);
            int x = (int)(screen.X - width / 2f), y = (int)(screen.Y - fontSize);
            Raylib.DrawRectangle(x - 6, y - 3, width + 12, fontSize + 6, PanelFill with { A = (byte)(PanelFill.A * alpha / 255) });
            Raylib.DrawText(text, x, y, fontSize, PanelInk with { A = alpha });
        }
    }

    /// <summary>Blends <paramref name="baseColor"/> toward <paramref name="tint"/> by <paramref name="amount"/> (0 = unchanged, 1 = fully tint), keeping <paramref name="baseColor"/>'s own alpha.</summary>
    private static Color BlendToward(Color baseColor, Color tint, float amount) => new(
        (byte)Math.Clamp(baseColor.R + (tint.R - baseColor.R) * amount, 0, 255),
        (byte)Math.Clamp(baseColor.G + (tint.G - baseColor.G) * amount, 0, 255),
        (byte)Math.Clamp(baseColor.B + (tint.B - baseColor.B) * amount, 0, 255),
        baseColor.A);

    /// <summary>Sim status, who's alive and in which groups, what they're doing right now, and the running tallies, in a bar along the bottom of the screen.</summary>
    /// <returns>The screen Y of the bar's top edge, so other panels can stay clear of it.</returns>
    private static int DrawHud(World world)
    {
        int Count(BramblekinState state) => world.Colony.Count(b => !b.IsDead && b.State == state);

        // The Stats button sits at the bottom-left, just above where the stats bar is, whether it shows or not,
        // with the Log button and the log above it.
        const int statLines = 7;
        int fontSize = ScaledFontSize(0.8f);
        int lineHeight = fontSize + fontSize / 6;
        int barHeight = lineHeight * statLines + 20;
        string statsLabel = _statsView == StatsView.Shown ? "Stats: on" : "Stats: off";
        int statsFont = Math.Max(14, (int)(30 * UiScale));
        int statsPad = Math.Max(6, (int)(14 * UiScale));
        int statsWidth = Raylib.MeasureText("Stats: off", statsFont) + statsPad * 2;
        int statsHeight = statsFont + statsPad * 2;

        // Places and draws the button above <barTop>; returns its top edge, for the panels above to stay clear of.
        int DrawStatsButton(int barTop)
        {
            _statsButtonBounds = new Rectangle(10, barTop - 10 - statsHeight, statsWidth, statsHeight);
            bool statsHovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _statsButtonBounds);
            Raylib.DrawRectangleRec(_statsButtonBounds, new Color(0, 0, 0, statsHovered ? 190 : 150));
            Raylib.DrawRectangleLinesEx(_statsButtonBounds, 2f, new Color(255, 255, 255, 110));
            Raylib.DrawText(statsLabel, (int)_statsButtonBounds.X + statsPad, (int)_statsButtonBounds.Y + statsPad, statsFont, Color.RayWhite);

            // The Alerts switch sits beside it.
            _alertsButtonBounds = new Rectangle(_statsButtonBounds.X + statsWidth + 10, _statsButtonBounds.Y, Raylib.MeasureText("Alerts: off", statsFont) + statsPad * 2, statsHeight);
            bool alertsHovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _alertsButtonBounds);
            Raylib.DrawRectangleRec(_alertsButtonBounds, new Color(0, 0, 0, alertsHovered ? 190 : 150));
            Raylib.DrawRectangleLinesEx(_alertsButtonBounds, 2f, new Color(255, 255, 255, 110));
            Raylib.DrawText(_alertsOn ? "Alerts: on" : "Alerts: off", (int)_alertsButtonBounds.X + statsPad, (int)_alertsButtonBounds.Y + statsPad, statsFont, Color.RayWhite);
            return (int)_statsButtonBounds.Y;
        }

        if (_statsView == StatsView.Hidden)
            return DrawStatsButton(Raylib.GetScreenHeight() - barHeight);

        int living = world.Colony.Count(b => !b.IsDead);
        int solitary = world.Colony.Count(b => !b.IsDead && b.GroupId is null);
        int largestGroup = world.Groups.Count > 0 ? world.Groups.Max(g => g.Members.Count) : 0;

        // Short lines rather than one or two wide ones, so the bar fits the
        // screen at any size (the font scales with UiScale).
        string[] lines =
        {
            $"Year {world.Year} {world.CurrentSeason}, day {world.DayOfYear} {world.TimeOfDayLabel.ToLowerInvariant()}{(world.WeatherLabel is { } weather ? $" - {weather}" : "")} (food x{world.FoodAbundance:0.0})   Speed {_timeScale}x{(_achievedSpeed < _timeScale * 0.85f ? $" (running {_achievedSpeed:0}x)" : "")}   FPS {Raylib.GetFPS()} (sim {_simMs:0} ms, draw {_drawMs:0} ms)   Food on map {world.LooseFoodCount}   Spider: {SpiderStatus(world)}",
            $"Homes: {world.Shelters.Count(s => s.IsBuilt && s.Tier == ShelterTier.Tent)} tents, {world.Shelters.Count(s => s.Tier == ShelterTier.House)} houses, " +
            $"{world.Shelters.Count(s => s.IsBuilt && s.IsBurrow)} burrows, {world.Shelters.Count(s => !s.IsBuilt)} being built   Food stored {world.Shelters.Sum(s => s.StoredFood)}   " +
            $"Villages {world.Villages.Count} (budded {world.Buddings})   Crops {world.Crops.Count}",
            $"Bramblekin {living}: {solitary} solitary, {world.Groups.Count} groups (largest {largestGroup}, {world.Groups.Count(World.KnowsFarming)} farming)   " +
            $"Alliances {world.CurrentAlliances}   Wars {world.CurrentWars}",
            $"Foraging {Count(BramblekinState.Foraging) + Count(BramblekinState.Hunting)}   Eating {Count(BramblekinState.Eating)}   Drinking {Count(BramblekinState.Drinking)}   " +
            $"Fleeing {Count(BramblekinState.Fleeing)}   Fighting {Count(BramblekinState.Fighting)}   Robbing {Count(BramblekinState.Attacking)}   Asleep {Count(BramblekinState.Sleeping)}{(world.Feasts.Count > 0 ? $"   Feasting {Count(BramblekinState.Feasting)}" : "")}",
            $"Arrived {world.Arrivals}   Died: starved {world.DeathsByStarvation}, thirst {world.DeathsByThirst}, old age {world.DeathsByOldAge}, predators {world.DeathsByPredator}, kin {world.DeathsByKin}, sickness {world.DeathsBySickness}   Sick {world.SickCount}",
            $"Born {world.Births} (gen {world.MaxGeneration})   Couples {world.LivingCouples}   Politics: {world.Departures} left, {world.Splinters} splits, {world.Coups} coups, {world.Exiles} exiles   Raids {world.StoreRaids}",
        };

        // UI Text Scaling: a background bar goes underneath, sized off
        // fontSize/lineHeight, so the text stays legible over a busy map.
        int y = Raylib.GetScreenHeight() - barHeight + 10;
        Raylib.DrawRectangle(0, y - 10, Raylib.GetScreenWidth(), barHeight, new Color(0, 0, 0, 90));
        for (int i = 0; i < lines.Length; i++)
            Raylib.DrawText(lines[i], 20, y + lineHeight * i, fontSize, Color.RayWhite);
        return DrawStatsButton(y - 10);
    }

    private static string SpiderStatus(World world) =>
        world.Spider is { IsDead: false } spider
            ? spider.State.ToString()
            : $"slain ({MathF.Ceiling(world.SpiderRespawnTimer)}s)";

    /// <summary>
    /// On-Screen Debug Console: renders <see cref="_debugLogs"/> (see
    /// <see cref="AddEventLog"/>) as a small, semi-transparent panel on the
    /// left of the screen, between <paramref name="top"/> and
    /// <paramref name="bottom"/> (below the speed buttons, above the HUD) —
    /// a running history of recent notable events. Newest entry at the
    /// bottom, oldest at top, matching the natural reading order of a
    /// scrolling log. Under it sits the Log button: the player chooses to see
    /// just the newest few entries, all of them, or none (see
    /// <see cref="LogView"/>).
    /// </summary>
    private static void DrawDebugConsole(int top, int bottom)
    {
        const int gap = 10;
        const int x = 10;

        // The Log button, bottom-left just above the HUD.
        string label = _logView switch
        {
            LogView.Brief => "Log: brief",
            LogView.Full => "Log: full",
            _ => _unseenLogs > 0 ? $"Log: off (+{_unseenLogs})" : "Log: off",
        };
        int buttonFont = Math.Max(14, (int)(30 * UiScale));
        int buttonPad = Math.Max(6, (int)(14 * UiScale));
        int buttonWidth = Math.Max(Raylib.MeasureText("Log: brief", buttonFont), Raylib.MeasureText(label, buttonFont)) + buttonPad * 2;
        int buttonHeight = buttonFont + buttonPad * 2;
        _logButtonBounds = new Rectangle(x, bottom - gap - buttonHeight, buttonWidth, buttonHeight);
        bool hovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _logButtonBounds);
        Raylib.DrawRectangleRec(_logButtonBounds, new Color(0, 0, 0, hovered ? 190 : 150));
        Raylib.DrawRectangleLinesEx(_logButtonBounds, 2f, new Color(255, 255, 255, 110));
        Raylib.DrawText(label, x + buttonPad, (int)_logButtonBounds.Y + buttonPad, buttonFont, Color.RayWhite);

        int entries = _logView switch
        {
            LogView.Full => _debugLogs.Count,
            LogView.Brief => Math.Min(BriefLogEntries, _debugLogs.Count),
            _ => 0,
        };
        if (entries == 0)
            return;

        // Readability: scaled by UiScale like the rest of this file's
        // responsive UI rather than a fixed pixel size, so it stays legible
        // at any screen size.
        int fontSize = (int)(18 * UiScale);
        int lineHeight = fontSize + 4;

        // Word-Wrap: in full, each stored entry is greedily word-wrapped at
        // render time into as many visual lines as it takes to stay under
        // maxWidth; brief keeps each to one line, cut short if need be.
        int maxWidth = (int)(400 * UiScale);
        var wrappedLines = new List<string>();
        for (int i = _debugLogs.Count - entries; i < _debugLogs.Count; i++)
        {
            if (_logView == LogView.Brief)
                wrappedLines.Add(Fit(_debugLogs[i], fontSize, maxWidth * 3 / 2));
            else
                WrapLine(_debugLogs[i], fontSize, maxWidth, wrappedLines);
        }

        // Only the newest lines that fit between the speed buttons and the
        // Log button, so a burst of long entries never grows the panel into either.
        int panelBottom = (int)_logButtonBounds.Y - gap / 2;
        int maxLines = Math.Max(1, (panelBottom - top - gap - 16) / lineHeight);
        if (wrappedLines.Count > maxLines)
            wrappedLines.RemoveRange(0, wrappedLines.Count - maxLines);

        int widestLine = 0;
        for (int i = 0; i < wrappedLines.Count; i++)
            widestLine = Math.Max(widestLine, Raylib.MeasureText(wrappedLines[i], fontSize));

        int width = widestLine + 16;
        int height = wrappedLines.Count * lineHeight + 16;
        int y = panelBottom - height;

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
