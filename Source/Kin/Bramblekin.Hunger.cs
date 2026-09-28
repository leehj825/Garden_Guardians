using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>
    /// Critical need: eat what it's holding; else rob the neighbour it
    /// committed to (see <see cref="BeginRobbery"/>); else forage the nearest
    /// visible Food or eat from its home's store, whichever is closer (see
    /// <see cref="PrefersLooseFood"/>); else scavenge an
    /// abandoned store; else hunt small game (a Grub, a frog on the bank), or
    /// a Stag Beetle with its pack; else (starving) eat the clan's seed corn;
    /// else (starving and Aggressive) raid someone's store; else — a
    /// follower borrows its
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

        // With a predator about, a stocked home is the safe place to eat.
        Shelter? store = StoreToEatFrom(world);
        if (IsThreatenedByPredator && store is not null)
        {
            GoHomeAndEat(store, deltaTime, world);
            return;
        }

        if (ValidPerceivedFood(world) is { } food && (store is null || PrefersLooseFood(food, store)))
        {
            ApproachFood(food, WalkSpeed * (IsStarving ? 1.25f : 1f), deltaTime, world, eatOnArrival: true);
            return;
        }

        // Settling pays off: it goes home and eats from its own (or its
        // group's) store.
        if (store is not null)
        {
            GoHomeAndEat(store, deltaTime, world);
            return;
        }

        // Farming pays off: a ripe berry on one of its group's bushes.
        if (world.GroupOf(this) is { } group && world.NearestRipeCrop(this, group, DetectionRadius) is { } bush)
        {
            Harvest(bush, deltaTime, world, eat: true);
            return;
        }

        // An abandoned store it can see is free for the taking.
        if (TryTakeFromStore(deltaTime, world, raid: false))
            return;

        if (!IsYoung && LivePrey is { } prey)
        {
            HuntPrey(prey, deltaTime, world);
            return;
        }

        if (TryPackHunt(deltaTime, world, hungry: true))
            return;

        // Starving, and nothing else to hand: the clan's seed corn.
        if (world.SeedCornLoft(this) is { } loft)
        {
            GoHomeAndEat(loft, deltaTime, world);
            return;
        }

        // Starving and Aggressive: raid someone else's store.
        if (TryTakeFromStore(deltaTime, world, raid: true))
            return;

        // Desperation: a starving, highly Aggressive Bramblekin with nothing
        // else in sight stalks the nearest outsider it can see carrying
        // food, to cross paths with it — whether it then attacks is decided
        // by the encounter (see World.ResolveEncounter).
        if (!IsYoung && IsStarving && Personality.Aggression >= World.HighAggressionThreshold && NearestFoodCarrier(world) is { } mark)
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

    /// <summary>A loose piece of Food this close is always worth grabbing before the walk home.</summary>
    private const float GrabRange = 3f;

    /// <summary>
    /// Loose Food or the store? Whichever is closer — loose Food saves the
    /// store for leaner days — but once starving, the sure meal at home
    /// beats chasing anything that isn't right at hand: others may get to
    /// loose Food first.
    /// </summary>
    private bool PrefersLooseFood(FoodShard food, Shelter store)
    {
        float toFood = GroundMover.HorizontalDistanceSquared(Position, food.Position);
        if (toFood <= GrabRange * GrabRange)
            return true;
        return !IsStarving && toFood < GroundMover.HorizontalDistanceSquared(Position, store.Position);
    }

    /// <summary>The Food this Bramblekin can currently see, if <paramref name="groupmate"/> could take it — how a Leader points food out to a hungry follower.</summary>
    public FoodShard? FoodSightingFor(Bramblekin groupmate, World world) =>
        _perceivedFood is { } food && world.IsAvailable(food, groupmate) ? food : null;

    /// <summary>
    /// Hungry with nothing in sight: heads back to where it last saw Food
    /// (Berries keep growing in the same patches) or where its group has
    /// lately found some, then keeps striking out toward random points
    /// twice as far as a normal wander — steering clear of remembered danger.
    /// </summary>
    private void Explore(float deltaTime, World world)
    {
        if (State != BramblekinState.Searching)
        {
            _wanderTarget = RememberedFoodSpot(world) ?? SafeWanderPoint(world, WanderRadius * 2f);
            SetState(BramblekinState.Searching);
        }

        if (MoveTo(_wanderTarget, WalkSpeed, deltaTime, world))
        {
            // Been there, nothing left: forget it (and tell the group).
            _foodMemory = null;
            world.GroupOf(this)?.FoodSpots.Forget(_wanderTarget);
            _wanderTarget = RememberedFoodSpot(world) ?? SafeWanderPoint(world, WanderRadius * 2f);
        }
    }
}
