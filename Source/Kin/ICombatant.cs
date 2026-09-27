using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Anything a Bramblekin can strike, or be threatened by: another
/// Bramblekin, the Wolf Spider, a Hornet or a Grub.
/// </summary>
public interface ICombatant
{
    Vector3 Position { get; }

    bool IsDead { get; }

    /// <summary>Body radius (m); strike reach is measured edge to edge.</summary>
    float CollisionRadius { get; }

    /// <summary>Takes one strike from <paramref name="attacker"/>.</summary>
    void TakeHit(int damage, Bramblekin attacker, World world);
}
