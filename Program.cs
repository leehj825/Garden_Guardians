// =============================================================================
//  Garden Guardians — Phase 1 Touch-Physics Prototype
// -----------------------------------------------------------------------------
//  Goal of this prototype (see Garden_Guardians_Roadmap.md, Phase 1 / Task 1):
//    * A fixed isometric camera looking down at a patch of backyard "terrain".
//    * A tiny hand-rolled physics loop (Raylib has no rigidbodies).
//    * A two-state input model: click the "Equip Pebble" button, then click the
//      ground to cast the Pebble-Drop miracle at that spot.
//
//  Scale convention: 1 world unit = 1 meter. The terrain is a 20 m x 20 m plane
//  centred on the origin, and "up" is +Y. Gravity is 9.8 m/s² downwards.
//
//  Everything lives in this single file for now; each class is small and
//  self-contained so it can be lifted into its own file once the prototype
//  graduates into the real project structure.
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
/// Owns the window and the main loop, and wires the individual systems
/// (camera, terrain, physics, input, UI) together. Platform-independent.
/// </summary>
public static class Game
{
    // Window settings. Kept as constants so they are easy to find and tweak.
    // On Android this is a *virtual* resolution: raylib scales it to fill the
    // display (letterboxing if the aspect ratio differs) and maps touches back
    // into these coordinates, so UI positions work unchanged on any phone.
    // Width > height also tells raylib to lock the activity to landscape.
    private const int ScreenWidth = 1280;
    private const int ScreenHeight = 720;
    private const int TargetFps = 60;

    public static void Run(GamePlatform platform)
    {
        if (platform == GamePlatform.Desktop)
        {
            // MSAA smooths the edges of the spheres and grid lines; resizable
            // lets us test different aspect ratios. Both are skipped on Android,
            // where not every GPU offers a 4x MSAA surface.
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow);
        }

        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Garden Guardians — Phase 1 Touch-Physics Prototype");
        Raylib.SetTargetFPS(TargetFps);

        // --- Build the world -------------------------------------------------
        var camera = IsometricCamera.Create(target: Vector3.Zero, distance: 30f);
        var terrain = new Terrain(size: 20f);
        var physics = new PhysicsManager();
        var input = new MiracleInput();
        var equipButton = new UiButton(new Rectangle(20, 20, 180, 50));

        // --- Main loop -------------------------------------------------------
        while (!Raylib.WindowShouldClose())
        {
            float deltaTime = Raylib.GetFrameTime();

            // 1) Input: UI gets first pick of the click so that pressing the
            //    button never also drops a pebble "through" it onto the ground.
            input.Update(camera, terrain, physics, equipButton);

            // 2) Simulation.
            physics.Update(deltaTime);

            // 3) Rendering.
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(135, 190, 235, 255)); // Sky blue.

            Raylib.BeginMode3D(camera);
            terrain.Draw();
            physics.Draw();
            input.DrawCursorPreview(camera, terrain);
            Raylib.EndMode3D();

            // 2D overlay (UI) is drawn after EndMode3D so it sits on top.
            equipButton.Draw(input.State == InputState.PebbleEquipped ? "Pebble Equipped" : "Equip Pebble",
                             highlighted: input.State == InputState.PebbleEquipped);
            DrawHud(input, physics);

            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }

    /// <summary>Small help text and debug counters in the bottom-left corner.</summary>
    private static void DrawHud(MiracleInput input, PhysicsManager physics)
    {
        int y = Raylib.GetScreenHeight() - 60;
        string hint = input.State == InputState.PebbleEquipped
            ? "Click the ground to drop the pebble."
            : "Click 'Equip Pebble', then click the ground.";
        Raylib.DrawText(hint, 20, y, 20, Color.DarkGray);
        Raylib.DrawText($"Pebbles: {physics.Count}   FPS: {Raylib.GetFPS()}", 20, y + 26, 20, Color.DarkGray);
    }
}

// =============================================================================
//  Camera
// =============================================================================

