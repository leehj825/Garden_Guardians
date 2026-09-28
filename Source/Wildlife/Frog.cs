using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A frog on the bank of the pond — prey for anyone quick (or with a sling)
/// enough, and food for the Heron. Frogs turn up on the shore from spring
/// to autumn, fewer as a drought shrinks the pond (see World.PondLife), and
/// sit, and now and then hop along the water's edge. Anything that comes
/// too close may startle one (it doesn't always notice in time): it leaps
/// into the water and stays under a while — out of reach — before hopping
/// back out onto the bank. Come winter they go down into the mud.
/// Killed, it drops <see cref="MeatYield"/> pieces of meat.
/// </summary>
public sealed class Frog : ICombatant
{
    public const float BodyRadius = 0.14f;

    public const int MaxHealth = 6;

    public const int MeatYield = 2;

    /// <summary>Anything this close (m) may startle it into the water…</summary>
    private const float AlertRadius = 1.8f;

    /// <summary>…it looks up every this many seconds…</summary>
    private const float AlertCheckInterval = 0.5f;

    /// <summary>…and notices a Bramblekin with these odds (the Heron, always).</summary>
    private const float AlertChance = 0.4f;

    private const float HopSeconds = 0.35f;
    private const float HopHeight = 0.3f;

    /// <summary>A frog further than this (m) from the water makes its way back to it.</summary>
    private const float StrayReach = 3f;

    private static readonly Color SkinColor = new(90, 140, 60, 255);
    private static readonly Color BellyColor = new(170, 180, 110, 255);
    private static readonly Color SpotColor = new(50, 85, 35, 255);
    private static readonly Color EyeColor = new(20, 20, 15, 255);

    private readonly Random _rng;

    /// <summary>Where it sits (on the bank, or under the water).</summary>
    private Vector3 _at;

    private Vector3 _hopFrom, _hopTo;

    /// <summary>Seconds into its hop; below 0 when it isn't hopping.</summary>
    private float _hopTime = -1f;

    /// <summary>This hop ends in the water.</summary>
    private bool _hopIntoWater;

    private float _sitTimer, _alertTimer, _underTimer;
    private Vector2 _facing = Vector2.UnitX;

    public Frog(Vector3 position, Random rng)
    {
        _rng = rng;
        _at = position;
        _sitTimer = 2f + 4f * (float)rng.NextDouble();
    }

    public Vector3 Position
    {
        get
        {
            if (_hopTime < 0f)
                return Rest(_at);
            float t = Math.Clamp(_hopTime / HopSeconds, 0f, 1f);
            return Vector3.Lerp(Rest(_hopFrom), Rest(_hopTo), t) + new Vector3(0f, HopHeight * 4f * t * (1f - t), 0f);
        }
    }

    public bool IsDead { get; private set; }

    public int Health { get; private set; } = MaxHealth;

    public float CollisionRadius => BodyRadius;

    /// <summary>Under the water.</summary>
    public bool IsSubmerged { get; private set; }

    /// <summary>Out of reach: under the water, or leaping for it — no use chasing it.</summary>
    public bool IsHidden => IsSubmerged || (_hopTime >= 0f && _hopIntoWater);

    /// <summary>On the surface if that's water, else on the ground.</summary>
    private static Vector3 Rest(Vector3 at) =>
        WaterMap.IsWet(at.X, at.Z) ? at with { Y = WaterMap.SurfaceHeight } : World.Grounded(at);

    public void MarkDead() => IsDead = true;

