using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>A newcomer looks around this long before settling at Intelligence 1…</summary>
    private const float MinSettleDelay = 10f;

    /// <summary>…and this long at Intelligence 0.</summary>
    private const float MaxSettleDelay = 50f;

    /// <summary>At or below this fraction of <see cref="MaxHealth"/>, a Bramblekin with a home goes and rests in it…</summary>
    private const float RestHealthFraction = 0.6f;

    /// <summary>…until it's back up to this fraction.</summary>
    private const float RestedHealthFraction = 0.9f;

    /// <summary>Resting at home heals 1 HP every this many seconds.</summary>
    private const float RestHealInterval = 1.5f;

    /// <summary>It only stockpiles Food lying within this many meters of home.</summary>
    private const float StockpileRange = 20f;

    /// <summary>A Bramblekin with a home wanders within this many meters of it.</summary>
    public const float HomeRange = 12f;

    /// <summary>Fleeing, it runs for home instead if home is closer than this (m).</summary>
    private const float FleeToHomeRange = 15f;

    /// <summary>After failing to find anywhere to build, it waits this long before looking again.</summary>
    private const float SettleRetryDelay = 5f;

    private float _settleRetryTimer;

    /// <summary>How many twig-search legs in a row came up empty — each one ranges further from the site.</summary>
    private int _twigSearchLegs;

    /// <summary>Radius (m) of the first twig-search leg around a site; each empty leg adds this much again, up to <see cref="MaxTwigSearchRadius"/>.</summary>
    private const float TwigSearchStep = 15f;

    private const float MaxTwigSearchRadius = 60f;

    private float TwigSearchRadius => MathF.Min(MaxTwigSearchRadius, TwigSearchStep * (1 + _twigSearchLegs));

    private float SettleDelay => MaxSettleDelay - (MaxSettleDelay - MinSettleDelay) * Personality.Intelligence;

    /// <summary>True while it's the one who should be fetching twigs for its home's current construction stage.</summary>
    private bool NeedsTwig => _carriedTwig is null && Home is { NeedsTwigs: true } && BuildsForHome;

    /// <summary>Whether it fetches twigs for its home: a solitary Bramblekin builds its own; in a group, that's the Builders' job (see Bramblekin.Duty) — or everyone's, while the group has no Leader's orders yet.</summary>
    private bool BuildsForHome => GroupId is null || Job is KinJob.Builder or KinJob.None;

    /// <summary>
    /// Settle need (fed and safe), for a solitary Bramblekin — a group's
    /// home is the group's business. Once it has looked around for a while
    /// (sharper minds settle sooner) it moves into an abandoned shelter it
    /// can see, or marks out a site near where it last found Food and builds
    /// a Tent from twigs. Then it rests there when hurt and stocks the
    /// store with Food from nearby. Returns false when there's nothing to
    /// do, leaving the rest to the Social need.
    /// </summary>
    private bool UpdateSettle(float deltaTime, World world)
    {
        if (GroupId is not null)
            return UpdateGroupSettle(deltaTime, world);

        if (Home is null && !TryFindHome(deltaTime, world))
            return false;

        Shelter home = Home!;
        if (home.NeedsTwigs)
        {
            DoBuildWork(home, deltaTime, world);
            return true;
        }

        return TendHome(home, deltaTime, world);
    }

    /// <summary>
    /// A group member's part in its group's home (see
    /// <see cref="World.UpdateGroupHomes"/>): help build or upgrade it,
    /// then rest in it, stock the shared store and hunt near it just like a
    /// homesteader does its own. A group still without a home just keeps
    /// moving with its Leader.
    /// </summary>
    private bool UpdateGroupSettle(float deltaTime, World world)
    {
        if (Home is not { } home)
            return false;

        if (home.NeedsTwigs && BuildsForHome)
        {
            DoBuildWork(home, deltaTime, world);
            return true;
        }

        return TendHome(home, deltaTime, world);
    }

    /// <summary>
    /// Life around a finished home: rest there when hurt (until nearly
    /// healed), carry held Food into the store, fetch visible Food lying
    /// near home to stock it, and hunt Grubs near home while the store is
    /// low. Returns false when there's nothing to do.
    /// </summary>
    private bool TendHome(Shelter home, float deltaTime, World world)
    {
        if (!home.IsBuilt)
            return false;

        bool resting = State is BramblekinState.Resting or BramblekinState.HeadingHome;
        if (Health <= MaxHealth * RestHealthFraction || (resting && Health < MaxHealth * RestedHealthFraction))
        {
            RestAtHome(home, deltaTime, world);
            return true;
        }

        if (_carried is not null && !home.StoreIsFull)
        {
            CarryFoodHome(home, deltaTime, world);
            return true;
        }

        if (_carried is null && !home.StoreIsFull && ValidPerceivedFood(world) is { } food &&
            GroundMover.HorizontalDistanceSquared(food.Position, home.Position) <= StockpileRange * StockpileRange)
        {
            ApproachFood(food, WalkSpeed, deltaTime, world, eatOnArrival: false);
            return true;
        }

        // A low store is worth a hunt: a Grub near home becomes meat to stock.
        if (home.StoredFood < home.StoreCapacity / 2 && _perceivedGrub is { IsDead: false } grub &&
            GroundMover.HorizontalDistanceSquared(grub.Position, home.Position) <= StockpileRange * StockpileRange)
        {
            HuntGrub(grub, deltaTime, world);
            return true;
        }

        return false;
    }

    /// <summary>Moves into a visible abandoned shelter, or marks out a new Tent site. Returns false (and waits a while before trying again) if neither works out.</summary>
    private bool TryFindHome(float deltaTime, World world)
    {
        if (_age < SettleDelay)
            return false;

        _settleRetryTimer -= deltaTime;
        if (_settleRetryTimer > 0f)
            return false;

        if (world.NearestAbandonedShelter(Position, DetectionRadius) is { } vacant)
        {
            world.ClaimShelter(vacant, this);
            Home = vacant;
            Game.AddEventLog($"[SETTLE] #{ID} moved into an abandoned {vacant.Tier}");
            return true;
        }

        Home = world.TryCreateShelterSite(_foodMemory ?? Position, owner: this, groupId: null);
        if (Home is null)
            _settleRetryTimer = SettleRetryDelay;
        return Home is not null;
    }

    /// <summary>Carries a twig to <paramref name="site"/>; else picks up the nearest visible one; else goes looking where twigs were last seen, or in ever-wider legs around the site.</summary>
    private void DoBuildWork(Shelter site, float deltaTime, World world)
    {
        if (_carriedTwig is { } twig)
        {
            SetState(BramblekinState.Building);
            float reach = (site.IsUpgrading ? Shelter.HouseRadius : site.Radius) + 0.3f;
            if (GroundMover.HorizontalDistanceSquared(Position, site.Position) <= reach * reach)
            {
                _carriedTwig = null;
                world.DeliverTwig(this, site, twig);
            }
            else
            {
                MoveTo(site.Position, WalkSpeed, deltaTime, world);
            }
            return;
        }

        if (ValidPerceivedTwig() is { } seen)
        {
            ClaimTwig(seen);
            SetState(BramblekinState.Collecting);
            if (GroundMover.HorizontalDistance(Position, seen.Position) <= PickupDistance)
            {
                ReleaseTwigClaim();
                World.PickUpTwig(seen);
                _carriedTwig = seen;
                _perceivedTwig = null;
                _twigSearchLegs = 0;
                return;
            }
            MoveTo(seen.Position, WalkSpeed, deltaTime, world);
            return;
        }

        // Nothing in sight: back to where twigs were last seen, else search
        // around the site — each empty leg ranging further out.
        if (State != BramblekinState.Collecting)
        {
            _wanderTarget = _twigMemory ?? RandomWanderPointAround(site.Position, TwigSearchRadius, world);
            SetState(BramblekinState.Collecting);
        }
        if (MoveTo(_wanderTarget, WalkSpeed, deltaTime, world))
        {
            _twigMemory = null; // Nothing left there.
            _twigSearchLegs++;
            _wanderTarget = RandomWanderPointAround(site.Position, TwigSearchRadius, world);
        }
    }

    /// <summary>Carries held Food into <paramref name="home"/>'s store.</summary>
    private void CarryFoodHome(Shelter home, float deltaTime, World world)
    {
        SetState(BramblekinState.Stockpiling);
        if (!home.Contains(Position))
        {
            MoveTo(home.Position, WalkSpeed, deltaTime, world);
            return;
        }

        if (_carried is { } food && World.DepositFood(home, food))
            _carried = null;
        StartPause();
    }

    /// <summary>Goes home and rests inside, healing 1 HP every <see cref="RestHealInterval"/> seconds (half again as fast in a House).</summary>
    private void RestAtHome(Shelter home, float deltaTime, World world)
    {
        if (!home.Contains(Position))
        {
            SetState(BramblekinState.HeadingHome);
            MoveTo(home.Position, WalkSpeed, deltaTime, world);
            return;
        }

        SetState(BramblekinState.Resting);
        _restTimer += deltaTime;
        float healInterval = home.Tier == ShelterTier.House ? RestHealInterval / 1.5f : RestHealInterval;
        if (_restTimer >= healInterval)
        {
            _restTimer -= healInterval;
            Health = Math.Min(MaxHealth, Health + 1);
        }
    }

    /// <summary>True if its home's store has Food it may eat right now.</summary>
    private bool CanEatFromStore(World world) =>
        Home is { IsBuilt: true, StoredFood: > 0 } home && world.MayEatFromStore(this, home);

    /// <summary>Hunger: walks home and takes a piece of Food from the store to eat there.</summary>
    private void GoHomeAndEat(Shelter home, float deltaTime, World world)
    {
        if (home.Contains(Position))
        {
            if (world.WithdrawFood(home) is { } food)
            {
                _carried = food;
                world.NoteAteFromStore(this, home);
                StartEating();
            }
            return;
        }

        SetState(BramblekinState.HeadingHome);
        MoveTo(home.Position, WalkSpeed * (IsStarving ? 1.25f : 1f), deltaTime, world);
    }

    /// <summary>The twig perception last spotted, if it's still there for the taking (else forces a fresh scan next frame).</summary>
    private Twig? ValidPerceivedTwig()
    {
        if (_perceivedTwig is { } twig && World.IsAvailable(twig, this))
            return twig;

        if (_perceivedTwig is not null)
        {
            _perceivedTwig = null;
            _perceptionTimer = 0f;
        }
        return null;
    }

    private void ClaimTwig(Twig twig)
    {
        if (_claimedTwig == twig)
            return;

        ReleaseTwigClaim();
        twig.ClaimedBy = this;
        twig.ClaimTimer = 0f;
        _claimedTwig = twig;
    }

    private void ReleaseTwigClaim()
    {
        if (_claimedTwig is not null && _claimedTwig.ClaimedBy == this)
            _claimedTwig.ClaimedBy = null;
        _claimedTwig = null;
    }

    /// <summary>Settling: sets (or clears) where it lives. Used when a group adopts, builds or loses a home.</summary>
    public void SetHome(Shelter? home) => Home = home;
}
