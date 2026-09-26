// =============================================================================
//  Garden Guardians — Pure Autonomous Simulation
// -----------------------------------------------------------------------------
//  Design (see Garden_Guardians_Roadmap.md/Garden_Guardians_Design.md):
//    * A fixed isometric camera looking down at a 100 m x 100 m patch of
//      "terrain", with a mobile-friendly one-finger-pan/two-finger-pinch
//      camera controller layered on top.
//    * Zero player intervention: there is no god-game lever (no Pebble-Drop,
//      no Gust, no Faith) left to pull. Every faction's Village Heart runs
//      its own economy end to end — Auto-Sprout, Auto-Conscription, farm and
//      building construction, Militia defense — entirely on its own. The
//      only two taps left are inspecting a faction (WorldTapInput) and
//      Genesis (reseeding the world after total extinction, since an
//      autonomous simulation still needs some way back from an empty map).
//    * A colony of Bramblekin that wander, gather, build and fight, steering
//      around obstacles (Village Hearts) and each faction's Wolf Spider
//      threat.
//    * The economic loop: crack an Acorn (Cooperative Acorn Cracking, up to
//      3 Chitin-Mallet Gatherers working it together) or forage a wild
//      Berry, and the Bramblekin carry the Food Shards back to the Village
//      Heart; Amber and a Trading Post round out a Tycoon Economy layer.
//    * The predator: a Wolf Spider that hunts busy workers by vibration,
//      fought off by a faction's own Militia.
//
//  Safety: entities are created and destroyed constantly (sprouts, spider
//  kills, deaths in combat), so every list that can change size mid-frame is
//  either walked with a reverse for-loop or mutated through a deferred
//  pending-add/pending-remove queue processed once at the end of the frame,
//  never directly inside another entity's Update().
//
//  Scale convention: 1 world unit = 1 meter. The terrain is a 100 m x 100 m plane
//  centred on the origin, and "up" is +Y.
//
//  Everything lives in this single file for now; each class is small and
//  self-contained so it can be lifted into its own file once the project
//  graduates into a fuller project structure.
// =============================================================================

using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Desktop entry point. Android starts the game from MainActivity instead
/// (see Platforms/Android/MainActivity.cs); both end up in <see cref="Game.Run"/>.
/// </summary>
public static class Program
{
    public static void Main() => Game.Run(GamePlatform.Desktop);
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

    /// <summary>How many Bramblekin the colony starts with.</summary>
    private const int ColonySize = 8;

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
        // map centre is Vector3.Zero, not (50, 0, 50); Position keeps the
        // same offset from Target as before so the viewing angle is
        // unchanged, just re-centred on the actual map.
        var camera = new Camera3D
        {
            Target = Vector3.Zero,
            Position = new Vector3(0.0f, 120.0f, 100.0f),
            Up = Vector3.UnitY,
            FovY = 45f,
            Projection = CameraProjection.Perspective,
        };
        var world = new World(new Terrain(size: 100f), new Random(), ColonySize);
        world.SpawnSpiderNearVillage();
        var input = new WorldTapInput();
        var touchCamera = new TouchCameraController();
        // Debug Time Scale buttons: 3x their old size (50x44 -> 150x132) so
        // they're comfortably tappable on a mobile screen; SpeedButtonGap
        // is the width of the "Nx" label panel DrawSpeedLabel draws between
        // them, scaled to match.
        const int speedButtonWidth = 150, speedButtonHeight = 132, speedButtonGap = 180;
        var speedDownButton = new UiButton(new Rectangle(20, 20, speedButtonWidth, speedButtonHeight));
        var speedUpButton = new UiButton(new Rectangle(20 + speedButtonWidth + speedButtonGap, 20, speedButtonWidth, speedButtonHeight));

        // --- Main loop -------------------------------------------------------
        while (!Raylib.WindowShouldClose())
        {
            float rawDeltaTime = MathF.Min(Raylib.GetFrameTime(), MaxDeltaTime);

            // 0) Spectator Camera: one finger (or a held mouse button) drags
            //    to pan, two fingers twist to rotate around the current
            //    Target and pinch to zoom. Runs before the tap input below
            //    so the rest of the frame sees an already-settled camera.
            touchCamera.Update(ref camera, world.Terrain.Size / 2f);

            // 1) Input: Pure Simulation — the player has no lever on the
            //    world any more. Conscription, Sprouting and Village
            //    Building are all the Village Heart's own business, run
            //    autonomously inside world.Update() below. The only taps
            //    left are inspecting a faction and Genesis (see
            //    WorldTapInput, which only fires on a clean release that
            //    never turned into a pan). The Debug Time Scale +/- buttons
            //    are checked first and, if hit, swallow the click so it
            //    never also lands as a ground tap.
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
            DrawHealthBars(camera, world);
            DrawFloatingTexts(camera, world);
            speedDownButton.Draw("-", highlighted: false, disabled: _timeScale <= TimeScaleSteps[0]);
            DrawSpeedLabel();
            speedUpButton.Draw("+", highlighted: false, disabled: _timeScale >= TimeScaleSteps[^1]);
            DrawColonyPanel(world);
            DrawGenesisPrompt(world);
            DrawMonumentAlerts(world);
            DrawHud(world);

            Raylib.EndDrawing();

            // 4) Deferred spawns/removals: applied once here, after this
            //    frame's Update() and Draw() have both fully run, so no
            //    entity list ever changes size while something is iterating
            //    it (a sprout mid-Colony-update, a kill mid-pounce, etc).
            world.CommitPendingChanges();
        }

        Raylib.CloseWindow();
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
    private static void DrawSpeedLabel()
    {
        // Kept in sync with the 3x-scaled speedDownButton/speedUpButton
        // layout in Run: this panel fills the gap between them exactly.
        const int fontSize = 64, x = 170, width = 180, y = 20, height = 132;
        Raylib.DrawRectangle(x, y, width, height, PanelFill);
        Raylib.DrawRectangleLines(x, y, width, height, PanelInk);

        string text = $"{(int)_timeScale}x";
        int textWidth = Raylib.MeasureText(text, fontSize);
        Color color = _timeScale != 1f ? new Color(230, 190, 60, 255) : PanelInk;
        Raylib.DrawText(text, x + (width - textWidth) / 2, y + (height - fontSize) / 2, fontSize, color);
    }

    private static readonly Color PanelFill = new(255, 250, 235, 220);
    private static readonly Color PanelInk = new(110, 70, 35, 255);

    /// <summary>
    /// Health bars for every living Bramblekin and the Wolf Spider, each
    /// projected from its 3D position into 2D screen space and drawn only
    /// while it's actually missing Health — a full-health entity gets no bar
    /// at all, so the battlefield doesn't get cluttered by default.
    /// </summary>
    private static void DrawHealthBars(Camera3D camera, World world)
    {
        for (int i = 0; i < world.Colony.Count; i++)
        {
            Bramblekin b = world.Colony[i];
            // Raylib Culling: a Bramblekin entirely outside the camera's
            // current view has no business drawing a health bar either.
            if (!b.IsDead && IsPointOnScreen(camera, b.Position))
                DrawHealthBar(camera, b.Position + new Vector3(0, Bramblekin.BodyHeight + 0.15f, 0), b.Health, Bramblekin.MaxHealth);
        }

        if (world.Spider is { } spider)
            DrawHealthBar(camera, spider.Position + new Vector3(0, WolfSpider.BodyRadius * 2f + 0.3f, 0), spider.Health, WolfSpider.MaxHealth);

        // Base Razing: a Village Heart under attack shows its own bar too,
        // same hidden-until-damaged rule as everything else here.
        foreach (VillageHeart village in world.Villages)
            DrawHealthBar(camera, village.Center + new Vector3(0, VillageHeart.Height + 0.3f, 0), village.Health, VillageHeart.MaxHealth);
    }

    /// <summary>Basic bounds check: true unless <paramref name="worldPosition"/> projects to a screen point entirely outside the camera's current viewport — used to skip health-bar/UI draw calls for off-screen entities.</summary>
    private static bool IsPointOnScreen(Camera3D camera, Vector3 worldPosition)
    {
        const float margin = 40f;
        Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);
        return screen.X >= -margin && screen.X <= Raylib.GetScreenWidth() + margin &&
               screen.Y >= -margin && screen.Y <= Raylib.GetScreenHeight() + margin;
    }

    /// <summary>A small red-background/green-fill bar at <paramref name="worldPosition"/>'s projected screen point.</summary>
    private static void DrawHealthBar(Camera3D camera, Vector3 worldPosition, int health, int maxHealth)
    {
        if (health >= maxHealth)
            return;

        Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);
        const int width = 34, height = 5;
        var back = new Rectangle(screen.X - width / 2f, screen.Y - height / 2f, width, height);
        Raylib.DrawRectangleRec(back, Color.Red);
        var fill = back with { Width = back.Width * Math.Clamp((float)health / maxHealth, 0f, 1f) };
        Raylib.DrawRectangleRec(fill, Color.Green);
        Raylib.DrawRectangleLinesEx(back, 1f, Color.Black);
    }

    /// <summary>
    /// Upkeep/Starvation pop-ups: each rises and fades above the Village
    /// Heart's projected screen position over its lifetime.
    /// </summary>
    private static void DrawFloatingTexts(Camera3D camera, World world)
    {
        const int fontSize = 20;
        foreach (var text in world.FloatingTexts)
        {
            float age = World.FloatingTextDuration - text.TimeLeft;
            Vector3 worldPosition = text.Position + new Vector3(0, VillageHeart.Height + 0.3f + age * 0.6f, 0);
            Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);

            byte alpha = (byte)(255 * Math.Clamp(text.TimeLeft / World.FloatingTextDuration, 0f, 1f));
            var color = new Color(text.Color.R, text.Color.G, text.Color.B, alpha);
            int width = Raylib.MeasureText(text.Text, fontSize);
            Raylib.DrawText(text.Text, (int)(screen.X - width / 2f), (int)screen.Y, fontSize, color);
        }
    }

    /// <summary>
    /// Contextual Faction UI: Food (against the storage cap), Population,
    /// Militia and Morale for whichever single faction <see cref="World.SelectedFactionID"/>
    /// currently points at (tap a Village Heart to switch — see
    /// <see cref="World.TrySelectFactionAt"/>), top right. No longer a
    /// global aggregate across every faction on the map.
    /// </summary>
    private static void DrawColonyPanel(World world)
    {
        VillageHeart? village = world.SelectedVillage;
        if (village is null)
            return; // No faction founded yet.

        // UI Text Scaling: bumped from 24px so the Faction Ledger reads on a
        // mobile screen; width/lineHeight below already derive the panel's
        // background rectangle from fontSize/lineHeight, so it grows to fit
        // automatically.
        const int fontSize = 32, lineHeight = 40;
        int militia = world.Colony.Count(b => !b.IsDead && b.FactionID == village.FactionID && b.Role == BramblekinRole.Militia);
        int builders = world.Colony.Count(b => !b.IsDead && b.FactionID == village.FactionID && b.Role == BramblekinRole.Builder);
        string header = $"{FactionColorName(village.FactionColor)} Faction ({village.Trait})";
        string food = $"Food Stored: {village.FoodStored} / {village.MaxFoodCapacity}";
        string population = $"Population: {village.Population} / {village.MaxPopulation}   Militia: {militia}   Builder: {builders}";
        string morale = $"Morale: {(int)village.Morale}%" +
                         (village.GatherersAreWeary ? " (Weary)" : village.BuildersAreInspired ? " (Inspired)" : "");
        // Tycoon Economy: Amber tacked onto this same panel, in Color.GOLD
        // so the tribe's banked wealth stands out from the survival stats
        // above it at a glance.
        string amber = $"| Amber: {village.AmberStored}";
        // The Nectar Brewery: Nectar gets its own line, in a distinct
        // purple/pink so this civilization buff currency reads apart from
        // Amber's gold at a glance.
        string nectar = $"Nectar: {village.NectarStored}";
        int width = Math.Max(Raylib.MeasureText(header, fontSize),
                    Math.Max(Raylib.MeasureText(food, fontSize),
                    Math.Max(Raylib.MeasureText(population, fontSize),
                    Math.Max(Raylib.MeasureText(morale, fontSize),
                    Math.Max(Raylib.MeasureText(amber, fontSize), Raylib.MeasureText(nectar, fontSize))))));
        int x = Raylib.GetScreenWidth() - width - 30;

        // Color Coding: the panel itself is tinted toward the selected
        // faction's own colour (blended with the usual parchment fill/ink
        // rather than replacing them outright, so the text stays legible
        // whatever the faction's hue) so the player always knows who
        // they're inspecting at a glance.
        Color fill = BlendToward(PanelFill, village.FactionColor, 0.4f);
        Color ink = BlendToward(PanelInk, village.FactionColor, 0.4f);

        const int topPadding = 20, textInset = 30;
        Raylib.DrawRectangle(x - 12, topPadding - 2, width + 24, lineHeight * 6 + 18, fill);
        Raylib.DrawRectangleLines(x - 12, topPadding - 2, width + 24, lineHeight * 6 + 18, ink);
        Raylib.DrawText(header, x, textInset, fontSize, ink);
        Raylib.DrawText(food, x, textInset + lineHeight, fontSize, ink);
        Raylib.DrawText(population, x, textInset + lineHeight * 2, fontSize, ink);
        Color moraleColor = village.GatherersAreWeary ? new Color(170, 60, 40, 255)
                           : village.BuildersAreInspired ? new Color(60, 130, 70, 255)
                           : ink;
        Raylib.DrawText(morale, x, textInset + lineHeight * 3, fontSize, moraleColor);
        Raylib.DrawText(amber, x, textInset + lineHeight * 4, fontSize, new Color(255, 203, 0, 255));
        Raylib.DrawText(nectar, x, textInset + lineHeight * 5, fontSize, new Color(215, 80, 210, 255));
    }

    /// <summary>
    /// Genesis: once <see cref="World.IsWorldExtinct"/>, blinks a large
    /// center-screen prompt (twice a second, driven off <see cref="Raylib.GetTime"/>
    /// so it needs no state of its own) telling the player to tap anywhere
    /// to reseed the world. The actual tap handling lives in
    /// <see cref="WorldTapInput.HandlePress"/>/TryGenesis.
    /// </summary>
    private static void DrawGenesisPrompt(World world)
    {
        if (!world.IsWorldExtinct)
            return;
        if ((int)(Raylib.GetTime() * 2) % 2 != 0)
            return; // Blink: visible for half of every second.

        const int titleSize = 44, subtitleSize = 28;
        string title = "World Dead.";
        string subtitle = "Tap anywhere to Seed new Life (Free)";
        int titleWidth = Raylib.MeasureText(title, titleSize);
        int subtitleWidth = Raylib.MeasureText(subtitle, subtitleSize);
        int centerX = Raylib.GetScreenWidth() / 2;
        int centerY = Raylib.GetScreenHeight() / 2;

        var color = new Color(200, 40, 40, 255);
        Raylib.DrawText(title, centerX - titleWidth / 2, centerY - titleSize, titleSize, color);
        Raylib.DrawText(subtitle, centerX - subtitleWidth / 2, centerY + 8, subtitleSize, color);
    }

    /// <summary>
    /// The Great Monument: a permanent, screen-wide banner for every
    /// faction that has ever finished one (see <see cref="World.CompletedMonuments"/>)
    /// — unlike the Genesis prompt, this never blinks and never goes away
    /// once shown, marking that faction's transition into an advanced
    /// civilization for the rest of the game. Stacks one line per faction
    /// if more than one tribe eventually gets there.
    /// </summary>
    private static void DrawMonumentAlerts(World world)
    {
        if (world.CompletedMonuments.Count == 0)
            return;

        const int fontSize = 36, lineHeight = 44;
        int barHeight = world.CompletedMonuments.Count * lineHeight + 20;
        int screenWidth = Raylib.GetScreenWidth();
        Raylib.DrawRectangle(0, 0, screenWidth, barHeight, new Color(20, 15, 5, 200));

        for (int i = 0; i < world.CompletedMonuments.Count; i++)
        {
            (_, Color factionColor) = world.CompletedMonuments[i];
            string text = $"{FactionColorName(factionColor)} Faction has completed the Monument!";
            int textWidth = Raylib.MeasureText(text, fontSize);
            Raylib.DrawText(text, (screenWidth - textWidth) / 2, 10 + i * lineHeight, fontSize, factionColor);
        }
    }

    /// <summary>
    /// Faction Personalities: a human-readable name for a faction's colour
    /// — the original Village Heart's green, or one of the four Schism
    /// palette colours (see <see cref="World.SchismFactionColors"/> — kept
    /// in sync with it by hand, since it's a display-only lookup) — for the
    /// panel header ("Blue Faction (Militaristic)"). Falls back to the
    /// FactionID if a colour somehow doesn't match (never expected in
    /// practice, since every Village Heart's colour comes from one of these
    /// two sources).
    /// </summary>
    private static string FactionColorName(Color color) => color switch
    {
        { R: 40, G: 180, B: 90 } => "Green",
        { R: 60, G: 120, B: 220 } => "Blue",
        { R: 225, G: 195, B: 55 } => "Yellow",
        { R: 205, G: 60, B: 55 } => "Red",
        { R: 150, G: 80, B: 195 } => "Purple",
        _ => "Unknown",
    };

    /// <summary>Blends <paramref name="baseColor"/> toward <paramref name="tint"/> by <paramref name="amount"/> (0 = unchanged, 1 = fully tint), keeping <paramref name="baseColor"/>'s own alpha.</summary>
    private static Color BlendToward(Color baseColor, Color tint, float amount) => new(
        (byte)Math.Clamp(baseColor.R + (tint.R - baseColor.R) * amount, 0, 255),
        (byte)Math.Clamp(baseColor.G + (tint.G - baseColor.G) * amount, 0, 255),
        (byte)Math.Clamp(baseColor.B + (tint.B - baseColor.B) * amount, 0, 255),
        baseColor.A);

    /// <summary>Small help text and debug counters in the bottom-left corner.</summary>
    private static void DrawHud(World world)
    {
        int Count(BramblekinState state) => world.Colony.Count(b => b.State == state);

        // UI Text Scaling: bumped from 20px so it reads on a mobile screen
        // held at arm's length; a background bar goes underneath both lines
        // so the now-larger text stays legible over a busy map instead of
        // the plain transparent overlay it used to sit on.
        const int fontSize = 26, lineHeight = 30;
        int y = Raylib.GetScreenHeight() - (lineHeight * 2 + 20);
        int barWidth = Raylib.GetScreenWidth();
        int barHeight = lineHeight * 2 + 20;
        Raylib.DrawRectangle(0, y - 10, barWidth, barHeight, new Color(0, 0, 0, 90));

        const string hint = "A pure autonomous simulation: no player intervention. Every Village Heart runs itself — sprouting, drafting Militia, farming and building on its own — while Militia trade blows with the spider toe-to-toe.";
        Raylib.DrawText(hint, 20, y, fontSize, Color.RayWhite);
        Raylib.DrawText(
            $"Bramblekin: {world.Colony.Count} " +
            $"(gathering {Count(BramblekinState.Gathering)}, returning {Count(BramblekinState.Returning)}, " +
            $"fleeing {Count(BramblekinState.Fleeing)}, defending {Count(BramblekinState.Defending)}, " +
            $"hunting {Count(BramblekinState.Hunting)}, raiding {Count(BramblekinState.Raiding)}, lost {world.Casualties})   " +
            $"Aphids: {world.Aphids.Count(a => !a.IsDead)}   " +
            $"Spider: {SpiderStatus(world)}   Sprouted: {world.Births}   FPS: {Raylib.GetFPS()}",
            20, y + lineHeight, fontSize, Color.RayWhite);
    }

    private static string SpiderStatus(World world) =>
        world.Spider is { } spider
            ? spider.State.ToString()
            : $"crushed, returns in {MathF.Ceiling(world.SpiderRespawnTimer)}s";
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
/// clean, undragged tap for faction-select/Genesis — see its own
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

    public void Draw()
    {
        // Solid grass plane, then a 1 m grid slightly above it to avoid
        // z-fighting between the two.
        Raylib.DrawPlane(new Vector3(0, GroundHeight, 0), new Vector2(Size, Size), new Color(86, 150, 60, 255));

        Rlgl.PushMatrix();
        Rlgl.Translatef(0, GroundHeight + 0.01f, 0);
        Raylib.DrawGrid((int)Size, 1f);
        Rlgl.PopMatrix();
    }
}

// =============================================================================
//  Input
// =============================================================================

/// <summary>
/// Pure Simulation: the player has no lever on the world any more — no
/// miracles, no Faith. The only two taps left are a Village Heart tap
/// (inspects that faction, see <see cref="World.TrySelectFactionAt"/>) and,
/// once every faction is gone, a tap anywhere on the ground to reseed the
/// world (Genesis, see <see cref="World.Genesis"/>) — an autonomous
/// simulation still needs some way back from total extinction rather than
/// sitting on a permanently empty map forever.
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
        // --- Genesis: the world is dead (every Village Heart gone) --
        // nothing below matters with nobody left to gather, build or fight,
        // so a tap anywhere on the ground reseeds it instead rather than
        // falling through to the usual faction-select handling.
        if (world.IsWorldExtinct)
        {
            TryGenesis(screenPosition, camera, world);
            return;
        }

        // --- Contextual Faction UI: a tap on or very near a Village Heart
        // inspects that faction.
        if (PickGround(camera, world.Terrain, screenPosition) is { } tapGround)
            world.TrySelectFactionAt(tapGround);
    }

    /// <summary>
    /// Free Extinction Recovery: raycasts the tap onto the terrain and, if
    /// it lands, reseeds the world right there. Genesis only ever runs from
    /// <see cref="World.IsWorldExtinct"/> in the first place; a tap that
    /// misses the terrain is simply ignored — the blinking prompt (<see cref="Game.DrawGenesisPrompt"/>)
    /// stays up and the player just taps again.
    /// </summary>
    private void TryGenesis(Vector2 screenPosition, Camera3D camera, World world)
    {
        Vector3? groundPoint = PickGround(camera, world.Terrain, screenPosition);
        if (groundPoint is null)
            return;

        world.Genesis(groundPoint.Value);
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
/// A solid circle on the ground that Bramblekin walk around — a Village
/// Heart's footprint. Coordinates are (x, z).
/// </summary>
public readonly record struct Obstacle(Vector2 Center, float Radius);

/// <summary>
/// Owns the simulation state — terrain, every faction's Village Heart, the
/// Acorn/Amber/Food Shard economy and the colony — and steps it in a fixed
/// order, entirely autonomously (Pure Simulation: there is no player lever
/// on any of this any more):
///
///   Dibs timeouts -> obstacle list -> shove food out from under obstacles
///   -> ambient prey -> Bramblekin (Militia poke, may damage the spider) ->
///   Wolf Spider (may bite Militia, may kill a Gatherer) -> Cooperative
///   Acorn Cracking's shatter check -> the Village Heart's own autonomous
///   business (Auto-Conscription, War Weariness, Upkeep, Auto-Sprout,
///   Auto-Construction, the True Schism) -> acorn/amber/berry/spider respawn
///   -> loot despawn.
/// </summary>
/// <summary>
/// The Spatial Grid: divides the map into fixed <see cref="ChunkSize"/>
/// (10m) chunks keyed by (chunk-x, chunk-z), so a nearest-target search
/// can look only at the handful of entities near the searcher instead of
/// scanning every entity on the whole map. Rebuilt from scratch once a
/// frame (see <see cref="World.RebuildSpatialGrids"/>) rather than having
/// each entity push incremental chunk-membership updates as it moves —
/// cheaper and simpler for a world that already rebuilds its obstacle
/// list the same way every frame, and exactly equivalent to updating each
/// entity's chunk registration on every move, since every entity moves at
/// most once per frame anyway.
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
    /// whole map.
    /// </summary>
    public void QueryNearby(Vector3 position, List<T> results)
    {
        results.Clear();
        var (cx, cz) = ChunkOf(position);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (_cells.TryGetValue((cx + dx, cz + dz), out List<T>? list))
                    results.AddRange(list);
            }
        }
    }
}

public sealed class World
{
    /// <summary>AI Time-Slicing: increments once per frame at the top of <see cref="Update(float)"/>; a Bramblekin only runs its heavy target-scanning ("Brain") logic on the frame where <c>FrameCounter % 15 == ID % 15</c>, staggering the load evenly across the colony.</summary>
    public long FrameCounter = 0;

    /// <summary>
    /// Food Abundance: seconds between checks that top the Acorn population
    /// back up to <see cref="MaxAcorns"/> — shortened from 6s so the map
    /// replenishes fast enough that a whole cluster of tribes isn't
    /// competing over the same trickle, and the map's dominant faction can't
    /// simply out-gather everyone else before food ever reaches the rest.
    /// </summary>
    private const float AcornSpawnInterval = 1.5f;

    /// <summary>Global Food Abundance: most Acorns allowed on the map at once, per faction — raised again for Pure Simulation, since every tribe's Auto-Sprout economy now has to feed and scale itself with no player miracle to fall back on.</summary>
    private const int MaxAcornsPerFaction = 15;

    /// <summary>
    /// Dynamic Ecosystem Scaling: the map-wide Acorn cap grows with the number of
    /// active <see cref="Villages"/> so a 5-faction map isn't starved by a cap sized
    /// for one colony. Never scales below a single faction's worth.
    /// </summary>
    private int MaxAcorns => MaxAcornsPerFaction * Math.Max(1, Villages.Count);

    /// <summary>How many Acorns the map starts with.</summary>
    private const int InitialAcorns = 2;

    /// <summary>
    /// Object Pooling: fixed size of the <see cref="Acorns"/> pool,
    /// pre-allocated once at startup instead of growing/shrinking with
    /// Add/RemoveAt every spawn/shatter. Generously above any realistic
    /// <see cref="MaxAcorns"/> ceiling (which itself scales with faction
    /// count via Dynamic Ecosystem Scaling) so the pool is never the thing
    /// that runs out.
    /// </summary>
    private const int AcornPoolCapacity = 300;

    /// <summary>Object Pooling: fixed size of the <see cref="FoodShards"/> pool — see <see cref="AcornPoolCapacity"/>. Generously above any realistic <see cref="MaxBerries"/> ceiling plus every Cooperative Acorn Cracking/Aphid-hunt/Base-Razing scatter that can pile on top of it.</summary>
    private const int FoodShardPoolCapacity = 1000;

    /// <summary>Object Pooling: fixed size of the <see cref="AmberNodes"/> pool — see <see cref="AcornPoolCapacity"/>. Generously above any realistic <see cref="MaxAmberOnMap"/> ceiling.</summary>
    private const int AmberPoolCapacity = 100;

    /// <summary>
    /// Cooperative Acorn Cracking: extra reach (m) beyond a Chitin-Mallet
    /// Gatherer's body radius and the Acorn's own radius — three of them
    /// must actually be touching it, not just nearby, for it to shatter.
    /// </summary>
    public const float AcornCoopContactMargin = 0.3f;

    /// <summary>
    /// Number of Food Shards a cracked acorn yields. High-yield: the reward
    /// for Cooperative Acorn Cracking, well above a Berry's 1 food or an
    /// Aphid's 2.
    /// </summary>
    private const int ShardsPerAcorn = 4;

    /// <summary>Food Stored consumed to sprout one new Bramblekin at the Village Heart.</summary>
    public const int FoodPerSprout = 5;

    /// <summary>
    /// Growth Buffer: Food Stored must reach this much before Auto-Sprout will
    /// spend <see cref="FoodPerSprout"/> of it on a new Bramblekin, leaving a
    /// 10-food safety buffer behind. Pure Simulation: with no player left to
    /// bail out a starving tribe, a colony that bred the instant it scraped
    /// together <see cref="FoodPerSprout"/> could sprout itself straight into
    /// a starvation cascade; this buffer keeps some Food Stored in reserve.
    /// </summary>
    public const int FoodSproutThreshold = 15;

    /// <summary>Seconds after a spider is crushed before a new one appears at an edge.</summary>
    public const float SpiderRespawnDelay = 45f;

    /// <summary>How long a crushed spider's splat mark stays on the ground, in seconds.</summary>
    private const float SplatDuration = 6f;

    /// <summary>Global Food Abundance: how often a wild Berry appears, in seconds — shortened again for Pure Simulation so the wilderness restocks fast enough for every tribe's own Auto-Sprout/Auto-Construction economy to keep scaling with no player miracle to bail it out.</summary>
    public const float BerrySpawnInterval = 0.75f;

    /// <summary>Global Food Abundance: most Berries allowed on the map (loose or carried) at once, per faction — raised again for Pure Simulation so a growing tribe's own economy always has enough wild food within reach to keep sprouting and building.</summary>
    public const int MaxBerriesPerFaction = 60;

    /// <summary>
    /// Dynamic Ecosystem Scaling: the map-wide Berry cap grows with the number of
    /// active <see cref="Villages"/> so a 5-faction map isn't starved by a cap sized
    /// for one colony. Never scales below a single faction's worth.
    /// </summary>
    public int MaxBerries => MaxBerriesPerFaction * Math.Max(1, Villages.Count);

    /// <summary>Aphid population the world tries to maintain, spread across the whole map.</summary>
    public const int MaxAphids = 12;

    /// <summary>Seconds between checks that top the Aphid population back up after a loss.</summary>
    public const float AphidRespawnDelay = 5f;

    /// <summary>Food Shards a Militia-hunted Aphid drops.</summary>
    private const int AphidFoodShardYield = 2;

    /// <summary>The Village Heart's base food storage cap, before any Granary bonus.</summary>
    public const int BaseMaxFoodCapacity = 10;

    /// <summary>How much a completed Granary permanently raises the food storage cap by.</summary>
    public const int GranaryFoodBonus = 10;

    /// <summary>Food Stored spent to place a Granary blueprint.</summary>
    public const int GranaryFoodCost = 10;

    /// <summary>How far (m) from the Village Heart an Auto-Granary may be placed.</summary>
    private const float GranaryPlacementRadius = 5f;

    /// <summary>
    /// Decoupled Economy: the most Granaries the Village Heart will ever
    /// build on its own — now that Granaries purely expand
    /// <see cref="VillageHeart.MaxFoodCapacity"/> (Wealth Accumulation) and
    /// no longer gate <see cref="VillageHeart.Population"/> growth at all
    /// (see <see cref="VillageHeart.MaxPopulation"/>/the Housing System), a
    /// thriving tribe can bank a genuinely massive food surplus: exactly
    /// <see cref="BaseMaxFoodCapacity"/> + 25*<see cref="GranaryFoodBonus"/>
    /// = 260 MaxFoodCapacity at the cap.
    /// </summary>
    public const int MaxGranaries = 25;

    /// <summary>The Housing System: Food Stored spent to place a Tent blueprint.</summary>
    public const int TentFoodCost = 8;

    /// <summary>The Housing System: how much a completed Tent permanently raises <see cref="VillageHeart.MaxPopulation"/> by.</summary>
    public const int TentPopulationBonus = 5;

    /// <summary>How far (m) from the Village Heart an Auto-Tent may be placed.</summary>
    private const float TentPlacementRadius = 5f;

    /// <summary>
    /// Hard Cap: the absolute ceiling on <see cref="VillageHeart.MaxPopulation"/>
    /// — the Village Heart never queues another Tent once it's reached this,
    /// full stop, no matter how much Food Stored is banked. This is also the
    /// True Schism's new trigger population (see <see cref="UpdateSchism"/>):
    /// once a tribe is physically maxed out on housing, splitting in two is
    /// the only way left for it to keep growing.
    /// </summary>
    public const int MaxPopulationCap = 40;

    /// <summary>The Split Fix: Food Stored the True Schism needs banked before it fires — no longer tied to MaxFoodCapacity, so a tribe sitting on a 260-capacity silo doesn't wait to fill it before relieving population pressure.</summary>
    public const int SchismFoodThreshold = 100;

    /// <summary>The Split Fix: exactly how many Bramblekin depart as Pioneers in a True Schism, regardless of the parent's total Population.</summary>
    public const int SchismPioneerCount = 20;

    /// <summary>The Split Fix: exactly how much Food Stored departs with the Pioneers in a True Schism.</summary>
    public const int SchismPioneerFood = 50;

    /// <summary>
    /// Upkeep Grace Period: extra seconds (on top of the normal
    /// <see cref="UpkeepInterval"/> cycle) before a freshly-founded Schism
    /// Village Heart pays its first Upkeep tax — see <see cref="FoundVillage"/>.
    /// Gives the Pioneers time to walk the ~<see cref="MinMigrationDistance"/>
    /// meters to their new home and start gathering before the tax bill
    /// arrives. Applied by adding to <see cref="VillageHeart.UpkeepTimer"/>'s
    /// starting value rather than by setting it negative: UpkeepTimer counts
    /// down to (and fires at) zero, so a negative starting value would fire
    /// the tax on the very next tick instead of delaying it — the opposite
    /// of a grace period.
    /// </summary>
    public const float SchismUpkeepGracePeriod = 30f;

    /// <summary>Food Stored spent to place a Spore Farm blueprint.</summary>
    public const int SporeFarmFoodCost = 10;

    /// <summary>Population needed before the Village Heart will build a Spore Farm — the Domestic Spore Farm: a big tribe's internal food loop, so its Gatherers don't need to cross the map for every Berry. Lowered from 15 so it lands early enough to actually save a struggling tribe, not just reward one that's already thriving.</summary>
    public const int SporeFarmPopulationThreshold = 10;

    /// <summary>
    /// Scaling Domestic Farms: the most Spore Farms a single Village Heart
    /// will ever build. Population / <see cref="SporeFarmPopulationThreshold"/>
    /// (rounded down) queues each one — the 1st at 10 Population, 2nd at 20,
    /// 3rd at 30, capped here at the 4th (40+).
    /// </summary>
    public const int MaxSporeFarmsPerVillage = 4;

    /// <summary>How far (m) from the Village Heart a Spore Farm may be placed — kept close, near the village's centre.</summary>
    private const float SporeFarmPlacementRadius = 5f;

    /// <summary>Tycoon Economy + Food Abundance: seconds between each Amber spawn attempt — still slower than Berries or Acorns (Amber stays a scarcer, higher-value find), but shortened from 20s so a multi-tribe map doesn't leave most factions without a realistic shot at ever finding one.</summary>
    public const float AmberSpawnInterval = 15f;

    /// <summary>Tycoon Economy: most Amber allowed on the map at once, per faction — kept scarce per faction, but scaling with the number of active <see cref="Villages"/> (same Dynamic Ecosystem Scaling as <see cref="MaxAcornsPerFaction"/>/<see cref="MaxBerriesPerFaction"/>) so a crowded map isn't still capped at a single-tribe's worth that only whichever faction is already dominant ever reaches first.</summary>
    private const int MaxAmberPerFaction = 2;

    /// <summary>The map-wide Amber cap — see <see cref="MaxAmberPerFaction"/>. Never scales below a single faction's worth.</summary>
    private int MaxAmberOnMap => MaxAmberPerFaction * Math.Max(1, Villages.Count);

    /// <summary>Population needed before a Village Heart will queue a Trading Post — see <see cref="World.UpdateAutoTradingPost"/>.</summary>
    public const int TradingPostPopulationThreshold = 15;

    /// <summary>Amber Stored spent to queue a Trading Post blueprint.</summary>
    public const int TradingPostAmberCost = 5;

    /// <summary>How far (m) from the Village Heart an Auto-Trading-Post may be placed.</summary>
    private const float TradingPostPlacementRadius = 5f;

