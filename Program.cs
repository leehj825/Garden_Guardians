// =============================================================================
//  Garden Guardians — Phase 1 Touch-Physics Prototype
// -----------------------------------------------------------------------------
//  Goal of this prototype (see Garden_Guardians_Roadmap.md, Phase 1 / Task 1):
//    * A fixed isometric camera looking down at a patch of backyard "terrain".
//    * A tiny hand-rolled physics loop (Raylib has no rigidbodies).
//    * A two-state input model: click the "Equip Pebble" button, then click the
//      ground to cast the Pebble-Drop miracle at that spot.
//    * The God's Shadow: a cast pebble is telegraphed by a dark shadow on the
//      ground for 1.5 s before it actually drops.
//    * A small colony of Bramblekin that wander the terrain and scurry out of
//      any God's Shadow at 3x speed (see Garden_Guardians_Design.md).
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

    /// <summary>How many Bramblekin the colony starts with.</summary>
    private const int ColonySize = 8;

    /// <summary>
    /// Largest time step the game simulates in one frame. A hitch (window
    /// dragged, app resumed) would otherwise teleport walkers and tunnel
    /// pebbles through the ground.
    /// </summary>
    private const float MaxDeltaTime = 1f / 20f;

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
        var miracles = new MiracleManager();
        var input = new MiracleInput();
        var equipButton = new UiButton(new Rectangle(20, 20, 180, 50));

        var rng = new Random();
        var colony = new List<Bramblekin>();
        for (int i = 0; i < ColonySize; i++)
            colony.Add(new Bramblekin(terrain.RandomPoint(rng, Bramblekin.EdgeMargin), rng));

        // --- Main loop -------------------------------------------------------
        while (!Raylib.WindowShouldClose())
        {
            float deltaTime = MathF.Min(Raylib.GetFrameTime(), MaxDeltaTime);

            // 1) Input: UI gets first pick of the click so that pressing the
            //    button never also drops a pebble "through" it onto the ground.
            //    A ground click queues a God's Shadow rather than a pebble.
            input.Update(camera, terrain, miracles, equipButton);

            // 2) Simulation. Miracles first, so a shadow cast this frame is
            //    already visible to the Bramblekin deciding where to run.
            miracles.Update(deltaTime, physics);
            foreach (var bramblekin in colony)
                bramblekin.Update(deltaTime, terrain, miracles.ActiveShadows);
            physics.Update(deltaTime);

            // 3) Rendering.
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(135, 190, 235, 255)); // Sky blue.

            Raylib.BeginMode3D(camera);
            terrain.Draw();
            miracles.Draw();
            foreach (var bramblekin in colony)
                bramblekin.Draw();
            physics.Draw();
            input.DrawCursorPreview(camera, terrain);
            Raylib.EndMode3D();

            // 2D overlay (UI) is drawn after EndMode3D so it sits on top.
            equipButton.Draw(input.State == InputState.PebbleEquipped ? "Pebble Equipped" : "Equip Pebble",
                             highlighted: input.State == InputState.PebbleEquipped);
            DrawHud(input, physics, colony);

            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }

    /// <summary>Small help text and debug counters in the bottom-left corner.</summary>
    private static void DrawHud(MiracleInput input, PhysicsManager physics, List<Bramblekin> colony)
    {
        int fleeing = colony.Count(b => b.State == BramblekinState.Fleeing);
        int y = Raylib.GetScreenHeight() - 60;
        string hint = input.State == InputState.PebbleEquipped
            ? "Click the ground to drop the pebble."
            : "Click 'Equip Pebble', then click the ground.";
        Raylib.DrawText(hint, 20, y, 20, Color.DarkGray);
        Raylib.DrawText($"Pebbles: {physics.Count}   Bramblekin: {colony.Count} ({fleeing} fleeing)   FPS: {Raylib.GetFPS()}",
                        20, y + 26, 20, Color.DarkGray);
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
            // While an object is falling, draw a small dark disc on the ground
            // under it. It helps judge height from the isometric view (the
            // bigger God's Shadow telegraph is drawn by MiracleManager).
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
    public InputState State { get; private set; } = InputState.Idle;

    /// <summary>
    /// Processes this frame's click, if any. The UI button is checked first and
    /// "consumes" the click, so a single click never both equips and drops.
    /// </summary>
    public void Update(Camera3D camera, Terrain terrain, MiracleManager miracles, UiButton equipButton)
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

        // Don't drop yet: cast the God's Shadow first. MiracleManager spawns
        // the pebble when the shadow's timer runs out.
        miracles.QueuePebbleDrop(groundPoint.Value);
        State = InputState.Idle; // One pebble per equip.
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
    /// While a pebble is equipped, draws where it would land: the outer ring is
    /// the God's Shadow that will scare Bramblekin away, the inner ring the
    /// pebble itself.
    /// </summary>
    public void DrawCursorPreview(Camera3D camera, Terrain terrain)
    {
        if (State != InputState.PebbleEquipped)
            return;

        Vector3? target = PickGround(camera, terrain, Raylib.GetMousePosition());
        if (target is null)
            return;

        var p = target.Value + new Vector3(0, 0.02f, 0); // Lift slightly to avoid z-fighting.
        Raylib.DrawCircle3D(p, MiracleManager.ShadowRadius, Vector3.UnitX, 90f, new Color(255, 255, 0, 140));
        Raylib.DrawCircle3D(p, MiracleManager.PebbleRadius, Vector3.UnitX, 90f, Color.Yellow);
        Raylib.DrawLine3D(p, p + new Vector3(0, MiracleManager.PebbleSpawnHeight, 0), new Color(255, 255, 0, 80));
    }
}