    /// <summary>A blow or a pebble: at 0 Health it's caught (see <see cref="World.KillFrog"/>).</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        Health = Math.Max(0, Health - damage);
        if (Health <= 0)
            world.KillFrog(this, attacker);
    }

    /// <summary>Sits, hops along the bank, dives when startled and surfaces again. Returns false once it's gone down into the mud for the winter.</summary>
    public bool Update(float deltaTime, World world)
    {
        if (IsDead)
            return true;

        if (_hopTime >= 0f)
        {
            _hopTime += deltaTime;
            if (_hopTime < HopSeconds)
                return true;
            _hopTime = -1f;
            _at = _hopTo;
            if (_hopIntoWater || WaterMap.IsWet(_at.X, _at.Z))
            {
                // Dived — or, swimming for a far bank, came down in the water again.
                IsSubmerged = true;
                _underTimer = _hopIntoWater ? 6f + 6f * (float)_rng.NextDouble() : 0.3f;
            }
            _sitTimer = 3f + 5f * (float)_rng.NextDouble();
            return true;
        }

        bool winter = world.CurrentSeason == Season.Winter;
        if (IsSubmerged)
        {
            if (winter)
                return false; // Down into the mud till spring.
            _underTimer -= deltaTime;
            bool wet = WaterMap.IsWet(_at.X, _at.Z);
            if (_underTimer > 0f && wet)
                return true;
            if (World.NearestShoreSpot(_at, 40f) is not { } bank)
                return false; // No bank anywhere near: it's gone.
            if (wet && Startler(world, bank, AlertRadius * 1.5f) is not null)
            {
                _underTimer = 2f; // Something's still waiting up there: stay down a little longer.
                return true;
            }
            IsSubmerged = false;
            Hop(bank, intoWater: false);
            return true;
        }

        // Startled: into the water.
        _alertTimer -= deltaTime;
        if (_alertTimer <= 0f || winter)
        {
            _alertTimer = AlertCheckInterval;
            ICombatant? startler = Startler(world, _at, AlertRadius);
            if (winter || (startler is not null && (startler is Heron || _rng.NextDouble() < AlertChance)))
            {
                if (WaterNear(_at, startler?.Position ?? _at) is { } water)
                {
                    Hop(water, intoWater: true);
                    return true;
                }
                if (winter)
                    return false;
            }
        }

        _sitTimer -= deltaTime;
        if (_sitTimer > 0f)
            return true;
        Vector3? next = WaterMap.DistanceToWater(_at.X, _at.Z) > StrayReach
            ? World.NearestShoreSpot(_at, 40f)
            : world.RandomShoreSpot(_at, 2.5f);
        if (next is { } spot && !world.IsBlocked(spot, BodyRadius))
            Hop(spot, intoWater: false);
        else
            _sitTimer = 2f;
        return true;
    }

    /// <summary>A hop goes no further than this (m) — a frog far from where it's going gets there in several.</summary>
    private const float MaxHop = 1.5f;

    private void Hop(Vector3 to, bool intoWater)
    {
        var step = new Vector3(to.X - _at.X, 0f, to.Z - _at.Z);
        if (step.Length() > MaxHop)
        {
            to = _at + Vector3.Normalize(step) * MaxHop;
            intoWater &= WaterMap.IsWet(to.X, to.Z);
        }
        _hopFrom = _at;
        _hopTo = to;
        _hopTime = 0f;
        _hopIntoWater = intoWater;
        var direction = new Vector2(to.X - _at.X, to.Z - _at.Z);
        if (direction.LengthSquared() > 1e-4f)
            _facing = Vector2.Normalize(direction);
    }

    /// <summary>A living Bramblekin (or the Heron, wading) within <paramref name="radius"/> of <paramref name="at"/>, if any.</summary>
    private static ICombatant? Startler(World world, Vector3 at, float radius)
    {
        if (world.Heron is { IsLanded: true } heron && GroundMover.HorizontalDistanceSquared(heron.Position, at) <= 9f)
            return heron;
        return world.NearestLivingKinWithin(at, radius);
    }

    /// <summary>A patch of water close by, as far from <paramref name="awayFrom"/> as it can find. Null if there's none within a couple of hops.</summary>
    private Vector3? WaterNear(Vector3 from, Vector3 awayFrom)
    {
        Vector3? best = null;
        float bestScore = float.MinValue;
        for (int i = 0; i < 12; i++)
        {
            float angle = i * MathF.Tau / 12f;
            var direction = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            foreach (float reach in stackalloc[] { 0.7f, 1.3f, 2f })
            {
                Vector3 spot = from + direction * reach;
                if (!WaterMap.IsWet(spot.X, spot.Z))
                    continue;
                float score = GroundMover.HorizontalDistance(spot, awayFrom) - 0.3f * reach + 0.1f * (float)_rng.NextDouble();
                if (score > bestScore)
                {
                    best = spot;
                    bestScore = score;
                }
            }
        }
        return best;
    }

    /// <summary>A squat green frog with dark spots and bulging eyes — or, under the water, just the eyes.</summary>
    public void Draw()
    {
        Vector3 at = Position;
        var side = new Vector3(-_facing.Y, 0f, _facing.X);
        var ahead = new Vector3(_facing.X, 0f, _facing.Y);
        if (IsSubmerged)
        {
            foreach (float s in stackalloc[] { -1f, 1f })
            {
                Vector3 eye = at + side * (0.05f * s) + new Vector3(0f, 0.02f, 0f);
                Raylib.DrawSphereEx(eye, 0.04f, 4, 5, SkinColor);
                Raylib.DrawSphereEx(eye + ahead * 0.025f + new Vector3(0f, 0.015f, 0f), 0.018f, 3, 4, EyeColor);
            }
            return;
        }

        Rlgl.PushMatrix();
        Rlgl.Translatef(at.X, at.Y + BodyRadius * 0.55f, at.Z);
        Rlgl.Rotatef(-MathF.Atan2(_facing.Y, _facing.X) * 180f / MathF.PI, 0f, 1f, 0f);
        Rlgl.Scalef(1.25f, 0.6f, 1f);
        Raylib.DrawSphereEx(Vector3.Zero, BodyRadius, 6, 8, SkinColor);
        Raylib.DrawSphereEx(new Vector3(0.02f, -0.04f, 0f), BodyRadius * 0.85f, 5, 7, BellyColor);
        Rlgl.PopMatrix();

        // Spots on its back, haunches, eyes.
        Raylib.DrawSphereEx(at + new Vector3(0f, BodyRadius * 0.95f, 0f) - ahead * 0.04f + side * 0.04f, 0.035f, 3, 4, SpotColor);
        Raylib.DrawSphereEx(at + new Vector3(0f, BodyRadius * 0.9f, 0f) - ahead * 0.07f - side * 0.05f, 0.03f, 3, 4, SpotColor);
        foreach (float s in stackalloc[] { -1f, 1f })
        {
            Raylib.DrawSphereEx(at + side * (0.12f * s) - ahead * 0.08f + new Vector3(0f, 0.05f, 0f), 0.06f, 4, 5, SkinColor);
            Vector3 eye = at + ahead * 0.1f + side * (0.06f * s) + new Vector3(0f, BodyRadius * 1.05f, 0f);
            Raylib.DrawSphereEx(eye, 0.04f, 4, 5, SkinColor);
            Raylib.DrawSphereEx(eye + ahead * 0.025f + new Vector3(0f, 0.012f, 0f), 0.02f, 3, 4, EyeColor);
        }
    }
}
