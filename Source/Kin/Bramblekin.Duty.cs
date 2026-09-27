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
        if (GroupId is null || IsYoung || !IsObedient || world.GroupOf(this) is not { } group)
            return false;

        return Job switch
        {
            KinJob.Guard => DoGuardDuty(group, deltaTime, world),
            KinJob.Hunter => DoHuntDuty(group, deltaTime, world),
            KinJob.Builder => DoBuilderDuty(deltaTime, world),
            KinJob.Gatherer => DoGatherDuty(deltaTime, world),
            _ => false,
        };
    }

    /// <summary>Guard: attacks the threat the Leader rallied the group against; otherwise keeps close to home.</summary>
    private bool DoGuardDuty(KinGroup group, float deltaTime, World world)
    {
        if (Health <= MaxHealth * DutyStandDownHealthFraction || Home is not { IsBuilt: true } home)
            return false;

        if (group.DefendTarget is { IsDead: false } threat &&
            GroundMover.HorizontalDistanceSquared(threat.Position, home.Position) <= World.HomeDefenseRadius * World.HomeDefenseRadius * 1.5f)
        {
            SetState(BramblekinState.Fighting);
            CombatTarget = threat;
            PursueAndStrike(threat, WalkSpeed * PursuitSpeedMultiplier, deltaTime, world);
            return true;
        }

        if (GroundMover.HorizontalDistanceSquared(Position, home.Position) > GuardPostRadius * GuardPostRadius)
        {
            SetState(BramblekinState.Guarding);
            MoveTo(home.Position, WalkSpeed, deltaTime, world);
            return true;
        }

        // On post: let Settle/Social keep it busy close by (resting, stocking the store).
        return false;
    }

    /// <summary>Hunter: goes after the Stag Beetle the Leader picked, or else a Grub it can see.</summary>
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

        if (_perceivedGrub is { IsDead: false } grub)
        {
            HuntGrub(grub, deltaTime, world);
            return true;
        }

        return DoGatherDuty(deltaTime, world);
    }

    /// <summary>Builder: fetches twigs for the home's current construction stage; with nothing to build, gathers.</summary>
    private bool DoBuilderDuty(float deltaTime, World world)
    {
        if (Home is { NeedsTwigs: true } home)
        {
            DoBuildWork(home, deltaTime, world);
            return true;
        }
        return DoGatherDuty(deltaTime, world);
    }

    /// <summary>Gatherer: brings Food lying within <see cref="GatherRange"/> of home into the shared store until it's full.</summary>
    private bool DoGatherDuty(float deltaTime, World world)
    {
        if (Home is not { IsBuilt: true } home || home.StoreIsFull)
            return false;

        if (_carried is not null)
        {
            CarryFoodHome(home, deltaTime, world);
            return true;
        }

        if (ValidPerceivedFood(world) is { } food &&
            GroundMover.HorizontalDistanceSquared(food.Position, home.Position) <= GatherRange * GatherRange)
        {
            ApproachFood(food, WalkSpeed, deltaTime, world, eatOnArrival: false);
            return true;
        }
        return false;
    }
}
