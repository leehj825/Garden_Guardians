using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The Heron: a great grey bird that comes down to the pond now and then
/// from spring to autumn (see World.PondLife) and stalks the shallows for
/// frogs — and for any Bramblekin at the water's edge. Standing stock
/// still it's easy to miss (see Bramblekin.Perceive) until it lunges with
/// its dagger of a beak; wading, it's slow, so a Bramblekin that sees it
/// coming simply walks away. It never leaves the water's edge. Hurt badly
/// enough — by a band of kin with spears, or a volley from slings — it
/// flies off; brought down, it's a feast of meat. It leaves of its own
/// accord after a while, or when winter or a drought comes.
/// </summary>
public sealed class Heron : ICombatant
{
    public const float BodyRadius = 0.35f;

    public const int MaxHealth = 50;

    /// <summary>A lunge of its beak: a third of a Bramblekin's Health.</summary>
    public const int StabDamage = 10;

    /// <summary>Wading, it's a threat to any Bramblekin within this far (m) that sees it…</summary>
    public const float ThreatRadius = 5f;

    /// <summary>…standing still, only once it's this close.</summary>
    public const float StillSightRadius = 2.2f;

    /// <summary>Meat it drops if it's brought down.</summary>
    public const int MeatYield = 6;

    /// <summary>It lunges at anything this close (m) — edge to its middle.</summary>
    private const float LungeReach = 1.7f;

    private const float StabCooldown = 2.2f;

    /// <summary>It spears a frog this close (m).</summary>
    private const float FrogReach = 0.9f;

    /// <summary>It goes after frogs within this far (m)…</summary>
    private const float FrogSight = 4f;

    private const float WadeSpeed = 0.45f;

    /// <summary>…and Bramblekin at the water's edge within <see cref="ThreatRadius"/>, at this pace — slower than one walking.</summary>
    private const float StalkSpeed = 0.9f;

    /// <summary>It never goes further than this (m) from the water.</summary>
    private const float ShoreLeash = 2.5f;

    /// <summary>Seconds spent flying in, or away.</summary>
    private const float FlightSeconds = 4f;

    private static readonly Color PlumageColor = new(150, 156, 166, 255);
    private static readonly Color WingColor = new(105, 112, 124, 255);
    private static readonly Color NeckColor = new(215, 215, 220, 255);
    private static readonly Color CrestColor = new(25, 25, 30, 255);
    private static readonly Color BeakColor = new(220, 180, 60, 255);
    private static readonly Color LegColor = new(150, 135, 90, 255);

    private enum Phase
    {
        Arriving,
        Stalking,
        Leaving,
    }

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private Phase _phase = Phase.Arriving;
    private Vector3 _sky, _ground;
    private float _flightTime;
    private float _stay;
    private float _stabCooldown;
    private float _lunge;
    private float _standTimer;
    private Vector3? _wadeTo;
    private bool _slain;

    public Heron(Vector3 landing, float staySeconds, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(landing, BodyRadius, 1f, rng, walks: false);
        _ground = landing;
        float angle = (float)(rng.NextDouble() * MathF.Tau);
        _sky = landing + new Vector3(MathF.Cos(angle) * 30f, 14f, MathF.Sin(angle) * 30f);
        _mover.Heading = Vector2.Normalize(new Vector2(landing.X - _sky.X, landing.Z - _sky.Z));
        _stay = staySeconds;
        _standTimer = 3f;
    }

    public Vector3 Position
    {
        get
        {
            if (_phase == Phase.Stalking)
                return World.Grounded(_mover.Position);
            float t = Math.Clamp(_flightTime / FlightSeconds, 0f, 1f);
            float along = _phase == Phase.Arriving ? 1f - (1f - t) * (1f - t) : t * t;
            return _phase == Phase.Arriving ? Vector3.Lerp(_sky, _ground, along) : Vector3.Lerp(_ground, _sky, along);
        }
    }

    /// <summary>Not there to fight: brought down, or on the wing.</summary>
    public bool IsDead => _slain || _phase != Phase.Stalking;

    /// <summary>Brought down (see <see cref="World.KillHeron"/>).</summary>
    public bool IsSlain => _slain;

    /// <summary>Down at the pond, hunting.</summary>
    public bool IsLanded => !_slain && _phase == Phase.Stalking;

    /// <summary>Standing stock still in the shallows — easy to miss.</summary>
    public bool IsStill => IsLanded && !_mover.IsMoving;

    public int Health { get; private set; } = MaxHealth;

    public float CollisionRadius => BodyRadius;

    public void MarkSlain() => _slain = true;

    /// <summary>Hurt down to this much Health, it's had enough and flies off.</summary>
    private const int FleeHealth = MaxHealth * 2 / 5;

