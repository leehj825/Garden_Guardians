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
//    * The first predator: a Wolf Spider that hunts busy workers by
//      vibration, and can be distracted by the thud of a dropped pebble —
//      or crushed by a direct hit.
//    * The macro-economy: every 5 food sprouts a new Bramblekin, and the
//      colony's worship refills the Faith that miracles cost.
//
//  Safety: entities are created and destroyed constantly (sprouts, spider
//  kills, expiring pebbles), so every list that can change size mid-frame is
//  either walked with a reverse for-loop or mutated through a deferred
//  pending-add/pending-remove queue processed once at the end of the frame,
//  never directly inside another entity's Update().
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
        world.SpawnSpiderNearEdge();
        var input = new MiracleInput();
        var pebbleButton = new UiButton(new Rectangle(20, 20, 220, 50));
        var gustButton = new UiButton(new Rectangle(250, 20, 180, 50));
        var draftButton = new UiButton(new Rectangle(20, 80, 220, 50));

        // --- Main loop -------------------------------------------------------
        while (!Raylib.WindowShouldClose())
        {
            float deltaTime = MathF.Min(Raylib.GetFrameTime(), MaxDeltaTime);

            // 1) Input: UI gets first pick of a press so that touching a
            //    button never also starts casting a miracle "through" it. A
            //    Pebble tap spends Faith and queues a God's Shadow; a Gust
            //    swipe spends Faith and pushes everything caught in it the
            //    instant the press is released. Draft Militia isn't a
            //    miracle — free, instant, no equip state — so it's checked
            //    directly here rather than through MiracleInput; consuming
            //    the press this way (skipping input.Update() for the frame)
            //    stops it from also being read as a world click.
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && draftButton.Contains(Raylib.GetMousePosition()))
                world.DraftMilitia();
            else
                input.Update(deltaTime, camera, world, pebbleButton, gustButton);

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
            pebbleButton.Draw(input.PebbleButtonLabel,
                              highlighted: input.State == InputState.PebbleEquipped,
                              disabled: !input.CanAffordPebble(world));
            gustButton.Draw(input.GustButtonLabel,
                            highlighted: input.State is InputState.GustEquipped or InputState.GustDragging,
                            disabled: !input.CanAffordGust(world));
            draftButton.Draw("Draft Militia", highlighted: false, disabled: !world.HasDraftableGatherer);
            DrawFaithMeter(world.Faith);
            DrawColonyPanel(world);
            DrawHud(input, world);

            Raylib.EndDrawing();

            // 4) Deferred spawns/removals: applied once here, after this
            //    frame's Update() and Draw() have both fully run, so no
            //    entity list ever changes size while something is iterating
            //    it (a sprout mid-Colony-update, a kill mid-pounce, etc).
            world.CommitPendingChanges();
        }

        Raylib.CloseWindow();
    }

    private static readonly Color PanelFill = new(255, 250, 235, 220);
    private static readonly Color PanelInk = new(110, 70, 35, 255);

    /// <summary>
    /// The Faith meter, top centre: a bar that fills toward
    /// <see cref="World.MaxFaith"/>, with a tick at each miracle's cost (Gust
    /// at 10, Pebble-Drop at 30) so the player can see at a glance what they
    /// can currently afford.
    /// </summary>
    private static void DrawFaithMeter(float faith)
    {
        const int width = 380, height = 56, barHeight = 16;
        int x = (Raylib.GetScreenWidth() - width) / 2;
        const int y = 18;
        bool canAffordAnything = faith >= MiracleManager.GustFaithCost;

        Raylib.DrawRectangle(x, y, width, height, PanelFill);
        Raylib.DrawRectangleLines(x, y, width, height, PanelInk);

        string text = $"Faith: {(int)faith} / {(int)World.MaxFaith}";
        Raylib.DrawText(text, x + 12, y + 6, 24, canAffordAnything ? PanelInk : new Color(170, 60, 40, 255));

        // Bar: gold once at least the cheapest miracle is affordable, dull otherwise.
        var bar = new Rectangle(x + 12, y + height - barHeight - 8, width - 24, barHeight);
        Raylib.DrawRectangleRec(bar, new Color(225, 215, 190, 255));
        var fill = bar with { Width = bar.Width * Math.Clamp(faith / World.MaxFaith, 0f, 1f) };
        Raylib.DrawRectangleRec(fill, canAffordAnything ? new Color(235, 185, 50, 255) : new Color(190, 160, 110, 255));
        Raylib.DrawRectangleLinesEx(bar, 1f, PanelInk);

        // Cost ticks, one per miracle.
        DrawCostTick(bar, MiracleManager.GustFaithCost);
        DrawCostTick(bar, MiracleManager.PebbleFaithCost);
    }

    private static void DrawCostTick(Rectangle bar, float cost)
    {
        float tickX = bar.X + bar.Width * (cost / World.MaxFaith);
        Raylib.DrawLineEx(new Vector2(tickX, bar.Y - 3), new Vector2(tickX, bar.Y + bar.Height + 3), 2f, PanelInk);
    }

    /// <summary>Food, population and Militia, top right. Shows progress toward the next sprout.</summary>
    private static void DrawColonyPanel(World world)
    {
        const int fontSize = 24, lineHeight = 30;
        int militia = world.Colony.Count(b => !b.IsDead && b.Role == BramblekinRole.Militia);
        string food = $"Food Stored: {world.FoodStored} / {World.FoodPerSprout}";
        string population = $"Population: {world.Colony.Count}   Militia: {militia}";
        int width = Math.Max(Raylib.MeasureText(food, fontSize), Raylib.MeasureText(population, fontSize));
        int x = Raylib.GetScreenWidth() - width - 30;

        Raylib.DrawRectangle(x - 12, 18, width + 24, lineHeight * 2 + 14, PanelFill);
        Raylib.DrawRectangleLines(x - 12, 18, width + 24, lineHeight * 2 + 14, PanelInk);
        Raylib.DrawText(food, x, 26, fontSize, PanelInk);
        Raylib.DrawText(population, x, 26 + lineHeight, fontSize, PanelInk);
    }

    /// <summary>Small help text and debug counters in the bottom-left corner.</summary>
    private static void DrawHud(MiracleInput input, World world)
    {
        int Count(BramblekinState state) => world.Colony.Count(b => b.State == state);

        int y = Raylib.GetScreenHeight() - 60;
        string hint = input.State switch
        {
            InputState.PebbleEquipped => $"Click the ground to drop the pebble ({MiracleManager.PebbleFaithCost} Faith).",
            InputState.GustEquipped => $"Click and drag across the ground, then release to blow a Gust ({MiracleManager.GustFaithCost} Faith).",
            InputState.GustDragging => "Release to blow the Gust in this direction.",
            _ => "Crack acorns or forage berries. Draft Militia to defend the village and hunt aphids. A Gust tumbles a hunting spider; a Pike Defense does too.",
        };
        Raylib.DrawText(hint, 20, y, 20, Color.DarkGray);
        Raylib.DrawText(
            $"Pebbles: {world.Physics.Count}   Bramblekin: {world.Colony.Count} " +
            $"(gathering {Count(BramblekinState.Gathering)}, returning {Count(BramblekinState.Returning)}, " +
            $"fleeing {Count(BramblekinState.Fleeing)}, defending {Count(BramblekinState.Defending)}, " +
            $"hunting {Count(BramblekinState.Hunting)}, lost {world.Casualties})   " +
            $"Aphids: {world.Aphids.Count(a => !a.IsDead)}   " +
            $"Spider: {SpiderStatus(world)}   Sprouted: {world.Births}   FPS: {Raylib.GetFPS()}",
            20, y + 26, 20, Color.DarkGray);
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
        Vector3? hit = RaycastGroundPlane(ray);
        return hit is not null && Contains(hit.Value) ? hit : null;
    }

    /// <summary>
    /// Intersects a ray with the infinite horizontal plane y = GroundHeight,
    /// with no bound on where that point falls — unlike <see cref="Raycast"/>,
    /// a hit off the edge of the terrain (or well beyond it) still counts.
    /// Used for The Gust, where a swipe only needs a direction and may start
    /// or end past the terrain's edge.
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
//  Physics
// =============================================================================

/// <summary>
/// A minimal rigid body: a sphere with a position, a velocity and a radius.
/// No rotation — pebbles slide rather than truly roll — but that is plenty
/// for stacking, rolling off each other and coming to rest.
///
/// Every object has a limited lifetime. Over its last
/// <see cref="ShrinkDuration"/> seconds it shrinks to nothing (so anything
/// resting on it settles gently and walkers can pass), then it is removed.
/// </summary>
public sealed class PhysicsObject
{
    /// <summary>How long the shrink-away at the end of an object's life takes, in seconds.</summary>
    public const float ShrinkDuration = 1.5f;

    private readonly float _fullRadius;

    public Vector3 Position;
    public Vector3 Velocity;

    /// <summary>Current sphere radius in meters (shrinks at the end of the object's life).</summary>
    public float Radius => _fullRadius * Scale;

