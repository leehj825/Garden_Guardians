using System.Numerics;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    // --- Slings ------------------------------------------------------------------------

    /// <summary>A sling looses a pebble at anything within this far (m).</summary>
    public const float SlingRange = 3.5f;

    /// <summary>Seconds to fit another pebble and swing again.</summary>
    private const float SlingCooldown = 1.4f;

    /// <summary>Knowing it can hit back from a few paces off, it's this much likelier to stand up to a Hornet or the Heron.</summary>
    private const float SlingNerve = 0.25f;

    /// <summary>
    /// What a sling is for (see <see cref="Craft.Slings"/>): things too quick
    /// to catch, or too dangerous to close with — a Hornet, a frog on the
    /// bank, the Heron.
    /// </summary>
    private bool CanSling(ICombatant target) => target is Hornet or Frog or Heron && !IsYoung && Knows(Craft.Slings);

    /// <summary>How often a pebble finds its mark: a darting Hornet least, the Heron's big grey bulk most.</summary>
    private static float SlingAccuracy(ICombatant target) => target switch
    {
        Hornet => 0.6f,
        Frog => 0.7f,
        _ => 0.85f,
    };

    /// <summary>
    /// A slinger in range of <paramref name="target"/> stands its ground,
    /// faces it and looses a pebble (once its sling's ready again). False if
    /// it has no sling for this, or the target's out of range.
    /// </summary>
    private bool TrySling(ICombatant target, World world)
    {
        if (!CanSling(target))
            return false;
        Vector3 targetPosition = target.Position;
        if (GroundMover.HorizontalDistanceSquared(Position, targetPosition) > SlingRange * SlingRange)
            return false;

        var toTarget = new Vector2(targetPosition.X - Position.X, targetPosition.Z - Position.Z);
        if (toTarget.LengthSquared() > 1e-6f)
            _mover.Heading = Vector2.Normalize(toTarget);
        if (_strikeCooldown > 0f)
            return true;
        _strikeCooldown = SlingCooldown;
        world.LoosePebble(this, target, _rng.NextDouble() < SlingAccuracy(target), StrikeDamage);
        return true;
    }
}
