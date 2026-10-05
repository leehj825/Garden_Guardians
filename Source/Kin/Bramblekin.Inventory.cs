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
    private bool DrinkBottle()
    {
        if (!Pack.Remove(ItemKind.Water))
            return false;
        Thirst = MathF.Max(0f, Thirst - BottleQuench);
        _waterSpot = null;
        _drinkWell = null;
        _drinkFrom = null;
        _drinkTimer = 0f;
        StartAction(BramblekinClip.PickingUp, lockMovement: true);
        StartPause();
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
    }

    /// <summary>The player picks a pack slot: eats the food or drinks the bottle in it.</summary>
    public void PlayerUseSlot(int slot, World world)
    {
        if (IsDead || Pack.KindAt(slot) is not { } kind)
            return;

        if (kind == ItemKind.Water)
        {
            if (Thirst < 10f)
            {
                world.QueueFloatingText(Position, "Not thirsty", EggTextColor);
                return;
            }
            DrinkBottle();
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

    // --- The clan's stock: twigs, stones and branches ------------------------------------------

    /// <summary>The loose twig a gatherer is walking to (a stone or branch is <see cref="_materialTarget"/>).</summary>
    private Twig? _gatherTwig;

    /// <summary>Walks to its clan's home store and takes up to <paramref name="amount"/> of <paramref name="kind"/> out of the clan's stock into its pack. False if there's none, or no room.</summary>
    private bool TakeFromClanStock(ItemKind kind, int amount, float deltaTime, World world)
    {
        if (world.GroupOf(this) is not { Home: { IsCollapsed: false } store } group || group.Stock.Count(kind) == 0 || !Pack.CanAdd(kind))
            return false;

        SetState(BramblekinState.Stockpiling);
        if (!store.Contains(Position))
        {
            MoveTo(store.Position, WalkSpeed, deltaTime, world);
            return true;
        }

        int taken = group.Stock.Take(kind, amount);
        int added = Pack.Add(kind, taken);
        group.Stock.Add(kind, taken - added); // (whatever didn't fit goes back)
        StartPause();
        return true;
    }

    /// <summary>Whether this gatherer should collect <paramref name="kind"/> for its clan: the clan can use it, and neither its stock nor its pack is full of it.</summary>
    private bool WantsForStock(KinGroup group, ItemKind kind) =>
        group.Stock.Count(kind) + Pack.Count(kind) < ClanStock.MaxPerItem && Pack.CanAdd(kind) &&
        kind switch
        {
            ItemKind.Stone => World.Knows(group, Craft.Stonework),
            ItemKind.Branch => World.Knows(group, Craft.Palisade),
            _ => true,
        };

    /// <summary>
    /// Gatherer with nothing else to do: picks up loose twigs, stones and branches near home (up to <see cref="StockTrip"/> at a time) and
    /// carries them to the clan's stock. False when there's nothing wanted to collect or carry.
    /// </summary>
    private bool TryGatherMaterials(Shelter home, KinGroup group, float deltaTime, World world)
    {
        if (Pack.MaterialCount < StockTrip && FindGatherTarget(home, group, world))
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
        if (!home.Contains(Position))
        {
            MoveTo(home.Position, WalkSpeed, deltaTime, world);
            return true;
        }
        foreach (ItemKind kind in new[] { ItemKind.Twig, ItemKind.Stone, ItemKind.Branch })
        {
            int held = Pack.Count(kind);
            if (held == 0)
                continue;
            group.Stock.Add(kind, held);
            Pack.Remove(kind, held); // (anything over the clan's cap is left behind)
        }
        StartPause();
        return true;
    }

    /// <summary>Keeps (or picks) the loose twig, stone or branch near <paramref name="home"/> it's going for. False if there's none worth getting.</summary>
    private bool FindGatherTarget(Shelter home, KinGroup group, World world)
    {
        if (_gatherTwig is { } twig && (!World.IsAvailable(twig, this) || !WantsForStock(group, ItemKind.Twig)))
        {
            ReleaseTwigClaim();
            _gatherTwig = null;
        }
        if (_materialTarget is { } material && (!material.IsActive || material.IsCarried || !WantsForStock(group, material.Kind == MaterialKind.Stone ? ItemKind.Stone : ItemKind.Branch)))
        {
            if (material.ClaimedBy == this)
                material.ClaimedBy = null;
            _materialTarget = null;
        }
        if (_gatherTwig is not null || _materialTarget is not null)
            return true;

        if (WantsForStock(group, ItemKind.Twig) && world.NearestAvailableTwig(home.Position, GatherRange, this) is { } found)
        {
            _gatherTwig = found;
            return true;
        }
        foreach (MaterialKind kind in Enum.GetValues<MaterialKind>())
        {
            if (!WantsForStock(group, kind == MaterialKind.Stone ? ItemKind.Stone : ItemKind.Branch))
                continue;
            if (world.NearestMaterial(home.Position, kind, GatherRange, this) is { } loose)
            {
                _materialTarget = loose;
                loose.ClaimedBy = this;
                return true;
            }
        }
        return false;
    }

    /// <summary>Gives back what it was saved carrying (see <see cref="KinSave.PackKinds"/>).</summary>
    public void RestorePack(KinSave save) => Pack.FromArrays(save.PackKinds, save.PackCounts);

    /// <summary>A wooden water bottle on its hip, drawn plainly (a model will replace it).</summary>
    private void DrawBottle(Vector2 facing)
    {
        // On the side away from the way it faces, so it doesn't hide the hands.
        var side = new Vector2(-facing.Y, facing.X);
        DrawBottleModel(Position + new Vector3(side.X * 0.22f, BodyHeight * 0.38f, side.Y * 0.22f));
    }

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
