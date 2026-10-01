using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>Asleep, Hunger and Thirst rise at this fraction of the usual rate.</summary>
    private const float SleepMetabolism = 0.5f;

    /// <summary>Asleep, it only notices a threat this close (m) — unless its clan's watch has raised the alarm.</summary>
    private const float SleepSenseRadius = 2f;

    /// <summary>The night watch stays within this far (m) of home.</summary>
    private const float WatchPostRadius = 2.5f;

    /// <summary>Asleep for the night.</summary>
    public bool IsAsleep => State == BramblekinState.Sleeping;

    /// <summary>
    /// Night (fed and safe): time for bed. It goes home and sleeps inside
    /// (healing as it would resting there); with no home, it sleeps where it
    /// is — close by its Leader, if it follows one — out in the open. Not everyone sleeps: its clan's night watch keeps
    /// watch by home, a raiding party keeps at it (raids go best in the
    /// dark), an errand is seen through, and an alarm wakes the whole clan.
    /// Returns false when it isn't bedtime for it.
    /// </summary>
    private bool UpdateNight(float deltaTime, World world)
    {
        if (!world.IsNight)
        {
            if (State == BramblekinState.Sleeping)
                StartPause(); // Morning.
            return false;
        }

        KinGroup? group = world.GroupOf(this);
        if (group is not null && group.NightWatch == this)
            return KeepWatch(deltaTime, world);
        if (_errand is not null || (Job == KinJob.Raider && group?.Goal == GroupGoal.Raid && IsObedient) || world.IsAlarmed(group))
        {
            if (State == BramblekinState.Sleeping)
                StartPause();
            return false;
        }

        if (Home is { IsBuilt: true } home)
        {
            if (!home.Contains(Position))
            {
                SetState(BramblekinState.HeadingHome);
                MoveTo(home.Position, WalkSpeed, deltaTime, world);
                return true;
            }
            SetState(BramblekinState.Sleeping);
            HealWhileResting(home, deltaTime);
            return true;
        }

        // No home: bed down near its Leader, or right where it is.
        if (group?.Leader is { IsDead: false } leader && leader != this &&
            GroundMover.HorizontalDistance(Position, leader.Position) > FollowRadius)
        {
            SetState(BramblekinState.Following);
            MoveTo(leader.Position, WalkSpeed, deltaTime, world);
            return true;
        }
        SetState(BramblekinState.Sleeping);
        return true;
    }

    /// <summary>The night watch: stays up by home, and raises the alarm at anything it sees coming (see <see cref="Perceive"/>).</summary>
    private bool KeepWatch(float deltaTime, World world)
    {
        if (Home is not { IsBuilt: true } home)
            return false;
        if (GroundMover.HorizontalDistanceSquared(Position, home.Position) > WatchPostRadius * WatchPostRadius)
        {
            SetState(BramblekinState.Guarding);
            MoveTo(home.Position, WalkSpeed, deltaTime, world);
            return true;
        }
        if (State != BramblekinState.Guarding)
            SetState(BramblekinState.Guarding);
        return true;
    }

    /// <summary>Resting (or asleep) inside: 1 HP every <see cref="RestHealInterval"/> seconds (half again as fast in a House, faster still by a lit hearth).</summary>
    private void HealWhileResting(Shelter home, float deltaTime)
    {
        _restTimer += deltaTime;
        float healInterval = home.Tier == ShelterTier.House ? RestHealInterval / 1.5f : RestHealInterval;
        if (home.IsHearthLit)
            healInterval /= World.HearthRestHealFactor;
        if (_restTimer >= healInterval)
        {
            _restTimer -= healInterval;
            Heal(1);
        }
    }

    /// <summary>A sleeper out of doors: a few pale "z"s drifting up off it.</summary>
    private void DrawSleep(World world)
    {
        if (IsInsideHome)
            return;
        float t = world.ElapsedSeconds * 0.8f + ID * 0.37f;
        for (int i = 0; i < 3; i++)
        {
            float rise = (t + i / 3f) % 1f;
            Vector3 at = Position + new Vector3(0.1f + rise * 0.15f, BodyHeight + 0.15f + rise * 0.5f, 0f);
            Detail.Sphere(at, 0.03f + 0.02f * rise, SleepColor with { A = (byte)(220 * (1f - rise)) });
        }
    }

    private static readonly Color SleepColor = new(230, 235, 255, 255);
}
