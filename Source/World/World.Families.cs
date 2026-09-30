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
    /// there's room. Members of two different groups only pair if their
    /// groups are allies — then one (not a Leader) moves over to the
    /// other's group. Returns true if group membership changed.
    /// </summary>
    private bool TryCourt(Bramblekin a, Bramblekin b)
    {
        if (!a.CanCourt || !b.CanCourt || a.Sex == b.Sex || a.IsCloseKinOf(b))
            return false;
        if (a.RelationshipTo(b) == RelationshipState.Enemy)
            return false;

        KinGroup? groupA = GroupOf(a);
        KinGroup? groupB = GroupOf(b);
        bool feastMatch = groupA is not null && groupB is not null && groupA != groupB && AtSameFeast(a, b);
        if (groupA is not null && groupB is not null && groupA != groupB && !AreAllied(groupA.Id, groupB.Id) && !feastMatch)
            return false;

        float sociability = (a.Personality.Sociability + b.Personality.Sociability) / 2f;
        float odds = CourtshipBaseChance + CourtshipSociabilityChance * sociability;
        bool gifted = TryCourtshipGift(a, b, ref odds);
        if (Rng.NextDouble() >= odds)
            return false;
        if (gifted)
            GiftsWon++;

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
        else if (groupA != groupB)
        {
            // Allied groups: one moves over to its partner's village.
            (Bramblekin mover, KinGroup to) = b != groupB!.Leader && groupA!.Members.Count < GroupSizeLimit(groupA) ? (b, groupA)
                : a != groupA!.Leader && groupB.Members.Count < GroupSizeLimit(groupB) ? (a, groupB)
                : (null!, null!);
            if (mover is null)
                return false;
            mover.JoinGroup(to.Id);
            to.Members.Add(mover);
            how = $" - {mover.GivenName} moved to {to.Title}";
            groupsChanged = true;
        }
        else
        {
            how = "";
            MoveInTogether(groupA!, a, b);
        }

        if (feastMatch)
            NoteFeastCourtship();
        Bramblekin.Pair(a, b);
        SetMutualRelationship(a, b, RelationshipState.Friend);
        CouplesFormed++;
        QueueFloatingText(a.Position, "Couple!", CoupleTextColor);
        Game.AddEventLog($"[COUPLE] {a.Name} and {b.Name} became a couple{how}");
        return groupsChanged;
    }

    /// <summary>A courtship gift adds this much to the odds, times (0.5 + the giver's Persuasiveness).</summary>
    private const float CourtshipGiftChance = 0.25f;

    /// <summary>Food given as courtship gifts.</summary>
    public int CourtshipGifts { get; private set; }

    /// <summary>Courtships a gift helped win.</summary>
    public int GiftsWon { get; private set; }

    /// <summary>
    /// Courting with a gift: a fed suitor with food in hand offers it to an
    /// empty-handed one. The gift is given whatever comes of it (the two
    /// part as Friends at least), and sways the odds — more so from a
    /// persuasive giver.
    /// </summary>
    private bool TryCourtshipGift(Bramblekin a, Bramblekin b, ref float odds)
    {
        (Bramblekin? giver, Bramblekin? taker) =
            a.HasFood && !a.IsHungry && !b.HasFood ? (a, b)
            : b.HasFood && !b.IsHungry && !a.HasFood ? (b, a)
            : (null, null);
        if (giver is null || taker is null || giver.SurrenderFood() is not { } food)
            return false;

        taker.ReceiveFood(food);
        CourtshipGifts++;
        odds += CourtshipGiftChance * (0.5f + giver.Personality.Persuasiveness) * (food.Kind == FoodShardKind.Honey ? 2f : 1f);
        if (food.Kind == FoodShardKind.Honey)
            NoteHoneyGift(); // The sweetest gift there is.
        SetMutualRelationship(giver, taker, RelationshipState.Friend);
        QueueFloatingText(taker.Position, "Gift", CoupleTextColor);
        return true;
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
    /// A couple leaves their group together — as a new,
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
        household.SettleTarget = FindOpenGround(kin.Position, GroupOf(kin));
        return household;
    }

    /// <summary>Orphans taken in by a couple.</summary>
    public int Adoptions { get; private set; }

    /// <summary>Lone orphans taken in by a settled clan they met.</summary>
    public int OrphansTakenIn { get; private set; }

    private bool IsAlive(int id)
    {
        foreach (Bramblekin kin in Colony)
        {
            if (kin.ID == id)
                return !kin.IsDead;
        }
        return false;
    }

    /// <summary>A single child (see <see cref="Bramblekin.IsChild"/>) whose parents are both dead and who has no guardians yet.</summary>
    private bool IsOrphan(Bramblekin kin) =>
        kin.IsChild && kin.Partner is null && kin.GuardianIds is null &&
        kin.ParentIds is { } parents && !IsAlive(parents.Mother) && !IsAlive(parents.Father);

    /// <summary>
    /// At each Leader decision: an orphaned child in <paramref name="group"/>
    /// is taken in by a couple there — a grown brother or sister first,
    /// else the most Sociable — who count it as their own from then on
    /// (close kin: never robbed, courted or struck down by them) and take it
    /// into their home.
    /// </summary>
    private void AdoptOrphans(KinGroup group)
    {
        foreach (Bramblekin child in group.Members)
        {
            if (child.IsDead || !IsOrphan(child))
                continue;

            Bramblekin? guardian = group.Members
                .Where(m => !m.IsDead && !m.IsChild && m.Partner is { IsDead: false } p && p.GroupId == group.Id && !p.IsChild)
                .OrderByDescending(m => m.IsCloseKinOf(child) || m.Partner!.IsCloseKinOf(child))
                .ThenByDescending(m => m.Personality.Sociability + m.Partner!.Personality.Sociability)
                .FirstOrDefault();
            if (guardian is null)
                continue;

            Bramblekin other = guardian.Partner!;
            child.Adopt(guardian, other);
            if (guardian.Home is { IsBuilt: true } home && IsGroupHome(group, home))
                child.SetHome(home);
            Adoptions++;
            QueueFloatingText(child.Position, "Adopted", CoupleTextColor);
            Game.AddEventLog($"[FAMILY] Orphaned {child.Name} was taken in by {guardian.Name} and {other.Name}");
        }
    }

    /// <summary>
    /// A young orphan with no clan left meets a member of a settled clan
    /// with room: the clan takes it in (and one of its couples will adopt
    /// it at the next Leader decision). Returns true if it joined.
    /// </summary>
    private bool TryTakeInOrphan(Bramblekin young, Bramblekin member)
    {
        if (!young.IsYoung || young.GroupId is not null || GroupOf(member) is not { Home.IsBuilt: true } group)
            return false;
        if (group.Members.Count >= BirthLimit(group))
            return false;

        young.JoinGroup(group.Id);
        group.Members.Add(young);
        OrphansTakenIn++;
        SetMutualRelationship(young, member, RelationshipState.Friend);
        QueueFloatingText(young.Position, "Taken in", group.Color);
        Game.AddEventLog($"[FAMILY] {member.Name} found young {young.Name} alone, and {group.Title} took it in");
        return true;
    }

    /// <summary>A death leaves its partner widowed.</summary>
    private static void NoteBereavement(Bramblekin dead)
    {
        if (dead.Partner is { } partner && partner.Partner == dead)
            partner.Widow();
    }
}