    /// <summary>Emergency Food Import: Amber spent per import.</summary>
    public const int EmergencyImportAmberCost = 1;

    /// <summary>Emergency Food Import: Food Stored gained per import.</summary>
    public const int EmergencyImportFoodGain = 5;

    /// <summary>The Nectar Brewery: Population needed before a Village Heart will queue one — see <see cref="UpdateAutoBrewery"/>.</summary>
    public const int BreweryPopulationThreshold = 20;

    /// <summary>The Nectar Brewery: Amber Stored spent to queue its blueprint.</summary>
    public const int BreweryAmberCost = 10;

    /// <summary>How far (m) from the Village Heart an Auto-Brewery may be placed.</summary>
    private const float BreweryPlacementRadius = 5f;

    /// <summary>The Nectar Brewery: seconds between each brew attempt once built — see <see cref="UpdateNectarBrewery"/>.</summary>
    public const float BreweryInterval = 30f;

    /// <summary>The Nectar Brewery: Food Stored consumed per brew.</summary>
    public const int BreweryFoodCost = 2;

    /// <summary>The Nectar Brewery: Amber Stored consumed per brew.</summary>
    public const int BreweryAmberUpkeep = 1;

    /// <summary>The Nectar Brewery: Nectar produced per successful brew.</summary>
    public const int BreweryNectarYield = 1;

    /// <summary>The Nectar Brewery: permanent Gatherer walk-speed bonus per point of <see cref="VillageHeart.NectarStored"/> — see <see cref="Bramblekin.EffectiveWalkSpeed"/>.</summary>
    public const float NectarSpeedBonusPerPoint = 0.05f;

    /// <summary>The Nectar Brewery: hard ceiling on the cumulative Nectar speed bonus (+50%), so an old enough civilization can't eventually move arbitrarily fast.</summary>
    public const float MaxNectarSpeedBonus = 0.5f;

    /// <summary>The Great Monument: Population needed before a tribe stops all other construction and commits to one — see <see cref="UpdateAutoMonument"/>.</summary>
    public const int MonumentPopulationThreshold = 40;

    /// <summary>The Great Monument: Amber Stored spent to queue its blueprint.</summary>
    public const int MonumentAmberCost = 50;

    /// <summary>How far (m) from the Village Heart the Monument may be placed — further out than the smaller buildings, since it's the biggest structure on the map.</summary>
    private const float MonumentPlacementRadius = 7f;

    /// <summary>Economic Buff: how often (seconds) the Village Heart pays its Upkeep food tax — doubled from 15s so Food Stored lasts much longer between taxes, leaving room to gather Amber instead of running a bare-survival loop.</summary>
    private const float UpkeepInterval = 30f;

    /// <summary>How long (seconds) a floating text pop-up (Upkeep, Starvation) stays on screen.</summary>
    public const float FloatingTextDuration = 1.5f;

    /// <summary>
    /// Cultural Borders: the base term of every Village Heart's own dynamic
    /// <see cref="VillageHeart.TerritoryRadius"/> (before its
    /// AmberStored/NectarStored bonus) — Militia never target a hostile
    /// (Aphid or Wolf Spider) further than that from their own Village
    /// Heart, and Gatherers prefer unclaimed food within it before looking
    /// anywhere else on the map (see <see cref="NearestAvailableShard"/>) —
    /// unless that Village Heart is starving (see <see cref="DesperationFoodThreshold"/>).
    /// Also the fallback territory reach for a homeless Bramblekin (one
    /// with no Village Heart of its own to scale off).
    /// </summary>
    public const float BaseTerritoryRadius = 15f;

    /// <summary>Cultural Borders: how much a Village Heart's <see cref="VillageHeart.TerritoryRadius"/> grows per point of <see cref="VillageHeart.AmberStored"/>.</summary>
    public const float TerritoryRadiusPerAmber = 0.5f;

    /// <summary>Cultural Borders: how much a Village Heart's <see cref="VillageHeart.TerritoryRadius"/> grows per point of <see cref="VillageHeart.NectarStored"/> — Nectar buys far more cultural reach than raw Amber, since it takes a whole Nectar Brewery economy to produce at all.</summary>
    public const float TerritoryRadiusPerNectar = 2.0f;

    /// <summary>
    /// Base Defense Aggro: any foreign Bramblekin caught within this
    /// distance of a Village Heart's own centre — much tighter than its
    /// full territory ring — is treated as an
    /// immediate, lethal threat regardless of any existing peace or Truce,
    /// instantly triggering a Blood Feud. This close to the Heart itself,
    /// there's no such thing as an innocent bystander — see
    /// <see cref="NearestForeignBramblekinNearHeart"/>.
    /// </summary>
    public const float BaseDefenseAggroRadius = 5f;

    /// <summary>
    /// Desperation Mode: once a Village Heart's Food Stored drops below
    /// this, its Gatherers stop preferring food within its own territory
    /// ring and instead track the nearest unclaimed food anywhere within
    /// <see cref="MaxGatherSearchRadius"/> — starving is worse than a walk,
    /// but the walk still isn't unlimited (see <see cref="MaxGatherSearchRadius"/>).
    /// </summary>
    public const int DesperationFoodThreshold = 5;

    /// <summary>
    /// Maximum Search Radius: a Gatherer never even considers a Food Shard
    /// or Acorn further than this from its current position, full stop —
    /// not a preference like a Village Heart's own territory ring, a hard
    /// cutoff with no last-resort exception. This is what actually stops a
    /// freshly-split Schism splinter's Gatherers from trekking all the way
    /// back to the parent tribe's base: without a hard ceiling, the parent's
    /// food was still technically the nearest *available* food on the whole
    /// map (everything closer already claimed, or just not there yet), and
    /// Strict Border Control (see <see cref="IsForeignTerritory"/>) only
    /// excludes foreign territory, it doesn't cap distance on its own. See
    /// <see cref="NearestAvailableShard"/>/<see cref="NearestClaimableAcorn"/>;
    /// coming up empty sends the Gatherer to <see cref="Bramblekin.StartWanderingNearHome"/>
    /// instead, to wait out its own Spore Farm's next Berry.
    /// </summary>
    public const float MaxGatherSearchRadius = 25f;

    /// <summary>
    /// Dibs failsafe: a Food Shard claimed but not actually picked up within
    /// this many seconds of game time has its claim force-released, so a
    /// claimant that's stuck, jittering at high Debug Time Scale, or
    /// otherwise never closes the distance can't lock it away from everyone
    /// else forever. See <see cref="UpdateFoodClaimTimeouts"/>.
    /// </summary>
    public const float FoodClaimTimeoutSeconds = 15f;

    /// <summary>The Blood Feud: how long (s) a declared war lasts before peace is automatically restored — see <see cref="DeclareBloodFeud"/>.</summary>
    public const float BloodFeudDurationSeconds = 120f;

    /// <summary>Strict Migration Distance: how far (m) a Migration Target must be from every existing Village Heart — kept comfortably outside a 20m territory ring plus its neighbour's so a fresh Schism splinter doesn't spawn straight into a Border War.</summary>
    private const float MinMigrationDistance = 35f;

    /// <summary>How many random coordinates <see cref="RandomMigrationTarget"/>/<see cref="RandomRefugeeTarget"/> try before giving up on a clean gap.</summary>
    private const int MigrationTargetAttempts = 50;

    /// <summary>The palette a new Schism faction's colour is drawn from, cycling once all four are in use.</summary>
    private static readonly Color[] SchismFactionColors =
    {
        new(60, 120, 220, 255),  // Blue
        new(225, 195, 55, 255),  // Yellow
        new(205, 60, 55, 255),   // Red
        new(150, 80, 195, 255),  // Purple
    };

    /// <summary>The next Schism's FactionID. Starts at 1 — Faction 0 is the original Village Heart.</summary>
    private int _nextSchismFactionId = 1;

    /// <summary>Morale cap. Also the starting amount: the colony begins confident.</summary>
    public const float MaxMorale = 100f;

    /// <summary>Morale lost the instant a Bramblekin is killed.</summary>
    public const float MoraleLossPerKill = 20f;

    /// <summary>Morale lost per second while the Wolf Spider is actively terrorizing the village (Hunting or Pouncing).</summary>
    public const float MoraleLossPerSecondTerrorized = 2f;

    /// <summary>Morale regained per second whenever the spider isn't actively terrorizing anyone.</summary>
    public const float MoraleRecoveryPerSecond = 1f;

    /// <summary>Below this Morale, Gatherers are Weary (see <see cref="WearySpeedMultiplier"/>).</summary>
    public const float WearyMoraleThreshold = 50f;

    /// <summary>Above this Morale, Builders work at <see cref="HighMoraleBuildMultiplier"/> speed.</summary>
    public const float HighMoraleThreshold = 80f;

    /// <summary>A Weary Gatherer's walk speed as a fraction of normal (a 40% reduction).</summary>
    public const float WearySpeedMultiplier = 0.6f;

    /// <summary>Construction Progress multiplier for a Builder while Morale is above <see cref="HighMoraleThreshold"/>.</summary>
    public const float HighMoraleBuildMultiplier = 2f;

    private readonly List<Obstacle> _obstacles = new();
    private readonly List<(Vector3 Position, float TimeLeft)> _splats = new();
    private readonly List<(Vector3 Position, string Text, Color Color, float TimeLeft)> _floatingTexts = new();
    private float _acornSpawnTimer = AcornSpawnInterval;
    private float _amberSpawnTimer = AmberSpawnInterval;

    // Deferred creation/destruction. Nothing below is added to or removed
    // from Colony/FoodShards while any part of the frame might still be
    // iterating them (a returning Bramblekin sprouting a new one while the
    // Colony foreach that is updating it is still running, for example).
    // Entities are instead queued here and the queues are drained once, in
    // CommitPendingChanges(), after every Update() and Draw() this frame.
    private readonly List<Bramblekin> _pendingBramblekinSpawns = new();
    private readonly List<Bramblekin> _pendingBramblekinRemovals = new();
    /// <summary>Object Pooling: queued (position, kind) spawn requests, applied by activating a pool slot at flush time rather than constructing a FoodShard up front — see <see cref="CommitPendingChanges"/>.</summary>
    private readonly List<(Vector3 Position, FoodShardKind Kind)> _pendingShardSpawns = new();
    private readonly List<FoodShard> _pendingShardRemovals = new();
    private readonly List<AmberNode> _pendingAmberRemovals = new();
    private readonly List<Aphid> _pendingAphidSpawns = new();
    private readonly List<Aphid> _pendingAphidRemovals = new();
    private readonly List<SpiderFang> _pendingFangSpawns = new();
    private readonly List<SpiderFang> _pendingFangRemovals = new();
    private readonly List<Chitin> _pendingChitinSpawns = new();
    private readonly List<Chitin> _pendingChitinRemovals = new();

    // The Spatial Grid: see RebuildSpatialGrids. Query results are written
    // into these reusable scratch buffers rather than allocating a fresh
    // list per targeting call — safe because the whole simulation runs
    // single-threaded and no query result is held across another query.
    private readonly SpatialGrid<FoodShard> _foodGrid = new();
    private readonly SpatialGrid<Acorn> _acornGrid = new();
    private readonly SpatialGrid<AmberNode> _amberGrid = new();
    private readonly SpatialGrid<Bramblekin> _colonyGrid = new();
    private readonly List<FoodShard> _foodQueryBuffer = new();
    private readonly List<Acorn> _acornQueryBuffer = new();
    private readonly List<AmberNode> _amberQueryBuffer = new();
    private readonly List<Bramblekin> _colonyQueryBuffer = new();

    private float _berrySpawnTimer = BerrySpawnInterval;
    private float _aphidRespawnTimer = AphidRespawnDelay;

    public Terrain Terrain { get; }

    /// <summary>
    /// Every faction's Village Heart. Phase 3 prep: the world still starts
    /// with exactly one (Faction 0, green — see <see cref="Village"/>), but
    /// the economy loop below already runs independently per entry so a
    /// splinter faction can simply be appended to this list later.
    /// </summary>
    public List<VillageHeart> Villages { get; } = new();

    /// <summary>The original Village Heart (Faction 0) — a convenience for the many single-village call sites (HUD, the Wolf Spider's threat model) that aren't faction-aware yet.</summary>
    public VillageHeart Village => Villages[0];

    /// <summary>
    /// Contextual Faction UI: which faction the top-right panel currently
    /// inspects. Defaults to Faction 0 — the original Village Heart — so
    /// the panel reads the same as before on a fresh game. Changed by
    /// <see cref="TrySelectFactionAt"/> whenever the player taps on or near
    /// a Village Heart.
    /// </summary>
    public int SelectedFactionID { get; set; }

    /// <summary>The Village Heart <see cref="SelectedFactionID"/> currently points at, if that faction still exists (null once it's been razed — see <see cref="DestroyVillageHeart"/> — or, transiently, right after Genesis reseeds Faction 0 from total extinction).</summary>
    public VillageHeart? SelectedVillage => VillageFor(SelectedFactionID);

    /// <summary>How near a tap has to land to a Village Heart's centre to select its faction — generous, well beyond the 1.6m footprint, since it's meant to catch an imprecise finger tap.</summary>
    public const float FactionSelectionRadius = 2.5f;

    /// <summary>
    /// Genesis: true once every Village Heart is gone — a dead end no
    /// autonomous system (Auto-Sprout, the Job Manager, anything) can ever
    /// climb out of on its own. See <see cref="WorldTapInput"/>'s tap
    /// handling (which reseeds Faction 0 via <see cref="Genesis"/>) and
    /// <see cref="Game.DrawGenesisPrompt"/> for what happens while this holds.
    /// </summary>
    public bool IsWorldExtinct => Villages.Count == 0;

    /// <summary>
    /// Contextual Faction UI: a single tap on or very near a Village
    /// Heart's footprint switches <see cref="SelectedFactionID"/> to that
    /// faction. A miss (too far from every Village Heart) leaves the
    /// current selection alone.
    /// </summary>
    public void TrySelectFactionAt(Vector3 groundPoint)
    {
        VillageHeart? nearest = null;
        float bestDistanceSquared = FactionSelectionRadius * FactionSelectionRadius;

        foreach (VillageHeart village in Villages)
        {
            float distanceSquared = Vector3.DistanceSquared(groundPoint, village.Center);
            if (distanceSquared <= bestDistanceSquared)
            {
                nearest = village;
                bestDistanceSquared = distanceSquared;
            }
        }

        if (nearest is not null)
            SelectedFactionID = nearest.FactionID;
    }

    /// <summary>Every Acorn currently on the map — richly populated (see <see cref="MaxAcorns"/>) rather than one at a time.</summary>
    public List<Acorn> Acorns { get; } = new();

    /// <summary>Tycoon Economy: every loose Amber currently on the map — scarce on purpose, capped map-wide at <see cref="MaxAmberOnMap"/>.</summary>
    public List<AmberNode> AmberNodes { get; } = new();

    public List<FoodShard> FoodShards { get; } = new();
    public List<Bramblekin> Colony { get; } = new();
    public List<Aphid> Aphids { get; } = new();
    public WolfSpider? Spider { get; set; }
    public Random Rng { get; }

    /// <summary>
    /// Spider Fangs dropped by a dead Wolf Spider. Individual Equipment:
    /// an un-upgraded Militia unit that touches one instantly equips
    /// <see cref="Bramblekin.HasFangPike"/> and it despawns — no carrying
    /// it home, no village-wide unlock.
    /// </summary>
    public List<SpiderFang> Fangs { get; } = new();

    /// <summary>
    /// Chitin dropped by a dead Wolf Spider alongside its Fang. Individual
    /// Equipment: an un-upgraded Gatherer that touches one instantly equips
    /// <see cref="Bramblekin.HasChitinMallet"/> and it despawns.
    /// </summary>
    public List<Chitin> Chitins { get; } = new();

    /// <summary>Under-construction sites; a Blueprint becomes a <see cref="Building"/> once its Construction Progress is complete.</summary>
    public List<Blueprint> Blueprints { get; } = new();

    /// <summary>Finished structures: Granaries, which permanently raise <see cref="VillageHeart.MaxFoodCapacity"/>.</summary>
    public List<Building> Buildings { get; } = new();

    /// <summary>Bramblekin lost to predators so far.</summary>
    public int Casualties { get; private set; }

    /// <summary>Bramblekin sprouted from stored food so far.</summary>
    public int Births { get; private set; }

    /// <summary>Seconds until a crushed spider is replaced (only meaningful while <see cref="Spider"/> is null).</summary>
    public float SpiderRespawnTimer { get; private set; }

    /// <summary>Solid circles every walker (Bramblekin, Aphids, the Wolf Spider) must steer around. Rebuilt every frame.</summary>
    public IReadOnlyList<Obstacle> Obstacles => _obstacles;

    /// <summary>Floating text pop-ups (Upkeep paid, Starvation) still fading out above the Village Heart.</summary>
    public IReadOnlyList<(Vector3 Position, string Text, Color Color, float TimeLeft)> FloatingTexts => _floatingTexts;

    /// <summary>
    /// The Great Monument: every faction that has ever completed one, in
    /// completion order — each entry stays here for the rest of the game
    /// (see <see cref="CompleteBlueprint"/>), driving the permanent,
    /// screen-wide "[Faction] has completed the Monument!" alert (see
    /// <see cref="Game.DrawMonumentAlerts"/>). Never cleared: a civilization
    /// that reaches this point stays marked as advanced for good, even if
    /// its Village Heart is later razed.
    /// </summary>
    public IReadOnlyList<(int FactionID, Color FactionColor)> CompletedMonuments => _completedMonuments;
    private readonly List<(int FactionID, Color FactionColor)> _completedMonuments = new();

    public World(Terrain terrain, Random rng, int colonySize)
    {
        Terrain = terrain;
        Rng = rng;

        // The original Village Heart: Faction 0, green — sits just off the
        // centre of the garden. It is a solid circle for walkers, same as
        // any faction that joins it later.
        var villageHeart = new VillageHeart(new Vector3(-2f, Terrain.GroundHeight, -2f), factionId: 0, factionColor: new Color(40, 180, 90, 255), rng);
        villageHeart.UpkeepTimer = UpkeepInterval;
        Villages.Add(villageHeart);
        RebuildObstacles();

        // Object Pooling: Acorns/FoodShards/AmberNodes are fixed-size pools,
        // every slot constructed once here (inactive) rather than
        // instantiated and destroyed per spawn/pickup/despawn — see
        // ActivateAcorn/ActivateFoodShard/ActivateAmberNode.
        for (int i = 0; i < AcornPoolCapacity; i++)
            Acorns.Add(new Acorn());
        for (int i = 0; i < FoodShardPoolCapacity; i++)
            FoodShards.Add(new FoodShard());
        for (int i = 0; i < AmberPoolCapacity; i++)
            AmberNodes.Add(new AmberNode());

        for (int i = 0; i < InitialAcorns; i++)
            ActivateAcorn(RandomAcornSpot());

        for (int i = 0; i < colonySize; i++)
            Colony.Add(new Bramblekin(RandomFreePoint(Bramblekin.BodyRadius, Bramblekin.EdgeMargin), rng, villageHeart.FactionID, villageHeart.FactionColor));

        for (int i = 0; i < MaxAphids; i++)
            Aphids.Add(new Aphid(RandomFreePoint(Aphid.BodyRadius, Aphid.EdgeMargin), rng));
    }

    /// <summary>Object Pooling: activates the first inactive slot in <see cref="Acorns"/> at <paramref name="position"/>, or silently does nothing if the pool is exhausted.</summary>
    private void ActivateAcorn(Vector3 position)
    {
        foreach (Acorn acorn in Acorns)
        {
            if (!acorn.IsActive)
            {
                acorn.Activate(position);
                return;
            }
        }
    }

    /// <summary>Object Pooling: activates the first inactive slot in <see cref="FoodShards"/> at <paramref name="position"/>, or silently does nothing if the pool is exhausted.</summary>
    private void ActivateFoodShard(Vector3 position, FoodShardKind kind = FoodShardKind.Cracked)
    {
        foreach (FoodShard shard in FoodShards)
        {
            if (!shard.IsActive)
            {
                shard.Activate(position, kind);
                return;
            }
        }
    }

    /// <summary>Object Pooling: activates the first inactive slot in <see cref="AmberNodes"/> at <paramref name="position"/>, or silently does nothing if the pool is exhausted.</summary>
    private void ActivateAmberNode(Vector3 position)
    {
        foreach (AmberNode amber in AmberNodes)
        {
            if (!amber.IsActive)
            {
                amber.Activate(position);
                return;
            }
        }
    }

    /// <summary>The Village Heart whose Faction matches <paramref name="factionId"/>, if any.</summary>
    public VillageHeart? VillageFor(int factionId) => Villages.FirstOrDefault(v => v.FactionID == factionId);

    /// <summary>
    /// Faction Personalities: the Population divisor for a Village Heart's
    /// Auto-Conscription target, per its fixed-for-life <see cref="FactionTrait"/>
    /// — Militaristic wants a Militia unit for every 2 Gatherers (Population / 3),
    /// Balanced and Agrarian for every 5 (Population / 6). Pure Simulation: with
    /// no player left to bail out a starving tribe, every non-Militaristic
    /// faction keeps its Militia small so more Bramblekin work as Gatherers.
    /// </summary>
    private static int MilitiaTargetDivisorFor(FactionTrait trait) => trait switch
    {
        FactionTrait.Militaristic => 3,
        _ => 6,
    };

    /// <summary>
    /// The Job Manager: each Village Heart's own autonomous quartermaster.
    /// Every frame it recomputes its own faction's Population — strictly
    /// its own FactionID's living Bramblekin, never any other faction's —
    /// and <see cref="VillageHeart.MilitiaTarget"/> per its own
    /// <see cref="FactionTrait"/> (see <see cref="MilitiaTargetDivisorFor"/>),
    /// and nudges its actual Militia headcount one step toward it: promoting
    /// the nearest same-faction Gatherer if under target, or standing down
    /// the nearest same-faction Militia unit (pike put away, sent back to
    /// Wandering) if over. One change per frame is plenty; at 60 fps even a
    /// large jump (a mass Sprout, or a Wolf Spider kill dropping the
    /// population) closes out in a fraction of a second, with no player
    /// input needed.
    ///
    /// Builder Conscription: the same nudge-one-step-per-frame treatment
    /// also keeps exactly one dedicated Builder on hand whenever this
    /// faction has an incomplete Blueprint (<see cref="HasIncompleteBlueprintFor"/>)
    /// — promoting the nearest Gatherer to work it, or standing the Builder
    /// back down to Gatherer once nothing's left to build — so the rest of
    /// the colony's Gatherers never have to abandon food duty to pick up a
    /// Blueprint themselves.
    /// </summary>
    private void UpdateJobManager(VillageHeart village)
    {
        village.Population = Colony.Count(b => !b.IsDead && b.FactionID == village.FactionID);
        village.MilitiaTarget = village.Population / MilitiaTargetDivisorFor(village.Trait);

        int current = Colony.Count(b => !b.IsDead && b.FactionID == village.FactionID && b.Role == BramblekinRole.Militia);
        if (current < village.MilitiaTarget)
            NearestByRole(village, BramblekinRole.Gatherer)?.PromoteToMilitia();
        else if (current > village.MilitiaTarget)
            NearestByRole(village, BramblekinRole.Militia)?.DemoteToGatherer(this);

        int builderTarget = HasIncompleteBlueprintFor(village.FactionID) ? 1 : 0;
        int currentBuilders = Colony.Count(b => !b.IsDead && b.FactionID == village.FactionID && b.Role == BramblekinRole.Builder);
        if (currentBuilders < builderTarget)
            NearestByRole(village, BramblekinRole.Gatherer)?.PromoteToBuilder();
        else if (currentBuilders > builderTarget)
            NearestByRole(village, BramblekinRole.Builder)?.DemoteToGatherer(this);
    }

    /// <summary>
    /// War Weariness: Morale drains at <see cref="MoraleLossPerSecondTerrorized"/>
    /// per second while the spider is actively Hunting or Pouncing — the two
    /// states that mean it's actually terrorizing the village, as opposed to
    /// prowling, staring at a distraction, feeding, or simply being absent —
    /// and recovers at <see cref="MoraleRecoveryPerSecond"/> the rest of the
    /// time. A kill drains it separately and immediately, in <see cref="Kill"/>.
    /// </summary>
    private void UpdateMorale(VillageHeart village, float deltaTime)
    {
        bool terrorized = Spider is { State: SpiderState.Hunting or SpiderState.Pouncing };
        village.Morale = terrorized
            ? MathF.Max(0f, village.Morale - MoraleLossPerSecondTerrorized * deltaTime)
            : MathF.Min(MaxMorale, village.Morale + MoraleRecoveryPerSecond * deltaTime);
    }

