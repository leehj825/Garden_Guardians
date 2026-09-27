namespace GardenGuardians;

/// <summary>
/// Heroes and dynasties: one Bramblekin's life, kept after it dies (the
/// Bramblekin itself is gone from the World by then) so family trees and the
/// hall of fame can reach back through the generations — see World.Lives.
/// </summary>
public sealed class LifeRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? MotherId { get; set; }
    public int? FatherId { get; set; }
    public int Generation { get; set; }

    /// <summary>When it was born here, or wandered in (World.ElapsedSeconds).</summary>
    public float Arrived { get; set; }

    /// <summary>When it died, if it has.</summary>
    public float? Died { get; set; }

    /// <summary>How it died ("died of old age at 5.5 years, leaving 7 children").</summary>
    public string? Fate { get; set; }

    /// <summary>The clan it belonged to when it died.</summary>
    public string? Clan { get; set; }

    // Filled in at death; while it lives, the Bramblekin's own figures are current.
    public int Children { get; set; }
    public float AgeYears { get; set; }
    public float LeaderSeconds { get; set; }
    public int SpiderKills { get; set; }
}
