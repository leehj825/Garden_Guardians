using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>What the Wolf Spider is currently doing.</summary>
public enum SpiderState
{
    /// <summary>Default: ambling slowly between random points.</summary>
    Prowling,

    /// <summary>Stalking a Bramblekin whose footsteps it can feel.</summary>
    Hunting,

    /// <summary>Committed dash at its target; kills any Bramblekin it touches.</summary>
    Pouncing,

    /// <summary>Getting its legs back under it after a pounce.</summary>
    Recovering,

    /// <summary>Dormant: no longer reachable now that the player's pebble-impact distraction has been removed.</summary>
    Investigating,

    /// <summary>Eating a catch: stays put and ignores everything for a while.</summary>
    Feeding,

    /// <summary>Dormant: no longer reachable now that the player's Gust has been removed.</summary>
    Tumbled,
}

/// <summary>
/// The first predator (Garden_Guardians_Design.md, "The Wolf Spider"). It is
/// blind in this prototype and hunts purely by vibration:
///
///   Prowling --feels a busy worker within VibrationRadius--> Hunting
///   Hunting --within PounceRange--> Pouncing (dash; touching = kill)
///   Pouncing --dash over--> Recovering (1 s) --> Hunting or Prowling
///   Pouncing --caught one--> Feeding (20 s, ignores everything) --> Prowling
///
/// Feeding caps how fast it can kill: without it every victim's dropped
/// food lures the next forager in, and the colony dies in a chain.
///
/// Its only counter is the Bramblekin themselves: any Bramblekin whose
/// fight-or-flight roll comes up "fight" (far likelier in a group, see
/// Bramblekin.RollFightOrFlight) closes in and strikes it. A Bramblekin
/// that is Fighting is never caught by a pounce; instead the spider Bites
/// the nearest fighter in range on its own cooldown. Health reaching 0,
/// from either side, is death — and a slain spider leaves a pile of Food
/// behind (see World.DamageSpider).
/// </summary>
public sealed class WolfSpider : ICombatant
{
    /// <summary>The model's scale: its legs span about 2.5 m.</summary>
    private const float ModelScale = 2.5f / PropModels.SpiderWidth;

    /// <summary>Collision radius (m) — twice a Bramblekin's.</summary>
    public const float BodyRadius = Bramblekin.BodyRadius * 2f;

    /// <summary>How far (m) it can feel a gathering/returning Bramblekin's footsteps.</summary>
    public const float VibrationRadius = 7f;

    /// <summary>Dormant: was how far (m) it could feel a pebble slam into the ground, back when the player had a pebble to drop.</summary>
    public const float ImpactHearingRadius = 12f;

    /// <summary>Safety in numbers: prey with at least <see cref="CrowdSize"/> others within this many meters is left alone — see <see cref="IsInACrowd"/>.</summary>
    public const float CrowdRadius = 2.5f;

    public const int CrowdSize = 2;

    /// <summary>Distance (m) at which a hunting spider launches its pounce.</summary>
    public const float PounceRange = 2.5f;

    private const float ProwlSpeed = 0.6f;
    private const float HuntSpeed = 1.5f;      // Faster than a walking Bramblekin, slower than a fleeing one.
    private const float PounceSpeed = 7f;
    private const float PounceDuration = 0.45f;
    private const float RecoverDuration = 1f;
    private const float ProwlPauseDuration = 1.5f;
    private const float StareDuration = 3f;
    private const float FeedDuration = 20f;

    /// <summary>Dormant: was how long a Gust-tumbled spider was stunned for, in seconds, back when the player had a Gust to cast.</summary>
    public const float TumbledDuration = 4f;

    /// <summary>Hit points out of <see cref="MaxHealth"/>.</summary>
    public const int MaxHealth = 50;

    /// <summary>Sustained Combat: how close a fighting Bramblekin must be for the spider to Bite it.</summary>
    private const float BiteRange = 1.5f;

