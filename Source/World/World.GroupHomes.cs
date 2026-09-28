using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>After failing to find a site for a group home, the group waits this long before looking again.</summary>
    private const float GroupSiteRetryDelay = 5f;

    /// <summary>A village has at most this many homes besides its main one.</summary>
    public const int MaxAnnexes = 2;

    /// <summary>No group grows past this, however much housing it has (three Houses' worth).</summary>
    public const int MaxVillageSize = 18;

    /// <summary>A group may keep raising young until it's this many past its housing — the crowding is what makes it build more.</summary>
    private const int BirthCrowdingAllowance = 2;

    /// <summary>A group only expands into a new home while its stores are at least this full — it has to be thriving, not just crowded.</summary>
    private const float ExpansionStoreFill = 0.5f;

    /// <summary>A new home in a village goes up within this many meters of the main one.</summary>
    private const float AnnexSiteRadius = 12f;

    /// <summary>Merging groups keep the smaller one's homes within this many meters of the larger's main home.</summary>
    private const float AnnexAbsorbRadius = 2f * AnnexSiteRadius;

    /// <summary>A village buds off a daughter group once it has at least this many members…</summary>
    private const int BaseBuddingSize = 10;

    /// <summary>…plus up to this many more for a Sociable Leader, who holds a bigger village together.</summary>
    private const float BuddingSizePerSociability = 6f;

    /// <summary>A house needs at least this many grown residents to set up as a group of its own.</summary>
    private const int MinBuddingResidents = 3;

    private readonly List<(KinGroup Parent, Shelter House)> _pendingBuddings = new();

    /// <summary>Villages: groups that built a second home.</summary>
    public int VillagesFounded { get; private set; }

    /// <summary>Daughter groups budded off a village.</summary>
    public int Buddings { get; private set; }

    /// <summary>Every home <paramref name="group"/> has: its main home, then any others in its village (see <see cref="VillageHomes"/>).</summary>
    public VillageHomes GroupHomes(KinGroup group) => new(group);

    /// <summary>How many the group's finished homes house between them (Tent 2, House 6).</summary>
    public int HousingCapacity(KinGroup group)
    {
        int capacity = 0;
        foreach (Shelter home in GroupHomes(group))
        {
            if (home.IsBuilt)
                capacity += home.ResidentCapacity;
        }
        return capacity;
    }

    /// <summary>How big <paramref name="group"/> may grow by taking others in: its housing, but never below <see cref="MaxGroupSize"/> or above <see cref="MaxVillageSize"/>.</summary>
    public int GroupSizeLimit(KinGroup group) => Math.Clamp(HousingCapacity(group), MaxGroupSize, MaxVillageSize);

    /// <summary>How big <paramref name="group"/> may grow by raising young: a little past its housing (see <see cref="BirthCrowdingAllowance"/>).</summary>
    public int BirthLimit(KinGroup group) =>
        Math.Clamp(HousingCapacity(group) + BirthCrowdingAllowance, MaxGroupSize, MaxVillageSize);

    /// <summary>Food stored across all the group's homes.</summary>
    public int StoredFood(KinGroup group)
    {
        int stored = 0;
        foreach (Shelter home in GroupHomes(group))
            stored += home.StoredFood;
        return stored;
    }

    /// <summary>Food stored across all the group's finished homes, as a fraction of what they can hold (0 with none).</summary>
    public float StoreFill(KinGroup group)
    {
        int stored = 0, capacity = 0;
        foreach (Shelter home in GroupHomes(group))
        {
            if (!home.IsBuilt)
                continue;
            stored += home.StoredFood;
            capacity += home.StoreCapacity;
        }
        return capacity > 0 ? stored / (float)capacity : 0f;
    }

    private bool IsGroupHome(KinGroup group, Shelter? shelter) =>
        shelter is { IsCollapsed: false } && (shelter == group.Home || group.Annexes.Contains(shelter));

    /// <summary>
    /// Group homes, once a frame. A group without a home adopts the best
    /// one any member already has (a House over a Tent, a finished home
    /// over a site, then the fuller store); failing that, its Leader marks
    /// out a site near where it last found Food. Every member lives in one
    /// of the group's homes — the roomiest, carrying over what was in its
    /// old store — and residents spread out of an overcrowded home into one
    /// with room. Then the group plans its next construction (see
    /// <see cref="PlanConstruction"/>) and, grown big enough, buds off a
    /// daughter group (see <see cref="QueueBudding"/>).
    /// </summary>
    private void UpdateGroupHomes(float deltaTime)
    {
        foreach (KinGroup group in _groups.Values)
        {
            group.Annexes.RemoveAll(a => a.IsCollapsed);
            if (group.Home is { IsCollapsed: true })
                group.Home = null;
            if (group.Home is null && group.Annexes.Count > 0)
            {
                group.Home = group.Annexes[0]; // The village's next home becomes its main one.
                group.Annexes.RemoveAt(0);
            }

            group.Home ??= BestMemberHome(group);
            if (group.Home is null)
            {
                group.HomeSiteRetryTimer -= deltaTime;
                if (group.HomeSiteRetryTimer > 0f || group.Leader is not { } leader)
                    continue;

                // Pioneers make for the open ground they picked; anyone else settles where its Leader last found food.
                group.Home = group.SettleTarget is { } target
                    ? TryCreateShelterSite(target, owner: null, groupId: group.Id, searchRadius: PioneerSiteRadius)
                    : TryCreateShelterSite(leader.FoodMemory ?? leader.Position, owner: null, groupId: group.Id);
                group.SettleTarget = null;
                if (group.Home is null)
                {
                    group.HomeSiteRetryTimer = GroupSiteRetryDelay;
                    continue;
                }
                Game.AddEventLog($"[SETTLE] {group.CapitalTitle} is building a home");
            }

            foreach (Shelter home in GroupHomes(group))
            {
                home.GroupId = group.Id;
                home.Owner = null;
                home.AbandonedSeconds = 0f;
            }

            // A budded group's dowry goes into its store as soon as it has one.
            while (group.Dowry > 0 && group.Home is { IsBuilt: true } newHome && newHome.TryDeposit())
                group.Dowry--;

            foreach (Bramblekin member in group.Members)
            {
                if (!IsGroupHome(group, member.Home))
                    MoveIn(member, PartnersHomeWithRoom(group, member) ?? RoomiestHome(group));
            }
            SpreadOutOneResident(group);

            if (group.ConstructionSite is null)
                PlanConstruction(group);

            Shelter? site = group.ConstructionSite;
            foreach (Bramblekin member in group.Members)
                member.SetBuildSite(site);

            QueueBudding(group);
        }

        ProcessBuddings();
    }

    /// <summary>
    /// A group's next construction, one at a time: a group that has
    /// outgrown its housing upgrades one of its Tents into a House; one
    /// whose homes are all Houses and full, and whose stores are well
    /// stocked, starts a new home nearby — a village, up to
    /// <see cref="MaxAnnexes"/> homes beyond the first.
    /// </summary>
    private void PlanConstruction(KinGroup group)
    {
        if (group.Home is not { IsBuilt: true } main)
            return;

        int members = group.Members.Count;
        int capacity = HousingCapacity(group);
        if (members > capacity && GroupHomes(group).FirstOrDefault(h => h is { IsBuilt: true, Tier: ShelterTier.Tent }) is { } tent)
        {
            tent.BeginUpgrade();
            tent.StageStartedAt = ElapsedSeconds;
            Game.AddEventLog($"[SETTLE] {group.CapitalTitle} ({members} strong) is upgrading a Tent into a House");
            return;
        }

        if (members >= capacity && group.Annexes.Count < MaxAnnexes && main.Tier == ShelterTier.House &&
            GroupHomes(group).All(h => h.IsBuilt && h.Tier == ShelterTier.House) &&
            StoreFill(group) >= ExpansionStoreFill &&
            TryCreateShelterSite(main.Position, owner: null, groupId: group.Id, searchRadius: AnnexSiteRadius) is { } annex)
        {
            group.Annexes.Add(annex);
            if (group.Annexes.Count == 1)
                VillagesFounded++;
            Game.AddEventLog($"[VILLAGE] {group.CapitalTitle} ({members} strong) is building a {(group.Annexes.Count == 1 ? "second" : "third")} home");
            if (group.Annexes.Count == 1)
                Chronicle($"{group.CapitalTitle} grew into a village of {members}", group);
        }
    }

    /// <summary>Where its partner lives, if that's one of the group's finished homes with room for one more.</summary>
    private Shelter? PartnersHomeWithRoom(KinGroup group, Bramblekin member) =>
        member.Partner is { IsDead: false, Home: { IsBuilt: true } home } && IsGroupHome(group, home) &&
        group.Members.Count(m => m.Home == home) < home.ResidentCapacity
            ? home
            : null;

    /// <summary>The group's finished home with the most free room (its main home if none is finished).</summary>
    private Shelter RoomiestHome(KinGroup group)
    {
        Shelter best = group.Home!;
        int bestRoom = int.MinValue;
        foreach (Shelter home in GroupHomes(group))
        {
            if (!home.IsBuilt)
                continue;
            int room = home.ResidentCapacity - group.Members.Count(m => m.Home == home);
            if (room > bestRoom)
            {
                best = home;
                bestRoom = room;
            }
        }
        return best;
    }

    /// <summary>One grown resident of an overcrowded home moves to a group home with room, if there is one.</summary>
    private void SpreadOutOneResident(KinGroup group)
    {
        if (group.Annexes.Count == 0)
            return;

        foreach (Shelter crowded in GroupHomes(group))
        {
            if (!crowded.IsBuilt || group.Members.Count(m => m.Home == crowded) <= crowded.ResidentCapacity)
                continue;

            Shelter roomy = RoomiestHome(group);
            if (roomy == crowded || group.Members.Count(m => m.Home == roomy) >= roomy.ResidentCapacity)
                return;

            // A single moves out, rather than splitting up a couple.
            if (group.Members.FirstOrDefault(m => m.Home == crowded && m != group.Leader && !m.IsYoung &&
                                                  m.Partner?.Home != crowded) is { } mover)
                mover.SetHome(roomy);
            return;
        }
    }

    /// <summary>
    /// Budding: a village with at least <see cref="BaseBuddingSize"/> members
    /// (more under a Sociable Leader) lets the residents of one of its other
    /// Houses — at least <see cref="MinBuddingResidents"/> grown ones — set
    /// up as a group of their own, keeping that House. Queued, since it adds
    /// a group while the groups are being walked.
    /// </summary>
    private void QueueBudding(KinGroup group)
    {
        if (group.Leader is not { } leader)
            return;

        int buddingSize = BaseBuddingSize + (int)MathF.Round(BuddingSizePerSociability * leader.Personality.Sociability);
        if (group.Members.Count < buddingSize && group.Members.Count < MaxVillageSize)
            return;

        foreach (Shelter annex in group.Annexes)
        {
            if (annex is { IsBuilt: true, Tier: ShelterTier.House, IsUpgrading: false } &&
                group.Members.Count(m => m.Home == annex && m != leader && !m.IsYoung) >= MinBuddingResidents)
            {
                _pendingBuddings.Add((group, annex));
                return;
            }
        }
    }

    private void ProcessBuddings()
    {
        foreach (var (parent, house) in _pendingBuddings)
        {
            if (!_groups.ContainsKey(parent.Id) || house.IsCollapsed || !parent.Annexes.Contains(house))
                continue;

            List<Bramblekin> settlers = parent.Members
                .Where(m => !m.IsDead && m.Home == house && m != parent.Leader && m.Partner != parent.Leader && !m.IsDueling)
                .ToList();
            // Couples stay together: a settler's partner living elsewhere in the village comes too.
            foreach (Bramblekin settler in settlers.ToList())
            {
                if (settler.Partner is { IsDead: false } partner && partner.GroupId == parent.Id &&
                    partner != parent.Leader && !partner.IsDueling && !settlers.Contains(partner))
                {
                    settlers.Add(partner);
                    partner.SetHome(house);
                }
            }
            if (settlers.Count(m => !m.IsYoung) < MinBuddingResidents)
                continue;

            // The settlers leave the House to the old village and head out, with
            // a share of its stores, to found a village of their own on open ground.
            var daughter = new KinGroup(Guid.NewGuid());
            _groups[daughter.Id] = daughter;
            foreach (Bramblekin settler in settlers)
            {
                settler.BudOff(daughter.Id);
                settler.SetHome(null);
                daughter.Members.Add(settler);
            }
            daughter.ElectLeader();
            NameGroup(daughter);
            daughter.SettleTarget = FindOpenGround(house.Position);
            daughter.Culture.CopyFrom(parent.Culture);
            int dowry = Math.Min(MaxDowry, StoredFood(parent) / 3);
            TakeFromStores(parent, dowry, preferred: house);
            daughter.Dowry = dowry;
            SetStance(parent, daughter, GroupStance.Allied); // Kin villages stand together.

            Buddings++;
            QueueFloatingText(house.Position, "New group!", daughter.Color);
            float distance = daughter.SettleTarget is { } target ? GroundMover.HorizontalDistance(house.Position, target) : 0f;
            Game.AddEventLog($"[COLONY] {parent.CapitalTitle} has grown too big: {settlers.Count} of them set out, led by {daughter.Leader!.Name}, " +
                             $"to found {daughter.Title} {distance:0}m away{(dowry > 0 ? $", taking {dowry} food" : "")} - allies of their old village");
            Headline("A new village", $"{settlers.Count} settlers left {parent.Title} to found {daughter.Title}, {distance:0}m away", PlaceOf(parent), false, parent, daughter);
        }
        _pendingBuddings.Clear();
    }

    /// <summary>
    /// A merger: <paramref name="larger"/> takes over <paramref name="smaller"/>'s
    /// homes that stand close to its own as part of its village (up to
    /// <see cref="MaxAnnexes"/>); the rest are left behind.
    /// </summary>
    private void AbsorbHomes(KinGroup larger, KinGroup smaller)
    {
        if (larger.Home is not { IsCollapsed: false } main)
            return; // Homeless: it adopts the best of its new members' homes anyway (see BestMemberHome).

        foreach (Shelter home in GroupHomes(smaller).ToList())
        {
            if (larger.Annexes.Count >= MaxAnnexes)
                break;
            if (GroundMover.HorizontalDistanceSquared(home.Position, main.Position) > AnnexAbsorbRadius * AnnexAbsorbRadius)
                continue;

            larger.Annexes.Add(home);
            home.GroupId = larger.Id;
            HandOverCrops(smaller, larger, home, fromHome: null);
            if (larger.Annexes.Count == 1)
                VillagesFounded++;
        }
        smaller.Home = null;
        smaller.Annexes.Clear();
    }

    // --- Spreading out -------------------------------------------------------------------

    /// <summary>Pioneers look for a spot at least this far (m) from every other home…</summary>
    private const float PioneerSpacing = 25f;

    /// <summary>…but no further than this from where they set out.</summary>
    private const float PioneerMaxTrek = 55f;

    /// <summary>They mark out their site within this many meters of the spot they picked.</summary>
    private const float PioneerSiteRadius = 10f;

    /// <summary>Pioneers weigh every meter to the water's edge (up to 50) this much against open ground and berries.</summary>
    private const float WaterPull = 0.35f;

    /// <summary>A budding village gives its settlers up to this much of its stores.</summary>
    private const int MaxDowry = 6;

    /// <summary>
    /// Spreading out: a spot for a new village — the most open ground
    /// (furthest from every home, up to <see cref="PioneerSpacing"/> and
    /// beyond) within <see cref="PioneerMaxTrek"/> of <paramref name="from"/>,
    /// with a bonus for Berry Patches nearby and for water within easy reach
    /// (see <see cref="WaterPull"/>). Null if the map is too crowded to find
    /// anywhere clear, in which case they settle wherever they find food.
    /// </summary>
    private Vector3? FindOpenGround(Vector3 from)
    {
        Vector3? best = null;
        float bestScore = float.MinValue;
        for (int attempt = 0; attempt < 48; attempt++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float distance = 15f + (float)Rng.NextDouble() * (PioneerMaxTrek - 15f);
            Vector3 candidate = from + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance;
            if (!Terrain.Contains(candidate, 8f) || IsBlocked(candidate, Shelter.HouseRadius + 0.3f))
                continue;

            float nearestHome = PioneerMaxTrek;
            foreach (Shelter shelter in Shelters)
            {
                if (!shelter.IsAbandoned && !shelter.IsCollapsed)
                    nearestHome = MathF.Min(nearestHome, GroundMover.HorizontalDistance(shelter.Position, candidate));
            }
            if (nearestHome < PioneerSpacing * 0.6f)
                continue;

            float nearestPatch = _berryPatches.Count == 0 ? 30f
                : _berryPatches.Min(patch => GroundMover.HorizontalDistance(patch, candidate));
            float toWater = WaterMap.DistanceToWater(candidate.X, candidate.Z);
            float score = MathF.Min(nearestHome, PioneerSpacing * 1.4f) - 0.5f * MathF.Min(nearestPatch, 30f) - 0.1f * distance -
                          WaterPull * MathF.Min(toWater, 50f);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>The best home any member of <paramref name="group"/> already has, if any.</summary>
    private static Shelter? BestMemberHome(KinGroup group)
    {
        Shelter? best = null;
        int bestScore = int.MinValue;
        foreach (Bramblekin member in group.Members)
        {
            if (member.Home is not { IsCollapsed: false } candidate)
                continue;

            int score = (candidate.Tier == ShelterTier.House ? 1000 : 0) + (candidate.IsBuilt ? 100 : 0) + candidate.StoredFood;
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>Moves <paramref name="member"/> into its group's home, carrying over what was in its old store (whatever doesn't fit stays behind).</summary>
    private static void MoveIn(Bramblekin member, Shelter home)
    {
        if (member.Home is { } old && old != home && home.IsBuilt)
        {
            while (old.StoredFood > 0 && !home.StoreIsFull && old.TryWithdraw())
                home.TryDeposit();
        }
        member.SetHome(home);
    }

    /// <summary>Counts, for every shelter, how many of its residents are inside it right now — see <see cref="Shelter.IsOvercrowded"/>.</summary>
    private void CountShelterOccupants()
    {
        foreach (Shelter shelter in Shelters)
            shelter.Occupants = 0;

        foreach (Bramblekin kin in Colony)
        {
            if (!kin.IsDead && kin.IsInsideHome)
                kin.Home!.Occupants++;
        }
    }

    /// <summary>
    /// Grouping for survival: when a struggling loner (hungry, hurt, or
    /// homeless) meets a member of a group that has a finished home, it may
    /// ask to join — more readily the more Sociable it is — and the group
    /// takes it in if there's room and its Leader is welcoming enough (any
    /// Leader takes in newcomers while the group is small).
    /// </summary>
    private bool TryJoinSettledGroup(Bramblekin a, Bramblekin b)
    {
        (Bramblekin? loner, Bramblekin? member) =
            a.GroupId is null && b.GroupId is not null ? (a, b)
            : b.GroupId is null && a.GroupId is not null ? (b, a)
            : (null, null);
        if (loner is null || member is null || GroupOf(member) is not { Home.IsBuilt: true } group)
            return false;

        bool struggling = loner.IsHungry || loner.Health < Bramblekin.MaxHealth * 0.6f || loner.Home is not { IsBuilt: true };
        if (!struggling || group.Members.Count >= GroupSizeLimit(group) || HasEnemyIn(loner, group) || loner.HasLeft(group.Id))
            return false;
        // A persuasive member can talk a wavering loner into joining.
        if (Rng.NextDouble() >= 0.3 + 0.5 * loner.Personality.Sociability + 0.3 * (member.Personality.Persuasiveness - 0.5))
            return false;
        if (group.Members.Count >= 3 && group.Leader is { } leader && Rng.NextDouble() >= 0.3 + 0.7 * leader.Personality.Sociability)
            return false;

        NoteRejoin(loner);
        loner.JoinGroup(group.Id);
        group.Members.Add(loner);
        SetMutualRelationship(loner, member, RelationshipState.Friend);
        AlliancesFormed++;
        QueueFloatingText(loner.Position, "+Joined", group.Color);
        Game.AddEventLog($"[JOIN] {loner.Name} asked to join {group.Title} for its home, and was taken in ({group.Members.Count} strong)");
        return true;
    }
}

/// <summary>
/// A group's homes — its main home, then the rest of its village, skipping
/// any that have collapsed — walked without allocating (the World asks
/// for them many times a step). Still an <see cref="IEnumerable{T}"/> for
/// LINQ, which does allocate.
/// </summary>
public readonly struct VillageHomes : IEnumerable<Shelter>
{
    private readonly KinGroup _group;

    public VillageHomes(KinGroup group) => _group = group;

    public Enumerator GetEnumerator() => new(_group);

    IEnumerator<Shelter> IEnumerable<Shelter>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<Shelter>
    {
        private readonly KinGroup _group;

        /// <summary>-1 before the main home, then the index into its Annexes.</summary>
        private int _next;

        public Enumerator(KinGroup group)
        {
            _group = group;
            _next = -1;
            Current = null!;
        }

        public Shelter Current { get; private set; }

        object System.Collections.IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_next == -1)
            {
                _next = 0;
                if (_group.Home is { IsCollapsed: false } home)
                {
                    Current = home;
                    return true;
                }
            }
            while (_next < _group.Annexes.Count)
            {
                Shelter annex = _group.Annexes[_next++];
                if (!annex.IsCollapsed)
                {
                    Current = annex;
                    return true;
                }
            }
            return false;
        }

        public void Reset() => _next = -1;

        public void Dispose()
        {
        }
    }
}