/// <summary>
/// Builds a fixed isometric-style perspective camera.
/// </summary>
public static class IsometricCamera
{
    /// <summary>
    /// Creates a camera that looks down at <paramref name="target"/> from a
    /// 45° elevation, rotated 45° around the vertical axis (the classic
    /// isometric diagonal view).
    /// </summary>
    /// <param name="target">World point the camera looks at.</param>
    /// <param name="distance">Straight-line distance from camera to target, in meters.</param>
    public static Camera3D Create(Vector3 target, float distance)
    {
        // A 45° pitch means the camera's height equals its horizontal distance
        // from the target: h = d·sin(45°), horizontal = d·cos(45°).
        float height = distance * MathF.Sin(MathF.PI / 4f);
        float horizontal = distance * MathF.Cos(MathF.PI / 4f);

        // A 45° yaw splits the horizontal distance equally between X and Z.
        float xz = horizontal * MathF.Cos(MathF.PI / 4f);

        return new Camera3D
        {
            Position = target + new Vector3(xz, height, xz),
            Target = target,
            Up = Vector3.UnitY,
            FovY = 45f,                                   // Degrees, vertical.
            Projection = CameraProjection.Perspective,
        };
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
    /// Intersects a ray with the infinite horizontal plane y = GroundHeight.
    /// Returns null if the ray is parallel to the plane, points away from it,
    /// or hits it outside the terrain bounds.
    /// </summary>
    public Vector3? Raycast(Ray ray)
    {
        // Plane: y = GroundHeight. Ray: P(t) = origin + t·direction.
        // Solve origin.y + t·direction.y = GroundHeight for t.
        if (MathF.Abs(ray.Direction.Y) < 1e-6f)
            return null; // Parallel to the ground — never intersects.

        float t = (GroundHeight - ray.Position.Y) / ray.Direction.Y;
        if (t < 0f)
            return null; // Intersection is behind the camera.

        Vector3 hit = ray.Position + ray.Direction * t;
        return Contains(hit) ? hit : null;
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
//  Physics
// =============================================================================

/// <summary>
/// A minimal rigid body: a sphere with a position, a velocity and a radius.
/// There is no rotation or inter-object collision yet — just enough to make
/// things fall and land on the terrain.
/// </summary>
public sealed class PhysicsObject
{
    public Vector3 Position;
    public Vector3 Velocity;

    /// <summary>Sphere radius in meters.</summary>
    public float Radius { get; }

    /// <summary>Mass in kilograms. Unused for now; reserved for impact/damage calculations.</summary>
    public float Mass { get; }

    public Color Color { get; }

    /// <summary>True once the object has come to rest on the terrain.</summary>
    public bool IsGrounded { get; private set; }

    public PhysicsObject(Vector3 position, float radius, float mass, Color color)
    {
        Position = position;
        Velocity = Vector3.Zero;
        Radius = radius;
        Mass = mass;
        Color = color;
    }

    /// <summary>
    /// Advances the object by one time step using semi-implicit Euler
    /// integration: update velocity from acceleration first, then position
    /// from the new velocity. This is more stable than plain Euler at no cost.
    /// </summary>
    public void Integrate(float deltaTime, float gravity)
    {
        if (IsGrounded)
            return; // Resting objects are frozen until something pushes them.

        Velocity.Y -= gravity * deltaTime;
        Position += Velocity * deltaTime;

        ResolveGroundCollision();
    }

    /// <summary>
    /// Stops the object when it reaches the terrain. We test the *bottom* of
    /// the sphere (centre minus radius) so it rests on the ground instead of
    /// sinking halfway into it.
    /// </summary>
    private void ResolveGroundCollision()
    {
        float bottom = Position.Y - Radius;
        if (bottom <= Terrain.GroundHeight)
        {
            Position.Y = Terrain.GroundHeight + Radius; // Snap onto the surface.
            Velocity = Vector3.Zero;                     // No bounce (yet).
            IsGrounded = true;
        }
    }

    public void Draw()
    {
        Raylib.DrawSphere(Position, Radius, Color);
        Raylib.DrawSphereWires(Position, Radius, 8, 8, new Color(0, 0, 0, 60));
    }
}

/// <summary>
/// Owns every simulated object and steps them each frame.
/// </summary>
public sealed class PhysicsManager
{
    /// <summary>Gravitational acceleration in m/s² (Earth, since 1 unit = 1 m).</summary>
    public const float Gravity = 9.8f;

    /// <summary>
    /// Largest time step we will simulate in one go. If the game hitches
    /// (window dragged, breakpoint hit) a huge deltaTime could tunnel objects
    /// straight through the ground; clamping keeps the simulation sane.
    /// </summary>
    private const float MaxDeltaTime = 1f / 20f;

    private readonly List<PhysicsObject> _objects = new();

    public int Count => _objects.Count;

    public void Add(PhysicsObject obj) => _objects.Add(obj);

    public void Update(float deltaTime)
    {
        float dt = MathF.Min(deltaTime, MaxDeltaTime);
        foreach (var obj in _objects)
            obj.Integrate(dt, Gravity);
    }

    public void Draw()
    {
        foreach (var obj in _objects)
        {
            // While an object is falling, draw a dark disc on the ground under
            // it. This is the first sketch of the "God's Shadow" telegraph from
            // the design doc, and it also helps judge depth from the iso view.
            if (!obj.IsGrounded)
                DrawDropShadow(obj);

            obj.Draw();
        }
    }

    private static void DrawDropShadow(PhysicsObject obj)
    {
        var groundPoint = new Vector3(obj.Position.X, Terrain.GroundHeight + 0.02f, obj.Position.Z);
        // A very flat cylinder makes a cheap filled disc lying on the ground.
        Raylib.DrawCylinder(groundPoint, obj.Radius * 1.2f, obj.Radius * 1.2f, 0.01f, 24, new Color(0, 0, 0, 110));
    }
}

// =============================================================================
//  Input & Miracles
// =============================================================================

/// <summary>The player's current "hand" state.</summary>
public enum InputState
{
    /// <summary>Nothing equipped; clicking the ground does nothing.</summary>
    Idle,

    /// <summary>The Pebble-Drop miracle is armed; the next ground click casts it.</summary>
    PebbleEquipped,
}

/// <summary>
/// Translates mouse/touch clicks into miracles. Holds the equip state and
/// knows how to turn a screen position into a point on the terrain.
/// </summary>
public sealed class MiracleInput
{
    /// <summary>How high above the clicked point the pebble spawns, in meters.</summary>
    private const float PebbleSpawnHeight = 10f;

    /// <summary>Pebble radius in meters (oversized for the prototype so it reads clearly).</summary>
    private const float PebbleRadius = 0.5f;

    /// <summary>Pebble mass in kilograms (placeholder for future impact damage).</summary>
    private const float PebbleMass = 2f;

    public InputState State { get; private set; } = InputState.Idle;

    /// <summary>
    /// Processes this frame's click, if any. The UI button is checked first and
    /// "consumes" the click, so a single click never both equips and drops.
    /// </summary>
    public void Update(Camera3D camera, Terrain terrain, PhysicsManager physics, UiButton equipButton)
    {
        // Raylib maps a primary touch to the left mouse button, so this same
        // code path will serve touch input on mobile later.
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left))
            return;

        Vector2 mouse = Raylib.GetMousePosition();

        // --- UI layer ------------------------------------------------------
        if (equipButton.Contains(mouse))
        {
            // Toggle: clicking the button again un-equips the pebble.
            State = State == InputState.PebbleEquipped ? InputState.Idle : InputState.PebbleEquipped;
            return;
        }

        // --- World layer ---------------------------------------------------
        if (State != InputState.PebbleEquipped)
            return;

        Vector3? groundPoint = PickGround(camera, terrain, mouse);
        if (groundPoint is null)
            return; // Clicked the sky or off the edge of the terrain: stay equipped.

        CastPebbleDrop(physics, groundPoint.Value);
        State = InputState.Idle; // One pebble per equip.
    }

    /// <summary>
    /// The Raycast Miracle: spawns a pebble 10 m above the target point and
    /// hands it to the physics loop, which lets gravity do the rest.
    /// </summary>
    private static void CastPebbleDrop(PhysicsManager physics, Vector3 groundPoint)
    {
        var spawn = groundPoint + new Vector3(0, PebbleSpawnHeight, 0);
        physics.Add(new PhysicsObject(spawn, PebbleRadius, PebbleMass, Color.Gray));
    }

    /// <summary>
    /// Casts a ray from the camera through the given screen position and
    /// returns where it meets the terrain (or null if it misses).
    /// </summary>
    private static Vector3? PickGround(Camera3D camera, Terrain terrain, Vector2 screenPosition)
    {
        // GetScreenToWorldRay is raylib 5.5's name for GetMouseRay (the old
        // name still exists but is marked obsolete in Raylib-cs 8).
        Ray ray = Raylib.GetScreenToWorldRay(screenPosition, camera);
        return terrain.Raycast(ray);
    }

    /// <summary>
    /// While a pebble is equipped, draws a targeting ring where it would land
    /// so the player can see what they are aiming at.
    /// </summary>
    public void DrawCursorPreview(Camera3D camera, Terrain terrain)
    {
        if (State != InputState.PebbleEquipped)
            return;

        Vector3? target = PickGround(camera, terrain, Raylib.GetMousePosition());
        if (target is null)
            return;

        var p = target.Value + new Vector3(0, 0.02f, 0); // Lift slightly to avoid z-fighting.
        Raylib.DrawCircle3D(p, PebbleRadius, Vector3.UnitX, 90f, Color.Yellow);
        Raylib.DrawLine3D(p, p + new Vector3(0, PebbleSpawnHeight, 0), new Color(255, 255, 0, 80));
    }
}

// =============================================================================
//  UI
// =============================================================================

/// <summary>A minimal clickable rectangle with a centred text label.</summary>
public sealed class UiButton
{
    private const int FontSize = 20;

    public Rectangle Bounds { get; }

    public UiButton(Rectangle bounds) => Bounds = bounds;

    public bool Contains(Vector2 point) => Raylib.CheckCollisionPointRec(point, Bounds);

    public void Draw(string label, bool highlighted)
    {
        bool hovered = Contains(Raylib.GetMousePosition());

        Color fill = highlighted ? new Color(230, 190, 60, 255)   // Gold when armed.
                   : hovered ? new Color(245, 245, 245, 255)      // Light on hover.
                   : new Color(220, 220, 220, 255);               // Default.

        Raylib.DrawRectangleRec(Bounds, fill);
        Raylib.DrawRectangleLinesEx(Bounds, 2f, Color.DarkGray);

        // Centre the label inside the rectangle.
        int textWidth = Raylib.MeasureText(label, FontSize);
        int x = (int)(Bounds.X + (Bounds.Width - textWidth) / 2f);
        int y = (int)(Bounds.Y + (Bounds.Height - FontSize) / 2f);
        Raylib.DrawText(label, x, y, FontSize, Color.Black);
    }
}