    /// <summary>The living Bramblekin of <paramref name="role"/> and <paramref name="village"/>'s Faction nearest that Village Heart, if any.</summary>
    private Bramblekin? NearestByRole(VillageHeart village, BramblekinRole role)
    {
        Bramblekin? nearest = null;
        float bestDistanceSquared = float.MaxValue;
        for (int i = Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = Colony[i];
            if (bramblekin.IsDead || bramblekin.Role != role || bramblekin.FactionID != village.FactionID)
                continue;

            float distanceSquared = Vector3.DistanceSquared(bramblekin.Position, village.Center);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                nearest = bramblekin;
            }
        }
        return nearest;
    }

    /// <summary>Closest (m) a Wolf Spider may spawn to any Village Heart — organic roaming: it starts out in the wilderness and only closes in on a village if it happens to wander within earshot (<see cref="WolfSpider.VibrationRadius"/>) of a Bramblekin, rather than beginning right on a faction's doorstep.</summary>
    private const float MinSpiderSpawnDistanceFromVillage = 25f;

    /// <summary>Spawns a Wolf Spider at a uniformly random spot on the map, at least <see cref="MinSpiderSpawnDistanceFromVillage"/> meters from every Village Heart.</summary>
    public void SpawnSpiderNearVillage()
    {
        Vector3 position = Vector3.Zero;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            position = Terrain.RandomPoint(Rng, margin: 1.5f);
            if (Villages.All(v => Vector3.Distance(position, v.Center) >= MinSpiderSpawnDistanceFromVillage) &&
                !IsBlocked(position, WolfSpider.BodyRadius))
                break;
        }

        Spider = new WolfSpider(position, Rng);
    }

    /// <summary>
    /// A Bramblekin caught by a predator: it drops its food and is marked
    /// dead immediately (so nothing keeps hunting or gathering with it), but
    /// its removal from <see cref="Colony"/> is deferred to the end of the
    /// frame so this is safe to call from inside a Colony iteration (e.g.
    /// the Wolf Spider's pounce, mid-way through updating the colony).
    /// </summary>
    public void Kill(Bramblekin bramblekin)
    {
        if (bramblekin.IsDead)
            return; // Already caught this frame; don't double-count it.

        bramblekin.MarkDead();
        _pendingBramblekinRemovals.Add(bramblekin);
        Casualties++;

        VillageHeart? home = VillageFor(bramblekin.FactionID);
        if (home is not null)
            home.Morale = MathF.Max(0f, home.Morale - MoraleLossPerKill);
    }

    /// <summary>Cultural Borders: whether <paramref name="claimant"/>'s Militia has anything huntable within <paramref name="home"/>'s own (wealth-scaled — see <see cref="VillageHeart.TerritoryRadius"/>) territory ring.</summary>
    public bool HasHuntableAphidNearVillage(Bramblekin claimant, VillageHeart home) => NearestLiveAphidNearVillage(claimant.Position, claimant, home) is not null;

    /// <summary>
    /// Cultural Borders + Dibs: the nearest still-live, unclaimed (or
    /// already claimed by <paramref name="claimant"/>) Aphid to
    /// <paramref name="from"/>, considering only ones within
    /// <paramref name="home"/>'s own <see cref="VillageHeart.TerritoryRadius"/>
    /// — a hard boundary, unlike a Gatherer's food search, which falls back
    /// to the wider map. Militia simply have nothing to hunt beyond it.
    /// </summary>
    public Aphid? NearestLiveAphidNearVillage(Vector3 from, Bramblekin claimant, VillageHeart home)
    {
        Aphid? nearest = null;
        float bestDistanceSquared = float.MaxValue;
        float territoryRadiusSquared = home.TerritoryRadius * home.TerritoryRadius;
        for (int i = Aphids.Count - 1; i >= 0; i--)
        {
            Aphid aphid = Aphids[i];
            if (aphid.IsDead || (aphid.ClaimedBy is not null && aphid.ClaimedBy != claimant))
                continue;
            if (Vector3.DistanceSquared(aphid.Position, home.Center) > territoryRadiusSquared)
                continue;

            float distanceSquared = Vector3.DistanceSquared(from, aphid.Position);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                nearest = aphid;
            }
        }
        return nearest;
    }

    /// <summary>
    /// The Blood Feud: declares (or refreshes) mutual war between
    /// <paramref name="factionA"/> and <paramref name="factionB"/> for
    /// <see cref="BloodFeudDurationSeconds"/> — both sides' <see cref="VillageHeart.HostileFactions"/>
    /// get the other's FactionID, so Militia on either side treat the
    /// other as a lethal attack-on-sight enemy within the territory ring,
    /// not just the side that was wronged. A no-op for a faction with no
    /// Village Heart left (a homeless refugee has nobody left to declare
    /// war on its behalf). Called from <see cref="Bramblekin.TakeDamage"/>
    /// the instant one faction lands a damaging hit on another, and from
    /// the Warning Shove resolution when the caught trespasser turns out
    /// to be an armed Militia unit rather than an unarmed thief.
    /// </summary>
    public void DeclareBloodFeud(int factionA, int factionB)
    {
        if (factionA == factionB)
            return;

        if (VillageFor(factionA) is { } villageA)
            villageA.HostileFactions[factionB] = BloodFeudDurationSeconds;
        if (VillageFor(factionB) is { } villageB)
            villageB.HostileFactions[factionA] = BloodFeudDurationSeconds;
    }

    /// <summary>
    /// The 'Enemy of My Enemy' Protocol: whether the Wolf Spider is
    /// actively a threat (Hunting or Pouncing, not just ambling through or
    /// feeding) within <paramref name="home"/>'s own <see cref="VillageHeart.TerritoryRadius"/>.
    /// While true, that faction's Militia calls a
    /// truce on every rival faction — see the guards on Blood Feud Border
    /// Wars and Base Razing in <see cref="Bramblekin.Update"/> — so the
    /// whole tribe can throw itself at the common enemy instead of a
    /// neighbour. Apex Priority (a Militia unit always Defends against any
    /// spider merely present in the ring, whatever its state) is the
    /// separate, broader rule already enforced by that same priority
    /// chain's ordering; this is the narrower, additional gate
    /// specifically on the Border Wars/Base Razing side of it.
    /// </summary>
    public bool IsSpiderActivelyThreateningTerritory(VillageHeart? home) =>
        home is not null && Spider is { State: SpiderState.Hunting or SpiderState.Pouncing } spider &&
        GroundMover.HorizontalDistanceSquared(spider.Position, home.Center) <= home.TerritoryRadius * home.TerritoryRadius;

    /// <summary>
    /// Base Defense Aggro: the nearest living foreign Bramblekin within
    /// <see cref="BaseDefenseAggroRadius"/> of <paramref name="home"/>'s own
    /// centre — checked regardless of any existing Truce or Blood Feud
    /// status, since a unit standing this close is already effectively
    /// attacking the base (this is exactly how Base Razing raiders end up
    /// crowding around a Heart). Finding one is what actually triggers the
    /// Blood Feud in the first place (see the priority chain in
    /// <see cref="Bramblekin.Update"/>) — this method itself never mutates
    /// anything.
    /// </summary>
    public Bramblekin? NearestForeignBramblekinNearHeart(VillageHeart home)
    {
        Bramblekin? nearest = null;
        float bestDistanceSquared = BaseDefenseAggroRadius * BaseDefenseAggroRadius;

        // The Spatial Grid: only the Colony chunks around home's own centre.
        _colonyGrid.QueryNearby(home.Center, _colonyQueryBuffer);
        for (int i = _colonyQueryBuffer.Count - 1; i >= 0; i--)
        {
            Bramblekin intruder = _colonyQueryBuffer[i];
            if (intruder.IsDead || intruder.FactionID == home.FactionID)
                continue;

            float distanceSquared = Vector3.DistanceSquared(intruder.Position, home.Center);
            if (distanceSquared > bestDistanceSquared)
                continue;

            nearest = intruder;
            bestDistanceSquared = distanceSquared;
        }
        return nearest;
    }

    /// <summary>
    /// Thievery: the nearest OTHER faction's Village Heart whose territory
    /// ring physically contains <paramref name="position"/>, if any —
    /// checked the instant a Gatherer (any faction but
    /// <paramref name="ownFactionId"/>'s own) picks up a Food Shard, to
    /// catch it stealing from someone else's border. See
    /// <see cref="Bramblekin.TrespassingAgainst"/>.
    /// </summary>
    public VillageHeart? ForeignTerritoryContaining(Vector3 position, int ownFactionId)
    {
        VillageHeart? nearest = null;
        float bestDistanceSquared = float.MaxValue;

        foreach (VillageHeart village in Villages)
        {
            if (village.FactionID == ownFactionId)
                continue;

            // Cultural Borders: each village's own border is its own
            // wealth-scaled TerritoryRadius, not a shared flat constant — a
            // wealthy tribe's ring can physically overlap into a poorer
            // neighbour's.
            float distanceSquared = Vector3.DistanceSquared(position, village.Center);
            if (distanceSquared > village.TerritoryRadius * village.TerritoryRadius || distanceSquared > bestDistanceSquared)
                continue;

            nearest = village;
            bestDistanceSquared = distanceSquared;
        }
        return nearest;
    }

    /// <summary>
    /// Thievery: the nearest living Bramblekin currently flagged as
    /// trespassing specifically against <paramref name="home"/> (see
    /// <see cref="Bramblekin.TrespassingAgainst"/>, set the instant a
    /// Gatherer picks up food sitting inside a foreign territory) that's
    /// still within the territory ring — a surgical, single-target
    /// response. Unlike a Blood Feud, this never touches the wider
    /// relationship between the two factions unless the confrontation
    /// itself escalates one (see <see cref="Bramblekin.UpdateDefending"/>).
    /// </summary>
    public Bramblekin? NearestTrespasserInTerritory(VillageHeart home)
    {
        Bramblekin? nearest = null;
        float bestDistanceSquared = float.MaxValue;
        float territoryRadiusSquared = home.TerritoryRadius * home.TerritoryRadius;

        // The Spatial Grid: only the Colony chunks around home's own centre.
        _colonyGrid.QueryNearby(home.Center, _colonyQueryBuffer);
        for (int i = _colonyQueryBuffer.Count - 1; i >= 0; i--)
        {
            Bramblekin trespasser = _colonyQueryBuffer[i];
            if (trespasser.IsDead || trespasser.TrespassingAgainst != home)
                continue;

            float distanceSquared = Vector3.DistanceSquared(trespasser.Position, home.Center);
            if (distanceSquared > territoryRadiusSquared || distanceSquared >= bestDistanceSquared)
                continue;

            nearest = trespasser;
            bestDistanceSquared = distanceSquared;
        }
        return nearest;
    }

    /// <summary>
    /// Base Razing: whether any living Bramblekin of a faction
    /// <paramref name="ownFactionId"/>'s own Village Heart currently has a
    /// Blood Feud with is within <paramref name="radius"/> of
    /// <paramref name="position"/> — a live threat always outranks Raiding
    /// an empty-looking enemy Village Heart, so a Militia unit checks this
    /// before (and while) committing to one. Default Peace: a faction with
    /// no declared Blood Feud is never counted as a threat here at all.
    /// </summary>
    public bool HasLivingHostileBramblekinNear(Vector3 position, int ownFactionId, float radius)
    {
        VillageHeart? home = VillageFor(ownFactionId);
        if (home is null || home.HostileFactions.Count == 0)
            return false;

        float radiusSquared = radius * radius;
        // The Spatial Grid: only the Colony chunks around position itself.
        _colonyGrid.QueryNearby(position, _colonyQueryBuffer);
        for (int i = _colonyQueryBuffer.Count - 1; i >= 0; i--)
        {
            Bramblekin enemy = _colonyQueryBuffer[i];
            if (enemy.IsDead || !home.HostileFactions.ContainsKey(enemy.FactionID))
                continue;
            if (Vector3.DistanceSquared(enemy.Position, position) <= radiusSquared)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Blood Feud Border Wars + the 20-Meter Territory Rule: the nearest
    /// living Bramblekin (Gatherer or Militia) of a faction
    /// <paramref name="home"/> is currently at declared war with (see
    /// <see cref="VillageHeart.HostileFactions"/>), currently within
    /// <paramref name="home"/>'s territory ring — an attack-on-sight
    /// intruder for its Militia to run down. Default Peace: any other
    /// faction's Bramblekin is completely ignored here, full stop —
    /// there's no war to fight yet. Recomputed fresh every frame from
    /// <see cref="Update"/>'s state-machine priority chain, same as the
    /// Wolf Spider and Aphid checks, so a Defending Militia keeps
    /// re-picking as intruders come and go.
    /// </summary>
    public Bramblekin? NearestHostileBramblekinInTerritory(VillageHeart home)
    {
        if (home.HostileFactions.Count == 0)
            return null;

        Bramblekin? nearest = null;
        float bestDistanceSquared = float.MaxValue;
        float territoryRadiusSquared = home.TerritoryRadius * home.TerritoryRadius;

        // The Spatial Grid: only the Colony chunks around home's own centre.
        _colonyGrid.QueryNearby(home.Center, _colonyQueryBuffer);
        for (int i = _colonyQueryBuffer.Count - 1; i >= 0; i--)
        {
            Bramblekin enemy = _colonyQueryBuffer[i];
            if (enemy.IsDead || !home.HostileFactions.ContainsKey(enemy.FactionID))
                continue;

            float distanceSquared = Vector3.DistanceSquared(enemy.Position, home.Center);
            if (distanceSquared > territoryRadiusSquared || distanceSquared >= bestDistanceSquared)
                continue;

            nearest = enemy;
            bestDistanceSquared = distanceSquared;
        }
        return nearest;
    }

    /// <summary>
    /// Border Wars: a rival Bramblekin killed by a Militia poke (as opposed
    /// to the Wolf Spider or starvation) leaves behind whatever Individual
    /// Equipment it died holding — Spoils of War — rather than simply
    /// perishing with it as <see cref="Bramblekin.MarkDead"/> otherwise
    /// documents. Its Food Shard (if any) is already dropped generically by
    /// <see cref="Kill"/>/MarkDead; this only adds the Fang Pike/Chitin
    /// Mallet on top, since a plain predator or hunger death still doesn't
    /// leave those behind.
    /// </summary>
    public void KillByBramblekin(Bramblekin victim)
    {
        if (victim.IsDead)
            return;

        bool hadFang = victim.HasFangPike;
        bool hadChitin = victim.HasChitinMallet;
        Vector3 spot = victim.Position;

        Kill(victim);

        if (hadFang)
            _pendingFangSpawns.Add(new SpiderFang(spot));
        else if (hadChitin)
            _pendingChitinSpawns.Add(new Chitin(spot));
    }

    /// <summary>
    /// Blood Feud Base Razing: the nearest Village Heart <paramref name="ownFactionId"/>'s
    /// own faction is currently at declared war with, within
    /// <paramref name="radius"/> of <paramref name="from"/> — an
    /// opportunistic raid target for a Militia unit that's wandered near a
    /// rival base, not the home-centered 20-Meter Territory Rule used for
    /// defense. Default Peace: any faction not in <see cref="VillageHeart.HostileFactions"/>
    /// is never a valid Raiding target, full stop.
    /// </summary>
    public VillageHeart? NearestHostileVillageHeartInRange(Vector3 from, int ownFactionId, float radius)
    {
        VillageHeart? home = VillageFor(ownFactionId);
        if (home is null || home.HostileFactions.Count == 0)
            return null;

        VillageHeart? nearest = null;
        float bestDistanceSquared = radius * radius;

        foreach (VillageHeart village in Villages)
        {
            if (!home.HostileFactions.ContainsKey(village.FactionID))
                continue;

            float distanceSquared = Vector3.DistanceSquared(from, village.Center);
            if (distanceSquared > bestDistanceSquared)
                continue;

            nearest = village;
            bestDistanceSquared = distanceSquared;
        }
        return nearest;
    }

    /// <summary>
    /// Base Razing: applies Militia poke damage to an enemy Village Heart
    /// and, if that brings its Health to 0, conquers it outright — see
    /// <see cref="DestroyVillageHeart"/>, which needs <paramref name="attackerFactionId"/>
    /// (the raider's own FactionID) on hand for the Refugee Protocol's
    /// Assimilation branch. Visual Damage Feedback: a floating "-N" pop-up
    /// on top of the Heart's own red damage flash (see <see cref="VillageHeart.TakeDamage"/>)
    /// confirms the hit actually landed, for debugging Base Razing.
    /// </summary>
    public void DamageVillageHeart(VillageHeart village, int amount, int attackerFactionId)
    {
        village.TakeDamage(amount);
        QueueFloatingText(village.Center, $"-{amount}", new Color(220, 30, 30, 255));
        if (village.Health <= 0)
            DestroyVillageHeart(village, attackerFactionId);
    }

    /// <summary>Loose Food Shards a razed Village Heart shatters into for the victors to claim.</summary>
    private const int VillageHeartLootShardCount = 10;

    /// <summary>
    /// Shared cleanup for a Village Heart that's gone for good, one way or
    /// another: removes it from <see cref="Villages"/> outright (same
    /// direct-mutation pattern as <see cref="CompleteBlueprint"/>'s
    /// Blueprints.Remove) and takes every Granary, Spore Farm and
    /// Blueprint sharing its FactionID down with it. Its own Colony
    /// survives as suddenly homeless refugees — <see cref="VillageFor"/>
    /// simply returns null for them from here on. Callers add whatever's
    /// specific to how it ended — Base Razing's loot/splat
    /// (<see cref="DestroyVillageHeart"/>), or nothing at all for a
    /// starved-out Ghost Town (see the per-village loop in <see cref="Update"/>).
    /// </summary>
    private void RemoveVillageAndItsBuildings(VillageHeart village)
    {
        Villages.Remove(village);
        Buildings.RemoveAll(b => b.FactionID == village.FactionID);
        Blueprints.RemoveAll(b => b.FactionID == village.FactionID);
    }

    /// <summary>
    /// Base Razing: a Village Heart reduced to 0 Health is conquered —
    /// this only ever runs from within the Colony loop, well before
    /// Villages is next enumerated this frame, so there's no
    /// concurrent-modification risk — shattering into
    /// <see cref="VillageHeartLootShardCount"/> loose Food Shards scattered
    /// around its footprint on top of the shared cleanup above, then
    /// running the Refugee Protocol (see <see cref="RunRefugeeProtocol"/>)
    /// for whichever of its own Gatherers are still alive.
    /// </summary>
    private void DestroyVillageHeart(VillageHeart village, int attackerFactionId)
    {
        if (!Villages.Contains(village))
            return; // Already razed this frame by another poke landing the same instant.

        int razedFactionId = village.FactionID;
        RemoveVillageAndItsBuildings(village);

        float half = Terrain.Size / 2f - Bramblekin.EdgeMargin;
        for (int i = 0; i < VillageHeartLootShardCount; i++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float distance = 0.5f + (float)Rng.NextDouble() * 1.5f;
            var position = village.Center + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * distance;
            position.X = Math.Clamp(position.X, -half, half);
            position.Z = Math.Clamp(position.Z, -half, half);
            _pendingShardSpawns.Add((position, FoodShardKind.Cracked));
        }

        _splats.Add((village.Center, SplatDuration));

        RunRefugeeProtocol(village, razedFactionId, attackerFactionId);
    }

    /// <summary>
    /// The Refugee Protocol: Base Razing no longer means instant death for
    /// the losing side's Gatherers. First tries <see cref="RandomRefugeeTarget"/>
    /// for empty ground far from every surviving Village Heart — if one
    /// exists, every surviving Gatherer of the razed faction becomes a
    /// Pioneer (exactly like a Schism splinter, reusing <see cref="Bramblekin.BecomePioneer"/>
    /// so they ignore hostiles and everything else while fleeing) bound for
    /// it, to plant a brand new Village Heart from scratch. If the map has
    /// no safe ground left at all, they surrender instead: Assimilation
    /// switches every survivor straight into <paramref name="attackerFactionId"/>'s
    /// faction and colour on the spot. Militia aren't covered here — this
    /// only ever runs on the losing side's remaining Gatherers and Builder,
    /// per the design.
    /// </summary>
    private void RunRefugeeProtocol(VillageHeart razedVillage, int razedFactionId, int attackerFactionId)
    {
        List<Bramblekin> survivors = Colony.Where(b => !b.IsDead && b.FactionID == razedFactionId &&
            b.Role is BramblekinRole.Gatherer or BramblekinRole.Builder).ToList();
        if (survivors.Count == 0)
            return;

        if (RandomRefugeeTarget() is { } safeSpot)
        {
            int newFactionId = _nextSchismFactionId++;
            Color newFactionColor = SchismFactionColors[(newFactionId - 1) % SchismFactionColors.Length];
            var migration = new Migration(newFactionId, newFactionColor, safeSpot, razedVillage, survivors.Count, foodAmount: 0);
            foreach (Bramblekin refugee in survivors)
                refugee.BecomePioneer(migration);
        }
        else if (VillageFor(attackerFactionId) is { } conqueror)
        {
            foreach (Bramblekin refugee in survivors)
                refugee.Assimilate(attackerFactionId, conqueror.FactionColor);
        }
    }

    /// <summary>
    /// Militia Hunting: an Aphid caught by a Militia unit. Marked dead and
    /// its removal queued, exactly like a killed Bramblekin, and it drops
    /// <see cref="AphidFoodShardYield"/> Food Shards where it stood for the
    /// Gatherers to collect.
    /// </summary>
    public void KillAphid(Aphid aphid)
    {
        if (aphid.IsDead)
            return;

        aphid.MarkDead();
        _pendingAphidRemovals.Add(aphid);

        float half = Terrain.Size / 2f - Bramblekin.EdgeMargin;
        for (int i = 0; i < AphidFoodShardYield; i++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            var position = aphid.Position + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * 0.3f;
            position.X = Math.Clamp(position.X, -half, half);
            position.Z = Math.Clamp(position.Z, -half, half);
            _pendingShardSpawns.Add((position, FoodShardKind.Cracked));
        }
    }

    /// <summary>Passive Foraging: spawns a wild Berry every <see cref="BerrySpawnInterval"/> s, up to <see cref="MaxBerries"/>.</summary>
    private void UpdateBerrySpawn(float deltaTime)
    {
        _berrySpawnTimer -= deltaTime;
        if (_berrySpawnTimer > 0f)
            return;
        _berrySpawnTimer = BerrySpawnInterval;

        int berries = FoodShards.Count(s => s.IsActive && s.Kind == FoodShardKind.Berry)
                    + _pendingShardSpawns.Count(s => s.Kind == FoodShardKind.Berry);
        if (berries >= MaxBerries)
            return;

        Vector3 spot = RandomWildernessSpot(FoodShard.Radius + 0.3f, edgeMargin: 1f);
        _pendingShardSpawns.Add((spot, FoodShardKind.Berry));
    }

    /// <summary>Tops the Aphid population back up to <see cref="MaxAphids"/> after a loss.</summary>
    private void UpdateAphidRespawn(float deltaTime)
    {
        _aphidRespawnTimer -= deltaTime;
        if (_aphidRespawnTimer > 0f)
            return;
        _aphidRespawnTimer = AphidRespawnDelay;

        int living = Aphids.Count(a => !a.IsDead) + _pendingAphidSpawns.Count;
        if (living >= MaxAphids)
            return;

        Vector3 spot = RandomFreePoint(Aphid.BodyRadius + 0.1f, Aphid.EdgeMargin);
        _pendingAphidSpawns.Add(new Aphid(spot, Rng));
    }

    /// <summary>
    /// Timeout Failsafe against the "dibs" deadlock: a claimed Food Shard whose
    /// claimant never actually closes the distance (stuck, jittering, or
    /// otherwise stalled) would otherwise lock that shard out of the pool
    /// forever. Ticking <see cref="FoodShard.ClaimTimer"/> here and force-
    /// releasing it past <see cref="FoodClaimTimeoutSeconds"/> guarantees
    /// someone else can always eventually grab it.
    /// </summary>
    private void UpdateFoodClaimTimeouts(float deltaTime)
    {
        foreach (FoodShard shard in FoodShards)
        {
            if (!shard.IsActive || shard.ClaimedBy is null)
                continue;

            shard.ClaimTimer += deltaTime;
            if (shard.ClaimTimer >= FoodClaimTimeoutSeconds)
            {
                shard.ClaimedBy = null;
                shard.ClaimTimer = 0f;
            }
        }

        // Same Dibs failsafe, applied to Amber.
        foreach (AmberNode amber in AmberNodes)
        {
            if (!amber.IsActive || amber.ClaimedBy is null)
                continue;

            amber.ClaimTimer += deltaTime;
            if (amber.ClaimTimer >= FoodClaimTimeoutSeconds)
            {
                amber.ClaimedBy = null;
                amber.ClaimTimer = 0f;
            }
        }
    }

    /// <summary>
    /// Breaking the Death Loop: ticks <see cref="FoodShard.DespawnTimer"/>/
    /// <see cref="AmberNode.DespawnTimer"/> down for every uncarried piece
    /// of loot on the map and removes it once its timer runs out. Without
    /// this, a pile of Food Shards scattered by a Base Razing, a hunted
    /// Aphid, or a cracked Acorn sits forever, drawing wave after wave of
    /// Gatherers into the same spot — often a rock cluster or a rival's
    /// border — to die exactly the way the last one did. Carried loot never
    /// counts down (see <see cref="FoodShard.IsCarried"/>/<see cref="AmberNode.IsCarried"/>):
    /// only what's actually sitting abandoned on the ground is at risk.
    /// Called directly at the top level of <see cref="Update"/> (not from
    /// inside the Colony loop), so removing straight from FoodShards/
    /// AmberNodes here — rather than through the deferred pending-removal
    /// queues — is safe.
    /// </summary>
    private void UpdateLootDespawn(float deltaTime)
    {
        for (int i = FoodShards.Count - 1; i >= 0; i--)
        {
            FoodShard shard = FoodShards[i];
            if (!shard.IsActive || shard.IsCarried) // Object Pooling: an inactive slot has nothing to despawn.
                continue;

            shard.DespawnTimer -= deltaTime;
            if (shard.DespawnTimer <= 0f)
                shard.Deactivate();
        }

        for (int i = AmberNodes.Count - 1; i >= 0; i--)
        {
            AmberNode amber = AmberNodes[i];
            if (!amber.IsActive || amber.IsCarried) // Object Pooling: an inactive slot has nothing to despawn.
                continue;

            amber.DespawnTimer -= deltaTime;
            if (amber.DespawnTimer <= 0f)
                amber.Deactivate();
        }
    }

    public void Update(float deltaTime)
    {
        FrameCounter++;
        UpdateFoodClaimTimeouts(deltaTime);
        RebuildObstacles();
        RebuildSpatialGrids();
        PushFoodOutOfObstacles();

        // Ambient prey moves before the colony reacts to it this frame.
        // Reverse for-loop: a Militia unit's own Update() (below) can
        // call KillAphid, which marks an Aphid dead but, like everything
        // else this session, defers the actual list removal.
        for (int i = Aphids.Count - 1; i >= 0; i--)
            Aphids[i].Update(deltaTime, this);

        // Reverse for-loop: a Bramblekin's own Update() can indirectly queue
        // a sprout (via DeliverFood), a kill (via the spider's pounce), or
        // now a dead Aphid (via Militia Hunting) — none of them touch their
        // list directly any more, but walking it backwards means this loop
        // stays correct even if that ever changes.
        for (int i = Colony.Count - 1; i >= 0; i--)
            Colony[i].Update(deltaTime, this);

        // Continuous Cracking: checked once here, after the Colony loop has
        // added every Cracking Gatherer's own contribution for the frame,
        // so several claimants finishing an Acorn off in the same frame can
        // never cause a double-shatter.
        UpdateAcornCracking();

        Spider?.Update(deltaTime, this);

        // The Simulation Loop: every faction's Village Heart runs its own
        // Auto-Conscription, War Weariness, Upkeep and Auto-Construction
        // entirely off its own Population/FoodStored/MaxFoodCapacity/Morale
        // — one faction starving or booming never touches another's.
        for (int villageIndex = Villages.Count - 1; villageIndex >= 0; villageIndex--)
        {
            VillageHeart village = Villages[villageIndex];
            UpdateJobManager(village);
            UpdateMorale(village, deltaTime);
            village.DamageFlashTimer = MathF.Max(0f, village.DamageFlashTimer - deltaTime);

            // The Blood Feud: every declared war's timer counts down toward
            // 0 regardless of anything else this Village Heart is doing;
            // peace is restored automatically the instant one runs out.
            // World.Update() is itself called once per Debug Time Scale
            // substep with a real (clamped) frame time rather than a
            // scaled-up deltaTime (see Program's simulation loop), so
            // ticking down by this method's own deltaTime already runs
            // these timers out faster at a higher Time Scale exactly like
            // every other timer here (UpkeepTimer, ClaimTimer, ...) -- a
            // literal GetFrameTime() * TimeScale here would double up with
            // that substep multiplication and run every feud out far too
            // fast at anything above 1x.
            if (village.HostileFactions.Count > 0)
            {
                foreach (int factionId in village.HostileFactions.Keys.ToList())
                {
                    float remaining = village.HostileFactions[factionId] - deltaTime;
                    if (remaining <= 0f)
                        village.HostileFactions.Remove(factionId);
                    else
                        village.HostileFactions[factionId] = remaining;
                }
            }

            // Ghost Town Cleanup: nobody left, and not even enough Food
            // Stored to Auto-Sprout a single replacement -- this faction is
            // done for good. Actually removed outright now (rather than
            // just left standing inert forever), taking its Granaries and
            // Spore Farms down with it (see RemoveVillageAndItsBuildings)
            // -- otherwise a starvation wipeout could never bring
            // World.IsWorldExtinct (Villages.Count == 0) true, and Genesis
            // would never have anything to trigger on. Iterated backwards
            // by index specifically so removing an entry mid-loop is safe.
            if (village.IsExtinct)
            {
                RemoveVillageAndItsBuildings(village);
                continue;
            }

            // Upkeep is the survival tax: it gets first claim on Food Stored,
            // ahead of anything discretionary, and can cost a Bramblekin its
            // life if the village can't pay it. Bypassed entirely once
            // Population hits 0 -- there's no one left to tax, even for a
            // village that isn't (yet) Extinct because it's still sitting on
            // enough Food Stored to Auto-Sprout its way back.
            if (village.Population > 0)
                UpdateUpkeep(village, deltaTime);

            // The New Economy AI: three independent phases, checked in a
            // fixed priority order every frame so a phase that spends Food
            // Stored this frame is always seen by the next one, rather than
            // letting a later phase double-spend against a stale balance.
            //
            // 1. Housing Phase (UpdateAutoTent): Population is capped by
            //    MaxPopulation now, not MaxFoodCapacity/Granaries — once a
            //    tribe hits its housing ceiling, it saves toward a Tent
            //    instead of sprouting.
            // 2. Growth Phase (UpdateAutoSprout): sprouts new Bramblekin with
            //    whatever Food Stored is on hand, until MaxPopulation is
            //    reached. Growing the tribe always outranks banking surplus
            //    Food away in a Granary — a colony that isn't there yet has
            //    nothing to gain from more storage capacity.
            // 3. Storage Phase (UpdateAutoGranary): checked LAST, and only
            //    once Food Stored is actually at (or effectively at) the
            //    current MaxFoodCapacity — Housing and Growth always get
            //    first claim on Food Stored; a Granary only ever gets built
            //    once there's genuinely nowhere left to put more food.
            // The Great Monument: once a tribe is wealthy and populous
            // enough to commit to one, it stops queuing any other building
            // (Growth/population is unaffected — that's Auto-Sprout, not a
            // building) until the Monument itself is finished. Checked
            // first so it can veto everything below it this same frame.
            bool pursuingMonument = UpdateAutoMonument(village);

            UpdateAutoSprout(village);

            if (!pursuingMonument)
            {
                UpdateAutoSporeFarm(village);
                UpdateAutoTent(village);
                UpdateAutoGranary(village);

                // Auxiliary Auto-Construction: population-gated one-time
                // builds that ride on top of the phases above rather than
                // being part of that priority order.
                UpdateAutoTradingPost(village);
                UpdateAutoBrewery(village);
            }

            // The Schism: a Village Heart maxed out on Housing and
            // overflowing with Food Stored spins off a new faction of its
            // own rather than just sitting capped out forever.
            UpdateSchism(village);
        }
        UpdateSporeFarmIncome(deltaTime);
        UpdateNectarBrewery(deltaTime);

        UpdateAcornSpawn(deltaTime);
        UpdateAmberSpawn(deltaTime);
        UpdateSpiderRespawn(deltaTime);
        UpdateBerrySpawn(deltaTime);
        UpdateAphidRespawn(deltaTime);
        UpdateLootDespawn(deltaTime);

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
    /// nothing is ever adding to or removing from Colony/FoodShards while
    /// something else might still be iterating them.
    /// </summary>
    public void CommitPendingChanges()
    {
        if (_pendingBramblekinRemovals.Count > 0)
        {
            for (int i = _pendingBramblekinRemovals.Count - 1; i >= 0; i--)
                Colony.Remove(_pendingBramblekinRemovals[i]);
            _pendingBramblekinRemovals.Clear();
        }

        if (_pendingBramblekinSpawns.Count > 0)
        {
            Colony.AddRange(_pendingBramblekinSpawns);
            _pendingBramblekinSpawns.Clear();
        }

        // Object Pooling: a removal returns its pool slot (Deactivate)
        // rather than removing it from the (now fixed-size) list; a spawn
        // activates the first free slot rather than constructing a new
        // FoodShard — see ActivateFoodShard.
        if (_pendingShardRemovals.Count > 0)
        {
            for (int i = _pendingShardRemovals.Count - 1; i >= 0; i--)
                _pendingShardRemovals[i].Deactivate();
            _pendingShardRemovals.Clear();
        }

        if (_pendingShardSpawns.Count > 0)
        {
            foreach (var (position, kind) in _pendingShardSpawns)
                ActivateFoodShard(position, kind);
            _pendingShardSpawns.Clear();
        }

        if (_pendingAmberRemovals.Count > 0)
        {
            for (int i = _pendingAmberRemovals.Count - 1; i >= 0; i--)
                _pendingAmberRemovals[i].Deactivate();
            _pendingAmberRemovals.Clear();
        }

        if (_pendingAphidRemovals.Count > 0)
        {
            for (int i = _pendingAphidRemovals.Count - 1; i >= 0; i--)
                Aphids.Remove(_pendingAphidRemovals[i]);
            _pendingAphidRemovals.Clear();
        }

        if (_pendingAphidSpawns.Count > 0)
        {
            Aphids.AddRange(_pendingAphidSpawns);
            _pendingAphidSpawns.Clear();
        }

        if (_pendingFangRemovals.Count > 0)
        {
            for (int i = _pendingFangRemovals.Count - 1; i >= 0; i--)
                Fangs.Remove(_pendingFangRemovals[i]);
            _pendingFangRemovals.Clear();
        }

        if (_pendingFangSpawns.Count > 0)
        {
            Fangs.AddRange(_pendingFangSpawns);
            _pendingFangSpawns.Clear();
        }

        if (_pendingChitinRemovals.Count > 0)
        {
            for (int i = _pendingChitinRemovals.Count - 1; i >= 0; i--)
                Chitins.Remove(_pendingChitinRemovals[i]);
            _pendingChitinRemovals.Clear();
        }

        if (_pendingChitinSpawns.Count > 0)
        {
            Chitins.AddRange(_pendingChitinSpawns);
            _pendingChitinSpawns.Clear();
        }
    }

    /// <summary>
    /// Raylib Culling: margin (px) added around the screen rectangle when
    /// deciding whether a projected point is "on screen" for
    /// <see cref="IsOnScreen"/> — generous enough that an entity's body
    /// (which extends a bit past its center point) doesn't visibly pop in
    /// right at the screen edge.
    /// </summary>
    private const float CullScreenMargin = 40f;

    /// <summary>Basic bounds check: true unless <paramref name="worldPosition"/> projects to a screen point entirely outside the camera's current viewport (plus <see cref="CullScreenMargin"/>) — used to skip Raylib draw calls for entities the camera can't currently see at all.</summary>
    private static bool IsOnScreen(Vector3 worldPosition, Camera3D camera)
    {
        Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);
        return screen.X >= -CullScreenMargin && screen.X <= Raylib.GetScreenWidth() + CullScreenMargin &&
               screen.Y >= -CullScreenMargin && screen.Y <= Raylib.GetScreenHeight() + CullScreenMargin;
    }

    public void Draw(Camera3D camera)
    {
        Terrain.Draw();
        for (int i = _splats.Count - 1; i >= 0; i--)
        {
            var (position, timeLeft) = _splats[i];
            // A dark stain that fades out.
            byte alpha = (byte)(200 * Math.Clamp(timeLeft / 2f, 0f, 1f));
            Raylib.DrawCylinder(position + new Vector3(0, 0.012f, 0), 0.9f, 0.9f, 0.005f, 20, new Color(30, 25, 20, (int)alpha));
        }
        for (int i = Villages.Count - 1; i >= 0; i--)
            Villages[i].Draw();

        // Object Pooling: Acorns/AmberNodes/FoodShards are fixed-size pools
        // pre-allocated up to their map caps — most slots sit inactive at
        // any given time, so every rendering (and targeting) loop over them
        // must skip anything with IsActive false.
        for (int i = Acorns.Count - 1; i >= 0; i--)
        {
            Acorn acorn = Acorns[i];
            if (acorn.IsActive && IsOnScreen(acorn.Position, camera))
                acorn.Draw();
        }

        for (int i = AmberNodes.Count - 1; i >= 0; i--)
        {
            AmberNode amber = AmberNodes[i];
            if (amber.IsActive && !amber.IsCarried && IsOnScreen(amber.Position, camera))
                amber.Draw(amber.Position);
        }

        for (int i = Buildings.Count - 1; i >= 0; i--)
            Buildings[i].Draw();
        for (int i = Blueprints.Count - 1; i >= 0; i--)
            Blueprints[i].Draw();

        for (int i = FoodShards.Count - 1; i >= 0; i--)
        {
            FoodShard shard = FoodShards[i];
            if (shard.IsActive && !shard.IsCarried && IsOnScreen(shard.Position, camera))
                shard.Draw(shard.Position);
        }

        for (int i = Fangs.Count - 1; i >= 0; i--)
            Fangs[i].Draw();

        for (int i = Chitins.Count - 1; i >= 0; i--)
            Chitins[i].Draw();

        // Same reverse-for/skip-dead pattern as the Colony loop below.
        for (int i = Aphids.Count - 1; i >= 0; i--)
        {
            if (!Aphids[i].IsDead)
                Aphids[i].Draw();
        }

        // Reverse for-loop, and skip anything marked dead this frame: its
        // removal from Colony is deferred, so without this check a
        // Bramblekin caught a moment ago would still be drawn standing there.
        for (int i = Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin b = Colony[i];
            if (!b.IsDead && IsOnScreen(b.Position, camera))
                b.Draw();
        }

        Spider?.Draw();
    }

    // --- Queries used by the Bramblekin AI ------------------------------------

    /// <summary>True if a round body of <paramref name="clearance"/> radius at <paramref name="point"/> would overlap an obstacle.</summary>
    public bool IsBlocked(Vector3 point, float clearance) => IsBlocked(point, clearance, _obstacles);

    private static bool IsBlocked(Vector3 point, float clearance, IReadOnlyList<Obstacle> obstacles)
    {
        var p = new Vector2(point.X, point.Z);
        foreach (var obstacle in obstacles)
        {
            float reach = obstacle.Radius + clearance;
            if (Vector2.DistanceSquared(p, obstacle.Center) < reach * reach)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Dibs: a shard can be gathered if nobody is carrying it, and it isn't
    /// claimed by a different Bramblekin actively pursuing it (see
    /// <see cref="FoodShard.ClaimedBy"/>). Rocks never cover shards — they
    /// shove them aside — but the blocked check stays as a safety net for a
    /// shard wedged somewhere unreachable.
    /// </summary>
    public bool IsAvailable(FoodShard shard, Bramblekin claimant) =>
        shard.IsActive // Object Pooling: an inactive slot is not a real shard.
        && !shard.IsCarried
        && (shard.ClaimedBy is null || shard.ClaimedBy == claimant)
        && !IsBlocked(shard.Position, 0f);

    /// <summary>The 20-Meter Territory Rule: whether <paramref name="claimant"/> has anything to gather, preferring <paramref name="home"/>'s territory but falling back to the wider map.</summary>
    public bool HasAvailableFoodFor(Bramblekin claimant, VillageHeart? home) => NearestAvailableShard(claimant.Position, claimant, home) is not null;

    /// <summary>
    /// Strict Border Control: true if <paramref name="point"/> falls inside
    /// ANY Village Heart's own (Cultural Borders — wealth-scaled, see
    /// <see cref="VillageHeart.TerritoryRadius"/>) territory ring other
    /// than <paramref name="ownFactionId"/>'s own.
    /// Shares <see cref="ForeignTerritoryContaining"/> with the Thievery
    /// check for one single "whose border is this?" answer — deliberately
    /// blind to <see cref="VillageHeart.HostileFactions"/>/Default Peace, a
    /// Schism splinter's shared ancestry with its parent, or anything else
    /// that makes two factions not currently shoot at each other: a truce
    /// means "don't attack," never "share food," so the parent tribe's own
    /// granary is exactly as foreign to a freshly-split Pioneer faction as
    /// any other rival's. A hard exclusion, not a preference — used by
    /// <see cref="NearestAvailableShard"/>, <see cref="NearestClaimableAcorn"/>
    /// and <see cref="NearestAvailableAmber"/> to rule a resource out
    /// entirely rather than merely discourage it, so a Gatherer never
    /// crosses into a foreign border for any resource, at any desperation
    /// level.
    /// </summary>
    private bool IsForeignTerritory(Vector3 point, int ownFactionId) =>
        ForeignTerritoryContaining(point, ownFactionId) is not null;

    /// <summary>
    /// Cultural Borders + Dibs + Strict Border Control (see
    /// <see cref="IsForeignTerritory"/>) + Maximum Search Radius (see
    /// <see cref="MaxGatherSearchRadius"/>), sorted by distance: among
    /// unclaimed (or self-claimed) shards within <paramref name="home"/>'s
    /// own (wealth-scaled — see <see cref="VillageHeart.TerritoryRadius"/>)
    /// territory ring, the nearest one to <paramref name="from"/>.
    /// Only if none qualify locally does this fall back to the nearest
    /// anywhere within <see cref="MaxGatherSearchRadius"/> — a Gatherer
    /// always prefers its own doorstep, then safe wild food elsewhere on the
    /// map, but a shard sitting inside another faction's territory ring is
    /// never a candidate at all, desperate or not, and neither is one
    /// further than <see cref="MaxGatherSearchRadius"/>; null (nothing to
    /// gather) is a perfectly normal result once the local neighbourhood is
    /// picked clean — see <see cref="Bramblekin.StartWanderingNearHome"/>.
    /// </summary>
    public FoodShard? NearestAvailableShard(Vector3 from, Bramblekin claimant, VillageHeart? home)
    {
        FoodShard? bestLocal = null;
        float bestLocalDistanceSquared = float.MaxValue;
        FoodShard? bestAny = null;
        float bestAnyDistanceSquared = float.MaxValue;
        // Cultural Borders: home's own wealth-scaled TerritoryRadius (see
        // VillageHeart.TerritoryRadius) — 0 when homeless, so the local
        // preference below (which also requires home is not null) simply
        // never matches.
        float territoryRadiusSquared = home is not null ? home.TerritoryRadius * home.TerritoryRadius : 0f;
        float maxGatherSearchRadiusSquared = MaxGatherSearchRadius * MaxGatherSearchRadius;
        // Desperation Mode: a starving village can't afford to wait for local food that
        // may not exist, so we skip the local-preference logic entirely and just grab
        // whatever's nearest anywhere within MaxGatherSearchRadius (still never foreign).
        bool desperate = home is not null && home.FoodStored < DesperationFoodThreshold;

        // The Spatial Grid: only the shards in from's own 10m chunk and its
        // 8 neighbors are ever considered — see SpatialGrid.
        _foodGrid.QueryNearby(from, _foodQueryBuffer);
        for (int i = _foodQueryBuffer.Count - 1; i >= 0; i--)
        {
            FoodShard shard = _foodQueryBuffer[i];
            if (!IsAvailable(shard, claimant))
                continue;
            if (IsForeignTerritory(shard.Position, claimant.FactionID))
                continue; // Strict Border Control: off-limits, full stop, no matter how desperate.

            float distanceSquared = Vector3.DistanceSquared(from, shard.Position);
            if (distanceSquared > maxGatherSearchRadiusSquared)
                continue; // Maximum Search Radius: never even evaluated, last resort or not.

            if (distanceSquared < bestAnyDistanceSquared)
            {
                bestAny = shard;
                bestAnyDistanceSquared = distanceSquared;
            }

            if (!desperate && home is not null && distanceSquared < bestLocalDistanceSquared && Vector3.DistanceSquared(shard.Position, home.Center) <= territoryRadiusSquared)
            {
                bestLocal = shard;
                bestLocalDistanceSquared = distanceSquared;
            }
        }
        return desperate ? bestAny : (bestLocal ?? bestAny);
    }

    /// <summary>
    /// A delivered shard leaves the map and adds to <paramref name="village"/>'s
    /// own stores, up to its <see cref="VillageHeart.MaxFoodCapacity"/> —
    /// food gathered past a full store is still delivered (the Bramblekin
    /// isn't left holding it forever) but doesn't raise the count. AI
    /// Faction Loyalty: a Gatherer always delivers to its own faction's
    /// Village Heart (see <see cref="Bramblekin.FactionID"/>), which then
    /// decides what to do with what's banked (Auto-Sprout, Auto-Construction
    /// — see <see cref="UpdateAutoSprout"/>/<see cref="UpdateAutoGranary"/>),
    /// all autonomous; this is a pure God Game, so the player never spends
    /// food directly.
    ///
    /// This is called from inside a Bramblekin's own Update(), which is
    /// itself inside World's reverse for-loop over Colony — so the shard's
    /// removal is queued, never applied to FoodShards directly here.
    /// </summary>
    public void DeliverFood(FoodShard shard, VillageHeart village)
    {
        if (!_pendingShardRemovals.Contains(shard))
            _pendingShardRemovals.Add(shard);
        village.FoodStored = Math.Min(village.FoodStored + 1, village.MaxFoodCapacity);
    }

    /// <summary>Tycoon Economy Dibs: same rules as <see cref="IsAvailable(FoodShard, Bramblekin)"/> — nobody carrying it, unclaimed (or claimed by <paramref name="claimant"/>).</summary>
    public bool IsAvailable(AmberNode amber, Bramblekin claimant) =>
        amber.IsActive // Object Pooling: an inactive slot is not a real Amber node.
        && !amber.IsCarried
        && (amber.ClaimedBy is null || amber.ClaimedBy == claimant)
        && !IsBlocked(amber.Position, 0f);

    /// <summary>
    /// Tycoon Economy: Strict Border Control (see <see cref="IsForeignTerritory"/>)
    /// + Maximum Search Radius (see <see cref="MaxGatherSearchRadius"/>),
    /// sorted by distance — the nearest available Amber to <paramref name="from"/>,
    /// or null if nothing qualifies within reach. Amber is map-wide scarce
    /// rather than territory-seeded, so unlike <see cref="NearestAvailableShard"/>
    /// there is no separate "prefer local territory" pass — just the one
    /// nearest-wins search, with any Amber inside a foreign Village Heart's
    /// territory ring excluded outright rather than merely discouraged.
    /// </summary>
    public AmberNode? NearestAvailableAmber(Vector3 from, Bramblekin claimant)
    {
        AmberNode? best = null;
        float bestDistanceSquared = float.MaxValue;
        float maxGatherSearchRadiusSquared = MaxGatherSearchRadius * MaxGatherSearchRadius;
        // The Spatial Grid: only from's own 10m chunk and its 8 neighbors.
        _amberGrid.QueryNearby(from, _amberQueryBuffer);
        for (int i = _amberQueryBuffer.Count - 1; i >= 0; i--)
        {
            AmberNode amber = _amberQueryBuffer[i];
            if (!IsAvailable(amber, claimant))
                continue;
            if (IsForeignTerritory(amber.Position, claimant.FactionID))
                continue; // Strict Border Control: off-limits, full stop.

            float distanceSquared = Vector3.DistanceSquared(from, amber.Position);
            if (distanceSquared > maxGatherSearchRadiusSquared)
                continue; // Maximum Search Radius: never even evaluated.

            if (distanceSquared < bestDistanceSquared)
            {
                best = amber;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>
    /// A delivered Amber leaves the map and adds to <paramref name="village"/>'s
    /// banked wealth (<see cref="VillageHeart.AmberStored"/>), uncapped —
    /// same deferred-removal pattern as <see cref="DeliverFood"/>, since
    /// this is called from inside a Bramblekin's own Update(), itself
    /// inside World's reverse for-loop over Colony.
    /// </summary>
    public void DeliverAmber(AmberNode amber, VillageHeart village)
    {
        if (!_pendingAmberRemovals.Contains(amber))
            _pendingAmberRemovals.Add(amber);
        village.AmberStored++;
    }

    /// <summary>A Fang can always be picked up — it's never carried or claimed, just touched and gone.</summary>
    public bool IsAvailable(SpiderFang fang) => !IsBlocked(fang.Position, 0f);

    public bool HasAvailableFang => Fangs.Any(IsAvailable);

    /// <summary>The nearest available Spider Fang to <paramref name="from"/>, if any.</summary>
    public SpiderFang? NearestAvailableFang(Vector3 from)
    {
        SpiderFang? best = null;
        float bestDistance = float.MaxValue;
        for (int i = Fangs.Count - 1; i >= 0; i--)
        {
            SpiderFang fang = Fangs[i];
            if (!IsAvailable(fang))
                continue;

            float distance = Vector3.DistanceSquared(from, fang.Position);
            if (distance < bestDistance)
            {
                best = fang;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>
    /// Individual Equipment: touching a Spider Fang consumes it outright —
    /// no carrying it home, no village-wide unlock. Called from inside a
    /// Bramblekin's own Update(), so the Fang's removal is queued rather
    /// than applied to <see cref="Fangs"/> directly here; the caller sets
    /// its own <see cref="Bramblekin.HasFangPike"/>.
    /// </summary>
    public void ConsumeFang(SpiderFang fang)
    {
        if (!_pendingFangRemovals.Contains(fang))
            _pendingFangRemovals.Add(fang);
    }

    /// <summary>A Chitin piece can always be picked up — same rule as a Fang.</summary>
    public bool IsAvailable(Chitin chitin) => !IsBlocked(chitin.Position, 0f);

    public bool HasAvailableChitin => Chitins.Any(IsAvailable);

    /// <summary>The nearest available Chitin piece to <paramref name="from"/>, if any.</summary>
    public Chitin? NearestAvailableChitin(Vector3 from)
    {
        Chitin? best = null;
        float bestDistance = float.MaxValue;
        for (int i = Chitins.Count - 1; i >= 0; i--)
        {
            Chitin chitin = Chitins[i];
            if (!IsAvailable(chitin))
                continue;

            float distance = Vector3.DistanceSquared(from, chitin.Position);
            if (distance < bestDistance)
            {
                best = chitin;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Individual Equipment: touching a Chitin piece consumes it outright — see <see cref="ConsumeFang"/>. The caller sets its own <see cref="Bramblekin.HasChitinMallet"/>.</summary>
    public void ConsumeChitin(Chitin chitin)
    {
        if (!_pendingChitinRemovals.Contains(chitin))
            _pendingChitinRemovals.Add(chitin);
    }

    /// <summary>
    /// The Housing System — Housing Phase, checked first of the three New
    /// Economy AI phases (see <see cref="Update"/>'s per-village loop):
    /// once Population catches up to <see cref="VillageHeart.MaxPopulation"/>,
    /// the tribe has physically run out of room to sprout into, so it
    /// hoards Food Stored until it can afford <see cref="TentFoodCost"/> and
    /// places a Tent Blueprint near its own centre instead. Completing it
    /// permanently raises MaxPopulation by <see cref="TentPopulationBonus"/>
    /// (see <see cref="CompleteBlueprint"/>), reopening the Growth Phase.
    /// Hard Cap: never queues a Tent once MaxPopulation has reached
    /// <see cref="MaxPopulationCap"/>, full stop — that ceiling is permanent,
    /// and past it the only way for a tribe to keep growing is the True
    /// Schism (see <see cref="UpdateSchism"/>).
    /// </summary>
    private void UpdateAutoTent(VillageHeart village)
    {
        if (village.MaxPopulation >= MaxPopulationCap)
            return; // Hard Cap: no more Tents, ever, regardless of Food Stored.
        if (village.Population < village.MaxPopulation)
            return; // Housing Phase not triggered: still room to grow.
        if (Blueprints.Any(b => b.Kind == BuildingKind.Tent && b.FactionID == village.FactionID))
            return; // Already building one; don't queue a second.
        if (village.FoodStored < TentFoodCost)
            return; // Saving toward a Tent.

        Vector3? spot = RandomPointNearVillage(village, TentPlacementRadius, Building.TentRadius + 0.2f);
        if (spot is { } point)
            TryPlaceBlueprint(village, point, BuildingKind.Tent);
    }

    /// <summary>
    /// Auto-Sprout — the Growth Phase, checked third (see <see cref="Update"/>'s
    /// per-village loop, after the Storage Phase — see <see cref="UpdateAutoGranary"/>'s
    /// Parallel Progress note): whenever Population is still below the Housing
    /// System's <see cref="VillageHeart.MaxPopulation"/> — entirely decoupled
    /// from MaxFoodCapacity/Granaries now — the Village Heart spends Food
    /// Stored on new Bramblekin as soon as it reaches <see cref="FoodSproutThreshold"/>
    /// — a Growth Buffer well above the <see cref="FoodPerSprout"/> cost itself,
    /// so a sprout always leaves a safety buffer of Food Stored behind rather
    /// than spending the colony down to the edge of starvation.
    /// Strictly Enforced: Food is deducted and a Bramblekin spawned only
    /// when doing so still keeps Population below MaxPopulation, checked
    /// fresh on every single iteration via a local running count rather
    /// than <see cref="VillageHeart.Population"/> itself (which the Job
    /// Manager only recomputes once a frame from the live Colony) — a large
    /// Food Stored windfall can spend down across several sprouts in one
    /// frame, but the loop condition below is re-evaluated before every one
    /// of them, so it can never push Population past MaxPopulation (the
    /// "41/40" bug). Once Population catches up, sprouting is disabled
    /// outright and the Housing Phase (see <see cref="UpdateAutoTent"/>)
    /// takes over instead.
    /// </summary>
    private void UpdateAutoSprout(VillageHeart village)
    {
        int sporeFarmCount = Buildings.Count(b => b.Kind == BuildingKind.SporeFarm && b.FactionID == village.FactionID)
            + Blueprints.Count(b => b.Kind == BuildingKind.SporeFarm && b.FactionID == village.FactionID);
        int targetSporeFarmCount = Math.Clamp(village.Population / 8, 1, 5);
        if (sporeFarmCount < targetSporeFarmCount)
            return; // Forbidden from Auto-Sprouting until farm quota is met.

        int projectedPopulation = village.Population;
        while (true)
        {
            // Strict Population Enforcement: re-checked before every single
            // sprout, not just once before the loop -- Food is deducted and
            // a Bramblekin spawned ONLY if Population (projected) is still
            // strictly below MaxPopulation.
            if (projectedPopulation >= village.MaxPopulation)
                return;
            if (village.FoodStored < FoodSproutThreshold)
                return;

            village.FoodStored -= FoodPerSprout;
            SproutBramblekin(village);
            projectedPopulation++;
        }
    }

    /// <summary>
    /// Auto-Construction (Granaries) — the Storage Phase, checked LAST (see
    /// <see cref="Update"/>'s per-village loop), after Housing and Growth
    /// have both already had first claim on Food Stored this frame. Only
    /// fires once Food Stored is actually maxed out (at or above the
    /// current MaxFoodCapacity) — there's no point banking surplus into
    /// more storage while a tribe still has empty houses to fill or mouths
    /// it could be feeding into new Bramblekin instead. Once maxed, the
    /// Village Heart places a Granary Blueprint (costing
    /// <see cref="GranaryFoodCost"/>) at a random unoccupied spot within
    /// <see cref="GranaryPlacementRadius"/> meters of itself, for the
    /// faction's dedicated Builder to work. Completing it permanently
    /// raises MaxFoodCapacity (see <see cref="CompleteBlueprint"/>),
    /// reopening headroom for the Trading Post/Amber economy. Guarded so at
    /// most one Auto-Granary is ever queued at a time, and capped at
    /// <see cref="MaxGranaries"/> total so the village can't spam Granaries
    /// forever — once it hits the cap, this simply stops firing.
    /// </summary>
    private void UpdateAutoGranary(VillageHeart village)
    {
        if (Buildings.Count(b => b.Kind == BuildingKind.Granary && b.FactionID == village.FactionID) >= MaxGranaries)
            return; // Capped: never queue another Granary.
        if (Blueprints.Any(b => b.Kind == BuildingKind.Granary && b.FactionID == village.FactionID))
            return; // Already building one; don't queue a second.
        if (village.FoodStored < village.MaxFoodCapacity)
            return; // Not maxed out yet — Housing/Growth still have first claim on Food Stored.

        Vector3? spot = RandomPointNearVillage(village, GranaryPlacementRadius, Building.GranaryRadius + 0.2f);
        if (spot is { } point)
            TryPlaceBlueprint(village, point, BuildingKind.Granary);
    }

    /// <summary>
    /// Auto-Construction (Spore Farm) — Scaling Domestic Farms: up to
    /// <see cref="MaxSporeFarmsPerVillage"/> Spore Farms per Village Heart, one
    /// more queued every time Population crosses another multiple of
    /// <see cref="SporeFarmPopulationThreshold"/> (1st at 10 Population, 2nd at
    /// 20, 3rd at 30, 4th at 40) — requires at least one Granary already up,
    /// and a Growth Buffer of its own: the Village Heart hoards Food Stored
    /// until it reaches <see cref="FoodSproutThreshold"/> before spending
    /// <see cref="SporeFarmFoodCost"/> of it on the next Blueprint, so farm
    /// expansion never itself starves the colony.
    /// </summary>
    private void UpdateAutoSporeFarm(VillageHeart village)
    {
        int sporeFarmCount = Buildings.Count(b => b.Kind == BuildingKind.SporeFarm && b.FactionID == village.FactionID)
            + Blueprints.Count(b => b.Kind == BuildingKind.SporeFarm && b.FactionID == village.FactionID);
        int targetSporeFarmCount = Math.Clamp(village.Population / 8, 1, 5);

        if (sporeFarmCount >= targetSporeFarmCount)
            return; // Already have (or are building) enough for the current Population.
        if (village.FoodStored < SporeFarmFoodCost)
            return; // MUST queue a SporePatch at 10 Food (cost).

        Vector3? spot = RandomPointNearVillage(village, SporeFarmPlacementRadius, Building.SporeFarmRadius + 0.2f);
        if (spot is { } point)
            TryPlaceBlueprint(village, point, BuildingKind.SporeFarm);
    }

    /// <summary>
    /// Auto-Construction (Trading Post) — The Blueprint Trigger: once a
    /// Village Heart's Population reaches <see cref="TradingPostPopulationThreshold"/>
    /// and it has banked at least <see cref="TradingPostAmberCost"/> Amber,
    /// and it doesn't already have a Trading Post (built or queued), it
    /// spends the Amber and places a Trading Post Blueprint near its own
    /// centre. Gatherers pick it up and build it exactly like a Granary or
    /// Spore Farm — <see cref="NearestIncompleteBlueprintFor"/> doesn't
    /// discriminate by <see cref="BuildingKind"/>.
    /// </summary>
    private void UpdateAutoTradingPost(VillageHeart village)
    {
        if (village.Population < TradingPostPopulationThreshold)
            return;
        if (village.AmberStored < TradingPostAmberCost)
            return;
        if (Buildings.Any(b => b.Kind == BuildingKind.TradingPost && b.FactionID == village.FactionID) ||
            Blueprints.Any(b => b.Kind == BuildingKind.TradingPost && b.FactionID == village.FactionID))
            return; // Already have one, finished or in progress.

        Vector3? spot = RandomPointNearVillage(village, TradingPostPlacementRadius, Building.TradingPostRadius + 0.2f);
        if (spot is not { } point)
            return;

        village.AmberStored -= TradingPostAmberCost;
        Blueprints.Add(new Blueprint(point, BuildingKind.TradingPost, village.FactionID, village.FactionColor));
    }

    /// <summary>
    /// Auto-Construction (The Nectar Brewery) — the Refined Economy: once a
    /// Village Heart's Population reaches <see cref="BreweryPopulationThreshold"/>
    /// and it has banked at least <see cref="BreweryAmberCost"/> Amber, and
    /// it doesn't already have one (built or queued), it spends the Amber
    /// and places a Brewery Blueprint near its own centre. Once built, it
    /// starts brewing on its own timer — see <see cref="UpdateNectarBrewery"/>.
    /// </summary>
    private void UpdateAutoBrewery(VillageHeart village)
    {
        if (village.Population < BreweryPopulationThreshold)
            return;
        if (village.AmberStored < BreweryAmberCost)
            return;
        if (Buildings.Any(b => b.Kind == BuildingKind.Brewery && b.FactionID == village.FactionID) ||
            Blueprints.Any(b => b.Kind == BuildingKind.Brewery && b.FactionID == village.FactionID))
            return; // Already have one, finished or in progress.

        Vector3? spot = RandomPointNearVillage(village, BreweryPlacementRadius, Building.BreweryRadius + 0.2f);
        if (spot is not { } point)
            return;

        village.AmberStored -= BreweryAmberCost;
        Blueprints.Add(new Blueprint(point, BuildingKind.Brewery, village.FactionID, village.FactionColor));
    }

    /// <summary>
    /// The Nectar Brewery's own economy: every <see cref="BreweryInterval"/>
    /// seconds, each finished Brewery attempts to consume
    /// <see cref="BreweryFoodCost"/> Food and <see cref="BreweryAmberUpkeep"/>
    /// Amber from its owning Village Heart's stores to brew
    /// <see cref="BreweryNectarYield"/> Nectar — a permanent civilization
    /// buff (see <see cref="VillageHeart.NectarStored"/>/<see cref="Bramblekin.EffectiveWalkSpeed"/>).
    /// If the village can't currently afford the brew, that cycle is simply
    /// skipped — the timer still resets and tries again next interval,
    /// exactly like a missed Upkeep tax doesn't destroy anything, just
    /// delays the payoff.
    /// </summary>
    private void UpdateNectarBrewery(float deltaTime)
    {
        for (int i = Buildings.Count - 1; i >= 0; i--)
        {
            Building building = Buildings[i];
            if (!building.TickBreweryTimer(deltaTime))
                continue;
            if (VillageFor(building.FactionID) is not { } village)
                continue;
            if (village.FoodStored < BreweryFoodCost || village.AmberStored < BreweryAmberUpkeep)
                continue;

            village.FoodStored -= BreweryFoodCost;
            village.AmberStored -= BreweryAmberUpkeep;
            village.NectarStored += BreweryNectarYield;
        }
    }

    /// <summary>
    /// The Great Monument — Civilization Goal: once a Village Heart reaches
    /// <see cref="MonumentPopulationThreshold"/> Population and has hoarded
    /// <see cref="MonumentAmberCost"/> Amber, it commits its entire Builder
    /// effort to one — see the per-village loop in <see cref="Update"/>,
    /// which skips every other Auto-Construction phase for as long as this
    /// returns true. Returns true while a Monument for this faction is
    /// queued (or was just queued this frame) and not yet finished; false
    /// once it's either not eligible yet or already stands complete, either
    /// of which lets ordinary building resume.
    /// </summary>
    private bool UpdateAutoMonument(VillageHeart village)
    {
        if (Buildings.Any(b => b.Kind == BuildingKind.Monument && b.FactionID == village.FactionID))
            return false; // Already an advanced civilization — back to ordinary building.

        if (Blueprints.Any(b => b.Kind == BuildingKind.Monument && b.FactionID == village.FactionID))
            return true; // Already committed — keep suppressing everything else until it's done.

        if (village.Population < MonumentPopulationThreshold || village.AmberStored < MonumentAmberCost)
            return false; // Not there yet.

        Vector3? spot = RandomPointNearVillage(village, MonumentPlacementRadius, Building.MonumentRadius + 0.3f);
        if (spot is not { } point)
            return false; // No room right now — try again next frame rather than stalling the tribe on nothing.

        village.AmberStored -= MonumentAmberCost;
        Blueprints.Add(new Blueprint(point, BuildingKind.Monument, village.FactionID, village.FactionColor));
        return true;
    }

    /// <summary>
    /// Emergency Food Import: once a Trading Post is fully built, it
    /// unlocks automated trading — called from <see cref="UpdateUpkeep"/>
    /// the instant a village's Upkeep tax comes due while it's sitting on
    /// zero Food Stored (a Bramblekin is about to starve). If it has at
    /// least <see cref="EmergencyImportAmberCost"/> Amber banked, the
    /// Trading Post deducts it and instantly adds <see cref="EmergencyImportFoodGain"/>
    /// Food Stored, with a floating "Trade: -1 Amber / +5 Food" alert above
    /// the Trading Post itself so the autonomous economy saving the tribe
    /// is visible. A no-op if the faction has no finished Trading Post, or
    /// no Amber left to spend.
    /// </summary>
    private void TryEmergencyFoodImport(VillageHeart village)
    {
        Building? tradingPost = Buildings.FirstOrDefault(b => b.Kind == BuildingKind.TradingPost && b.FactionID == village.FactionID);
        if (tradingPost is null)
            return;
        if (village.AmberStored < EmergencyImportAmberCost)
            return;

        village.AmberStored -= EmergencyImportAmberCost;
        village.FoodStored = Math.Min(village.FoodStored + EmergencyImportFoodGain, village.MaxFoodCapacity);
        QueueFloatingText(tradingPost.Position, $"Trade: -{EmergencyImportAmberCost} Amber / +{EmergencyImportFoodGain} Food", new Color(255, 203, 0, 255));
    }

    /// <summary>
    /// The True Schism — The Split Fix: once a Village Heart is both
    /// physically maxed out on housing (Population at
    /// <see cref="MaxPopulationCap"/> — it has nowhere left to sprout into,
    /// full stop) and has banked at least <see cref="SchismFoodThreshold"/>
    /// Food Stored, it splits in two immediately — it no longer waits to
    /// fill a (now potentially 260-capacity) silo all the way to
    /// MaxFoodCapacity before relieving the pressure. A fixed
    /// <see cref="SchismPioneerCount"/> Bramblekin (same Gatherer/Militia
    /// ratio as the parent, rounded down, so a heavily militarized tribe
    /// doesn't send off a defenseless splinter) depart as Pioneers, taking a
    /// fixed <see cref="SchismPioneerFood"/> Food Stored with them, to found
    /// a brand new faction elsewhere on the map, leaving the parent at
    /// roughly half Population and comfortably fed — see
    /// <see cref="Bramblekin.BecomePioneer"/> and <see cref="FoundVillage"/>.
    /// A fresh splinter this size is no longer easy prey, and Default Peace
    /// (<see cref="VillageHeart.HostileFactions"/>) means it starts out at
    /// peace with the parent it just split from automatically — no
    /// separate grace-period timer needed any more. Guarded by
    /// <see cref="VillageHeart.HasActiveMigration"/> so only one Migration
    /// is ever in flight per origin at a time.
    /// </summary>
    private void UpdateSchism(VillageHeart village)
    {
        if (village.HasActiveMigration)
            return;
        if (village.Population < MaxPopulationCap)
            return; // Housing isn't maxed out yet — still room to grow in place.
        if (village.FoodStored < SchismFoodThreshold)
            return; // The Split Fix: 100 Food is plenty to send a party off safely — no need to wait for a full silo.

        int totalGatherers = Colony.Count(b => !b.IsDead && b.FactionID == village.FactionID && b.Role == BramblekinRole.Gatherer);
        int totalMilitia = Colony.Count(b => !b.IsDead && b.FactionID == village.FactionID && b.Role == BramblekinRole.Militia);
        int totalLiving = totalGatherers + totalMilitia;
        if (totalLiving < SchismPioneerCount)
            return; // Someone died since Population was last counted; try again next frame.

        // Same Gatherer/Militia ratio as the parent (rounded down), but
        // adding up to a fixed SchismPioneerCount total rather than a flat
        // half of each role — see SchismPioneerCount.
        int pioneerGatherers = totalGatherers * SchismPioneerCount / totalLiving;
        int pioneerMilitia = SchismPioneerCount - pioneerGatherers;
        if (pioneerMilitia > totalMilitia)
        {
            pioneerMilitia = totalMilitia;
            pioneerGatherers = SchismPioneerCount - pioneerMilitia;
        }
        if (pioneerGatherers > totalGatherers)
            return; // Not enough of either role yet to make up a full pioneer party.

        var pioneers = new List<Bramblekin>(SchismPioneerCount);
        int gathererCount = 0, militiaCount = 0;
        for (int i = Colony.Count - 1; i >= 0; i--)
        {
            if (gathererCount >= pioneerGatherers && militiaCount >= pioneerMilitia)
                break;

            Bramblekin bramblekin = Colony[i];
            if (bramblekin.IsDead || bramblekin.FactionID != village.FactionID)
                continue;

            if (bramblekin.Role == BramblekinRole.Gatherer && gathererCount < pioneerGatherers)
            {
                pioneers.Add(bramblekin);
                gathererCount++;
            }
            else if (bramblekin.Role == BramblekinRole.Militia && militiaCount < pioneerMilitia)
            {
                pioneers.Add(bramblekin);
                militiaCount++;
            }
        }

        if (gathererCount < pioneerGatherers || militiaCount < pioneerMilitia)
            return; // Someone died mid-count; try again next frame.

        // The Wealth Transfer: exactly SchismPioneerFood leaves the parent's
        // stores — the new Village Heart is seeded with exactly the same
        // amount in FoundVillage below, via Migration.FoodAmount.
        village.FoodStored -= SchismPioneerFood;
        village.HasActiveMigration = true;

        // The Physical Population Transfer: every pioneer's FactionID flips
        // the instant it becomes a Pioneer (see Bramblekin.BecomePioneer,
        // called below) — from that point on, world.VillageFor(FactionID)
        // (the Gatherer/Militia's own lookup of "my Village Heart") already
        // resolves to the new faction rather than this one, so nothing
        // further is needed to redirect their loyalty. Population itself is
        // just a live count of Colony by FactionID (see UpdateJobManager),
        // recomputed every frame — but it's adjusted here too, immediately,
        // rather than left to wait for that recompute, so the parent's own
        // Population never reads stale-high for even a single frame after a
        // Schism it already committed to.
        village.Population -= pioneers.Count;

        int newFactionId = _nextSchismFactionId++;
        Color newFactionColor = SchismFactionColors[(newFactionId - 1) % SchismFactionColors.Length];
        var migration = new Migration(newFactionId, newFactionColor, RandomMigrationTarget(), village, pioneers.Count, SchismPioneerFood);

        foreach (var pioneer in pioneers)
            pioneer.BecomePioneer(migration);
    }

    /// <summary>
    /// Strict Migration Distance: a random point at least
    /// <see cref="MinMigrationDistance"/> meters from every existing
    /// Village Heart — a Schism's destination. Overcrowding Fallback: if
    /// none of <see cref="MigrationTargetAttempts"/> random tries lands
    /// clean, the 100x100 map is genuinely too crowded for a gap that wide,
    /// so settle for the least-bad candidate tried (the one furthest from
    /// its nearest Village Heart) and accept that territorial war with a
    /// close neighbour is now unavoidable.
    /// </summary>
    private Vector3 RandomMigrationTarget()
    {
        Vector3 best = Vector3.Zero;
        float bestDistance = -1f;
        for (int attempt = 0; attempt < MigrationTargetAttempts; attempt++)
        {
            Vector3 candidate = Terrain.RandomPoint(Rng, margin: 2f);
            float nearestVillage = Villages.Count == 0 ? float.MaxValue : Villages.Min(v => Vector3.Distance(candidate, v.Center));
            if (nearestVillage >= MinMigrationDistance)
                return candidate;

            if (nearestVillage > bestDistance)
            {
                bestDistance = nearestVillage;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>The Refugee Protocol: how far (m) from every surviving Village Heart a razed faction's resettlement point must land.</summary>
    private const float RefugeeSafeDistance = 30f;

    /// <summary>
    /// The Refugee Protocol: a random point at least
    /// <see cref="RefugeeSafeDistance"/> meters from every surviving
    /// Village Heart, for a just-razed faction's Gatherers to flee to and
    /// found a new Village Heart from scratch. Unlike <see cref="RandomMigrationTarget"/>
    /// there is no furthest-point fallback here — a null result means the
    /// map is genuinely full of other tribes, and <see cref="DestroyVillageHeart"/>
    /// falls back to Assimilation instead of sending refugees to their
    /// deaths in someone else's territory.
    /// </summary>
    private Vector3? RandomRefugeeTarget()
    {
        for (int attempt = 0; attempt < MigrationTargetAttempts; attempt++)
        {
            Vector3 candidate = Terrain.RandomPoint(Rng, margin: 2f);
            if (Villages.All(v => Vector3.Distance(candidate, v.Center) >= RefugeeSafeDistance))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// The True Schism's payoff: founds a brand new Village Heart at
    /// <paramref name="migration"/>'s Target, seeded with exactly
    /// <see cref="Migration.FoodAmount"/> Food Stored (the fixed
    /// <see cref="SchismPioneerFood"/> handed over at the moment it split —
    /// see <see cref="UpdateSchism"/>'s Wealth Transfer) and a Population
    /// counted fresh from every living Bramblekin already carrying this
    /// migration's FactionID (set the instant each one became a Pioneer, in
    /// <see cref="Bramblekin.BecomePioneer"/> — normally exactly
    /// <see cref="SchismPioneerCount"/>, one fewer per any Pioneer lost en
    /// route), so the new tribe never reads as a lone, starving founder.
    ///
    /// The Founding Housing Fix: MaxPopulation is seeded to at least that
    /// same Population, not left at VillageHeart's plain-founding default of
    /// 10. Population always starts well above 10 here (normally 20) — if
    /// MaxPopulation were left at 10, UpdateAutoTent's Housing Phase would
    /// see the tribe as instantly "overcrowded" the moment it lands and
    /// immediately pull every single Gatherer off food duty into a
    /// back-to-back Tent-building spree (up to 6 Tents, to climb from 10 to
    /// the new tribe's actual headcount) — spending down its starting Food
    /// Stored on Tent costs while gathering zero food income, at exactly the
    /// moment it's most exposed (thin reserves, no located food nearby yet).
    /// That's the real starvation death spiral behind a freshly-split tribe
    /// grinding down toward 1 population and never recovering: not a bad
    /// transfer, but new housing debt the transfer itself creates.
    ///
    /// UpkeepTimer starts <see cref="SchismUpkeepGracePeriod"/> seconds
    /// beyond the normal cycle, so it isn't taxed the moment it lands. Then
    /// immediately starts running its own autonomous economy loop alongside
    /// every other entry in <see cref="Villages"/>. Default Peace means it
    /// starts out diplomatically at peace with every other faction, the
    /// parent it split from included — no separate peace-grace-period timer
    /// needed on top of the Upkeep one above. Marks the Migration founded so
    /// every Pioneer bound to it — not just the one that triggered this —
    /// drops Migrating for good on its very next Update() (see
    /// <see cref="Bramblekin.UpdateMigrating"/>), and clears the origin's
    /// <see cref="VillageHeart.HasActiveMigration"/> so it's free to schism
    /// again once it re-crowds.
    /// </summary>
    public VillageHeart FoundVillage(Migration migration)
    {
        int foundingPopulation = Colony.Count(b => !b.IsDead && b.FactionID == migration.NewFactionID);
        var village = new VillageHeart(migration.Target, migration.NewFactionID, migration.NewFactionColor, Rng)
        {
            FoodStored = migration.FoodAmount,
            Population = foundingPopulation,
            MaxPopulation = Math.Max(10, foundingPopulation),
            UpkeepTimer = UpkeepInterval + SchismUpkeepGracePeriod,
        };
        Villages.Add(village);
        RebuildObstacles();

        migration.MarkFounded();
        migration.Origin.HasActiveMigration = false;
        return village;
    }

    /// <summary>Gatherers Genesis instantly spawns beside the new Village Heart, so the economy can restart immediately.</summary>
    private const int GenesisGathererCount = 2;

    /// <summary>How far (m) from the new Village Heart's centre a Genesis Gatherer may land.</summary>
    private const float GenesisSpawnRadius = 3f;

    /// <summary>
    /// Genesis: the player's one lever to recover from total extinction —
    /// every Village Heart gone means no Job Manager, no Auto-Sprout,
    /// nothing left to run the game's economy on its own (see the
    /// Extinction check in <see cref="Update"/>). Instantly founds a brand
    /// new Faction 0 (green) Village Heart at <paramref name="groundPoint"/>,
    /// with <see cref="GenesisGathererCount"/> Gatherers spawned right
    /// beside it so it isn't left standing empty. Called from
    /// <see cref="WorldTapInput"/>'s tap handling once it's confirmed the
    /// world is dead — Free Extinction Recovery: an autonomous simulation
    /// still needs some way back from total extinction rather than sitting
    /// on a permanently empty map forever.
    /// </summary>
    public void Genesis(Vector3 groundPoint)
    {
        var village = new VillageHeart(groundPoint, factionId: 0, factionColor: new Color(40, 180, 90, 255), Rng);
        Villages.Add(village);
        RebuildObstacles();

        for (int i = 0; i < GenesisGathererCount; i++)
        {
            Vector3 spot = RandomPointNearVillage(village, GenesisSpawnRadius, Bramblekin.BodyRadius + 0.1f)
                           ?? RandomFreePoint(Bramblekin.BodyRadius, Bramblekin.EdgeMargin);
            Colony.Add(new Bramblekin(spot, Rng, village.FactionID, village.FactionColor));
        }
    }

    /// <summary>
    /// Passive Income: every finished Spore Farm spawns a Berry (Food
    /// Shard) directly on top of itself every <see cref="Building.SporeFarmInterval"/>
    /// seconds, for Gatherers to pick up and deliver like any other food.
    /// </summary>
    private void UpdateSporeFarmIncome(float deltaTime)
    {
        for (int i = Buildings.Count - 1; i >= 0; i--)
        {
            Building building = Buildings[i];
            if (building.TickSporeTimer(deltaTime))
                _pendingShardSpawns.Add((building.Position, FoodShardKind.Berry));
        }
    }

    /// <summary>
    /// Upkeep — a true survival economy. Every <see cref="UpkeepInterval"/>
    /// seconds the Village Heart pays a food tax of Math.Max(1, Population/5).
    /// If Food Stored can cover it, the cost is deducted and a "-X Food"
    /// pop-up appears above the Village Heart. If it can't, Food Stored is
    /// drained to zero outright and one Bramblekin — a Gatherer if there is
    /// one, a Militia unit otherwise — dies of starvation on the spot, with
    /// a red "Starving!" pop-up.
    ///
    /// Emergency Food Import: right as this tax comes due, a Trading Post
    /// gets first crack at a village sitting on zero Food Stored — see
    /// <see cref="TryEmergencyFoodImport"/> — before the tax (and, if that
    /// still isn't enough, starvation) is even computed.
    /// </summary>
    private void UpdateUpkeep(VillageHeart village, float deltaTime)
    {
        village.UpkeepTimer -= deltaTime;
        if (village.UpkeepTimer > 0f)
            return;
        village.UpkeepTimer += UpkeepInterval;

        if (village.FoodStored == 0)
            TryEmergencyFoodImport(village);

        int cost = Math.Max(1, village.Population / 8);
        if (village.FoodStored >= cost)
        {
            village.FoodStored -= cost;
            QueueFloatingText(village.Center, $"-{cost} Food", Color.White);
            return;
        }

        village.FoodStored = 0;
        Bramblekin? victim = NearestByRole(village, BramblekinRole.Gatherer)
            ?? NearestByRole(village, BramblekinRole.Builder)
            ?? NearestByRole(village, BramblekinRole.Militia);
        if (victim is { } v)
            Kill(v);
        QueueFloatingText(village.Center, "Starving!", new Color(220, 30, 30, 255));
    }

    /// <summary>Queues a floating text pop-up (see <see cref="FloatingTexts"/>) at a world position.</summary>
    private void QueueFloatingText(Vector3 position, string text, Color color) =>
        _floatingTexts.Add((position, text, color, FloatingTextDuration));

    /// <summary>A random point within <paramref name="maxRadius"/> meters of <paramref name="village"/> that isn't blocked. Null if nothing opened up in a handful of tries.</summary>
    public Vector3? RandomPointNearVillage(VillageHeart village, float maxRadius, float clearance)
    {
        float minRadius = village.Obstacle.Radius + 0.5f;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float radius = minRadius + (float)Rng.NextDouble() * MathF.Max(maxRadius - minRadius, 0f);
            var point = village.Center + new Vector3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);
            if (!IsBlocked(point, clearance) && Terrain.Contains(point, 0f))
                return point;
        }
        return null;
    }

    /// <summary>True if <paramref name="point"/> falls inside ANY Village Heart's own (Cultural Borders — wealth-scaled, see <see cref="VillageHeart.TerritoryRadius"/>) ring, regardless of faction.</summary>
    private bool IsInsideAnyTerritory(Vector3 point)
    {
        for (int i = Villages.Count - 1; i >= 0; i--)
        {
            VillageHeart village = Villages[i];
            if (Vector3.DistanceSquared(point, village.Center) <= village.TerritoryRadius * village.TerritoryRadius)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Full Map Resource Spawning (the 100x100 Fix): a uniformly random open
    /// point anywhere across the entire terrain — not seeded near any one
    /// faction's territory the way the old Territory Resource Spawning was
    /// — with every Village Heart's <see cref="VillageHeart.TerritoryRadius"/>
    /// ring explicitly excluded (see <see cref="IsInsideAnyTerritory"/>), so
    /// wild Berries, Acorns and Amber populate the empty wilderness between
    /// tribes instead of clustering into whatever small patch of the map
    /// those rings happen to cover. Falls back to whatever the last (still
    /// open, just not territory-clear) candidate was if nothing outside
    /// every ring turns up in a reasonable number of tries — better a rare
    /// spawn just inside someone's border than none at all.
    /// </summary>
    private Vector3 RandomWildernessSpot(float clearance, float edgeMargin)
    {
        Vector3 candidate = Vector3.Zero;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            candidate = Terrain.RandomPoint(Rng, edgeMargin);
            if (IsBlocked(candidate, clearance))
                continue;
            if (IsInsideAnyTerritory(candidate))
                continue;
            return candidate;
        }

        return candidate;
    }

    /// <summary>
    /// Village Building: spends the Blueprint kind's Food cost (<see cref="GranaryFoodCost"/>,
    /// <see cref="SporeFarmFoodCost"/> or <see cref="TentFoodCost"/>) to
    /// place a Blueprint owned by <paramref name="village"/>'s Faction at
    /// <paramref name="groundPoint"/> — called by the Village Heart's own
    /// Auto-Construction (<see cref="UpdateAutoTent"/>/<see cref="UpdateAutoGranary"/>/<see cref="UpdateAutoSporeFarm"/>).
    /// The Trading Post is the one exception: it's priced in Amber, not
    /// Food, so <see cref="UpdateAutoTradingPost"/> places its Blueprint
    /// directly instead of going through here. Returns false (and spends
    /// nothing) if there isn't enough Food Stored.
    /// </summary>
    public bool TryPlaceBlueprint(VillageHeart village, Vector3 groundPoint, BuildingKind kind = BuildingKind.Granary)
    {
        int cost = kind switch
        {
            BuildingKind.Granary => GranaryFoodCost,
            BuildingKind.Tent => TentFoodCost,
            _ => SporeFarmFoodCost,
        };
        if (village.FoodStored < cost)
            return false;

        village.FoodStored -= cost;
        Blueprints.Add(new Blueprint(groundPoint, kind, village.FactionID, village.FactionColor));
        return true;
    }

    /// <summary>Whether any Blueprint belonging to <paramref name="factionId"/> still needs Builder hands.</summary>
    public bool HasIncompleteBlueprintFor(int factionId) => Blueprints.Any(b => b.FactionID == factionId);

    /// <summary>The nearest Blueprint belonging to <paramref name="factionId"/> to <paramref name="from"/>, if any — AI Faction Loyalty: a Builder only ever works its own faction's sites.</summary>
    public Blueprint? NearestIncompleteBlueprintFor(Vector3 from, int factionId)
    {
        Blueprint? best = null;
        float bestDistance = float.MaxValue;
        for (int i = Blueprints.Count - 1; i >= 0; i--)
        {
            Blueprint blueprint = Blueprints[i];
            if (blueprint.FactionID != factionId)
                continue;

            float distance = Vector3.DistanceSquared(from, blueprint.Position);
            if (distance < bestDistance)
            {
                best = blueprint;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>
    /// Finishes a Blueprint once a Builder's Construction Progress reaches
    /// its requirement: removes the site and adds the completed Building,
    /// carrying over the Blueprint's Faction. A finished Granary permanently
    /// raises its owning Village Heart's MaxFoodCapacity and nothing else —
    /// the Housing System decouples Wealth Accumulation from Population
    /// growth entirely; a finished Tent permanently raises MaxPopulation
    /// instead (and only that); a finished Spore Farm raises neither but
    /// starts its own passive-income timer (see <see cref="UpdateSporeFarmIncome"/>).
    /// Called from inside a Bramblekin's own Update() (itself inside World's
    /// reverse for-loop over Colony), but mutates Blueprints/Buildings
    /// directly rather than through a pending queue: nothing else iterates
    /// either list while the Colony loop is running, so — unlike
    /// Colony/FoodShards/Aphids — there's no concurrent-modification hazard
    /// here to defer around.
    /// </summary>
    public void CompleteBlueprint(Blueprint blueprint)
    {
        Blueprints.Remove(blueprint);
        Buildings.Add(new Building(blueprint.Position, blueprint.Kind, blueprint.FactionID, blueprint.FactionColor));
        if (VillageFor(blueprint.FactionID) is not { } owner)
            return;

        if (blueprint.Kind == BuildingKind.Granary)
            owner.MaxFoodCapacity += GranaryFoodBonus;
        else if (blueprint.Kind == BuildingKind.Tent)
            owner.MaxPopulation = Math.Min(owner.MaxPopulation + TentPopulationBonus, MaxPopulationCap);
        else if (blueprint.Kind == BuildingKind.Monument)
            _completedMonuments.Add((blueprint.FactionID, blueprint.FactionColor));
    }

    /// <summary>Queues a new Bramblekin of <paramref name="village"/>'s Faction on a free spot right beside it.</summary>
    private void SproutBramblekin(VillageHeart village)
    {
        float distance = village.Obstacle.Radius + Bramblekin.BodyRadius + 0.2f;
        float startAngle = (float)(Rng.NextDouble() * MathF.Tau);
        Vector3 spot = village.Center + new Vector3(distance, 0, 0);

        // Try 12 spots round the village; take the first free one.
        for (int i = 0; i < 12; i++)
        {
            float angle = startAngle + i * MathF.Tau / 12;
            var candidate = village.Center + new Vector3(MathF.Cos(angle) * distance, 0, MathF.Sin(angle) * distance);
            if (!IsBlocked(candidate, Bramblekin.BodyRadius) && Terrain.Contains(candidate, Bramblekin.EdgeMargin))
            {
                spot = candidate;
                break;
            }
        }

        _pendingBramblekinSpawns.Add(new Bramblekin(spot, Rng, village.FactionID, village.FactionColor));
        Births++;
    }

    /// <summary>A random point on the terrain that isn't inside an obstacle.</summary>
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

    // --- Internals ----------------------------------------------------------------

    /// <summary>Collects the solid circles on the ground: every Village Heart's footprint.</summary>
    private void RebuildObstacles()
    {
        _obstacles.Clear();
        foreach (var village in Villages)
            _obstacles.Add(village.Obstacle);
    }

    /// <summary>
    /// The Spatial Grid: every living Bramblekin registered within 10m
    /// chunks of <paramref name="position"/> (its own chunk plus the 8
    /// neighbors) — used by the Wolf Spider's prey/Militia search and an
    /// Aphid's flee check, same restricted-scan pattern as the Bramblekin
    /// resource searches. The returned list is a reused scratch buffer:
    /// safe to iterate immediately, but don't hold onto it past the call
    /// that reads it.
    /// </summary>
    public List<Bramblekin> QueryNearbyColony(Vector3 position)
    {
        _colonyGrid.QueryNearby(position, _colonyQueryBuffer);
        return _colonyQueryBuffer;
    }

    /// <summary>The Spatial Grid: every active, gatherable Food Shard/Acorn/AmberNode and every living Bramblekin, re-registered into its current 10m chunk. Rebuilt fresh once a frame, same pattern as <see cref="RebuildObstacles"/>, rather than tracked incrementally as each entity moves.</summary>
    private void RebuildSpatialGrids()
    {
        _foodGrid.Clear();
        foreach (FoodShard shard in FoodShards)
        {
            if (shard.IsActive && !shard.IsCarried)
                _foodGrid.Register(shard, shard.Position);
        }

        _acornGrid.Clear();
        foreach (Acorn acorn in Acorns)
        {
            if (acorn.IsActive)
                _acornGrid.Register(acorn, acorn.Position);
        }

        _amberGrid.Clear();
        foreach (AmberNode amber in AmberNodes)
        {
            if (amber.IsActive && !amber.IsCarried)
                _amberGrid.Register(amber, amber.Position);
        }

        _colonyGrid.Clear();
        foreach (Bramblekin bramblekin in Colony)
        {
            if (!bramblekin.IsDead)
                _colonyGrid.Register(bramblekin, bramblekin.Position);
        }
    }

    /// <summary>
    /// Rocks shove food aside instead of burying it: any shard on the ground
    /// that overlaps a rock (or the village) is slid straight out along the
    /// line from the obstacle's centre. A second pass catches a shard pushed
    /// from one rock into a neighbouring one.
    /// </summary>
    private void PushFoodOutOfObstacles()
    {
        float half = Terrain.Size / 2f - FoodShard.Radius;

        for (int i = FoodShards.Count - 1; i >= 0; i--)
        {
            FoodShard shard = FoodShards[i];
            if (!shard.IsActive || shard.IsCarried) // Object Pooling: an inactive slot isn't really sitting anywhere.
                continue;

            var position = new Vector2(shard.Position.X, shard.Position.Z);
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var obstacle in _obstacles)
                {
                    Vector2 offset = position - obstacle.Center;
                    float minDistance = obstacle.Radius + FoodShard.Radius;
                    float distanceSquared = offset.LengthSquared();
                    if (distanceSquared >= minDistance * minDistance)
                        continue;

                    // Dead centre has no direction: pick one at random.
                    float distance = MathF.Sqrt(distanceSquared);
                    Vector2 normal = distance > 1e-5f
                        ? offset / distance
                        : Vector2.Normalize(new Vector2((float)Rng.NextDouble() - 0.5f, (float)Rng.NextDouble() - 0.5f) + new Vector2(1e-3f, 0));
                    position = obstacle.Center + normal * minDistance;
                }
            }

            shard.Position = new Vector3(
                Math.Clamp(position.X, -half, half),
                Terrain.GroundHeight,
                Math.Clamp(position.Y, -half, half));
        }
    }

    /// <summary>
    /// Cooperative Acorn Cracking + Strict Border Control (see
    /// <see cref="IsForeignTerritory"/>) + Maximum Search Radius (see
    /// <see cref="MaxGatherSearchRadius"/>): the nearest Acorn
    /// <paramref name="gatherer"/> (a Chitin-Mallet Gatherer) either
    /// already holds a claim on or can still claim a free slot on —
    /// <see cref="Acorn.MaxClaimants"/> may work the same Acorn at once.
    /// Sorted by distance like any other target; an Acorn sitting inside a
    /// rival's territory ring is excluded outright — never a candidate, no
    /// matter how desperate the gatherer's own village is — and one any
    /// further than <see cref="MaxGatherSearchRadius"/> is never even
    /// considered. No more suicidal cross-border mining runs.
    /// </summary>
    public Acorn? NearestClaimableAcorn(Vector3 from, Bramblekin gatherer)
    {
        Acorn? best = null;
        float bestDistanceSquared = float.MaxValue;
        float maxGatherSearchRadiusSquared = MaxGatherSearchRadius * MaxGatherSearchRadius;
        // The Spatial Grid: only from's own 10m chunk and its 8 neighbors.
        _acornGrid.QueryNearby(from, _acornQueryBuffer);
        for (int i = _acornQueryBuffer.Count - 1; i >= 0; i--)
        {
            Acorn acorn = _acornQueryBuffer[i];
            if (!acorn.IsActive) // Object Pooling: an inactive slot is not a real Acorn.
                continue;
            if (!acorn.IsClaimedBy(gatherer) && acorn.Claimants.Count >= Acorn.MaxClaimants)
                continue;
            if (IsForeignTerritory(acorn.Position, gatherer.FactionID))
                continue; // Strict Border Control: off-limits, full stop.

            float distanceSquared = Vector3.DistanceSquared(from, acorn.Position);
            if (distanceSquared > maxGatherSearchRadiusSquared)
                continue; // Maximum Search Radius: never even evaluated, last resort or not.

            if (distanceSquared < bestDistanceSquared)
            {
                best = acorn;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>
    /// Continuous Cracking: shatters any Acorn whose CrackProgress has
    /// reached its CrackThreshold — checked once a frame, after the Colony
    /// loop has added every Cracking Gatherer's contribution for the frame,
    /// so several claimants finishing it off in the same frame can never
    /// cause a double-shatter. The Shatter Trigger: explicitly hands every
    /// claimant back from Cracking to Gathering (see Bramblekin.OnAcornShattered)
    /// so they immediately call dibs on the fresh Food Shards instead of
    /// idling with a now-dangling Acorn reference.
    /// </summary>
    private void UpdateAcornCracking()
    {
        for (int i = Acorns.Count - 1; i >= 0; i--)
        {
            Acorn acorn = Acorns[i];
            if (!acorn.IsActive || acorn.CrackProgress < acorn.CrackThreshold) // Object Pooling: an inactive slot never shatters.
                continue;

            foreach (Bramblekin claimant in acorn.Claimants)
                claimant.OnAcornShattered();

            ScatterFoodShardsAround(acorn.Position, ShardsPerAcorn, Acorn.Radius + FoodShard.Radius + 0.35f);
            acorn.Deactivate();
        }
    }

    /// <summary>
    /// Scatters <paramref name="count"/> Food Shards in a ring
    /// <paramref name="distance"/> meters out from <paramref name="center"/>,
    /// clamped to stay on the terrain — Cooperative Acorn Cracking's shatter.
    /// Queued rather than added directly, same as any other spawn from
    /// inside World.Update().
    /// </summary>
    private void ScatterFoodShardsAround(Vector3 center, int count, float distance)
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
            _pendingShardSpawns.Add((position, FoodShardKind.Cracked));
        }
    }

    /// <summary>
    /// Sustained Combat: applies Militia poke damage to the spider and, if
    /// that brings its Health to 0, kills it outright via the same
    /// despawn/respawn-timer path. Purely a Health mutation otherwise: never
    /// touches State.
    /// </summary>
    public void DamageSpider(int amount)
    {
        if (Spider is null)
            return;

        Spider.TakeDamage(amount);
        if (Spider.Health <= 0)
            DespawnSpider();
    }

    /// <summary>
    /// Removes the spider, leaves a splat where it stood, and drops one
    /// Spider Fang and one Chitin right where it died — Individual Equipment's
    /// raw materials (see <see cref="ConsumeFang"/>/<see cref="ConsumeChitin"/>)
    /// — then starts the respawn timer.
    /// </summary>
    private void DespawnSpider()
    {
        if (Spider is null)
            return;

        _splats.Add((Spider.Position, SplatDuration));
        _pendingFangSpawns.Add(new SpiderFang(Spider.Position));
        _pendingChitinSpawns.Add(new Chitin(Spider.Position));
        Spider = null;
        SpiderRespawnTimer = SpiderRespawnDelay;
    }

    private void UpdateSpiderRespawn(float deltaTime)
    {
        if (Spider is not null || SpiderRespawnTimer <= 0f)
            return;

        SpiderRespawnTimer -= deltaTime;
        if (SpiderRespawnTimer <= 0f)
            SpawnSpiderNearVillage();
    }

    /// <summary>Tops the Acorn population back up to <see cref="MaxAcorns"/> every <see cref="AcornSpawnInterval"/> seconds, same pattern as Berries and Aphids.</summary>
    private void UpdateAcornSpawn(float deltaTime)
    {
        _acornSpawnTimer -= deltaTime;
        if (_acornSpawnTimer > 0f)
            return;
        _acornSpawnTimer = AcornSpawnInterval;

        // Object Pooling: Acorns.Count is now the fixed pool size, not the
        // live count — MaxAcorns caps how many are actually active.
        if (Acorns.Count(a => a.IsActive) >= MaxAcorns)
            return;

        ActivateAcorn(RandomAcornSpot());
    }

    /// <summary>Somewhere open anywhere on the map, outside every Village Heart's Territory Ring — see <see cref="RandomWildernessSpot"/>.</summary>
    private Vector3 RandomAcornSpot() => RandomWildernessSpot(Acorn.Radius + 0.5f, edgeMargin: 1.5f);

    /// <summary>
    /// Tycoon Economy: tops the map-wide Amber population back up to
    /// <see cref="MaxAmberOnMap"/> every <see cref="AmberSpawnInterval"/>
    /// seconds — kept scarce (at most 3 on the map at once) and spread
    /// anywhere valid across the whole map, outside every Village Heart's
    /// Territory Ring, same pattern as Acorns and Berries.
    /// </summary>
    private void UpdateAmberSpawn(float deltaTime)
    {
        _amberSpawnTimer -= deltaTime;
        if (_amberSpawnTimer > 0f)
            return;
        _amberSpawnTimer = AmberSpawnInterval;

        // Object Pooling: AmberNodes.Count is now the fixed pool size, not
        // the live count — MaxAmberOnMap caps how many are actually active.
        if (AmberNodes.Count(a => a.IsActive) >= MaxAmberOnMap)
            return;

        ActivateAmberNode(RandomWildernessSpot(AmberNode.Radius + 0.5f, edgeMargin: 1.5f));
    }
}

// =============================================================================
//  Economy objects
// =============================================================================

/// <summary>
/// Faction Personalities: a Village Heart's independent, fixed-for-life
/// stance on military vs. economy, randomly assigned the moment it's
/// founded (the original Village Heart included) — see <see cref="VillageHeart.Trait"/>
/// and <see cref="World.MilitiaTargetDivisorFor"/>.
/// </summary>
public enum FactionTrait
{
    /// <summary>1 Militia per 5 Gatherers (Population / 6).</summary>
    Balanced,

    /// <summary>1 Militia per 2 Gatherers (Population / 3) — highly aggressive.</summary>
    Militaristic,

    /// <summary>1 Militia per 5 Gatherers (Population / 6) — maximizes food collection.</summary>
    Agrarian,
}

/// <summary>
/// The Village Heart: a faction's home and food store. A static brown block
/// that its own Bramblekin deliver food to. Phase 3: each faction gets its
/// own instance with its own economy (<see cref="VillageHeart.FoodStored"/>,
/// <see cref="VillageHeart.Population"/>, <see cref="VillageHeart.MaxFoodCapacity"/>,
/// <see cref="VillageHeart.Morale"/>) rather than sharing one set of numbers off
/// <see cref="World"/> — see <see cref="World.Villages"/> and the
/// per-village loop in <see cref="World.Update"/>.
/// </summary>
public sealed class VillageHeart
{
    /// <summary>Footprint edge length, in meters.</summary>
    public const float Width = 1.6f;

    public const float Height = 1.2f;

    /// <summary>Base Razing: hit points out of <see cref="MaxHealth"/>. Reduced by an enemy Militia's Poke (see <see cref="World.DamageVillageHeart"/>); at 0, the Heart is conquered and razed.</summary>
    public int Health { get; private set; } = MaxHealth;

    public const int MaxHealth = 200;

    /// <summary>
    /// Cultural Borders: radius (m) of this Village Heart's territory ring
    /// — no longer a static 20m for every tribe alike, but scaled by this
    /// specific tribe's own banked wealth: a base <see cref="World.BaseTerritoryRadius"/>
    /// plus <see cref="World.TerritoryRadiusPerAmber"/> per <see cref="AmberStored"/>
    /// and <see cref="World.TerritoryRadiusPerNectar"/> per <see cref="NectarStored"/>.
    /// As a wealthy tribe's ring grows it can physically overlap into a
    /// poorer neighbour's, letting it claim resources closer to that rival's
    /// own base without ever counting as foreign territory (see
    /// <see cref="World.ForeignTerritoryContaining"/>) — Strict Border
    /// Control only ever excludes what falls inside the OTHER faction's own
    /// ring, so a bigger ring simply reaches further.
    /// </summary>
    public float TerritoryRadius =>
        World.BaseTerritoryRadius + AmberStored * World.TerritoryRadiusPerAmber + NectarStored * World.TerritoryRadiusPerNectar;

    /// <summary>Which tribe this Village Heart belongs to. The original heart is Faction 0.</summary>
    public int FactionID { get; }

    /// <summary>This faction's colour — tints its territory ring and, faintly, every one of its Bramblekin (see <see cref="Bramblekin.Draw"/>).</summary>
    public Color FactionColor { get; }

    /// <summary>Centre of the footprint on the ground.</summary>
    public Vector3 Center { get; }

    /// <summary>The Village Heart's bounding box, used for obstacle/collision checks.</summary>
    public BoundingBox Bounds { get; }

    /// <summary>
    /// The circle walkers treat as solid. Slightly bigger than the inscribed
    /// circle so corners are mostly covered without leaving wide gaps at the
    /// faces.
    /// </summary>
    public Obstacle Obstacle => new(new Vector2(Center.X, Center.Z), Width / 2f * 1.2f);

    /// <summary>A returning Bramblekin within this distance of the centre has arrived.</summary>
    public float DeliveryDistance => Obstacle.Radius + Bramblekin.BodyRadius + 0.2f;

    /// <summary>Food in this faction's stores, waiting to become the next sprout.</summary>
    public int FoodStored { get; internal set; }

    /// <summary>
    /// Tycoon Economy: this faction's banked wealth, delivered by Gatherers
    /// carrying home an <see cref="AmberNode"/> once <see cref="FoodStored"/>
    /// already covers survival (Maslow's Hierarchy — see
    /// <see cref="Bramblekin.UpdateGathering"/>). Spent on the Trading Post
    /// blueprint and Emergency Food Imports (see <see cref="World.UpdateAutoTradingPost"/>/
    /// <see cref="World.TryEmergencyFoodImport"/>) rather than anything the
    /// player spends directly.
    /// </summary>
    public int AmberStored { get; set; } = 0;

    /// <summary>
    /// The Nectar Brewery: this faction's permanent civilization buff
    /// currency, brewed from Food and Amber (see <see cref="World.UpdateNectarBrewery"/>).
    /// Never spent — every point banked here permanently raises every one
    /// of this faction's Gatherers' walk speed (see <see cref="Bramblekin.EffectiveWalkSpeed"/>)
    /// and, alongside <see cref="AmberStored"/>, this tribe's own Cultural
    /// Borders (see <see cref="TerritoryRadius"/>).
    /// </summary>
    public int NectarStored { get; set; } = 0;

    /// <summary>
    /// This faction's food storage cap. Starts at <see cref="World.BaseMaxFoodCapacity"/>
    /// and rises permanently by <see cref="World.GranaryFoodBonus"/> for each Granary it completes.
    /// </summary>
    public int MaxFoodCapacity { get; internal set; } = World.BaseMaxFoodCapacity;

    /// <summary>War Weariness: this faction's morale, drained by casualties and an actively hunting spider, recovered by calm.</summary>
    public float Morale { get; internal set; } = World.MaxMorale;

    /// <summary>Living Bramblekin of this faction — Militia and Gatherer alike. Recomputed every frame by the Job Manager.</summary>
    public int Population { get; internal set; }

    /// <summary>
    /// The Housing System: this faction's hard population ceiling —
    /// decoupled entirely from <see cref="MaxFoodCapacity"/>/Granaries.
    /// Starts at 10 and permanently rises by <see cref="World.TentPopulationBonus"/>
    /// for each completed Tent, capped at <see cref="World.MaxPopulationCap"/>
    /// (see <see cref="World.UpdateAutoTent"/>'s Hard Cap). Auto-Sprout (the
    /// Growth Phase, see <see cref="World.UpdateAutoSprout"/>) refuses to
    /// grow the tribe past this, full stop.
    /// </summary>
    public int MaxPopulation { get; internal set; } = 10;

    /// <summary>Auto-Conscription: how many of this faction's Bramblekin the Job Manager currently wants as Militia.</summary>
    public int MilitiaTarget { get; internal set; }

    /// <summary>Counts down to this faction's next Upkeep tax. Internal bookkeeping for <see cref="World"/>.</summary>
    internal float UpkeepTimer { get; set; }

    /// <summary>The Schism: true while this faction's Pioneers are already out founding a new Village Heart — guards against queuing a second Migration before the first lands.</summary>
    public bool HasActiveMigration { get; internal set; }

    /// <summary>
    /// Default Peace and Thievery: which other Factions this Village Heart is
    /// currently at war with, and how many seconds that Blood Feud has left
    /// — keyed by FactionID. Every faction starts and stays at peace with
    /// every other by default; an entry is only ever added by
    /// <see cref="World.DeclareBloodFeud"/> (a foreign Militia unit caught
    /// trespassing, or that faction landing a damaging hit on this one),
    /// and ticks down to removal in <see cref="World.Update"/>. While a
    /// FactionID is a key here, this faction's Militia treats it as a
    /// lethal attack-on-sight enemy within the territory ring; every other
    /// faction is simply ignored, Thievery aside (see
    /// <see cref="Bramblekin.TrespassingAgainst"/>).
    /// </summary>
    public Dictionary<int, float> HostileFactions { get; } = new();

    /// <summary>Below <see cref="World.WearyMoraleThreshold"/>: this faction's Gatherers walk at <see cref="World.WearySpeedMultiplier"/> speed.</summary>
    public bool GatherersAreWeary => Morale < World.WearyMoraleThreshold;

    /// <summary>Above <see cref="World.HighMoraleThreshold"/>: this faction's Builders work at <see cref="World.HighMoraleBuildMultiplier"/> speed.</summary>
    public bool BuildersAreInspired => Morale > World.HighMoraleThreshold;

    /// <summary>Faction Personalities: fixed for this Village Heart's entire life, randomly rolled the moment it's founded.</summary>
    public FactionTrait Trait { get; }

    /// <summary>
    /// Extinction: no one left (<see cref="Population"/> is 0) and not even
    /// enough Food Stored to Auto-Sprout a single replacement (<see cref="World.FoodSproutThreshold"/>)
    /// — this faction is done for good. An Extinct Village Heart stops
    /// functioning entirely (see the per-village loop in <see cref="World.Update"/>):
    /// no Upkeep, no Auto-Anything. It still stands, and can still be found
    /// and razed by Base Razing, until then.
    /// </summary>
    public bool IsExtinct => Population == 0 && FoodStored < World.FoodSproutThreshold;

    public VillageHeart(Vector3 center, int factionId, Color factionColor, Random rng)
    {
        Center = center;
        FactionID = factionId;
        FactionColor = factionColor;
        Trait = (FactionTrait)rng.Next(3);
        float half = Width / 2f;
        Bounds = new BoundingBox(
            new Vector3(center.X - half, Terrain.GroundHeight, center.Z - half),
            new Vector3(center.X + half, Terrain.GroundHeight + Height, center.Z + half));
    }

    /// <summary>How long (s) a landed hit tints the Heart red in <see cref="Draw"/> — debug feedback that Base Razing damage is actually executing.</summary>
    private const float DamageFlashDuration = 0.3f;

    /// <summary>Counts down from <see cref="DamageFlashDuration"/> after every hit; ticked in <see cref="World.Update"/>.</summary>
    public float DamageFlashTimer { get; internal set; }

    /// <summary>Base Razing: reduces Health, floored at 0, and starts the red damage flash. Purely a Health mutation otherwise — destruction/loot is World's call, via <see cref="World.DamageVillageHeart"/>.</summary>
    public void TakeDamage(int amount)
    {
        Health = Math.Max(0, Health - amount);
        DamageFlashTimer = DamageFlashDuration;
    }

    public void Draw()
    {
        DrawTerritoryRing();

        var middle = Center + new Vector3(0, Height / 2f, 0);
        Color cubeColor = DamageFlashTimer > 0f ? new Color(210, 40, 40, 255) : new Color(122, 78, 40, 255);
        Raylib.DrawCube(middle, Width, Height, Width, cubeColor);
        Raylib.DrawCubeWires(middle, Width, Height, Width, new Color(60, 35, 15, 255));

        // A small dark doorway on the camera-facing side so it reads as a home.
        var door = Center + new Vector3(Width / 2f + 0.01f, 0.3f, 0);
        Raylib.DrawCube(door, 0.02f, 0.6f, 0.45f, new Color(45, 25, 10, 255));
    }

    /// <summary>
    /// Territory: a faint ring of <see cref="FactionColor"/>, <see cref="TerritoryRadius"/>
    /// meters out, laid flat on the ground just above the grid so it doesn't
    /// z-fight with it. Purely a border — the claim itself, not a filled
    /// disc — so overlapping territories both stay readable.
    /// </summary>
    private void DrawTerritoryRing()
    {
        const int segments = 48;
        var ringColor = new Color(FactionColor.R, FactionColor.G, FactionColor.B, (byte)140);

        Vector3 Point(int i)
        {
            float angle = i * MathF.Tau / segments;
            return Center + new Vector3(MathF.Cos(angle) * TerritoryRadius, 0.02f, MathF.Sin(angle) * TerritoryRadius);
        }

        Vector3 previous = Point(0);
        for (int i = 1; i <= segments; i++)
        {
            Vector3 next = Point(i);
            Raylib.DrawLine3D(previous, next, ringColor);
            previous = next;
        }
    }
}

/// <summary>
/// The Schism: bookkeeping shared by the 4 Pioneers of one migration, so
/// the moment any one of them reaches <see cref="Target"/> and founds the
/// new Village Heart (<see cref="World.FoundVillage"/>), every Pioneer
/// bound to it — not just the one that arrived — drops its Migrating state
/// on its very next Update() (see <see cref="Bramblekin.UpdateMigrating"/>).
/// </summary>
public sealed class Migration
{
    public int NewFactionID { get; }
    public Color NewFactionColor { get; }
    public Vector3 Target { get; }

    /// <summary>The overcrowded Village Heart this Migration set out from — whose <see cref="VillageHeart.HasActiveMigration"/> clears once this Migration is founded or abandoned.</summary>
    public VillageHeart Origin { get; }

    /// <summary>The True Schism: how much Food Stored (half of Origin's own, at the moment it split) the new Village Heart is seeded with — see <see cref="World.FoundVillage"/>.</summary>
    public int FoodAmount { get; }

    /// <summary>True once a Pioneer has reached <see cref="Target"/> and founded the new Village Heart.</summary>
    public bool Founded { get; private set; }

    /// <summary>Pioneers still alive and travelling. If this reaches zero before the Migration is Founded, it's abandoned so the origin can try again.</summary>
    private int _pioneersRemaining;

    public Migration(int newFactionId, Color newFactionColor, Vector3 target, VillageHeart origin, int pioneerCount, int foodAmount)
    {
        NewFactionID = newFactionId;
        NewFactionColor = newFactionColor;
        Target = target;
        Origin = origin;
        FoodAmount = foodAmount;
        _pioneersRemaining = pioneerCount;
    }

    public void MarkFounded() => Founded = true;

    /// <summary>A Pioneer bound to this Migration died before reaching Target. Abandons the Migration (freeing the origin to try again) once none are left.</summary>
    public void PioneerLost()
    {
        _pioneersRemaining = Math.Max(0, _pioneersRemaining - 1);
        if (_pioneersRemaining == 0 && !Founded)
            Origin.HasActiveMigration = false;
    }
}

/// <summary>
/// The Acorn puzzle object: too hard for a single Bramblekin to open alone.
/// Up to <see cref="MaxClaimants"/> Chitin-Mallet Gatherers can claim and
/// crack one together — Cooperative Acorn Cracking (see <see cref="World.NearestClaimableAcorn"/>/
/// <see cref="World.Update"/>'s coop-crack check). Shatters into Food
/// Shards once enough progress has been made.
/// </summary>
public sealed class Acorn
{
    public const float Radius = 0.35f;

    /// <summary>Cooperative Acorn Cracking: at most this many Chitin-Mallet Gatherers may claim the same Acorn at once.</summary>
    public const int MaxClaimants = 3;

    /// <summary>
    /// Continuous Cracking: how far this Acorn's shatter has progressed.
    /// Every Chitin-Mallet Gatherer actually touching it while Cracking
    /// adds its own cracking speed to this every frame (see
    /// <see cref="Bramblekin.UpdateCracking"/>), so claimants' rates simply
    /// add together — more Gatherers means it breaks proportionally
    /// faster, rather than needing all <see cref="MaxClaimants"/> to show
    /// up before anything happens at all.
    /// </summary>
    public float CrackProgress { get; set; }

    /// <summary>CrackProgress needed to shatter this Acorn — see <see cref="CrackProgress"/>.</summary>
    public float CrackThreshold { get; } = 100f;

    public Vector3 Position { get; private set; }

    /// <summary>
    /// Object Pooling: false for a pool slot that isn't currently a real
    /// Acorn on the map — see <see cref="World.Acorns"/>. Every rendering
    /// and targeting loop over the pool must skip anything with this false.
    /// </summary>
    public bool IsActive { get; private set; }

    private readonly List<Bramblekin> _claimants = new();

    /// <summary>The Chitin-Mallet Gatherers currently claiming this Acorn.</summary>
    public IReadOnlyList<Bramblekin> Claimants => _claimants;

    /// <summary>Constructs an inactive pool slot — see <see cref="World.Acorns"/>. Call <see cref="Activate"/> to actually spawn one.</summary>
    public Acorn()
    {
    }

    /// <summary>Object Pooling: reuses this pool slot as a freshly spawned Acorn at <paramref name="groundPoint"/>, resetting every bit of its previous state.</summary>
    public void Activate(Vector3 groundPoint)
    {
        Position = groundPoint;
        CrackProgress = 0f;
        _claimants.Clear();
        IsActive = true;
    }

    /// <summary>Object Pooling: returns this slot to the pool — shattered by Cooperative Acorn Cracking. See <see cref="World.Acorns"/>.</summary>
    public void Deactivate()
    {
        IsActive = false;
        _claimants.Clear();
    }

    public bool IsClaimedBy(Bramblekin gatherer) => _claimants.Contains(gatherer);

    /// <summary>Claims a free slot for <paramref name="gatherer"/> (a no-op if it already holds one). Returns whether it now holds a claim.</summary>
    public bool TryClaim(Bramblekin gatherer)
    {
        if (_claimants.Contains(gatherer))
            return true;
        if (_claimants.Count >= MaxClaimants)
            return false;

        _claimants.Add(gatherer);
        return true;
    }

    public void ReleaseClaim(Bramblekin gatherer) => _claimants.Remove(gatherer);

    public void Draw()
    {
        var center = Position + new Vector3(0, Radius, 0);
        Raylib.DrawSphere(center, Radius, new Color(235, 195, 50, 255));
        Raylib.DrawSphereWires(center, Radius, 8, 8, new Color(120, 90, 20, 90));

        // Brown cap and stalk on top.
        var capBase = center + new Vector3(0, Radius * 0.55f, 0);
        Raylib.DrawCylinder(capBase, Radius * 0.75f, Radius * 0.95f, Radius * 0.35f, 12, new Color(115, 75, 35, 255));
        Raylib.DrawCylinder(capBase + new Vector3(0, Radius * 0.35f, 0), 0.03f, 0.03f, 0.12f, 6, new Color(90, 60, 30, 255));
    }
}

/// <summary>
/// Tycoon Economy: a rare, wealth-only resource — scarce on the map (see
/// <see cref="World.MaxAmberOnMap"/>/<see cref="World.AmberSpawnInterval"/>)
/// and pursued by a Gatherer only once its home Village Heart's
/// <see cref="VillageHeart.FoodStored"/> already covers survival — Maslow's
/// Hierarchy, see <see cref="Bramblekin.UpdateGathering"/>. Carried straight
/// home like a wild Berry, no cracking involved. Drawn as a golden gem
/// (two stacked cones) rather than Acorn's sphere-and-cap, so the two
/// never read as the same thing at a glance.
/// </summary>
public sealed class AmberNode
{
    public const float Radius = 0.22f;

    /// <summary>Resting spot on the ground (y = GroundHeight). Ignored while carried.</summary>
    public Vector3 Position { get; set; }

    /// <summary>True while a Bramblekin is holding it; carried Amber is hidden from the map, same as a carried Food Shard.</summary>
    public bool IsCarried { get; set; }

    /// <summary>
    /// Dibs: the one Gatherer currently pursuing this Amber, if any — same
    /// claim/timeout pattern as <see cref="FoodShard.ClaimedBy"/>/<see cref="FoodShard.ClaimTimer"/>,
    /// enforced by <see cref="World"/>.
    /// </summary>
    public Bramblekin? ClaimedBy { get; set; }

    /// <summary>Seconds since <see cref="ClaimedBy"/> was last set. Reset to 0 on every new claim; ticked and enforced by World.</summary>
    public float ClaimTimer { get; set; }

    /// <summary>Breaking the Death Loop: seconds an uncarried Amber sits on the map before it despawns — see <see cref="DespawnTimer"/>.</summary>
    public const float DespawnLifespan = 60f;

    /// <summary>
    /// Counts down from <see cref="DespawnLifespan"/>; once it reaches 0
    /// while this Amber isn't being carried, World removes it outright
    /// (see <see cref="World.UpdateLootDespawn"/>) so a pile of loot
    /// dropped by dead Bramblekin can't sit forever as bait that lures more
    /// Gatherers to their deaths in the same spot.
    /// </summary>
    public float DespawnTimer { get; set; } = DespawnLifespan;

    /// <summary>
    /// Object Pooling: false for a pool slot that isn't currently a real
    /// Amber node on the map — see <see cref="World.AmberNodes"/>. Every
    /// rendering and targeting loop over the pool must skip anything with
    /// this false.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>Constructs an inactive pool slot — see <see cref="World.AmberNodes"/>. Call <see cref="Activate"/> to actually spawn one.</summary>
    public AmberNode()
    {
    }

    /// <summary>Object Pooling: reuses this pool slot as a freshly spawned Amber node at <paramref name="groundPoint"/>, resetting every bit of its previous state.</summary>
    public void Activate(Vector3 groundPoint)
    {
        Position = groundPoint;
        IsCarried = false;
        ClaimedBy = null;
        ClaimTimer = 0f;
        DespawnTimer = DespawnLifespan;
        IsActive = true;
    }

    /// <summary>Object Pooling: returns this slot to the pool — delivered or despawned. See <see cref="World.AmberNodes"/>.</summary>
    public void Deactivate()
    {
        IsActive = false;
        IsCarried = false;
        ClaimedBy = null;
    }

    /// <summary>Draws the gem resting on the ground at (or carried above) <paramref name="groundPoint"/>.</summary>
    public void Draw(Vector3 groundPoint)
    {
        var gold = new Color(255, 203, 0, 255);
        var edge = new Color(150, 110, 0, 200);
        Vector3 mid = groundPoint + new Vector3(0, Radius, 0);
        Raylib.DrawCylinder(groundPoint, 0f, Radius, Radius, 4, gold);
        Raylib.DrawCylinder(mid, Radius, 0f, Radius, 4, gold);
        Raylib.DrawCylinderWires(groundPoint, 0f, Radius, Radius, 4, edge);
        Raylib.DrawCylinderWires(mid, Radius, 0f, Radius, 4, edge);
    }
}

/// <summary>Where a Food Shard came from — purely cosmetic, it's worth the same 1 food either way.</summary>
public enum FoodShardKind
{
    /// <summary>Cracked from an Acorn (Cooperative Acorn Cracking) or dropped by a hunted Aphid. Orange.</summary>
    Cracked,

    /// <summary>Passive Foraging: a wild Berry. Red.</summary>
    Berry,
}

/// <summary>A small piece of food — cracked acorn, Aphid meat, or a wild berry — that a Bramblekin can carry home.</summary>
public sealed class FoodShard
{
    public const float Radius = 0.18f;

    /// <summary>Resting spot on the ground (y = GroundHeight). Ignored while carried.</summary>
    public Vector3 Position { get; set; }

    /// <summary>True while a Bramblekin is holding it; carried shards are hidden from the map.</summary>
    public bool IsCarried { get; set; }

    /// <summary>Where it came from. Only affects colour; it's worth the same 1 food regardless.</summary>
    public FoodShardKind Kind { get; private set; }

    /// <summary>
    /// Object Pooling: false for a pool slot that isn't currently a real
    /// Food Shard on the map. World pre-allocates a fixed pool of these at
    /// startup (see <see cref="World.FoodShards"/>) instead of constructing
    /// and destroying one per spawn/pickup/despawn; every rendering and
    /// targeting loop over the pool must skip anything with this false.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Dibs: the one Bramblekin currently pursuing this shard, if any — see
    /// <see cref="World.IsAvailable(FoodShard, Bramblekin)"/>. Only
    /// meaningful while that Bramblekin's own State is actually Gathering;
    /// it's released (see Bramblekin.SetState) the moment that stops being
    /// true — and, as a failsafe against a claimant that's stuck, jittering,
    /// or otherwise never actually closes the distance, it's also force-
    /// released after <see cref="World.FoodClaimTimeoutSeconds"/> of game
    /// time (see <see cref="ClaimTimer"/> and <see cref="World.Update"/>'s
    /// timeout sweep) so nobody else is ever locked out forever.
    /// </summary>
    public Bramblekin? ClaimedBy { get; set; }

    /// <summary>Seconds since <see cref="ClaimedBy"/> was last set. Reset to 0 on every new claim; ticked and enforced by World.</summary>
    public float ClaimTimer { get; set; }

    /// <summary>Breaking the Death Loop: seconds an uncarried Food Shard sits on the map before it despawns — see <see cref="DespawnTimer"/>.</summary>
    public const float DespawnLifespan = 60f;

    /// <summary>
    /// Counts down from <see cref="DespawnLifespan"/>; once it reaches 0
    /// while this shard isn't being carried, World removes it outright
    /// (see <see cref="World.UpdateLootDespawn"/>) so a pile of loot
    /// dropped by dead Bramblekin (or an old Aphid-hunt/Acorn-crack scatter)
    /// can't sit forever as bait that lures more Gatherers to their deaths
    /// in the same spot.
    /// </summary>
    public float DespawnTimer { get; set; } = DespawnLifespan;

    /// <summary>Constructs an inactive pool slot — see <see cref="World.FoodShards"/>. Call <see cref="Activate"/> to actually spawn one.</summary>
    public FoodShard()
    {
    }

    /// <summary>Object Pooling: reuses this pool slot as a freshly spawned Food Shard at <paramref name="groundPoint"/>, resetting every bit of its previous state.</summary>
    public void Activate(Vector3 groundPoint, FoodShardKind kind = FoodShardKind.Cracked)
    {
        Position = groundPoint;
        Kind = kind;
        IsCarried = false;
        ClaimedBy = null;
        ClaimTimer = 0f;
        DespawnTimer = DespawnLifespan;
        IsActive = true;
    }

    /// <summary>Object Pooling: returns this slot to the pool — picked up (delivered/consumed) or despawned. See <see cref="World.FoodShards"/>.</summary>
    public void Deactivate()
    {
        IsActive = false;
        IsCarried = false;
        ClaimedBy = null;
    }

    /// <summary>Draws the shard resting on the ground at (or carried above) <paramref name="groundPoint"/>.</summary>
    public void Draw(Vector3 groundPoint)
    {
        Color color = Kind == FoodShardKind.Berry ? new Color(210, 40, 45, 255) : new Color(245, 150, 45, 255);
        Raylib.DrawSphere(groundPoint + new Vector3(0, Radius, 0), Radius, color);
    }
}

/// <summary>
/// Individual Equipment's raw material: dropped where the Wolf Spider dies
/// (see <see cref="World.DespawnSpider"/>), alongside a <see cref="Chitin"/>.
/// An un-upgraded Militia unit that touches one instantly equips a Fang Pike
/// (<see cref="Bramblekin.HasFangPike"/>) and it despawns — see
/// <see cref="World.ConsumeFang"/>.
/// </summary>
public sealed class SpiderFang
{
    public const float Radius = 0.16f;

    /// <summary>Resting spot on the ground (y = GroundHeight).</summary>
    public Vector3 Position { get; }

    public SpiderFang(Vector3 groundPoint) => Position = groundPoint;

    public void Draw()
    {
        var fill = new Color(235, 235, 240, 255);
        var edge = new Color(150, 150, 160, 255);

        var tip = Position + new Vector3(0, Radius * 2f, 0);
        var baseLeft = Position + new Vector3(-Radius * 0.5f, 0, 0);
        var baseRight = Position + new Vector3(Radius * 0.5f, 0, 0);

        // Both winding orders, so it reads as a solid fang from any angle.
        Raylib.DrawTriangle3D(baseLeft, tip, baseRight, fill);
        Raylib.DrawTriangle3D(baseRight, tip, baseLeft, fill);
        Raylib.DrawLine3D(baseLeft, tip, edge);
        Raylib.DrawLine3D(tip, baseRight, edge);
        Raylib.DrawLine3D(baseRight, baseLeft, edge);
    }
}

/// <summary>
/// Individual Equipment's other raw material: dropped alongside a
/// <see cref="SpiderFang"/> where the Wolf Spider dies. An un-upgraded
/// Gatherer that touches one instantly equips a Chitin Mallet (<see cref="Bramblekin.HasChitinMallet"/>)
/// and it despawns — see <see cref="World.ConsumeChitin"/>.
/// </summary>
public sealed class Chitin
{
    public const float Radius = 0.15f;

    /// <summary>Resting spot on the ground (y = GroundHeight).</summary>
    public Vector3 Position { get; }

    public Chitin(Vector3 groundPoint) => Position = groundPoint;

    /// <summary>A small grey chitin plate lying on the ground.</summary>
    public void Draw()
    {
        var center = Position + new Vector3(0, Radius * 0.6f, 0);
        Raylib.DrawCube(center, Radius * 1.6f, Radius * 0.7f, Radius * 1.3f, new Color(150, 150, 150, 255));
        Raylib.DrawCubeWires(center, Radius * 1.6f, Radius * 0.7f, Radius * 1.3f, new Color(90, 90, 95, 255));
    }
}

// =============================================================================
//  Village Building
// =============================================================================

/// <summary>Which kind of Village Building a <see cref="Blueprint"/>/<see cref="Building"/> is.</summary>
public enum BuildingKind
{
    /// <summary>Permanently raises <see cref="VillageHeart.MaxFoodCapacity"/> by <see cref="World.GranaryFoodBonus"/>.</summary>
    Granary,

    /// <summary>Passive Income: spawns a Berry on top of itself every <see cref="Building.SporeFarmInterval"/> seconds.</summary>
    SporeFarm,

    /// <summary>Tycoon Economy: once built, unlocks the Emergency Food Import (see <see cref="World.TryEmergencyFoodImport"/>).</summary>
    TradingPost,

    /// <summary>The Housing System: permanently raises <see cref="VillageHeart.MaxPopulation"/> by <see cref="World.TentPopulationBonus"/>, hard-capped at <see cref="World.MaxPopulationCap"/>.</summary>
    Tent,

    /// <summary>The Nectar Brewery: once built, consumes Food and Amber on a timer to brew Nectar — a permanent civilization buff (see <see cref="World.UpdateNectarBrewery"/>).</summary>
    Brewery,

    /// <summary>The Great Monument: a tribe's endgame civilization goal — a massive, multi-stage structure that takes the whole tribe's coordinated effort (200 Construction Progress) and, once finished, marks that faction's transition into an advanced civilization with a permanent screen-wide alert (see <see cref="World.CompleteBlueprint"/>).</summary>
    Monument,
}

/// <summary>
/// A finished piece of Village Building: either a Granary (permanently
/// raises the food cap) or a Spore Farm (a flat mushroom bed that spawns
/// Berries on a timer — see <see cref="TickSporeTimer"/>).
/// </summary>
public sealed class Building
{
    public const float GranaryRadius = 0.7f;
    public const float GranaryHeight = 1.1f;

    /// <summary>Large enough, and drawn in a saturated Dark Green well off the grass-green ground plane's hue (see <see cref="Draw"/>), to read as an obviously distinct landmark rather than blending into the terrain.</summary>
    public const float SporeFarmRadius = 1.6f;
    private const float SporeFarmHeight = 0.12f;

    /// <summary>Economic Buff: seconds between each Berry a finished Spore Farm spawns on top of itself — fast enough that a large tribe's Gatherers have a safe, internal food loop and never need to cross the map for every Berry, and fast enough to actually free up bandwidth for Amber/Trading Post play.</summary>
    public const float SporeFarmInterval = 5f;

    /// <summary>Tycoon Economy: the Trading Post's footprint — a square structure, distinct from the two round buildings.</summary>
    public const float TradingPostRadius = 0.9f;
    private const float TradingPostHeight = 1.3f;

    /// <summary>The Housing System: a Tent's small footprint — the smallest building on the map, so a tribe can pack in several without crowding out its other structures.</summary>
    public const float TentRadius = 0.5f;
    public const float TentHeight = 0.55f;

    /// <summary>The Nectar Brewery's footprint — a round structure, slightly larger than a Granary since it houses a whole secondary economy.</summary>
    public const float BreweryRadius = 0.8f;
    private const float BreweryHeight = 1.4f;

    /// <summary>
    /// The Great Monument's footprint — by far the largest structure on the
    /// map, befitting the whole tribe's coordinated, endgame effort.
    /// </summary>
    public const float MonumentRadius = 2.2f;
    private const float MonumentHeight = 3.2f;

    public BuildingKind Kind { get; }
    public Vector3 Position { get; }

    /// <summary>Which faction built this — carried over from the <see cref="Blueprint"/> it was completed from.</summary>
    public int FactionID { get; }

    /// <summary>The owning faction's colour.</summary>
    public Color FactionColor { get; }

    /// <summary>Counts down to the next Berry. Only meaningful for a Spore Farm.</summary>
    private float _sporeTimer = SporeFarmInterval;

    /// <summary>The Nectar Brewery's brewing clock: counts down to the next brew attempt. Only meaningful for a Brewery.</summary>
    private float _breweryTimer = World.BreweryInterval;

    public Building(Vector3 position, BuildingKind kind, int factionId, Color factionColor)
    {
        Position = position;
        Kind = kind;
        FactionID = factionId;
        FactionColor = factionColor;
    }

    /// <summary>The footprint radius (m) a Blueprint/Building of this kind actually occupies on the ground.</summary>
    public static float RadiusFor(BuildingKind kind) => kind switch
    {
        BuildingKind.Granary => GranaryRadius,
        BuildingKind.TradingPost => TradingPostRadius,
        BuildingKind.Tent => TentRadius,
        BuildingKind.Brewery => BreweryRadius,
        BuildingKind.Monument => MonumentRadius,
        _ => SporeFarmRadius,
    };

    /// <summary>
    /// A Spore Farm's passive-income clock: counts down by
    /// <paramref name="deltaTime"/> and, once it reaches zero, resets and
    /// returns true so <see cref="World"/> can spawn a Berry on top of it.
    /// Always false for a Granary.
    /// </summary>
    public bool TickSporeTimer(float deltaTime)
    {
        if (Kind != BuildingKind.SporeFarm)
            return false;

        _sporeTimer -= deltaTime;
        if (_sporeTimer > 0f)
            return false;

        _sporeTimer += SporeFarmInterval;
        return true;
    }

    /// <summary>
    /// The Nectar Brewery's brewing clock: counts down by
    /// <paramref name="deltaTime"/> and, once it reaches zero, resets and
    /// returns true so <see cref="World.UpdateNectarBrewery"/> can attempt
    /// the actual Food/Amber-for-Nectar brew. Always false for anything
    /// else.
    /// </summary>
    public bool TickBreweryTimer(float deltaTime)
    {
        if (Kind != BuildingKind.Brewery)
            return false;

        _breweryTimer -= deltaTime;
        if (_breweryTimer > 0f)
            return false;

        _breweryTimer += World.BreweryInterval;
        return true;
    }

    public void Draw()
    {
        if (Kind == BuildingKind.Granary)
        {
            var center = Position + new Vector3(0, GranaryHeight / 2f, 0);
            Raylib.DrawCylinder(center, GranaryRadius, GranaryRadius, GranaryHeight, 16, new Color(180, 140, 70, 255));
            Raylib.DrawCylinderWires(center, GranaryRadius, GranaryRadius, GranaryHeight, 16, new Color(90, 65, 30, 255));
            return;
        }

        if (Kind == BuildingKind.TradingPost)
        {
            // A brown square structure with a Color.GOLD center — the
            // Tycoon Economy's landmark, deliberately square so it reads
            // apart from the two round buildings at a glance.
            var goldCube = new Color(255, 203, 0, 255);
            var postCenter = Position + new Vector3(0, TradingPostHeight / 2f, 0);
            Raylib.DrawCube(postCenter, TradingPostRadius * 2f, TradingPostHeight, TradingPostRadius * 2f, new Color(120, 80, 45, 255));
            Raylib.DrawCubeWires(postCenter, TradingPostRadius * 2f, TradingPostHeight, TradingPostRadius * 2f, new Color(65, 40, 20, 255));
            Raylib.DrawCube(postCenter, TradingPostRadius * 0.9f, TradingPostHeight * 0.9f, TradingPostRadius * 0.9f, goldCube);
            return;
        }

        if (Kind == BuildingKind.Tent)
        {
            // A small white/grey dome — a cone (full radius tapering to a
            // point) reads as a simple canvas tent, small and plain enough
            // not to compete visually with the three "real" buildings.
            var canvas = new Color(235, 235, 230, 255);
            var canvasEdge = new Color(150, 150, 145, 220);
            var tentCenter = Position + new Vector3(0, TentHeight / 2f, 0);
            Raylib.DrawCylinder(tentCenter, TentRadius, 0f, TentHeight, 16, canvas);
            Raylib.DrawCylinderWires(tentCenter, TentRadius, 0f, TentHeight, 16, canvasEdge);
            return;
        }

        if (Kind == BuildingKind.Brewery)
        {
            // A rounded purple/pink vat — Nectar's own colour on the
            // Faction Ledger — with a golden spout on top so it reads as a
            // still/brewery rather than another plain Granary silo.
            var vat = new Color(150, 60, 150, 255);
            var vatEdge = new Color(80, 25, 85, 255);
            var vatCenter = Position + new Vector3(0, BreweryHeight / 2f, 0);
            Raylib.DrawCylinder(vatCenter, BreweryRadius, BreweryRadius * 0.8f, BreweryHeight, 16, vat);
            Raylib.DrawCylinderWires(vatCenter, BreweryRadius, BreweryRadius * 0.8f, BreweryHeight, 16, vatEdge);
            var spout = Position + new Vector3(0, BreweryHeight + 0.08f, 0);
            Raylib.DrawCylinder(spout, BreweryRadius * 0.35f, BreweryRadius * 0.2f, 0.18f, 10, new Color(255, 203, 0, 255));
            return;
        }

        if (Kind == BuildingKind.Monument)
        {
            // The Great Monument: a stepped pyramid — three stacked, shrinking
            // tiers topped with a golden capstone — massive enough (by far
            // the tallest/widest structure on the map) to read as the
            // tribe's endgame civilization goal at a glance.
            var stone = new Color(190, 180, 165, 255);
            var stoneEdge = new Color(95, 88, 78, 255);
            const int tiers = 3;
            float tierHeight = MonumentHeight / tiers;
            for (int tier = 0; tier < tiers; tier++)
            {
                // Each tier's own base picks up exactly where the one below
                // it tapered to, so the whole stack reads as one continuous
                // stepped pyramid rather than three disconnected cylinders.
                float baseRadius = MonumentRadius * (1f - tier * 0.3f);
                float topRadius = MonumentRadius * (1f - (tier + 1) * 0.3f);
                var tierCenter = Position + new Vector3(0, tierHeight * tier + tierHeight / 2f, 0);
                Raylib.DrawCylinder(tierCenter, baseRadius, topRadius, tierHeight, 4, stone);
                Raylib.DrawCylinderWires(tierCenter, baseRadius, topRadius, tierHeight, 4, stoneEdge);
            }

            var capstone = Position + new Vector3(0, MonumentHeight + 0.3f, 0);
            Raylib.DrawCylinder(capstone, MonumentRadius * 0.15f, 0f, 0.6f, 4, new Color(255, 203, 0, 255));
            return;
        }

        // Spore Farm: a large, saturated Dark Green disc, deliberately far
        // enough from the grass-green ground plane's own hue (86, 150, 60)
        // that it reads as an obvious landmark at a glance rather than
        // blending in.
        var patchCenter = Position + new Vector3(0, SporeFarmHeight / 2f, 0);
        Raylib.DrawCylinder(patchCenter, SporeFarmRadius, SporeFarmRadius, SporeFarmHeight, 24, new Color(20, 95, 35, 255));
        Raylib.DrawCylinderWires(patchCenter, SporeFarmRadius, SporeFarmRadius, SporeFarmHeight, 24, new Color(10, 45, 15, 255));
    }
}

/// <summary>
/// A Building site under construction: placed for Food Stored via
/// <see cref="World.TryPlaceBlueprint"/>, then worked on by the faction's
/// dedicated Builder (the Builder AI, <see cref="Bramblekin"/>'s Building
/// state) until its
/// Construction Progress reaches <see cref="ProgressRequired"/>, at which
/// point <see cref="World.CompleteBlueprint"/> turns it into a <see cref="Building"/>
/// of the same <see cref="Kind"/>.
/// </summary>
public sealed class Blueprint
{
    public BuildingKind Kind { get; }

    /// <summary>Construction Progress needed to finish — 10 for a Granary, 15 for a Spore Farm, 20 for a Trading Post, 8 for a Tent (cheap and fast — Housing needs to keep pace with a growing tribe), 25 for a Brewery, and a massive 200 for the Great Monument — a whole tribe's coordinated effort.</summary>
    public float ProgressRequired => Kind switch
    {
        BuildingKind.Granary => 10f,
        BuildingKind.TradingPost => 20f,
        BuildingKind.Tent => 8f,
        BuildingKind.Brewery => 25f,
        BuildingKind.Monument => 200f,
        _ => 15f,
    };

    public Vector3 Position { get; }
    public float Progress { get; private set; }

    public bool IsComplete => Progress >= ProgressRequired;

    /// <summary>Which faction placed this site — AI Faction Loyalty: only that faction's Builders will work it.</summary>
    public int FactionID { get; }

    /// <summary>The owning faction's colour.</summary>
    public Color FactionColor { get; }

    public Blueprint(Vector3 position, BuildingKind kind, int factionId, Color factionColor)
    {
        Position = position;
        Kind = kind;
        FactionID = factionId;
        FactionColor = factionColor;
    }

    public void AddProgress(float amount) => Progress = MathF.Min(Progress + amount, ProgressRequired);

    /// <summary>A translucent wireframe at full size, filled in from the ground up as Construction Progress advances.</summary>
    public void Draw()
    {
        float t = MathF.Max(Progress / ProgressRequired, 0.05f);
        var fill = new Color(255, 255, 255, 90);
        var wire = new Color(210, 200, 70, 200);

        float radius = Building.RadiusFor(Kind);
        // A Spore Farm is nearly flat when finished, but a full-height wireframe (like a Granary's)
        // still reads clearly as "a site under construction" while it fills in.
        float fullHeight = Kind switch
        {
            BuildingKind.Granary => Building.GranaryHeight,
            BuildingKind.Tent => Building.TentHeight,
            _ => 0.3f,
        };

        var wireCenter = Position + new Vector3(0, fullHeight / 2f, 0);
        Raylib.DrawCylinderWires(wireCenter, radius, radius, fullHeight, 16, wire);
        float height = fullHeight * t;
        Raylib.DrawCylinder(Position + new Vector3(0, height / 2f, 0), radius, radius, height, 16, fill);
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
            // Never avoid the thing we're walking to (the village when delivering).
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

/// <summary>What a Bramblekin is currently doing.</summary>
public enum BramblekinState
{
    /// <summary>Wandering: walking at a slow, steady pace toward a random point.</summary>
    Walking,

    /// <summary>Wandering: standing still for a moment after arriving somewhere.</summary>
    Pausing,

    /// <summary>Heading for the nearest available Food Shard.</summary>
    Gathering,

    /// <summary>Carrying a Food Shard back to the Village Heart.</summary>
    Returning,

    /// <summary>
    /// Continuous Cracking: a Chitin-Mallet Gatherer pathing to (and, once
    /// touching, steadily adding its cracking speed to) a claimed Acorn —
    /// see <see cref="Bramblekin.UpdateCracking"/>. Up to <see cref="Acorn.MaxClaimants"/>
    /// Gatherers can be in this state on the same Acorn at once, their
    /// rates simply adding together.
    /// </summary>
    Cracking,

    /// <summary>Running at 3x speed from a predator, or from a Warning Shove.</summary>
    Fleeing,

    /// <summary>
    /// Militia only: charging the Wolf Spider to intercept it before it
    /// reaches the village, chasing down a rival faction it's in a
    /// declared Blood Feud with, or confronting (Warning Shove first) a
    /// specific foreign Gatherer caught trespassing — see
    /// <see cref="Bramblekin.UpdateDefending"/>.
    /// </summary>
    Defending,

    /// <summary>Militia only: chasing down the nearest Aphid within the 20-Meter Territory Rule.</summary>
    Hunting,

    /// <summary>
    /// Blood Feud Base Razing: Militia only, opportunistic offense rather
    /// than home defense — a unit that wanders within its own 20m aggro
    /// radius of a Village Heart it's in a declared Blood Feud with, with
    /// no living hostile Bramblekin also in range, paths to it and Pokes it
    /// down. See <see cref="Bramblekin.UpdateRaiding"/>.
    /// </summary>
    Raiding,

    /// <summary>Builder only: the Builder AI, pathing to and working a Blueprint.</summary>
    Building,

    /// <summary>
    /// Individual Equipment: an un-upgraded Militia unit fetching a Spider
    /// Fang, or an un-upgraded Gatherer fetching a Chitin piece — see
    /// <see cref="Bramblekin.UpdateEquipping"/>.
    /// </summary>
    Equipping,

    /// <summary>
    /// The Schism: a Pioneer, forced into this state the instant it's
    /// chosen (see <see cref="Bramblekin.BecomePioneer"/>) and overriding
    /// every other priority — it ignores food, blueprints and enemies alike
    /// and paths straight for its Migration's Target until it (or another
    /// Pioneer bound to the same Migration) founds the new Village Heart.
    /// See <see cref="Bramblekin.UpdateMigrating"/>.
    /// </summary>
    Migrating,
}

/// <summary>A Bramblekin's class: an ordinary worker, a dedicated builder, or a drafted defender.</summary>
public enum BramblekinRole
{
    /// <summary>Gathers food; flees the Wolf Spider like everyone else.</summary>
    Gatherer,

    /// <summary>Set by Conscription (the Job Manager). Never gathers; instead defends the colony and hunts Aphids.</summary>
    Militia,

    /// <summary>
    /// Set by Conscription (the Job Manager) whenever the faction has an
    /// incomplete Blueprint — a single Gatherer pulled off food duty to work
    /// it, so the rest of the colony's Gatherers never have to abandon
    /// gathering to pick up a trowel. Flees the Wolf Spider like a Gatherer,
    /// but never gathers food itself.
    /// </summary>
    Builder,
}

/// <summary>
/// One of the tiny creatures the player protects. The player never controls
/// them directly; each runs a small state machine, checked in priority order
/// every frame:
///
///   1. Cultural Borders: a Militia unit only Defends against the
///      Wolf Spider, or Hunts an Aphid, while that hostile is within its
///      own Village Heart's dynamic, wealth-scaled <see cref="VillageHeart.TerritoryRadius"/>
///      — see <see cref="World.SpawnSpiderNearVillage"/>'s organic
///      roaming. A Gatherer's own Fear Aura response is unaffected by
///      territory: it flees the spider on sight within <see cref="FearRadius"/>
///      regardless of where either of them is standing. Default Peace &amp;
///      Thievery: at this same priority tier, a Militia unit also Defends
///      against any rival faction it's in a declared Blood Feud with (see
///      <see cref="VillageHeart.HostileFactions"/>) and confronts (Warning
///      Shove first) any specific foreign Gatherer it's caught stealing
///      food from its own territory (see <see cref="TrespassingAgainst"/>)
///      — every other faction is otherwise completely ignored.
///   2. Village Building (Builder only — a single Gatherer the Job Manager
///      pulls onto Blueprint duty, see <see cref="World.HasIncompleteBlueprintFor"/>),
///      then Individual Equipment (an un-upgraded Militia fetching a Fang,
///      or Gatherer fetching Chitin — see <see cref="HasFangPike"/>/
///      <see cref="HasChitinMallet"/>), then Economy (Gathering food,
///      prioritizing the 20 m Territory Rule before the wider map) or
///      Hunting (Militia) override wandering in that priority order.
///   3. Wandering: Walking to a random free point, Pausing 2 s, repeat.
///      Shared by all three roles as the default idle behaviour.
///
/// Gathering and Returning Bramblekin shake the ground; that is what the Wolf
/// Spider hunts by (<see cref="IsVibrating"/>). Militia never gather, so they
/// never draw that attention — the spider only ever engages one through the
/// Bite/Poke exchange once it's within range.
///
/// Dibs: a Bramblekin claims its current Food Shard/Aphid/Acorn target (see
/// <see cref="FoodShard.ClaimedBy"/>/<see cref="Aphid.ClaimedBy"/>/<see cref="Acorn.Claimants"/>)
/// so others don't swarm the same one; the claim is released automatically
/// the moment it stops actively pursuing it (see <see cref="SetState"/>) or
/// dies (see <see cref="MarkDead"/>). The Wolf Spider is exempt — any number
/// of Militia can pile onto it at once.
/// </summary>
public sealed class Bramblekin
{
    private static int _nextId = 0;

    /// <summary>AI Time-Slicing: a stable, evenly-distributed per-unit index used to stagger which frame each Bramblekin runs its expensive Brain (target-scanning) logic on — see <see cref="World.FrameCounter"/>.</summary>
    public int ID { get; } = _nextId++;

    /// <summary>Normal walking speed in m/s. Buffed 50% over the original slow amble so Bramblekin can cross the larger 100x100 m map before Upkeep starves them.</summary>
    public const float WalkSpeed = 1.5f;

    /// <summary>Flee speed as a multiple of <see cref="WalkSpeed"/>.</summary>
    public const float FleeSpeedMultiplier = 3f;

    /// <summary>How long a Bramblekin rests after reaching a target, in seconds.</summary>
    public const float PauseDuration = 2f;

    /// <summary>Collision radius in meters: used against the village and other obstacles.</summary>
    public const float BodyRadius = 0.25f;

    /// <summary>Total body height in meters, including the rounded ends.</summary>
    public const float BodyHeight = 0.9f;

    /// <summary>How far from the terrain edge targets are kept, in meters.</summary>
    public const float EdgeMargin = 0.5f;

    /// <summary>
    /// Fear Aura: a Wolf Spider closer than this (m) sends the Bramblekin
    /// running. Deliberately a little shorter than the spider's pounce range,
    /// so a hunting spider gets the jump on distracted workers.
    /// </summary>
    public const float FearRadius = 2.0f;

    /// <summary>How far past the Fear Aura a frightened Bramblekin aims to run, in meters.</summary>
    private const float PredatorFleeMargin = 3f;

    /// <summary>
    /// Within this distance of a shard, it is picked up. Forgiving on
    /// purpose (well beyond BodyRadius + FoodShard.Radius' exact-touch
    /// distance): at a high Debug Time Scale a claimant can be a whole
    /// tick's movement away from dead-center and still needs to register as
    /// "close enough", or it jitters past the target forever without ever
    /// satisfying a tighter check.
    /// </summary>
    private const float PickupDistance = 0.8f;

    /// <summary>Continuous Cracking: how much this Gatherer alone adds to a touched Acorn's CrackProgress per second — see <see cref="UpdateCracking"/>.</summary>
    private const float CrackRatePerGatherer = 20f;

    /// <summary>Within this distance of a Spider Fang, it is picked up — see <see cref="PickupDistance"/>'s High-Speed Physics note.</summary>
    private const float FangPickupDistance = 0.8f;

    /// <summary>Within this distance of a Chitin piece, it is picked up — see <see cref="PickupDistance"/>'s High-Speed Physics note.</summary>
    private const float ChitinPickupDistance = 0.8f;

    /// <summary>The Schism: within this distance of its Migration Target, a Pioneer has arrived.</summary>
    private const float MigrationArriveDistance = BodyRadius + 0.2f;

    /// <summary>Militia charge speed while Defending — faster than a gathering amble, short of a full panicked flee.</summary>
    private const float DefendSpeed = WalkSpeed * 1.5f;

    /// <summary>How far from the spider, back toward the Village Heart, a defending Militia tries to stand.</summary>
    private const float InterceptStandoff = 1.2f;

    /// <summary>Speed while chasing an Aphid.</summary>
    private const float HuntSpeed = WalkSpeed;

    /// <summary>Within this distance of an Aphid, it is caught.</summary>
    private const float HuntContactDistance = BodyRadius + Aphid.BodyRadius + 0.05f;

    /// <summary>
    /// Extra reach (m) beyond a Blueprint's own footprint radius and the
    /// Builder's body radius — the two must actually be able to
    /// intersect/touch, not just get within some flat distance of its
    /// centre regardless of how big the site itself is. Combined with even
    /// the smallest Blueprint's own footprint, the total contact distance
    /// already comes out well past the High-Speed Physics floor (see
    /// <see cref="PickupDistance"/>'s note), so it needs no bump of its own.
    /// </summary>
    private const float BuildContactMargin = 0.3f;

    /// <summary>Sustained Combat: within this distance of the Wolf Spider, a Defending Militia unit pokes it instead of just closing in.</summary>
    private const float PokeRange = 1.5f;

    /// <summary>
    /// Base Razing: within this distance of a Village Heart's centre, a
    /// Raiding Militia unit attacks it instead of just closing in. Much
    /// more forgiving than <see cref="PokeRange"/>: the Heart's own solid
    /// Obstacle (radius ~0.96m) plus a walker's BodyRadius already stops
    /// units short of the exact centre coordinate, and crowding several
    /// raiders around the same small footprint leaves some of them jostled
    /// out past a tight range — a real softlock that used to leave raiders
    /// permanently "crowding around" the Heart without ever landing a hit.
    /// </summary>
    private const float BuildingAttackRange = 3.5f;

    /// <summary>
    /// The Militia Leash: how far (m) a Militia unit may stray from its own
    /// Village Heart while Chasing (Defending) or Attacking (Raiding)
    /// before it breaks off entirely and heads straight home instead,
    /// letting the target escape. Deliberately a hair past the old fixed
    /// 20m territory rule — a moving target right at that boundary, or an
    /// obstacle detour, can easily drag a chasing unit slightly past it
    /// without this being a runaway pursuit. Cultural Borders: a very
    /// wealthy Village Heart's own dynamic <see cref="VillageHeart.TerritoryRadius"/>
    /// can now grow past this fixed leash — its Militia still won't chase
    /// any further than this, full stop, while still keeping Militia from ever wandering off to
    /// fight across the whole map.
    /// </summary>
    private const float MilitiaLeashDistance = 22f;

    /// <summary>Cooldown (s) between pokes — rapid, so Militia can wail on a spider (especially a Tumbled one) quickly.</summary>
    private const float PokeCooldownDuration = 1.0f;

    /// <summary>
    /// Damage a Militia poke deals to the Wolf Spider — boosted once this
    /// specific unit's own <see cref="HasFangPike"/> is true. Individual
    /// Equipment: unlike the old village-wide unlock, this is per-unit.
    /// </summary>
    private const int PokeDamage = 15;
    private const int UpgradedPokeDamage = 30;

    private static readonly Color CalmColor = new(196, 160, 110, 255);   // Bark brown.
    private static readonly Color PanicColor = new(225, 85, 60, 255);    // Alarm red.
    private static readonly Color MilitiaColor = new(150, 130, 95, 255); // A shade duller than a Gatherer — worn, armed.
    private static readonly Color BuilderColor = new(170, 140, 200, 255); // Lavender — visually distinct, on-the-job.
    private static readonly Color PikeColor = new(120, 55, 40, 255);     // Rose-thorn brown-red.
    private static readonly Color FangPikeColor = new(235, 235, 240, 255); // Spider Fang: bright white/silver.
    private static readonly Color MalletHandleColor = new(120, 80, 45, 255); // Wooden handle.
    private static readonly Color MalletHeadColor = new(150, 150, 150, 255); // Grey chitin head.

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private Vector3 _target;
    private float _pauseTimer;
    private float _pokeCooldown;
    private FoodShard? _carried;
    private AmberNode? _carriedAmber;

    /// <summary>
    /// Thievery: the foreign Village Heart this Gatherer is currently
    /// trespassing against, set the instant it picks up a Food Shard
    /// sitting inside that faction's own 20m territory ring (see
    /// <see cref="World.ForeignTerritoryContaining"/>), null otherwise.
    /// Only ever meaningful while <see cref="_carried"/> is the shard it
    /// stole — cleared the moment that's no longer true (see
    /// <see cref="DropCarried"/> and <see cref="UpdateReturning"/>), so the
    /// flag never outlives the theft itself. That specific Village Heart's
    /// Militia checks this to single the thief out — see
    /// <see cref="World.NearestTrespasserInTerritory"/>.
    /// </summary>
    public VillageHeart? TrespassingAgainst { get; private set; }

    // Dibs: the current claim on a Food Shard/Aphid/Acorn this Bramblekin is
    // actively pursuing, so re-picking a different (or no) target releases
    // the old one rather than leaving it permanently locked out for others.
    // See SetState and MarkDead, which release all three on any transition
    // away from the state that owns them.
    private FoodShard? _claimedShard;
    private Aphid? _claimedAphid;
    private Acorn? _claimedAcorn;
    private AmberNode? _claimedAmber;

    /// <summary>
    /// The Bramblekin this Militia unit is currently chasing down while
    /// Defending, when there's no Wolf Spider in territory to prioritize
    /// instead — either a declared enemy (Blood Feud Border Wars) or a
    /// specific caught Trespasser (Thievery Response); UpdateDefending
    /// decides which by checking whether its faction is a key in the home
    /// Village Heart's HostileFactions. Re-picked every frame, same spirit
    /// as <see cref="_claimedAphid"/> — not a Dibs claim (multiple
    /// defenders can pile onto the same target), just a per-unit "who am I
    /// dealing with right now" reference.
    /// </summary>
    private Bramblekin? _combatTarget;

    /// <summary>
    /// Blood Feud Base Razing: the enemy Village Heart this Militia unit is
    /// currently pathing to and Poking while Raiding — opportunistic
    /// offense, picked up when it wanders within its own aggro radius of
    /// one its faction is at declared war with, with no living hostile
    /// Bramblekin also in range. Re-checked every frame in
    /// <see cref="UpdateRaiding"/>; not a Dibs claim, any number of Militia
    /// can pile onto the same Heart at once.
    /// </summary>
    private VillageHeart? _raidTarget;

    /// <summary>The Schism: set the instant this Bramblekin becomes a Pioneer (see <see cref="BecomePioneer"/>), cleared the instant it stops Migrating (see <see cref="UpdateMigrating"/>).</summary>
    private Migration? _migration;

    /// <summary>Feet position on the ground (y = GroundHeight).</summary>
    public Vector3 Position => _mover.Position;

    public BramblekinState State { get; private set; }

    /// <summary>Gatherer by default; the Job Manager promotes/demotes it to track its faction's Militia Target and Builder Conscription.</summary>
    public BramblekinRole Role { get; private set; } = BramblekinRole.Gatherer;

    public bool IsCarrying => _carried is not null || _carriedAmber is not null;

    /// <summary>
    /// Individual Equipment: true once this specific Militia unit has
    /// touched a Spider Fang. Boosts its own Poke damage and renders its
    /// pike bright silver (see <see cref="Draw"/>). Perishes with it if it
    /// dies — the Village Heart's next Sprout is always un-upgraded.
    /// </summary>
    public bool HasFangPike { get; private set; }

    /// <summary>
    /// Individual Equipment: true once this specific Gatherer has touched a
    /// Chitin piece. Lets it target whole Acorns (Cooperative Acorn
    /// Cracking) and renders a grey mallet (see <see cref="Draw"/>).
    /// Perishes with it if it dies.
    /// </summary>
    public bool HasChitinMallet { get; private set; }

    /// <summary>
    /// True once this Bramblekin has been caught by a predator. A dead
    /// Bramblekin lingers in <see cref="World.Colony"/> until the end of the
    /// frame (see World's pending-removal queue) so nothing removes it out
    /// from under an in-progress iteration; every system checks this flag
    /// and treats a dead Bramblekin as already gone.
    /// </summary>
    public bool IsDead { get; private set; }

    /// <summary>Busy workers (gathering or hauling food) make vibrations a Wolf Spider can feel. A dead Bramblekin never vibrates.</summary>
    public bool IsVibrating => !IsDead && State is BramblekinState.Gathering or BramblekinState.Returning;

    /// <summary>Hit points out of <see cref="MaxHealth"/>. Only a Militia unit ever takes damage (the spider's Bite), but every Bramblekin tracks it.</summary>
    public int Health { get; private set; } = MaxHealth;

    public const int MaxHealth = 30;

    /// <summary>
    /// Which tribe this Bramblekin belongs to — determines which Village
    /// Heart it gathers/builds for (see <see cref="World.VillageFor"/>).
    /// Private set: the Schism reassigns this the instant a Bramblekin
    /// becomes a Pioneer (see <see cref="BecomePioneer"/>), well before its
    /// new Village Heart even exists.
    /// </summary>
    public int FactionID { get; private set; }

    /// <summary>Its faction's colour, blended faintly into its body (see <see cref="Draw"/>) so tribes read apart at a glance.</summary>
    public Color FactionColor { get; private set; }

    public Bramblekin(Vector3 position, Random rng, int factionId, Color factionColor)
    {
        _rng = rng;
        FactionID = factionId;
        FactionColor = factionColor;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);

        // Start mid-pause with a random timer so the colony doesn't move in lockstep.
        SetState(BramblekinState.Pausing);
        _pauseTimer = (float)rng.NextDouble() * PauseDuration;
    }

    /// <summary>
    /// Reduces Health and, if that brings it to 0, dies — via
    /// <see cref="World.KillByBramblekin"/> (Spoils of War) when
    /// <paramref name="attackerFactionId"/> names the Bramblekin faction
    /// that dealt the blow, or the plain <see cref="World.Kill"/> for
    /// anything else (the Wolf Spider's Bite, chiefly). The Blood Feud:
    /// landing ANY damaging hit from one faction on another — a killing
    /// blow or not — immediately declares war between them (see
    /// <see cref="World.DeclareBloodFeud"/>) if they weren't already at
    /// war, since Default Peace never survives actual bloodshed.
    /// </summary>
    public void TakeDamage(int amount, World world, int? attackerFactionId = null)
    {
        if (IsDead)
            return;

        if (attackerFactionId is { } attackerId)
            world.DeclareBloodFeud(FactionID, attackerId);

        Health = Math.Max(0, Health - amount);
        if (Health <= 0)
        {
            if (attackerFactionId is not null)
                world.KillByBramblekin(this);
            else
                world.Kill(this);
        }
    }

    /// <summary>
    /// Marks this Bramblekin as caught: drops any carried food immediately
    /// (so it's still gatherable), releases any Dibs claim so it doesn't
    /// lock a shard/Aphid/Acorn out forever, and flags it dead. Death
    /// Penalty: any equipment (<see cref="HasFangPike"/>/<see cref="HasChitinMallet"/>)
    /// simply perishes with the instance — nothing else ever references it,
    /// and the Village Heart's next Sprout always starts fresh, un-upgraded.
    /// Called once, from <see cref="World.Kill"/>; it does not touch
    /// <see cref="World.Colony"/> itself — that removal is deferred and
    /// processed at the end of the frame.
    /// </summary>
    public void MarkDead()
    {
        if (IsDead)
            return;

        DropCarried();
        ReleaseFoodClaim();
        ReleaseAphidClaim();
        ReleaseAcornClaim();
        ReleaseAmberClaim();

        // The Schism: a Pioneer lost en route. Tells its Migration so the
        // origin's HasActiveMigration eventually clears if all 4 are lost
        // before any of them founds the new Village Heart.
        _migration?.PioneerLost();
        _migration = null;

        IsDead = true;
    }

    /// <summary>
    /// Conscription: reclassifies this Bramblekin as Militia (called by the
    /// Job Manager as it works the colony toward its own faction's Militia
    /// Target). Drops anything carried and, if it was mid-Gathering,
    /// mid-Returning or mid-Building, immediately breaks that off with a
    /// short pause rather than let it finish one last delivery or Blueprint
    /// — the transition is meant to be immediate. The Building guard is
    /// defensive: only a Builder normally enters that state, and Builder
    /// Conscription only ever promotes to Militia from the Gatherer pool,
    /// but nothing else ever reclaims a Bramblekin stuck in Building, so
    /// this stays here as a safety net rather than fighting. The priority
    /// chain re-decides what to do next (defend, hunt, or wander) on the
    /// very next Update().
    /// </summary>
    public void PromoteToMilitia()
    {
        if (Role == BramblekinRole.Militia)
            return;

        Role = BramblekinRole.Militia;
        DropCarried();
        if (State is BramblekinState.Gathering or BramblekinState.Returning or BramblekinState.Building)
            StartPause();
    }

    /// <summary>
    /// Conscription in reverse: stands this Militia or Builder unit down
    /// (a Militia's pike is put away — Draw() stops drawing it the moment
    /// Role changes) and sends it back to Wandering so it starts looking
    /// for food again. Whatever it was doing — mid-charge Defending,
    /// mid-chase Hunting, mid-Building — is dropped immediately, same as a
    /// promotion is.
    /// </summary>
    public void DemoteToGatherer(World world)
    {
        if (Role == BramblekinRole.Gatherer)
            return;

        Role = BramblekinRole.Gatherer;
        DropCarried(); // Defensive: neither Militia nor Builder ever actually carries food.
        StartWandering(world);
    }

    /// <summary>
    /// Conscription (the Job Manager's Builder assignment): pulls this
    /// Gatherer off food duty to work the faction's Blueprints instead.
    /// Releases whatever Gathering claim it was holding (Food Shard, Acorn
    /// or Amber) so it isn't left orphaned, mid-claim, at the old job, and
    /// interrupts anything it was mid-way through so the priority chain
    /// picks Building fresh on its very next Update().
    /// </summary>
    public void PromoteToBuilder()
    {
        if (Role == BramblekinRole.Builder)
            return;

        Role = BramblekinRole.Builder;
        DropCarried();
        ReleaseFoodClaim();
        ReleaseAcornClaim();
        ReleaseAmberClaim();
        if (State is BramblekinState.Gathering or BramblekinState.Returning or BramblekinState.Cracking or BramblekinState.Equipping)
            StartPause();
    }

    /// <summary>
    /// The Schism: this Bramblekin is one of the 4 Pioneers a Village Heart
    /// just sent off. Immediately switches its Faction (and, with it, its
    /// visual tint — see <see cref="Draw"/>) to the new one, drops anything
    /// it was carrying or claiming, and forces it into Migrating, where it
    /// stays — ignoring food, blueprints and enemies alike — until it or
    /// another Pioneer bound to the same <see cref="Migration"/> founds the
    /// new Village Heart (see <see cref="UpdateMigrating"/>).
    ///
    /// Brain Wipe: <see cref="ReleaseFoodClaim"/>/<see cref="ReleaseAcornClaim"/>
    /// below null out this Bramblekin's Food Shard/Acorn target (<c>_claimedShard</c>/
    /// <c>_claimedAcorn</c>) and cancel its claim on whichever one it was
    /// still holding at the old, now-foreign Village Heart, all before
    /// State ever flips to Migrating — a Pioneer's very first frame under
    /// its new Faction never has a stale pointer back at the parent's
    /// granary for <see cref="UpdateGathering"/> to pick back up the moment
    /// it stops Migrating.
    /// </summary>
    public void BecomePioneer(Migration migration)
    {
        DropCarried();
        ReleaseFoodClaim();
        ReleaseAphidClaim();
        ReleaseAcornClaim();
        ReleaseAmberClaim();

        FactionID = migration.NewFactionID;
        FactionColor = migration.NewFactionColor;
        _migration = migration;
        _target = migration.Target;
        SetState(BramblekinState.Migrating);
    }

    /// <summary>
    /// The Refugee Protocol's Assimilation branch: called on a Base Razing
    /// survivor when <see cref="World.RunRefugeeProtocol"/> can't find any
    /// safe ground left to resettle on. Surrenders outright — no Migrating
    /// detour, no Pioneer status, just an immediate switch into the
    /// conquering faction's colour and FactionID, wherever the fight left
    /// it standing. The very next Update() picks up its new home's food,
    /// blueprints and defense needs like it had always belonged there.
    /// </summary>
    public void Assimilate(int factionId, Color factionColor)
    {
        FactionID = factionId;
        FactionColor = factionColor;
    }

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return; // Awaiting removal at the end of the frame; do nothing.

        _mover.Idle();
        if (_pokeCooldown > 0f)
            _pokeCooldown -= deltaTime;

        // --- 0. The Schism (absolute priority): a Pioneer ignores food,
        // blueprints and enemies alike and paths straight for its
        // Migration's Target. Nothing else in this method runs while
        // Migrating — see UpdateMigrating.
        if (State == BramblekinState.Migrating)
        {
            UpdateMigrating(deltaTime, world);
            return;
        }

        bool isSafe(Vector3 p) => IsSafeSpot(p, world);

        VillageHeart? home = world.VillageFor(FactionID);

        // --- 2. Cultural Borders: Militia only engage the Wolf Spider
        // while it's within their own Village Heart's (wealth-scaled) own
        // territory ring — organic roaming means it spends most of its time
        // out of territory, ignored. Re-aimed every frame, so a defending
        // Militia keeps adjusting where it's standing as the spider moves.
        // A Gatherer's Fear Aura is unaffected by territory: it flees on
        // sight within FearRadius regardless of where either of them is.
        // The 'Enemy of My Enemy' Protocol — Apex Priority: this check is
        // unconditional on the spider's own State (any spider merely
        // present in the ring, whatever it's doing, wins), and being
        // checked here — ahead of 2a/2b/2c/2d below in the same if/else-if
        // chain — it already completely overrides any rival-faction target
        // the instant it's true, full stop.
        if (Role == BramblekinRole.Militia && world.Spider is { } spider && home is not null &&
                 GroundMover.HorizontalDistanceSquared(spider.Position, home.Center) <= home.TerritoryRadius * home.TerritoryRadius)
        {
            _combatTarget = null;
            _target = ComputeInterceptPoint(spider, home);
            if (State != BramblekinState.Defending)
            {
                DropCarried();
                SetState(BramblekinState.Defending);
            }
        }
        // AI Time-Slicing (the "Brain"): 2a-2d below each scan the full
        // Colony (and, for 2d, Villages) arrays looking for a threat --
        // expensive with a large map/colony. Only staggered onto this
        // unit's own frame (World.FrameCounter % 15 == ID % 15); whatever
        // _combatTarget/_raidTarget/State it last settled on stays exactly
        // as-is on the other 14 frames out of 15, and UpdateDefending/
        // UpdateRaiding (pure Legs — chase, poke, leash checks) keep
        // running every frame regardless, off the cached target.
        else if (Role == BramblekinRole.Militia && world.FrameCounter % 15 == ID % 15 &&
                 TryUpdateMilitiaThreatPriority(world, home))
        {
            // Handled inside TryUpdateMilitiaThreatPriority, which already
            // set _combatTarget/_raidTarget/_target/State for whichever of
            // 2a-2d matched.
        }
        else if (Role != BramblekinRole.Militia && world.Spider is { } nearSpider &&
                 GroundMover.HorizontalDistanceSquared(Position, nearSpider.Position) < FearRadius * FearRadius)
        {
            DropCarried();
            _target = FindPointAwayFrom(nearSpider.Position, world);
            if (State != BramblekinState.Fleeing)
                SetState(BramblekinState.Fleeing);
        }

        // --- 3. Village Building (Builder only): the Job Manager's Builder
        // Conscription already keeps exactly one Bramblekin assigned to this
        // Role whenever the faction has an incomplete Blueprint, so it's
        // simply put to work here rather than every idle Gatherer racing for
        // the same site. Once set to Building it's no longer "Walking or
        // Pausing", so nothing below can steal it back this frame.
        if (Role == BramblekinRole.Builder && State is BramblekinState.Walking or BramblekinState.Pausing && world.HasIncompleteBlueprintFor(FactionID))
            SetState(BramblekinState.Building);

        // --- 3a. Individual Equipment: an un-upgraded unit prioritizes
        // gearing up over its ordinary job — a Militia unit fetches a
        // Spider Fang, a Gatherer fetches a Chitin piece.
        if (State is BramblekinState.Walking or BramblekinState.Pausing)
        {
            if (Role == BramblekinRole.Militia && !HasFangPike && world.HasAvailableFang)
                SetState(BramblekinState.Equipping);
            else if (Role == BramblekinRole.Gatherer && !HasChitinMallet && world.HasAvailableChitin)
                SetState(BramblekinState.Equipping);
        }

        // --- 3b. Economy overrides wandering (Gatherers only) --------------
        if (Role == BramblekinRole.Gatherer && State is BramblekinState.Walking or BramblekinState.Pausing && world.HasAvailableFoodFor(this, home))
            SetState(BramblekinState.Gathering);

        // --- 3c. Militia hunts Aphids within the 20-Meter Territory Rule, when it has no spider to fight -----------
        if (Role == BramblekinRole.Militia && State is BramblekinState.Walking or BramblekinState.Pausing &&
            home is not null && world.HasHuntableAphidNearVillage(this, home))
            SetState(BramblekinState.Hunting);

        // --- 4. Run the current state -------------------------------------------
        switch (State)
        {
            case BramblekinState.Pausing:
                _pauseTimer -= deltaTime;
                if (_pauseTimer <= 0f)
                    StartWandering(world);
                break;

            case BramblekinState.Walking:
                // Don't stroll into a spot that has since been marked for a drop or covered by a rock.
                if (!IsSafeSpot(_target, world))
                    _target = world.RandomFreePoint(BodyRadius + 0.1f, EdgeMargin);

                if (_mover.MoveTowards(_target, EffectiveWalkSpeed(world), deltaTime, world, isSafe))
                    StartPause();
                break;

            case BramblekinState.Gathering:
                UpdateGathering(deltaTime, world, home);
                break;

            case BramblekinState.Cracking:
                UpdateCracking(deltaTime, world);
                break;

            case BramblekinState.Returning:
                UpdateReturning(deltaTime, world, home);
                break;

            case BramblekinState.Fleeing:
                if (_mover.MoveTowards(_target, WalkSpeed * FleeSpeedMultiplier, deltaTime, world, isSafe))
                    StartPause(); // Catch its breath, then back to work.
                break;

            case BramblekinState.Defending:
                UpdateDefending(deltaTime, world, home);
                break;

            case BramblekinState.Hunting:
                UpdateHunting(deltaTime, world, home);
                break;

            case BramblekinState.Raiding:
                UpdateRaiding(deltaTime, world, home);
                break;

            case BramblekinState.Building:
                UpdateBuilding(deltaTime, world);
                break;

            case BramblekinState.Equipping:
                UpdateEquipping(deltaTime, world);
                break;
        }
    }

    /// <summary>
    /// The Brain half of the Militia threat-priority chain (2a-2d, split
    /// out of <see cref="Update"/> so the whole thing can be gated behind
    /// the AI Time-Slice check there): Base Defense Aggro, Blood Feud
    /// Border Wars, Thievery Response, then Blood Feud Base Razing, in that
    /// priority order. Sets <see cref="_combatTarget"/>/<see cref="_raidTarget"/>/
    /// <see cref="_target"/>/<see cref="State"/> and returns true the
    /// instant any one of them matches; returns false (touching nothing)
    /// if none do, leaving whatever this unit was already doing in place.
    /// </summary>
    private bool TryUpdateMilitiaThreatPriority(World world, VillageHeart? home)
    {
        // --- 2a. Base Defense Aggro: a foreign Bramblekin caught within
        // World.BaseDefenseAggroRadius of our own Village Heart — right up
        // against the doorstep, not just somewhere in the wider 20m ring —
        // is treated as an active attack in progress no matter what peace
        // or Truce currently holds. Instantly declares a Blood Feud on its
        // whole faction (World.DeclareBloodFeud) and engages it directly,
        // rather than waiting for Border Wars/Thievery to notice next
        // frame — this is what used to leave Base Razing raiders crowding
        // around a Heart while the defenders looked right through them.
        if (home is not null && world.NearestForeignBramblekinNearHeart(home) is { } intruder)
        {
            world.DeclareBloodFeud(FactionID, intruder.FactionID);
            _combatTarget = intruder;
            _target = intruder.Position;
            if (State != BramblekinState.Defending)
            {
                DropCarried();
                SetState(BramblekinState.Defending);
            }
            return true;
        }
        // --- 2b. Blood Feud Border Wars: with no Wolf Spider to answer, a
        // faction's Militia still has to answer a Bramblekin (Gatherer or
        // Militia) of a faction it's actually at declared war with (see
        // VillageHeart.HostileFactions) trespassing within their own
        // Village Heart's territory ring. Default Peace: any other
        // faction's Bramblekin is completely ignored here, full stop — see
        // World.NearestHostileBramblekinInTerritory. Same absolute priority
        // tier and the same Defending state as the spider fight above — see
        // UpdateDefending for the actual chase/poke. The 'Enemy of My
        // Enemy' Protocol — Temporary Truce: guarded against a spider
        // that's actively Hunting/Pouncing nearby (see
        // IsSpiderActivelyThreateningTerritory) on top of Apex Priority
        // above, so a common-enemy emergency always wins even in the
        // narrower window that check alone wouldn't have caught.
        if (home is not null &&
            !world.IsSpiderActivelyThreateningTerritory(home) &&
            world.NearestHostileBramblekinInTerritory(home) is { } enemy)
        {
            _combatTarget = enemy;
            _target = enemy.Position;
            if (State != BramblekinState.Defending)
            {
                DropCarried();
                SetState(BramblekinState.Defending);
            }
            return true;
        }
        // --- 2c. Thievery Response: Default Peace still allows for a
        // surgical, single-target response — a foreign Gatherer caught
        // physically picking up food inside our own territory (see
        // Bramblekin.TrespassingAgainst / World.NearestTrespasserInTerritory)
        // gets singled out and confronted, without declaring war on its
        // whole faction. UpdateDefending resolves it as a non-lethal
        // Warning Shove unless the confrontation itself escalates into a
        // Blood Feud (an armed trespasser, or one that lands a hit back).
        // Checked after Blood Feud Border Wars: an already-hostile
        // faction's trespassing Gatherer is just an enemy in our territory
        // by then, not merely a thief to warn off.
        if (home is not null &&
            !world.IsSpiderActivelyThreateningTerritory(home) &&
            world.NearestTrespasserInTerritory(home) is { } trespasser)
        {
            _combatTarget = trespasser;
            _target = trespasser.Position;
            if (State != BramblekinState.Defending)
            {
                DropCarried();
                SetState(BramblekinState.Defending);
            }
            return true;
        }
        // --- 2d. Blood Feud Base Razing: opportunistic offense rather than
        // home defense -- a Militia unit that's simply wandered within its
        // own (Cultural Borders — wealth-scaled) aggro radius of a Village
        // Heart it's actually at declared war with, with no living hostile
        // Bramblekin also in that radius
        // (a live threat always comes first — see 2b above, which already
        // claims this frame if one's in range), paths in and Pokes it down
        // instead. Default Peace: any faction with no declared Blood Feud
        // is never a valid Raiding target. Temporary Truce applies here
        // too: a spider actively threatening home calls off Base Razing
        // just like Border Wars.
        float ownTerritoryRadius = home?.TerritoryRadius ?? World.BaseTerritoryRadius;
        if (!world.IsSpiderActivelyThreateningTerritory(home) &&
            world.NearestHostileVillageHeartInRange(Position, FactionID, ownTerritoryRadius) is { } enemyHeart &&
            !world.HasLivingHostileBramblekinNear(Position, FactionID, ownTerritoryRadius))
        {
            _raidTarget = enemyHeart;
            _target = enemyHeart.Center;
            if (State != BramblekinState.Raiding)
            {
                DropCarried();
                SetState(BramblekinState.Raiding);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// War Weariness: below <see cref="World.WearyMoraleThreshold"/> Morale, a
    /// Gatherer or Builder is Weary and walks at <see cref="World.WearySpeedMultiplier"/>
    /// speed. Militia are unaffected — soldiers, not workers — and a full
    /// panicked Flee (see <see cref="BramblekinState.Fleeing"/>) always runs
    /// at full speed regardless: fatigue doesn't slow down running for your
    /// life. The Nectar Brewery: stacked on top for a Gatherer specifically
    /// — see <see cref="NectarSpeedMultiplier"/>.
    /// </summary>
    private float EffectiveWalkSpeed(World world)
    {
        VillageHeart? home = world.VillageFor(FactionID);
        float speed = Role != BramblekinRole.Militia && (home?.GatherersAreWeary ?? false)
            ? WalkSpeed * World.WearySpeedMultiplier
            : WalkSpeed;

        if (Role == BramblekinRole.Gatherer && home is not null)
            speed *= NectarSpeedMultiplier(home);

        return speed;
    }

    /// <summary>
    /// The Nectar Brewery: a permanent civilization buff — every point of
    /// <see cref="VillageHeart.NectarStored"/> this Gatherer's own faction
    /// has brewed (see <see cref="World.UpdateNectarBrewery"/>) permanently
    /// moves it <see cref="World.NectarSpeedBonusPerPoint"/> faster, capped
    /// at <see cref="World.MaxNectarSpeedBonus"/> (+50%) so a sufficiently
    /// ancient civilization can't eventually move arbitrarily fast.
    /// </summary>
    private static float NectarSpeedMultiplier(VillageHeart home) =>
        1f + Math.Min(home.NectarStored * World.NectarSpeedBonusPerPoint, World.MaxNectarSpeedBonus);

    /// <summary>
    /// Individual Equipment: a Militia unit's pike renders bright
    /// white/silver once its own <see cref="HasFangPike"/> is true (Rose-
    /// Thorn brown otherwise); a Gatherer with <see cref="HasChitinMallet"/>
    /// carries a small grey mallet instead. Both are per-unit — no more
    /// village-wide unlock.
    /// </summary>
    public void Draw()
    {
        Color baseColor = State == BramblekinState.Fleeing ? PanicColor
                    : Role == BramblekinRole.Militia ? MilitiaColor
                    : Role == BramblekinRole.Builder ? BuilderColor
                    : CalmColor;
        Color color = TintWithFaction(baseColor);

        // A capsule standing upright: DrawCapsule takes the centres of its two
        // hemispherical ends, so inset them by the radius.
        var bottom = Position + new Vector3(0, BodyRadius, 0);
        var top = Position + new Vector3(0, BodyHeight - BodyRadius, 0);
        Raylib.DrawCapsule(bottom, top, BodyRadius, 8, 4, color);
        Raylib.DrawCapsuleWires(bottom, top, BodyRadius, 8, 4, new Color(0, 0, 0, 50));

        Vector2 facing = _mover.Heading.LengthSquared() > 1e-6f ? _mover.Heading : Vector2.UnitX;

        // Militia carry a pike: a small line held out front, angled up, so
        // they read as armed even at a glance -- Rose-Thorn brown normally,
        // or bright white/silver once this unit's own Fang Pike is equipped.
        if (Role == BramblekinRole.Militia)
        {
            Color pikeColor = HasFangPike ? FangPikeColor : PikeColor;
            var grip = Position + new Vector3(0, BodyHeight * 0.6f, 0);
            var tip = grip + new Vector3(facing.X, 0.55f, facing.Y) * 0.6f;
            Raylib.DrawLine3D(grip, tip, pikeColor);
            Raylib.DrawSphere(tip, 0.025f, pikeColor);
        }
        // A Chitin-Mallet Gatherer carries a small hammer the same way.
        else if (HasChitinMallet)
        {
            var grip = Position + new Vector3(0, BodyHeight * 0.55f, 0);
            var headCenter = grip + new Vector3(facing.X, 0.3f, facing.Y) * 0.5f;
            Raylib.DrawLine3D(grip, headCenter, MalletHandleColor);
            Raylib.DrawCube(headCenter, 0.12f, 0.12f, 0.12f, MalletHeadColor);
        }

        // Carried food (or Amber) rides on top of the head.
        _carried?.Draw(Position + new Vector3(0, BodyHeight, 0));
        _carriedAmber?.Draw(Position + new Vector3(0, BodyHeight, 0));
    }

    /// <summary>Unit Colors: blends a faint dash of <see cref="FactionColor"/> into a base body color, so tribes read apart without drowning out State/Role's own colour cues.</summary>
    private Color TintWithFaction(Color baseColor)
    {
        const float tintStrength = 0.3f;
        byte Mix(byte body, byte faction) => (byte)(body * (1f - tintStrength) + faction * tintStrength);
        return new Color(Mix(baseColor.R, FactionColor.R), Mix(baseColor.G, FactionColor.G), Mix(baseColor.B, FactionColor.B), baseColor.A);
    }

    /// <summary>Puts carried food (or Amber) back on the ground where we stand (it can be gathered again later). Thievery: also clears <see cref="TrespassingAgainst"/> — the flag only ever applies while the stolen goods are still in hand.</summary>
    public void DropCarried()
    {
        if (_carried is not null)
        {
            _carried.Position = Position;
            _carried.IsCarried = false;
            _carried = null;
            TrespassingAgainst = null;
        }

        if (_carriedAmber is not null)
        {
            _carriedAmber.Position = Position;
            _carriedAmber.IsCarried = false;
            _carriedAmber = null;
            TrespassingAgainst = null;
        }
    }

    /// <summary>Dibs: releases this Bramblekin's claim on its current Food Shard target, if any (a no-op if someone else has since claimed it, e.g. through a race that shouldn't happen but is cheap to guard against).</summary>
    private void ReleaseFoodClaim()
    {
        if (_claimedShard is not null && _claimedShard.ClaimedBy == this)
            _claimedShard.ClaimedBy = null;
        _claimedShard = null;
    }

    /// <summary>Tycoon Economy Dibs: releases this Gatherer's claim on its current Amber target, if any.</summary>
    private void ReleaseAmberClaim()
    {
        if (_claimedAmber is not null && _claimedAmber.ClaimedBy == this)
            _claimedAmber.ClaimedBy = null;
        _claimedAmber = null;
    }

    /// <summary>Dibs: releases this Bramblekin's claim on its current Aphid target, if any.</summary>
    private void ReleaseAphidClaim()
    {
        if (_claimedAphid is not null && _claimedAphid.ClaimedBy == this)
            _claimedAphid.ClaimedBy = null;
        _claimedAphid = null;
    }

    /// <summary>Cooperative Acorn Cracking: releases this Gatherer's claim slot on its current Acorn target, if any.</summary>
    private void ReleaseAcornClaim()
    {
        _claimedAcorn?.ReleaseClaim(this);
        _claimedAcorn = null;
    }

    /// <summary>
    /// The Shatter Trigger: called once by <see cref="World.UpdateAcornCracking"/>
    /// for every claimant the instant their shared Acorn's CrackProgress
    /// crosses its CrackThreshold. Explicitly drops the now-gone Acorn as a
    /// target (no need to release the claim slot itself — the whole Acorn
    /// is being discarded) and hands this Gatherer straight back to
    /// Gathering, so it immediately calls dibs on one of the Food Shards
    /// the shatter just dropped rather than idling on a dangling reference.
    /// </summary>
    public void OnAcornShattered()
    {
        _claimedAcorn = null;
        if (State == BramblekinState.Cracking)
            SetState(BramblekinState.Gathering);
    }

    /// <summary>Instant displacement (m) a Warning Shove knocks a caught trespasser back by.</summary>
    private const float WarningShoveDistance = 1.2f;

    /// <summary>
    /// The Warning Shove (Thievery): this interrupts whatever this
    /// Bramblekin was doing — a defending
    /// Militia unit just caught it red-handed. Knocks it directly away from
    /// <paramref name="shovedFrom"/>, drops whatever it was carrying
    /// (clearing <see cref="TrespassingAgainst"/> with it — see
    /// <see cref="DropCarried"/>), and sends it fleeing straight for its
    /// own Village Heart rather than to some arbitrary safe point, same as
    /// any other Fleeing trigger.
    /// </summary>
    public void ReceiveWarningShove(Vector3 shovedFrom, World world)
    {
        if (IsDead)
            return;

        Vector2 away = new(Position.X - shovedFrom.X, Position.Z - shovedFrom.Z);
        away = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away) : Vector2.UnitX;
        _mover.Nudge(new Vector3(away.X, 0, away.Y) * WarningShoveDistance, world);

        DropCarried();
        VillageHeart? home = world.VillageFor(FactionID);
        _target = home?.Center ?? FindPointAwayFrom(shovedFrom, world);
        SetState(BramblekinState.Fleeing);
    }

    // --- Economy states ----------------------------------------------------------

    private void UpdateGathering(float deltaTime, World world, VillageHeart? home)
    {
        // AI Time-Slicing (the "Brain"): the target searches below scan the
        // full FoodShards/AmberNodes/Acorns arrays, which gets expensive
        // with a large map and colony. Only one in every 15 Bramblekin runs
        // this scan on any given frame (staggered by ID), so the aggregate
        // cost stays flat regardless of colony size. Whatever was claimed
        // last scan (_claimedAmber/_claimedShard) is cached and kept below,
        // so on off-frames this unit still walks to, and picks up, its
        // existing target every frame — only the re-scan itself is gated.
        if (world.FrameCounter % 15 == ID % 15)
        {
            // Continuous Cracking: only a Chitin-Mallet Gatherer ever targets a
            // whole Acorn, and only while it can still claim one of its
            // MaxClaimants slots. Claiming one hands off to the Cracking state
            // entirely — see UpdateCracking for the walk-there/add-progress
            // loop and World.UpdateAcornCracking for the actual shatter.
            //
            // Efficiency Check: an upgraded Gatherer doesn't blindly beeline for
            // every claimable Acorn in reach — it only commits when the Acorn
            // is genuinely the smarter catch, i.e. no loose Food Shard sitting
            // closer that it would otherwise walk straight past. A shard tied
            // (or a wash) with the Acorn still favors cracking, since a group
            // Acorn generally out-yields a single shard once a few Gatherers
            // pile on.
            if (HasChitinMallet && world.NearestClaimableAcorn(Position, this) is { } acorn)
            {
                FoodShard? nearestShard = world.NearestAvailableShard(Position, this, home);
                bool acornIsSmarterChoice = nearestShard is null ||
                    GroundMover.HorizontalDistanceSquared(Position, acorn.Position) <= GroundMover.HorizontalDistanceSquared(Position, nearestShard.Position);

                if (acornIsSmarterChoice && acorn.TryClaim(this))
                {
                    ReleaseFoodClaim(); // Switching to the Acorn this frame — don't leave a stale claim on whatever shard we were chasing.
                    _claimedAcorn = acorn;
                    SetState(BramblekinState.Cracking);
                    return;
                }
            }

            // Tycoon Economy — Maslow's Hierarchy: a well-fed village's
            // Gatherers chase Amber (wealth) ahead of wild food; a hungry one
            // ignores Amber completely and falls straight through to the Food
            // Shard logic below. Same Safe Gathering (Danger Penalty) and
            // Maximum Search Radius rules as any other target — see
            // World.NearestAvailableAmber.
            bool wellFed = home is not null && home.FoodStored >= home.MaxFoodCapacity / 2;
            AmberNode? amber = wellFed ? world.NearestAvailableAmber(Position, this) : null;
            if (amber is not null)
            {
                if (amber != _claimedAmber)
                {
                    ReleaseAmberClaim();
                    _claimedAmber = amber;
                    amber.ClaimedBy = this;
                    amber.ClaimTimer = 0f;
                }
            }
            else
            {
                ReleaseAmberClaim(); // Not well-fed, or nothing to chase — don't leave a stale claim behind.
            }

            if (_claimedAmber is null)
            {
                FoodShard? shard = world.NearestAvailableShard(Position, this, home);
                if (shard != _claimedShard)
                {
                    ReleaseFoodClaim();
                    _claimedShard = shard;
                    if (shard is not null)
                    {
                        shard.ClaimedBy = this;
                        shard.ClaimTimer = 0f;
                    }
                }

                if (shard is null)
                {
                    // Maximum Search Radius: nothing to gather within reach at all
                    // (as opposed to StartWandering's ordinary map-wide roam) --
                    // wait close to home instead of hiking toward whatever's
                    // technically nearest across the whole map; the local Spore
                    // Farm's next Berry is the actual fix, not a long walk.
                    StartWanderingNearHome(world, home);
                    return;
                }
            }
        }

        // Object Pooling: a cached target held from an earlier scan may
        // have since despawned (UpdateLootDespawn deactivates it, rather
        // than removing it from the pool, without going through this
        // Bramblekin at all) or even been recycled by the pool into an
        // unrelated spawn elsewhere on the map — drop a now-inactive
        // reference rather than walking toward, or "picking up", a
        // pool slot that isn't really this shard/amber any more. Waits
        // for the next scan frame to pick something else.
        if (_claimedAmber is { IsActive: false })
            _claimedAmber = null;
        if (_claimedShard is { IsActive: false })
            _claimedShard = null;

        // Continuous Legs: whichever target is currently cached (found this
        // frame's scan, or a still-valid one from up to 14 frames ago) is
        // walked toward and, on arrival, picked up, every single frame —
        // never gated by the time-slice above.
        if (_claimedAmber is { } cachedAmber)
        {
            if (GroundMover.HorizontalDistance(Position, cachedAmber.Position) <= PickupDistance)
            {
                cachedAmber.IsCarried = true;
                cachedAmber.ClaimedBy = null;
                _claimedAmber = null;
                _carriedAmber = cachedAmber;

                // Same Thievery rule as a stolen Food Shard: an Amber node
                // sitting inside a rival's 20m border flags us as a caught
                // trespasser the instant we pick it up.
                TrespassingAgainst = world.ForeignTerritoryContaining(cachedAmber.Position, FactionID);

                SetState(BramblekinState.Returning);
                return;
            }

            _mover.MoveTowards(cachedAmber.Position, EffectiveWalkSpeed(world), deltaTime, world, p => IsSafeSpot(p, world));
            return;
        }

        if (_claimedShard is { } cachedShard)
        {
            if (GroundMover.HorizontalDistance(Position, cachedShard.Position) <= PickupDistance)
            {
                cachedShard.IsCarried = true;
                cachedShard.ClaimedBy = null;
                _claimedShard = null;
                _carried = cachedShard;

                // Thievery: caught in the act the instant the shard we just
                // grabbed turns out to be sitting inside someone else's 20m
                // border -- flags us for that specific faction's Militia to
                // single out, whatever the wider peace between us still holds.
                TrespassingAgainst = world.ForeignTerritoryContaining(cachedShard.Position, FactionID);

                SetState(BramblekinState.Returning);
                return;
            }

            _mover.MoveTowards(cachedShard.Position, EffectiveWalkSpeed(world), deltaTime, world, p => IsSafeSpot(p, world));
        }
    }

    /// <summary>
    /// Continuous Cracking: an Acorn is a mining node, not a switch three
    /// Gatherers all have to flip at once — paths to the claimed Acorn and,
    /// once touching, steadily adds <see cref="CrackRatePerGatherer"/> to
    /// its <see cref="Acorn.CrackProgress"/> every frame. Up to
    /// <see cref="Acorn.MaxClaimants"/> Gatherers can be doing this on the
    /// same Acorn at once — their rates simply add together, so it breaks
    /// proportionally faster the more show up (20/s alone takes 5s for the
    /// default 100 CrackThreshold; two together take 2.5s; three, ~1.7s)
    /// rather than nothing happening at all until every slot is full. The
    /// actual shatter is handled centrally, once a frame, by
    /// <see cref="World.UpdateAcornCracking"/> — see
    /// <see cref="OnAcornShattered"/> for the hand-off back to Gathering.
    /// </summary>
    private void UpdateCracking(float deltaTime, World world)
    {
        // The claim may have gone stale since last frame -- the Acorn
        // already shattered (handled via OnAcornShattered, which should
        // already have moved us out of this state, but a stray call path
        // is cheap to guard against).
        if (_claimedAcorn is not { } acorn || !acorn.IsActive)
        {
            _claimedAcorn = null;
            SetState(BramblekinState.Gathering);
            return;
        }

        float contactDistance = BodyRadius + Acorn.Radius + World.AcornCoopContactMargin;
        if (GroundMover.HorizontalDistance(Position, acorn.Position) > contactDistance)
        {
            _mover.MoveTowards(acorn.Position, EffectiveWalkSpeed(world), deltaTime, world, p => IsSafeSpot(p, world));
            return;
        }

        acorn.CrackProgress += CrackRatePerGatherer * deltaTime;
    }

    private void UpdateReturning(float deltaTime, World world, VillageHeart? home)
    {
        // AI Faction Loyalty: a Gatherer only ever delivers to its own
        // faction's Village Heart. Falls back to wandering in the
        // practically-unreachable case that faction no longer has one.
        if (home is null)
        {
            StartWandering(world);
            return;
        }

        if (GroundMover.HorizontalDistance(Position, home.Center) <= home.DeliveryDistance)
        {
            // Explicit payload-type check: Amber and Food Shards are two
            // distinct carry slots (see _carriedAmber/_carried), so the
            // drop-off has to ask which one this Gatherer is actually
            // holding rather than assuming — depositing the wrong one (or
            // silently dropping neither) is exactly how Amber stopped
            // incrementing VillageHeart.AmberStored.
            if (_carriedAmber is { } amberPayload)
            {
                world.DeliverAmber(amberPayload, home);
                _carriedAmber = null;
            }
            else if (_carried is { } foodPayload)
            {
                world.DeliverFood(foodPayload, home);
                _carried = null;
            }
            // else: reached the Heart carrying nothing (shouldn't happen,
            // but falling through to StartWandering below instead of
            // crashing keeps a stray edge case harmless).

            TrespassingAgainst = null; // Got away with it — the theft is over either way.

            // Always go back through Walking rather than jumping straight
            // to Gathering here: that used to let a Gatherer loop
            // Gathering <-> Returning forever as long as any food was on
            // the map, permanently bypassing Update()'s priority chain (and
            // with it, the elevated Building-over-Gathering quota below) —
            // a Blueprint could never win it back. Walking/Pausing are both
            // re-evaluated by that chain every frame, so this costs at most
            // one frame before the right next job (Building or Gathering)
            // is picked.
            StartWandering(world);
            return;
        }

        _mover.MoveTowards(home.Center, EffectiveWalkSpeed(world), deltaTime, world, p => IsSafeSpot(p, world));
    }

    // --- Village Building (Builder AI) ---------------------------------------------

    private void UpdateBuilding(float deltaTime, World world)
    {
        // Re-pick the nearest Blueprint every frame: another Bramblekin may
        // have just finished ours, or a new one may have gone up closer.
        // AI Faction Loyalty: only ever our own faction's sites.
        Blueprint? blueprint = world.NearestIncompleteBlueprintFor(Position, FactionID);
        if (blueprint is null)
        {
            StartWandering(world);
            return;
        }

        float contactDistance = BodyRadius + Building.RadiusFor(blueprint.Kind) + BuildContactMargin;
        if (GroundMover.HorizontalDistance(Position, blueprint.Position) <= contactDistance)
        {
            bool inspired = world.VillageFor(FactionID)?.BuildersAreInspired ?? false;
            float rate = inspired ? World.HighMoraleBuildMultiplier : 1f;
            blueprint.AddProgress(rate * deltaTime);
            if (blueprint.IsComplete)
                world.CompleteBlueprint(blueprint);
            return;
        }

        // Blueprints/Buildings are never added to World.Obstacles (see
        // RebuildObstacles), so obstacle avoidance can't steer a Builder
        // away from the site it's trying to reach or push it back out once
        // it's standing on/inside the footprint above.
        _mover.MoveTowards(blueprint.Position, EffectiveWalkSpeed(world), deltaTime, world, p => IsSafeSpot(p, world));
    }

    // --- Militia states -------------------------------------------------------------

    private void UpdateDefending(float deltaTime, World world, VillageHeart? home)
    {
        // The Militia Leash: over-extended past MilitiaLeashDistance from
        // home, drop whatever's being chased and head straight back —
        // moves toward home directly (rather than only setting State and
        // waiting for the Walking case to pick it up next frame) so this
        // unit reliably makes progress home even if another priority-chain
        // branch (Border Wars, Base Razing) tries to re-claim it into
        // Defending again before it arrives; only once it's back inside
        // the leash does a fresh chase actually stick.
        if (home is not null && GroundMover.HorizontalDistanceSquared(Position, home.Center) > MilitiaLeashDistance * MilitiaLeashDistance)
        {
            _combatTarget = null;
            _target = home.Center;
            SetState(BramblekinState.Walking);
            _mover.MoveTowards(_target, EffectiveWalkSpeed(world), deltaTime, world, p => IsSafeSpot(p, world));
            return;
        }

        bool spiderInTerritory = home is not null && world.Spider is { } spiderCheck &&
                                  GroundMover.HorizontalDistanceSquared(spiderCheck.Position, home.Center) <= home.TerritoryRadius * home.TerritoryRadius;

        if (home is not null && spiderInTerritory)
        {
            _combatTarget = null;
            WolfSpider spider = world.Spider!;
            _target = ComputeInterceptPoint(spider, home);

            // Sustained Combat: close enough to jab it directly, dealing
            // PokeDamage (boosted to UpgradedPokeDamage once this specific
            // unit's own Fang Pike is equipped). A fast, per-unit cooldown lets
            // Militia wail on it rapidly, especially a Tumbled spider that can't
            // fight back. This is pure Health damage -- it never touches the
            // spider's State, so it can't wake a Tumbled spider early (see the
            // hard lock in WolfSpider.Update()).
            if (_pokeCooldown <= 0f && GroundMover.HorizontalDistance(Position, spider.Position) <= PokeRange)
            {
                world.DamageSpider(HasFangPike ? UpgradedPokeDamage : PokeDamage);
                _pokeCooldown = PokeCooldownDuration;
            }

            _mover.MoveTowards(_target, DefendSpeed, deltaTime, world, p => IsSafeSpot(p, world));
            return;
        }

        // Blood Feud Border Wars / Thievery Response: no Wolf Spider threat
        // in territory right now, so chase down whichever individual the
        // outer priority chain assigned us — a declared enemy (lethal) or a
        // caught Trespasser (a Warning Shove first, unless it escalates) —
        // as long as they're still alive and still within the territory
        // ring. Re-checked every frame since they may flee, die, or simply
        // wander back out.
        if (home is not null && _combatTarget is { IsDead: false } enemy &&
            GroundMover.HorizontalDistanceSquared(enemy.Position, home.Center) <= home.TerritoryRadius * home.TerritoryRadius)
        {
            _target = enemy.Position;

            if (_pokeCooldown <= 0f && GroundMover.HorizontalDistance(Position, enemy.Position) <= PokeRange)
            {
                if (home.HostileFactions.ContainsKey(enemy.FactionID))
                {
                    // Blood Feud: lethal. Same Sustained Combat mechanics as
                    // the spider fight above, just aimed at a rival
                    // Bramblekin's own Health — Spoils of War (see
                    // World.KillByBramblekin) applies only on the killing
                    // blow here, never on a plain spider Poke.
                    enemy.TakeDamage(HasFangPike ? UpgradedPokeDamage : PokeDamage, world, attackerFactionId: FactionID);
                }
                else if (enemy.Role == BramblekinRole.Militia)
                {
                    // The Blood Feud (armed trespasser): a Warning Shove
                    // doesn't work on an enemy soldier — open fire outright;
                    // TakeDamage's own attackerFactionId hook declares the
                    // war for us the instant the hit lands.
                    enemy.TakeDamage(HasFangPike ? UpgradedPokeDamage : PokeDamage, world, attackerFactionId: FactionID);
                }
                else
                {
                    // The Warning Shove: a caught Gatherer-thief gets 0
                    // damage and a shove home instead of a killing blow.
                    enemy.ReceiveWarningShove(Position, world);
                }

                _pokeCooldown = PokeCooldownDuration;
            }

            _mover.MoveTowards(_target, DefendSpeed, deltaTime, world, p => IsSafeSpot(p, world));
            return;
        }

        // Nothing left to fight: stand down.
        _combatTarget = null;
        StartWandering(world);
    }

    /// <summary>
    /// Blood Feud Base Razing: paths to and attacks an enemy Village Heart
    /// — the same Sustained Combat damage/cooldown numbers as UpdateDefending
    /// (PokeDamage/UpgradedPokeDamage/PokeCooldownDuration), but checked
    /// against the much more forgiving <see cref="BuildingAttackRange"/>
    /// rather than <see cref="PokeRange"/>, since a building's solid
    /// footprint (and a crowd of raiders jostling around it) never lets a
    /// walker actually reach its exact centre coordinate. Stands down the
    /// instant a living hostile Bramblekin shows up nearby (that always
    /// wins — the outer priority chain picks it up as Border Wars/home
    /// defense next frame instead) or the target Heart is razed (by this
    /// unit's own killing blow or anyone else's) or simply falls out of
    /// range. The Militia Leash: also breaks off (see UpdateDefending's own
    /// copy of this same check) if this unit itself has strayed past
    /// MilitiaLeashDistance from home, regardless of how close the target
    /// still is.
    /// </summary>
    private void UpdateRaiding(float deltaTime, World world, VillageHeart? home)
    {
        if (home is not null && GroundMover.HorizontalDistanceSquared(Position, home.Center) > MilitiaLeashDistance * MilitiaLeashDistance)
        {
            _raidTarget = null;
            _target = home.Center;
            SetState(BramblekinState.Walking);
            _mover.MoveTowards(_target, EffectiveWalkSpeed(world), deltaTime, world, p => IsSafeSpot(p, world));
            return;
        }

        float raidingReach = home?.TerritoryRadius ?? World.BaseTerritoryRadius;
        if (_raidTarget is null || !world.Villages.Contains(_raidTarget) ||
            GroundMover.HorizontalDistanceSquared(Position, _raidTarget.Center) > raidingReach * raidingReach ||
            world.HasLivingHostileBramblekinNear(Position, FactionID, raidingReach))
        {
            _raidTarget = null;
            StartWandering(world);
            return;
        }

        _target = _raidTarget.Center;

        if (_pokeCooldown <= 0f && GroundMover.HorizontalDistance(Position, _raidTarget.Center) <= BuildingAttackRange)
        {
            world.DamageVillageHeart(_raidTarget, HasFangPike ? UpgradedPokeDamage : PokeDamage, FactionID);
            _pokeCooldown = PokeCooldownDuration;
        }

        _mover.MoveTowards(_target, DefendSpeed, deltaTime, world, p => IsSafeSpot(p, world));
    }

    /// <summary>
    /// A point <see cref="InterceptStandoff"/> meters from the spider, on the
    /// side facing <paramref name="home"/> — the spot a Militia unit tries to
    /// hold to physically get between the spider and its Village Heart.
    /// </summary>
    private static Vector3 ComputeInterceptPoint(WolfSpider spider, VillageHeart home)
    {
        Vector2 toVillage = new(home.Center.X - spider.Position.X, home.Center.Z - spider.Position.Z);
        Vector2 direction = toVillage.LengthSquared() > 1e-6f ? Vector2.Normalize(toVillage) : Vector2.UnitX;
        return spider.Position + new Vector3(direction.X, 0, direction.Y) * InterceptStandoff;
    }

    private void UpdateHunting(float deltaTime, World world, VillageHeart? home)
    {
        // AI Time-Slicing (the "Brain"): re-picking the nearest live,
        // unclaimed Aphid scans the whole Aphids array, so — same as
        // UpdateGathering — it's only re-run on this unit's staggered
        // frame; the cached _claimedAphid keeps being chased every frame
        // in between.
        if (world.FrameCounter % 15 == ID % 15)
        {
            // The 20-Meter Territory Rule: nothing to hunt without a home village.
            // Another Militia unit may have already caught (or claimed) ours, or
            // it may simply have wandered off/out of territory.
            Aphid? aphid = home is null ? null : world.NearestLiveAphidNearVillage(Position, this, home);
            if (aphid != _claimedAphid)
            {
                ReleaseAphidClaim();
                _claimedAphid = aphid;
                if (aphid is not null)
                    aphid.ClaimedBy = this;
            }

            if (aphid is null)
            {
                StartWandering(world);
                return;
            }
        }

        if (_claimedAphid is not { } cachedAphid)
        {
            StartWandering(world);
            return;
        }

        if (GroundMover.HorizontalDistance(Position, cachedAphid.Position) <= HuntContactDistance)
        {
            world.KillAphid(cachedAphid);
            _claimedAphid = null;
            return; // Re-targets (or wanders) fresh next scan.
        }

        _mover.MoveTowards(cachedAphid.Position, HuntSpeed, deltaTime, world, p => IsSafeSpot(p, world));
    }

    // --- Individual Equipment (RPG-Style) --------------------------------------------

    /// <summary>
    /// Un-upgraded units prioritize gearing up: a Militia unit fetches the
    /// nearest Spider Fang and instantly equips <see cref="HasFangPike"/> on
    /// touch; a Gatherer fetches the nearest Chitin piece and equips
    /// <see cref="HasChitinMallet"/>. Either way the item despawns — no
    /// carrying it home, no shared/village-wide unlock.
    /// </summary>
    private void UpdateEquipping(float deltaTime, World world)
    {
        if (Role == BramblekinRole.Militia)
        {
            if (HasFangPike)
            {
                StartWandering(world);
                return;
            }

            SpiderFang? fang = world.NearestAvailableFang(Position);
            if (fang is null)
            {
                StartWandering(world);
                return;
            }

            if (GroundMover.HorizontalDistance(Position, fang.Position) <= FangPickupDistance)
            {
                world.ConsumeFang(fang);
                HasFangPike = true;
                StartWandering(world);
                return;
            }

            _mover.MoveTowards(fang.Position, EffectiveWalkSpeed(world), deltaTime, world, p => IsSafeSpot(p, world));
            return;
        }

        if (HasChitinMallet)
        {
            StartWandering(world);
            return;
        }

        Chitin? chitin = world.NearestAvailableChitin(Position);
        if (chitin is null)
        {
            StartWandering(world);
            return;
        }

        if (GroundMover.HorizontalDistance(Position, chitin.Position) <= ChitinPickupDistance)
        {
            world.ConsumeChitin(chitin);
            HasChitinMallet = true;
            StartWandering(world);
            return;
        }

        _mover.MoveTowards(chitin.Position, EffectiveWalkSpeed(world), deltaTime, world, p => IsSafeSpot(p, world));
    }

    // --- The Schism -------------------------------------------------------------------

    /// <summary>
    /// A Pioneer's entire world while Migrating: path straight for its
    /// Migration's Target, oblivious to food, blueprints and the Wolf
    /// Spider alike (see the hard lock at the top of Update()).
    /// The moment ANY Pioneer bound to the same Migration founds the new
    /// Village Heart — not just this one — it drops Migrating for good on
    /// this check and reverts to ordinary AI; since its FactionID already
    /// matches that Village Heart, the very next priority-chain evaluation
    /// has it defending, gathering or building for it like any other unit.
    /// </summary>
    private void UpdateMigrating(float deltaTime, World world)
    {
        if (_migration is not { } migration)
        {
            // Defensive: BecomePioneer always sets this, but don't strand a
            // Bramblekin in Migrating forever if it somehow didn't.
            StartWandering(world);
            return;
        }

        if (migration.Founded)
        {
            _migration = null;
            StartWandering(world);
            return;
        }

        if (GroundMover.HorizontalDistance(Position, migration.Target) <= MigrationArriveDistance)
        {
            world.FoundVillage(migration);
            _migration = null;
            StartWandering(world);
            return;
        }

        _mover.MoveTowards(migration.Target, EffectiveWalkSpeed(world), deltaTime, world, p => IsSafeSpot(p, world));
    }

    // --- Wandering ----------------------------------------------------------------

    private void StartWandering(World world)
    {
        // The Militia Leash: a Militia unit's default wander destination
        // never drifts outside its own borders, unlike a Gatherer's
        // map-wide roam — strictly within its own Village Heart's Cultural
        // Borders (wealth-scaled, see VillageHeart.TerritoryRadius). Falls
        // back to standing at home outright
        // (never the map-wide point below) if nothing opens up nearby, and
        // to the ordinary map-wide wander if it has no home at all (a
        // homeless refugee, e.g. after Base Razing, has no border left to
        // keep).
        VillageHeart? home = Role == BramblekinRole.Militia ? world.VillageFor(FactionID) : null;
        if (home is not null)
        {
            _target = world.RandomPointNearVillage(home, home.TerritoryRadius, BodyRadius + 0.1f) ?? home.Center;
            SetState(BramblekinState.Walking);
            return;
        }

        // No-spawn zones: RandomFreePoint never picks a spot inside the village.
        _target = world.RandomFreePoint(BodyRadius + 0.1f, EdgeMargin);
        SetState(BramblekinState.Walking);
    }

    /// <summary>
    /// Maximum Search Radius: a Gatherer's own equivalent of the Militia
    /// Leash above, used specifically when <see cref="World.NearestAvailableShard"/>/
    /// <see cref="World.NearestClaimableAcorn"/> come back completely empty
    /// (nothing within <see cref="World.MaxGatherSearchRadius"/>) rather
    /// than the ordinary map-wide <see cref="StartWandering"/>. Waits close
    /// to <paramref name="home"/> instead — its own Spore Farm's next Berry
    /// is what actually fixes this, not a long walk toward a target that
    /// doesn't exist. Falls back to the ordinary map-wide wander if it has
    /// no home at all (a homeless refugee).
    /// </summary>
    private void StartWanderingNearHome(World world, VillageHeart? home)
    {
        if (home is not null)
        {
            _target = world.RandomPointNearVillage(home, home.TerritoryRadius, BodyRadius + 0.1f) ?? home.Center;
            SetState(BramblekinState.Walking);
            return;
        }

        StartWandering(world);
    }

    private void StartPause()
    {
        SetState(BramblekinState.Pausing);
        _pauseTimer = PauseDuration;
    }

    /// <summary>
    /// Dibs: changing to any state other than the one that owns a given
    /// claim releases it — the single hook every transition already goes
    /// through, so a Gatherer scared off mid-Gathering (Fleeing), promoted
    /// to Militia (Building/Gathering -> Pausing), or simply re-tasked to
    /// Building doesn't leave a shard/Aphid/Acorn locked out forever. The
    /// Acorn claim specifically survives a Gathering &lt;-&gt; Cracking
    /// transition either way — those two states are just "chasing an
    /// Acorn" and "actively cracking it," not a change of target.
    /// </summary>
    private void SetState(BramblekinState state)
    {
        if (state != BramblekinState.Gathering)
        {
            ReleaseFoodClaim();
            ReleaseAmberClaim();
        }
        if (state != BramblekinState.Gathering && state != BramblekinState.Cracking)
            ReleaseAcornClaim();
        if (state != BramblekinState.Hunting)
            ReleaseAphidClaim();

        State = state;
        _mover.ResetProgress();
    }

    // --- Fleeing ------------------------------------------------------------------

    /// <summary>A safe spot directly away from a predator, well outside its Fear Aura.</summary>
    private Vector3 FindPointAwayFrom(Vector3 predator, World world) =>
        FindSafePointAround(predator, FearRadius + PredatorFleeMargin, world);

    /// <summary>
    /// Finds a safe point <paramref name="distance"/> meters from
    /// <paramref name="danger"/>, ideally straight away from it (the shortest
    /// escape). If that point is off the terrain, under a shadow or inside a
    /// rock, it tries directions progressively further round the circle,
    /// alternating left and right.
    /// </summary>
    private Vector3 FindSafePointAround(Vector3 danger, float distance, World world)
    {
        float awayX = Position.X - danger.X;
        float awayZ = Position.Z - danger.Z;

        // Standing dead centre: any direction is as good as another.
        float baseAngle = awayX * awayX + awayZ * awayZ > 1e-6f
            ? MathF.Atan2(awayZ, awayX)
            : (float)(_rng.NextDouble() * MathF.Tau);

        const int steps = 12;                      // 30° increments.
        const float stepAngle = MathF.Tau / steps;

        for (int i = 0; i <= steps / 2; i++)
        {
            foreach (int side in i == 0 ? new[] { 1 } : new[] { 1, -1 })
            {
                float angle = baseAngle + side * i * stepAngle;
                var candidate = new Vector3(
                    danger.X + MathF.Cos(angle) * distance,
                    Terrain.GroundHeight,
                    danger.Z + MathF.Sin(angle) * distance);

                if (world.Terrain.Contains(candidate, EdgeMargin) && IsSafeSpot(candidate, world))
                    return candidate;
            }
        }

        // Boxed in (e.g. overlapping shadows in a corner): run straight away
        // and hope. Clamp so it at least stays on the terrain.
        float half = world.Terrain.Size / 2f - EdgeMargin;
        return new Vector3(
            Math.Clamp(danger.X + MathF.Cos(baseAngle) * distance, -half, half),
            Terrain.GroundHeight,
            Math.Clamp(danger.Z + MathF.Sin(baseAngle) * distance, -half, half));
    }

    /// <summary>Not inside a rock or the village.</summary>
    private static bool IsSafeSpot(Vector3 point, World world) =>
        !world.IsBlocked(point, BodyRadius);
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
/// food lures the next gatherer in, and the colony dies in a chain.
///
/// This is a pure, zero-intervention simulation: there is no player lever
/// left to pull against it. Its only counter is Sustained Combat — see
/// Bramblekin's Defending state, Militia close in and Poke it (damage, on a
/// fast cooldown) whenever they're in range. When a Militia unit gets close
/// enough it Bites back on its own cooldown. Health reaching 0, from either
/// side, is death.
/// </summary>
public sealed class WolfSpider
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

    /// <summary>Sustained Combat: how close a Militia unit must be for the spider to Bite it.</summary>
    private const float BiteRange = 1.5f;

    /// <summary>Bite damage dealt to the nearest Militia unit in range.</summary>
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

    public Vector3 Position => _mover.Position;

    public SpiderState State { get; private set; } = SpiderState.Prowling;

    /// <summary>Bramblekin killed so far.</summary>
    public int Kills { get; private set; }

    /// <summary>Hit points out of <see cref="MaxHealth"/>.</summary>
    public int Health { get; private set; } = MaxHealth;

    public WolfSpider(Vector3 position, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, edgeMargin: 1f, rng);
        _target = position;
        _timer = ProwlPauseDuration;
    }

    /// <summary>
    /// Sustained Combat: a Militia poke's damage. Purely a Health mutation —
    /// never touches State — so it can never wake a Tumbled spider early
    /// (see the hard lock at the top of Update()). Death itself (Health
    /// reaching 0) is World's call, not this method's: see World.DamageSpider.
    /// </summary>
    public void TakeDamage(int amount) => Health = Math.Max(0, Health - amount);

    public void Update(float deltaTime, World world)
    {
        _mover.Idle();

        // Tumbled is a dormant hard lock (nothing triggers it any more, now
        // that the player's Gust is gone), checked and handled before
        // anything else in this method — the prey safety net, the Bite
        // retaliation below, every bit of vision/AI. Nothing can
        // re-target, re-notice, retaliate or otherwise step on the stun
        // early — not even taking Poke damage (TakeDamage is a pure Health
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
        // Militia unit in range on its own cooldown, regardless of what else
        // it's otherwise doing (prowling, hunting, even mid-pounce) — a
        // reflex, not a deliberate target choice the way Hunt/Pounce are.
        _biteCooldown = MathF.Max(0f, _biteCooldown - deltaTime);
        if (_biteCooldown <= 0f && NearestMilitiaInRange(world, BiteRange) is { } target)
        {
            target.TakeDamage(BiteDamage, world);
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

        // Anything it touches mid-pounce: only a Gatherer is caught this way
        // (Pounce is the spider hunting vibrating prey, and Militia never
        // vibrate — see IsVibrating). A Militia unit that happens to be
        // standing in the way no longer blocks or interrupts the pounce;
        // sustained Militia-vs-spider combat is handled entirely by the
        // Poke/Bite exchange in UpdateDefending/Update instead. Reverse
        // for-loop: World.Kill only queues the removal now, so Colony never
        // actually changes size during this walk, but the pattern stays
        // consistent everywhere.
        Bramblekin? gathererHit = null;
        for (int i = world.Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = world.Colony[i];
            if (bramblekin.IsDead || bramblekin.Role != BramblekinRole.Gatherer)
                continue;

            if (GroundMover.HorizontalDistance(Position, bramblekin.Position) >= BodyRadius + Bramblekin.BodyRadius)
                continue;

            gathererHit = bramblekin;
            break;
        }

        if (gathererHit is not null)
        {
            world.Kill(gathererHit);
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

    /// <summary>The nearest living Militia unit within <paramref name="range"/>, if any — the Bite's target.</summary>
    private Bramblekin? NearestMilitiaInRange(World world, float range)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = range * range;
        // The Spatial Grid: only the Colony chunks around this spider.
        List<Bramblekin> nearby = world.QueryNearbyColony(Position);
        for (int i = nearby.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = nearby[i];
            if (bramblekin.IsDead || bramblekin.Role != BramblekinRole.Militia)
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
//  Ambient Prey: the Aphid
// =============================================================================

/// <summary>
/// Harmless background wildlife: wanders very slowly, skitters weakly away
/// from anything that gets too close, and offers no resistance to a Militia
/// unit that catches it. Not part of the economy on its own — Militia hunting
/// one turns it into Food Shards for the Gatherers to collect.
/// </summary>
public sealed class Aphid
{
    /// <summary>Collision/body radius in meters — smaller than a Bramblekin.</summary>
    public const float BodyRadius = 0.15f;

    /// <summary>Total body height in meters.</summary>
    public const float BodyHeight = 0.28f;

    /// <summary>How far from the terrain edge it wanders, in meters.</summary>
    public const float EdgeMargin = 0.4f;

    private const float WanderSpeed = 0.3f;
    private const float FleeSpeed = 0.5f;
    private const float PauseDuration = 1.5f;

    /// <summary>A Bramblekin closer than this (m) spooks it into a weak flee.</summary>
    private const float FleeTriggerRadius = 1.5f;

    /// <summary>How far past the trigger radius it tries to put between itself and the threat.</summary>
    private const float FleeMargin = 1f;

    private static readonly Color BodyColor = new(95, 165, 70, 255);

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private Vector3 _target;
    private float _pauseTimer;

    /// <summary>Feet position on the ground (y = GroundHeight).</summary>
    public Vector3 Position => _mover.Position;

    /// <summary>True once caught by a Militia unit. Removal from World.Aphids is deferred to the end of the frame.</summary>
    public bool IsDead { get; private set; }

    /// <summary>
    /// Dibs: the one Militia unit currently hunting this Aphid, if any — see
    /// <see cref="World.NearestLiveAphidNearVillage"/>. Only meaningful
    /// while that Militia unit's own State is actually Hunting; it's
    /// released (see Bramblekin.SetState) the moment that stops being true.
    /// </summary>
    public Bramblekin? ClaimedBy { get; set; }

    public Aphid(Vector3 position, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);
        _pauseTimer = (float)rng.NextDouble() * PauseDuration;
    }

    /// <summary>Marks it caught. Called once, from World.KillAphid.</summary>
    public void MarkDead() => IsDead = true;

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return;

        _mover.Idle();

        Bramblekin? threat = NearestCloseBramblekin(world);
        if (threat is not null)
        {
            // Re-aimed every frame while something is close, same as a
            // Bramblekin's own Fear Aura response, just much gentler.
            _target = FleeTarget(threat.Position, world);
            _mover.MoveTowards(_target, FleeSpeed, deltaTime, world, p => world.Terrain.Contains(p, EdgeMargin));
            return;
        }

        if (_pauseTimer > 0f)
        {
            _pauseTimer -= deltaTime;
            if (_pauseTimer <= 0f)
                _target = world.RandomFreePoint(BodyRadius + 0.05f, EdgeMargin);
            return;
        }

        if (_mover.MoveTowards(_target, WanderSpeed, deltaTime, world, p => world.Terrain.Contains(p, EdgeMargin)))
            _pauseTimer = PauseDuration;
    }

    private Bramblekin? NearestCloseBramblekin(World world)
    {
        Bramblekin? nearest = null;
        float bestDistanceSquared = FleeTriggerRadius * FleeTriggerRadius;
        // The Spatial Grid: only the Colony chunks around this Aphid.
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

    private Vector3 FleeTarget(Vector3 threat, World world)
    {
        float dx = Position.X - threat.X;
        float dz = Position.Z - threat.Z;
        float angle = dx * dx + dz * dz > 1e-6f
            ? MathF.Atan2(dz, dx)
            : (float)(_rng.NextDouble() * MathF.Tau);

        float distance = FleeTriggerRadius + FleeMargin;
        float half = world.Terrain.Size / 2f - EdgeMargin;
        return new Vector3(
            Math.Clamp(threat.X + MathF.Cos(angle) * distance, -half, half),
            Terrain.GroundHeight,
            Math.Clamp(threat.Z + MathF.Sin(angle) * distance, -half, half));
    }

    public void Draw()
    {
        var bottom = Position + new Vector3(0, BodyRadius * 0.8f, 0);
        var top = Position + new Vector3(0, BodyHeight - BodyRadius * 0.8f, 0);
        Raylib.DrawCapsule(bottom, top, BodyRadius, 6, 3, BodyColor);
        Raylib.DrawCapsuleWires(bottom, top, BodyRadius, 6, 3, new Color(0, 0, 0, 40));
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
