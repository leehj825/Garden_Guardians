using System.Numerics;

namespace GardenGuardians;

/// <summary>Where a Kingdom's assault on the ant hill has got to.</summary>
public enum AssaultPhase
{
    /// <summary>The party gathers at the capital.</summary>
    Mustering,

    /// <summary>It marches together to a staging point just outside the hill's zone.</summary>
    Marching,

    /// <summary>It fights the guards.</summary>
    Fighting,

    /// <summary>Every guard is down: the survivors go into the hill for the prize.</summary>
    Looting,

    /// <summary>The carriers take the prize home to the capital.</summary>
    Returning,

    /// <summary>The Kingdom has called it off: everyone goes home.</summary>
    Retreating,
}

/// <summary>One Kingdom assault on the ant hill (see World.Assault). Not saved: a loaded garden starts with none under way.</summary>
public sealed class Assault
{
    /// <summary>"Thornreach" — whoever sent it.</summary>
    public string Name { get; init; } = "";

    /// <summary>The hill's level when it set out: the guards it faces are 5 × this many, and the prize is this many eggs.</summary>
    public int Level { get; init; }

    /// <summary>Where it gathers, and where it brings the prize: the capital's middle.</summary>
    public Vector3 Muster { get; init; }

    /// <summary>Where it forms up just outside the zone.</summary>
    public Vector3 Staging { get; init; }

    /// <summary>The soldiers it set out with (the dead included).</summary>
    public List<Bramblekin> Party { get; } = new();

    public AssaultPhase Phase { get; set; } = AssaultPhase.Mustering;

    /// <summary>Seconds in the current phase.</summary>
    public float PhaseSeconds { get; set; }

    public float StartedAt { get; init; }

    /// <summary>Guards each member (by ID) has struck down.</summary>
    public Dictionary<int, int> Kills { get; } = new();

    /// <summary>Members (by ID) who ran away on their own.</summary>
    public HashSet<int> Fled { get; } = new();

    /// <summary>Members (by ID) who have gone into the hill for the prize and carry it home.</summary>
    public HashSet<int> Carriers { get; } = new();

    public bool Ended { get; set; }

    public int Size => Party.Count;

    public bool IsActive(Bramblekin kin) => !kin.IsDead && !Fled.Contains(kin.ID);
}
