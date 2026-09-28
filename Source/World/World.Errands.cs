using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    private static readonly Color AidTextColor = new(70, 160, 110, 255);

    /// <summary>At most this many aid runners on the road from one group to one ally at a time.</summary>
    private const int MaxAidRunners = 3;

    /// <summary>Food delivered by aid carriers (see <see cref="DeliverAid"/>).</summary>
    public int AidDelivered { get; private set; }

    /// <summary>Errand sacks lost on the way — the carrier killed, or cut off from its group.</summary>
    public int ErrandsLost { get; private set; }

    /// <summary>
    /// A fit, fed grown member of <paramref name="group"/> free to go on an
    /// errand — not the Leader, not already on one — the most Sociable
    /// first. Null if nobody can be spared.
    /// </summary>
    private static Bramblekin? PickErrandRunner(KinGroup group) =>
        group.Members
            .Where(m => !m.IsDead && !m.IsYoung && !m.IsElder && m != group.Leader && m.Errand is null && !m.IsDueling &&
                        !m.IsHungry && m.Health >= Bramblekin.MaxHealth * 0.7f && m.Home is { IsBuilt: true })
            .MaxBy(m => m.Personality.Sociability);

    /// <summary>
    /// Aid in person: sends a runner from <paramref name="giver"/> with up
    /// to <see cref="AidAmount"/> food in a sack, to walk it to its allies'
    /// home. Returns false if nobody could be spared (or one is already on
    /// the way).
    /// </summary>
    private bool DispatchAid(KinGroup giver, KinGroup ally, Shelter allyHome)
    {
        if (Colony.Count(k => k.Errand is { Kind: ErrandKind.Aid } e && e.From == giver.Id && e.To == ally.Id) >= MaxAidRunners)
            return false;
        if (SendRunner(ErrandKind.Aid, giver, ally, allyHome, AidAmount) is not { } runner)
            return false;
        Game.AddEventLog($"[AID] {runner.Name} set out from {giver.Title} with {runner.SackLoad} food for their hungry allies, {ally.Title}");
        return true;
    }

    /// <summary>Sends a runner from <paramref name="from"/> with up to <paramref name="amount"/> food from its stores, to <paramref name="destination"/>. Null if nobody could be spared, or there's no food.</summary>
    private Bramblekin? SendRunner(ErrandKind kind, KinGroup from, KinGroup to, Shelter destination, int amount)
    {
        if (PickErrandRunner(from) is not { } runner)
            return null;
        int load = Math.Min(amount, StoredFood(from));
        if (load <= 0)
            return null;
        TakeFromStores(from, load, preferred: runner.Home);
        runner.GiveErrand(new Errand { Kind = kind, From = from.Id, To = to.Id, Destination = destination, Load = load });
        QueueFloatingText(runner.Position, kind == ErrandKind.Tribute ? $"Tribute x{load}" : $"Aid x{load}", AidTextColor);
        return runner;
    }

    /// <summary>An aid runner arrived: its sack goes into the allies' stores (anything that won't fit is left at the door).</summary>
    public void DeliverAid(Bramblekin runner, Errand errand)
    {
        int delivered = errand.Load;
        if (_groups.TryGetValue(errand.To, out KinGroup? ally))
        {
            foreach (Shelter home in GroupHomes(ally).Prepend(errand.Destination).Distinct())
            {
                while (errand.Load > 0 && home.TryDeposit())
                    errand.Load--;
            }
        }
        if (errand.Load > 0)
            ScatterFoodAround(errand.Destination.Position, errand.Load, errand.Destination.Radius + 0.4f, FoodShardKind.Berry);

        if (errand.Kind == ErrandKind.Tribute)
        {
            TributeDelivered += delivered;
            QueueFloatingText(errand.Destination.Position, $"Tribute +{delivered}", HostileTextColor);
            Game.AddEventLog($"[TRIBUTE] {runner.Name} brought {delivered} food in tribute to {(ally?.Title ?? "the victors")}");
        }
        else
        {
            AidDelivered += delivered;
            FoodAided += delivered;
            QueueFloatingText(errand.Destination.Position, $"Aid +{delivered}", AidTextColor);
            Game.AddEventLog($"[AID] {runner.Name} delivered {delivered} food to {(ally?.Title ?? "its allies")}");
        }
        errand.Load = 0;
        runner.EndErrand();
    }

    /// <summary>A helper's work is done: its pay (as much as its employers can spare) goes in its sack for the trip home.</summary>
    public void PayForLabour(Bramblekin helper, Errand errand)
    {
        if (_groups.TryGetValue(errand.To, out KinGroup? employers))
        {
            int pay = Math.Min(errand.Payment, StoredFood(employers));
            TakeFromStores(employers, pay, preferred: errand.Destination);
            errand.Load = pay;
            LabourFoodPaid += pay;
            Game.AddEventLog(pay > 0
                ? $"[TRADE] {helper.Name} finished helping {employers.Title} build, and heads home with {pay} food"
                : $"[TRADE] {helper.Name} finished helping {employers.Title} build, but they had nothing to pay with");
            if (pay < errand.Payment)
                AddGrievance(errand.From, errand.To, 0.5f * (errand.Payment - pay));
        }
        errand.Returning = true;
    }

    /// <summary>Home again: the sack goes into its own group's stores (anything that won't fit is left at the door).</summary>
    public void UnpackErrand(Bramblekin runner, Errand errand)
    {
        if (runner.Home is { } home && GroupOf(runner) is { } group)
        {
            foreach (Shelter store in GroupHomes(group).Prepend(home).Distinct())
            {
                while (errand.Load > 0 && store.TryDeposit())
                    errand.Load--;
            }
        }
        if (errand.Load > 0 && runner.Home is { } door)
            ScatterFoodAround(door.Position, errand.Load, door.Radius + 0.4f, FoodShardKind.Berry);
        errand.Load = 0;
        runner.EndErrand();
    }

    // --- Trade: labour for food ---------------------------------------------------------

    /// <summary>A hired helper fetches this many twigs for its employers…</summary>
    private const int LabourTwigs = 3;

    /// <summary>…and is paid this much food for them.</summary>
    private const int LabourPay = 3;

    /// <summary>Helpers hired from an ally.</summary>
    public int HelpersHired { get; private set; }

    /// <summary>Food paid to helpers and carried home.</summary>
    public int LabourFoodPaid { get; private set; }

    /// <summary>
    /// Trade between allies — labour for food. A group with building under
    /// way and food to spare (see <see cref="LabourPay"/>) hires a helper
    /// from an ally whose stores are running low: the helper walks over,
    /// fetches <see cref="LabourTwigs"/> twigs for the construction, and
    /// carries its pay home to its own village. One helper at a time;
    /// likelier under a Sociable Leader.
    /// </summary>
    private void TryHireHelper(KinGroup employers, KinGroup ally)
    {
        if (employers.ConstructionSite is not { } site || employers.Leader is not { } leader)
            return;
        if (StoredFood(employers) < LabourPay + 2 || StoreFill(ally) >= 0.5f)
            return;
        if (Colony.Any(k => k.Errand is { Kind: ErrandKind.Labour } e && e.To == employers.Id))
            return;
        if (Rng.NextDouble() >= 0.3 * leader.Personality.Sociability)
            return;
        if (PickErrandRunner(ally) is not { } helper)
            return;

        helper.GiveErrand(new Errand
        {
            Kind = ErrandKind.Labour, From = ally.Id, To = employers.Id, Destination = site,
            TwigsOwed = LabourTwigs, Payment = LabourPay,
        });
        HelpersHired++;
        QueueFloatingText(helper.Position, "Off to help", AidTextColor);
        Game.AddEventLog($"[TRADE] {helper.Name} of {ally.Title} went to help their allies, {employers.Title}, build - {LabourTwigs} twigs for {LabourPay} food");
    }

    // --- Trade: stones and branches for food ------------------------------------------------

    /// <summary>A stone or branch hauled to an ally costs it this much food.</summary>
    private const int HaulPrice = 2;

    /// <summary>A clan with no loose stone (or branch) within this far (m) of its main home would sooner buy one…</summary>
    private const float FarToFetch = 20f;

    /// <summary>…from an ally with one lying within this far (m) of its own.</summary>
    private const float HaulPickupReach = 18f;

    /// <summary>Stones and branches hauled to an ally, and the food paid for them.</summary>
    public int MaterialsTraded { get; private set; }

    public int HaulFoodPaid { get; private set; }

    /// <summary>A group by its id, if it still exists.</summary>
    public KinGroup? GroupWithId(Guid id) => _groups.TryGetValue(id, out KinGroup? group) ? group : null;

    /// <summary>
    /// Trade between allies — stones and branches for food. A clan that
    /// needs stones (for its well or a footing) or branches (for a palisade)
    /// with none lying within <see cref="FarToFetch"/> of home, and food to
    /// spare, buys one from an ally that doesn't need that kind itself and
    /// has one lying close to its own home: the ally sends a hauler with it,
    /// and the hauler carries <see cref="HaulPrice"/> food home. So a clan by
    /// the rocks comes to supply stone, and one by the oak, wood.
    /// </summary>
    private void TryTradeMaterial(KinGroup buyers, KinGroup sellers)
    {
        if (buyers.Home is not { IsBuilt: true, IsCollapsed: false } buyersHome || sellers.Home is not { IsBuilt: true, IsCollapsed: false } sellersHome)
            return;
        if (StoredFood(buyers) < HaulPrice + 2 || Colony.Any(k => k.Errand is { Kind: ErrandKind.Haul } e && e.To == buyers.Id))
            return;
        foreach (MaterialKind kind in Enum.GetValues<MaterialKind>())
        {
            if (MaterialTarget(buyers, kind, buyersHome.Position) is null || MaterialTarget(sellers, kind, sellersHome.Position) is not null)
                continue;
            if (NearestMaterial(buyersHome.Position, kind, FarToFetch, claimant: null) is not null ||
                NearestMaterial(sellersHome.Position, kind, HaulPickupReach, claimant: null) is null)
                continue;
            if (PickErrandRunner(sellers) is not { } hauler)
                return;

            hauler.GiveErrand(new Errand
            {
                Kind = ErrandKind.Haul, From = sellers.Id, To = buyers.Id, Destination = buyersHome, Material = kind, Payment = HaulPrice,
            });
            string what = kind == MaterialKind.Stone ? "a stone" : "a branch";
            QueueFloatingText(hauler.Position, $"Hauling {what}", AidTextColor);
            Game.AddEventLog($"[TRADE] {hauler.Name} of {sellers.Title} set out to haul {what} to their allies, {buyers.Title}, for {HaulPrice} food");
            return;
        }
    }

    /// <summary>A hauler's done: delivered, it's paid (as much as its buyers can spare); if not, it just heads home.</summary>
    public void PayForHaul(Bramblekin hauler, Errand errand, bool delivered)
    {
        if (delivered && _groups.TryGetValue(errand.To, out KinGroup? buyers))
        {
            int pay = Math.Min(errand.Payment, StoredFood(buyers));
            TakeFromStores(buyers, pay, preferred: errand.Destination);
            errand.Load = pay;
            MaterialsTraded++;
            HaulFoodPaid += pay;
            if (MaterialsTraded == 1)
                Game.AddEventLog($"[TRADE] {hauler.Name} delivered the first {(errand.Material == MaterialKind.Stone ? "stone" : "branch")} traded to {buyers.Title}, for {pay} food");
            if (pay < errand.Payment)
                AddGrievance(errand.From, errand.To, 0.5f * (errand.Payment - pay));
        }
        errand.Returning = true;
    }

    /// <summary>An errand still makes sense: the runner is still in its group, and (outbound) the allies and the place it's going still exist.</summary>
    public bool IsErrandValid(Bramblekin runner, Errand errand)
    {
        if (runner.GroupId != errand.From)
            return false;
        if (errand.Returning)
            return true;
        return _groups.ContainsKey(errand.To) && !errand.Destination.IsCollapsed;
    }

    /// <summary>An errand that can't be finished (or whose runner died): whatever was in the sack spills on the ground.</summary>
    public void AbandonErrand(Bramblekin runner, Errand errand)
    {
        if (errand.Load > 0)
        {
            ScatterFoodAround(runner.Position, errand.Load, 0.6f, FoodShardKind.Berry);
            ErrandsLost++;
        }
        errand.Load = 0;
        runner.EndErrand();
    }
}
