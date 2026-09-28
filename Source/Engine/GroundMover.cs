using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Walks a round body across the terrain: steers around obstacles, pushes
/// itself back out of anything it overlaps, stays on the terrain, never sets
/// foot in the pond (finding a way round it — see <see cref="WaterMap"/>),
/// and takes a short sideways detour if it stops making progress. Used by
/// the Bramblekin and every creature that walks.
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

    /// <param name="walks">Walkers (true) keep out of the pond and find their way round it; fliers (false) go straight over.</param>
    public GroundMover(Vector3 position, float bodyRadius, float edgeMargin, Random rng, bool walks = true)
    {
        Position = position;
        BodyRadius = bodyRadius;
        _edgeMargin = edgeMargin;
        _rng = rng;
        _walks = walks;
        ResetProgress();
    }

    private readonly bool _walks;

    // Walking round the water: the waypoints of the way round (null when the
    // straight way is clear), the next one, and the target they lead to.
    private List<Vector2>? _route;
    private int _routeIndex;
    private Vector2 _routeGoal = new(float.NaN, float.NaN);

    /// <summary>The water level (see <see cref="WaterMap.Generation"/>) the way round was worked out at.</summary>
    private int _routeGeneration;

    /// <summary>A target that moves this far (m) since the way to it was worked out gets a fresh look.</summary>
    private const float RouteGoalDrift = 1f;

    /// <summary>A waypoint this close (m) counts as reached.</summary>
    private const float WaypointReach = 0.6f;

    /// <summary>Forget any detour and restart stuck detection (call on every change of plan).</summary>
    public void ResetProgress()
    {
        _detour = null;
        _routeGoal = new Vector2(float.NaN, float.NaN);
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
        Vector3 before = Position;
        Position += offset;
        PushOutOfObstacles(world.ObstaclesNear(Position));
        ClampToTerrain(world.Terrain);
        KeepOutOfWater(before);
    }

    /// <summary>
    /// Moves toward <paramref name="target"/> at <paramref name="speed"/>,
    /// steering around obstacles, then pushes the body back out of anything
    /// it still overlaps. <paramref name="isSafeSpot"/> vets detour points
    /// (it's handed the World, so callers can pass a lambda that captures
    /// nothing — no allocation on every step).
    /// Returns true on arrival.
    /// </summary>
    public bool MoveTowards(Vector3 target, float speed, float deltaTime, World world, Func<World, Vector3, bool> isSafeSpot)
    {
        IReadOnlyList<Obstacle> obstacles = world.ObstaclesNear(Position);
        float step = speed * deltaTime;
        CheckIfStuck(target, step, deltaTime, world, isSafeSpot);

        // Head for the detour waypoint first, if we're working our way round
        // something — else for the next waypoint round the water, if any.
        bool onRoute = false;
        Vector3 goal = _detour ?? (_walks ? RouteTowards(target, out onRoute) : target);
        // A goal past MapBoundaryLimit can never be reached — the bounds rule
        // below turns the walker back before it gets there, and it would pace
        // back and forth at the limit forever (a prowling spider stuck at the
        // map's edge). Head for the nearest point inside the limit instead.
        goal = new Vector3(Math.Clamp(goal.X, -MapBoundaryLimit, MapBoundaryLimit), goal.Y, Math.Clamp(goal.Z, -MapBoundaryLimit, MapBoundaryLimit));
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
        KeepOutOfWater(before);
        IsMoving = Vector3.DistanceSquared(before, Position) > 1e-8f;

        if (arrived && _detour is not null)
        {
            _detour = null; // Detour done; resume toward the real target next frame.
            return false;
        }
        if (arrived && onRoute)
        {
            _routeIndex++; // On to the next waypoint round the water.
            return false;
        }
        return arrived;
    }

    /// <summary>
    /// Where to head for on the way to <paramref name="target"/>: the target
    /// itself if the straight way is clear of the water, else the next
    /// waypoint of a way round it (<paramref name="onRoute"/> true while
    /// there are more waypoints after it). The way is worked out again only
    /// when the target moves (see <see cref="RouteGoalDrift"/>).
    /// </summary>
    private Vector3 RouteTowards(Vector3 target, out bool onRoute)
    {
        onRoute = false;
        var here = new Vector2(Position.X, Position.Z);
        var goal = new Vector2(target.X, target.Z);
        if (!(Vector2.DistanceSquared(goal, _routeGoal) <= RouteGoalDrift * RouteGoalDrift) || _routeGeneration != WaterMap.Generation) // (NaN-safe: a fresh mover always looks.)
        {
            _routeGoal = goal;
            _routeGeneration = WaterMap.Generation;
            _routeIndex = 0;
            _route = WaterMap.IsClearWay(here, goal) ? null : WaterMap.FindRoute(here, goal);
        }
        if (_route is null)
            return target;

        // Already close to a waypoint (pushed past it by the crowd)? On to the next.
        while (_routeIndex < _route.Count - 1 && Vector2.DistanceSquared(here, _route[_routeIndex]) <= WaypointReach * WaypointReach)
            _routeIndex++;
        if (_routeIndex >= _route.Count)
        {
            _route = null; // Round the water: straight on from here.
            return target;
        }

        Vector2 waypoint = _route[_routeIndex];
        onRoute = _routeIndex < _route.Count - 1;
        return new Vector3(waypoint.X, Terrain.GroundHeight, waypoint.Y);
    }

    /// <summary>A walker never steps from dry ground into the pond: it slides along the shore if it can, else stays put.</summary>
    private void KeepOutOfWater(Vector3 before)
    {
        if (!_walks || !WaterMap.IsWet(Position.X, Position.Z) || WaterMap.IsWet(before.X, before.Z))
            return;
        if (!WaterMap.IsWet(Position.X, before.Z))
            Position = new Vector3(Position.X, Terrain.GroundHeight, before.Z);
        else if (!WaterMap.IsWet(before.X, Position.Z))
            Position = new Vector3(before.X, Terrain.GroundHeight, Position.Z);
        else
            Position = before;
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

        for (int i = 0; i < obstacles.Count; i++)
        {
            Obstacle obstacle = obstacles[i];
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
        for (int i = 0; i < obstacles.Count; i++)
        {
            Obstacle obstacle = obstacles[i];
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
    private void CheckIfStuck(Vector3 target, float step, float deltaTime, World world, Func<World, Vector3, bool> isSafeSpot)
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
    private Vector3? PickDetour(Vector3 target, World world, Func<World, Vector3, bool> isSafeSpot)
    {
        var forward = new Vector2(target.X - Position.X, target.Z - Position.Z);
        forward = forward.LengthSquared() > 1e-6f ? Vector2.Normalize(forward) : Vector2.UnitX;
        var left = new Vector2(-forward.Y, forward.X);
        float firstSide = _rng.Next(2) == 0 ? 1f : -1f;

        foreach (float side in new[] { firstSide, -firstSide })
        {
            Vector2 offset = left * side * 1.5f - forward * 0.5f;
            var candidate = new Vector3(Position.X + offset.X, Terrain.GroundHeight, Position.Z + offset.Y);
            if (world.Terrain.Contains(candidate, _edgeMargin) && isSafeSpot(world, candidate) &&
                !(_walks && WaterMap.IsWet(candidate.X, candidate.Z)))
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