    /// <summary>Seconds since the object was spawned.</summary>
    public float Age { get; private set; }

    /// <summary>Total lifetime in seconds; the object is removed when <see cref="Age"/> reaches it.</summary>
    public float Lifetime { get; }

    /// <summary>1 for most of its life, falling to 0 during the final shrink.</summary>
    public float Scale => Math.Clamp((Lifetime - Age) / ShrinkDuration, 0f, 1f);

    /// <summary>True once the object has started shrinking away.</summary>
    public bool IsExpiring => Age >= Lifetime - ShrinkDuration;

    public bool IsExpired => Age >= Lifetime;

    /// <summary>Mass in kilograms. Heavier bodies are pushed less in collisions.</summary>
    public float Mass { get; }

    public float InverseMass => 1f / Mass;

    public Color Color { get; }

    /// <summary>True while the sphere is touching the terrain.</summary>
    public bool OnGround { get; internal set; }

    /// <summary>True while the sphere is clearly above the ground (falling or perched on something).</summary>
    public bool IsAirborne => Position.Y - Radius > Terrain.GroundHeight + 0.05f;

    public PhysicsObject(Vector3 position, float radius, float mass, Color color, float lifetime = float.PositiveInfinity)
    {
        Position = position;
        Velocity = Vector3.Zero;
        _fullRadius = radius;
        Mass = mass;
        Color = color;
        Lifetime = lifetime;
    }

    public void Tick(float deltaTime) => Age += deltaTime;

    /// <summary>Skips ahead to the start of the shrink-away (no-op if already shrinking).</summary>
    public void Expire() => Age = MathF.Max(Age, Lifetime - ShrinkDuration);

    public void Draw()
    {
        if (Radius <= 0.001f)
            return;

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
///   5. Age every object and remove the ones whose lifetime is over.
///
/// At most <see cref="MaxObjects"/> objects exist at once: adding one past
/// the cap makes the oldest start shrinking away early.
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

    /// <summary>
    /// Most objects allowed at once. Keeps the garden readable and the
    /// all-pairs collision check (which grows with the square of the count)
    /// cheap on phones.
    /// </summary>
    public const int MaxObjects = 25;

    private readonly float _terrainHalfSize;
    private readonly List<PhysicsObject> _objects = new();
    private readonly List<BoundingBox> _staticBoxes = new();
    private readonly List<GroundImpact> _impacts = new();

    public PhysicsManager(Terrain terrain) => _terrainHalfSize = terrain.Size / 2f;

    public int Count => _objects.Count;

    public IReadOnlyList<PhysicsObject> Objects => _objects;

    /// <summary>Hard ground landings that happened during the last <see cref="Update"/>.</summary>
    public IReadOnlyList<GroundImpact> Impacts => _impacts;

    public void Add(PhysicsObject obj)
    {
        _objects.Add(obj);

        // Over the cap: retire the oldest objects that aren't already on their way out.
        int surplus = _objects.Count(o => !o.IsExpiring) - MaxObjects;
        foreach (var oldest in _objects.Where(o => !o.IsExpiring).OrderByDescending(o => o.Age).Take(surplus).ToList())
            oldest.Expire();
    }

    /// <summary>Adds an immovable box (e.g. a building) that spheres collide with.</summary>
    public void AddStaticBox(BoundingBox box) => _staticBoxes.Add(box);

    public void Update(float deltaTime)
    {
        _impacts.Clear();

        // 1-2) Integrate, then catch first contact with the ground. Reverse
        // for-loop: nothing here mutates _objects, but pebbles are created
        // and expired every frame elsewhere, so every walk of this list uses
        // the same crash-proof pattern as a rule, not case by case.
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            PhysicsObject obj = _objects[i];
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
            for (int i = _objects.Count - 1; i >= 0; i--)
                ResolveStaticContacts(_objects[i]);
        }

        // 4) Friction.
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            PhysicsObject obj = _objects[i];
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

        // 5) Lifetimes: age every pebble, then remove the ones that expired.
        // RemoveAll is safe here — it is the list's own single mutating pass,
        // not a foreach we are mutating out from under.
        for (int i = _objects.Count - 1; i >= 0; i--)
            _objects[i].Tick(deltaTime);
        _objects.RemoveAll(o => o.IsExpired);
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

    /// <summary>The Gust is armed; the next press starts a swipe.</summary>
    GustEquipped,

    /// <summary>Mid-swipe: the player is holding the press down, aiming the Gust.</summary>
    GustDragging,
}

/// <summary>Which miracle a "Not Enough Faith" refusal message belongs to.</summary>
public enum RefusedMiracle
{
    None,
    Pebble,
    Gust,
}

/// <summary>
/// Translates mouse/touch input into miracles. Holds the equip/drag state and
/// knows how to turn a screen position into a point on the terrain (or, for
/// the Gust, the wider ground plane).
/// </summary>
public sealed class MiracleInput
{
    /// <summary>How long the "Not Enough Faith" message stays on its button, in seconds.</summary>
    private const float RefusalMessageDuration = 1.5f;

    /// <summary>
    /// Shortest horizontal swipe (m, world space) that counts as a Gust cast.
    /// A shorter drag — effectively a tap-and-release — is ignored, so an
    /// accidental fumble doesn't spend Faith or fling something with no
    /// meaningful direction; the Gust stays equipped so the player can just
    /// try again.
    /// </summary>
    private const float MinGustDragDistance = 0.5f;

    private float _refusalTimer;
    private RefusedMiracle _refused = RefusedMiracle.None;
    private Vector3? _gustStartGround;

    public InputState State { get; private set; } = InputState.Idle;

    /// <summary>Text for the Pebble button, including its temporary "Not Enough Faith" refusal.</summary>
    public string PebbleButtonLabel =>
        _refused == RefusedMiracle.Pebble && _refusalTimer > 0f ? "Not Enough Faith"
        : State == InputState.PebbleEquipped ? "Pebble Equipped"
        : "Equip Pebble";

    /// <summary>Text for the Gust button, including its temporary "Not Enough Faith" refusal.</summary>
    public string GustButtonLabel =>
        _refused == RefusedMiracle.Gust && _refusalTimer > 0f ? "Not Enough Faith"
        : State is InputState.GustEquipped or InputState.GustDragging ? "Gust Equipped"
        : "Equip Gust";

    public bool CanAffordPebble(World world) => world.Faith >= MiracleManager.PebbleFaithCost;

    public bool CanAffordGust(World world) => world.Faith >= MiracleManager.GustFaithCost;

    /// <summary>Polls the mouse/touch and handles this frame's press/release, if any.</summary>
    public void Update(float deltaTime, Camera3D camera, World world, UiButton pebbleButton, UiButton gustButton)
    {
        _refusalTimer = MathF.Max(0f, _refusalTimer - deltaTime);

        // Raylib maps a primary touch to the left mouse button, so the same
        // code path serves desktop clicks and phone taps/drags.
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            HandlePress(Raylib.GetMousePosition(), camera, world, pebbleButton, gustButton);

        if (State == InputState.GustDragging && Raylib.IsMouseButtonReleased(MouseButton.Left))
            HandleGustRelease(Raylib.GetMousePosition(), camera, world);
    }

    /// <summary>
    /// Handles the start of a press at <paramref name="screenPosition"/>: a
    /// button toggles its miracle, a Pebble cast fires immediately, and a
    /// Gust cast begins tracking a swipe. Public (like <see cref="HandleGustRelease"/>)
    /// so input can be driven directly, from a test harness or an
    /// alternate input source.
    /// </summary>
    public void HandlePress(Vector2 screenPosition, Camera3D camera, World world, UiButton pebbleButton, UiButton gustButton)
    {
        // --- UI layer ------------------------------------------------------
        if (pebbleButton.Contains(screenPosition))
        {
            if (State == InputState.PebbleEquipped)
                State = InputState.Idle; // Toggle off (nothing was spent yet).
            else if (CanAffordPebble(world))
                State = InputState.PebbleEquipped;
            else
                Refuse(RefusedMiracle.Pebble);
            return;
        }

        if (gustButton.Contains(screenPosition))
        {
            if (State is InputState.GustEquipped or InputState.GustDragging)
                State = InputState.Idle; // Toggle off (nothing was spent yet).
            else if (CanAffordGust(world))
                State = InputState.GustEquipped;
            else
                Refuse(RefusedMiracle.Gust);
            return;
        }

        // --- World layer ---------------------------------------------------
        switch (State)
        {
            case InputState.PebbleEquipped:
                CastPebble(screenPosition, camera, world);
                break;

            case InputState.GustEquipped:
                // Anchor the swipe. Uses the unbounded ground-plane raycast:
                // a swipe can reasonably start right at the terrain's edge.
                Vector3? start = PickGroundPlane(camera, world.Terrain, screenPosition);
                if (start is not null)
                {
                    _gustStartGround = start;
                    State = InputState.GustDragging;
                }
                break;
        }
    }

