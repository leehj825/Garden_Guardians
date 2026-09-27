namespace GardenGuardians;

/// <summary>What a group's Leader has decided the group should be doing — see <see cref="World.DecideGroupGoal"/>.</summary>
public enum GroupGoal
{
    /// <summary>Fill the shared store.</summary>
    Stockpile,

    /// <summary>Build (or upgrade) the group's home.</summary>
    Settle,

    /// <summary>Bring down a Stag Beetle together.</summary>
    Hunt,

    /// <summary>Drive off a threat near home.</summary>
    Defend,
}

/// <summary>A group member's assignment from its Leader, carried out in the Duty need (see Bramblekin.UpdateDuty).</summary>
public enum KinJob
{
    None,

    /// <summary>Brings Food home to the shared store.</summary>
    Gatherer,

    /// <summary>Fetches twigs to build or upgrade the home.</summary>
    Builder,

    /// <summary>Hunts the prey the Leader has picked.</summary>
    Hunter,

    /// <summary>Stays by the home and attacks whatever threatens it.</summary>
    Guard,
}

/// <summary>Who may eat from a group's shared store — set by its Leader's personality.</summary>
public enum SharingRule
{
    /// <summary>Anyone hungry eats.</summary>
    Equal,

    /// <summary>The Leader eats whenever it's hungry; everyone else only once starving.</summary>
    LeaderFirst,
}

/// <summary>A Leader's broad character, for comparing how differently groups are run — see <see cref="World.GoalShare"/>.</summary>
public enum LeaderStyle
{
    /// <summary>Aggression ≥ 0.6 — and more Aggressive than Intelligent.</summary>
    Warlike,

    /// <summary>Intelligence ≥ 0.6 — and more Intelligent than Aggressive.</summary>
    Planner,

    Moderate,
}
