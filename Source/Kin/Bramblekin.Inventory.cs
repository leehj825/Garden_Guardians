using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>What it carries: food, fish and water bottles (see <see cref="Inventory"/>).</summary>
    public Inventory Pack { get; } = new();

    /// <summary>A worker carries its haul to the store once it holds this much food.</summary>
    public const int StockTrip = 5;

    /// <summary>Standing at the water's edge for this long (s) fills one bottle.</summary>
    public const float BottleFillSeconds = 3f;

    /// <summary>Bottles filled by a drink at the pond or a well.</summary>
    private const int BottlesPerDrink = 3;

    /// <summary>A bottle drunk takes this much off Thirst.</summary>
    private const float BottleQuench = 100f;

    /// <summary>Bottles it keeps for drinking when it pours the rest into its cistern, and how many spare ones make a trip home worthwhile.</summary>
    private const int BottleReserve = 2, PourTrip = 3;

    private float _bottleTimer;

    /// <summary>It holds enough to be worth a trip to the store: food in hand, or a full load in the pack.</summary>
    private bool HasLoadToStock => _carried is not null || Pack.FoodCount >= StockTrip;

    /// <summary>Puts picked-up <paramref name="food"/> into the pack. False (and nothing changes) if the pack has no room for it.</summary>
    private bool Stow(FoodShard food, World world)
    {
        ItemKind kind = ItemInfo.Of(food.Kind);
        if (!Pack.CanAdd(kind))
            return false;
        Pack.Add(kind);
        world.StowFood(food);
        return true;
    }

    /// <summary>Takes one food item out of the pack and into its hand, ready to eat. Does nothing if it holds food already or the pack has none.</summary>
    private void TakeMealFromPack(World world)
    {
        if (_carried is not null || Pack.FoodSlot() is not { } slot || Pack.KindAt(slot) is not { } kind)
            return;
        Pack.RemoveAt(slot);
        if (world.HoldFood(Position, ItemInfo.FoodOf(kind)) is { } meal)
            _carried = meal;
        else
            Pack.Add(kind); // Pool exhausted: practically unreachable.
    }

    /// <summary>Puts every food item in the pack into <paramref name="home"/>'s store, until it's full.</summary>
    private void DepositPack(Shelter home, World world)
    {
        for (int slot = 0; slot < Inventory.Slots; slot++)
        {
            while (Pack.KindAt(slot) is { } kind && ItemInfo.IsFood(kind))
            {
                if (!world.DepositFood(home, ItemInfo.FoodOf(kind)))
                    return;
                Pack.RemoveAt(slot);
            }
        }
    }

    /// <summary>Drinks one bottle if it has one. True if it did.</summary>
    private bool DrinkBottle(World world)
    {
        if (!Pack.Remove(ItemKind.Water))
            return false;
        Thirst = MathF.Max(0f, Thirst - BottleQuench);
        _waterSpot = null;
        _drinkWell = null;
        _drinkFrom = null;
        _drinkTimer = 0f;
        StartPause(); // (no pick-up clip: drinking a bottle from the pack is not a pick-up)
        return true;
    }

    /// <summary>Standing at the water's edge fills its bottles, one every <see cref="BottleFillSeconds"/>, up to what the pack holds.</summary>
    private void UpdateBottles(float deltaTime, World world)
    {
        if (!Pack.CanAdd(ItemKind.Water))
        {
            _bottleTimer = 0f;
            return;
        }

        bool atWater =
            (State == BramblekinState.Drinking && _drinkTimer > 0f && _drinkFrom is null) ||
            (State == BramblekinState.Fishing && _fishingSpot is { } spot && GroundMover.HorizontalDistanceSquared(Position, spot) < 1f) ||
            (State == BramblekinState.Collecting && _bottleSpot is { } bottleSpot && GroundMover.HorizontalDistanceSquared(Position, bottleSpot) < 1f) ||
            (IsPlayerControlled && PlayerMove.LengthSquared() < 0.01f && World.NearestShoreSpot(Position, PlayerWaterReach, creek: true) is not null);
        if (!atWater)
        {
            _bottleTimer = 0f;
            return;
        }

        _bottleTimer += deltaTime;
        if (_bottleTimer < BottleFillSeconds)
            return;
        _bottleTimer = 0f;
        Pack.Add(ItemKind.Water);
        StartAction(BramblekinClip.PickingUp, lockMovement: false); // bends to fill the bottle (walking on cuts it short)
    }

    /// <summary>How much bigger than the usual pop-ups the messages about the player's items are drawn (picking up, eating, drinking).</summary>
    private const float ItemTextSize = 2f;

    /// <summary>The player picks a pack slot: eats the food or drinks the bottle in it.</summary>
    public void PlayerUseSlot(int slot, World world)
    {
        if (IsDead || Pack.KindAt(slot) is not { } kind)
            return;

        if (kind == ItemKind.Water)
        {
            if (Thirst < 10f)
            {
                world.QueueFloatingText(Position, "Not thirsty", EggTextColor, ItemTextSize);
                return;
            }
            DrinkBottle(world);
            return;
        }

        if (!ItemInfo.IsFood(kind))
        {
            world.QueueFloatingText(Position, "Can't eat that", EggTextColor);
            return;
        }
        if (Hunger < 10f)
        {
            world.QueueFloatingText(Position, "Not hungry", EggTextColor);
            return;
        }
        if (_carried is not null || State == BramblekinState.Eating)
            return;
        Pack.RemoveAt(slot);
        if (world.HoldFood(Position, ItemInfo.FoodOf(kind)) is { } meal)
        {
            _carried = meal;
            StartEating();
        }
        else
        {
            Pack.Add(kind);
        }
    }

    // --- A home's store: twigs, stones and branches ----------------------------------------------

    /// <summary>The loose twig a gatherer is walking to (a stone or branch is <see cref="_materialTarget"/>).</summary>
    private Twig? _gatherTwig;

    /// <summary>Walks to the nearest home of its clan whose store holds <paramref name="kind"/>, and takes up to <paramref name="amount"/> into its pack. False if no store has any, or the pack has no room.</summary>
    private bool TakeFromStore(ItemKind kind, int amount, float deltaTime, World world, float maxDistance = 1000f)
    {
        if (!Pack.CanAdd(kind) || world.GroupOf(this) is not { } group)
            return false;

        Shelter? store = null;
        float nearest = float.MaxValue;
        foreach (Shelter home in world.GroupHomes(group))
        {
            if (home is not { IsBuilt: true, IsCollapsed: false } || home.Stock.Count(kind) == 0)
                continue;
            float distance = GroundMover.HorizontalDistanceSquared(Position, home.Position);
            if (distance < nearest && distance <= maxDistance * maxDistance)
            {
                store = home;
                nearest = distance;
            }
        }
        if (store is null)
            return false;

        SetState(BramblekinState.Stockpiling);
        if (!store.Contains(Position))
        {
            MoveTo(store.Position, WalkSpeed, deltaTime, world);
            return true;
        }

        int taken = store.Stock.Take(kind, amount);
        int added = Pack.Add(kind, taken);
        store.Stock.Add(kind, taken - added); // (whatever didn't fit goes back)
        StartPause();
        return true;
    }

    /// <summary>Whether this gatherer should collect <paramref name="kind"/> for <paramref name="store"/>: its clan can use it, and neither the store nor the pack is full of it.</summary>
    private bool WantsForStock(Shelter store, KinGroup group, ItemKind kind) =>
        store.Stock.Count(kind) + Pack.Count(kind) < StoreStock.MaxPerItem && Pack.CanAdd(kind) &&
        kind switch
        {
            ItemKind.Stone => World.Knows(group, Craft.Stonework),
            ItemKind.Branch => World.Knows(group, Craft.Palisade),
            _ => true,
        };

    /// <summary>
    /// Gatherer with nothing else to do: picks up loose twigs, stones and branches near <paramref name="store"/> (up to <see cref="StockTrip"/>
    /// at a time) and puts them in its stock. False when there's nothing wanted to collect or carry.
    /// </summary>
    private bool TryGatherMaterials(Shelter store, KinGroup group, float deltaTime, World world)
    {
        if (Pack.MaterialCount < StockTrip && FindGatherTarget(store, group, world))
        {
            SetState(BramblekinState.Collecting);
            if (_gatherTwig is { } twig)
            {
                ClaimTwig(twig);
                if (GroundMover.HorizontalDistance(Position, twig.Position) > PickupDistance)
                {
                    MoveTo(twig.Position, WalkSpeed, deltaTime, world);
                    return true;
                }
                ReleaseTwigClaim();
                twig.Deactivate();
                _gatherTwig = null;
                Pack.Add(ItemKind.Twig);
            }
            else if (_materialTarget is { } material)
            {
                float reach = PickupDistance + (material.Kind == MaterialKind.Branch ? 0.4f : 0f);
                if (GroundMover.HorizontalDistance(Position, material.Position) > reach)
                {
                    MoveTo(material.Position, WalkSpeed, deltaTime, world);
                    return true;
                }
                Pack.Add(material.Kind == MaterialKind.Stone ? ItemKind.Stone : ItemKind.Branch);
                material.Deactivate();
                _materialTarget = null;
            }
            StartAction(BramblekinClip.PickingUp, lockMovement: true);
            return true;
        }

        if (Pack.MaterialCount == 0)
            return false;

        SetState(BramblekinState.Stockpiling);
        if (!store.Contains(Position))
        {
            MoveTo(store.Position, WalkSpeed, deltaTime, world);
            return true;
        }
        foreach (ItemKind kind in new[] { ItemKind.Twig, ItemKind.Stone, ItemKind.Branch })
        {
            int held = Pack.Count(kind);
            if (held == 0)
                continue;
            int added = store.Stock.Add(kind, held);
            Pack.Remove(kind, held);
            if (added < held)
                world.DropItems(kind, store.Position, held - added); // (what the store can't hold is left lying about)
        }
        StartPause();
        return true;
    }

    /// <summary>Keeps (or picks) the loose twig, stone or branch near <paramref name="store"/> it's going for. False if there's none worth getting.</summary>
    private bool FindGatherTarget(Shelter store, KinGroup group, World world)
    {
        if (_gatherTwig is { } twig && (!World.IsAvailable(twig, this) || !WantsForStock(store, group, ItemKind.Twig)))
        {
            ReleaseTwigClaim();
            _gatherTwig = null;
        }
        if (_materialTarget is { } material && (!material.IsActive || material.IsCarried || !WantsForStock(store, group, material.Kind == MaterialKind.Stone ? ItemKind.Stone : ItemKind.Branch)))
        {
            if (material.ClaimedBy == this)
                material.ClaimedBy = null;
            _materialTarget = null;
        }
        if (_gatherTwig is not null || _materialTarget is not null)
            return true;

        if (WantsForStock(store, group, ItemKind.Twig) && world.NearestAvailableTwig(store.Position, GatherRange, this) is { } found)
        {
            _gatherTwig = found;
            return true;
        }
        foreach (MaterialKind kind in Enum.GetValues<MaterialKind>())
        {
            if (!WantsForStock(store, group, kind == MaterialKind.Stone ? ItemKind.Stone : ItemKind.Branch))
                continue;
            if (world.NearestMaterial(store.Position, kind, GatherRange, this) is { } loose)
            {
                _materialTarget = loose;
                loose.ClaimedBy = this;
                return true;
            }
        }
        return false;
    }

    /// <summary>Kin keep this many bottles in their home store, and fill bottles at the shore (up to <see cref="WaterStoreTarget"/> in store) when it holds fewer.</summary>
    private const int WaterStoreTarget = 20;

    /// <summary>The shore spot it is filling bottles at for its store.</summary>
    private Vector3? _bottleSpot;

    /// <summary>
    /// Life around a home: a full load of bottles (<see cref="StockTrip"/>) goes into the store (keeping a couple to drink); else, while the store
    /// holds fewer than <see cref="WaterStoreTarget"/>, it stands at the nearest shore filling bottles. False if there's nothing to do.
    /// </summary>
    private bool TryStockWater(Shelter store, float deltaTime, World world)
    {
        if (IsYoung)
            return false;

        if (Pack.Count(ItemKind.Water) >= StockTrip)
        {
            SetState(BramblekinState.Stockpiling);
            if (!store.Contains(Position))
            {
                MoveTo(store.Position, WalkSpeed, deltaTime, world);
                return true;
            }
            int pour = Pack.Count(ItemKind.Water) - BottleReserve;
            Pack.Remove(ItemKind.Water, pour);
            store.Stock.Add(ItemKind.Water, pour);
            _bottleSpot = null;
            StartPause();
            return true;
        }

        if (store.Stock.Count(ItemKind.Water) >= WaterStoreTarget || !Pack.CanAdd(ItemKind.Water))
        {
            _bottleSpot = null;
            return false;
        }
        _bottleSpot ??= World.NearestShoreSpot(store.Position, 40f, creek: true);
        if (_bottleSpot is not { } spot)
            return false;

        SetState(BramblekinState.Collecting);
        if (GroundMover.HorizontalDistance(Position, spot) > 0.5f)
        {
            MoveTo(spot, WalkSpeed, deltaTime, world);
            return true;
        }
        _mover.Heading = TowardWater(spot);
        _mover.Idle();
        return true; // (UpdateBottles fills a bottle every few seconds while it stands here)
    }

    /// <summary>How far (m) the player's Pick up button reaches.</summary>
    private const float PlayerPickUpReach = 1.1f;

    /// <summary>The player's Pick up button: the nearest loose food, twig, stone or branch within arm's reach (it turns to face it, then bends to pick it up) goes into the pack (hungry or not); with none, a bottle is filled if the water is at hand.</summary>
    public bool PlayerPickUp(World world, bool auto = false)
    {
        if (IsDead || State == BramblekinState.Eating || _actionLock > 0f)
            return false;
        float reach = auto ? PlayerAutoPickReach : PlayerPickUpReach; // (an explorer's own pick-up: smaller reach, no messages, no bottle)

        FoodShard? food = Pack.CanAdd(ItemKind.Berry) ? world.NearestAvailableFood(Position, reach, this) : null;
        Twig? twig = Pack.CanAdd(ItemKind.Twig) ? world.NearestAvailableTwig(Position, reach, this) : null;
        Material? stone = Pack.CanAdd(ItemKind.Stone) ? world.NearestMaterial(Position, MaterialKind.Stone, reach, this) : null;
        Material? branch = Pack.CanAdd(ItemKind.Branch) ? world.NearestMaterial(Position, MaterialKind.Branch, reach, this) : null;

        float best = float.MaxValue;
        int pick = -1;
        Vector3 pickAt = Position;
        void Consider(int which, Vector3? at)
        {
            if (at is not { } point)
                return;
            float distance = GroundMover.HorizontalDistanceSquared(Position, point);
            if (distance < best)
            {
                best = distance;
                pick = which;
                pickAt = point;
            }
        }
        Consider(0, food?.Position);
        Consider(1, twig?.Position);
        Consider(2, stone?.Position);
        Consider(3, branch?.Position);

        ItemKind? got = null;
        switch (pick)
        {
            case 0 when food is not null && Pack.CanAdd(ItemInfo.Of(food.Kind)):
                got = ItemInfo.Of(food.Kind);
                Pack.Add(got.Value);
                world.StowFood(food);
                break;
            case 1:
                twig!.Deactivate();
                Pack.Add(ItemKind.Twig);
                got = ItemKind.Twig;
                break;
            case 2:
                stone!.Deactivate();
                Pack.Add(ItemKind.Stone);
                got = ItemKind.Stone;
                break;
            case 3:
                branch!.Deactivate();
                Pack.Add(ItemKind.Branch);
                got = ItemKind.Branch;
                break;
            default:
                if (!auto && Pack.CanAdd(ItemKind.Water) && World.NearestShoreSpot(Position, PlayerWaterReach, creek: true) is { } shore)
                {
                    Pack.Add(ItemKind.Water);
                    got = ItemKind.Water;
                    pickAt = shore;
                }
                break;
        }

        if (got is { } item)
        {
            var toward = new Vector2(pickAt.X - Position.X, pickAt.Z - Position.Z);
            if (toward.LengthSquared() > 0.01f)
                _mover.Heading = Vector2.Normalize(toward); // face what it reaches for
            StartAction(BramblekinClip.PickingUp, lockMovement: true);
            world.QueueFloatingText(Position, ItemInfo.Name(item), EggTextColor, ItemTextSize);
            return true;
        }
        if (!auto)
            world.QueueFloatingText(Position, Pack.MaterialCount + Pack.FoodCount >= Inventory.Slots * Inventory.MaxStack ? "Pack full" : "Nothing to pick up", EggTextColor, ItemTextSize);
        return false;
    }

    /// <summary>Gives back what it was saved carrying (see <see cref="KinSave.PackKinds"/>).</summary>
    public void RestorePack(KinSave save) => Pack.FromArrays(save.PackKinds, save.PackCounts);

    /// <summary>The wooden water bottle standing on <paramref name="hip"/> (its base), drawn from plain shapes until there's a model.</summary>
    public static void DrawBottleModel(Vector3 hip)
    {
        var wood = new Color(150, 100, 55, 255);
        Raylib.DrawCylinder(hip, 0.07f, 0.09f, 0.2f, 8, wood);
        Raylib.DrawCylinder(hip + new Vector3(0f, 0.2f, 0f), 0.035f, 0.07f, 0.07f, 8, wood);
        Raylib.DrawCylinder(hip + new Vector3(0f, 0.27f, 0f), 0.04f, 0.04f, 0.04f, 8, new Color(205, 175, 120, 255));
        Raylib.DrawCylinder(hip + new Vector3(0f, 0.07f, 0f), 0.092f, 0.092f, 0.025f, 8, new Color(90, 60, 35, 255));
    }
}