    /// <summary>Raycasts the tap onto the terrain and, if it lands, spends Faith and casts the Pebble-Drop.</summary>
    private void CastPebble(Vector2 screenPosition, Camera3D camera, World world)
    {
        Vector3? groundPoint = PickGround(camera, world.Terrain, screenPosition);
        if (groundPoint is null)
            return; // Tapped the sky or off the edge of the terrain: stay equipped, nothing spent.

        // Pay the moment the raycast lands. Equipping already checked the
        // cost and Faith only goes down when a miracle is cast, so this
        // succeeds; the check is a guard, not a gameplay rule.
        if (!world.TrySpendFaith(MiracleManager.PebbleFaithCost))
        {
            State = InputState.Idle;
            Refuse(RefusedMiracle.Pebble);
            return;
        }

        // Don't drop yet: cast the God's Shadow first. MiracleManager spawns
        // the pebble when the shadow's timer runs out.
        world.Miracles.QueuePebbleDrop(groundPoint.Value);
        State = InputState.Idle; // One pebble per equip.
    }

    /// <summary>
    /// Ends a Gust swipe: projects the release point onto the ground plane,
    /// and — if the drag was long enough to read as a real swipe — spends
    /// Faith and casts the Gust along the start-to-end vector. Public so it
    /// can be driven directly (see <see cref="HandlePress"/>).
    /// </summary>
    public void HandleGustRelease(Vector2 screenPosition, Camera3D camera, World world)
    {
        Vector3? start = _gustStartGround;
        Vector3? end = PickGroundPlane(camera, world.Terrain, screenPosition);
        _gustStartGround = null;

        if (start is null || end is null)
        {
            // Should be unreachable with this fixed camera (the ground plane
            // raycast only fails for a ray parallel to or behind it), but
            // stay armed rather than lose the Faith on a gesture with no
            // usable vector.
            State = InputState.GustEquipped;
            return;
        }

        Vector3 delta = end.Value - start.Value;
        delta.Y = 0f; // The Gust only ever blows horizontally.
        if (delta.Length() < MinGustDragDistance)
        {
            State = InputState.GustEquipped; // Too short to read as a swipe; try again.
            return;
        }

        if (!world.TrySpendFaith(MiracleManager.GustFaithCost))
        {
            State = InputState.Idle;
            Refuse(RefusedMiracle.Gust);
            return;
        }

        world.CastGust(start.Value, Vector3.Normalize(delta));
        State = InputState.Idle;
    }

