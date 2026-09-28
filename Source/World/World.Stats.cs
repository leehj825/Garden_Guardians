using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Where a Bramblekin stands socially, for survival statistics — see
/// <see cref="Bramblekin.Status"/> and <see cref="World.DeathRatePerKinHour"/>.
/// </summary>
public enum SurvivalStatus
{
    /// <summary>Alone, with no home of its own.</summary>
    Wanderer,

    /// <summary>Alone, living in a shelter it built or claimed.</summary>
    Homesteader,

    /// <summary>In a group.</summary>
    Member,

    /// <summary>Alone again after leaving (or being thrown out of) a group.</summary>
    Independent,

    /// <summary>Born here and not yet grown — see <see cref="Bramblekin.IsYoung"/>.</summary>
    Young,
}

public sealed partial class World
{
    private static readonly int SurvivalStatusCount = Enum.GetValues<SurvivalStatus>().Length;

    /// <summary>Total seconds lived by all Bramblekin in each <see cref="SurvivalStatus"/> — the exposure a death rate is measured against.</summary>
    private readonly double[] _exposureSeconds = new double[SurvivalStatusCount];

    /// <summary>Deaths, by the status each Bramblekin held when it died.</summary>
    private readonly int[] _deathsByStatus = new int[SurvivalStatusCount];

    private readonly int[,] _deathCausesByStatus = new int[SurvivalStatusCount, Enum.GetValues<DeathCause>().Length];

    /// <summary>
    /// Survival trend: deaths per kin-hour lived in <paramref name="status"/>
    /// — deaths while in that status divided by the total time every
    /// Bramblekin has spent in it. Unlike an average lifespan, this isn't
    /// skewed by when each Bramblekin happened to arrive. NaN until
    /// anyone has spent any time in that status.
    /// </summary>
    public double DeathRatePerKinHour(SurvivalStatus status)
    {
        double hours = _exposureSeconds[(int)status] / 3600.0;
        return hours > 0 ? _deathsByStatus[(int)status] / hours : double.NaN;
    }

    public double KinHoursIn(SurvivalStatus status) => _exposureSeconds[(int)status] / 3600.0;

    public int DeathsIn(SurvivalStatus status) => _deathsByStatus[(int)status];

    /// <summary>Adds this frame's time to each living Bramblekin's current status, and fades its Infamy a little (see <see cref="Bramblekin.DecayInfamy"/>).</summary>
    private void AccumulateExposure(float deltaTime)
    {
        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsDead)
                continue;
            _exposureSeconds[(int)kin.Status] += deltaTime;
            kin.DecayInfamy(deltaTime);
        }
    }

    private void RecordDeath(Bramblekin kin, DeathCause cause)
    {
        _deathsByStatus[(int)kin.Status]++;
        _deathCausesByStatus[(int)kin.Status, (int)cause]++;
    }

    /// <summary>How many Bramblekin died of <paramref name="cause"/> while in <paramref name="status"/>.</summary>
    public int DeathsIn(SurvivalStatus status, DeathCause cause) => _deathCausesByStatus[(int)status, (int)cause];
}
