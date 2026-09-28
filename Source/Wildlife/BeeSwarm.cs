using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Bees roused from the hive in the oak (see World.Beehive): a small angry
/// cloud that chases whoever took their honey, stinging, until it gets
/// indoors, the bees tire of it, or they're swatted down one by one. Then
/// what's left of the swarm goes home.
/// </summary>
public sealed class BeeSwarm : ICombatant
{
    /// <summary>A roused swarm has this many bees.</summary>
    public const int SwarmSize = 5;

    /// <summary>Each sting takes this much Health.</summary>
    public const int StingDamage = 2;

    private const float Speed = 3.2f;
    private const float StingRange = 0.5f;
    private const float StingInterval = 1.1f;

    /// <summary>They give up the chase after this long (s).</summary>
    private const float ChaseSeconds = 14f;

    private static readonly Color BeeColor = new(235, 185, 40, 255);
    private static readonly Color BandColor = new(40, 30, 20, 255);

    private readonly Vector3 _hive;
    private Vector3 _position;
    private Bramblekin? _target;
    private float _chase = ChaseSeconds;
    private float _sting;
    private bool _goingHome;

    public BeeSwarm(Vector3 hive, Bramblekin target)
    {
        _hive = hive;
        _position = hive;
        _target = target;
    }

    public Vector3 Position => _position;

    public int Bees { get; private set; } = SwarmSize;

    /// <summary>Gone: all swatted, or back in the hive.</summary>
    public bool IsDead => Bees <= 0 || _goingHome;

    public float CollisionRadius => 0.35f;

    /// <summary>A swat: one bee fewer.</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        if (IsDead)
            return;
        Bees--;
        world.NoteBeeSwatted();
    }

    /// <summary>Chases and stings; false once it's back in the hive (or swatted to nothing).</summary>
    public bool Update(float deltaTime, World world)
    {
        if (Bees <= 0)
            return false;
        _chase -= deltaTime;
        if (_target is not { IsDead: false } target || target.IsSheltered || _chase <= 0f)
            _goingHome = true;

        if (_goingHome)
        {
            Vector3 toHive = _hive - _position;
            if (toHive.Length() < 0.3f)
                return false;
            _position += Vector3.Normalize(toHive) * MathF.Min(toHive.Length(), Speed * deltaTime);
            return true;
        }

        Vector3 aim = _target!.Position + new Vector3(0f, 0.5f, 0f);
        Vector3 to = aim - _position;
        float distance = to.Length();
        if (distance > 1e-3f)
            _position += to / distance * MathF.Min(distance, Speed * deltaTime);
        _sting -= deltaTime;
        if (distance <= StingRange + Bramblekin.BodyRadius && _sting <= 0f)
        {
            _sting = StingInterval * SwarmSize / Math.Max(1, Bees);
            world.NoteBeeSting();
            _target.TakeDamage(StingDamage, world, DeathCause.Predator, this);
        }
        return true;
    }

    /// <summary>A little cloud of striped bees, buzzing about its middle.</summary>
    public void Draw(float time)
    {
        for (int i = 0; i < Bees; i++)
        {
            float a = time * 9f + i * 1.9f;
            var at = _position + new Vector3(MathF.Cos(a) * 0.25f, MathF.Sin(a * 1.3f) * 0.12f, MathF.Sin(a) * 0.25f);
            Detail.Sphere(at, 0.06f, BeeColor);
            Detail.Sphere(at + new Vector3(0f, 0.01f, 0f), 0.035f, BandColor);
        }
    }
}
