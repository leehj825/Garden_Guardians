using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>After each rest, a bold Bramblekin goes for honey with odds this × its Courage (while there's honey to have and the hive is in reach).</summary>
    private const float HoneyForayChance = 0.25f;

    /// <summary>It only goes for honey from within this far (m) of the hive.</summary>
    private const float HoneyForayReach = 45f;

    /// <summary>Climbing up to the hive and working a comb free takes this long (s).</summary>
    private const float HoneyClimbSeconds = 2.5f;

    /// <summary>A honey foray is given up after this long (s).</summary>
    private const float HoneyForayTimeout = 45f;

    private float _honeyTimer;

    /// <summary>Only the bold go for honey: fed, fit, grown, empty-handed, and brave enough.</summary>
    public bool CanGoForHoney(World world) =>
        world.HoneyToHave && !IsDead && !IsYoung && !IsSick && !IsHungry && _carried is null && Health >= MaxHealth * 0.6f &&
        Personality.Courage >= 0.45f && State != BramblekinState.GatheringHoney && _errand is null &&
        GroundMover.HorizontalDistance(Position, World.HiveFoot) <= HoneyForayReach;

    /// <summary>Off to the hive for a comb (sent by its Leader, or on its own account).</summary>
    public void GoForHoney()
    {
        _honeyTimer = 0f;
        SetState(BramblekinState.GatheringHoney);
    }

    private bool TryStartHoneyForay(World world)
    {
        if (!CanGoForHoney(world) || _rng.NextDouble() >= HoneyForayChance * Personality.Courage)
            return false;
        GoForHoney();
        return true;
    }

    /// <summary>
    /// A honey foray under way: walks to the foot of the oak, climbs up to
    /// the hive and works a comb free (see <see cref="World.TakeHoney"/>) —
    /// then home with it, like any food. Returns false once it's over.
    /// </summary>
    private bool UpdateHoneyForay(float deltaTime, World world)
    {
        if (State != BramblekinState.GatheringHoney)
            return false;
        _honeyTimer += deltaTime;
        if (!world.HoneyToHave || _carried is not null || _honeyTimer > HoneyForayTimeout)
        {
            StartPause();
            return false;
        }
        if (GroundMover.HorizontalDistance(Position, World.HiveFoot) > 0.8f)
        {
            MoveTo(World.HiveFoot, WalkSpeed, deltaTime, world);
            _climbTimer = 0f;
            return true;
        }
        _climbTimer += deltaTime;
        if (_climbTimer < HoneyClimbSeconds)
            return true;
        _carried = world.TakeHoney(this);
        StartPause();
        return true;
    }

    private float _climbTimer;
}
