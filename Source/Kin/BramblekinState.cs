using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>What a Bramblekin is currently doing, grouped by the need driving it.</summary>
public enum BramblekinState
{
    // --- Social (fed and safe) ---

    /// <summary>Resting between moves.</summary>
    Idle,

    /// <summary>Roaming to a random nearby point (or milling about near its Leader).</summary>
    Wandering,

    /// <summary>Walking over to meet a stranger it has spotted.</summary>
    Socializing,

    /// <summary>A follower catching up with its group's Leader.</summary>
    Following,

    // --- Settle (fed and safe) ---

    /// <summary>Walking to pick up a twig for building.</summary>
    Collecting,

    /// <summary>Carrying a twig to its home's construction site.</summary>
    Building,

    /// <summary>Carrying food home to put in the store.</summary>
    Stockpiling,

    /// <summary>On its way home — to eat from the store, rest, or hide.</summary>
    HeadingHome,

    /// <summary>Inside its home: healing, and safe from the Wolf Spider and Hornets.</summary>
    Resting,

    // --- Duty (a group member's job) ---

    /// <summary>A Guard returning to its post by the group's home.</summary>
    Guarding,

    // --- Critical (hunger) ---

    /// <summary>Hungry with no food in sight: roaming further afield to find some.</summary>
    Searching,

    /// <summary>Walking to a piece of loose Food it has claimed.</summary>
    Foraging,

    /// <summary>Eating the Food it's holding.</summary>
    Eating,

    /// <summary>Chasing down a Grub to eat.</summary>
    Hunting,

    /// <summary>Starving and Aggressive: attacking another Bramblekin to steal its food.</summary>
    Attacking,

    /// <summary>Hungry: on its way to take Food from someone else's store (or scavenge an abandoned one).</summary>
    Raiding,

    // --- Safety ---

    /// <summary>Running from a threat it chose not to fight.</summary>
    Fleeing,

    /// <summary>Standing its ground against a threat — or defending a groupmate from one.</summary>
    Fighting,
}
