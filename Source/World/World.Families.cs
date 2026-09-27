using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>Two single, grown, unrelated Bramblekin of opposite sex pair up on meeting with at least these odds…</summary>
    private const float CourtshipBaseChance = 0.2f;

    /// <summary>…plus this much more at full (average) Sociability.</summary>
    private const float CourtshipSociabilityChance = 0.5f;

    /// <summary>When one of a couple leaves its group, its partner goes too if its Loyalty to the group is below this; otherwise the couple splits up.</summary>
    private const float PartnerFollowLoyalty = 0.6f;

    private static readonly Color CoupleTextColor = new(230, 110, 160, 255);

    /// <summary>Couples that have formed.</summary>
    public int CouplesFormed { get; private set; }

    /// <summary>Couples split up because one left the group and the other stayed.</summary>
    public int Separations { get; private set; }

    /// <summary>Couples alive right now.</summary>
    public int LivingCouples => Colony.Count(k => !k.IsDead && k.Sex == Sex.Female && k.Partner is { IsDead: false });

    /// <summary>
    /// Courtship, on meeting: two single, grown (not elder), unrelated
    /// Bramblekin of opposite sex who aren't Enemies pair up — odds
    /// <see cref="CourtshipBaseChance"/> plus up to
    /// <see cref="CourtshipSociabilityChance"/> for their average
    /// Sociability. Groupmates simply become a couple; two loners set up
    /// together as a new group; a loner joins its new partner's group if
    /// there's room. Members of two different groups don't — neither would
    /// leave its own. Returns true if group membership changed.
    /// </summary>
    private bool TryCourt(Bramblekin a, Bramblekin b)
    {
        if (!a.CanCourt || !b.CanCourt || a.Sex == b.Sex || a.IsCloseKinOf(b))
            return false;
        if (a.RelationshipTo(b) == RelationshipState.Enemy)
            return false;

        KinGroup? groupA = GroupOf(a);
        KinGroup? groupB = GroupOf(b);
        if (groupA is not null && groupB is not null && groupA != groupB)
            return false;

        float sociability = (a.Personality.Sociability + b.Personality.Sociability) / 2f;
        if (Rng.NextDouble() >= CourtshipBaseChance + CourtshipSociabilityChance * sociability)
            return false;

        string how;
        bool groupsChanged = false;
        if (groupA is null && groupB is null)
        {
            var household = new KinGroup(Guid.NewGuid());
            _groups[household.Id] = household;
            foreach (Bramblekin kin in new[] { a, b })
            {
                NoteRejoin(kin);
                kin.JoinGroup(household.Id);
                household.Members.Add(kin);
            }
            household.ElectLeader();
            NameGroup(household);
            how = $" and set up together as {household.Title}";
            groupsChanged = true;
        }
        else if (groupA is null || groupB is null)
        {
            KinGroup group = (groupA ?? groupB)!;
            Bramblekin loner = groupA is null ? a : b;
            if (group.Members.Count >= GroupSizeLimit(group) || HasEnemyIn(loner, group) || loner.HasLeft(group.Id))
                return false;

            NoteRejoin(loner);
            loner.JoinGroup(group.Id);
            group.Members.Add(loner);
            how = $" - {loner.GivenName} joined {group.Title}";
            groupsChanged = true;
        }
        else
        {
            how = "";
            MoveInTogether(groupA!, a, b);
        }

        Bramblekin.Pair(a, b);
        SetMutualRelationship(a, b, RelationshipState.Friend);
        CouplesFormed++;
        QueueFloatingText(a.Position, "Couple!", CoupleTextColor);
        Game.AddEventLog($"[COUPLE] {a.Name} and {b.Name} became a couple{how}");
        return groupsChanged;
    }

    /// <summary>A couple in the same group shares a home: one moves into the other's if there's room.</summary>
    private void MoveInTogether(KinGroup group, Bramblekin a, Bramblekin b)
    {
        if (a.Home == b.Home)
            return;
        foreach (var (mover, host) in new[] { (b, a), (a, b) })
        {
            if (mover == group.Leader || host.Home is not { IsBuilt: true } home || !IsGroupHome(group, home))
                continue;
            if (group.Members.Count(m => m.Home == home) < home.ResidentCapacity)
            {
                mover.SetHome(home);
                return;
            }
        }
    }

    /// <summary>
    /// One of a couple is leaving <paramref name="group"/>: true if its
    /// partner (a groupmate, not the Leader) is unhappy enough there to go
    /// along (see <see cref="PartnerFollowLoyalty"/>). Otherwise, if its
    /// partner stays, the couple splits up.
    /// </summary>
    private bool PartnerGoesAlong(KinGroup group, Bramblekin leaving)
    {
        if (leaving.Partner is not { IsDead: false } partner || partner.GroupId != group.Id)
            return false;
        if (partner != group.Leader && !partner.IsDueling && partner.Loyalty < PartnerFollowLoyalty)
            return true;

        Separations++;
        Game.AddEventLog($"[SEPARATED] {partner.Name} stayed with {group.Title}; {leaving.GivenName} and {partner.GivenName} are no longer a couple");
        leaving.Separate();
        return false;
    }

    /// <summary>
    /// A couple leaves <paramref name="group"/> together — as a new,
    /// homeless household of two with a Leader of its own. Both count as
    /// having left.
    /// </summary>
    private KinGroup LeaveAsCouple(Bramblekin kin, Bramblekin partner)
    {
        var household = new KinGroup(Guid.NewGuid());
        _groups[household.Id] = household;
        foreach (Bramblekin member in new[] { kin, partner })
        {
            member.SplitOff(household.Id);
            household.Members.Add(member);
        }
        household.ElectLeader();
        NameGroup(household);
        return household;
    }

    /// <summary>A death leaves its partner widowed.</summary>
    private static void NoteBereavement(Bramblekin dead)
    {
        if (dead.Partner is { } partner && partner.Partner == dead)
            partner.Widow();
    }
}
