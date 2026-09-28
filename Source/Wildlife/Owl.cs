using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The Owl: a night hunter that roosts in the Giant Oak. On some nights it
/// glides out over the garden, circling high and silent, and drops on a
/// Bramblekin caught in the open — a sleeper most of all (see
/// Bramblekin.Night). Nobody indoors is in reach, nor anyone in the light
/// of a lit hearth. After a strike it mantles over its catch on the ground
/// for a moment, and that's the only time it can be fought: a band of kin
/// that hurts it badly enough drives it off for the night; brought down,
/// it's a meal of meat. It goes back to the oak at dawn (see World.DayNight).
/// </summary>
public sealed class Owl : ICombatant
{
    public const float BodyRadius = 0.3f;

    public const int MaxHealth = 30;

    /// <summary>Its talons on a Bramblekin that's awake: over a third of its Health…</summary>
    public const int TalonDamage = 12;

    /// <summary>…and on one fast asleep, most of it.</summary>
    public const int SleeperTalonDamage = 24;

    /// <summary>Meat it drops if it's brought down.</summary>
    public const int MeatYield = 4;

    /// <summary>Nobody within this far (m) of a lit hearth is ever its prey.</summary>
    public const float HearthLightRadius = 6f;

    /// <summary>On the ground over its catch, it's a threat to anyone within this far (m).</summary>
    public const float ThreatRadius = 5f;

    /// <summary>It spots prey within this far (m) of the point below it.</summary>
    private const float SightRadius = 20f;

    private const float CircleHeight = 8f;
    private const float CircleRadius = 9f;

    /// <summary>Radians a second round its circle.</summary>
    private const float CircleSpeed = 0.45f;

    private const float FlightSeconds = 4f;
    private const float SwoopSeconds = 1.5f;
    private const float GrabSeconds = 1.4f;
    private const float ClimbSeconds = 2f;

    /// <summary>Seconds between one strike and the next search.</summary>
    private const float StrikeCooldown = 10f;

    /// <summary>It gives up for the night after this many strikes.</summary>
    private const int StrikesPerNight = 3;

    /// <summary>Hurt down to this much Health, it's had enough and flies back to the oak.</summary>
    private const int FleeHealth = MaxHealth / 2;

    /// <summary>It only strikes when its prey is still within this far (m) as it comes down.</summary>
    private const float StrikeReach = 1.2f;

    private static readonly Color PlumageColor = new(120, 95, 70, 255);
    private static readonly Color BreastColor = new(200, 180, 150, 255);
    private static readonly Color FaceColor = new(225, 210, 185, 255);
    private static readonly Color EyeColor = new(255, 200, 40, 255);
    private static readonly Color BeakColor = new(60, 50, 40, 255);

    private enum Phase
    {
        Arriving,
        Circling,
        Swooping,
        Grabbing,
        Climbing,
        Leaving,
    }

    private readonly Random _rng;
    private readonly Vector3 _roost;
    private Phase _phase = Phase.Arriving;
    private Vector3 _center;
    private float _angle;
    private float _timer;
    private float _cooldown = StrikeCooldown * 0.5f;
    private Vector3 _from;
    private Vector3 _grabAt;
    private Bramblekin? _prey;
    private int _strikes;
    private bool _slain;

    public Owl(Vector3 roost, Vector3 center, Random rng)
    {
        _rng = rng;
        _roost = roost;
        _center = center;
        _angle = (float)(rng.NextDouble() * MathF.Tau);
        _from = roost;
    }

    public Vector3 Position
    {
        get
        {
            float t = Math.Clamp(_timer, 0f, 1f);
            return _phase switch
            {
                Phase.Arriving => Vector3.Lerp(_from, CirclePoint, Ease(t)),
                Phase.Circling => CirclePoint,
                Phase.Swooping => Vector3.Lerp(_from, SwoopGoal, t * t),
                Phase.Grabbing => _grabAt,
                Phase.Climbing => Vector3.Lerp(_grabAt, CirclePoint, Ease(t)),
                _ => Vector3.Lerp(_from, _roost, Ease(t)),
            };
        }
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);

    private Vector3 CirclePoint =>
        _center + new Vector3(MathF.Cos(_angle) * CircleRadius, CircleHeight, MathF.Sin(_angle) * CircleRadius);

