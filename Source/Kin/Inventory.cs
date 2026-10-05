namespace GardenGuardians;

/// <summary>What a pack slot can hold. The first entries are the <see cref="FoodShardKind"/>s, in the same order, so one casts to the other.</summary>
public enum ItemKind
{
    Berry,
    Meat,
    Acorn,
    Seed,
    Mushroom,
    Cress,
    Fish,
    Honeydew,

    /// <summary>A wooden water bottle, full: one drink (or one cupful for a cistern).</summary>
    Water,

    /// <summary>Building material (see <see cref="Twig"/>, <see cref="Material"/>).</summary>
    Twig,
    Stone,
    Branch,
}

public static class ItemInfo
{
    public static bool IsFood(ItemKind kind) => kind <= ItemKind.Honeydew;

    public static bool IsMaterial(ItemKind kind) => kind >= ItemKind.Twig;

    public static ItemKind Of(FoodShardKind kind) => (ItemKind)(int)kind;

    /// <summary>The loose-food kind of a food item (not for <see cref="ItemKind.Water"/>).</summary>
    public static FoodShardKind FoodOf(ItemKind kind) => (FoodShardKind)(int)kind;

    public static string Name(ItemKind kind) => kind switch
    {
        ItemKind.Berry => "Berry",
        ItemKind.Meat => "Meat",
        ItemKind.Acorn => "Acorn",
        ItemKind.Seed => "Grass seed",
        ItemKind.Mushroom => "Mushroom",
        ItemKind.Cress => "Watercress",
        ItemKind.Fish => "Fish",
        ItemKind.Honeydew => "Honeydew",
        ItemKind.Water => "Water bottle",
        ItemKind.Twig => "Twig",
        ItemKind.Stone => "Stone",
        ItemKind.Branch => "Branch",
        _ => kind.ToString(),
    };
}

/// <summary>
/// What a Bramblekin carries: <see cref="Slots"/> slots, each holding one kind of item stacked up to <see cref="MaxStack"/>.
/// Food, fish and water bottles live here; building materials and the like are still carried in hand.
/// </summary>
public sealed class Inventory
{
    public const int Slots = 9;
    public const int MaxStack = 10;

    private readonly ItemKind?[] _kind = new ItemKind?[Slots];
    private readonly int[] _count = new int[Slots];

    public ItemKind? KindAt(int slot) => _count[slot] > 0 ? _kind[slot] : null;

    public int CountAt(int slot) => _count[slot];

    public int Count(ItemKind kind)
    {
        int total = 0;
        for (int i = 0; i < Slots; i++)
            if (_count[i] > 0 && _kind[i] == kind)
                total += _count[i];
        return total;
    }