    /// <summary>Bite damage dealt to the nearest fighting Bramblekin in range.</summary>
    public const int BiteDamage = 10;

    /// <summary>Cooldown (s) between Bites.</summary>
    private const float BiteCooldownDuration = 1.5f;

    /// <summary>
    /// Seconds a hunt continues after the target stops vibrating (e.g. it
    /// panicked and dropped its food) before the spider gives up on it.
    /// </summary>
    private const float ChaseMemory = 2f;

    /// <summary>How close (m) to an impact point it goes before staring.</summary>
    private const float InvestigateStandOff = 1.2f;

    /// <summary>Gives up walking to an impact after this long (s) and just stares from where it is.</summary>
    private const float InvestigateTravelTimeout = 8f;

    private static readonly Color BodyColor = new(45, 42, 40, 255);
    private static readonly Color LegColor = new(30, 28, 26, 255);

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private Vector3 _target;               // Prowl point, or impact point while investigating.
    private Bramblekin? _prey;
    private float _timer;                  // Pause / dash / recover / stare / travel timer, by state.
    private float _sinceVibration;         // Seconds since the prey last vibrated.
    private Vector2 _pounceDirection;
    private bool _staring;
    private float _walkCycle;              // Leg animation phase.
    private float _biteCooldown;

    /// <summary>Terrain-aware, same treatment as Bramblekin — Y is snapped to World.GetHeightAt every read.</summary>
    public Vector3 Position => _mover.GroundedPosition;

    public SpiderState State { get; private set; } = SpiderState.Prowling;

    /// <summary>Bramblekin killed so far.</summary>
    public int Kills { get; private set; }

    /// <summary>Hit points out of <see cref="MaxHealth"/>.</summary>
    public int Health { get; private set; } = MaxHealth;

    /// <summary>True once slain — see <see cref="World.DamageSpider"/>. Bramblekin still holding a reference to it (as a threat or a fight target) check this.</summary>
    public bool IsDead { get; private set; }

    public float CollisionRadius => BodyRadius;

