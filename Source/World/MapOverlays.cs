namespace GardenGuardians;

/// <summary>The drawn-over-the-map guides the player can switch on and off, each on its own.</summary>
[Flags]
public enum MapOverlays
{
    None = 0,
    /// <summary>Each village's washed ground and rim.</summary>
    ClanRange = 1,
    /// <summary>Tether lines from followers to their Leader, and the green/red lines between clans.</summary>
    KinLinks = 2,
    /// <summary>The selected Bramblekin's detection ring.</summary>
    KinRange = 4,
    /// <summary>Greys out the ground the selected clan hasn't seen (off by default, not part of <see cref="All"/>).</summary>
    Fog = 8,
    All = ClanRange | KinLinks | KinRange,
}