// =============================================================================
//  Miracles: the God's Shadow telegraph
// =============================================================================

/// <summary>
/// A pending Pebble-Drop: a dark circle on the ground that warns the
/// Bramblekin a pebble is about to fall there. While it exists, any Bramblekin
/// under it drops what it is doing and scurries out.
/// </summary>
public sealed class GodShadow
{
    /// <summary>Centre of the shadow on the ground (y = GroundHeight).</summary>
    public Vector3 Center { get; }

    /// <summary>Radius of the danger zone, in meters.</summary>
    public float Radius { get; }

    /// <summary>Total telegraph time, in seconds.</summary>
    public float Duration { get; }

    /// <summary>Seconds left before the pebble is released.</summary>
    public float TimeLeft { get; private set; }

    /// <summary>0 when the shadow appears, 1 when the pebble drops.</summary>
    public float Progress => 1f - TimeLeft / Duration;

    public bool HasExpired => TimeLeft <= 0f;

    public GodShadow(Vector3 center, float radius, float duration)
    {
        Center = center;
        Radius = radius;
        Duration = duration;
        TimeLeft = duration;
    }

    public void Tick(float deltaTime) => TimeLeft -= deltaTime;

    /// <summary>
    /// True if a round body of radius <paramref name="bodyRadius"/> standing at
    /// <paramref name="point"/> is at least partly under the shadow. Only the
    /// horizontal (x, z) distance matters.
    /// </summary>
    public bool Overlaps(Vector3 point, float bodyRadius)
    {
        float dx = point.X - Center.X;
        float dz = point.Z - Center.Z;
        float reach = Radius + bodyRadius;
        return dx * dx + dz * dz < reach * reach;
    }
}

/// <summary>
/// Runs cast miracles over time. For now that means the Pebble-Drop: each
/// cast first becomes a <see cref="GodShadow"/>, and only when its timer runs
/// out does the physical pebble spawn and fall.
/// </summary>
public sealed class MiracleManager
{
    /// <summary>How long the shadow telegraphs the drop before the pebble spawns.</summary>
    public const float TelegraphDuration = 1.5f;

    /// <summary>
    /// Radius of the God's Shadow, in meters. Deliberately larger than the
    /// pebble so the danger zone is easy to read and Bramblekin clear it with
    /// some room to spare.
    /// </summary>
    public const float ShadowRadius = 1.5f;

    /// <summary>How high above the target the pebble spawns, in meters.</summary>
    public const float PebbleSpawnHeight = 10f;

    /// <summary>Pebble radius in meters (oversized for the prototype so it reads clearly).</summary>
    public const float PebbleRadius = 0.5f;

    /// <summary>Pebble mass in kilograms (placeholder for future impact damage).</summary>
    public const float PebbleMass = 2f;

    private readonly List<GodShadow> _shadows = new();

    /// <summary>Shadows currently on the ground. Bramblekin read this to decide when to flee.</summary>
    public IReadOnlyList<GodShadow> ActiveShadows => _shadows;

    /// <summary>Starts a Pebble-Drop at <paramref name="groundPoint"/>: shadow now, pebble later.</summary>
    public void QueuePebbleDrop(Vector3 groundPoint)
    {
        _shadows.Add(new GodShadow(groundPoint, ShadowRadius, TelegraphDuration));
    }

    /// <summary>Counts down every shadow and releases the pebble for any that expired.</summary>
    public void Update(float deltaTime, PhysicsManager physics)
    {
        // Iterate backwards so expired shadows can be removed in place.
        for (int i = _shadows.Count - 1; i >= 0; i--)
        {
            GodShadow shadow = _shadows[i];
            shadow.Tick(deltaTime);
            if (!shadow.HasExpired)
                continue;

            var spawn = shadow.Center + new Vector3(0, PebbleSpawnHeight, 0);
            physics.Add(new PhysicsObject(spawn, PebbleRadius, PebbleMass, Color.Gray));
            _shadows.RemoveAt(i);
        }
    }

