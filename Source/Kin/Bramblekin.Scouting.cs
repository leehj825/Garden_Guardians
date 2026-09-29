using System.Numerics;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>A scout gives up on a place it can't reach after this long (s).</summary>
    private const float ScoutGiveUpSeconds = 45f;

    /// <summary>A scout has arrived when this close (m).</summary>
    private const float ScoutArrivalDistance = 3f;

    /// <summary>A raft crossing is planned only when walking round would be this many times the straight way, and the two banks are within <see cref="RaftMaxCrossing"/>.</summary>
    private const float RaftDetourRatio = 1.6f, RaftMaxCrossing = 40f;

    /// <summary>Poling speed (m/s), the odds per second of capsizing, and the longest a crossing may take (s) before it's cut short.</summary>
    private const float RaftSpeed = 1.3f, RaftCapsizeChancePerSecond = 0.004f, RaftMaxSeconds = 60f;

    /// <summary>A scout will walk this far (m) to a raft's launch point.</summary>
    private const float RaftLaunchReach = 25f;

    private Vector3? _scoutTarget;
    private float _scoutTimer;
    private Vector3? _raftLaunch, _raftLanding;
    private bool _raftPlanned;
    private float _raftTimer;
    private bool _onRaft;

    /// <summary>True while poling across the pond on a raft (see World.DrawRafts).</summary>
    public bool IsOnRaft => _onRaft;

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
            _raftPlanned = false;
            _raftLaunch = _raftLanding = null;
        }
        if (_scoutTarget is not { } target)
            return DoGatherDuty(deltaTime, world); // The whole reach is mapped.

        SetState(BramblekinState.Traveling);
        if (!_raftPlanned)
        {
            _raftPlanned = true;
            PlanRaftCrossing(group, target, world);
        }
        if (_raftLaunch is { } launch)
        {
            _scoutTimer = 0f; // The walk to the launch doesn't count against giving up.
            if (GroundMover.HorizontalDistance(Position, launch) <= 1.5f)
            {
                _onRaft = true;
                _raftTimer = 0f;
                world.NoteRaftLaunch(this);
                return true;
            }
            MoveTo(launch, WalkSpeed, deltaTime, world);
            return true;
        }
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

    /// <summary>
    /// A clan with rafts poles straight across the pond when the way round is long: picks the bank
    /// here and the bank by <paramref name="target"/>, if the two are near enough and the straight
    /// way is over water.
    /// </summary>
    private void PlanRaftCrossing(KinGroup group, Vector3 target, World world)
    {
        if (!World.Knows(group, Craft.Rafts))
            return;
        var from = new Vector2(Position.X, Position.Z);
        var to = new Vector2(target.X, target.Z);
        if (WaterMap.IsClearWay(from, to))
            return; // Nothing in the way.
        float straight = Vector2.Distance(from, to);
        if (straight > RaftMaxCrossing * 1.5f)
            return;
        if (WaterMap.FindRoute(from, to) is { } route)
        {
            float around = 0f;
            Vector2 previous = from;
            foreach (Vector2 waypoint in route)
            {
                around += Vector2.Distance(previous, waypoint);
                previous = waypoint;
            }
            around += Vector2.Distance(previous, to);
            if (around < straight * RaftDetourRatio)
                return; // Walking round is nearly as quick.
        }
        if (world.NearestShore(Position) is not { } launch || GroundMover.HorizontalDistance(launch, Position) > RaftLaunchReach ||
            world.NearestShore(target) is not { } landing || GroundMover.HorizontalDistance(launch, landing) > RaftMaxCrossing)
            return;
        _raftLaunch = launch;
        _raftLanding = landing;
    }

    /// <summary>On the raft: glides to the far bank (it may capsize and be put back at the launch).</summary>
    private bool PoleAcross(float deltaTime, World world)
    {
        if (_raftLanding is not { } landing || _raftLaunch is not { } launch)
        {
            _onRaft = false;
            return false;
        }
        SetState(BramblekinState.Traveling);
        _raftTimer += deltaTime;
        if (_rng.NextDouble() < RaftCapsizeChancePerSecond * deltaTime)
        {
            _mover.Position = launch;
            EndRaftTrip();
            world.NoteRaftMishap(this);
            return true;
        }

        float distance = GroundMover.HorizontalDistance(Position, landing);
        float step = RaftSpeed * deltaTime;
        if (distance <= step || _raftTimer >= RaftMaxSeconds)
        {
            _mover.Position = landing;
            EndRaftTrip();
            world.NoteRaftCrossing(this);
            return true;
        }
        Vector3 toward = landing - Position;
        var flat = new Vector2(toward.X, toward.Z);
        flat /= flat.Length();
        _mover.Position = new Vector3(Position.X + flat.X * step, Position.Y, Position.Z + flat.Y * step);
        _mover.Heading = flat;
        return true;
    }

    private void EndRaftTrip()
    {
        _onRaft = false;
        _raftLaunch = _raftLanding = null;
        _raftPlanned = true; // Don't plan another crossing to the same target.
        _mover.ResetProgress();
    }
}