    private void Refuse(RefusedMiracle which)
    {
        _refused = which;
        _refusalTimer = RefusalMessageDuration;
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
    private static Vector3? PickGround(Camera3D camera, Terrain terrain, Vector2 screenPosition)
    {
        Ray? ray = SafeScreenRay(screenPosition, camera);
        return ray is null ? null : terrain.Raycast(ray.Value);
    }

    /// <summary>
    /// Same as <see cref="PickGround"/>, but the hit point is not bounded to
    /// the terrain rectangle — used for the Gust, where a swipe only needs a
    /// direction and may reasonably start or end just past the terrain's edge.
    /// </summary>
    private static Vector3? PickGroundPlane(Camera3D camera, Terrain terrain, Vector2 screenPosition)
    {
        Ray? ray = SafeScreenRay(screenPosition, camera);
        return ray is null ? null : terrain.RaycastGroundPlane(ray.Value);
    }

    /// <summary>
    /// The bounds/NaN/Infinity guards shared by <see cref="PickGround"/> and
    /// <see cref="PickGroundPlane"/>: validates the screen position and the
    /// window before asking raylib to project it, and validates the ray it
    /// gets back.
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

    /// <summary>
    /// Live aiming feedback: while a pebble is equipped, where it would land
    /// (the outer ring is the God's Shadow, the inner ring the pebble
    /// itself); while dragging a Gust, a line from where the swipe started
    /// to the current touch position.
    /// </summary>
    public void DrawCursorPreview(Camera3D camera, Terrain terrain)
    {
        switch (State)
        {
            case InputState.PebbleEquipped:
                Vector3? target = PickGround(camera, terrain, Raylib.GetMousePosition());
                if (target is null)
                    return;

                var p = target.Value + new Vector3(0, 0.02f, 0); // Lift slightly to avoid z-fighting.
                Raylib.DrawCircle3D(p, MiracleManager.ShadowRadius, Vector3.UnitX, 90f, new Color(255, 255, 0, 140));
                Raylib.DrawCircle3D(p, MiracleManager.PebbleRadius, Vector3.UnitX, 90f, Color.Yellow);
                Raylib.DrawLine3D(p, p + new Vector3(0, MiracleManager.PebbleSpawnHeight, 0), new Color(255, 255, 0, 80));
                break;

            case InputState.GustDragging when _gustStartGround is not null:
                Vector3? end = PickGroundPlane(camera, terrain, Raylib.GetMousePosition());
                if (end is null)
                    return;

                var start = _gustStartGround.Value + new Vector3(0, 0.05f, 0);
                var endPoint = end.Value + new Vector3(0, 0.05f, 0);
                Raylib.DrawSphere(start, 0.15f, new Color(255, 255, 255, 160));
                Raylib.DrawLine3D(start, endPoint, new Color(255, 255, 255, 180));
                break;
        }
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
/// A cast Gust's wind-streak visual: a set of lines that shoot across the
/// terrain along <see cref="Direction"/> from <see cref="Origin"/> and fade
/// out over <see cref="Duration"/> seconds. Purely cosmetic — the physics
/// push happens once, immediately, when the Gust is cast (see World.CastGust).
/// </summary>
public sealed class GustEffect
{
    /// <summary>Ground point the swipe started from (y = GroundHeight).</summary>
    public Vector3 Origin { get; }

    /// <summary>Unit, horizontal (y = 0) direction of the wind.</summary>
    public Vector3 Direction { get; }

    public float Duration { get; }

    /// <summary>Seconds left before the visual finishes.</summary>
    public float TimeLeft { get; private set; }

    /// <summary>0 when cast, 1 once the visual has fully played out.</summary>
    public float Progress => 1f - TimeLeft / Duration;

    public bool HasExpired => TimeLeft <= 0f;

    public GustEffect(Vector3 origin, Vector3 direction, float duration)
    {
        Origin = origin;
        Direction = direction;
        Duration = duration;
        TimeLeft = duration;
    }

    public void Tick(float deltaTime) => TimeLeft -= deltaTime;
}

/// <summary>
/// Runs cast miracles over time. For now that means the Pebble-Drop: each
/// cast first becomes a <see cref="GodShadow"/>, and only when its timer runs
/// out does the physical pebble spawn and fall. The Gust is simpler — its
/// physics happens all at once in World.CastGust — so this only owns its
/// fading wind-streak visual.
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

    /// <summary>Faith spent per Pebble-Drop.</summary>
    public const float PebbleFaithCost = 30f;

    /// <summary>
    /// How long a dropped pebble stays in the garden, in seconds, before it
    /// shrinks away. Together with <see cref="PhysicsManager.MaxObjects"/>
    /// this stops the map from silting up with rocks.
    /// </summary>
    public const float PebbleLifetime = 30f;

    /// <summary>Faith spent per Gust.</summary>
    public const float GustFaithCost = 10f;

    /// <summary>How long the wind-streak visual plays for, in seconds.</summary>
    public const float GustVisualDuration = 1f;

    private readonly List<GodShadow> _shadows = new();
    private readonly List<GustEffect> _gusts = new();

    /// <summary>Shadows currently on the ground. Bramblekin read this to decide when to flee.</summary>
    public IReadOnlyList<GodShadow> ActiveShadows => _shadows;

    /// <summary>Starts a Pebble-Drop at <paramref name="groundPoint"/>: shadow now, pebble later.</summary>
    public void QueuePebbleDrop(Vector3 groundPoint)
    {
        _shadows.Add(new GodShadow(groundPoint, ShadowRadius, TelegraphDuration));
    }

    /// <summary>
    /// Starts the wind-streak visual for a Gust cast from <paramref name="origin"/>
    /// along <paramref name="direction"/>. The physics push itself is applied
    /// immediately by the caller (World.CastGust) — this only animates it.
    /// </summary>
    public void QueueGust(Vector3 origin, Vector3 direction)
    {
        _gusts.Add(new GustEffect(origin, direction, GustVisualDuration));
    }

    /// <summary>Counts down every shadow and gust, releasing the pebble for any shadow that expired.</summary>
    public void Update(float deltaTime, PhysicsManager physics)
    {
        // Iterate backwards so expired entries can be removed in place.
        for (int i = _shadows.Count - 1; i >= 0; i--)
        {
            GodShadow shadow = _shadows[i];
            shadow.Tick(deltaTime);
            if (!shadow.HasExpired)
                continue;

            var spawn = shadow.Center + new Vector3(0, PebbleSpawnHeight, 0);
            physics.Add(new PhysicsObject(spawn, PebbleRadius, PebbleMass, Color.Gray, PebbleLifetime));
            _shadows.RemoveAt(i);
        }

        for (int i = _gusts.Count - 1; i >= 0; i--)
        {
            _gusts[i].Tick(deltaTime);
            if (_gusts[i].HasExpired)
                _gusts.RemoveAt(i);
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

        foreach (var gust in _gusts)
            DrawGustStreaks(gust);
    }

    /// <summary>
    /// A handful of white streak lines that race out from <paramref name="gust"/>'s
    /// origin along its direction and fade as they go, like a burst of wind
    /// made visible. Purely decorative — the actual push already happened.
    /// </summary>
    private static void DrawGustStreaks(GustEffect gust)
    {
        const int StreakCount = 6;
        const float HalfSpread = 1.8f;    // Roughly matches World's Gust corridor width.
        const float StreakLength = 3.5f;
        const float TravelDistance = 26f; // How far the streak heads race out over the visual's life.

        var direction2D = new Vector2(gust.Direction.X, gust.Direction.Z);
        var side = new Vector2(-direction2D.Y, direction2D.X); // Perpendicular, for lateral spread.
        var sideOffset3D = new Vector3(side.X, 0, side.Y);

        var color = new Color(255, 255, 255, (int)(210 * (1f - gust.Progress)));

        // A stable per-streak lateral offset, so the streaks fan out instead
        // of all riding the same line, without needing per-frame randomness.
        for (int i = 0; i < StreakCount; i++)
        {
            float lateral = (i - (StreakCount - 1) / 2f) / StreakCount * 2f * HalfSpread;
            float headDistance = gust.Progress * TravelDistance + i * 0.5f; // Staggered starts.
            float tailDistance = MathF.Max(0f, headDistance - StreakLength);

            Vector3 offset = sideOffset3D * lateral + new Vector3(0, 0.05f, 0);
            Vector3 head = gust.Origin + gust.Direction * headDistance + offset;
            Vector3 tail = gust.Origin + gust.Direction * tailDistance + offset;
            Raylib.DrawLine3D(tail, head, color);
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
///   faith regen -> miracles (shadows, pebble release)
///   -> physics (+ acorn cracking, spider squishing)
///   -> shard sliding (from a Gust) -> obstacle list -> shove food out from
///   under rocks -> Bramblekin -> Wolf Spider (may kill Bramblekin)
///   -> acorn / spider respawn
///
/// A cast Gust (CastGust) sits outside this per-frame order: it applies its
/// push to everything caught in its corridor all at once, the instant it's
/// cast, rather than as a lingering per-frame force.
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

    /// <summary>
    /// Number of Food Shards a cracked acorn yields. High-yield: the reward
    /// for spending Faith on a Pebble-Drop, well above a Berry's 1 food or
    /// an Aphid's 2.
    /// </summary>
    private const int ShardsPerAcorn = 4;

    /// <summary>Faith cap. Also the starting amount: the colony begins devout.</summary>
    public const float MaxFaith = 100f;

    /// <summary>Faith each living Bramblekin generates per second (0.5 x population per second).</summary>
    public const float FaithPerBramblekinPerSecond = 0.5f;

    /// <summary>Food Stored consumed to sprout one new Bramblekin at the Village Heart.</summary>
    public const int FoodPerSprout = 5;

    /// <summary>
    /// A pebble whose centre lands within this distance (m) of the spider's
    /// centre crushes it. Tight on purpose: a direct hit, not a near miss.
    /// </summary>
    public const float SquishRadius = 0.4f;

    /// <summary>Seconds after a spider is crushed before a new one appears at an edge.</summary>
    public const float SpiderRespawnDelay = 45f;

    /// <summary>How long a crushed spider's splat mark stays on the ground, in seconds.</summary>
    private const float SplatDuration = 6f;

    /// <summary>How far (m) the Gust's wind corridor reaches from the swipe's start point.</summary>
    public const float GustCorridorLength = 30f;

    /// <summary>Half-width (m) of the wind corridor — "wide" per the design.</summary>
    public const float GustCorridorHalfWidth = 2f;

    /// <summary>Speed (m/s) a loose Food Shard is flung to when caught in a Gust.</summary>
    public const float ShardGustSpeed = 5f;

    /// <summary>How quickly a flung shard's speed bleeds off, per second.</summary>
    public const float ShardFriction = 2.5f;

    /// <summary>Below this speed (m/s) a sliding shard is simply stopped.</summary>
    private const float ShardRestSpeed = 0.05f;

    /// <summary>One-time position nudge (m) a Bramblekin gets from a Gust.</summary>
    public const float BramblekinGustPush = 1f;

    /// <summary>One-time knockback (m) the Wolf Spider gets from a Gust.</summary>
    public const float SpiderGustKnockback = 3f;

    /// <summary>How often a wild Berry appears, in seconds.</summary>
    public const float BerrySpawnInterval = 8f;

    /// <summary>Most Berries allowed on the map (loose or carried) at once.</summary>
    public const int MaxBerries = 5;

    /// <summary>Aphid population the world tries to maintain.</summary>
    public const int MaxAphids = 3;

    /// <summary>Seconds between checks that top the Aphid population back up after a loss.</summary>
    public const float AphidRespawnDelay = 12f;

    /// <summary>Food Shards a Militia-hunted Aphid drops.</summary>
    private const int AphidFoodShardYield = 2;

    private readonly List<Obstacle> _obstacles = new();
    private readonly List<(Vector3 Position, float TimeLeft)> _splats = new();
    private float _acornRespawnTimer;

    // Deferred creation/destruction. Nothing below is added to or removed
    // from Colony/FoodShards while any part of the frame might still be
    // iterating them (a returning Bramblekin sprouting a new one while the
    // Colony foreach that is updating it is still running, for example).
    // Entities are instead queued here and the queues are drained once, in
    // CommitPendingChanges(), after every Update() and Draw() this frame.
    private readonly List<Bramblekin> _pendingBramblekinSpawns = new();
    private readonly List<Bramblekin> _pendingBramblekinRemovals = new();
    private readonly List<FoodShard> _pendingShardSpawns = new();
    private readonly List<FoodShard> _pendingShardRemovals = new();
    private readonly List<Aphid> _pendingAphidSpawns = new();
    private readonly List<Aphid> _pendingAphidRemovals = new();

    private float _berrySpawnTimer = BerrySpawnInterval;
    private float _aphidRespawnTimer = AphidRespawnDelay;

    public Terrain Terrain { get; }
    public PhysicsManager Physics { get; }
    public MiracleManager Miracles { get; } = new();
    public VillageHeart Village { get; }
    public Acorn? Acorn { get; private set; }
    public List<FoodShard> FoodShards { get; } = new();
    public List<Bramblekin> Colony { get; } = new();
    public List<Aphid> Aphids { get; } = new();
    public WolfSpider? Spider { get; set; }
    public Random Rng { get; }

    /// <summary>Bramblekin lost to predators so far.</summary>
    public int Casualties { get; private set; }

    /// <summary>Bramblekin sprouted from stored food so far.</summary>
    public int Births { get; private set; }

    /// <summary>Food in the village stores, waiting to become the next sprout.</summary>
    public int FoodStored { get; private set; }

    /// <summary>The god's power budget. Miracles spend it; worship refills it.</summary>
    public float Faith { get; set; } = MaxFaith;

    /// <summary>Seconds until a crushed spider is replaced (only meaningful while <see cref="Spider"/> is null).</summary>
    public float SpiderRespawnTimer { get; private set; }

    /// <summary>Spiders crushed by direct pebble hits so far.</summary>
    public int SpidersCrushed { get; private set; }

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

        for (int i = 0; i < MaxAphids; i++)
            Aphids.Add(new Aphid(RandomFreePoint(Aphid.BodyRadius, Aphid.EdgeMargin), rng));
    }

    /// <summary>
    /// The Armory: finds the closest Gatherer to the Village Heart and
    /// permanently drafts it into the Militia. Returns false (and does
    /// nothing) if every living Bramblekin is already Militia.
    /// </summary>
    public bool DraftMilitia()
    {
        Bramblekin? nearest = null;
        float bestDistanceSquared = float.MaxValue;
        for (int i = Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = Colony[i];
            if (bramblekin.IsDead || bramblekin.Role == BramblekinRole.Militia)
                continue;

            float distanceSquared = Vector3.DistanceSquared(bramblekin.Position, Village.Center);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                nearest = bramblekin;
            }
        }

        if (nearest is null)
            return false;

        nearest.PromoteToMilitia();
        return true;
    }

    /// <summary>Whether "Draft Militia" currently has anyone left to draft.</summary>
    public bool HasDraftableGatherer => Colony.Any(b => !b.IsDead && b.Role == BramblekinRole.Gatherer);

    /// <summary>Spawns a Wolf Spider at a random spot just inside one of the terrain's edges.</summary>
    public void SpawnSpiderNearEdge()
    {
        float inset = Terrain.Size / 2f - 1.5f;
        float along = (float)(Rng.NextDouble() * 2 - 1) * inset;
        Vector3 position = Rng.Next(4) switch
        {
            0 => new Vector3(-inset, Terrain.GroundHeight, along),
            1 => new Vector3(inset, Terrain.GroundHeight, along),
            2 => new Vector3(along, Terrain.GroundHeight, -inset),
            _ => new Vector3(along, Terrain.GroundHeight, inset),
        };
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
    }

    /// <summary>Pays for a miracle. Returns false (and spends nothing) if there isn't enough Faith.</summary>
    public bool TrySpendFaith(float amount)
    {
        if (Faith < amount)
            return false;

        Faith -= amount;
        return true;
    }

    /// <summary>
    /// The Gust miracle: starts the wind-streak visual and immediately shoves
    /// every loose Food Shard, Bramblekin and the Wolf Spider caught in a
    /// wide corridor running from <paramref name="origin"/> along
    /// <paramref name="direction"/> (a horizontal unit vector) across the
    /// terrain. Faith is spent by the caller before this is invoked.
    /// </summary>
    public void CastGust(Vector3 origin, Vector3 direction)
    {
        Miracles.QueueGust(origin, direction);

        for (int i = FoodShards.Count - 1; i >= 0; i--)
        {
            FoodShard shard = FoodShards[i];
            if (!shard.IsCarried && IsInGustCorridor(origin, direction, shard.Position))
                shard.Velocity = direction * ShardGustSpeed;
        }

        for (int i = Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = Colony[i];
            if (!bramblekin.IsDead && IsInGustCorridor(origin, direction, bramblekin.Position))
                bramblekin.ApplyWindPush(direction * BramblekinGustPush, this);
        }

        if (Spider is { } spider && IsInGustCorridor(origin, direction, spider.Position))
            spider.ApplyWindPush(direction * SpiderGustKnockback, this);
    }

    /// <summary>
    /// True if <paramref name="point"/> lies within the wide rectangular
    /// corridor running from <paramref name="origin"/> along
    /// <paramref name="direction"/> (both horizontal; <paramref name="direction"/>
    /// must already be a unit vector).
    /// </summary>
    private static bool IsInGustCorridor(Vector3 origin, Vector3 direction, Vector3 point)
    {
        var o = new Vector2(origin.X, origin.Z);
        var d = new Vector2(direction.X, direction.Z);
        var toPoint = new Vector2(point.X, point.Z) - o;

        float along = Vector2.Dot(toPoint, d);
        if (along < 0f || along > GustCorridorLength)
            return false;

        Vector2 perpendicular = toPoint - d * along;
        return perpendicular.LengthSquared() <= GustCorridorHalfWidth * GustCorridorHalfWidth;
    }

    /// <summary>Whether a Militia unit currently has anything to hunt.</summary>
    public bool HasHuntableAphid => Aphids.Any(a => !a.IsDead);

    /// <summary>The nearest still-live Aphid to <paramref name="from"/>, if any.</summary>
    public Aphid? NearestLiveAphid(Vector3 from)
    {
        Aphid? nearest = null;
        float bestDistanceSquared = float.MaxValue;
        for (int i = Aphids.Count - 1; i >= 0; i--)
        {
            Aphid aphid = Aphids[i];
            if (aphid.IsDead)
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
            _pendingShardSpawns.Add(new FoodShard(position, FoodShardKind.Cracked));
        }
    }

    /// <summary>Passive Foraging: spawns a wild Berry every <see cref="BerrySpawnInterval"/> s, up to <see cref="MaxBerries"/>.</summary>
    private void UpdateBerrySpawn(float deltaTime)
    {
        _berrySpawnTimer -= deltaTime;
        if (_berrySpawnTimer > 0f)
            return;
        _berrySpawnTimer = BerrySpawnInterval;

        int berries = FoodShards.Count(s => s.Kind == FoodShardKind.Berry)
                    + _pendingShardSpawns.Count(s => s.Kind == FoodShardKind.Berry);
        if (berries >= MaxBerries)
            return;

        Vector3 spot = RandomFreePoint(FoodShard.Radius + 0.3f, edgeMargin: 1f);
        _pendingShardSpawns.Add(new FoodShard(spot, FoodShardKind.Berry));
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
    /// Slides any Food Shard the Gust has flung, decelerating it with
    /// friction each frame until it stops. Shards at rest (the common case)
    /// are skipped entirely.
    /// </summary>
    private void UpdateShardPhysics(float deltaTime)
    {
        float half = Terrain.Size / 2f - FoodShard.Radius;
        for (int i = FoodShards.Count - 1; i >= 0; i--)
        {
            FoodShard shard = FoodShards[i];
            if (shard.IsCarried || shard.Velocity == Vector3.Zero)
                continue;

            shard.Position += shard.Velocity * deltaTime;

            float damping = MathF.Max(0f, 1f - ShardFriction * deltaTime);
            Vector3 velocity = shard.Velocity * damping;
            shard.Velocity = velocity.LengthSquared() < ShardRestSpeed * ShardRestSpeed ? Vector3.Zero : velocity;

            shard.Position = new Vector3(
                Math.Clamp(shard.Position.X, -half, half),
                Terrain.GroundHeight,
                Math.Clamp(shard.Position.Z, -half, half));
        }
    }

    public void Update(float deltaTime)
    {
        // Worship: every living Bramblekin feeds the Faith pool, so each
        // loss weakens the player's miracles as well as the economy.
        // Counted as !IsDead rather than Colony.Count: a kill this frame is
        // marked dead immediately but its removal from Colony is deferred to
        // the end of the frame, and a just-caught Bramblekin shouldn't still
        // be tithing.
        int livingPopulation = Colony.Count(b => !b.IsDead);
        Faith = MathF.Min(MaxFaith, Faith + FaithPerBramblekinPerSecond * livingPopulation * deltaTime);

        Miracles.Update(deltaTime, Physics);
        Physics.Update(deltaTime);
        CrackAcornOnImpact();
        SquishSpiderOnImpact();

        UpdateShardPhysics(deltaTime);
        RebuildObstacles();
        PushFoodOutOfObstacles();

        // Ambient prey moves before the colony reacts to it this frame —
        // same ordering as pebbles settling before Bramblekin steer round
        // them. Reverse for-loop: a Militia unit's own Update() (below) can
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

        Spider?.Update(deltaTime, this);

        UpdateAcornRespawn(deltaTime);
        UpdateSpiderRespawn(deltaTime);
        UpdateBerrySpawn(deltaTime);
        UpdateAphidRespawn(deltaTime);

        for (int i = _splats.Count - 1; i >= 0; i--)
        {
            var splat = _splats[i];
            splat.TimeLeft -= deltaTime;
            if (splat.TimeLeft <= 0f)
                _splats.RemoveAt(i);
            else
                _splats[i] = splat;
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

        if (_pendingShardRemovals.Count > 0)
        {
            for (int i = _pendingShardRemovals.Count - 1; i >= 0; i--)
                FoodShards.Remove(_pendingShardRemovals[i]);
            _pendingShardRemovals.Clear();
        }

        if (_pendingShardSpawns.Count > 0)
        {
            FoodShards.AddRange(_pendingShardSpawns);
            _pendingShardSpawns.Clear();
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
    }

    public void Draw()
    {
        Terrain.Draw();
        for (int i = _splats.Count - 1; i >= 0; i--)
        {
            var (position, timeLeft) = _splats[i];
            // A dark stain that fades out.
            byte alpha = (byte)(200 * Math.Clamp(timeLeft / 2f, 0f, 1f));
            Raylib.DrawCylinder(position + new Vector3(0, 0.012f, 0), 0.9f, 0.9f, 0.005f, 20, new Color(30, 25, 20, (int)alpha));
        }
        Miracles.Draw();
        Village.Draw();
        Acorn?.Draw();

        for (int i = FoodShards.Count - 1; i >= 0; i--)
        {
            FoodShard shard = FoodShards[i];
            if (!shard.IsCarried)
                shard.Draw(shard.Position);
        }

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
            if (!Colony[i].IsDead)
                Colony[i].Draw();
        }

        Spider?.Draw();
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
    /// A shard can be gathered if nobody is carrying it and no God's Shadow
    /// is over it (walking in there would be suicidal). Rocks never cover
    /// shards — they shove them aside — but the blocked check stays as a
    /// safety net for a shard wedged somewhere unreachable.
    /// </summary>
    public bool IsAvailable(FoodShard shard) =>
        !shard.IsCarried && !IsBlocked(shard.Position, 0f) && ShadowOver(shard.Position, FoodShard.Radius) is null;

    public bool HasAvailableFood => FoodShards.Any(IsAvailable);

    public FoodShard? NearestAvailableShard(Vector3 from)
    {
        FoodShard? best = null;
        float bestDistance = float.MaxValue;
        for (int i = FoodShards.Count - 1; i >= 0; i--)
        {
            FoodShard shard = FoodShards[i];
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

    /// <summary>
    /// A delivered shard leaves the map and adds to the village stores.
    /// Bramblekin are plant-based: every <see cref="FoodPerSprout"/> stored
    /// food is spent at once to sprout a new one at the Village Heart. There
    /// is no population cap.
    ///
    /// This is called from inside a Bramblekin's own Update(), which is
    /// itself inside World's reverse for-loop over Colony — so the shard's
    /// removal and any resulting sprout are both queued, never applied to
    /// FoodShards/Colony directly here.
    /// </summary>
    public void DeliverFood(FoodShard shard)
    {
        if (!_pendingShardRemovals.Contains(shard))
            _pendingShardRemovals.Add(shard);
        FoodStored++;

        while (FoodStored >= FoodPerSprout)
        {
            FoodStored -= FoodPerSprout;
            SproutBramblekin();
        }
    }

    /// <summary>Queues a new Bramblekin on a free spot right beside the Village Heart.</summary>
    private void SproutBramblekin()
    {
        float distance = Village.Obstacle.Radius + Bramblekin.BodyRadius + 0.2f;
        float startAngle = (float)(Rng.NextDouble() * MathF.Tau);
        Vector3 spot = Village.Center + new Vector3(distance, 0, 0);

        // Try 12 spots round the village; take the first free one.
        for (int i = 0; i < 12; i++)
        {
            float angle = startAngle + i * MathF.Tau / 12;
            var candidate = Village.Center + new Vector3(MathF.Cos(angle) * distance, 0, MathF.Sin(angle) * distance);
            if (!IsBlocked(candidate, Bramblekin.BodyRadius) && Terrain.Contains(candidate, Bramblekin.EdgeMargin))
            {
                spot = candidate;
                break;
            }
        }

        _pendingBramblekinSpawns.Add(new Bramblekin(spot, Rng));
        Births++;
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
    /// shouldn't make anyone swerve). A pebble that has nearly shrunk away
    /// (see PhysicsObject's end-of-life shrink) is skipped too, so
    /// Bramblekin start walking through the spot as it fades rather than
    /// stopping dead at an invisible speck.
    /// </summary>
    private void RebuildObstacles()
    {
        _obstacles.Clear();
        _obstacles.Add(Village.Obstacle);

        for (int i = Physics.Objects.Count - 1; i >= 0; i--)
        {
            PhysicsObject? pebble = Physics.Objects[i];
            // Defensive: obstacle avoidance must never see a null/garbage
            // entry here, however this list is populated in the future.
            if (pebble is null || pebble.Radius <= 0.01f)
                continue;

            if (pebble.Position.Y - pebble.Radius < Bramblekin.BodyHeight)
                _obstacles.Add(new Obstacle(new Vector2(pebble.Position.X, pebble.Position.Z), pebble.Radius));
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
            if (shard.IsCarried)
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
    /// Queued rather than added directly: this runs from inside World.Update
    /// before the Colony pass, and the new shards should only become visible
    /// to gatherers on a clean iteration next frame.
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
            _pendingShardSpawns.Add(new FoodShard(position));
        }
    }

    /// <summary>
    /// The high-skill reward: a pebble whose centre lands right on top of the
    /// Wolf Spider crushes it. (A spider staring at a distraction, or
    /// feeding, stands still — that's the moment to strike.)
    /// </summary>
    private void SquishSpiderOnImpact()
    {
        if (Spider is null)
            return;

        foreach (var impact in Physics.Impacts)
        {
            if (GroundMover.HorizontalDistance(impact.Point, Spider.Position) > SquishRadius)
                continue;

            _splats.Add((Spider.Position, SplatDuration));
            Spider = null;
            SpidersCrushed++;
            SpiderRespawnTimer = SpiderRespawnDelay;
            return;
        }
    }

    private void UpdateSpiderRespawn(float deltaTime)
    {
        if (Spider is not null || SpiderRespawnTimer <= 0f)
            return;

        SpiderRespawnTimer -= deltaTime;
        if (SpiderRespawnTimer <= 0f)
            SpawnSpiderNearEdge();
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

/// <summary>Where a Food Shard came from — purely cosmetic, it's worth the same 1 food either way.</summary>
public enum FoodShardKind
{
    /// <summary>Cracked from an Acorn (by a Pebble-Drop) or dropped by a hunted Aphid. Orange.</summary>
    Cracked,

    /// <summary>Passive Foraging: a wild Berry, grabbable without spending Faith. Red.</summary>
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
    public FoodShardKind Kind { get; }

    /// <summary>
    /// Ground-plane sliding speed. Zero at rest; a Gust sets it, and World's
    /// shard-physics step bleeds it off with friction each frame. Ignored
    /// while carried.
    /// </summary>
    public Vector3 Velocity { get; set; }

    public FoodShard(Vector3 groundPoint, FoodShardKind kind = FoodShardKind.Cracked)
    {
        Position = groundPoint;
        Kind = kind;
    }

    /// <summary>Draws the shard resting on the ground at (or carried above) <paramref name="groundPoint"/>.</summary>
    public void Draw(Vector3 groundPoint)
    {
        Color color = Kind == FoodShardKind.Berry ? new Color(210, 40, 45, 255) : new Color(245, 150, 45, 255);
        Raylib.DrawSphere(groundPoint + new Vector3(0, Radius, 0), Radius, color);
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
    /// <summary>Within this distance of a target counts as "arrived".</summary>
    private const float ArriveDistance = 0.05f;

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
    /// Gust knockback — then re-clamps it to the terrain and pushes it back
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
            Vector2 heading = Steer(position, toGoal / distance, MathF.Min(distance, LookAhead), goal, world.Obstacles);
            position += heading * step;
            Heading = heading;
        }

        Vector3 before = Position;
        Position = new Vector3(position.X, Terrain.GroundHeight, position.Y);
        PushOutOfObstacles(world.Obstacles);
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

    /// <summary>Running at 3x speed from a God's Shadow or a predator.</summary>
    Fleeing,

    /// <summary>Militia only: charging the Wolf Spider to intercept it before it reaches the village.</summary>
    Defending,

    /// <summary>Militia only: chasing down the nearest Aphid.</summary>
    Hunting,
}

/// <summary>A Bramblekin's class: an ordinary worker, or a drafted defender.</summary>
public enum BramblekinRole
{
    /// <summary>Gathers food; flees the Wolf Spider like everyone else.</summary>
    Gatherer,

    /// <summary>Drafted via "Draft Militia". Never gathers; instead defends the colony and hunts Aphids.</summary>
    Militia,
}

/// <summary>
/// One of the tiny creatures the player protects. The player never controls
/// them directly; each runs a small state machine, checked in priority order
/// every frame:
///
///   1. God's Shadow (always wins, every role): standing under a shadow drops
///      any carried food and sends it Fleeing at 3x speed to the nearest safe
///      spot — even with a spider on its tail.
///   2. The Wolf Spider, within <see cref="FearRadius"/>: a Gatherer drops
///      its food and Flees directly away; a Militia unit instead Defends —
///      charging in to stand between the spider and the Village Heart.
///   3. Economy (Gatherers only): while Food Shards are available, wandering
///      (Walking/Pausing) is overridden by Gathering -> pick up ->
///      Returning -> deliver.
///   3b. Hunting (Militia only): with no spider to fight, wandering is
///      overridden by chasing down the nearest Aphid.
///   4. Wandering: Walking to a random free point, Pausing 2 s, repeat.
///      Shared by both roles as the default idle behaviour.
///
/// Gathering and Returning Bramblekin shake the ground; that is what the Wolf
/// Spider hunts by (<see cref="IsVibrating"/>). Militia never gather, so they
/// never draw that attention — the spider only notices them by running into
/// one (see WolfSpider's Pike Defense in Pounce()).
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

    /// <summary>
    /// Fear Aura: a Wolf Spider closer than this (m) sends the Bramblekin
    /// running. Deliberately a little shorter than the spider's pounce range,
    /// so a hunting spider gets the jump on distracted workers — which is why
    /// the player's pebble distraction matters.
    /// </summary>
    public const float FearRadius = 2.0f;

    /// <summary>How far past the Fear Aura a frightened Bramblekin aims to run, in meters.</summary>
    private const float PredatorFleeMargin = 3f;

    /// <summary>Extra clearance beyond the shadow's edge when picking an escape point.</summary>
    private const float SafetyMargin = 0.5f;

    /// <summary>Within this distance of a shard, it is picked up.</summary>
    private const float PickupDistance = BodyRadius + FoodShard.Radius + 0.1f;

    /// <summary>Militia charge speed while Defending — faster than a gathering amble, short of a full panicked flee.</summary>
    private const float DefendSpeed = WalkSpeed * 1.5f;

    /// <summary>How far from the spider, back toward the Village Heart, a defending Militia tries to stand.</summary>
    private const float InterceptStandoff = 1.2f;

    /// <summary>
    /// Beyond this distance (m) from the spider, a Defending Militia gives up
    /// and goes back to its own business. Larger than <see cref="FearRadius"/>
    /// so it doesn't flicker in and out right at the trigger boundary.
    /// </summary>
    private const float DisengageRadius = FearRadius * 2f;

    /// <summary>Speed while chasing an Aphid.</summary>
    private const float HuntSpeed = WalkSpeed;

    /// <summary>Within this distance of an Aphid, it is caught.</summary>
    private const float HuntContactDistance = BodyRadius + Aphid.BodyRadius + 0.05f;

    private static readonly Color CalmColor = new(196, 160, 110, 255);   // Bark brown.
    private static readonly Color PanicColor = new(225, 85, 60, 255);    // Alarm red.
    private static readonly Color MilitiaColor = new(150, 130, 95, 255); // A shade duller than a Gatherer — worn, armed.
    private static readonly Color PikeColor = new(120, 55, 40, 255);     // Rose-thorn brown-red.

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private Vector3 _target;
    private float _pauseTimer;
    private FoodShard? _carried;

    /// <summary>Feet position on the ground (y = GroundHeight).</summary>
    public Vector3 Position => _mover.Position;

    public BramblekinState State { get; private set; }

    /// <summary>Gatherer by default; permanently becomes Militia via "Draft Militia".</summary>
    public BramblekinRole Role { get; private set; } = BramblekinRole.Gatherer;

    public bool IsCarrying => _carried is not null;

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

    public Bramblekin(Vector3 position, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);

        // Start mid-pause with a random timer so the colony doesn't move in lockstep.
        SetState(BramblekinState.Pausing);
        _pauseTimer = (float)rng.NextDouble() * PauseDuration;
    }

    /// <summary>
    /// Marks this Bramblekin as caught: drops any carried food immediately
    /// (so it's still gatherable) and flags it dead. Called once, from
    /// <see cref="World.Kill"/>; it does not touch <see cref="World.Colony"/>
    /// itself — that removal is deferred and processed at the end of the
    /// frame.
    /// </summary>
    public void MarkDead()
    {
        if (IsDead)
            return;

        DropCarried();
        IsDead = true;
    }

    /// <summary>
    /// The Armory: permanently reclassifies this Bramblekin as Militia. Drops
    /// anything carried and, if it was mid-Gathering or mid-Returning (a
    /// Gatherer-only errand), immediately breaks that off with a short pause
    /// rather than let it finish one last delivery — the transition is meant
    /// to be immediate. The priority chain re-decides what to do next
    /// (defend, hunt, or wander) on the very next Update().
    /// </summary>
    public void PromoteToMilitia()
    {
        if (Role == BramblekinRole.Militia)
            return;

        Role = BramblekinRole.Militia;
        DropCarried();
        if (State is BramblekinState.Gathering or BramblekinState.Returning)
            StartPause();
    }

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return; // Awaiting removal at the end of the frame; do nothing.

        _mover.Idle();
        bool isSafe(Vector3 p) => IsSafeSpot(p, world);

        // --- 1. God's Shadow (absolute priority) ------------------------------
        // A shadow flight only replans if its escape point has since been
        // covered by a newer shadow or blocked by a rock.
        GodShadow? threat = world.ShadowOver(Position, BodyRadius);
        if (threat is not null)
        {
            if (State != BramblekinState.Fleeing || !IsSafeSpot(_target, world))
            {
                DropCarried();
                _target = FindEscapePoint(threat, world);
                SetState(BramblekinState.Fleeing);
            }
        }
        // --- 2. The Wolf Spider: Gatherers flee, Militia defends --------------
        // Re-aimed every frame while the spider is close, so a fleeing
        // Gatherer keeps running straight away and a defending Militia keeps
        // adjusting where it's standing as the spider moves.
        else if (world.Spider is { } spider &&
                 GroundMover.HorizontalDistance(Position, spider.Position) < FearRadius)
        {
            if (Role == BramblekinRole.Militia)
            {
                _target = ComputeInterceptPoint(spider, world);
                if (State != BramblekinState.Defending)
                {
                    DropCarried();
                    SetState(BramblekinState.Defending);
                }
            }
            else
            {
                DropCarried();
                _target = FindPointAwayFrom(spider.Position, world);
                if (State != BramblekinState.Fleeing)
                    SetState(BramblekinState.Fleeing);
            }
        }

        // --- 3. Economy overrides wandering (Gatherers only) -------------------
        if (Role == BramblekinRole.Gatherer && State is BramblekinState.Walking or BramblekinState.Pausing && world.HasAvailableFood)
            SetState(BramblekinState.Gathering);

        // --- 3b. Militia hunts Aphids when it has no spider to fight -----------
        if (Role == BramblekinRole.Militia && State is BramblekinState.Walking or BramblekinState.Pausing && world.HasHuntableAphid)
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

                if (_mover.MoveTowards(_target, WalkSpeed, deltaTime, world, isSafe))
                    StartPause();
                break;

            case BramblekinState.Gathering:
                UpdateGathering(deltaTime, world);
                break;

            case BramblekinState.Returning:
                UpdateReturning(deltaTime, world);
                break;

            case BramblekinState.Fleeing:
                if (_mover.MoveTowards(_target, WalkSpeed * FleeSpeedMultiplier, deltaTime, world, isSafe))
                    StartPause(); // Catch its breath, then back to work.
                break;

            case BramblekinState.Defending:
                UpdateDefending(deltaTime, world);
                break;

            case BramblekinState.Hunting:
                UpdateHunting(deltaTime, world);
                break;
        }
    }

    public void Draw()
    {
        Color color = State == BramblekinState.Fleeing ? PanicColor
                    : Role == BramblekinRole.Militia ? MilitiaColor
                    : CalmColor;

        // A capsule standing upright: DrawCapsule takes the centres of its two
        // hemispherical ends, so inset them by the radius.
        var bottom = Position + new Vector3(0, BodyRadius, 0);
        var top = Position + new Vector3(0, BodyHeight - BodyRadius, 0);
        Raylib.DrawCapsule(bottom, top, BodyRadius, 8, 4, color);
        Raylib.DrawCapsuleWires(bottom, top, BodyRadius, 8, 4, new Color(0, 0, 0, 50));

        // Militia carry a Rose-Thorn Pike: a small brown line held out front,
        // angled up, so they read as armed even at a glance.
        if (Role == BramblekinRole.Militia)
        {
            Vector2 facing = _mover.Heading.LengthSquared() > 1e-6f ? _mover.Heading : Vector2.UnitX;
            var grip = Position + new Vector3(0, BodyHeight * 0.6f, 0);
            var tip = grip + new Vector3(facing.X, 0.55f, facing.Y) * 0.6f;
            Raylib.DrawLine3D(grip, tip, PikeColor);
            Raylib.DrawSphere(tip, 0.025f, PikeColor);
        }

        // Carried food rides on top of the head.
        _carried?.Draw(Position + new Vector3(0, BodyHeight, 0));
    }

    /// <summary>Puts carried food back on the ground where we stand (it can be gathered again later).</summary>
    public void DropCarried()
    {
        if (_carried is null)
            return;

        _carried.Position = Position;
        _carried.IsCarried = false;
        _carried = null;
    }

    /// <summary>
    /// The Gust's effect on a Bramblekin: an immediate, gentle shove in the
    /// wind's direction. Never touches <see cref="State"/> or its current
    /// target — it's just physically moved a little, same as running into
    /// a pebble that suddenly appeared underfoot.
    /// </summary>
    public void ApplyWindPush(Vector3 push, World world)
    {
        if (IsDead)
            return;

        _mover.Nudge(push, world);
    }

    // --- Economy states ----------------------------------------------------------

    private void UpdateGathering(float deltaTime, World world)
    {
        // Re-pick the nearest shard every frame: another Bramblekin may have
        // grabbed ours, or a shadow may have made it unreachable.
        FoodShard? shard = world.NearestAvailableShard(Position);
        if (shard is null)
        {
            StartWandering(world);
            return;
        }

        if (GroundMover.HorizontalDistance(Position, shard.Position) <= PickupDistance)
        {
            shard.IsCarried = true;
            _carried = shard;
            SetState(BramblekinState.Returning);
            return;
        }

        _mover.MoveTowards(shard.Position, WalkSpeed, deltaTime, world, p => IsSafeSpot(p, world));
    }

    private void UpdateReturning(float deltaTime, World world)
    {
        if (GroundMover.HorizontalDistance(Position, world.Village.Center) <= world.Village.DeliveryDistance)
        {
            world.DeliverFood(_carried!);
            _carried = null;

            // Defensive: PromoteToMilitia() already breaks a Gatherer errand
            // off immediately, so this path is Gatherer-only in practice —
            // but a mid-delivery promotion should never be able to walk a
            // freshly drafted Militia unit straight back into Gathering.
            if (Role == BramblekinRole.Gatherer && world.HasAvailableFood)
                SetState(BramblekinState.Gathering);
            else
                StartWandering(world);
            return;
        }

        _mover.MoveTowards(world.Village.Center, WalkSpeed, deltaTime, world, p => IsSafeSpot(p, world));
    }

    // --- Militia states -------------------------------------------------------------

    private void UpdateDefending(float deltaTime, World world)
    {
        // The spider is gone or has wandered well clear: stand down.
        if (world.Spider is not { } spider ||
            GroundMover.HorizontalDistance(Position, spider.Position) > DisengageRadius)
        {
            StartWandering(world);
            return;
        }

        _target = ComputeInterceptPoint(spider, world);
        _mover.MoveTowards(_target, DefendSpeed, deltaTime, world, p => IsSafeSpot(p, world));
    }

    /// <summary>
    /// A point <see cref="InterceptStandoff"/> meters from the spider, on the
    /// side facing the Village Heart — the spot a Militia unit tries to hold
    /// to physically get between the spider and the village.
    /// </summary>
    private static Vector3 ComputeInterceptPoint(WolfSpider spider, World world)
    {
        Vector2 toVillage = new(world.Village.Center.X - spider.Position.X, world.Village.Center.Z - spider.Position.Z);
        Vector2 direction = toVillage.LengthSquared() > 1e-6f ? Vector2.Normalize(toVillage) : Vector2.UnitX;
        return spider.Position + new Vector3(direction.X, 0, direction.Y) * InterceptStandoff;
    }

    private void UpdateHunting(float deltaTime, World world)
    {
        // Re-pick the nearest live Aphid every frame: another Militia unit
        // may have already caught ours, or it may simply have wandered off.
        Aphid? aphid = world.NearestLiveAphid(Position);
        if (aphid is null)
        {
            StartWandering(world);
            return;
        }

        if (GroundMover.HorizontalDistance(Position, aphid.Position) <= HuntContactDistance)
        {
            world.KillAphid(aphid);
            return; // Re-targets (or wanders) fresh next frame.
        }

        _mover.MoveTowards(aphid.Position, HuntSpeed, deltaTime, world, p => IsSafeSpot(p, world));
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
        _mover.ResetProgress();
    }

    // --- Fleeing ------------------------------------------------------------------

    /// <summary>
    /// Picks the closest safe spot just outside <paramref name="threat"/>:
    /// straight away from the shadow's centre if possible.
    /// </summary>
    private Vector3 FindEscapePoint(GodShadow threat, World world) =>
        FindSafePointAround(threat.Center, threat.Radius + BodyRadius + SafetyMargin, world);

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

    /// <summary>Not under a shadow and not inside a rock or the village.</summary>
    private static bool IsSafeSpot(Vector3 point, World world) =>
        world.ShadowOver(point, BodyRadius) is null && !world.IsBlocked(point, BodyRadius);
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

    /// <summary>Distracted by a pebble impact: goes to the spot and stares at it.</summary>
    Investigating,

    /// <summary>Eating a catch: stays put and ignores everything for a while.</summary>
    Feeding,

    /// <summary>Knocked over by a Gust while Hunting or Pouncing: stunned, does nothing for a few seconds.</summary>
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
/// The player's counter is a pebble: any hard landing within
/// <see cref="ImpactHearingRadius"/> is a far stronger vibration than a
/// Bramblekin's footsteps, so the spider instantly abandons whatever it was
/// doing (even mid-pounce) and goes to investigate the impact, staring at
/// it for 3 s before resuming its prowl.
///
/// A second counter, The Gust, always knocks it back physically; if it was
/// actively Hunting or Pouncing, that shove also tumbles it — stunned,
/// doing nothing — for <see cref="TumbledDuration"/> seconds. A third: Pike
/// Defense, when a Pounce lands on a Militia unit instead of a Gatherer —
/// the unit survives and the failed pounce tumbles the spider the same way.
/// </summary>
public sealed class WolfSpider
{
    /// <summary>Collision radius (m) — twice a Bramblekin's.</summary>
    public const float BodyRadius = Bramblekin.BodyRadius * 2f;

    /// <summary>How far (m) it can feel a gathering/returning Bramblekin's footsteps.</summary>
    public const float VibrationRadius = 7f;

    /// <summary>How far (m) it can feel a pebble slam into the ground — much further than footsteps.</summary>
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

    /// <summary>How long a Gust-tumbled spider is stunned for, in seconds.</summary>
    public const float TumbledDuration = 4f;

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

    public Vector3 Position => _mover.Position;

    public SpiderState State { get; private set; } = SpiderState.Prowling;

    /// <summary>Bramblekin killed so far.</summary>
    public int Kills { get; private set; }

    public WolfSpider(Vector3 position, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, edgeMargin: 1f, rng);
        _target = position;
        _timer = ProwlPauseDuration;
    }

    public void Update(float deltaTime, World world)
    {
        _mover.Idle();

        // Tumbled is a hard lock, checked and handled before anything else
        // in this method — the prey safety net, the pebble-thud distraction,
        // every bit of vision/AI below. Nothing can re-target, re-notice or
        // otherwise step on the stun early; the only way out is the timer
        // running down. This used to be one case at the end of the switch
        // below, reachable only if nothing upstream happened to touch state
        // first — exactly the kind of thing a new interrupt source (a Pike
        // Defense block, say) could quietly break. A dedicated early return
        // makes that structurally impossible instead of relying on every
        // future addition to remember to check for it.
        if (State == SpiderState.Tumbled)
        {
            _timer -= deltaTime;
            if (_timer <= 0f)
                StartProwling();
            return;
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

        // --- The Distraction: a pebble impact trumps everything but a meal ----
        foreach (var impact in State == SpiderState.Feeding ? [] : world.Physics.Impacts)
        {
            if (GroundMover.HorizontalDistance(Position, impact.Point) <= ImpactHearingRadius)
            {
                StartInvestigating(impact.Point);
                break;
            }
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

    /// <summary>
    /// The Gust's counter to the spider: always a strong physical knockback.
    /// If it was actively Hunting or Pouncing, the shove also interrupts
    /// that — dropping any tracked prey and knocking it into the Tumbled
    /// (stunned) state for <see cref="TumbledDuration"/> seconds before it
    /// resets to Prowling. Caught in any other state, it's simply shoved;
    /// whatever it was doing (prowling, staring, eating) carries on.
    /// </summary>
    public void ApplyWindPush(Vector3 push, World world)
    {
        _mover.Nudge(push, world);

        if (State is SpiderState.Hunting or SpiderState.Pouncing)
        {
            _prey = null;
            _timer = TumbledDuration;
            SetState(SpiderState.Tumbled);
        }
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
            GroundMover.HorizontalDistance(Position, _prey.Position) > VibrationRadius * 1.5f)
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

        // Anything it touches mid-pounce: a Militia unit in range always
        // wins the check over a Gatherer (it's there to intercept, that's
        // the point of the Phalanx), even if both would technically overlap
        // at once. Reverse for-loop: World.Kill only queues the removal now,
        // so Colony never actually changes size during this walk, but the
        // pattern stays consistent everywhere.
        Bramblekin? militiaHit = null;
        Bramblekin? gathererHit = null;
        for (int i = world.Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = world.Colony[i];
            if (bramblekin.IsDead)
                continue;

            if (GroundMover.HorizontalDistance(Position, bramblekin.Position) >= BodyRadius + Bramblekin.BodyRadius)
                continue;

            if (bramblekin.Role == BramblekinRole.Militia)
            {
                militiaHit = bramblekin;
                break; // Found the one that matters; no need to keep scanning.
            }
            gathererHit ??= bramblekin;
        }

        if (militiaHit is not null)
        {
            // Pike Defense: the pounce is blocked, not a kill. The Militia
            // unit survives; the shove that would have been the kill instead
            // knocks the spider straight into Tumbled.
            _prey = null;
            _timer = TumbledDuration;
            SetState(SpiderState.Tumbled);
            return;
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
        float bestDistance = VibrationRadius;
        for (int i = world.Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = world.Colony[i];
            // IsVibrating is already false for a dead Bramblekin; checked
            // again explicitly so this never targets one even if that changes.
            if (bramblekin.IsDead || !bramblekin.IsVibrating)
                continue;

            float distance = GroundMover.HorizontalDistance(Position, bramblekin.Position);
            if (distance <= bestDistance)
            {
                best = bramblekin;
                bestDistance = distance;
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
        float bestDistance = FleeTriggerRadius;
        for (int i = world.Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = world.Colony[i];
            if (bramblekin.IsDead)
                continue;

            float distance = GroundMover.HorizontalDistance(Position, bramblekin.Position);
            if (distance <= bestDistance)
            {
                nearest = bramblekin;
                bestDistance = distance;
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
    private const int FontSize = 20;

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

        // Centre the label inside the rectangle.
        int textWidth = Raylib.MeasureText(label, FontSize);
        int x = (int)(Bounds.X + (Bounds.Width - textWidth) / 2f);
        int y = (int)(Bounds.Y + (Bounds.Height - FontSize) / 2f);
        Raylib.DrawText(label, x, y, FontSize, disabled && !highlighted ? new Color(90, 80, 75, 255) : Color.Black);
    }
}
