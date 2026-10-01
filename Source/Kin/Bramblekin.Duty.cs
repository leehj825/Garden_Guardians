using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>A Guard stays within this many meters of home.</summary>
    private const float GuardPostRadius = 3f;

    /// <summary>A Gatherer brings in Food lying within this many meters of home.</summary>
    private const float GatherRange = 25f;

    /// <summary>Hunters and Guards stand down to rest once Health is at or below this fraction.</summary>
    private const float DutyStandDownHealthFraction = 0.5f;

    /// <summary>Its job from its group's Leader — see <see cref="World.DecideGroupGoal"/>.</summary>
    public KinJob Job { get; private set; }

    public void AssignJob(KinJob job) => Job = job;

    /// <summary>
    /// Duty need (fed and safe, in a group): does the job its Leader gave
    /// it — provided it's loyal enough to take orders at all (see
    /// <see cref="IsObedient"/>). Returns false when the job has nothing
    /// for it to do, leaving it to Settle and Social.
    /// </summary>
    private bool UpdateDuty(float deltaTime, World world)
    {
        // An errand for its group comes before any job (see Bramblekin.Errands).
        if (_errand is { } errand)
            return DoErrand(errand, deltaTime, world);

        if (Job != KinJob.Builder && (_carriedMaterial is not null || _materialTarget is not null))
            PutDownMaterial(); // Moved off building: leaves the stone (or branch) where it is.
        if (Job != KinJob.Builder)
            _hearthToFeed = null;
        if (GroupId is null || IsYoung || !IsObedient || world.GroupOf(this) is not { } group)
            return false;

        return Job switch
        {
            KinJob.Guard => DoGuardDuty(group, deltaTime, world),
            KinJob.Hunter => DoHuntDuty(group, deltaTime, world),
            KinJob.Builder => DoBuilderDuty(group, deltaTime, world),
            KinJob.Gatherer => DoGatherDuty(deltaTime, world),
            KinJob.Farmer => DoFarmDuty(group, deltaTime, world),
            KinJob.Raider => DoRaidDuty(group, deltaTime, world),
            KinJob.Healer => DoHealerDuty(group, deltaTime, world),
            KinJob.Scout => DoScoutDuty(group, deltaTime, world),
            KinJob.Fisher => DoFisherDuty(deltaTime, world),
            _ => false,
        };
    }

    /// <summary>A soldier in a village loiters this long (s) at each waypoint of its patrol, looking about.</summary>
    private const float PatrolLookSeconds = 3f;

    /// <summary>…and picks up food lying within this far (m) of where it is, to carry to the stores (never leaving the route for more).</summary>
    private const float PatrolPickupRange = 4f;

    private int _patrolIndex = -1;
    private float _patrolLook;

    /// <summary>
    /// Guard: attacks the threat the Leader rallied the group against (in a village, any of its clans' alarms near any of its homes);
    /// in a village it is a soldier: it patrols the village's edge, picks up food lying by the route and takes it to the stores; elsewhere
    /// it keeps close to home.
    /// </summary>
    private bool DoGuardDuty(KinGroup group, float deltaTime, World world)
    {
        if (Health <= MaxHealth * DutyStandDownHealthFraction || Home is not { IsBuilt: true } home)
            return false;

        Village? village = World.SocietyJobsEnabled ? world.VillageOf(group) : null;
        if (world.AlarmFor(group, village) is { } threat)
        {
            SetState(BramblekinState.Fighting);
            CombatTarget = threat;
            PursueAndStrike(threat, WalkSpeed * PursuitSpeedMultiplier, deltaTime, world);
            return true;
        }

        if (village is not null)
            return DoPatrol(village, group, deltaTime, world);

        if (GroundMover.HorizontalDistanceSquared(Position, home.Position) > GuardPostRadius * GuardPostRadius)
        {
            SetState(BramblekinState.Guarding);
            MoveTo(home.Position, WalkSpeed, deltaTime, world);
            return true;
        }

        // On post: let Settle/Social keep it busy close by (resting, stocking the store).
        return false;
    }

    /// <summary>A soldier's round: food it is carrying goes to the stores first; food within reach of the route is picked up; otherwise on to the next waypoint round the village's edge.</summary>
    private bool DoPatrol(Village village, KinGroup group, float deltaTime, World world)
    {
        if (_carried is not null && StoreToStock(world) is { } store)
        {
            CarryFoodHome(store, deltaTime, world);
            return true;
        }

        if (ValidPerceivedFood(world) is { } food && GroundMover.HorizontalDistanceSquared(food.Position, Position) <= PatrolPickupRange * PatrolPickupRange &&
            GroundMover.HorizontalDistanceSquared(food.Position, village.Centre) <= (village.PatrolRadius + PatrolPickupRange) * (village.PatrolRadius + PatrolPickupRange))
        {
            ApproachFood(food, WalkSpeed, deltaTime, world, eatOnArrival: false);
            return true;
        }

        if (_patrolIndex < 0)
            _patrolIndex = ID % World.PatrolPoints; // Soldiers start spread round the ring.
        Vector3 waypoint = world.PatrolWaypoint(village, _patrolIndex);
        SetState(BramblekinState.Guarding);
        if (GroundMover.HorizontalDistanceSquared(Position, waypoint) > 1.2f * 1.2f)
        {
            _patrolLook = 0f;
            MoveTo(waypoint, WalkSpeed * 0.8f, deltaTime, world);
            return true;
        }
        _patrolLook += deltaTime;
        if (_patrolLook >= PatrolLookSeconds)
        {
            _patrolLook = 0f;
            _patrolIndex = (_patrolIndex + 1) % World.PatrolPoints;
        }
        return true;
    }

    /// <summary>Fisher: carries a catch to the stores, else fishes the shore near home (a Gatherer with no shore near gathers instead).</summary>
    private bool DoFisherDuty(float deltaTime, World world)
    {
        if (Home is not { IsBuilt: true } home || StoreToStock(world) is not { } store)
            return false;
        if (_carried is not null)
        {
            CarryFoodHome(store, deltaTime, world);
            return true;
        }
        return TryFishing(home, deltaTime, world) || DoGatherDuty(deltaTime, world);
    }

    /// <summary>Hunter: goes after the Stag Beetle the Leader picked, or else small game it can see (a Grub, or a frog on the bank).</summary>
    private bool DoHuntDuty(KinGroup group, float deltaTime, World world)
    {
        if (Health <= MaxHealth * DutyStandDownHealthFraction)
            return false;

        if (group.HuntTarget is { IsDead: false } beetle)
        {
            SetState(BramblekinState.Hunting);
            CombatTarget = beetle;
            PursueAndStrike(beetle, WalkSpeed * 1.2f, deltaTime, world);
            return true;
        }

        if (LivePrey is { } prey)
        {
            HuntPrey(prey, deltaTime, world);
            return true;
        }

        return DoGatherDuty(deltaTime, world);
    }

    /// <summary>
    /// Raider (at war): goes with the raiding party to the enemy store its
    /// Leader picked, takes a piece of Food and carries it home to its own
    /// stores. The enemy's residents see it coming and defend. Stands down
    /// to rest at half Health.
    /// </summary>
    private bool DoRaidDuty(KinGroup group, float deltaTime, World world)
    {
        if (Health <= MaxHealth * DutyStandDownHealthFraction)
            return false;

        if (_carried is not null)
        {
            if (StoreToStock(world) is not { } store)
                return false;
            CarryFoodHome(store, deltaTime, world);
            return true;
        }

        if (group.WarTarget is not { IsCollapsed: false, StoredFood: > 0 } target)
            return false;

        _raidTarget = target;
        SetState(BramblekinState.Raiding);
        if (!target.Contains(Position))
        {
            MoveTo(target.Position, WalkSpeed * 1.2f, deltaTime, world);
            return true;
        }

        if (!BreakIn(target, deltaTime))
            return true;
        _raidTarget = null;
        if (world.RaidStore(this, target) is { } food)
        {
            _carried = food;
            world.NoteWarRaid(this, target);
        }
        return true;
    }

    /// <summary>A raider needs this long at a palisaded store to get past the stakes (see <see cref="Craft.Palisade"/>).</summary>
    private const float PalisadeBreakInSeconds = 4f;

    private float _breakInTimer;

    /// <summary>At a store to raid: true once in — straight away, or after <see cref="PalisadeBreakInSeconds"/> at a palisaded one (time for its defenders to come).</summary>
    private bool BreakIn(Shelter store, float deltaTime)
    {
        if (!store.HasPalisade)
            return true;
        _breakInTimer += deltaTime;
        if (_breakInTimer < PalisadeBreakInSeconds)
            return false;
        _breakInTimer = 0f;
        return true;
    }

    /// <summary>True while it's a Raider out with its group's raiding party (see <see cref="DoRaidDuty"/>).</summary>
    private bool IsOnWarRaid(World world) =>
        Job == KinJob.Raider && IsObedient && world.GroupOf(this) is { Goal: GroupGoal.Raid, WarTarget: not null };

    /// <summary>Builder: fetches twigs for whichever group home is under construction; else a twig for a hearth burning low (see <see cref="Craft.Hearth"/>); else stones and branches for footings and palisades (see <see cref="TryFetchMaterial"/>); with nothing to build, gathers.</summary>
    private bool DoBuilderDuty(KinGroup group, float deltaTime, World world)
    {
        if (BuildSite is { } site)
        {
            _hearthToFeed = null;
            DoBuildWork(site, deltaTime, world);
            return true;
        }
        if (_carriedMaterial is null && (_hearthToFeed is { NeedsFuel: true, IsCollapsed: false } ? _hearthToFeed : world.HearthToFeed(group, Position)) is { } hearth)
        {
            _hearthToFeed = hearth;
            DoBuildWork(hearth, deltaTime, world);
            return true;
        }
        _hearthToFeed = null;
        return TryFetchMaterial(group, deltaTime, world) || DoGatherDuty(deltaTime, world);
    }

    /// <summary>Gatherer: brings what's ripe off the group's crops, and Food lying within <see cref="GatherRange"/> of home, into the shared stores until they're full — and, with neither, fishes if it knows how (see <see cref="TryFishing"/>).</summary>
    private bool DoGatherDuty(float deltaTime, World world)
    {
        if (Home is not { IsBuilt: true } home || StoreToStock(world) is not { } store)
            return false;

        if (_carried is not null)
        {
            CarryFoodHome(store, deltaTime, world);
            return true;
        }

        if (world.GroupOf(this) is { } group && world.NearestRipeCrop(this, group, GatherRange) is { } bush)
        {
            Harvest(bush, deltaTime, world, eat: false);
            return true;
        }

        if (ValidPerceivedFood(world) is { } food &&
            GroundMover.HorizontalDistanceSquared(food.Position, home.Position) <= GatherRange * GatherRange)
        {
            ApproachFood(food, WalkSpeed, deltaTime, world, eatOnArrival: false);
            return true;
        }

        if (world.Snares.Count > 0 && world.GroupOf(this) is { } clan && world.SprungSnareNear(clan, home.Position, GatherRange) is { } snare)
        {
            ResetSnare(snare, deltaTime, world);
            return true;
        }
        return TryFishing(home, deltaTime, world);
    }

    private float _snareTimer;

    /// <summary>Walks to a sprung snare and sets it again (see <see cref="Snare.ResetSeconds"/>).</summary>
    private void ResetSnare(Snare snare, float deltaTime, World world)
    {
        SetState(BramblekinState.Farming);
        if (GroundMover.HorizontalDistance(Position, snare.Position) > 0.5f)
        {
            _snareTimer = 0f;
            MoveTo(snare.Position, WalkSpeed, deltaTime, world);
            return;
        }
        _snareTimer += deltaTime * WorkPace;
        if (_snareTimer < Snare.ResetSeconds)
            return;
        _snareTimer = 0f;
        world.ResetSnare(snare);
    }
}