    /// <summary>Where a swoop comes down: on its prey, wherever it has got to.</summary>
    private Vector3 SwoopGoal => _prey is { IsDead: false } prey ? prey.Position + new Vector3(0f, 0.3f, 0f) : _grabAt;

    /// <summary>Not there to fight: brought down, or on the wing.</summary>
    public bool IsDead => _slain || _phase != Phase.Grabbing;

    /// <summary>Down on the ground over its catch — the only time it can be struck.</summary>
    public bool IsLanded => !_slain && _phase == Phase.Grabbing;

    public bool IsSlain => _slain;

    /// <summary>Going back to the oak (or already there).</summary>
    public bool IsLeaving => _phase == Phase.Leaving;

    public int Health { get; private set; } = MaxHealth;

    public float CollisionRadius => BodyRadius;

    public void MarkSlain() => _slain = true;

    private Bramblekin? _driver;

    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        if (!IsLanded)
            return;
        Health = Math.Max(0, Health - damage);
        if (Health <= 0)
            world.KillOwl(this, attacker);
        else if (Health <= FleeHealth)
        {
            _driver = attacker;
            _timer = GrabSeconds; // Lets go and takes off at once.
        }
    }

    /// <summary>Glides in, circles, swoops, goes home at dawn. Returns false once it's back on its roost.</summary>
    public bool Update(float deltaTime, World world)
    {
        if (_slain)
            return false;

        switch (_phase)
        {
            case Phase.Arriving:
                _timer += deltaTime / FlightSeconds;
                if (_timer >= 1f)
                    Enter(Phase.Circling);
                return true;

            case Phase.Leaving:
                _timer += deltaTime / FlightSeconds;
                return _timer < 1f;

            case Phase.Circling:
                if (!world.IsNight || _strikes >= StrikesPerNight)
                {
                    Leave(world, driver: null);
                    return true;
                }
                _angle += CircleSpeed * deltaTime;
                _cooldown -= deltaTime;
                if (_cooldown <= 0f)
                {
                    _cooldown = 2f; // Nothing found: look again in a moment…
                    if (FindPrey(world) is { } prey)
                    {
                        _prey = prey;
                        _grabAt = prey.Position;
                        Enter(Phase.Swooping);
                    }
                    else if (_rng.NextDouble() < 0.3)
                    {
                        _center = world.OwlHuntingGround(_center); // …or drift on to another part of the garden.
                    }
                }
                return true;

            case Phase.Swooping:
                _timer += deltaTime / SwoopSeconds;
                if (_prey is { IsDead: false } target)
                    _grabAt = World.Grounded(target.Position);
                if (_timer < 1f)
                    return true;
                Strike(world);
                return true;

            case Phase.Grabbing:
                _timer += deltaTime;
                if (_timer < GrabSeconds)
                    return true;
                if (_driver is not null || Health <= FleeHealth)
                {
                    Leave(world, _driver);
                    return true;
                }
                Enter(Phase.Climbing);
                return true;

            case Phase.Climbing:
                _timer += deltaTime / ClimbSeconds;
                if (_timer >= 1f)
                    Enter(Phase.Circling);
                return true;
        }
        return true;
    }

    /// <summary>Talons down on its prey — if it's still where the owl is coming down, and still out in the open.</summary>
    private void Strike(World world)
    {
        _strikes++;
        _cooldown = StrikeCooldown;
        Enter(Phase.Grabbing);
        if (_prey is not { IsDead: false } prey || prey.IsSheltered ||
            GroundMover.HorizontalDistance(prey.Position, _grabAt) > StrikeReach)
        {
            _prey = null;
            return;
        }
        world.NoteOwlStrike(this, prey);
        prey.TakeDamage(prey.IsAsleep ? SleeperTalonDamage : TalonDamage, world, DeathCause.Predator, this);
        _prey = null;
    }

    /// <summary>
    /// The Bramblekin it would drop on: out in the open (not indoors, not in
    /// a hearth's light) within sight of the point below it — a sleeper
    /// or a youngster before anyone else, then the nearest.
    /// </summary>
    private Bramblekin? FindPrey(World world)
    {
        Vector3 below = World.Grounded(Position);
        Bramblekin? best = null;
        float bestScore = float.MaxValue;
        foreach (Bramblekin kin in world.QueryColonyWithin(below, SightRadius))
        {
            if (kin.IsDead || kin.IsSheltered || kin.IsInsideHome)
                continue;
            float distance = GroundMover.HorizontalDistance(below, kin.Position);
            if (distance > SightRadius || world.IsInHearthLight(kin.Position))
                continue;
            float score = distance - (kin.IsAsleep ? 10f : 0f) - (kin.IsYoung ? 4f : 0f);
            if (score < bestScore)
            {
                best = kin;
                bestScore = score;
            }
        }
        return best;
    }

    private void Enter(Phase phase)
    {
        if (phase == Phase.Swooping)
            _from = Position;
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>Back to its roost in the oak — driven off by <paramref name="driver"/>, or because the night is over.</summary>
    private void Leave(World world, Bramblekin? driver)
    {
        _from = Position;
        _phase = Phase.Leaving;
        _timer = 0f;
        world.NoteOwlLeft(this, driver);
    }

    /// <summary>
    /// A round brown owl: a pale speckled breast, a flat pale face with two
    /// great yellow eyes (see <see cref="DrawEyes"/> for their glow), broad
    /// wings spread and slowly beating in flight, folded and mantled over
    /// its catch on the ground.
    /// </summary>
    public void Draw()
    {
        Vector3 at = Position;
        Vector3 ahead = Heading();
        var side = new Vector3(-ahead.Z, 0f, ahead.X);
        bool flying = _phase != Phase.Grabbing;

        Detail.Sphere(at, 0.3f, PlumageColor);
        Detail.Sphere(at + ahead * 0.12f - new Vector3(0f, 0.05f, 0f), 0.22f, BreastColor);
        Vector3 head = at + new Vector3(0f, 0.3f, 0f) + ahead * 0.05f;
        Detail.Sphere(head, 0.2f, PlumageColor);
        Detail.Sphere(head + ahead * 0.1f, 0.14f, FaceColor);
        Raylib.DrawCylinderEx(head + ahead * 0.2f, head + ahead * 0.26f - new Vector3(0f, 0.05f, 0f), 0.03f, 0.005f, 4, BeakColor);
        foreach (float s in stackalloc[] { -1f, 1f })
            Raylib.DrawCylinderEx(head + side * 0.1f * s + new Vector3(0f, 0.12f, 0f), head + side * 0.13f * s + new Vector3(0f, 0.22f, 0f), 0.04f, 0.005f, 4, PlumageColor);

        float beat = flying ? MathF.Sin(_timer * 9f + _angle * 6f) * 0.2f : 0.15f;
        float span = flying ? 0.95f : 0.4f;
        foreach (float s in stackalloc[] { -1f, 1f })
        {
            Vector3 root = at + side * 0.2f * s + new Vector3(0f, 0.08f, 0f);
            Vector3 tip = at + side * span * s + new Vector3(0f, 0.08f + beat, 0f) - ahead * 0.15f;
            Raylib.DrawCylinderEx(root, tip, 0.16f, 0.05f, 4, PlumageColor);
        }
    }

    /// <summary>Its two eyes, drawn again in the night's light pass so they shine in the dark.</summary>
    public void DrawEyes(float darkness)
    {
        Vector3 ahead = Heading();
        var side = new Vector3(-ahead.Z, 0f, ahead.X);
        Vector3 head = Position + new Vector3(0f, 0.32f, 0f) + ahead * 0.2f;
        var glow = EyeColor with { A = (byte)(200 * darkness) };
        foreach (float s in stackalloc[] { -1f, 1f })
            Raylib.DrawSphereEx(head + side * 0.06f * s, 0.035f, 4, 6, glow);
    }

    /// <summary>Which way it's going: along its circle, down its swoop, or at its catch.</summary>
    private Vector3 Heading()
    {
        Vector3 direction = _phase switch
        {
            Phase.Circling or Phase.Arriving or Phase.Climbing => new Vector3(-MathF.Sin(_angle), 0f, MathF.Cos(_angle)),
            Phase.Swooping => SwoopGoal - _from,
            Phase.Leaving => _roost - _from,
            _ => _prey is { } prey ? prey.Position - _grabAt : new Vector3(MathF.Cos(_angle), 0f, MathF.Sin(_angle)),
        };
        direction.Y = 0f;
        return direction.LengthSquared() > 1e-6f ? Vector3.Normalize(direction) : Vector3.UnitX;
    }
}
