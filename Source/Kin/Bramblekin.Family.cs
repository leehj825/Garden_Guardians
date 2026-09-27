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

    /// <summary>A parent, a child, or a (half-)sibling of <paramref name="other"/> — never a partner.</summary>
    public bool IsCloseKinOf(Bramblekin other)
    {
        if (ParentIds is { } mine && (mine.Mother == other.ID || mine.Father == other.ID))
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
