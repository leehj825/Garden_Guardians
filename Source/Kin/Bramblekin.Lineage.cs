using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>Seconds a newborn stays young — small, fed first, kept out of work, fights and politics.</summary>
    public const float MaturityAge = 90f;

    /// <summary>How big a newborn is next to a grown Bramblekin; it grows to full size as it matures.</summary>
    private const float NewbornScale = 0.55f;

    private bool _bornHere;

    /// <summary>0 for a Bramblekin that wandered in; one more than its older parent's for one born here.</summary>
    public int Generation { get; private set; }

    /// <summary>The two Bramblekin it was born to, if it was born here.</summary>
    public (int A, int B)? ParentIds { get; private set; }

    /// <summary>True for a Bramblekin born here that hasn't reached <see cref="MaturityAge"/> yet.</summary>
    public bool IsYoung => _bornHere && _age < MaturityAge;

    /// <summary>Body size relative to a grown Bramblekin — newborns start small and grow up.</summary>
    public float BodyScale => !IsYoung ? 1f : NewbornScale + (1f - NewbornScale) * (_age / MaturityAge);

    /// <summary>
    /// A child born to <paramref name="a"/> and <paramref name="b"/>: it
    /// inherits a mix of their Personalities (see <see cref="Personality.Inherit"/>),
    /// starts out fed, and already counts its parents as Friends.
    /// </summary>
    public static Bramblekin BornTo(Bramblekin a, Bramblekin b, Vector3 position, Random rng)
    {
        var child = new Bramblekin(position, rng, Personality.Inherit(a.Personality, b.Personality, rng))
        {
            _bornHere = true,
            Generation = Math.Max(a.Generation, b.Generation) + 1,
            ParentIds = (a.ID, b.ID),
        };
        child.Hunger = 20f;
        child.SetRelationship(a, RelationshipState.Friend);
        child.SetRelationship(b, RelationshipState.Friend);
        a.SetRelationship(child, RelationshipState.Friend);
        b.SetRelationship(child, RelationshipState.Friend);
        return child;
    }
}
