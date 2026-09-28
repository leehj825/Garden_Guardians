using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>After losing its partner, it doesn't look for another for this long.</summary>
    private const float MourningSeconds = 90f;

    private float _mourningTimer;

    /// <summary>Its partner, if it's in a couple — see <see cref="World.TryCourt"/>.</summary>
    public Bramblekin? Partner { get; private set; }

    /// <summary>True once it has lost a partner to death (until it pairs up again).</summary>
    public bool IsWidowed { get; private set; }

    /// <summary>Free to pair up: grown, not yet an elder, single, and not still mourning.</summary>
    public bool CanCourt => !IsYoung && !IsElder && Partner is null && _mourningTimer <= 0f && !IsDead;

    /// <summary>A child is still one — and can be taken in as an orphan (see World.AdoptOrphans) — until this age (s): its first year.</summary>
    public const float ChildhoodAge = 600f;

    /// <summary>Born here and not yet a year old.</summary>
    public bool IsChild => _bornHere && _age < ChildhoodAge;

    /// <summary>The couple who took it in when it was orphaned, if any — family from then on, like parents.</summary>
    public (int A, int B)? GuardianIds { get; private set; }

    /// <summary>Its guardians' names, kept after they're gone.</summary>
    public (string A, string B)? GuardianNames { get; private set; }

    /// <summary>Taken in by <paramref name="a"/> and <paramref name="b"/>: they count it among their children, and it's close kin to them from now on.</summary>
    public void Adopt(Bramblekin a, Bramblekin b)
    {
        GuardianIds = (a.ID, b.ID);
        GuardianNames = (a.Name, b.Name);
        a.Children++;
        b.Children++;
        foreach (Bramblekin guardian in new[] { a, b })
        {
            SetRelationship(guardian, RelationshipState.Friend);
            guardian.SetRelationship(this, RelationshipState.Friend);
        }
    }

    /// <summary>A parent (or guardian), a child (or ward), or a (half-)sibling of <paramref name="other"/> — never a partner.</summary>
    public bool IsCloseKinOf(Bramblekin other)
    {
        if (ParentIds is { } mine && (mine.Mother == other.ID || mine.Father == other.ID))
            return true;
        if (GuardianIds is { } guardians && (guardians.A == other.ID || guardians.B == other.ID))
            return true;
        if (other.GuardianIds is { } theirGuardians && (theirGuardians.A == ID || theirGuardians.B == ID))
            return true;
        if (other.ParentIds is { } theirs)
        {
            if (theirs.Mother == ID || theirs.Father == ID)
                return true;
            if (ParentIds is { } own &&
                (own.Mother == theirs.Mother || own.Father == theirs.Father))
                return true;
        }
        return false;
    }

    /// <summary>Makes <paramref name="a"/> and <paramref name="b"/> a couple.</summary>
    public static void Pair(Bramblekin a, Bramblekin b)
    {
        a.Partner = b;
        b.Partner = a;
        a.IsWidowed = false;
        b.IsWidowed = false;
    }

    /// <summary>The couple splits up (one of them left and the other stayed).</summary>
    public void Separate()
    {
        if (Partner is { } partner && partner.Partner == this)
            partner.Partner = null;
        Partner = null;
    }

    /// <summary>Its partner has died: it's on its own again, and mourns a while before it looks for another.</summary>
    public void Widow()
    {
        Partner = null;
        IsWidowed = true;
        _mourningTimer = MourningSeconds;
    }

    private void UpdateFamily(float deltaTime) =>
        _mourningTimer = MathF.Max(0f, _mourningTimer - deltaTime);

    /// <summary>"Partner: Nella Thornwood", "Widowed" or "Single", plus its children, for the Kin Inspector.</summary>
    public string DescribeFamily()
    {
        string partner = Partner is { IsDead: false } p ? $"Partner: {p.Name}" : IsWidowed ? "Widowed" : "Single";
        return Children > 0 ? $"{partner}, {Children} {(Children == 1 ? "child" : "children")}" : partner;
    }
}
