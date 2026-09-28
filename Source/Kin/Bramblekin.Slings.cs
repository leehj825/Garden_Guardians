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

    /// <summary>A Guard with a sling clears any hornets' nest it sees within this far (m) of home…</summary>
    private const float NestClearingRadius = 14f;

    /// <summary>…while it's at least this fit.</summary>
    private const float NestClearingHealthFraction = 0.6f;

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
    /// A Guard with a sling doesn't give a hornets' nest near home a wide
    /// berth: it picks the swarm off a pebble at a time, from just outside
    /// the reach of the Hornet it's aiming at (see <see cref="SlingRange"/>).
    /// </summary>
    private bool IsClearingNest(Hornet hornet) =>
        Job == KinJob.Guard && CanSling(hornet) && Health >= MaxHealth * NestClearingHealthFraction && Home is { IsBuilt: true } home &&
        GroundMover.HorizontalDistanceSquared(hornet.Position, home.Position) <= NestClearingRadius * NestClearingRadius;

    /// <summary>
    /// A slinger in range of <paramref name="target"/> stands its ground,
    /// faces it and looses a pebble (once its sling's ready again). False if
    /// it has no sling for this, or the target's out of range — or close
    /// enough to strike, which is surer and quicker.
    /// </summary>
    private bool TrySling(ICombatant target, World world)
    {
        if (!CanSling(target))
            return false;
        Vector3 targetPosition = target.Position;
        float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, targetPosition);
        float strikeReach = BodyRadius + target.CollisionRadius + StrikeReach;
        if (distanceSquared > SlingRange * SlingRange || distanceSquared <= strikeReach * strikeReach)
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
