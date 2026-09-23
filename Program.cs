// =============================================================================
//  Garden Guardians — Phase 1 Touch-Physics Prototype
// -----------------------------------------------------------------------------
//  Goal of this prototype (see Garden_Guardians_Roadmap.md, Phase 1):
//    * A fixed isometric camera looking down at a patch of backyard "terrain".
//    * A tiny hand-rolled physics loop (Raylib has no rigidbodies): gravity,
//      ground contact, and solid pebbles that stack or roll off each other.
//    * A two-state input model: click the "Equip Pebble" button, then click the
//      ground to cast the Pebble-Drop miracle at that spot.
//    * The God's Shadow: a cast pebble is telegraphed by a dark shadow on the
//      ground for 1.5 s before it actually drops.
//    * A colony of Bramblekin that wander, steer around rocks, and scurry out
//      of any God's Shadow at 3x speed (see Garden_Guardians_Design.md).
//    * The first economic loop: crack an Acorn with a pebble, and the
//      Bramblekin carry the Food Shards back to the Village Heart.
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
/// Owns the window and the main loop: reads input, steps the <see cref="World"/>,
/// and draws it with the UI on top. Platform-independent.
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
        var world = new World(new Terrain(size: 20f), new Random(), ColonySize);
        var input = new MiracleInput();
        var equipButton = new UiButton(new Rectangle(20, 20, 180, 50));

        // --- Main loop -------------------------------------------------------
        while (!Raylib.WindowShouldClose())
        {
            float deltaTime = MathF.Min(Raylib.GetFrameTime(), MaxDeltaTime);

            // 1) Input: UI gets first pick of the click so that pressing the
            //    button never also drops a pebble "through" it onto the ground.
            //    A ground click queues a God's Shadow rather than a pebble.
            input.Update(camera, world.Terrain, world.Miracles, equipButton);

            // 2) Simulation.
            world.Update(deltaTime);

            // 3) Rendering.
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(135, 190, 235, 255)); // Sky blue.

            Raylib.BeginMode3D(camera);
            world.Draw();
            input.DrawCursorPreview(camera, world.Terrain);
            Raylib.EndMode3D();

            // 2D overlay (UI) is drawn after EndMode3D so it sits on top.
            equipButton.Draw(input.State == InputState.PebbleEquipped ? "Pebble Equipped" : "Equip Pebble",
                             highlighted: input.State == InputState.PebbleEquipped);
            DrawFoodCounter(world.FoodStored);
            DrawHud(input, world);

            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }

    /// <summary>The global "Food Stored" counter in the top-right corner.</summary>
    private static void DrawFoodCounter(int foodStored)
    {
        const int fontSize = 30;
        string text = $"Food Stored: {foodStored}";
        int width = Raylib.MeasureText(text, fontSize);
        int x = Raylib.GetScreenWidth() - width - 30;

        Raylib.DrawRectangle(x - 12, 18, width + 24, fontSize + 18, new Color(255, 250, 235, 220));
        Raylib.DrawRectangleLines(x - 12, 18, width + 24, fontSize + 18, new Color(110, 70, 35, 255));
        Raylib.DrawText(text, x, 27, fontSize, new Color(110, 70, 35, 255));
    }

    /// <summary>Small help text and debug counters in the bottom-left corner.</summary>
    private static void DrawHud(MiracleInput input, World world)
    {
        int Count(BramblekinState state) => world.Colony.Count(b => b.State == state);

        int y = Raylib.GetScreenHeight() - 60;
        string hint = input.State == InputState.PebbleEquipped
            ? "Click the ground to drop the pebble."
            : "Click 'Equip Pebble', then drop it on the acorn to crack it.";
        Raylib.DrawText(hint, 20, y, 20, Color.DarkGray);
        Raylib.DrawText(
            $"Pebbles: {world.Physics.Count}   Bramblekin: {world.Colony.Count} " +
            $"(gathering {Count(BramblekinState.Gathering)}, returning {Count(BramblekinState.Returning)}, " +
            $"fleeing {Count(BramblekinState.Fleeing)})   FPS: {Raylib.GetFPS()}",
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
/// No rotation — pebbles slide rather than truly roll — but that is plenty
/// for stacking, rolling off each other and coming to rest.
/// </summary>
public sealed class PhysicsObject
{
    public Vector3 Position;
    public Vector3 Velocity;

    /// <summary>Sphere radius in meters.</summary>
    public float Radius { get; }

    /// <summary>Mass in kilograms. Heavier bodies are pushed less in collisions.</summary>
    public float Mass { get; }

    public float InverseMass => 1f / Mass;

    public Color Color { get; }

    /// <summary>True while the sphere is touching the terrain.</summary>
    public bool OnGround { get; internal set; }

    /// <summary>True while the sphere is clearly above the ground (falling or perched on something).</summary>
    public bool IsAirborne => Position.Y - Radius > Terrain.GroundHeight + 0.05f;

    public PhysicsObject(Vector3 position, float radius, float mass, Color color)
    {
        Position = position;
        Velocity = Vector3.Zero;
        Radius = radius;
        Mass = mass;
        Color = color;
    }

    public void Draw()
    {
        Raylib.DrawSphere(Position, Radius, Color);
        Raylib.DrawSphereWires(Position, Radius, 8, 8, new Color(0, 0, 0, 60));
    }
}

/// <summary>A sphere hitting the ground hard enough to matter (e.g. to crack an acorn).</summary>
public readonly record struct GroundImpact(PhysicsObject Body, Vector3 Point, float Speed);

/// <summary>
/// Owns every simulated sphere and steps them each frame:
///
///   1. Integrate: gravity, then move (semi-implicit Euler).
///   2. Detect hard ground landings and report them as <see cref="Impacts"/>.
///   3. Solve contacts a few times over: sphere-vs-sphere, then each sphere
///      against the ground, static boxes and the terrain edge. Repeating the
///      pass lets a stack settle: pushing one pair apart can create a new
///      overlap with a third sphere or the ground, which the next pass fixes.
///   4. Ground friction, so rocks that rolled off a pile come to rest.
/// </summary>
public sealed class PhysicsManager
{
    /// <summary>Gravitational acceleration in m/s² (Earth, since 1 unit = 1 m).</summary>
    public const float Gravity = 9.8f;

    /// <summary>Contact passes per frame. More is stiffer; 4 is plenty for a handful of pebbles.</summary>
    private const int SolverIterations = 4;

    /// <summary>Bounciness of sphere-sphere hits (0 = dead stop, 1 = perfectly elastic).</summary>
    private const float Restitution = 0.2f;

    /// <summary>How quickly horizontal speed bleeds away on the ground, per second.</summary>
    private const float GroundFriction = 4f;

    /// <summary>Below this horizontal speed (m/s) a grounded sphere is simply stopped.</summary>
    private const float RestSpeed = 0.05f;

    /// <summary>Landings faster than this (m/s) are reported as impacts.</summary>
    private const float ImpactSpeed = 3f;

    private readonly float _terrainHalfSize;
    private readonly List<PhysicsObject> _objects = new();
    private readonly List<BoundingBox> _staticBoxes = new();
    private readonly List<GroundImpact> _impacts = new();

    public PhysicsManager(Terrain terrain) => _terrainHalfSize = terrain.Size / 2f;

    public int Count => _objects.Count;

    public IReadOnlyList<PhysicsObject> Objects => _objects;

    /// <summary>Hard ground landings that happened during the last <see cref="Update"/>.</summary>
    public IReadOnlyList<GroundImpact> Impacts => _impacts;

    public void Add(PhysicsObject obj) => _objects.Add(obj);

    /// <summary>Adds an immovable box (e.g. a building) that spheres collide with.</summary>
    public void AddStaticBox(BoundingBox box) => _staticBoxes.Add(box);

    public void Update(float deltaTime)
    {
        _impacts.Clear();

        // 1-2) Integrate, then catch first contact with the ground.
        foreach (var obj in _objects)
        {
            obj.Velocity.Y -= Gravity * deltaTime;
            obj.Position += obj.Velocity * deltaTime;

            bool touching = obj.Position.Y - obj.Radius <= Terrain.GroundHeight;
            if (touching && !obj.OnGround && -obj.Velocity.Y >= ImpactSpeed)
            {
                var point = new Vector3(obj.Position.X, Terrain.GroundHeight, obj.Position.Z);
                _impacts.Add(new GroundImpact(obj, point, -obj.Velocity.Y));
            }
            obj.OnGround = touching;
        }

        // 3) Contacts.
        for (int iteration = 0; iteration < SolverIterations; iteration++)
        {
            ResolveSphereContacts();
            foreach (var obj in _objects)
                ResolveStaticContacts(obj);
        }

        // 4) Friction.
        foreach (var obj in _objects)
        {
            if (!obj.OnGround)
                continue;

            float damping = MathF.Max(0f, 1f - GroundFriction * deltaTime);
            obj.Velocity.X *= damping;
            obj.Velocity.Z *= damping;
            if (obj.Velocity.X * obj.Velocity.X + obj.Velocity.Z * obj.Velocity.Z < RestSpeed * RestSpeed)
            {
                obj.Velocity.X = 0f;
                obj.Velocity.Z = 0f;
            }
        }
    }

    /// <summary>
    /// Rock-to-rock collision. For every overlapping pair: push the two apart
    /// along the line between their centres (the collision normal) by the
    /// penetration depth, split by mass, then cancel the velocity with which
    /// they approach each other along that normal. A pebble landing slightly
    /// off-centre on another gets a sideways normal and slides off; one landing
    /// dead on top stays stacked.
    /// </summary>
    private void ResolveSphereContacts()
    {
        for (int i = 0; i < _objects.Count; i++)
        {
            for (int j = i + 1; j < _objects.Count; j++)
            {
                PhysicsObject a = _objects[i], b = _objects[j];

                Vector3 delta = b.Position - a.Position;
                float minDistance = a.Radius + b.Radius;
                float distanceSquared = delta.LengthSquared();
                if (distanceSquared >= minDistance * minDistance)
                    continue;

                float distance = MathF.Sqrt(distanceSquared);
                // Exactly coincident centres have no direction; separate vertically.
                Vector3 normal = distance > 1e-5f ? delta / distance : Vector3.UnitY;
                float penetration = minDistance - distance;

                float totalInverseMass = a.InverseMass + b.InverseMass;
                a.Position -= normal * (penetration * a.InverseMass / totalInverseMass);
                b.Position += normal * (penetration * b.InverseMass / totalInverseMass);

                float approachSpeed = Vector3.Dot(b.Velocity - a.Velocity, normal);
                if (approachSpeed < 0f)
                {
                    float impulse = -(1f + Restitution) * approachSpeed / totalInverseMass;
                    a.Velocity -= normal * (impulse * a.InverseMass);
                    b.Velocity += normal * (impulse * b.InverseMass);
                }
            }
        }
    }

    /// <summary>Keeps a sphere above the ground, outside static boxes and on the terrain.</summary>
    private void ResolveStaticContacts(PhysicsObject obj)
    {
        // Ground: rest the bottom of the sphere on the surface.
        if (obj.Position.Y - obj.Radius <= Terrain.GroundHeight)
        {
            obj.Position.Y = Terrain.GroundHeight + obj.Radius;
            if (obj.Velocity.Y < 0f)
                obj.Velocity.Y = 0f;
            obj.OnGround = true;
        }

        // Static boxes: push out from the closest point on the box.
        foreach (var box in _staticBoxes)
        {
            Vector3 closest = Vector3.Clamp(obj.Position, box.Min, box.Max);
            Vector3 offset = obj.Position - closest;
            float distanceSquared = offset.LengthSquared();
            if (distanceSquared >= obj.Radius * obj.Radius)
                continue;

            Vector3 normal;
            if (distanceSquared > 1e-8f)
            {
                normal = offset / MathF.Sqrt(distanceSquared);
                obj.Position = closest + normal * obj.Radius;
            }
            else
            {
                // Centre inside the box: pop it out on top.
                normal = Vector3.UnitY;
                obj.Position.Y = box.Max.Y + obj.Radius;
            }

            float intoBox = Vector3.Dot(obj.Velocity, normal);
            if (intoBox < 0f)
                obj.Velocity -= normal * intoBox;
        }

        // Terrain edge: keep pebbles on the play area.
        float limit = _terrainHalfSize - obj.Radius;
        if (MathF.Abs(obj.Position.X) > limit)
        {
            obj.Position.X = Math.Clamp(obj.Position.X, -limit, limit);
            obj.Velocity.X = 0f;
        }
        if (MathF.Abs(obj.Position.Z) > limit)
        {
            obj.Position.Z = Math.Clamp(obj.Position.Z, -limit, limit);
            obj.Velocity.Z = 0f;
        }
    }

    public void Draw()
    {
        foreach (var obj in _objects)
        {
            // While an object is clearly above the ground, draw a small dark
            // disc under it. It helps judge height from the isometric view (the
            // bigger God's Shadow telegraph is drawn by MiracleManager).
            if (obj.IsAirborne)
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
//  World: everything that lives on the terrain
// =============================================================================

/// <summary>
/// A solid circle on the ground that Bramblekin walk around (a resting
/// pebble, the Village Heart). Coordinates are (x, z).
/// </summary>
public readonly record struct Obstacle(Vector2 Center, float Radius);

/// <summary>
/// Owns the simulation state — terrain, physics, miracles, the village, the
/// acorn, food shards and the colony — and steps it in a fixed order:
///
///   miracles (shadows, pebble release) -> physics (+ acorn cracking)
///   -> obstacle list -> Bramblekin -> acorn respawn
/// </summary>
public sealed class World
{
    /// <summary>Seconds after an acorn is cracked before a new one appears.</summary>
    private const float AcornRespawnDelay = 4f;

    /// <summary>
    /// How far (beyond touching) a pebble may land from the acorn and still
    /// crack it — "on or very close to" the acorn.
    /// </summary>
    private const float AcornCrackSlack = 0.4f;

    /// <summary>Number of Food Shards a cracked acorn yields.</summary>
    private const int ShardsPerAcorn = 3;

    private readonly List<Obstacle> _obstacles = new();
    private float _acornRespawnTimer;

    public Terrain Terrain { get; }
    public PhysicsManager Physics { get; }
    public MiracleManager Miracles { get; } = new();
    public VillageHeart Village { get; }
    public Acorn? Acorn { get; private set; }
    public List<FoodShard> FoodShards { get; } = new();
    public List<Bramblekin> Colony { get; } = new();
    public Random Rng { get; }

    /// <summary>Food delivered to the Village Heart so far.</summary>
    public int FoodStored { get; private set; }

    /// <summary>Solid circles Bramblekin must walk around. Rebuilt every frame.</summary>
    public IReadOnlyList<Obstacle> Obstacles => _obstacles;

    public World(Terrain terrain, Random rng, int colonySize)
    {
        Terrain = terrain;
        Rng = rng;
        Physics = new PhysicsManager(terrain);

        // The Village Heart sits just off the centre of the garden. It is a
        // solid box for pebbles and a solid circle for walkers.
        Village = new VillageHeart(new Vector3(-2f, Terrain.GroundHeight, -2f));
        Physics.AddStaticBox(Village.Bounds);
        RebuildObstacles();

        Acorn = new Acorn(RandomAcornSpot());

        for (int i = 0; i < colonySize; i++)
            Colony.Add(new Bramblekin(RandomFreePoint(Bramblekin.BodyRadius, Bramblekin.EdgeMargin), rng));
    }

    public void Update(float deltaTime)
    {
        Miracles.Update(deltaTime, Physics);
        Physics.Update(deltaTime);
        CrackAcornOnImpact();

        RebuildObstacles();
        foreach (var bramblekin in Colony)
            bramblekin.Update(deltaTime, this);

        UpdateAcornRespawn(deltaTime);
    }

    public void Draw()
    {
        Terrain.Draw();
        Miracles.Draw();
        Village.Draw();
        Acorn?.Draw();
        foreach (var shard in FoodShards)
        {
            if (!shard.IsCarried)
                shard.Draw(shard.Position);
        }
        foreach (var bramblekin in Colony)
            bramblekin.Draw();
        Physics.Draw();
    }

    // --- Queries used by the Bramblekin AI ------------------------------------

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

    /// <summary>The first God's Shadow that a body of <paramref name="bodyRadius"/> at <paramref name="point"/> is under, if any.</summary>
    public GodShadow? ShadowOver(Vector3 point, float bodyRadius)
    {
        foreach (var shadow in Miracles.ActiveShadows)
        {
            if (shadow.Overlaps(point, bodyRadius))
                return shadow;
        }
        return null;
    }

    /// <summary>
    /// A shard can be gathered if nobody is carrying it, no pebble is sitting
    /// on it, and no God's Shadow is over it (walking in there would be
    /// suicidal).
    /// </summary>
    public bool IsAvailable(FoodShard shard) =>
        !shard.IsCarried && !IsBlocked(shard.Position, 0f) && ShadowOver(shard.Position, FoodShard.Radius) is null;

    public bool HasAvailableFood => FoodShards.Any(IsAvailable);

    public FoodShard? NearestAvailableShard(Vector3 from)
    {
        FoodShard? best = null;
        float bestDistance = float.MaxValue;
        foreach (var shard in FoodShards)
        {
            if (!IsAvailable(shard))
                continue;

            float distance = Vector3.DistanceSquared(from, shard.Position);
            if (distance < bestDistance)
            {
                best = shard;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>A delivered shard leaves the map and adds to the village stores.</summary>
    public void DeliverFood(FoodShard shard)
    {
        FoodShards.Remove(shard);
        FoodStored++;
    }

    /// <summary>A random point on the terrain that isn't inside an obstacle or under a shadow.</summary>
    public Vector3 RandomFreePoint(float clearance, float edgeMargin)
    {
        Vector3 candidate = Vector3.Zero;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            candidate = Terrain.RandomPoint(Rng, edgeMargin);
            if (!IsBlocked(candidate, clearance) && ShadowOver(candidate, clearance) is null)
                return candidate;
        }
        return candidate; // Practically unreachable: obstacles cover a tiny fraction of the map.
    }

    // --- Internals ----------------------------------------------------------------

    /// <summary>
    /// Collects the solid circles on the ground: the village, plus every
    /// pebble low enough to block a walker (a pebble still falling from 10 m
    /// shouldn't make anyone swerve).
    /// </summary>
    private void RebuildObstacles()
    {
        _obstacles.Clear();
        _obstacles.Add(Village.Obstacle);

        foreach (var pebble in Physics.Objects)
        {
            if (pebble.Position.Y - pebble.Radius < Bramblekin.BodyHeight)
                _obstacles.Add(new Obstacle(new Vector2(pebble.Position.X, pebble.Position.Z), pebble.Radius));
        }
    }

    /// <summary>The Miracle Action: a pebble landing on or right next to the acorn cracks it open.</summary>
    private void CrackAcornOnImpact()
    {
        if (Acorn is null)
            return;

        foreach (var impact in Physics.Impacts)
        {
            float crackDistance = impact.Body.Radius + Acorn.Radius + AcornCrackSlack;
            float dx = impact.Point.X - Acorn.Position.X;
            float dz = impact.Point.Z - Acorn.Position.Z;
            if (dx * dx + dz * dz > crackDistance * crackDistance)
                continue;

            SpawnFoodShards(Acorn.Position, impact.Body);
            Acorn = null;
            _acornRespawnTimer = AcornRespawnDelay;
            return;
        }
    }

    /// <summary>
    /// Scatters the shards in a ring around the pebble that cracked the acorn,
    /// just outside it, so none end up buried under the rock. The first shard
    /// flies out on the acorn's side; the others are spaced evenly round.
    /// </summary>
    private void SpawnFoodShards(Vector3 acornPosition, PhysicsObject pebble)
    {
        float dx = acornPosition.X - pebble.Position.X;
        float dz = acornPosition.Z - pebble.Position.Z;
        float baseAngle = dx * dx + dz * dz > 1e-6f ? MathF.Atan2(dz, dx) : (float)(Rng.NextDouble() * MathF.Tau);
        float distance = pebble.Radius + FoodShard.Radius + 0.35f;

        for (int i = 0; i < ShardsPerAcorn; i++)
        {
            float angle = baseAngle + i * MathF.Tau / ShardsPerAcorn;
            var position = new Vector3(
                pebble.Position.X + MathF.Cos(angle) * distance,
                Terrain.GroundHeight,
                pebble.Position.Z + MathF.Sin(angle) * distance);

            // Keep shards on the terrain even if the acorn was near an edge.
            float half = Terrain.Size / 2f - Bramblekin.EdgeMargin;
            position.X = Math.Clamp(position.X, -half, half);
            position.Z = Math.Clamp(position.Z, -half, half);
            FoodShards.Add(new FoodShard(position));
        }
    }

    private void UpdateAcornRespawn(float deltaTime)
    {
        if (Acorn is not null)
            return;

        _acornRespawnTimer -= deltaTime;
        if (_acornRespawnTimer <= 0f)
            Acorn = new Acorn(RandomAcornSpot());
    }

    /// <summary>Somewhere open, not hugging the edge, and a fair walk from the village.</summary>
    private Vector3 RandomAcornSpot()
    {
        Vector3 candidate = Vector3.Zero;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            candidate = RandomFreePoint(Acorn.Radius + 0.5f, edgeMargin: 1.5f);
            if (Vector3.Distance(candidate, Village.Center) > 4f)
                return candidate;
        }
        return candidate;
    }
}

// =============================================================================
//  Economy objects
// =============================================================================

/// <summary>
/// The Village Heart: the colony's home and food store. A static brown block
/// that Bramblekin deliver food to.
/// </summary>
public sealed class VillageHeart
{
    /// <summary>Footprint edge length, in meters.</summary>
    public const float Width = 1.6f;

    public const float Height = 1.2f;

    /// <summary>Centre of the footprint on the ground.</summary>
    public Vector3 Center { get; }

    /// <summary>The solid box pebbles collide with.</summary>
    public BoundingBox Bounds { get; }

    /// <summary>
    /// The circle walkers treat as solid. Slightly bigger than the inscribed
    /// circle so corners are mostly covered without leaving wide gaps at the
    /// faces.
    /// </summary>
    public Obstacle Obstacle => new(new Vector2(Center.X, Center.Z), Width / 2f * 1.2f);

    /// <summary>A returning Bramblekin within this distance of the centre has arrived.</summary>
    public float DeliveryDistance => Obstacle.Radius + Bramblekin.BodyRadius + 0.2f;

    public VillageHeart(Vector3 center)
    {
        Center = center;
        float half = Width / 2f;
        Bounds = new BoundingBox(
            new Vector3(center.X - half, Terrain.GroundHeight, center.Z - half),
            new Vector3(center.X + half, Terrain.GroundHeight + Height, center.Z + half));
    }

    public void Draw()
    {
        var middle = Center + new Vector3(0, Height / 2f, 0);
        Raylib.DrawCube(middle, Width, Height, Width, new Color(122, 78, 40, 255));
        Raylib.DrawCubeWires(middle, Width, Height, Width, new Color(60, 35, 15, 255));

        // A small dark doorway on the camera-facing side so it reads as a home.
        var door = Center + new Vector3(Width / 2f + 0.01f, 0.3f, 0);
        Raylib.DrawCube(door, 0.02f, 0.6f, 0.45f, new Color(45, 25, 10, 255));
    }
}

/// <summary>
/// The Acorn puzzle object: too hard for the Bramblekin to open, but a
/// Pebble-Drop miracle cracks it into Food Shards.
/// </summary>
public sealed class Acorn
{
    public const float Radius = 0.35f;

    public Vector3 Position { get; }

    public Acorn(Vector3 groundPoint) => Position = groundPoint;

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

/// <summary>A small piece of cracked acorn that a Bramblekin can carry home.</summary>
public sealed class FoodShard
{
    public const float Radius = 0.18f;

    /// <summary>Resting spot on the ground (y = GroundHeight). Ignored while carried.</summary>
    public Vector3 Position { get; set; }

    /// <summary>True while a Bramblekin is holding it; carried shards are hidden from the map.</summary>
    public bool IsCarried { get; set; }

    public FoodShard(Vector3 groundPoint) => Position = groundPoint;

    /// <summary>Draws the shard resting on the ground at (or carried above) <paramref name="groundPoint"/>.</summary>
    public void Draw(Vector3 groundPoint)
    {
        Raylib.DrawSphere(groundPoint + new Vector3(0, Radius, 0), Radius, new Color(245, 150, 45, 255));
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

    /// <summary>Scurrying out from under a God's Shadow at 3x speed.</summary>
    Fleeing,
}

/// <summary>
/// One of the tiny creatures the player protects. The player never controls
/// them directly; each runs a small state machine, checked in priority order
/// every frame:
///
///   1. Self-preservation (always wins): standing under a God's Shadow drops
///      any carried food and sends it Fleeing at 3x speed to the nearest safe
///      spot. Afterwards it pauses briefly and carries on.
///   2. Economy: while Food Shards are available, wandering (Walking/Pausing)
///      is overridden by Gathering -> pick up -> Returning -> deliver.
///   3. Wandering: Walking to a random free point, Pausing 2 s, repeat.
///
/// Movement steers around pebbles and the village, and if it ever stops
/// making progress it takes a short sideways detour.
/// </summary>
public sealed class Bramblekin
{
    /// <summary>Normal walking speed in m/s (a slow amble).</summary>
    public const float WalkSpeed = 1.0f;

    /// <summary>Flee speed as a multiple of <see cref="WalkSpeed"/>.</summary>
    public const float FleeSpeedMultiplier = 3f;

    /// <summary>How long a Bramblekin rests after reaching a target, in seconds.</summary>
    public const float PauseDuration = 2f;

    /// <summary>Collision radius in meters: used against pebbles, the village and shadows.</summary>
    public const float BodyRadius = 0.25f;

    /// <summary>Total body height in meters, including the rounded ends.</summary>
    public const float BodyHeight = 0.9f;

    /// <summary>How far from the terrain edge targets are kept, in meters.</summary>
    public const float EdgeMargin = 0.5f;

    /// <summary>Extra clearance beyond the shadow's edge when picking an escape point.</summary>
    private const float SafetyMargin = 0.5f;

    /// <summary>Within this distance of a target counts as "arrived".</summary>
    private const float ArriveDistance = 0.05f;

    /// <summary>Within this distance of a shard, it is picked up.</summary>
    private const float PickupDistance = BodyRadius + FoodShard.Radius + 0.1f;

    /// <summary>How far ahead (m) a walker looks for obstacles in its path.</summary>
    private const float LookAhead = 1.5f;

    /// <summary>Gap (m) a walker tries to keep between itself and an obstacle while passing it.</summary>
    private const float AvoidMargin = 0.15f;

    /// <summary>How strongly avoidance bends the heading (1 = 45° at most, higher = sharper).</summary>
    private const float SteerStrength = 1.5f;

    /// <summary>Progress is checked this often (s); too little movement means we're stuck.</summary>
    private const float StuckCheckInterval = 1.0f;

    /// <summary>Fraction of the expected distance that must be covered per check to not count as stuck.</summary>
    private const float StuckProgressFraction = 0.3f;

    private static readonly Color CalmColor = new(196, 160, 110, 255);   // Bark brown.
    private static readonly Color PanicColor = new(225, 85, 60, 255);    // Alarm red.

    private readonly Random _rng;
    private Vector3 _target;
    private float _pauseTimer;
    private FoodShard? _carried;

    // Stuck detection: where we were at the last check, and a temporary
    // sideways waypoint used to get unstuck.
    private Vector3 _progressAnchor;
    private float _progressTimer;
    private Vector3? _detour;

    /// <summary>Feet position on the ground (y = GroundHeight).</summary>
    public Vector3 Position { get; private set; }

    public BramblekinState State { get; private set; }

    public bool IsCarrying => _carried is not null;

    public Bramblekin(Vector3 position, Random rng)
    {
        Position = position;
        _rng = rng;

        // Start mid-pause with a random timer so the colony doesn't move in lockstep.
        SetState(BramblekinState.Pausing);
        _pauseTimer = (float)rng.NextDouble() * PauseDuration;
    }

    public void Update(float deltaTime, World world)
    {
        // --- 1. Self-preservation (absolute priority) -------------------------
        // A fleeing Bramblekin only replans if its escape point has since been
        // covered by a newer shadow or blocked by a rock.
        GodShadow? threat = world.ShadowOver(Position, BodyRadius);
        if (threat is not null && (State != BramblekinState.Fleeing || !IsSafeSpot(_target, world)))
        {
            DropCarried();
            _target = FindEscapePoint(threat, world);
            SetState(BramblekinState.Fleeing);
        }

        // --- 2. Economy overrides wandering -----------------------------------
        if (State is BramblekinState.Walking or BramblekinState.Pausing && world.HasAvailableFood)
            SetState(BramblekinState.Gathering);

        // --- 3. Run the current state -----------------------------------------
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

                if (MoveTowards(_target, WalkSpeed, deltaTime, world))
                    StartPause();
                break;

            case BramblekinState.Gathering:
                UpdateGathering(deltaTime, world);
                break;

            case BramblekinState.Returning:
                UpdateReturning(deltaTime, world);
                break;

            case BramblekinState.Fleeing:
                if (MoveTowards(_target, WalkSpeed * FleeSpeedMultiplier, deltaTime, world))
                    StartPause(); // Catch its breath, then back to work.
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

        // Carried food rides on top of the head.
        _carried?.Draw(Position + new Vector3(0, BodyHeight, 0));
    }

    // --- Economy states ----------------------------------------------------------

    private void UpdateGathering(float deltaTime, World world)
    {
        // Re-pick the nearest shard every frame: another Bramblekin may have
        // grabbed ours, or a rock or shadow may have made it unreachable.
        FoodShard? shard = world.NearestAvailableShard(Position);
        if (shard is null)
        {
            StartWandering(world);
            return;
        }

        if (HorizontalDistance(Position, shard.Position) <= PickupDistance)
        {
            shard.IsCarried = true;
            _carried = shard;
            SetState(BramblekinState.Returning);
            return;
        }

        MoveTowards(shard.Position, WalkSpeed, deltaTime, world);
    }

    private void UpdateReturning(float deltaTime, World world)
    {
        if (HorizontalDistance(Position, world.Village.Center) <= world.Village.DeliveryDistance)
        {
            world.DeliverFood(_carried!);
            _carried = null;

            if (world.HasAvailableFood)
                SetState(BramblekinState.Gathering);
            else
                StartWandering(world);
            return;
        }

        MoveTowards(world.Village.Center, WalkSpeed, deltaTime, world);
    }

    /// <summary>Puts carried food back on the ground where we stand (it can be gathered again later).</summary>
    private void DropCarried()
    {
        if (_carried is null)
            return;

        _carried.Position = Position;
        _carried.IsCarried = false;
        _carried = null;
    }

    // --- Wandering ----------------------------------------------------------------

    private void StartWandering(World world)
    {
        // No-spawn zones: RandomFreePoint never picks a spot inside a rock,
        // the village, or a God's Shadow.
        _target = world.RandomFreePoint(BodyRadius + 0.1f, EdgeMargin);
        SetState(BramblekinState.Walking);
    }

    private void StartPause()
    {
        SetState(BramblekinState.Pausing);
        _pauseTimer = PauseDuration;
    }

    private void SetState(BramblekinState state)
    {
        State = state;
        _detour = null;
        _progressTimer = 0f;
        _progressAnchor = Position;
    }

    // --- Movement: steering, collision, unsticking ---------------------------------

    /// <summary>
    /// Moves toward <paramref name="target"/> at <paramref name="speed"/>,
    /// steering around obstacles, then pushes the body back out of anything
    /// it still overlaps. Returns true on arrival.
    /// </summary>
    private bool MoveTowards(Vector3 target, float speed, float deltaTime, World world)
    {
        float step = speed * deltaTime;
        CheckIfStuck(target, step, deltaTime, world);

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
            Vector2 heading = Steer(position, toGoal / distance, MathF.Min(distance, LookAhead), goal, world.Obstacles);
            position += heading * step;
        }

        Position = new Vector3(position.X, Terrain.GroundHeight, position.Y);
        PushOutOfObstacles(world);
        ClampToTerrain(world.Terrain);

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
    private static Vector2 Steer(Vector2 position, Vector2 direction, float lookAhead, Vector3 goal,
                                 IReadOnlyList<Obstacle> obstacles)
    {
        var goal2 = new Vector2(goal.X, goal.Z);
        Obstacle? nearest = null;
        Vector2 nearestOffset = Vector2.Zero;
        float nearestAlong = float.MaxValue;

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
                nearest = obstacle;
                nearestOffset = offset;
                nearestAlong = along;
            }
        }

        if (nearest is null)
            return direction;

        // Push away from the obstacle's side of the path. Dead-centre hits
        // have no side, so always go left for consistency.
        Vector2 away = nearestOffset.LengthSquared() > 1e-6f
            ? -Vector2.Normalize(nearestOffset)
            : new Vector2(-direction.Y, direction.X);

        return Vector2.Normalize(direction + away * SteerStrength);
    }

    /// <summary>Bramblekin-to-rock collision: slide the body back outside any obstacle it overlaps.</summary>
    private void PushOutOfObstacles(World world)
    {
        var position = new Vector2(Position.X, Position.Z);
        foreach (var obstacle in world.Obstacles)
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
    private void CheckIfStuck(Vector3 target, float step, float deltaTime, World world)
    {
        _progressTimer += deltaTime;
        if (_progressTimer < StuckCheckInterval)
            return;

        float expected = step / deltaTime * StuckCheckInterval;
        float moved = HorizontalDistance(Position, _progressAnchor);
        bool nearTarget = HorizontalDistance(Position, target) < 0.5f;

        if (moved < expected * StuckProgressFraction && !nearTarget && _detour is null)
            _detour = PickDetour(target, world);

        _progressTimer = 0f;
        _progressAnchor = Position;
    }

    /// <summary>A free point ~1.5 m to the left or right of the line toward the target.</summary>
    private Vector3? PickDetour(Vector3 target, World world)
    {
        var forward = new Vector2(target.X - Position.X, target.Z - Position.Z);
        forward = forward.LengthSquared() > 1e-6f ? Vector2.Normalize(forward) : Vector2.UnitX;
        var left = new Vector2(-forward.Y, forward.X);
        float firstSide = _rng.Next(2) == 0 ? 1f : -1f;

        foreach (float side in new[] { firstSide, -firstSide })
        {
            Vector2 offset = left * side * 1.5f - forward * 0.5f;
            var candidate = new Vector3(Position.X + offset.X, Terrain.GroundHeight, Position.Z + offset.Y);
            if (world.Terrain.Contains(candidate, EdgeMargin) && IsSafeSpot(candidate, world))
                return candidate;
        }
        return null;
    }

    // --- Fleeing ------------------------------------------------------------------

    /// <summary>
    /// Picks the closest safe spot just outside <paramref name="threat"/>.
    /// The ideal escape runs straight away from the shadow's centre; if that
    /// point is off the terrain, under another shadow or inside a rock, it
    /// tries directions progressively further round the circle, alternating
    /// left and right.
    /// </summary>
    private Vector3 FindEscapePoint(GodShadow threat, World world)
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

                if (world.Terrain.Contains(candidate, EdgeMargin) && IsSafeSpot(candidate, world))
                    return candidate;
            }
        }

        // Boxed in (e.g. overlapping shadows in a corner): run straight away
        // and hope. Clamp so it at least stays on the terrain.
        float half = world.Terrain.Size / 2f - EdgeMargin;
        return new Vector3(
            Math.Clamp(threat.Center.X + MathF.Cos(baseAngle) * escapeDistance, -half, half),
            Terrain.GroundHeight,
            Math.Clamp(threat.Center.Z + MathF.Sin(baseAngle) * escapeDistance, -half, half));
    }

    /// <summary>Not under a shadow and not inside a rock or the village.</summary>
    private static bool IsSafeSpot(Vector3 point, World world) =>
        world.ShadowOver(point, BodyRadius) is null && !world.IsBlocked(point, BodyRadius);

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
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
