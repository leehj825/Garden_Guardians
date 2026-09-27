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
    public (int Mother, int Father)? ParentIds { get; private set; }

    /// <summary>True for a Bramblekin born here that hasn't reached <see cref="MaturityAge"/> yet.</summary>
    public bool IsYoung => _bornHere && _age < MaturityAge;

    /// <summary>Body size relative to a grown Bramblekin — newborns start small and grow up.</summary>
    public float BodyScale => !IsYoung ? 1f : NewbornScale + (1f - NewbornScale) * (_age / MaturityAge);

    /// <summary>
    /// A child born to <paramref name="mother"/> and <paramref name="father"/>:
    /// it inherits a mix of their Personalities (see <see cref="Personality.Inherit"/>),
    /// is a daughter or a son at even odds, starts out fed, and already
    /// counts its parents as Friends.
    /// </summary>
    public static Bramblekin BornTo(Bramblekin mother, Bramblekin father, Vector3 position, Random rng)
    {
        var child = new Bramblekin(position, rng, Personality.Inherit(mother.Personality, father.Personality, rng))
        {
            _bornHere = true,
            Generation = Math.Max(mother.Generation, father.Generation) + 1,
            ParentIds = (mother.ID, father.ID),
        };
        child.Hunger = 20f;
        child.SetRelationship(mother, RelationshipState.Friend);
        child.SetRelationship(father, RelationshipState.Friend);
        mother.SetRelationship(child, RelationshipState.Friend);
        father.SetRelationship(child, RelationshipState.Friend);
        return child;
    }
}
