using System.Numerics;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Rations: a village feeds its soldiers (Garden_Guardians_Society_Design.md, section 5) ---------------------------------
    // A kin whose job is fighting cannot also be out foraging. A village's soldiers are fed from its stores — every home of its clans —
    // so a Guard eats there and goes back to its post. It only pays what it earns: how many soldiers it can feed comes from what its
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

    /// <summary>Every look: the village's stock and income, how many soldiers that feeds, and which of its Guards are the ones fed.</summary>
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

        // Its Guards, in a steady order: the first few it can afford are paid.
        var guards = ClansOf(village).SelectMany(c => c.Members).Where(m => !m.IsDead && !m.IsYoung && m.Job == KinJob.Guard).OrderBy(m => m.ID).ToList();
        village.Paid = Math.Min(guards.Count, village.AllowedPaid);
        for (int i = 0; i < guards.Count; i++)
            guards[i].IsPaid = i < village.AllowedPaid;
    }

    /// <summary>Clears the deposit counts for the next look, and un-pays anyone whose village is gone or whose job changed.</summary>
    private void ClearRationsOutsideVillages()
    {
        _depositsByClan.Clear();
        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsPaid && (kin.IsDead || kin.Job != KinJob.Guard || kin.GroupId is not { } id || !_groups.TryGetValue(id, out KinGroup? clan) || VillageOf(clan) is null))
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