    public void Draw()
    {
        foreach (var shadow in _shadows)
        {
            // The shadow darkens as the drop approaches, like something
            // descending from above.
            byte alpha = (byte)(80 + 110 * shadow.Progress);
            var p = shadow.Center + new Vector3(0, 0.015f, 0); // Just above the grid lines.

            // A very flat cylinder is a cheap filled disc lying on the ground.
            Raylib.DrawCylinder(p, shadow.Radius, shadow.Radius, 0.01f, 36, new Color(0, 0, 0, (int)alpha));
            Raylib.DrawCircle3D(p + new Vector3(0, 0.015f, 0), shadow.Radius, Vector3.UnitX, 90f, new Color(0, 0, 0, 220));
        }
    }
}

// =============================================================================
//  Creatures: the Bramblekin
// =============================================================================

/// <summary>What a Bramblekin is currently doing.</summary>
public enum BramblekinState
{
    /// <summary>Walking at a slow, steady pace toward a random wander target.</summary>
    Walking,

    /// <summary>Standing still for a moment after arriving somewhere.</summary>
    Pausing,

    /// <summary>Scurrying out from under a God's Shadow at 3x speed.</summary>
    Fleeing,
}

/// <summary>
/// One of the tiny creatures the player protects. The player never controls
/// them directly; they run a small state machine:
///
///   Walking --arrive--> Pausing (2 s) --timer--> Walking (new random target)
///
/// and, overriding everything, the self-preservation rule from the design doc:
/// standing under a God's Shadow sends it Fleeing at 3x speed to the nearest
/// safe spot, after which it pauses and resumes wandering.
/// </summary>
public sealed class Bramblekin
{
    /// <summary>Normal walking speed in m/s (a slow amble).</summary>
    public const float WalkSpeed = 1.0f;

    /// <summary>Flee speed as a multiple of <see cref="WalkSpeed"/>.</summary>
    public const float FleeSpeedMultiplier = 3f;

    /// <summary>How long a Bramblekin rests after reaching a target, in seconds.</summary>
    public const float PauseDuration = 2f;

    /// <summary>Body radius in meters (also used for the shadow overlap test).</summary>
    public const float BodyRadius = 0.25f;

    /// <summary>Total body height in meters, including the rounded ends.</summary>
    public const float BodyHeight = 0.9f;

    /// <summary>How far from the terrain edge targets are kept, in meters.</summary>
    public const float EdgeMargin = 0.5f;

    /// <summary>Extra clearance beyond the shadow's edge when picking an escape point.</summary>
    private const float SafetyMargin = 0.5f;

    /// <summary>Within this distance of a target counts as "arrived".</summary>
    private const float ArriveDistance = 0.05f;

    private static readonly Color CalmColor = new(196, 160, 110, 255);   // Bark brown.
    private static readonly Color PanicColor = new(225, 85, 60, 255);    // Alarm red.

    private readonly Random _rng;
    private Vector3 _target;
    private float _pauseTimer;

    /// <summary>Feet position on the ground (y = GroundHeight).</summary>
    public Vector3 Position { get; private set; }

    public BramblekinState State { get; private set; }

    public Bramblekin(Vector3 position, Random rng)
    {
        Position = position;
        _rng = rng;

        // Start mid-pause with a random timer so the colony doesn't move in lockstep.
        State = BramblekinState.Pausing;
        _pauseTimer = (float)rng.NextDouble() * PauseDuration;
    }

    public void Update(float deltaTime, Terrain terrain, IReadOnlyList<GodShadow> shadows)
    {
        // --- Self-preservation override --------------------------------------
        // Checked before the normal state logic, every frame, so it wins over
        // whatever the Bramblekin was doing. A fleeing Bramblekin only replans
        // if its escape point has itself been covered by a newer shadow.
        GodShadow? threat = FirstOverlapping(Position, shadows);
        if (threat is not null &&
            (State != BramblekinState.Fleeing || FirstOverlapping(_target, shadows) is not null))
        {
            _target = FindEscapePoint(threat, terrain, shadows);
            State = BramblekinState.Fleeing;
        }

        // --- Normal behaviour -------------------------------------------------
        switch (State)
        {
            case BramblekinState.Pausing:
                _pauseTimer -= deltaTime;
                if (_pauseTimer <= 0f)
                {
                    _target = PickWanderTarget(terrain, shadows);
                    State = BramblekinState.Walking;
                }
                break;

            case BramblekinState.Walking:
                // Don't stroll into a spot that has since been marked for a drop.
                if (FirstOverlapping(_target, shadows) is not null)
                    _target = PickWanderTarget(terrain, shadows);

                if (MoveTowards(_target, WalkSpeed * deltaTime))
                    StartPause();
                break;

            case BramblekinState.Fleeing:
                if (MoveTowards(_target, WalkSpeed * FleeSpeedMultiplier * deltaTime))
                    StartPause(); // Catch its breath, then go back to wandering.
                break;
        }
    }

