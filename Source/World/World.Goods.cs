using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Trade goods and the market ---------------------------------------------------------
    // A clan that weaves makes cloth and one that cuts stone makes blocks, a little at a time
    // while it has a home to work from. They wear out slowly. Two allied clans that both hold a
    // market swap goods for food: the one with goods to spare sells two for the other's food.

    /// <summary>A weaving clan makes a length of cloth this often (s), a stonecutting one a block…</summary>
    private const float ClothSeconds = 25f, StoneSeconds = 35f;

    /// <summary>…up to this many of each; one of each wears out this often.</summary>
    private const int MaxGoods = 10;

    private const float GoodsWearSeconds = 90f;

    /// <summary>A sale: two goods for this much food; a clan trades at most this often (s).</summary>
    private const int GoodsPrice = 5;

    private const float TradeEvery = 30f;

    private static readonly Color GoodsTextColor = new(200, 150, 60, 255);

    public int GoodsMade { get; private set; }

    public int GoodsTraded { get; private set; }

    public int GoodsFoodPaid { get; private set; }

    private float _goodsWear;

    /// <summary>Every clan with the crafts makes its goods over time.</summary>
    private void UpdateGoods(float deltaTime)
    {
        _goodsWear += deltaTime;
        bool wear = _goodsWear >= GoodsWearSeconds;
        if (wear)
            _goodsWear = 0f;
        foreach (KinGroup group in Groups)
        {
            group.TradeCooldown = MathF.Max(0f, group.TradeCooldown - deltaTime);
            if (wear)
            {
                group.Cloth = Math.Max(0, group.Cloth - 1);
                group.CutStone = Math.Max(0, group.CutStone - 1);
            }
            if (group.Home is not { IsBuilt: true, IsCollapsed: false } || !Knows(group, Craft.Tools))
                continue;
            group.GoodsTimer += deltaTime * Math.Min(3, group.Members.Count(m => !m.IsDead && !m.IsYoung)) / 2f;
            bool weaves = Knows(group, Craft.Weaving), cuts = Knows(group, Craft.Stonecutting);
            if (weaves && group.GoodsTimer >= ClothSeconds && group.Cloth < MaxGoods)
            {
                group.GoodsTimer = 0f;
                group.Cloth++;
                GoodsMade++;
            }
            else if (cuts && group.GoodsTimer >= StoneSeconds && group.CutStone < MaxGoods)
            {
                group.GoodsTimer = 0f;
                group.CutStone++;
                GoodsMade++;
            }
        }
    }

    /// <summary>Allied clans both holding a market: <paramref name="seller"/> sells two of what it has most of for food from <paramref name="buyer"/>'s stores.</summary>
    private void TryTradeGoods(KinGroup seller, KinGroup buyer)
    {
        if (!Knows(seller, Craft.Markets) || !Knows(buyer, Craft.Markets) || seller.TradeCooldown > 0f)
            return;
        bool cloth = seller.Cloth >= seller.CutStone;
        if ((cloth ? seller.Cloth : seller.CutStone) < 2 || (cloth ? buyer.Cloth : buyer.CutStone) >= MaxGoods - 1)
            return;
        if (StoredFood(buyer) < GoodsPrice + 2)
            return;

        TakeFromStores(buyer, GoodsPrice, preferred: null);
        int paid = 0;
        foreach (Shelter home in GroupHomes(seller))
        {
            while (paid < GoodsPrice && home.TryDeposit())
                paid++;
        }
        if (cloth)
        {
            seller.Cloth -= 2;
            buyer.Cloth += 2;
        }
        else
        {
            seller.CutStone -= 2;
            buyer.CutStone += 2;
        }
        seller.TradeCooldown = TradeEvery;
        CarryInfectionAlongTrade(seller, buyer);
        GoodsTraded++;
        GoodsFoodPaid += paid;
        string what = cloth ? "cloth" : "cut stone";
        if (GoodsTraded == 1)
        {
            Game.AddEventLog($"[MARKET] {seller.CapitalTitle} sold {what} to their allies, {buyer.Title}, at market for {paid} food");
            Chronicle($"{seller.CapitalTitle} and {buyer.Title} traded at market", seller, buyer);
        }
        if (seller.Leader is { } leader)
            QueueFloatingText(leader.Position, $"Sold {what}", GoodsTextColor);
    }
}
