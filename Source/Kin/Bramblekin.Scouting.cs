using System.Numerics;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>A scout gives up on a place it can't reach after this long (s).</summary>
    private const float ScoutGiveUpSeconds = 45f;

    /// <summary>A scout has arrived when this close (m).</summary>
    private const float ScoutArrivalDistance = 3f;

    private Vector3? _scoutTarget;
    private float _scoutTimer;

    /// <summary>
    /// Scout: walks out to the nearest ground its clan hasn't seen (see <see cref="KnownMap"/>), marking
    /// what it passes, then on to the next. Stands down to rest when hurt; with nothing left to find it
    /// gathers like anyone else.
    /// </summary>
    private bool DoScoutDuty(KinGroup group, float deltaTime, World world)
    {
        if (Health <= MaxHealth * DutyStandDownHealthFraction || Home is not { IsBuilt: true } home)
            return false;

        if (_scoutTarget is { } current && group.Known.IsKnown(current))
            _scoutTarget = null; // Someone else saw it meanwhile.
        if (_scoutTarget is null)
        {
            _scoutTarget = world.ScoutTarget(group, home.Position);
            _scoutTimer = 0f;
        }
        if (_scoutTarget is not { } target)
            return DoGatherDuty(deltaTime, world); // The whole reach is mapped.

        SetState(BramblekinState.Traveling);
        _scoutTimer += deltaTime;
        if (GroundMover.HorizontalDistance(Position, target) <= ScoutArrivalDistance || _scoutTimer >= ScoutGiveUpSeconds)
        {
            group.Known.MarkCell(target); // Seen, or as near as it can get.
            _scoutTarget = null;
            return true;
        }
        MoveTo(target, WalkSpeed, deltaTime, world);
        return true;
    }
}