    public WolfSpider(Vector3 position, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, edgeMargin: 1f, rng);
        _target = position;
        _timer = ProwlPauseDuration;
    }

    /// <summary>
    /// Sustained Combat: a Bramblekin strike's damage. Purely a Health
    /// mutation — never touches State — so it can never wake a Tumbled
    /// spider early (see the hard lock at the top of Update()). Death itself
    /// (Health reaching 0) is World's call, not this method's: see
    /// World.DamageSpider.
    /// </summary>
    public void TakeDamage(int amount) => Health = Math.Max(0, Health - amount);

    /// <summary>A Bramblekin's strike — routed through World so a killing blow is handled in one place.</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world) => world.DamageSpider(damage, attacker);

    /// <summary>Called once, by World.DamageSpider, when Health reaches 0.</summary>
    public void MarkDead() => IsDead = true;

    public void Update(float deltaTime, World world)
    {
        _mover.Idle();

        // Tumbled is a dormant hard lock (nothing triggers it any more, now
        // that the player's Gust is gone), checked and handled before
        // anything else in this method — the prey safety net, the Bite
        // retaliation below, every bit of vision/AI. Nothing can
        // re-target, re-notice, retaliate or otherwise step on the stun
        // early — not even taking strike damage (TakeDamage is a pure Health
        // mutation that never touches State); the only way out is the timer
        // counting all the way down to zero on its own. A dedicated early
        // return makes that structurally impossible to short-circuit,
        // rather than relying on every future addition to remember to check
        // for it.
        if (State == SpiderState.Tumbled)
        {
            _timer -= deltaTime;
            if (_timer <= 0f)
                StartProwling();
            return;
        }

        // Sustained Combat: while awake, retaliate against the nearest
        // fighting Bramblekin in range on its own cooldown, regardless of
        // what else it's otherwise doing (prowling, hunting, even
        // mid-pounce) — a reflex, not a deliberate target choice the way
        // Hunt/Pounce are.
        _biteCooldown = MathF.Max(0f, _biteCooldown - deltaTime);
        if (_biteCooldown <= 0f && NearestFighterInRange(world, BiteRange) is { } target)
        {
            target.TakeDamage(BiteDamage, world, DeathCause.Predator, this);
            _biteCooldown = BiteCooldownDuration;
        }

        // Safety net: if the Bramblekin we're tracking died or vanished by
        // any means since last frame, drop the reference immediately rather
        // than move toward or read a dead target. Only forces the state back
        // to Prowling out of an active Hunt — a Pounce already in flight
        // doesn't use _prey for its hit test, so it's left to finish (and,
        // on a kill, sets Feeding itself).
        if (_prey is not null && _prey.IsDead)
        {
            _prey = null;
            if (State == SpiderState.Hunting)
                StartProwling();
        }

        switch (State)
        {
            case SpiderState.Prowling:
                // Busy workers give themselves away.
                if (FindPrey(world) is { } prey)
                {
                    StartHunting(prey);
                    break;
                }
                Prowl(deltaTime, world);
                break;

            case SpiderState.Hunting:
                Hunt(deltaTime, world);
                break;

            case SpiderState.Pouncing:
                Pounce(deltaTime, world);
                break;

            case SpiderState.Recovering:
                _timer -= deltaTime;
                if (_timer <= 0f)
                {
                    if (FindPrey(world) is { } next)
                        StartHunting(next);
                    else
                        StartProwling();
                }
                break;

            case SpiderState.Investigating:
                Investigate(deltaTime, world);
                break;

            case SpiderState.Feeding:
                _timer -= deltaTime;
                if (_timer <= 0f)
                    StartProwling();
                break;

            // SpiderState.Tumbled is handled by the early return above.
        }

        if (_mover.IsMoving)
            _walkCycle += deltaTime * (State == SpiderState.Pouncing ? 30f : 12f);
    }

    // --- States ---------------------------------------------------------------------

    private void Prowl(float deltaTime, World world)
    {
        // Short pause at each point, then pick another.
        if (_timer > 0f)
        {
            _timer -= deltaTime;
            if (_timer <= 0f)
            {
                _target = world.RandomFreePoint(BodyRadius + 0.1f, 1f);
                _mover.ResetProgress();
            }
            return;
        }

        if (_mover.MoveTowards(_target, ProwlSpeed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)))
            _timer = ProwlPauseDuration;
    }

    private void Hunt(float deltaTime, World world)
    {
        // Keep chasing while the prey is alive, in range, and either still
        // vibrating or only recently gone quiet. Otherwise switch to another
        // busy worker if there is one, or give up. (A Bramblekin is marked
        // dead before it's taken out of the Colony, so IsDead is enough.)
        if (_prey is null || _prey.IsDead ||
            GroundMover.HorizontalDistanceSquared(Position, _prey.Position) > (VibrationRadius * 1.5f) * (VibrationRadius * 1.5f))
        {
            _prey = null;
        }
        else
        {
            _sinceVibration = _prey.IsVibrating ? 0f : _sinceVibration + deltaTime;
            if (_sinceVibration > ChaseMemory)
                _prey = null;
        }

        if (_prey is null)
        {
            if (FindPrey(world) is { } other)
                StartHunting(other);
            else
                StartProwling();
            return;
        }

        float distance = GroundMover.HorizontalDistance(Position, _prey.Position);
        if (distance <= PounceRange)
        {
            // Commit to a straight dash at where the prey is right now; a
            // quick Bramblekin can still sidestep it.
            var toPrey = new Vector2(_prey.Position.X - Position.X, _prey.Position.Z - Position.Z);
            _pounceDirection = toPrey.LengthSquared() > 1e-6f ? Vector2.Normalize(toPrey) : _mover.Heading;
            _timer = PounceDuration;
            SetState(SpiderState.Pouncing);
            return;
        }

        _mover.MoveTowards(_prey.Position, HuntSpeed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
    }

    private void Pounce(float deltaTime, World world)
    {
        // Dash along the committed direction (still solid against rocks).
        var dashTarget = Position + new Vector3(_pounceDirection.X, 0, _pounceDirection.Y) * (PounceSpeed * deltaTime + 0.01f);
        _mover.MoveTowards(dashTarget, PounceSpeed, deltaTime, world, static (_, _) => false);
        _mover.Heading = _pounceDirection;

        // Anything it touches mid-pounce is caught — except a Bramblekin
        // that's Fighting it, or backing away from the fight on guard (see
        // Bramblekin.IsBraced): it's braced for the spider, so it doesn't block
        // or interrupt the pounce, and the Strike/Bite exchange handles that
        // fight instead. Reverse for-loop: World.Kill only queues the
        // removal, so Colony never actually changes size during this walk,
        // but the pattern stays consistent everywhere.
        Bramblekin? caught = null;
        for (int i = world.Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = world.Colony[i];
            if (bramblekin.IsDead || bramblekin.IsBraced || bramblekin.IsSheltered)
                continue;

            if (GroundMover.HorizontalDistance(Position, bramblekin.Position) >= BodyRadius + Bramblekin.BodyRadius)
                continue;

            caught = bramblekin;
            break;
        }

        if (caught is not null)
        {
            world.Kill(caught, DeathCause.Predator, this);
            Kills++;
            _prey = null;
            _timer = FeedDuration;
            SetState(SpiderState.Feeding);
            return;
        }

        _timer -= deltaTime;
        if (_timer <= 0f)
        {
            // Dash ended without catching anyone: drop the stale target
            // reference rather than leave it dangling through Recovering.
            _prey = null;
            _timer = RecoverDuration;
            SetState(SpiderState.Recovering);
        }
    }

    private void Investigate(float deltaTime, World world)
    {
        if (!_staring)
        {
            _timer += deltaTime;
            bool closeEnough = GroundMover.HorizontalDistance(Position, _target) <= InvestigateStandOff;
            if (closeEnough || _timer > InvestigateTravelTimeout)
            {
                _staring = true;
                _timer = StareDuration;
            }
            else
            {
                _mover.MoveTowards(_target, HuntSpeed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
            }
            return;
        }

        // Stare: face the impact point and don't move.
        var toImpact = new Vector2(_target.X - Position.X, _target.Z - Position.Z);
        if (toImpact.LengthSquared() > 1e-6f)
            _mover.Heading = Vector2.Normalize(toImpact);

        _timer -= deltaTime;
        if (_timer <= 0f)
            StartProwling();
    }

    // --- Transitions ----------------------------------------------------------------

    private void StartProwling()
    {
        _prey = null;
        _timer = ProwlPauseDuration;
        SetState(SpiderState.Prowling);
    }

    private void StartHunting(Bramblekin prey)
    {
        _prey = prey;
        _sinceVibration = 0f;
        SetState(SpiderState.Hunting);
    }

    private void StartInvestigating(Vector3 impactPoint)
    {
        _prey = null;
        _target = impactPoint;
        _staring = false;
        _timer = 0f;
        SetState(SpiderState.Investigating);
    }

    private void SetState(SpiderState state)
    {
        State = state;
        _mover.ResetProgress();
    }

    /// <summary>The nearest vibrating Bramblekin within <see cref="VibrationRadius"/>, if any.</summary>
    private Bramblekin? FindPrey(World world)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = VibrationRadius * VibrationRadius;
        // The Spatial Grid: only the Colony chunks around this spider — out
        // far enough to count the company around the furthest possible prey.
        List<Bramblekin> nearby = world.QueryNearbyColony(Position, VibrationRadius + CrowdRadius);
        for (int i = nearby.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = nearby[i];
            // IsVibrating is already false for a dead Bramblekin; checked
            // again explicitly so this never targets one even if that changes.
            if (bramblekin.IsDead || !bramblekin.IsVibrating || bramblekin.IsSheltered)
                continue;
            if (world.IsInsidePalisade(bramblekin.Position))
                continue; // Stakes it won't go past.

            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, bramblekin.Position);
            if (distanceSquared <= bestDistanceSquared && !IsInACrowd(bramblekin, nearby))
            {
                best = bramblekin;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>
    /// Safety in numbers: like any predator it singles out the isolated —
    /// prey with <see cref="CrowdSize"/> or more others within
    /// <see cref="CrowdRadius"/> isn't worth the risk. <paramref name="nearby"/>
    /// is the Colony around the spider, which already covers the whole crowd
    /// radius around anything close enough to be prey.
    /// </summary>
    private static bool IsInACrowd(Bramblekin prey, List<Bramblekin> nearby)
    {
        int company = 0;
        foreach (Bramblekin other in nearby)
        {
            if (other != prey && !other.IsDead &&
                GroundMover.HorizontalDistanceSquared(other.Position, prey.Position) <= CrowdRadius * CrowdRadius &&
                ++company >= CrowdSize)
                return true;
        }
        return false;
    }

    /// <summary>The nearest living, Fighting Bramblekin within <paramref name="range"/>, if any — the Bite's target.</summary>
    private Bramblekin? NearestFighterInRange(World world, float range)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = range * range;
        // The Spatial Grid: only the Colony chunks around this spider.
        List<Bramblekin> nearby = world.QueryNearbyColony(Position, range);
        for (int i = nearby.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = nearby[i];
            if (bramblekin.IsDead || bramblekin.State != BramblekinState.Fighting)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, bramblekin.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = bramblekin;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    // --- Drawing ----------------------------------------------------------------------

    /// <summary>
    /// A squat two-part body (big abdomen behind, smaller head in front) with
    /// eight jointed legs, all drawn in the spider's local frame: +X forward,
    /// +Z to its right. Eye colour shows its mood: dim when prowling, red when
    /// hunting, yellow when investigating.
    /// </summary>
    public void Draw()
    {
        float yawDegrees = -MathF.Atan2(_mover.Heading.Y, _mover.Heading.X) * 180f / MathF.PI;

        Rlgl.PushMatrix();
        Rlgl.Translatef(Position.X, Position.Y, Position.Z);
        Rlgl.Rotatef(yawDegrees, 0, 1, 0);

        // Tumbled: roll onto its side (tips over in the first moment, then a
        // small dazed wobble) rather than standing upright.
        if (State == SpiderState.Tumbled)
        {
            float elapsed = TumbledDuration - _timer;
            float fallIn = MathF.Min(elapsed / 0.3f, 1f);
            float wobble = MathF.Sin(elapsed * 6f) * 6f;
            Rlgl.Rotatef(fallIn * 80f + wobble, 1, 0, 0);
        }

        // The model (its head faces −X, so turned about), tinted by mood — dim when
        // prowling, red when hunting, yellow when investigating, pale when tumbled —
        // and bobbing as its legs work while it moves.
        Color mood = State switch
        {
            SpiderState.Hunting or SpiderState.Pouncing => new Color(255, 170, 160, 255),
            SpiderState.Investigating => new Color(255, 240, 170, 255),
            SpiderState.Tumbled => new Color(200, 210, 235, 255),
            _ => Color.White,
        };
        float bob = _mover.IsMoving ? MathF.Abs(MathF.Sin(_walkCycle)) * 0.04f : 0f;
        PropModels.Draw(PropModels.Spider, new Vector3(0f, bob, 0f), 180f, ModelScale, mood);

        Rlgl.PopMatrix();

        // While staring at an impact, a faint line shows what it is looking at.
        if (State == SpiderState.Investigating && _staring)
            Raylib.DrawLine3D(Position + new Vector3(0, 0.4f, 0), _target + new Vector3(0, 0.05f, 0), new Color(240, 210, 60, 160));
    }
}
