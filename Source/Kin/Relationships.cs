using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>How one Bramblekin regards another it has met — see <see cref="Bramblekin.KnownKins"/>.</summary>
public enum RelationshipState
{
    Neutral,
    Friend,

    /// <summary>Permanent: once blows (or a robbery) have been traded, no later encounter can undo it.</summary>
    Enemy,
}

/// <summary>What killed a Bramblekin — tallied separately on the HUD.</summary>
public enum DeathCause
{
    Starvation,
    Predator,
    Kin,
}
