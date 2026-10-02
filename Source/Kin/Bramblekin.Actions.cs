using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
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
        _mealCooked = false;
    }

    /// <summary>The meal under way was taken from a store with a lit hearth: it's cooked (see <see cref="Craft.Hearth"/>).</summary>
    private bool _mealCooked;

    private void FinishEating(World world)
    {
        if (_carried is { } food)
        {
            world.ConsumeFood(food);
            _carried = null;
            Hunger = MathF.Max(0f, Hunger - FoodNourishment - (_mealCooked ? World.CookedNourishmentBonus : 0f));
            QuenchWith(food.Kind);
            Heal(FoodHealing + (_mealCooked ? World.CookedHealingBonus : 0));
            if (_mealCooked)
                world.NoteCookedMeal();
        }
        _mealCooked = false;
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
        if (NerveBroken(victim))
            return false;
        if (GroupId is not null && victim.GroupId == GroupId)
            return false;

        float leash = DetectionRadius * ThreatLeashMultiplier;
        return GroundMover.HorizontalDistanceSquared(Position, victim.Position) <= leash * leash;
    }

    /// <summary>
    /// Closes to strike range of <paramref name="target"/> and strikes on
    /// cooldown. When robbing, the first blow that lands takes the victim's
    /// food (see <see cref="World.StealFood"/>). Blood is thicker than
    /// water: it never deals a close relative (parent, child, sibling) the
    /// blow that would kill it. A slinger stops short of a Hornet and looses pebbles instead (see <see cref="TrySling"/>).
    /// </summary>
    private void PursueAndStrike(ICombatant target, float speed, float deltaTime, World world)
    {
        if (TrySling(target, world))
            return;

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
        if (target is Bramblekin relative && relative.Health <= StrikeDamage && relative.IsCloseKinOf(this))
            return;

        if (State == BramblekinState.Attacking && target is Bramblekin victim && victim.HasFood)
            world.StealFood(this, victim);
        if (State == BramblekinState.Fighting && _threatIsAllyDefense)
            world.NoteDefended(this, target);
        if (target is not Bramblekin)
            Train(Skill.Hunting, world, target is StagBeetle or WolfSpider ? 2f : 1f);
        target.TakeHit(target is Bramblekin ? StrikeDamage : HuntingDamage, this, world);
    }

    /// <summary>
    /// Runs directly away from <paramref name="threatPosition"/>, turning
    /// along the map's edge rather than into it — or home, if it's close
    /// and <paramref name="homeIsSafe"/> (it keeps out wildlife, but not
    /// another Bramblekin).
    /// </summary>
    private void FleeFrom(Vector3 threatPosition, bool homeIsSafe, float deltaTime, World world)
    {
        var away = new Vector2(Position.X - threatPosition.X, Position.Z - threatPosition.Z);
        away = away.LengthSquared() > 1e-4f ? Vector2.Normalize(away) : _mover.Heading;
        var left = new Vector2(-away.Y, away.X);

        // Home is the safest place there is: run there instead, unless that
        // means running past the threat.
        if (homeIsSafe && Home is { IsBuilt: true } home)
        {
            var toHome = new Vector2(home.Position.X - Position.X, home.Position.Z - Position.Z);
            float homeDistance = toHome.Length();
            if (homeDistance <= FleeToHomeRange &&
                (homeDistance < 3f || Vector2.Dot(toHome / homeDistance, away) > -0.3f))
            {
                MoveTo(home.Position, WalkSpeed * FleeSpeedMultiplier, deltaTime, world);
                return;
            }
        }

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

    /// <summary>True while it moved this step (used to wear paths into the ground — see World.Trails).</summary>
    public bool IsWalking => _mover.IsMoving;

    /// <summary>Walks toward <paramref name="target"/>, steering round Pebbles. Returns true on arrival.</summary>
    private bool MoveTo(Vector3 target, float speed, float deltaTime, World world) =>
        _mover.MoveTowards(target, speed * AgeSpeedFactor * VigorSpeedFactor * (IsSick ? SickSpeedFactor : 1f) * WorkPace * world.PathSpeed(Position), deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));

    private void StartPause()
    {
        SetState(BramblekinState.Idle);
        _pauseTimer = PauseDuration * (0.5f + (float)_rng.NextDouble()) * (1.4f - 0.8f * Personality.Diligence); // The idle dawdle.
    }

    private void SetState(BramblekinState state)
    {
        if (State == state)
            return;

        if (State == BramblekinState.Foraging)
            ReleaseFoodClaim();
        if (State == BramblekinState.Collecting)
            ReleaseTwigClaim();
        if (State == BramblekinState.Socializing)
            _companion = null;
        if (state is BramblekinState.Resting or BramblekinState.Sleeping)
            _restTimer = 0f;

        State = state;
        if (state is not (BramblekinState.Fighting or BramblekinState.Attacking or BramblekinState.Hunting or BramblekinState.Dueling))
            CombatTarget = null;
        _mover.ResetProgress();
    }

    /// <summary>A random reachable point within <paramref name="radius"/> of where it stands (anywhere on the map as a fallback).</summary>
    private Vector3 RandomWanderPoint(World world, float radius) => RandomWanderPointAround(Position, radius, world);

    /// <summary>A random wander point that isn't somewhere it remembers danger (if one turns up in a few tries).</summary>
    private Vector3 SafeWanderPoint(World world, float radius)
    {
        Vector3 point = RandomWanderPoint(world, radius);
        for (int attempt = 0; attempt < 3 && IsDangerous(point, world); attempt++)
            point = RandomWanderPoint(world, radius);
        return point;
    }

    /// <summary>A random reachable point within <paramref name="radius"/> of <paramref name="center"/> (anywhere on the map as a fallback).</summary>
    private Vector3 RandomWanderPointAround(Vector3 center, float radius, World world)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float angle = (float)(_rng.NextDouble() * MathF.Tau);
            float distance = MathF.Sqrt((float)_rng.NextDouble()) * radius;
            Vector3 candidate = center + new Vector3(MathF.Cos(angle) * distance, 0f, MathF.Sin(angle) * distance);
            if (world.Terrain.Contains(candidate, EdgeMargin + 1f) && !world.IsBlockedOrAntZone(candidate, BodyRadius))
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
            if (world.Terrain.Contains(candidate, EdgeMargin + 1f) && !world.IsBlockedOrAntZone(candidate, BodyRadius))
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
        return world.Terrain.Contains(candidate, EdgeMargin + 1f) && !world.IsBlockedOrAntZone(candidate, BodyRadius) ? candidate : center;
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
}
