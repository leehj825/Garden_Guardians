namespace GardenGuardians;

/// <summary>
/// What a Release build leaves out. Debug builds (and the desktop prototype) keep every development aid; a Release build is the game as it is meant to be
/// played for now, a simulation to watch: no fast-forward, no taking over a kin, no testing aids on the start menu, and only random terrains for new gardens.
/// </summary>
internal static class Build
{
#if DEBUG
    /// <summary>True in a Debug build, which can play as the Release game (see <see cref="PlayAsRelease"/>).</summary>
    public const bool IsDebugBuild = true;
#else
    public const bool IsDebugBuild = false;
#endif

    /// <summary>A Debug build with the Settings page's "Play as Release" switched on behaves as the Release game, so the Release experience can be tested from the Debug APK.</summary>
    public static bool PlayAsRelease { get; set; }

    /// <summary>True in a Release build, or in a Debug build playing as Release.</summary>
    public static bool Release => !IsDebugBuild || PlayAsRelease;

    /// <summary>The speed buttons (and any speed other than 1x).</summary>
    public static bool SpeedControls => !Release;

    /// <summary>Taking over a Bramblekin (the Control button and PlayControl).</summary>
    public static bool KinControl => !Release;

    /// <summary>
    /// Explore: a Release build lets the player take a Bramblekin's wheel in a gentler form (the Explore button, where a Debug build has Control): walk
    /// and run (push the stick to its edge), jump, pick things up, open the bag; no attacking, no job, no taking sides. It picks up what it walks near
    /// and eats and drinks on its own, and cannot be hurt. See <see cref="PlayControl"/>.
    /// </summary>
    public static bool Explore => Release;

    /// <summary>The event log on the screen (and its Log button) and the stats bar along the bottom (and its Stats button): developer readouts, not part of the game.</summary>
    public static bool Diagnostics => !Release;

    /// <summary>Starting a new garden on the fixed original terrain, and choosing the age it starts in (a testing aid).</summary>
    public static bool TestingChoices => !Release;
}
