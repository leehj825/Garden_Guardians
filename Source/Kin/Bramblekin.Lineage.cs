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

    /// <summary>Its own name, e.g. "Pipwick" — see <see cref="Names"/>.</summary>
    public string GivenName { get; private set; } = "";

    /// <summary>Its family's name: one it founded on wandering in, or a parent's.</summary>
    public string FamilyName { get; private set; } = "";

    /// <summary>"Given Family", as the event log and the Kin Inspector show it.</summary>
    public string Name => $"{GivenName} {FamilyName}";

    /// <summary>Names a newcomer, who founds a family of its own.</summary>
    public void Christen(string givenName, string familyName)
    {
        GivenName = givenName;
        FamilyName = familyName;
    }

    /// <summary>0 for a Bramblekin that wandered in; one more than its older parent's for one born here.</summary>
    public int Generation { get; private set; }

    /// <summary>The two Bramblekin it was born to, if it was born here.</summary>
    public (int Mother, int Father)? ParentIds { get; private set; }

    /// <summary>Its parents' names, kept after they're gone.</summary>
    public (string Mother, string Father)? ParentNames { get; private set; }

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
    public static Bramblekin BornTo(Bramblekin mother, Bramblekin father, Vector3 position, Random rng, ClanCulture? culture = null)
    {
        Personality nature = Personality.Inherit(mother.Personality, father.Personality, rng);
        var child = new Bramblekin(position, rng, culture?.Nudge(nature) ?? nature)
        {
            _bornHere = true,
            Generation = Math.Max(mother.Generation, father.Generation) + 1,
            ParentIds = (mother.ID, father.ID),
            ParentNames = (mother.Name, father.Name),
            GivenName = Names.Given(rng),
            // Either parent's family name, at even odds.
            FamilyName = rng.Next(2) == 0 ? mother.FamilyName : father.FamilyName,
        };
        child.Hunger = 20f;
        child.Thirst = 10f;
        child.Crafts = mother.Crafts | father.Crafts;
        child.InheritSkills(mother, father);
        child.SetRelationship(mother, RelationshipState.Friend);
        child.SetRelationship(father, RelationshipState.Friend);
        mother.SetRelationship(child, RelationshipState.Friend);
        father.SetRelationship(child, RelationshipState.Friend);
        return child;
    }
}
