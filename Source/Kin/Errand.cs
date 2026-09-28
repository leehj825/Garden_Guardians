namespace GardenGuardians;

/// <summary>What an <see cref="Errand"/> is for.</summary>
public enum ErrandKind
{
    /// <summary>Carrying food from a well-stocked group to an ally that has run out.</summary>
    Aid,

    /// <summary>Helping an ally build (fetching its twigs), for food carried home as pay.</summary>
    Labour,

    /// <summary>Carrying a beaten group's tribute to the group that won the war.</summary>
    Tribute,
}

/// <summary>
/// A trip one Bramblekin makes for its group to an allied one — see
/// World.Errands. It carries a sack (<see cref="Load"/>) that anyone
/// desperate enough can rob, and that spills on the ground if it dies.
/// </summary>
public sealed class Errand
{
    public required ErrandKind Kind { get; init; }

    /// <summary>The carrier's own group.</summary>
    public required Guid From { get; init; }

    /// <summary>The allied group it's going to.</summary>
    public required Guid To { get; init; }

    /// <summary>Where it's headed: the ally's home (aid) or its construction site (labour).</summary>
    public required Shelter Destination { get; init; }

    /// <summary>Food in its sack.</summary>
    public int Load { get; set; }

    /// <summary>Labour: twig deliveries still owed.</summary>
    public int TwigsOwed { get; set; }

    /// <summary>Labour: the food promised once the work is done.</summary>
    public int Payment { get; init; }

    /// <summary>On its way home (labour, with its pay).</summary>
    public bool Returning { get; set; }
}
