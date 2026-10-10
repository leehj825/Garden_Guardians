using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    // --- Under the player's control (see PlayControl): the kin walks where the stick says and strikes when told ----------------------

    /// <summary>A kin under control jogs a little faster than it walks; with the run toggle on it runs.</summary>
    private const float PlayerRunSpeed = WalkSpeed * 1.35f, PlayerSprintSpeed = WalkSpeed * 2.3f;

    /// <summary>A jump lasts this long (s) from take-off to landing, and carries the body this high (m) at the top. Only one at a time: a new one waits until the kin is down. The jump clip is stretched over <c>JumpClipSeconds</c>, a little longer than the flight (the clip's natural 2.4 s is too slow for the motion, the flight's own 0.7 s too quick for the pose), so its last moments are the kin settling after the landing.</summary>
    private const float JumpSeconds = 0.7f, JumpPeak = 0.5f, JumpClipSeconds = 1.0f;

    /// <summary>A hunter's arrow flies this far (m) before it drops, and its bow takes this long (s) to be ready again; the arrow leaves the bow this long (s) into the aim clip.</summary>
    public const float ArrowRange = 12f;
    private const float ArrowReleaseDelay = 0.3f;

    /// <summary>The aim is helped towards a target within this half-angle (radians) of where the player looks.</summary>
    private const float AimAssistCone = 0.5f;

    /// <summary>A strike reaches this far (m) past the edges of the two bodies, and takes this long (s) to recover from; its swing lasts this long (s).</summary>
    private const float PlayerReach = 1.4f;

    /// <summary>The next blow or shot may start this share of the way to the end of the clip of the last one: the player's combo runs on with no gap.</summary>
    private const float BlowRecoveredShare = 0.97f;

    /// <summary>It stands still this near (m) to food, its store or the water, hungry or thirsty, and eats or drinks on its own.</summary>
    private const float PlayerFoodReach = 2f, PlayerStoreReach = 3.5f, PlayerWaterReach = 2.5f;

    private float _swing, _arrowDelay, _jumpTime, _jumpHeight;

    /// <summary>True while the player is steering it (see <see cref="PlayControl"/>).</summary>
    public bool IsPlayerControlled { get; private set; }

    /// <summary>The way it faces.</summary>
    public Vector2 Facing => _mover.Heading;

    /// <summary>Where the stick says to go, in world X/Z, length 0 to 1.</summary>
    public Vector2 PlayerMove { get; set; }

    /// <summary>Where the player is looking, in world X/Z (the camera's way): a hunter's arrows go this way unless a target is near it.</summary>
    public Vector2 PlayerLook { get; set; } = Vector2.UnitX;

    /// <summary>The run toggle: while on (and moving) the kin runs.</summary>
    public bool PlayerRunning { get; set; }

    /// <summary>Set while the shoot button is held (a Hunter's bow: the Attack button is a plain blow for every job).</summary>
    public bool PlayerWantsShoot { get; set; }

    /// <summary>Set for the frame the jump button is pressed.</summary>
    public bool PlayerWantsJump { get; set; }

    /// <summary>True from take-off to landing: no second jump, and no striking, until it is over.</summary>
    public bool IsAirborne { get; private set; }

    /// <summary>True while the jump clip plays: the flight and the settling after it.</summary>
    private bool _jumpClipPlaying;

    /// <summary>How far through the jump clip it is, 0 to 1.</summary>
    private float JumpProgress => Math.Clamp(_jumpTime / JumpClipSeconds, 0f, 1f);

    /// <summary>Under control with the run toggle on, or the stick pushed right to its edge.</summary>
    private bool IsRunning => IsPlayerControlled && (PlayerRunning || PlayerStickRun);

    /// <summary>Set each frame by <see cref="PlayControl"/>: the stick is at its very edge (or Shift is held), which runs.</summary>
    public bool PlayerStickRun { get; set; }

    /// <summary>
    /// Explorer: the player's gentler form of control (see <see cref="Build.Explore"/>): it picks up what it walks near, eats and drinks on its own even
    /// while walking, and cannot be hurt.
    /// </summary>
    public bool PlayerExplorer { get; set; }

    /// <summary>An explorer picks up what lies this near (m), and looks again this often (s).</summary>
    private const float PlayerAutoPickReach = 0.9f, PlayerAutoPickEvery = 0.35f;

    private float _autoPickCooldown;

    /// <summary>Set while the attack button is held.</summary>
    public bool PlayerWantsStrike { get; set; }

    /// <summary>Whether a strike may hit Bramblekin of other clans too (it always hits wildlife).</summary>
    public bool PlayerMayHitKin { get; set; }

    /// <summary>The job the player has chosen for the kin while it is under control: None ("normal"), Hunter, Swordsman, Spearman or Fisher.</summary>
    public KinJob PlayerJob { get; private set; }

    /// <summary>The clan (if any) the kin belongs to again when the player gives it back. While set, the kin has no clan, village or kingdom.</summary>
    public Guid? AwayGroupId { get; private set; }

    private KinJob _awayJob;
    private float _awayLoyalty;

    /// <summary>Steps the player's job choice round: normal, Hunter, Swordsman, Spearman, Fisher.</summary>
    public void CyclePlayerJob()
    {
        PlayerJob = PlayerJob switch { KinJob.None => KinJob.Hunter, KinJob.Hunter => KinJob.Swordsman, KinJob.Swordsman => KinJob.Spearman, KinJob.Spearman => KinJob.Fisher, _ => KinJob.None };
        if (IsPlayerControlled)
            AssignJob(PlayerJob);
    }

    /// <summary>
    /// Takes the wheel, or gives it back to the kin's own mind. Under control the kin stands alone: it leaves its clan (and with it the village
    /// and kingdom), holds no village job and takes the job the player picked. Given back, it rejoins its clan, if that still stands, and takes its old job again.
    /// </summary>
    public void SetPlayerControlled(bool on, World world)
    {
        if (on)
            Sfx.Listener = this;
        else if (Sfx.Listener == this)
            Sfx.Listener = null;
        if (on && !IsPlayerControlled)
        {
            AwayGroupId = GroupId;
            _awayJob = Job;
            _awayLoyalty = Loyalty;
            LeaveGroup(); // (Also clears Job.)
            VillageJob = KinJob.None;
            IsPaid = false;
            AssignJob(PlayerJob);
        }
        else if (!on && IsPlayerControlled)
        {
            AssignJob(KinJob.None);
            IsPlayerControlled = false;
            PlayerExplorer = false;
            PlayerStickRun = false;
            if (AwayGroupId is { } clan && world.GroupExists(clan))
            {
                JoinGroup(clan);
                Loyalty = _awayLoyalty;
                AssignJob(_awayJob);
            }
            AwayGroupId = null;
        }
        IsPlayerControlled = on;
        PlayerMove = Vector2.Zero;
        PlayerWantsStrike = false;
        PlayerWantsJump = false;
        PlayerWantsShoot = false;
        PlayerRunning = false;
        IsAirborne = false;
        _jumpClipPlaying = false;
        _jumpTime = _jumpHeight = _arrowDelay = 0f;
        _swing = 0f;
        CombatTarget = null;
        _fleeTimer = 0f;
        _robTarget = null;
        SetState(BramblekinState.Idle);
    }

    /// <summary>One step under control: a swing, a meal or a drink under way, the strike button, eating or drinking where it stands, else the stick.</summary>
    private void UpdatePlayerControl(float deltaTime, World world)
    {
        _perceptionTimer -= deltaTime;
        if (_perceptionTimer <= 0f)
        {
            _perceptionTimer += PerceptionInterval;
            Perceive(world); // (Only for the food it sees; nothing here reacts to a threat.)
        }

        if (!IsAirborne && PlayerWantsJump && _swing <= 0f && State != BramblekinState.Eating && !(State == BramblekinState.Drinking && _drinkTimer > 0f))
        {
            IsAirborne = true;
            _jumpClipPlaying = true;
            _jumpTime = 0f;
        }
        if (_jumpClipPlaying)
        {
            _jumpTime += deltaTime;
            if (_jumpTime >= JumpClipSeconds)
                _jumpClipPlaying = false;
        }
        if (IsAirborne)
        {
            float t = Math.Clamp(_jumpTime / JumpSeconds, 0f, 1f);
            _jumpHeight = 4f * JumpPeak * t * (1f - t);
            if (t >= 1f)
            {
                IsAirborne = false;
                _jumpHeight = 0f;
            }
            SetState(BramblekinState.Idle);
            if (PlayerMove.LengthSquared() > 0.01f)
                MovePlayer(deltaTime, world); // (a jump carries on in the direction of the stick)
            else
                _mover.Idle();
            return;
        }

        if (_swing > 0f)
        {
            UpdatePlayerBlow(deltaTime, world);
            return;
        }
        if (State == BramblekinState.Eating)
        {
            UpdateHunger(deltaTime, world);
            return;
        }
        if (State == BramblekinState.Drinking && _drinkTimer > 0f)
        {
            UpdateThirst(deltaTime, world);
            return;
        }

        if (PlayerWantsShoot && Job == KinJob.Hunter && _strikeCooldown <= 0f)
        {
            PlayerAim(world);
            return;
        }
        if (PlayerWantsStrike && _strikeCooldown <= 0f)
        {
            PlayerStrike(world);
            return;
        }

        bool moving = PlayerMove.LengthSquared() > 0.01f;
        if (PlayerExplorer)
        {
            _autoPickCooldown -= deltaTime;
            if (moving && _autoPickCooldown <= 0f)
            {
                _autoPickCooldown = PlayerAutoPickEvery;
                if (PlayerPickUp(world, auto: true))
                    return; // (it bends to pick it up)
            }
        }
        if ((!moving || PlayerExplorer) && TryPlayerSustain(deltaTime, world))
            return;
        if (!moving && Job == KinJob.Fisher && TryPlayerFishing(deltaTime, world))
            return;

        SetState(BramblekinState.Idle);
        if (moving)
            MovePlayer(deltaTime, world);
        else
            _mover.Idle();
    }

    /// <summary>A blow or shot under way: its time runs down (the clip's own, so the next one can start the moment it ends) and the arrow leaves the bow on its cue.</summary>
    private void UpdatePlayerBlow(float deltaTime, World world)
    {
        _swing -= deltaTime;
        if (_arrowDelay > 0f)
        {
            _arrowDelay -= deltaTime;
            if (_arrowDelay <= 0f)
                LoosePlayerArrow(world);
        }
        SetState(BramblekinState.Fighting);
    }

    /// <summary>One step along the stick: a jog, or a run with the run toggle on.</summary>
    private void MovePlayer(float deltaTime, World world)
    {
        float speed = (IsRunning ? PlayerSprintSpeed : PlayerRunSpeed) * AgeSpeedFactor * VigorSpeedFactor * (IsSick ? SickSpeedFactor : 1f) * world.PathSpeed(Position);
        _mover.Step(PlayerMove, speed, deltaTime, world);
    }

    /// <summary>True if <paramref name="other"/> is of the clan the kin came from (or belongs to).</summary>
    public bool IsClanmate(Bramblekin other) => (AwayGroupId ?? GroupId) is { } clan && other.GroupId == clan;

    /// <summary>The damage an arrow of this kin does to <paramref name="target"/> (and the hunting practice it earns): see <see cref="World.LooseArrow"/>.</summary>
    public int ArrowHit(ICombatant target, World world)
    {
        Train(target is Bramblekin ? Skill.Fighting : Skill.Hunting, world, target is StagBeetle or WolfSpider ? 2f : 1f);
        return (int)MathF.Round(BaseStrike * Profile.Arrow * (target is Bramblekin ? 1f : 1f + 0.5f * SkillAt(Skill.Hunting)));
    }

    /// <summary>The bow is drawn: the kin turns to the way it will shoot, the aim clip plays, and the arrow follows partway through (see <see cref="LoosePlayerArrow"/>).</summary>
    private void PlayerAim(World world)
    {
        _jumpClipPlaying = false;
        BeginBlow(world, ranged: true);
        float clipSeconds = BramblekinModel.ActionSeconds(_actionClip);
        _strikeCooldown = clipSeconds * BlowRecoveredShare;
        _swing = clipSeconds;
        _arrowDelay = ArrowReleaseDelay;
        SetState(BramblekinState.Fighting);
        Vector3 toward = AimDirection(world);
        var flat = new Vector2(toward.X, toward.Z);
        if (flat.LengthSquared() > 1e-6f)
            _mover.Heading = Vector2.Normalize(flat);
    }

    /// <summary>Where an arrow loosed now would go: at the nearest target within reach and near the player's line of sight, else the way the player looks.</summary>
    private Vector3 AimDirection(World world) => AimDirection(world, out _);

    private Vector3 AimDirection(World world, out ICombatant? best)
    {
        Vector2 look = PlayerLook.LengthSquared() > 1e-6f ? Vector2.Normalize(PlayerLook) : _mover.Heading;
        Vector3 from = Position + new Vector3(0f, BodyHeight * 0.75f, 0f);
        best = null;
        float bestDistance = ArrowRange;
        foreach (ICombatant candidate in world.ArrowTargets(this))
        {
            var flat = new Vector2(candidate.Position.X - Position.X, candidate.Position.Z - Position.Z);
            float distance = flat.Length();
            if (distance >= bestDistance || distance < 1e-3f || Vector2.Dot(look, flat / distance) < MathF.Cos(AimAssistCone))
                continue;
            best = candidate;
            bestDistance = distance;
        }
        if (best is null)
            return new Vector3(look.X, 0.03f, look.Y);
        Vector3 to = best.Position + new Vector3(0f, MathF.Max(0.15f, best.CollisionRadius), 0f) - from;
        return Vector3.Normalize(to);
    }

    /// <summary>A hunter under control is shown where its next arrow goes: a red ring round the target it has locked on to, else a yellow mark where the aim (the camera's way) reaches, with the arrow's path.</summary>
    private void DrawAimMarker(World world)
    {
        Vector3 direction = AimDirection(world, out ICombatant? target);
        Vector3 from = Position + new Vector3(0f, BodyHeight * 0.75f, 0f);
        var flatNormal = new Vector3(1f, 0f, 0f);
        if (target is not null)
        {
            Vector3 at = target.Position;
            float ground = World.GetHeightAt(at.X, at.Z) + 0.06f;
            float radius = MathF.Max(0.3f, target.CollisionRadius + 0.25f);
            var red = new Color(235, 50, 40, 230);
            Raylib.DrawCircle3D(new Vector3(at.X, ground, at.Z), radius, flatNormal, 90f, red);
            Raylib.DrawCircle3D(new Vector3(at.X, ground, at.Z), radius * 0.8f, flatNormal, 90f, red);
            Vector3 top = new(at.X, at.Y + MathF.Max(0.6f, target.CollisionRadius * 2f) + 0.2f, at.Z);
            Raylib.DrawLine3D(top, top + new Vector3(0f, 0.25f, 0f), red); // (an arrow-shaped marker above it)
            Raylib.DrawLine3D(top, top + new Vector3(0.08f, 0.12f, 0f), red);
            Raylib.DrawLine3D(top, top + new Vector3(-0.08f, 0.12f, 0f), red);
            return;
        }

        Vector3 end = from + direction * ArrowRange;
        var yellow = new Color(255, 225, 80, 230);
        Raylib.DrawLine3D(from, end, new Color(255, 225, 80, 110));
        float floor = World.GetHeightAt(end.X, end.Z) + 0.06f;
        var mark = new Vector3(end.X, floor, end.Z);
        Raylib.DrawCircle3D(mark, 0.55f, flatNormal, 90f, yellow);
        Raylib.DrawCircle3D(mark, 0.25f, flatNormal, 90f, yellow);
        Raylib.DrawLine3D(mark + new Vector3(-0.8f, 0f, 0f), mark + new Vector3(0.8f, 0f, 0f), yellow);
        Raylib.DrawLine3D(mark + new Vector3(0f, 0f, -0.8f), mark + new Vector3(0f, 0f, 0.8f), yellow);
    }

    /// <summary>The arrow leaves the bow.</summary>
    private void LoosePlayerArrow(World world)
    {
        Vector3 direction = AimDirection(world);
        var flat = new Vector2(direction.X, direction.Z);
        if (flat.LengthSquared() > 1e-6f)
            _mover.Heading = Vector2.Normalize(flat);
        Vector3 from = Position + new Vector3(0f, _jumpHeight + BodyHeight * 0.75f, 0f) + new Vector3(_mover.Heading.X, 0f, _mover.Heading.Y) * 0.4f;
        world.LooseArrow(this, from, direction);
    }

    /// <summary>Standing still, hungry or thirsty, with food, its store or the water at hand: it eats or drinks. True while it does.</summary>
    private bool TryPlayerSustain(float deltaTime, World world)
    {
        if (IsThirsty && World.NearestShoreSpot(Position, PlayerWaterReach, creek: true) is { } shore)
        {
            if (State != BramblekinState.Drinking || _waterSpot is null)
            {
                SetState(BramblekinState.Drinking);
                _waterSpot = shore;
                _drinkFrom = null;
                _drinkWell = null;
                _drinkGeneration = WaterMap.Generation;
                _drinkTimer = 0f;
            }
            UpdateThirst(deltaTime, world);
            return true;
        }
        if (!IsHungry)
            return false;
        TakeMealFromPack(world);
        if (_carried is not null)
        {
            StartEating();
            return true;
        }
        if (world.NearestAvailableFood(Position, PlayerFoodReach, this) is { } food)
        {
            ApproachFood(food, WalkSpeed, deltaTime, world, eatOnArrival: true);
            return true;
        }
        if (StoreToEatFrom(world) is { } store && GroundMover.HorizontalDistanceSquared(Position, store.Position) <= PlayerStoreReach * PlayerStoreReach)
        {
            GoHomeAndEat(store, deltaTime, world);
            return true;
        }
        return false;
    }

    /// <summary>A fisher standing still at the water's edge casts again and again, and eats what it lands. True while it fishes.</summary>
    private bool TryPlayerFishing(float deltaTime, World world)
    {
        if (World.NearestShoreSpot(Position, PlayerWaterReach, creek: true) is not { } shore)
        {
            _fishingTimer = 0f;
            _fishingSpot = null;
            return false;
        }
        SetState(BramblekinState.Fishing);
        _fishingSpot = Position; // (the rod is drawn where the kin stands)
        _mover.Heading = TowardWater(shore);
        _mover.Idle();
        _fishingTimer += deltaTime;
        if (_fishingTimer < FishingSeconds * 0.6f)
            return true;
        _fishingTimer = 0f;
        Train(Skill.Fishing, world, 0.5f);
        float skill = 1f + 0.5f * SkillAt(Skill.Fishing);
        if (_rng.NextDouble() >= MathF.Min(0.95f, CatchChance(world.CurrentSeason) * skill) * (0.3f + 0.7f * WaterMap.Fullness))
            return true;
        if (world.CatchFish(this) is { } fish)
        {
            world.QueueFloatingText(Position, "Fish!", EggTextColor);
            if (!Stow(fish, world))
            {
                _carried = fish;
                StartEating();
            }
        }
        return true;
    }

    /// <summary>A swing at whatever is nearest in front: wildlife, an ant, an enemy — and, if the player allows it, Bramblekin of other clans.</summary>
    private void PlayerStrike(World world)
    {
        _jumpClipPlaying = false;
        BeginBlow(world);
        float clipSeconds = BramblekinModel.ActionSeconds(_actionClip);
        _strikeCooldown = clipSeconds * BlowRecoveredShare;
        _swing = clipSeconds;
        SetState(BramblekinState.Fighting);

        Vector2 heading = _mover.Heading.LengthSquared() > 1e-6f ? Vector2.Normalize(_mover.Heading) : Vector2.UnitX;
        ICombatant? best = null;
        float bestDistance = float.MaxValue;

        void Consider(ICombatant? candidate)
        {
            if (candidate is null || candidate.IsDead || ReferenceEquals(candidate, this))
                return;
            float distance = GroundMover.HorizontalDistance(Position, candidate.Position);
            if (distance > BodyRadius + candidate.CollisionRadius + PlayerReach + Profile.Reach || distance >= bestDistance)
                return;
            if (distance > 0.7f)
            {
                var toward = new Vector2(candidate.Position.X - Position.X, candidate.Position.Z - Position.Z) / distance;
                if (Vector2.Dot(heading, toward) < 0.25f)
                    return; // Behind it.
            }
            best = candidate;
            bestDistance = distance;
        }

        Consider(world.Spider);
        foreach (Hornet hornet in world.Hornets)
            Consider(hornet);
        foreach (InvaderSpider invader in world.Invaders)
            Consider(invader);
        foreach (StagBeetle beetle in world.Beetles)
            Consider(beetle);
        foreach (Grub grub in world.Grubs)
            Consider(grub);
        foreach (Ant ant in world.Ants)
            Consider(ant);
        foreach (HillGuard guard in world.HillGuards)
        {
            if (!guard.IsHidden)
                Consider(guard);
        }
        if (PlayerMayHitKin)
        {
            foreach (Bramblekin other in world.QueryColonyWithin(Position, PlayerReach + 2f))
            {
                if (other != this && !other.IsDead && !other.IsSheltered && ((AwayGroupId ?? GroupId) is not { } clan || other.GroupId != clan))
                    Consider(other);
            }
        }

        if (best is null)
            return;
        var face = new Vector2(best.Position.X - Position.X, best.Position.Z - Position.Z);
        if (face.LengthSquared() > 1e-6f)
            _mover.Heading = Vector2.Normalize(face);
        Train(best is Bramblekin ? Skill.Fighting : Skill.Hunting, world, best is StagBeetle or WolfSpider ? 2f : 1f);
        best.TakeHit(best is Bramblekin ? StrikeDamage : HuntingDamage, this, world);
    }

    /// <summary>For the HUD: what a controlled kin can do about being hungry or thirsty.</summary>
    public string? PlayerHint =>
        PlayerExplorer && IsThirsty ? "Thirsty: walk to the water's edge to drink"
        : PlayerExplorer && IsHungry ? "Hungry: eat from the bag, or walk to food or your clan's store"
        : PlayerExplorer ? null
        : IsThirsty ? "Thirsty: stand still at the water's edge to drink"
        : Job == KinJob.Fisher && !IsHungry ? "Fisher: stand still at the water's edge to fish"
        : IsHungry ? "Hungry: stand still by food or at your own home's store to eat"
        : null;
}