    /// <summary>A blow or a pebble: brought down at 0 Health (see <see cref="World.KillHeron"/>); hurt to <see cref="FleeHealth"/>, it's had enough and flies off.</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        if (!IsLanded)
            return;
        Health = Math.Max(0, Health - damage);
        if (Health <= 0)
            world.KillHeron(this, attacker);
        else if (Health <= FleeHealth)
            Leave(world, attacker);
    }

    /// <summary>Flies in, stalks the shallows, flies off. Returns false once it has gone.</summary>
    public bool Update(float deltaTime, World world)
    {
        if (_slain)
            return false;
        _mover.Idle();

        if (_phase != Phase.Stalking)
        {
            _flightTime += deltaTime;
            if (_flightTime < FlightSeconds)
                return true;
            if (_phase == Phase.Leaving)
                return false;
            _phase = Phase.Stalking;
            return true;
        }

        _stay -= deltaTime;
        _stabCooldown -= deltaTime;
        _lunge = MathF.Max(0f, _lunge - deltaTime);
        if (_stay <= 0f || world.CurrentSeason == Season.Winter || WaterMap.Fullness < World.HeronLeavesBelowFullness)
        {
            Leave(world, driver: null);
            return true;
        }

        // A Bramblekin in reach gets the beak.
        Bramblekin? kin = NearestKinAtTheEdge(world);
        if (kin is not null && GroundMover.HorizontalDistance(Position, kin.Position) <= LungeReach + Bramblekin.BodyRadius)
        {
            Face(kin.Position);
            if (_stabCooldown <= 0f)
            {
                _stabCooldown = StabCooldown;
                _lunge = 0.4f;
                world.NoteHeronStab();
                kin.TakeDamage(StabDamage, world, DeathCause.Predator, this);
            }
            return true;
        }

        Frog? frog = world.NearestVisibleFrog(Position, FrogSight);
        if (frog is not null && GroundMover.HorizontalDistance(Position, frog.Position) <= FrogReach)
        {
            Face(frog.Position);
            if (_stabCooldown <= 0f)
            {
                _stabCooldown = StabCooldown;
                _lunge = 0.4f;
                world.HeronTakesFrog(frog);
            }
            return true;
        }

        // Stalk: a Bramblekin at the water's edge, else a frog.
        if (kin is not null || frog is not null)
        {
            _mover.MoveTowards(kin?.Position ?? frog!.Position, StalkSpeed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
            _standTimer = 1f + 2f * (float)_rng.NextDouble();
            return true;
        }

        // Nothing in sight: stand stock still a while, then wade along to another stretch of shore.
        if (_standTimer > 0f)
        {
            _standTimer -= deltaTime;
            return true;
        }
        _wadeTo ??= world.RandomShoreSpot(Position, 10f) is { } shore && !world.IsNearHome(shore, 2f) ? shore : null;
        if (_wadeTo is not { } wade || _mover.MoveTowards(wade, WadeSpeed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)))
        {
            _wadeTo = null;
            _standTimer = 3f + 5f * (float)_rng.NextDouble();
        }
        return true;
    }

    /// <summary>The nearest Bramblekin in the open, at the water's edge, within <see cref="ThreatRadius"/> — not one by its door (the Heron keeps clear of homes).</summary>
    private Bramblekin? NearestKinAtTheEdge(World world)
    {
        Bramblekin? best = null;
        float bestDistance = ThreatRadius * ThreatRadius;
        foreach (Bramblekin kin in world.QueryColonyWithin(Position, ThreatRadius))
        {
            if (kin.IsDead || kin.IsSheltered || WaterMap.DistanceToWater(kin.Position.X, kin.Position.Z) > ShoreLeash || world.IsNearHome(kin.Position, 1f))
                continue;
            float distance = GroundMover.HorizontalDistanceSquared(Position, kin.Position);
            if (distance < bestDistance)
            {
                best = kin;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void Face(Vector3 at)
    {
        var direction = new Vector2(at.X - Position.X, at.Z - Position.Z);
        if (direction.LengthSquared() > 1e-4f)
            _mover.Heading = Vector2.Normalize(direction);
    }

    /// <summary>Takes off and flies away — driven off by <paramref name="driver"/>, or just done here.</summary>
    private void Leave(World world, Bramblekin? driver)
    {
        if (_phase != Phase.Stalking)
            return;
        _ground = Position;
        float angle = (float)(_rng.NextDouble() * MathF.Tau);
        _sky = _ground + new Vector3(MathF.Cos(angle) * 30f, 14f, MathF.Sin(angle) * 30f);
        _mover.Heading = Vector2.Normalize(new Vector2(_sky.X - _ground.X, _sky.Z - _ground.Z));
        _phase = Phase.Leaving;
        _flightTime = 0f;
        world.NoteHeronLeft(this, driver);
    }

    /// <summary>
    /// A tall grey heron: long yellow legs, a grey body with darker wings, a
    /// white S-curved neck, a black crest and a yellow dagger of a beak —
    /// thrust out when it lunges. On the wing, broad grey wings beating slowly.
    /// </summary>
    public void Draw()
    {
        Vector3 at = Position;
        Vector2 facing = _mover.Heading.LengthSquared() > 1e-6f ? Vector2.Normalize(_mover.Heading) : Vector2.UnitX;
        var ahead = new Vector3(facing.X, 0f, facing.Y);
        var side = new Vector3(-facing.Y, 0f, facing.X);
        Vector3 Local(float forward, float up, float across) => at + ahead * forward + new Vector3(0f, up, 0f) + side * across;
        bool flying = _phase != Phase.Stalking;

        // Legs: standing, straight down; flying, trailing behind.
        float bodyHeight = flying ? 0f : 0.95f;
        foreach (float s in stackalloc[] { -0.07f, 0.07f })
        {
            Vector3 hip = Local(0f, bodyHeight, s);
            Vector3 foot = flying ? Local(-0.8f, bodyHeight - 0.1f, s) : Local(0.02f, 0f, s);
            Raylib.DrawCylinderEx(hip, foot, 0.025f, 0.02f, 4, LegColor);
        }

        // Body: a long grey egg, darker wings folded along its back.
        float yaw = -MathF.Atan2(facing.Y, facing.X) * 180f / MathF.PI;
        Vector3 body = Local(0f, bodyHeight + 0.12f, 0f);
        Rlgl.PushMatrix();
        Rlgl.Translatef(body.X, body.Y, body.Z);
        Rlgl.Rotatef(yaw, 0f, 1f, 0f);
        Rlgl.Rotatef(flying ? 0f : 18f, 0f, 0f, 1f);
        Rlgl.Scalef(1.6f, 0.85f, 0.9f);
        Raylib.DrawSphereEx(Vector3.Zero, 0.25f, 6, 10, PlumageColor);
        if (!flying)
            Raylib.DrawSphereEx(new Vector3(-0.04f, 0.06f, 0f), 0.24f, 6, 10, WingColor);
        Rlgl.PopMatrix();

        if (flying)
        {
            // Broad wings, beating slowly.
            float beat = MathF.Sin(_flightTime * 5f) * 0.35f;
            foreach (float s in stackalloc[] { -1f, 1f })
            {
                Vector3 root = Local(0f, bodyHeight + 0.15f, 0.12f * s);
                Vector3 tip = Local(-0.1f, bodyHeight + 0.15f + beat, 0.95f * s);
                Raylib.DrawCylinderEx(root, tip, 0.14f, 0.06f, 4, WingColor);
            }
        }

        // Neck: an S-curve up to the head — thrust forward and down in a lunge.
        float thrust = _lunge > 0f ? MathF.Sin(_lunge / 0.4f * MathF.PI) : 0f;
        Vector3 shoulder = Local(0.3f, bodyHeight + 0.22f, 0f);
        Vector3 crook = flying ? Local(0.45f, bodyHeight + 0.2f, 0f) : Local(0.24f + 0.35f * thrust, bodyHeight + 0.45f - 0.2f * thrust, 0f);
        Vector3 head = flying ? Local(0.55f, bodyHeight + 0.25f, 0f) : Local(0.36f + 0.75f * thrust, bodyHeight + 0.62f - 0.45f * thrust, 0f);
        Raylib.DrawCylinderEx(shoulder, crook, 0.07f, 0.05f, 5, NeckColor);
        Raylib.DrawCylinderEx(crook, head, 0.05f, 0.045f, 5, NeckColor);
        Raylib.DrawSphereEx(head, 0.075f, 5, 6, NeckColor);

        // A black crest streaming back, and the long yellow beak.
        Raylib.DrawCylinderEx(head + new Vector3(0f, 0.04f, 0f), head - ahead * 0.2f + new Vector3(0f, 0.02f, 0f), 0.02f, 0.005f, 4, CrestColor);
        Vector3 beakDown = new(0f, -0.06f * thrust, 0f);
        Raylib.DrawCylinderEx(head + ahead * 0.05f, head + ahead * 0.3f + beakDown, 0.035f, 0.004f, 5, BeakColor);
        Raylib.DrawSphereEx(head + ahead * 0.03f + side * 0.04f + new Vector3(0f, 0.02f, 0f), 0.015f, 3, 4, CrestColor);
        Raylib.DrawSphereEx(head + ahead * 0.03f - side * 0.04f + new Vector3(0f, 0.02f, 0f), 0.015f, 3, 4, CrestColor);
    }
}