    public void Draw()
    {
        Color color = State == BramblekinState.Fleeing ? PanicColor : CalmColor;

        // A capsule standing upright: DrawCapsule takes the centres of its two
        // hemispherical ends, so inset them by the radius.
        var bottom = Position + new Vector3(0, BodyRadius, 0);
        var top = Position + new Vector3(0, BodyHeight - BodyRadius, 0);
        Raylib.DrawCapsule(bottom, top, BodyRadius, 8, 4, color);
        Raylib.DrawCapsuleWires(bottom, top, BodyRadius, 8, 4, new Color(0, 0, 0, 50));
    }

    private void StartPause()
    {
        State = BramblekinState.Pausing;
        _pauseTimer = PauseDuration;
    }

    /// <summary>
    /// Moves along the ground toward <paramref name="target"/> by at most
    /// <paramref name="maxStep"/> meters. Returns true on arrival.
    /// </summary>
    private bool MoveTowards(Vector3 target, float maxStep)
    {
        Vector3 toTarget = target - Position;
        float distance = toTarget.Length();
        if (distance <= MathF.Max(maxStep, ArriveDistance))
        {
            Position = target;
            return true;
        }

        Position += toTarget / distance * maxStep;
        return false;
    }

    /// <summary>A random terrain point that isn't under any shadow.</summary>
    private Vector3 PickWanderTarget(Terrain terrain, IReadOnlyList<GodShadow> shadows)
    {
        // A few tries is plenty: shadows cover a tiny fraction of the terrain.
        Vector3 candidate = Position;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            candidate = terrain.RandomPoint(_rng, EdgeMargin);
            if (FirstOverlapping(candidate, shadows) is null)
                return candidate;
        }
        return candidate;
    }

    /// <summary>
    /// Picks the closest safe spot just outside <paramref name="threat"/>.
    /// The ideal escape runs straight away from the shadow's centre; if that
    /// point is off the terrain or under another shadow, it tries directions
    /// progressively further round the circle, alternating left and right.
    /// </summary>
    private Vector3 FindEscapePoint(GodShadow threat, Terrain terrain, IReadOnlyList<GodShadow> shadows)
    {
        float awayX = Position.X - threat.Center.X;
        float awayZ = Position.Z - threat.Center.Z;

        // Standing dead centre: any direction is as good as another.
        float baseAngle = awayX * awayX + awayZ * awayZ > 1e-6f
            ? MathF.Atan2(awayZ, awayX)
            : (float)(_rng.NextDouble() * MathF.Tau);

        float escapeDistance = threat.Radius + BodyRadius + SafetyMargin;
        const int steps = 12;                      // 30° increments.
        const float stepAngle = MathF.Tau / steps;

        for (int i = 0; i <= steps / 2; i++)
        {
            foreach (int side in i == 0 ? new[] { 1 } : new[] { 1, -1 })
            {
                float angle = baseAngle + side * i * stepAngle;
                var candidate = new Vector3(
                    threat.Center.X + MathF.Cos(angle) * escapeDistance,
                    Terrain.GroundHeight,
                    threat.Center.Z + MathF.Sin(angle) * escapeDistance);

                if (terrain.Contains(candidate, EdgeMargin) && FirstOverlapping(candidate, shadows) is null)
                    return candidate;
            }
        }

        // Boxed in (e.g. overlapping shadows in a corner): run straight away
        // and hope. Clamp so it at least stays on the terrain.
        float half = terrain.Size / 2f - EdgeMargin;
        return new Vector3(
            Math.Clamp(threat.Center.X + MathF.Cos(baseAngle) * escapeDistance, -half, half),
            Terrain.GroundHeight,
            Math.Clamp(threat.Center.Z + MathF.Sin(baseAngle) * escapeDistance, -half, half));
    }

    private static GodShadow? FirstOverlapping(Vector3 point, IReadOnlyList<GodShadow> shadows)
    {
        foreach (var shadow in shadows)
        {
            if (shadow.Overlaps(point, BodyRadius))
                return shadow;
        }
        return null;
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
