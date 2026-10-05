using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Villages ---------------------------------------------------------------------------
    // Homes standing close together — of one clan or several — are a village: it has a name of its own, a middle, and a headman chosen from
    // its clans' chiefs. It is worked out from where the homes stand (see Garden_Guardians_Society_Design.md, section 4.3), so it forms,
    // grows, merges and dissolves with them. The technology cap: at least one of its clans must have reached the Farming Age.

    /// <summary>Homes this near each other (m) are part of the same village.</summary>
    private const float VillageLink = 14f;

    /// <summary>How often (s) the villages are looked over.</summary>
    private const float VillageCheckInterval = 5f;

    /// <summary>A chief's claim to be headman grows by this for each clan member (support at home) and by this for each fellow chief who counts them a friend.</summary>
    private const float HeadmanSupportPerMember = 0.02f, HeadmanFriendVote = 0.15f;

    /// <summary>A village whose homes fall short (one pulled down for an upgrade, a clan moving house) is kept this long (s) before it is abandoned.</summary>
    private const float VillageGraceSeconds = 120f;

    private float _villageTimer;

    public List<Village> Villages { get; } = new();

    /// <summary>Villages ever founded (the real thing, not clans that merely budded a second home), for the report.</summary>
    public int VillagesNamed { get; private set; }

    public int HeadmenChosen { get; private set; }

    /// <summary>The village <paramref name="group"/> belongs to, if any.</summary>
    public Village? VillageOf(KinGroup group)
    {
        if (group.VillageId is not { } id)
            return null;
        foreach (Village v in Villages) // (a plain loop: soldiers ask every step)
        {
            if (v.Id == id)
                return v;
        }
        return null;
    }

    /// <summary>The Bramblekin leading <paramref name="village"/>, if it has one alive.</summary>
    public Bramblekin? HeadmanOf(Village village) =>
        village.HeadmanId is { } id ? Colony.FirstOrDefault(k => k.ID == id && !k.IsDead) : null;

    /// <summary>The clans of <paramref name="village"/>.</summary>
    public IEnumerable<KinGroup> ClansOf(Village village)
    {
        foreach (Guid id in village.ClanIds)
        {
            if (_groups.TryGetValue(id, out KinGroup? clan))
                yield return clan;
        }
    }

    private void UpdateVillages(float deltaTime)
    {
        _villageTimer -= deltaTime;
        if (_villageTimer > 0f)
            return;
        _villageTimer = VillageCheckInterval;

        // Every standing home of a clan that exists, joined to each home within reach (not across a war).
        var homes = Shelters.Where(s => s is { IsBuilt: true, IsCollapsed: false, GroupId: not null } && _groups.ContainsKey(s.GroupId!.Value)).ToList();
        var parent = Enumerable.Range(0, homes.Count).ToArray();
        int Find(int a)
        {
            while (parent[a] != a)
                a = parent[a] = parent[parent[a]];
            return a;
        }
        for (int i = 0; i < homes.Count; i++)
        {
            for (int j = i + 1; j < homes.Count; j++)
            {
                if (GroundMover.HorizontalDistance(homes[i].Position, homes[j].Position) <= VillageLink &&
                    (homes[i].GroupId == homes[j].GroupId || !AreAtWar(homes[i].GroupId, homes[j].GroupId)))
                    parent[Find(i)] = Find(j);
            }
        }

        // A clan belongs to one village only: the one its main home stands in (else where most of its homes are). Its homes elsewhere still
        // count there as homes, but the clan is not counted twice.
        int[] root = Enumerable.Range(0, homes.Count).Select(Find).ToArray();
        var primary = new Dictionary<Guid, int>();
        foreach (IGrouping<Guid, int> byClan in Enumerable.Range(0, homes.Count).GroupBy(i => homes[i].GroupId!.Value))
        {
            KinGroup clan = _groups[byClan.Key];
            int main = clan.Home is { } h ? homes.IndexOf(h) : -1;
            primary[byClan.Key] = main >= 0 ? root[main] : byClan.GroupBy(i => root[i]).OrderByDescending(g => g.Count()).First().Key;
        }

        // The clusters of two or more homes, with a clan farming, are the villages there should be.
        var clusters = new List<(List<Shelter> Homes, List<Guid> Clans)>();
        foreach (var members in Enumerable.Range(0, homes.Count).GroupBy(i => root[i]))
        {
            List<Guid> clans = primary.Where(p => p.Value == members.Key).Select(p => p.Key).OrderBy(id => id).ToList();
            List<Shelter> cluster = members.Select(i => homes[i]).Where(h => clans.Contains(h.GroupId!.Value)).ToList();
            if (cluster.Count < 2 || !clans.Any(id => EraOf(_groups[id]) >= Era.FarmingAge))
                continue;
            clusters.Add((cluster, clans));
        }

        var kept = new HashSet<Village>();
        foreach (var (cluster, clans) in clusters)
        {
            // It is the village that already has one of these clans (the oldest, if several now join up).
            List<Village> found = Villages.Where(v => v.ClanIds.Any(clans.Contains) && !kept.Contains(v)).OrderBy(v => v.FoundedAt).ToList();
            Village village;
            Vector3 centre = Grounded(new Vector3(cluster.Average(h => h.Position.X), 0f, cluster.Average(h => h.Position.Z)));
            if (found.Count == 0)
            {
                string name = Names.Village(Rng, n => Villages.Any(v => v.Name == n));
                village = new Village(Guid.NewGuid(), name, centre, ElapsedSeconds);
                Villages.Add(village);
                VillagesNamed++;
                string who = string.Join(" and ", clans.Select(id => _groups[id].Title));
                Game.AddEventLog($"[VILLAGE] {name} is founded: {cluster.Count} homes of {who}");
                Headline("A village", $"{name} is founded, where {who} live side by side", centre, false, clans.Select(id => _groups[id]).ToArray());
                foreach (KinGroup clan in clans.Select(id => _groups[id]))
                    Chronicle($"{clan.CapitalTitle} became part of the village of {name}", clan);
            }
            else
            {
                village = found[0];
                for (int k = 1; k < found.Count; k++)
                {
                    Game.AddEventLog($"[VILLAGE] {found[k].Name} joins {village.Name}");
                    Villages.Remove(found[k]);
                }
            }
            kept.Add(village);
            village.LapsedAt = null;
            village.Centre = centre;
            village.Homes = cluster.Count;
            village.PatrolRadius = MathF.Max(6f, cluster.Max(h => GroundMover.HorizontalDistance(h.Position, centre) + h.PalisadeRadius) + 1.5f);
            if (!village.ClanIds.OrderBy(id => id).SequenceEqual(clans))
            {
                foreach (Guid joined in clans.Except(village.ClanIds))
                {
                    if (village.ClanIds.Count > 0 && _groups.TryGetValue(joined, out KinGroup? newcomer))
                        Chronicle($"{newcomer.CapitalTitle} joined the village of {village.Name}", newcomer);
                }
                village.ClanIds.Clear();
                village.ClanIds.AddRange(clans);
            }
        }

        // Villages whose homes are gone (or too few) fade away.
        for (int i = Villages.Count - 1; i >= 0; i--)
        {
            if (kept.Contains(Villages[i]))
                continue;
            Villages[i].LapsedAt ??= ElapsedSeconds;
            if (ElapsedSeconds - Villages[i].LapsedAt < VillageGraceSeconds && ClansOf(Villages[i]).Any())
                continue; // Not yet: the homes may be back.
            Game.AddEventLog($"[VILLAGE] {Villages[i].Name} is abandoned");
            Chronicle($"The village of {Villages[i].Name} was abandoned", ClansOf(Villages[i]).ToArray());
            Villages.RemoveAt(i);
        }

        foreach (KinGroup clan in _groups.Values)
            clan.VillageId = null;
        foreach (Village village in Villages)
        {
            foreach (KinGroup clan in ClansOf(village))
                clan.VillageId = village.Id;
            ChooseHeadman(village);
            UpdateRations(village);
        }
        ClearRationsOutsideVillages();
        UpdateRealms();
    }

    /// <summary>
    /// The headman is one of the village's clans' chiefs. He keeps the post while he is alive and still a chief there; otherwise the chiefs
    /// choose: the best claim to lead (see <see cref="Bramblekin.LeadershipScore"/>), plus the size of a chief's clan behind them, plus
    /// the other chiefs who call them a friend.
    /// </summary>
    private void ChooseHeadman(Village village)
    {
        List<Bramblekin> chiefs = ClansOf(village).Select(c => c.Leader).Where(l => l is { IsDead: false }).Select(l => l!).ToList();
        if (chiefs.Count == 0)
        {
            village.HeadmanId = null;
            village.HeadmanClan = null;
            return;
        }
        if (village.HeadmanId is { } current && chiefs.Any(c => c.ID == current))
            return;

        float Claim(Bramblekin chief) =>
            chief.LeadershipScore + HeadmanSupportPerMember * (GroupOf(chief)?.Members.Count ?? 0) +
            HeadmanFriendVote * chiefs.Count(other => other != chief && other.RelationshipTo(chief) == RelationshipState.Friend);

        Bramblekin headman = chiefs.OrderByDescending(Claim).ThenBy(c => c.ID).First();
        bool first = village.HeadmanId is null;
        village.HeadmanId = headman.ID;
        village.HeadmanClan = headman.GroupId;
        HeadmenChosen++;
        Game.AddEventLog($"[VILLAGE] {headman.Name} {(first ? "is chosen" : "becomes")} headman of {village.Name}");
        Chronicle($"{headman.Name} {(first ? "was chosen" : "became")} headman of the village of {village.Name}", ClansOf(village).ToArray());
    }

    /// <summary>Waypoints on a soldier's round of a village.</summary>
    public const int PatrolPoints = 8;

    /// <summary>Waypoint <paramref name="index"/> of <paramref name="village"/>'s round: on a ring past its homes, moved in (or round) if something stands there.</summary>
    public Vector3 PatrolWaypoint(Village village, int index)
    {
        float angle = index * MathF.Tau / PatrolPoints + (village.FoundedAt * 0.37f) % MathF.Tau;
        for (float r = village.PatrolRadius; r > 3f; r -= 1f)
        {
            Vector3 p = Grounded(village.Centre + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * r);
            if (Terrain.Contains(p, 2f) && !IsBlockedOrAntZone(p, 0.4f))
                return p;
        }
        return village.Centre;
    }

    /// <summary>
    /// The threat a clan's soldiers should go for: its own alarm (a threat near its home) or — in a village — any of the village's clans'
    /// alarms near any of the village's homes. Null if none.
    /// </summary>
    public ICombatant? AlarmFor(KinGroup group, Village? village)
    {
        if (group.DefendTarget is { IsDead: false } own && group.Home is { } home &&
            GroundMover.HorizontalDistanceSquared(own.Position, home.Position) <= HomeDefenseRadius * HomeDefenseRadius * 1.5f)
            return own;
        if (village is null)
            return null;
        foreach (Guid clanId in village.ClanIds)
        {
            if (!_groups.TryGetValue(clanId, out KinGroup? clan) || clan.DefendTarget is not { IsDead: false } shared)
                continue;
            foreach (Shelter h in GroupHomes(clan))
            {
                if (GroundMover.HorizontalDistanceSquared(shared.Position, h.Position) <= HomeDefenseRadius * HomeDefenseRadius * 1.5f)
                    return shared;
            }
        }
        return null;
    }

    /// <summary>A line about the village for the clan card: "Mossbridge (3 homes, headman Tibo)".</summary>
    public string? DescribeVillage(KinGroup group)
    {
        if (VillageOf(group) is not { } village)
            return null;
        string headman = HeadmanOf(village) is { } h ? h.Name : "no headman";
        return $"{village.Name} ({village.Homes} homes, {village.ClanIds.Count} {(village.ClanIds.Count == 1 ? "clan" : "clans")}, headman {headman}, store {village.Stock}, {village.Soldiers} soldiers, {village.Builders} builders, {village.Healers} healers, {village.Scouts} scouts; {village.Paid} of {village.AllowedPaid} fed{(DescribeRealm(village) is { } realm ? $"; {realm}" : "")})";
    }
}