    /// <summary>How many food items, of any kind, it holds.</summary>
    public int FoodCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < Slots; i++)
                if (_count[i] > 0 && _kind[i] is { } kind && ItemInfo.IsFood(kind))
                    total += _count[i];
            return total;
        }
    }

    /// <summary>How many building materials (twigs, stones, branches) it holds.</summary>
    public int MaterialCount => Count(ItemKind.Twig) + Count(ItemKind.Stone) + Count(ItemKind.Branch);

    /// <summary>True if <paramref name="amount"/> more of <paramref name="kind"/> fit.</summary>
    public bool CanAdd(ItemKind kind, int amount = 1)
    {
        int room = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (_count[i] == 0)
                room += MaxStack;
            else if (_kind[i] == kind)
                room += MaxStack - _count[i];
        }
        return room >= amount;
    }

    /// <summary>Adds up to <paramref name="amount"/> of <paramref name="kind"/>, topping up its stacks before opening a new slot. Returns how many went in.</summary>
    public int Add(ItemKind kind, int amount = 1)
    {
        int added = 0;
        for (int i = 0; i < Slots && added < amount; i++)
        {
            if (_count[i] == 0 || _kind[i] != kind)
                continue;
            int take = Math.Min(MaxStack - _count[i], amount - added);
            _count[i] += take;
            added += take;
        }
        for (int i = 0; i < Slots && added < amount; i++)
        {
            if (_count[i] != 0)
                continue;
            int take = Math.Min(MaxStack, amount - added);
            _kind[i] = kind;
            _count[i] = take;
            added += take;
        }
        return added;
    }

    /// <summary>Takes one item out of <paramref name="slot"/>. Returns what it was, or null if the slot was empty.</summary>
    public ItemKind? RemoveAt(int slot)
    {
        if (slot < 0 || slot >= Slots || _count[slot] == 0)
            return null;
        _count[slot]--;
        return _kind[slot];
    }

    /// <summary>Takes <paramref name="amount"/> of <paramref name="kind"/> (emptying the smallest stacks first). False, and nothing taken, if it holds fewer.</summary>
    public bool Remove(ItemKind kind, int amount = 1)
    {
        if (Count(kind) < amount)
            return false;
        while (amount > 0)
        {
            int best = -1;
            for (int i = 0; i < Slots; i++)
                if (_count[i] > 0 && _kind[i] == kind && (best < 0 || _count[i] < _count[best]))
                    best = i;
            int take = Math.Min(_count[best], amount);
            _count[best] -= take;
            amount -= take;
        }
        return true;
    }

    /// <summary>The slot of the food stack to eat from: the smallest, to free slots up. Null if it holds no food.</summary>
    public int? FoodSlot()
    {
        int? best = null;
        for (int i = 0; i < Slots; i++)
        {
            if (_count[i] == 0 || _kind[i] is not { } kind || !ItemInfo.IsFood(kind))
                continue;
            if (best is null || _count[i] < _count[best.Value])
                best = i;
        }
        return best;
    }

    /// <summary>Slots as parallel arrays for saving (empty slots are kind -1).</summary>
    public (int[] Kinds, int[] Counts) ToArrays()
    {
        var kinds = new int[Slots];
        var counts = new int[Slots];
        for (int i = 0; i < Slots; i++)
        {
            kinds[i] = _count[i] > 0 && _kind[i] is { } kind ? (int)kind : -1;
            counts[i] = _count[i];
        }
        return (kinds, counts);
    }

    public void FromArrays(int[]? kinds, int[]? counts)
    {
        for (int i = 0; i < Slots; i++)
        {
            _kind[i] = null;
            _count[i] = 0;
        }
        if (kinds is null || counts is null)
            return;
        int kindCount = Enum.GetValues<ItemKind>().Length;
        for (int i = 0; i < Slots && i < kinds.Length && i < counts.Length; i++)
        {
            if (kinds[i] < 0 || kinds[i] >= kindCount || counts[i] <= 0)
                continue;
            _kind[i] = (ItemKind)kinds[i];
            _count[i] = Math.Min(MaxStack, counts[i]);
        }
    }
}

/// <summary>What a home's store holds besides food: just a count per item kind, each up to <see cref="MaxPerItem"/>.</summary>
public sealed class StoreStock
{
    public const int MaxPerItem = 64;

    private readonly int[] _count = new int[Enum.GetValues<ItemKind>().Length];

    public int Count(ItemKind kind) => _count[(int)kind];

    /// <summary>Adds up to <paramref name="amount"/> (what fits under the cap). Returns how many went in.</summary>
    public int Add(ItemKind kind, int amount)
    {
        int added = Math.Max(0, Math.Min(amount, MaxPerItem - _count[(int)kind]));
        _count[(int)kind] += added;
        return added;
    }

    /// <summary>Takes up to <paramref name="amount"/>. Returns how many came out.</summary>
    public int Take(ItemKind kind, int amount)
    {
        int taken = Math.Max(0, Math.Min(amount, _count[(int)kind]));
        _count[(int)kind] -= taken;
        return taken;
    }

    public int[] ToArray() => (int[])_count.Clone();

    public void FromArray(int[]? counts)
    {
        Array.Clear(_count);
        if (counts is null)
            return;
        for (int i = 0; i < _count.Length && i < counts.Length; i++)
            _count[i] = Math.Clamp(counts[i], 0, MaxPerItem);
    }
}
