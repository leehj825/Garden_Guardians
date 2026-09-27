using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>
    /// Critical need: eat what it's holding; else rob the neighbour it
    /// committed to (see <see cref="BeginRobbery"/>); else forage the nearest
    /// visible Food; else eat from its home's store; else hunt a visible
    /// Grub; else — a follower borrows its
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

        // Settling pays off: with nothing loose in sight, it goes home and
        // eats from its own (or its group's) store.
        if (Home is { } home && CanEatFromStore(world))
        {
            GoHomeAndEat(home, deltaTime, world);
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
}
