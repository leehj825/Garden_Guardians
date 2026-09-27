using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>A year in the garden: four seasons.</summary>
    public const float SecondsPerYear = World.SeasonLength * 4f;

    /// <summary>From this age on it's an elder: slower and weaker, past raising young, but looked up to.</summary>
    public const float ElderAge = 3.5f * SecondsPerYear;

    /// <summary>Each Bramblekin's natural lifespan is rolled between this…</summary>
    private const float MinLifespan = 4f * SecondsPerYear;

    /// <summary>…and this.</summary>
    private const float MaxLifespan = 6.5f * SecondsPerYear;

    /// <summary>A newcomer wanders in already grown, somewhere between this age…</summary>
    private const float MinArrivalAge = 0.3f * SecondsPerYear;

    /// <summary>…and this.</summary>
    private const float MaxArrivalAge = 1.5f * SecondsPerYear;

    /// <summary>An elder moves at this fraction of the usual speed…</summary>
    private const float ElderSpeedFactor = 0.8f;

    /// <summary>…and hits this much less hard.</summary>
    private const float ElderStrikeFactor = 0.7f;

    /// <summary>An elder's claim to lead gets this much extra — see <see cref="LeadershipScore"/>.</summary>
    private const float ElderLeadershipBonus = 0.3f;

    /// <summary>The silver-grey an elder's body fades toward…</summary>
    private static readonly Color ElderColor = new(175, 175, 170, 255);

    /// <summary>…by this much.</summary>
    private const float ElderGreying = 0.5f;

    /// <summary>How long this one will live, barring accidents — rolled at birth (or arrival).</summary>
    private float _lifespan;

    /// <summary>Seconds since it arrived in (or was born into) the garden — unlike <see cref="Age"/>, which a newcomer brings with it.</summary>
    private float _timeHere;

    /// <summary>Its age in seconds.</summary>
    public float Age => _age;

    public float AgeInYears => _age / SecondsPerYear;

    public bool IsElder => _age >= ElderAge;

    /// <summary>How many young it has had.</summary>
    public int Children { get; private set; }

    public void NoteChildBorn() => Children++;

    /// <summary>A newcomer arrives grown: somewhere between <see cref="MinArrivalAge"/> and <see cref="MaxArrivalAge"/> old.</summary>
    public void SetArrivalAge(Random rng) =>
        _age = MinArrivalAge + (float)rng.NextDouble() * (MaxArrivalAge - MinArrivalAge);

    private void RollLifespan(Random rng) =>
        _lifespan = MinLifespan + (float)rng.NextDouble() * (MaxLifespan - MinLifespan);

    /// <summary>Speed multiplier for its age (see <see cref="ElderSpeedFactor"/>).</summary>
    private float AgeSpeedFactor => IsElder ? ElderSpeedFactor : 1f;

    /// <summary>Ages it; returns true (it's dead) once it reaches the end of its lifespan.</summary>
    private bool UpdateAging(float deltaTime, World world)
    {
        _age += deltaTime;
        _timeHere += deltaTime;
        if (_age < _lifespan)
            return false;

        world.Kill(this, DeathCause.OldAge, killer: null);
        return true;
    }

    /// <summary>"3.2 years (elder)" for the Kin Inspector.</summary>
    public string DescribeAge() =>
        IsYoung ? $"{_age:0}s old (young)" : $"{AgeInYears:0.0} years{(IsElder ? " (elder)" : "")}";
}
