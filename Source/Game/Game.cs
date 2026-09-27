using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

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
        var followCamera = new FollowCamera(camera);

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
            var mapButton = new UiButton(new Rectangle(speedButtonMargin * 2 + speedButtonWidth * 2 + speedButtonGap, speedButtonMargin,
                (int)(190 * uiScale), speedButtonHeight));
            UiButton? followButton = FollowButton(world);

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
            else if (mousePressed && mapButton.Contains(mousePosition))
                followCamera.ShowWholeMap();
            else if (mousePressed && followButton is not null && followButton.Contains(mousePosition))
                followCamera.ToggleFollow(world);
            else
                input.Update(camera, world);
            followCamera.Update(ref camera, world, rawDeltaTime, touchCamera.DraggedThisGesture);

            // 2) Simulation: TimeScale runs World.Update() several times per
            //    rendered frame (see _timeScale's own doc comment) rather
            //    than scaling deltaTime itself.
            int simulationSteps = Math.Max(1, (int)MathF.Round(_timeScale));
            for (int step = 0; step < simulationSteps; step++)
                world.Update(rawDeltaTime);

            // 3) Rendering.
            Raylib.BeginDrawing();
            Raylib.ClearBackground(world.SkyColor);

            Raylib.BeginMode3D(camera);
            world.Draw(camera);
            Raylib.EndMode3D();

            // 2D overlay (UI) is drawn after EndMode3D so it sits on top.
            DrawStatusBars(camera, world);
            DrawNameTag(camera, world);
            DrawFloatingTexts(camera, world);
            speedDownButton.Draw("-", highlighted: false, disabled: _timeScale <= TimeScaleSteps[0]);
            DrawSpeedLabel(speedLabelBounds, uiScale);
            speedUpButton.Draw("+", highlighted: false, disabled: _timeScale >= TimeScaleSteps[^1]);
            mapButton.Draw("Map", highlighted: false);
            DrawKinPanel(world);
            followButton?.Draw(followCamera.IsFollowing ? "Following" : "Follow", highlighted: followCamera.IsFollowing);
            int hudTop = DrawHud(world);
            DrawDebugConsole(top: speedButtonMargin * 2 + speedButtonHeight, bottom: hudTop);

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
            $"alliances {world.AlliancesFormed}; grubs hunted {world.GrubsKilled}, beetles {world.BeetlesKilled}, hornets swatted {world.HornetsKilled}, spiders slain {world.SpidersKilled}.");
        Console.WriteLine(
            $"Homes: {world.TentsBuilt} tents and {world.HousesBuilt} houses built, {world.SheltersCollapsed} collapsed; " +
            $"{world.StoreMeals} meals eaten from stores, {world.StoreRaids} store raids; " +
            $"{world.VillagesFounded} villages founded, {world.Buddings} daughter groups budded off.");
        Console.WriteLine(
            $"Neighbours: {world.AlliancesMade} alliances made, {world.WarsDeclared} wars declared, {world.PeacesMade} peaces made; " +
            $"{world.AidSent} aid shipments ({world.FoodAided} food), {world.WarRaids} pieces carried off in war raids; " +
            $"at the end {world.CurrentAlliances} alliances and {world.CurrentWars} wars.");
        Console.WriteLine(
            $"Farming: worked out {world.FarmingDiscoveries} times, taught {world.FarmingTaught} times; {world.BushesPlanted} bushes planted, " +
            $"{world.FruitHarvested} berries picked; at the end {world.Groups.Count(World.KnowsFarming)} groups farm {world.Bushes.Count(b => b.GroupId is not null)} bushes " +
            $"({world.Bushes.Count(b => b.GroupId is null)} wild).");
        Console.WriteLine(
            $"Building: a Tent takes {world.AverageTentBuildSeconds:0}s on average, a House upgrade {world.AverageHouseUpgradeSeconds:0}s; " +
            $"{world.StagesOlderThan(600f)} of {world.Shelters.Count(s => !s.IsBuilt || s.IsUpgrading)} construction stages under way have stalled over 10 min.");
        PrintSurvivalTrend(world);
        PrintLeadership(world);
        PrintRebellion(world);
        PrintLineage(world);
    }

    /// <summary>Headless summary: how often followers rebelled, and how the Independents who left are faring.</summary>
    private static void PrintRebellion(World world)
    {
        List<Bramblekin> independents = world.Colony.Where(b => !b.IsDead && b.Status == SurvivalStatus.Independent).ToList();
        Console.WriteLine(
            $"Rebellion: {world.Departures} left, {world.Splinters} splinter groups, {world.Coups} coups " +
            $"({world.FailedChallenges} failed challenges), {world.Exiles} exiles; {world.Rejoins} independents later joined another group.");
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
                $"{world.DeathsIn(status, DeathCause.Kin)} to kin, {world.DeathsIn(status, DeathCause.OldAge)} of old age)");
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
            $"[t={world.ElapsedSeconds,6:0}s Y{world.Year} {world.CurrentSeason,-6}] kin {living.Count,3} (solitary {solitary}, groups {world.Groups.Count}, largest {largestGroup}, villages {villages}, bushes {world.Bushes.Count}, allies {world.CurrentAlliances}, wars {world.CurrentWars}) " +
            $"avg hunger {averageHunger,5:0.0}  food on map {world.LooseFoodCount,3}, stored {stored,3}  tents {tents} houses {houses}  " +
            $"arrived {world.Arrivals} born {world.Births}  died: starved {world.DeathsByStarvation}, predators {world.DeathsByPredator}, kin {world.DeathsByKin}, old age {world.DeathsByOldAge}");
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
            $"{world.DeathsByOldAge} died of old age" +
            (living.Count > 0 ? $"; the oldest alive is {living.Max(b => b.AgeInYears):0.0} years." : "."));
        if (living.Count > 0)
        {
            Console.WriteLine(
                $"  Traits of the living (a newcomer averages 0.50): aggression {living.Average(b => b.Personality.Aggression):0.00}, " +
                $"sociability {living.Average(b => b.Personality.Sociability):0.00}, intelligence {living.Average(b => b.Personality.Intelligence):0.00}");
        }
    }

    /// <summary>One line describing a Bramblekin's home for the Kin Inspector.</summary>
    private static string DescribeHome(Bramblekin kin) => kin.Home switch
    {
        null => "Home: none",
        { IsBuilt: false } site => $"Home: building a Tent ({site.TwigsDelivered}/{site.TwigsNeeded} twigs)",
        { IsUpgrading: true } home => $"Home: Tent -> House ({home.TwigsDelivered}/{home.TwigsNeeded}), store {home.StoredFood}/{home.StoreCapacity}",
        { } home => $"Home: {home.Tier}{(home.GroupId is null ? "" : " (group)")}, store {home.StoredFood}/{home.StoreCapacity}",
    };

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
        var (fontSize, lineHeight, topPadding, margin, inset) = KinPanelMetrics();

        Bramblekin? kin = world.SelectedKin;
        if (kin is null || kin.IsDead)
        {
            const string hint = "Tap a Bramblekin to follow it";
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

        // Sized to its widest line, so no stat ever runs off the panel.
        int width = lines.Max(line => Raylib.MeasureText(line.Text, fontSize));
        int height = KinPanelHeight(lines.Count);
        int x = Raylib.GetScreenWidth() - width - margin;
        Raylib.DrawRectangle(x - inset, topPadding, width + inset * 2, height, fill);
        Raylib.DrawRectangleLines(x - inset, topPadding, width + inset * 2, height, ink);
        for (int i = 0; i < lines.Count; i++)
            Raylib.DrawText(lines[i].Text, x, topPadding + inset + lineHeight * i, fontSize, lines[i].Color);
    }

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

    /// <summary>What the Kin Inspector says about <paramref name="kin"/>, line by line.</summary>
    private static List<(string Text, Color Color)> KinPanelLines(World world, Bramblekin kin, Color ink)
    {
        KinGroup? group = world.GroupOf(kin);
        string role = group is null ? "Solitary" : group.Leader == kin ? "Leader" : "Follower";
        int friends = kin.KnownKins.Values.Count(r => r == RelationshipState.Friend);
        int enemies = kin.KnownKins.Values.Count(r => r == RelationshipState.Enemy);
        int neutral = kin.KnownKins.Values.Count(r => r == RelationshipState.Neutral);

        return new List<(string Text, Color Color)>
        {
            ($"{kin.Name}  ({kin.Sex.ToString().ToLowerInvariant()}, {role}{(kin.IsYoung ? ", young" : kin.IsElder ? ", elder" : "")})", ink),
            ($"Age {kin.DescribeAge()}, generation {kin.Generation}", ink),
            (kin.ParentNames is { } parents ? $"Child of {parents.Mother} & {parents.Father}" : "Wandered in from the edge", ink),
            (kin.DescribeFamily(), kin.Partner is not null ? new Color(190, 70, 120, 255) : ink),
            ($"State: {kin.State}   Health: {kin.Health} / {Bramblekin.MaxHealth}", ink),
            ($"Hunger: {(int)kin.Hunger}%{(kin.IsStarving ? " STARVING" : kin.IsHungry ? " (hungry)" : "")}{(kin.HasFood ? "  +food" : "")}",
                kin.IsStarving ? new Color(170, 60, 40, 255) : ink),
            ($"Aggression:   {kin.Personality.Aggression:0.00}", new Color(185, 60, 45, 255)),
            ($"Sociability:  {kin.Personality.Sociability:0.00}", new Color(60, 130, 70, 255)),
            ($"Intelligence: {kin.Personality.Intelligence:0.00} ({kin.DetectionRadius:0}m)", new Color(60, 100, 170, 255)),
            (group is null ? "Group: none" : $"Group: {group.Name ?? group.ShortId}, {group.Members.Count} members" +
                (world.DescribeRelations(group) is { } relations ? $" ({relations})" : ""), ink),
            (DescribeHome(kin), ink),
            (group is null ? "Job: none" : $"Job: {kin.Job} (group goal: {group.Goal}{(group.Sharing == SharingRule.LeaderFirst ? ", leader eats first" : "")})", ink),
            (group is null || group.Leader == kin
                ? $"Reputation: {kin.Reputation:0.0}{(kin.Status == SurvivalStatus.Independent ? "  (independent)" : "")}"
                : $"Loyalty: {kin.Loyalty:0.00}{(kin.Loyalty < Bramblekin.ObedienceThreshold ? " (disobedient)" : "")}   Reputation: {kin.Reputation:0.0}",
                kin.GroupId is not null && group?.Leader != kin && kin.Loyalty < Bramblekin.ObedienceThreshold ? new Color(170, 60, 40, 255) : ink),
            ($"Known: {friends} friend, {enemies} enemy, {neutral} neutral", ink),
        };
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

        int living = world.Colony.Count(b => !b.IsDead);
        int solitary = world.Colony.Count(b => !b.IsDead && b.GroupId is null);
        int largestGroup = world.Groups.Count > 0 ? world.Groups.Max(g => g.Members.Count) : 0;

        // Short lines rather than one or two wide ones, so the bar fits the
        // screen at any size (the font scales with UiScale).
        string[] lines =
        {
            $"Year {world.Year} {world.CurrentSeason} (food x{world.FoodAbundance:0.0})   Speed {_timeScale}x   FPS {Raylib.GetFPS()}   Food on map {world.LooseFoodCount}   Spider: {SpiderStatus(world)}",
            $"Homes: {world.Shelters.Count(s => s.IsBuilt && s.Tier == ShelterTier.Tent)} tents, {world.Shelters.Count(s => s.Tier == ShelterTier.House)} houses, " +
            $"{world.Shelters.Count(s => !s.IsBuilt)} being built   Food stored {world.Shelters.Sum(s => s.StoredFood)}   " +
            $"Villages {world.Groups.Count(g => g.Annexes.Count > 0)} (budded {world.Buddings})   Bushes {world.Bushes.Count}",
            $"Bramblekin {living}: {solitary} solitary, {world.Groups.Count} groups (largest {largestGroup}, {world.Groups.Count(World.KnowsFarming)} farming)   " +
            $"Alliances {world.CurrentAlliances}   Wars {world.CurrentWars}",
            $"Foraging {Count(BramblekinState.Foraging) + Count(BramblekinState.Hunting)}   Eating {Count(BramblekinState.Eating)}   " +
            $"Fleeing {Count(BramblekinState.Fleeing)}   Fighting {Count(BramblekinState.Fighting)}   Robbing {Count(BramblekinState.Attacking)}",
            $"Arrived {world.Arrivals}   Died: starved {world.DeathsByStarvation}, old age {world.DeathsByOldAge}, predators {world.DeathsByPredator}, kin {world.DeathsByKin}   Thefts {world.Thefts}",
            $"Born {world.Births} (gen {world.MaxGeneration})   Couples {world.LivingCouples}   Politics: {world.Departures} left, {world.Splinters} splits, {world.Coups} coups, {world.Exiles} exiles   Raids {world.StoreRaids}",
        };

        // UI Text Scaling: a background bar goes underneath, sized off
        // fontSize/lineHeight, so the text stays legible over a busy map.
        int fontSize = ScaledFontSize(0.8f);
        int lineHeight = fontSize + fontSize / 6;
        int barHeight = lineHeight * lines.Length + 20;
        int y = Raylib.GetScreenHeight() - barHeight + 10;
        Raylib.DrawRectangle(0, y - 10, Raylib.GetScreenWidth(), barHeight, new Color(0, 0, 0, 90));
        for (int i = 0; i < lines.Length; i++)
            Raylib.DrawText(lines[i], 20, y + lineHeight * i, fontSize, Color.RayWhite);
        return y - 10;
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
    /// a running history of recent notable events for on-device debugging.
    /// Newest entry at the bottom, oldest at top, matching the natural
    /// reading order of a scrolling log.
    /// </summary>
    private static void DrawDebugConsole(int top, int bottom)
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

        // Only the newest lines that fit between the speed buttons and the
        // HUD, so a burst of long entries never grows the panel into either.
        const int gap = 10;
        int maxLines = Math.Max(1, (bottom - top - 2 * gap - 16) / lineHeight);
        if (wrappedLines.Count > maxLines)
            wrappedLines.RemoveRange(0, wrappedLines.Count - maxLines);

        int widestLine = 0;
        for (int i = 0; i < wrappedLines.Count; i++)
            widestLine = Math.Max(widestLine, Raylib.MeasureText(wrappedLines[i], fontSize));

        int width = widestLine + 16;
        int height = wrappedLines.Count * lineHeight + 16;
        int x = 10;
        int y = bottom - gap - height;

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
