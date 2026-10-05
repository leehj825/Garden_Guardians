namespace GardenGuardians;

/// <summary>
/// What a Release build leaves out. Debug builds (and the desktop prototype) keep every development aid; a Release build is the game as it is meant to be
/// played for now, a simulation to watch: no fast-forward, no taking over a kin, no testing aids on the start menu, and only random terrains for new gardens.
/// </summary>
internal static class Build
{
#if DEBUG
    public static readonly bool Release = false;
#else
    public static readonly bool Release = true;
#endif

    /// <summary>The speed buttons (and any speed other than 1x).</summary>
    public static readonly bool SpeedControls = !Release;

    /// <summary>Taking over a Bramblekin (the Control button and PlayControl).</summary>
    public static readonly bool KinControl = !Release;

    /// <summary>Starting a new garden on the fixed original terrain, and choosing the age it starts in (a testing aid).</summary>
    public static readonly bool TestingChoices = !Release;
}
