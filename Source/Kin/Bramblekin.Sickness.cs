using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>Odds per second of falling ill unprompted — doubled in Winter, and again while hungry.</summary>
    private const float SicknessChancePerSecond = 1f / 8000f;

    /// <summary>An illness lasts between this…</summary>
    private const float SicknessMinSeconds = 60f;

    /// <summary>…and this long.</summary>
    private const float SicknessMaxSeconds = 150f;

    /// <summary>While ill it loses 1 Health this often — half as often again for elders and the young — and can't heal.</summary>
    private const float SicknessDamageInterval = 7f;

    /// <summary>Recovered, it can't catch it again for this long (a year).</summary>
    private const float ImmunitySeconds = 600f;

    /// <summary>The sick walk this much slower…</summary>
    private const float SickSpeedFactor = 0.75f;

    /// <summary>…and get hungry this much faster.</summary>
    private const float SickHungerFactor = 1.3f;

    private static readonly Color SickColor = new(120, 190, 60, 255);

    private float _sickness;
    private float _immunity;
    private float _sicknessDamageTimer;

    /// <summary>True while it's ill (see <see cref="UpdateSickness"/>).</summary>
    public bool IsSick => _sickness > 0f;

    /// <summary>Not ill, and not still immune from the last time.</summary>
    public bool CanCatchSickness => !IsSick && _immunity <= 0f && !IsDead;

    /// <summary>Heals <paramref name="amount"/> (a meal, a rest) — unless it's ill: the sick don't heal.</summary>
    private void Heal(int amount)
    {
        if (!IsSick)
            Health = Math.Min(MaxHealth, Health + amount);
    }

    /// <summary>Falls ill, for <see cref="SicknessMinSeconds"/>–<see cref="SicknessMaxSeconds"/>.</summary>
    public void FallSick()
    {
        _sickness = SicknessMinSeconds + (float)_rng.NextDouble() * (SicknessMaxSeconds - SicknessMinSeconds);
        _sicknessDamageTimer = 0f;
    }

    /// <summary>
    /// Sickness, each step: a healthy Bramblekin may fall ill (likelier in
    /// Winter and when hungry; it also spreads when the sick meet others —
    /// see World.Sickness); an ill one slowly loses Health until it
    /// recovers, immune for a year — or dies of it. Returns true if it died.
    /// </summary>
    private bool UpdateSickness(float deltaTime, World world)
    {
        if (_immunity > 0f)
            _immunity -= deltaTime;

        if (!IsSick)
        {
            float chance = SicknessChancePerSecond * (world.CurrentSeason == Season.Winter ? 2f : 1f) * (IsHungry ? 2f : 1f);
            if (_immunity <= 0f && _rng.NextDouble() < chance * deltaTime)
            {
                FallSick();
                world.NoteFellSick(this);
            }
            return false;
        }

        _sickness -= deltaTime;
        if (_sickness <= 0f)
        {
            _sickness = 0f;
            _immunity = ImmunitySeconds;
            return false;
        }

        _sicknessDamageTimer += deltaTime * (IsElder || IsYoung ? 1.5f : 1f);
        if (_sicknessDamageTimer >= SicknessDamageInterval)
        {
            _sicknessDamageTimer -= SicknessDamageInterval;
            TakeDamage(1, world, DeathCause.Sickness, source: null);
        }
        return IsDead;
    }

    /// <summary>A sick Bramblekin: a pale green blotch over its head.</summary>
    private void DrawSickness(System.Numerics.Vector3 head)
    {
        if (IsSick)
            Detail.Sphere(head + new System.Numerics.Vector3(0f, 0.25f, 0f), 0.07f, SickColor);
    }

    /// <summary>For saving: how much longer it's ill, and immune.</summary>
    public (float Sickness, float Immunity) SicknessState => (_sickness, _immunity);

    /// <summary>Loading a saved world: its illness or immunity as it was.</summary>
    public void RestoreSickness(float sickness, float immunity)
    {
        _sickness = sickness;
        _immunity = immunity;
    }
}
