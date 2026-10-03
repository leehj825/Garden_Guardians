using System.Numerics;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Rations: a village feeds its soldiers (Garden_Guardians_Society_Design.md, section 5) ---------------------------------
    // A kin whose job is fighting cannot also be out foraging. A village's soldiers are fed from its stores — every home of its clans —
    // so a Swordsman eats there and goes back to its post. It only pays what it earns: how many soldiers it can feed comes from what its
    // people have lately been bringing in (and any stock above a reserve). If the stores fall to the reserve, rations stop and the
    // soldiers forage like everyone else.

    /// <summary>Half of what a village takes in may go to feeding its soldiers.</summary>
    private const float RationShare = 0.5f;

    /// <summary>Stock above the reserve can feed a soldier for this long (s).</summary>
    private const float RationHorizonSeconds = 900f;

    /// <summary>A village keeps this many pieces of food in store for each of its people before it pays anyone from them.</summary>
    private const float ReservePerPerson = 0.7f;

    /// <summary>How quickly the income figure follows what came in (per look).</summary>
    private const float IncomeSmoothing = 0.05f;

    /// <summary>A testing aid: with rations off a village's soldiers fend for themselves, as before.</summary>
    public static bool RationsEnabled { get; set; } = true;

    /// <summary>A testing aid: with the village jobs off there is no garrison, no Fishers and no patrol (the jobs are as before step 3).</summary>
    public static bool SocietyJobsEnabled { get; set; } = true;

    private readonly Dictionary<Guid, int> _depositsByClan = new();

    /// <summary>Meals paid soldiers have eaten from village stores, for the report.</summary>
    public int RationMeals { get; private set; }

    /// <summary>Called whenever food goes into a home's store: the village's income.</summary>
    private void NoteDeposited(Shelter shelter)
    {
        if (shelter.GroupId is { } id)
            _depositsByClan[id] = _depositsByClan.GetValueOrDefault(id) + 1;
    }

    /// <summary>The pieces of food a village's people have in store, and the reserve it will not pay out below.</summary>
    private (int Stock, int People, float Reserve) VillageFood(Village village)
    {
        int stock = 0, people = 0;
        foreach (KinGroup clan in ClansOf(village))
        {
            people += clan.Members.Count(m => !m.IsDead);
            foreach (Shelter home in GroupHomes(clan))
                stock += home.StoredFood;
        }
        return (stock, people, people * ReservePerPerson);
    }

    /// <summary>Every look: the village's stock and income, how many soldiers that feeds, and which of its Swordsmen are the ones fed.</summary>
    private void UpdateRations(Village village)
    {
        (int stock, _, float reserve) = VillageFood(village);
        village.Stock = stock;

        float deposited = 0f;
        foreach (Guid id in village.ClanIds)
            deposited += _depositsByClan.GetValueOrDefault(id);
        village.IncomeRate += (deposited / VillageCheckInterval - village.IncomeRate) * IncomeSmoothing;

        float fromIncome = village.IncomeRate * RationShare / Bramblekin.MealsPerSecond;
        float fromStock = MathF.Max(0f, stock - reserve) / (Bramblekin.MealsPerSecond * RationHorizonSeconds);
        village.AllowedPaid = RationsEnabled ? (int)MathF.Floor(fromIncome + fromStock) : 0;

        UpdateVillageJobs(village);

        // The fed are the first few of those with a village job, soldiers first.
        KinJob[] priority = { KinJob.Swordsman, KinJob.Healer, KinJob.Builder, KinJob.Scout };
        var holders = ClansOf(village).SelectMany(c => c.Members)
            .Where(m => !m.IsDead && !m.IsYoung && m.VillageJob != KinJob.None)
            .OrderBy(m => Array.IndexOf(priority, m.VillageJob)).ThenBy(m => m.ID).ToList();
        village.Paid = Math.Min(holders.Count, village.AllowedPaid);
        for (int i = 0; i < holders.Count; i++)
            holders[i].IsPaid = i < village.AllowedPaid;
    }

    /// <summary>VillageJobs are on: the headman gives jobs out to the clans' people (a testing aid; off, the clans choose as before step 4).</summary>
    public static bool VillageJobsEnabled { get; set; } = true;

    /// <summary>
    /// The headman sets the village's jobs, from the food it can feed people on (<see cref="Village.AllowedPaid"/>): most to soldiers, then
    /// a healer while someone needs care, builders while there is building (a wall, a home), a scout while there is ground to map.
    /// He picks the best of any clan's people for each, keeps those who are doing well, and gives up the rest. With no headman, no one is given orders.
    /// </summary>
    private void UpdateVillageJobs(Village village)
    {
        List<KinGroup> clans = ClansOf(village).ToList();
        List<Bramblekin> adults = clans.SelectMany(c => c.Members).Where(m => !m.IsDead && !m.IsYoung).ToList();
        Bramblekin? headman = HeadmanOf(village);
        if (!VillageJobsEnabled || headman is null || !SocietyJobsEnabled)
        {
            foreach (Bramblekin kin in adults)
                kin.VillageJob = KinJob.None;
            village.Soldiers = village.Builders = village.Healers = village.Scouts = 0;
            return;
        }

        int slots = village.AllowedPaid;
        int soldiers = slots > 0 ? Math.Min(Math.Max(1, (int)MathF.Ceiling(slots * 0.6f)), Math.Max(1, adults.Count * 2 / 5)) : 0;
        int left = Math.Max(0, slots - soldiers);
        int healers = left > 0 && clans.Any(c => Knows(c, Craft.Herbalism)) && adults.Any(m => m.NeedsCare) ? 1 : 0;
        left -= healers;
        bool building = WallPieces.Any(p => !p.IsBuilt && p.GroupId is { } g && village.ClanIds.Contains(g)) ||
                        clans.Any(c => GroupHomes(c).Any(h => !h.IsBuilt || h.NeedsTwigs));
        int builders = left > 0 && building ? Math.Min(2, left) : 0;
        left -= builders;
        int scouts = left > 0 && clans.Any(c => Knows(c, Craft.Exploration)) ? 1 : 0;

        float Score(Bramblekin m, KinJob job) => job switch
        {
            KinJob.Swordsman => m.Personality.Courage + 0.5f * m.Personality.Aggression + 0.5f * m.Personality.Strength,
            KinJob.Healer => m.Personality.Intelligence + m.Personality.Sociability + m.SkillAt(Skill.Healing),
            KinJob.Builder => m.Personality.Intelligence + m.Personality.Diligence + m.SkillAt(Skill.Building),
            _ => m.Personality.Courage + m.Personality.Intelligence,
        };

        foreach ((KinJob job, int want) in new[] { (KinJob.Swordsman, soldiers), (KinJob.Healer, healers), (KinJob.Builder, builders), (KinJob.Scout, scouts) })
        {
            List<Bramblekin> have = adults.Where(m => m.VillageJob == job).ToList();
            foreach (Bramblekin extra in have.OrderBy(m => Score(m, job)).Take(Math.Max(0, have.Count - want)))
                extra.VillageJob = KinJob.None; // Too many (or someone fell sick): the lowest are let go.
            foreach (Bramblekin gone in have.Where(m => m.Health <= Bramblekin.MaxHealth / 2 || m == headman))
                gone.VillageJob = KinJob.None;
            int missing = want - adults.Count(m => m.VillageJob == job);
            if (missing <= 0)
                continue;
            foreach (Bramblekin pick in adults.Where(m => m.VillageJob == KinJob.None && m != headman && m.Job is not (KinJob.Farmer or KinJob.Fisher) &&
                                                          m.Health > Bramblekin.MaxHealth / 2 && !m.IsSick)
                         .OrderByDescending(m => Score(m, job)).Take(missing))
                pick.VillageJob = job;
        }

        village.Soldiers = adults.Count(m => m.VillageJob == KinJob.Swordsman);
        village.Builders = adults.Count(m => m.VillageJob == KinJob.Builder);
        village.Healers = adults.Count(m => m.VillageJob == KinJob.Healer);
        village.Scouts = adults.Count(m => m.VillageJob == KinJob.Scout);
    }

    /// <summary>Clears the deposit counts for the next look, and un-pays anyone whose village is gone or whose job changed.</summary>
    private void ClearRationsOutsideVillages()
    {
        _depositsByClan.Clear();
        foreach (Bramblekin kin in Colony)
        {
            bool inVillage = !kin.IsDead && kin.GroupId is { } id && _groups.TryGetValue(id, out KinGroup? clan) && VillageOf(clan) is not null;
            if (!inVillage)
            {
                kin.IsPaid = false;
                kin.VillageJob = KinJob.None;
            }
            else if (kin.VillageJob == KinJob.None)
                kin.IsPaid = false;
        }
    }

    /// <summary>The home a paid soldier eats at: the nearest in its village with food in it, while the village's stores are above the reserve. Null if none (then it forages).</summary>
    public Shelter? RationStoreFor(Bramblekin kin)
    {
        if (!kin.IsPaid || GroupOf(kin) is not { } clan || VillageOf(clan) is not { } village)
            return null;
        (int stock, _, float reserve) = VillageFood(village);
        if (stock <= reserve)
            return null;

        Shelter? best = null;
        float nearest = float.MaxValue;
        foreach (KinGroup member in ClansOf(village))
        {
            foreach (Shelter home in GroupHomes(member))
            {
                if (!home.IsBuilt || home.IsCollapsed || home.StoredFood <= 0)
                    continue;
                float d = GroundMover.HorizontalDistanceSquared(kin.Position, home.Position);
                if (d < nearest)
                {
                    nearest = d;
                    best = home;
                }
            }
        }
        return best;
    }

    /// <summary>A paid soldier ate at a village store.</summary>
    public void NoteRation(Bramblekin kin)
    {
        RationMeals++;
        if (GroupOf(kin) is { } clan && VillageOf(clan) is { } village)
            village.RationMeals++;
    }
}
