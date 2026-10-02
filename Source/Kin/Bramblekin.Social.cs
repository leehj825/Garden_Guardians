using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>
    /// Social need (fed and safe): pocket a spare piece of Food if one is
    /// close and its hands are empty; a follower stays near its Leader;
    /// in winter, anyone with a home huddles inside it (see
    /// <see cref="WinterIn"/>); anyone else alternates short rests with a
    /// move chosen by <see cref="ChooseSocialAction"/>.
    /// </summary>
    private void UpdateSocial(float deltaTime, World world)
    {
        // Without a finished home to stock, it just pockets one spare bite.
        if (_carried is null && Home is not { IsBuilt: true } && ValidPerceivedFood(world) is { } food &&
            GroundMover.HorizontalDistance(Position, food.Position) <= DetectionRadius * ReserveGrabRadiusFraction)
        {
            ApproachFood(food, WalkSpeed, deltaTime, world, eatOnArrival: false);
            return;
        }

        // Big game: a pack that spots a Stag Beetle goes after it together.
        if (TryPackHunt(deltaTime, world, hungry: false))
            return;

        // A group without a finished home keeps moving with its Leader; a
        // settled group lives around its home instead.
        Bramblekin? leader = world.GroupOf(this)?.Leader;
        if (leader is { IsDead: false } && leader != this && Home is not { IsBuilt: true })
        {
            FollowLeader(leader, deltaTime, world);
            return;
        }

        if ((world.CurrentSeason == Season.Winter || world.IsStorming) && Home is { IsBuilt: true } home)
        {
            WinterIn(home, deltaTime, world); // Sitting out the winter — or a storm.
            return;
        }

        switch (State)
        {
            case BramblekinState.Socializing:
                UpdateSocializing(deltaTime, world);
                return;

            case BramblekinState.Wandering:
                float speed = leader == this ? WalkSpeed * LeaderWanderSpeedMultiplier : WalkSpeed;
                if (MoveTo(_wanderTarget, speed, deltaTime, world))
                    StartPause();
                return;

            case BramblekinState.Idle:
                _pauseTimer -= deltaTime;
                if (_pauseTimer <= 0f)
                    ChooseSocialAction(world);
                return;

            default:
                // Coming out of foraging, fleeing or a fight: catch its breath first.
                StartPause();
                return;
        }
    }

    /// <summary>
    /// After each rest: with odds of Sociability × <see cref="SocialSeekFactor"/>
    /// it goes to meet the nearest stranger it can see; a loner (below
    /// <see cref="LonerThreshold"/>) walks away from anyone crowding it;
    /// otherwise it simply wanders.
    /// </summary>
    private void ChooseSocialAction(World world)
    {
        if (!IsYoung && _rng.NextDouble() < Personality.Sociability * SocialSeekFactor && NearestStranger(world) is { } stranger)
        {
            _companion = stranger;
            _socializeTimer = SocializeTimeout;
            SetState(BramblekinState.Socializing);
            return;
        }

        if (Personality.Sociability < LonerThreshold && NearestOutsiderWithin(world, PersonalSpaceRadius) is { } crowder)
        {
            _wanderTarget = PointAwayFrom(crowder.Position, WanderRadius * 0.5f, world);
            SetState(BramblekinState.Wandering);
            return;
        }

        // Settled, it stays around home — and spends some of its time inside.
        // The young keep much closer.
        if (Home is { IsBuilt: true } home)
        {
            float range = IsYoung ? HomeRange / 3f : HomeRange;
            _wanderTarget = _rng.NextDouble() < 0.35 ? home.Position : RandomWanderPointAround(home.Position, range, world);
            SetState(BramblekinState.Wandering);
            return;
        }

        _wanderTarget = RandomWanderPoint(world, WanderRadius);
        SetState(BramblekinState.Wandering);
    }

    /// <summary>
    /// Wintering in: with nothing better to do in the lean season, it goes
    /// home and huddles inside, safe from Hornets and the Spider and burning
    /// Food at <see cref="WinterShelterMetabolism"/> of the usual rate.
    /// </summary>
    private void WinterIn(Shelter home, float deltaTime, World world)
    {
        if (!home.Contains(Position))
        {
            SetState(BramblekinState.HeadingHome);
            MoveTo(home.Position, WalkSpeed, deltaTime, world);
            return;
        }
        SetState(BramblekinState.Resting);
    }

    /// <summary>Walks up to the stranger it spotted; the World resolves the encounter once they're close.</summary>
    private void UpdateSocializing(float deltaTime, World world)
    {
        _socializeTimer -= deltaTime;
        if (_companion is not { IsDead: false } companion || _socializeTimer <= 0f || _knownKins.ContainsKey(companion.ID))
        {
            StartPause(); // Met them (or gave up).
            return;
        }

        float distance = GroundMover.HorizontalDistance(Position, companion.Position);
        if (distance > DetectionRadius * ThreatLeashMultiplier || distance <= World.EncounterRadius * 0.8f)
        {
            StartPause();
            return;
        }

        MoveTo(companion.Position, WalkSpeed, deltaTime, world);
    }

    /// <summary>Group Dynamics: a follower overrides its own wandering to stay within <see cref="FollowRadius"/> of its Leader, milling about near it once there.</summary>
    private void FollowLeader(Bramblekin leader, float deltaTime, World world)
    {
        float distance = GroundMover.HorizontalDistance(Position, leader.Position);
        if (distance > FollowRadius)
        {
            SetState(BramblekinState.Following);
            float speed = WalkSpeed * (distance > FollowRadius * 2f ? FollowCatchUpSpeedMultiplier : 1.1f);
            MoveTo(leader.Position, speed, deltaTime, world);
            return;
        }

        switch (State)
        {
            case BramblekinState.Idle:
                _pauseTimer -= deltaTime;
                if (_pauseTimer <= 0f)
                {
                    _wanderTarget = PointNear(leader.Position, FollowRadius * 0.7f, world);
                    SetState(BramblekinState.Wandering);
                }
                return;

            case BramblekinState.Wandering:
                if (MoveTo(_wanderTarget, WalkSpeed * 0.8f, deltaTime, world))
                    StartPause();
                return;

            default:
                StartPause();
                return;
        }
    }
}
