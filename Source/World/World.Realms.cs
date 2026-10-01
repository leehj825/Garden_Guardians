using System.Numerics;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Kingdoms: alliances of villages (Garden_Guardians_Society_Design.md, section 4.4) ------------------------------------
    // Three or more allied villages near each other, one of them in the Kingdom Age, are offered a crown: the most populous such village
    // is the capital and its headman is king. Vassal villages keep their headmen, send the capital a little food from what they have in
    // store, and pledge a third of their soldiers to march to any sister village's defence. A village that goes to war with the capital
    // leaves; with fewer than three villages the kingdom dissolves. (The older clan-over-clan vassalage in World.Kingdoms stays for clans
    // outside any village.)

    /// <summary>Villages this near each other (m, middle to middle) can be one kingdom.</summary>
    private const float RealmReach = 90f;

    /// <summary>The fewest villages in a kingdom.</summary>
    private const int RealmMinVillages = 3;

    /// <summary>A vassal village sends a piece of tribute every this many look-overs (about half a minute), when its stores are well above its reserve.</summary>
    private const int TributeEveryLooks = 6;
    private const float TributeStockFactor = 1.5f;

    /// <summary>One soldier in this many of a kingdom's villages is pledged to the realm's defence.</summary>
    private const int PledgeOneIn = 3;

    public List<Kingdom> Realms { get; } = new();

    public int KingdomsFounded { get; private set; }
    public int KingsCrowned { get; private set; }

    /// <summary>The kingdom <paramref name="village"/> belongs to, if any.</summary>
    public Kingdom? RealmOf(Village village) => village.KingdomId is { } id ? Realms.FirstOrDefault(k => k.Id == id) : null;

    public Village? CapitalOf(Kingdom kingdom) => Villages.FirstOrDefault(v => v.Id == kingdom.Capital);

    public Bramblekin? KingOf(Kingdom kingdom) =>
        kingdom.KingId is { } id ? Colony.FirstOrDefault(k => k.ID == id && !k.IsDead) : null;

    /// <summary>The villages of <paramref name="kingdom"/>.</summary>
    public IEnumerable<Village> VillagesOf(Kingdom kingdom) => Villages.Where(v => kingdom.VillageIds.Contains(v.Id));

    private int Population(Village village) => ClansOf(village).Sum(c => c.Members.Count(m => !m.IsDead));

    /// <summary>
    /// Villages that can be one kingdom: at peace with each other. Alliances alone are too rare to bind three villages, so peaceful
    /// neighbours are enough; a war between any two of their clans keeps them apart.
    /// </summary>
    public bool VillagesAllied(Village a, Village b) => !VillagesAtWar(a, b);

    private bool VillagesAtWar(Village a, Village b)
    {
        foreach (Guid x in a.ClanIds)
        {
            foreach (Guid y in b.ClanIds)
            {
                if (AreAtWar(x, y))
                    return true;
            }
        }
        return false;
    }

    public bool InKingdomAge(Village village) => ClansOf(village).Any(c => EraOf(c) >= Era.KingdomAge);

    /// <summary>Called with each look at the villages: kingdoms form, grow, lose members, crown kings, collect tribute and dissolve.</summary>
    private void UpdateRealms()
    {
        // Existing kingdoms: drop villages that are gone or at war with the capital; dissolve below three.
        for (int i = Realms.Count - 1; i >= 0; i--)
        {
            Kingdom kingdom = Realms[i];
            kingdom.VillageIds.RemoveAll(id => Villages.All(v => v.Id != id));
            Village? capital = CapitalOf(kingdom);
            if (capital is not null)
            {
                foreach (Village vassal in VillagesOf(kingdom).Where(v => v != capital && VillagesAtWar(v, capital)).ToList())
                {
                    kingdom.VillageIds.Remove(vassal.Id);
                    vassal.KingdomId = null;
                    Game.AddEventLog($"[KINGDOM] {vassal.Name} breaks from the kingdom of {kingdom.Name}, at war with {capital.Name}");
                    Chronicle($"The village of {vassal.Name} broke from the kingdom of {kingdom.Name}", ClansOf(vassal).ToArray());
                }
            }
            if (kingdom.VillageIds.Count < RealmMinVillages)
            {
                Game.AddEventLog($"[KINGDOM] The kingdom of {kingdom.Name} falls apart");
                Chronicle($"The kingdom of {kingdom.Name} fell apart", VillagesOf(kingdom).SelectMany(ClansOf).ToArray());
                foreach (Village member in VillagesOf(kingdom))
                    member.KingdomId = null;
                Realms.RemoveAt(i);
                continue;
            }
            if (capital is null)
            {
                capital = VillagesOf(kingdom).OrderByDescending(Population).First();
                kingdom.Capital = capital.Id;
                kingdom.KingId = null;
                Game.AddEventLog($"[KINGDOM] {capital.Name} is now the capital of {kingdom.Name}");
            }
            CrownKing(kingdom, capital);
        }

        // Allied villages nearby swear fealty to an existing kingdom.
        foreach (Village village in Villages.Where(v => v.KingdomId is null).ToList())
        {
            foreach (Kingdom kingdom in Realms)
            {
                Village? capital = CapitalOf(kingdom);
                if (capital is null || Vector3.Distance(capital.Centre, village.Centre) > RealmReach || VillagesAtWar(village, capital) ||
                    !VillagesOf(kingdom).Any(m => VillagesAllied(m, village)))
                    continue;
                kingdom.VillageIds.Add(village.Id);
                village.KingdomId = kingdom.Id;
                Game.AddEventLog($"[KINGDOM] {village.Name} swears fealty to {KingOf(kingdom)?.Name ?? "the crown"} of {kingdom.Name}");
                Chronicle($"The village of {village.Name} joined the kingdom of {kingdom.Name}", ClansOf(village).ToArray());
                break;
            }
        }

        // New kingdoms: groups of three or more allied villages near each other, one of them Kingdom Age.
        List<Village> free = Villages.Where(v => v.KingdomId is null && HeadmanOf(v) is not null).ToList();
        var seen = new HashSet<Village>();
        foreach (Village start in free)
        {
            if (!seen.Add(start))
                continue;
            var component = new List<Village> { start };
            for (int i = 0; i < component.Count; i++)
            {
                foreach (Village other in free.Where(o => !seen.Contains(o)).ToList())
                {
                    if (Vector3.Distance(component[i].Centre, other.Centre) <= RealmReach && VillagesAllied(component[i], other) && !VillagesAtWar(component[i], other))
                    {
                        seen.Add(other);
                        component.Add(other);
                    }
                }
            }
            if (component.Count < RealmMinVillages)
                continue;
            Village? capital = component.Where(InKingdomAge).OrderByDescending(Population).FirstOrDefault();
            if (capital is null)
                continue;
            // Only villages that are within reach of the capital come in.
            List<Village> members = component.Where(v => v == capital || Vector3.Distance(v.Centre, capital.Centre) <= RealmReach).ToList();
            if (members.Count < RealmMinVillages)
                continue;
            string name = Names.Kingdom(Rng, n => Realms.Any(k => k.Name == n));
            var kingdom = new Kingdom(Guid.NewGuid(), name, capital.Id, ElapsedSeconds);
            foreach (Village member in members)
            {
                kingdom.VillageIds.Add(member.Id);
                member.KingdomId = kingdom.Id;
            }
            Realms.Add(kingdom);
            KingdomsFounded++;
            KinGroup[] clans = members.SelectMany(ClansOf).ToArray();
            Game.AddEventLog($"[KINGDOM] The kingdom of {name} is founded: {members.Count} villages under {capital.Name}");
            Headline("A kingdom", $"The villages of {string.Join(", ", members.Select(m => m.Name))} unite as the kingdom of {name}, with {capital.Name} for its capital", capital.Centre, false, clans);
            CrownKing(kingdom, capital);
        }

        foreach (Kingdom kingdom in Realms)
            RunRealm(kingdom);
    }

    /// <summary>The capital's headman is king; when the headman changes, so does the king.</summary>
    private void CrownKing(Kingdom kingdom, Village capital)
    {
        if (capital.HeadmanId is not { } headman || kingdom.KingId == headman)
            return;
        Bramblekin? king = HeadmanOf(capital);
        if (king is null)
            return;
        bool first = kingdom.KingId is null;
        kingdom.KingId = headman;
        KingsCrowned++;
        Game.AddEventLog($"[KINGDOM] {king.Name} {(first ? "is crowned" : "becomes king")} of {kingdom.Name}");
        Chronicle($"{king.Name} {(first ? "was crowned" : "became king")} of the kingdom of {kingdom.Name}", ClansOf(capital).ToArray());
    }

    /// <summary>Tribute from the vassals to the capital's stores, and the count of pledged soldiers.</summary>
    private void RunRealm(Kingdom kingdom)
    {
        Village? capital = CapitalOf(kingdom);
        if (capital is null)
            return;
        kingdom.Looks++;
        int pledged = 0;
        foreach (Village village in VillagesOf(kingdom))
        {
            pledged += ClansOf(village).SelectMany(c => c.Members).Count(m => !m.IsDead && IsPledged(m));
            if (village == capital || kingdom.Looks % TributeEveryLooks != 0)
                continue;
            (int stock, _, float reserve) = VillageFood(village);
            if (stock <= reserve * TributeStockFactor)
                continue;
            Shelter? from = ClansOf(village).SelectMany(c => GroupHomes(c)).Where(h => h.StoredFood > 0).OrderByDescending(h => h.StoredFood).FirstOrDefault();
            Shelter? to = ClansOf(capital).SelectMany(c => GroupHomes(c)).Where(h => !h.StoreIsFull).OrderBy(h => h.StoredFood).FirstOrDefault();
            if (from is null || to is null || !from.TryWithdraw())
                continue;
            if (to.TryDeposit())
                kingdom.TributePaid++;
            else
                from.TryDeposit();
        }
        kingdom.Pledged = pledged;
    }

    /// <summary>A soldier of a kingdom's village, one in <see cref="PledgeOneIn"/>, is sworn to defend its sister villages too.</summary>
    public bool IsPledged(Bramblekin soldier) =>
        soldier.Job == KinJob.Guard && soldier.ID % PledgeOneIn == 0 && GroupOf(soldier) is { } clan && VillageOf(clan)?.KingdomId is not null;

    /// <summary>The threat a pledged soldier of <paramref name="village"/> should march to: an alarm at a sister village of its kingdom. Null if none.</summary>
    public ICombatant? RealmAlarmFor(Village village)
    {
        if (RealmOf(village) is not { } kingdom)
            return null;
        foreach (Village sister in VillagesOf(kingdom))
        {
            if (sister == village)
                continue;
            foreach (KinGroup clan in ClansOf(sister))
            {
                if (clan.DefendTarget is { IsDead: false } threat &&
                    GroupHomes(clan).Any(h => GroundMover.HorizontalDistanceSquared(threat.Position, h.Position) <= HomeDefenseRadius * HomeDefenseRadius * 1.5f))
                    return threat;
            }
        }
        return null;
    }

    /// <summary>"Kingdom of Thornreach, capital Mossbridge, king Tibo, 4 villages".</summary>
    public string? DescribeRealm(Village village)
    {
        if (RealmOf(village) is not { } kingdom)
            return null;
        string king = KingOf(kingdom) is { } k ? k.Name : "no king";
        string role = village.Id == kingdom.Capital ? "its capital" : $"vassal of {CapitalOf(kingdom)?.Name ?? "the crown"}";
        return $"kingdom of {kingdom.Name}: {role}, king {king}, {kingdom.VillageIds.Count} villages, {kingdom.Pledged} soldiers pledged, {kingdom.TributePaid} tribute sent";
    }
}
