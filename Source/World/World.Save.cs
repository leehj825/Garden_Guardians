using System.Numerics;
using System.Reflection;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Save ------------------------------------------------------------------------------

    /// <summary>
    /// Save/Load: everything lasting about the garden — its props, homes,
    /// bushes, loose food and twigs, every Bramblekin and group, the
    /// relations between groups, tributes owed, the chronicle and history,
    /// and every counter, timer and statistic. Call between frames (after
    /// <see cref="CommitPendingChanges"/>).
    /// </summary>
    public SaveGame ToSave()
    {
        var save = new SaveGame
        {
            FamilyNames = _familyNames.ToList(),
            BerryPatches = _berryPatches.Select(p => (V3)p).ToList(),
            Props = GardenProps.Select(p => new PropSave
            {
                Position = p.Position, Kind = p.Kind, Rotation = p.Rotation, TwigLength = p.TwigLength, IsYellow = p.IsYellow, Scale = p.Scale,
            }).ToList(),
            Shelters = Shelters.Where(s => !s.IsCollapsed).Select(s => new ShelterSave
            {
                Id = s.ID, Position = s.Position, Tier = s.Tier, Built = s.IsBuilt, Upgrading = s.IsUpgrading, Twigs = s.TwigsDelivered,
                Stored = s.StoredFood, Owner = s.Owner is { IsDead: false } owner ? owner.ID : null, GroupId = s.GroupId,
                AbandonedSeconds = s.AbandonedSeconds, StageStartedAt = s.StageStartedAt,
                Granary = s.HasGranary, Stakes = s.StakesSet, Stones = s.StonesLaid, Cistern = s.HasCistern, Water = s.Water,
            }).ToList(),
            Kin = Colony.Where(k => !k.IsDead).Select(k => k.ToSave()).ToList(),
            Groups = _groups.Values.Select(g => new GroupSave
            {
                Id = g.Id, Name = g.Name, Leader = g.Leader is { IsDead: false } leader ? leader.ID : null,
                Home = g.Home is { IsCollapsed: false } home ? home.ID : null,
                Annexes = g.Annexes.Where(a => !a.IsCollapsed).Select(a => a.ID).ToList(),
                HomeSiteRetryTimer = g.HomeSiteRetryTimer, Goal = g.Goal == GroupGoal.Raid ? GroupGoal.Stockpile : g.Goal, Sharing = g.Sharing,
                BirthCooldown = g.BirthCooldown, DecisionTimer = g.DecisionTimer, SettleTarget = g.SettleTarget is { } target ? target : null,
                Dowry = g.Dowry, SeedCorn = g.SeedCorn, NextRaidAt = g.NextRaidAt,
                Martial = g.Culture.Martial, Hunting = g.Culture.Hunting, Farming = g.Culture.Farming, Leading = g.Culture.Leading,
                SpidersSlain = g.SpidersSlain,
                Dangers = g.Dangers.Places.Select(p => new PlaceSave(p.Where, p.When)).ToList(),
                FoodSpots = g.FoodSpots.Places.Select(p => new PlaceSave(p.Where, p.When)).ToList(),
            }).ToList(),
            Bushes = Crops.Select(b => new BushSave
            {
                Position = b.Position, Kind = b.Kind, Age = b.Age, GroupId = b.GroupId, Growth = b.Growth, Fruit = b.Fruit, FruitTimer = b.FruitTimer, WildSeconds = b.WildSeconds,
            }).ToList(),
            Food = FoodShards.Where(f => f is { IsActive: true, IsCarried: false })
                .Select(f => new LooseSave { Position = f.Position, Kind = f.Kind, DespawnTimer = f.DespawnTimer }).ToList(),
            Wells = Wells.Select(w => new WellSave { Position = w.Position, GroupId = w.GroupId, StonesNeeded = w.StonesNeeded, StonesLaid = w.StonesLaid }).ToList(),
            Materials = Materials.Where(m => m is { IsActive: true, IsCarried: false })
                .Select(m => new MaterialSave { Position = m.Position, Kind = m.Kind, DespawnTimer = m.DespawnTimer }).ToList(),
            Twigs = Twigs.Where(t => t is { IsActive: true, IsCarried: false })
                .Select(t => new LooseSave { Position = t.Position, DespawnTimer = t.DespawnTimer }).ToList(),
            Relations = _relations.Select(r => new RelationSave
            {
                First = r.Key.Item1, Second = r.Key.Item2, Stance = r.Value.Stance, Grievance = r.Value.Grievance, Since = r.Value.Since,
                FirstScore = r.Value.FirstScore, SecondScore = r.Value.SecondScore,
            }).ToList(),
            Tributes = _tributes.Select(t => new TributeSave { Payer = t.Payer, Receiver = t.Receiver, SeasonsLeft = t.SeasonsLeft, NextDue = t.NextDue }).ToList(),
            Chronicle = ChronicleEntries.ToList(),
            History = History.ToList(),
            Lives = _lives.Values.ToList(),
            Anthill = Anthill is { } hill ? hill.Position : null,
            AnthillStock = Anthill?.Stock ?? 0,
        };

        foreach (PropertyInfo property in SavedProperties)
            save.Numbers["p:" + property.Name] = Convert.ToDouble(property.GetValue(this));
        foreach (FieldInfo field in SavedFields)
        {
            object value = field.GetValue(this)!;
            if (value is Array array)
            {
                save.NumberArrays["f:" + field.Name] = array.Cast<object>().Select(Convert.ToDouble).ToArray();
                if (array.Rank == 2)
                    save.Numbers["columns:" + field.Name] = array.GetLength(1); // So a later build with more columns reads it right.
            }
            else
                save.Numbers["f:" + field.Name] = Convert.ToDouble(value);
        }
        return save;
    }

    // --- Load ------------------------------------------------------------------------------

    /// <summary>Save/Load: the garden exactly as <paramref name="save"/> left it (bar the fleeting — see SaveGame).</summary>
    public static World FromSave(SaveGame save, Terrain terrain, Random rng) => new(terrain, rng, save);

    private World(Terrain terrain, Random rng, SaveGame save)
    {
        Terrain = terrain;
        Rng = rng;

        foreach (PropSave prop in save.Props)
            GardenProps.Add(new GardenProp(prop.Position, prop.Kind, prop.Rotation, prop.TwigLength, prop.IsYellow, prop.Scale));
        RebuildObstacles();
        _berryPatches.AddRange(save.BerryPatches.Select(p => (Vector3)p));
        foreach (GardenProp prop in GardenProps)
        {
            if (prop.Kind == GardenPropKind.Twig)
                _twigPatches.Add(prop.Position);
        }
        foreach (string family in save.FamilyNames)
            _familyNames.Add(family);

        for (int i = 0; i < FoodPoolCapacity; i++)
            FoodShards.Add(new FoodShard());
        foreach (LooseSave loose in save.Food)
        {
            if (ActivateFood(loose.Position, loose.Kind) is { } food)
                food.DespawnTimer = loose.DespawnTimer;
        }
        for (int i = 0; i < TwigPoolCapacity; i++)
            Twigs.Add(new Twig());
        foreach (LooseSave loose in save.Twigs)
        {
            if (Twigs.FirstOrDefault(t => !t.IsActive) is { } twig)
            {
                twig.Activate(loose.Position, (float)(Rng.NextDouble() * MathF.Tau));
                twig.DespawnTimer = loose.DespawnTimer;
            }
        }

        InitializeMaterials(scatterStones: save.Materials is null);
        foreach (MaterialSave loose in save.Materials ?? new List<MaterialSave>())
        {
            if (ActivateMaterial(loose.Position, loose.Kind) is { } material)
                material.DespawnTimer = loose.DespawnTimer;
        }

        var shelters = new Dictionary<int, Shelter>();
        foreach (ShelterSave s in save.Shelters)
        {
            var shelter = new Shelter(s.Position)
            {
                GroupId = s.GroupId, AbandonedSeconds = s.AbandonedSeconds, StageStartedAt = s.StageStartedAt,
                HasGranary = s.Granary, StakesSet = s.Palisade ? Shelter.PalisadeStakeCost : s.Stakes, StonesLaid = s.Stones,
                HasCistern = s.Cistern, Water = s.Water,
            };
            shelter.Restore(s.Id, s.Tier, s.Built, s.Upgrading, s.Twigs, s.Stored);
            Shelters.Add(shelter);
            shelters[s.Id] = shelter;
        }
        foreach (WellSave w in save.Wells)
            Wells.Add(new Well(w.Position, w.GroupId, w.StonesNeeded) { StonesLaid = w.StonesLaid });

        var kin = new Dictionary<int, Bramblekin>();
        foreach (KinSave k in save.Kin)
        {
            Bramblekin restored = Bramblekin.FromSave(k, Rng);
            Colony.Add(restored);
            kin[k.Id] = restored;
        }
        foreach (KinSave k in save.Kin)
        {
            Bramblekin restored = kin[k.Id];
            restored.LinkSave(k, kin, shelters);
            if (k.CarryingFood && ActivateFood(restored.Position, FoodShardKind.Berry) is { } food)
            {
                PickUpFood(food);
                restored.ReceiveFood(food);
            }
        }
        foreach (ShelterSave s in save.Shelters)
        {
            if (s.Owner is { } ownerId && kin.TryGetValue(ownerId, out Bramblekin? owner))
                shelters[s.Id].Owner = owner;
        }

        foreach (GroupSave g in save.Groups)
        {
            var group = new KinGroup(g.Id)
            {
                Name = g.Name,
                Home = g.Home is { } homeId && shelters.TryGetValue(homeId, out Shelter? home) ? home : null,
                HomeSiteRetryTimer = g.HomeSiteRetryTimer,
                Goal = g.Goal,
                Sharing = g.Sharing,
                BirthCooldown = g.BirthCooldown,
                DecisionTimer = g.DecisionTimer,
                SettleTarget = g.SettleTarget is { } target ? target : null,
                Dowry = g.Dowry,
                SeedCorn = g.SeedCorn ?? FirstSeedCorn, // A save from before seed corn: a first handful.
                NextRaidAt = g.NextRaidAt,
            };
            foreach (int annexId in g.Annexes)
            {
                if (shelters.TryGetValue(annexId, out Shelter? annex))
                    group.Annexes.Add(annex);
            }
            group.Culture.Martial = g.Martial;
            group.Culture.Hunting = g.Hunting;
            group.Culture.Farming = g.Farming;
            group.Culture.Leading = g.Leading;
            if (g.Leading == Tradition.None)
                group.Culture.UpdateLeading(); // An older save, from before the name was kept.
            group.SpidersSlain = g.SpidersSlain;
            group.Dangers.Load(g.Dangers.Select(p => ((Vector3)p.Where, p.When)));
            group.FoodSpots.Load(g.FoodSpots.Select(p => ((Vector3)p.Where, p.When)));
            if (g.Leader is { } leaderId && kin.TryGetValue(leaderId, out Bramblekin? leader))
                group.SetLeader(leader);
            _groups[group.Id] = group;
        }

        foreach (BushSave b in save.Bushes)
        {
            var bush = new Crop(b.Position, b.GroupId, b.Kind) { IsWatered = IsWatered(b.Position, b.Kind), Age = b.Age };
            bush.Restore(b.Growth, b.Fruit, b.FruitTimer, b.WildSeconds);
            Crops.Add(bush);
        }
        ClearOakGround();
        RebuildObstacles();
        foreach (RelationSave r in save.Relations)
        {
            _relations[RelationKey(r.First, r.Second)] = new GroupRelation
            {
                Stance = r.Stance, Grievance = r.Grievance, Since = r.Since, FirstScore = r.FirstScore, SecondScore = r.SecondScore,
            };
        }
        foreach (TributeSave t in save.Tributes)
            _tributes.Add(new Tribute { Payer = t.Payer, Receiver = t.Receiver, SeasonsLeft = t.SeasonsLeft, NextDue = t.NextDue });
        ChronicleEntries.AddRange(save.Chronicle);
        History.AddRange(save.History);
        foreach (LifeRecord life in save.Lives)
            _lives[life.Id] = life;
        RestoreAnthill(save.Anthill, save.AnthillStock);
        foreach (Bramblekin living in Colony)
            RegisterLife(living); // An older save, from before lives were kept.

        // Counters, timers and statistics last, so nothing above overwrites them.
        foreach (PropertyInfo property in SavedProperties)
        {
            if (save.Numbers.TryGetValue("p:" + property.Name, out double value))
                property.SetValue(this, FromDouble(value, property.PropertyType));
        }
        foreach (FieldInfo field in SavedFields)
        {
            if (field.GetValue(this) is Array array)
            {
                if (save.NumberArrays.TryGetValue("f:" + field.Name, out double[]? values))
                    RestoreArray(array, values, save.Numbers.TryGetValue("columns:" + field.Name, out double columns) ? (int)columns : null);
            }
            else if (save.Numbers.TryGetValue("f:" + field.Name, out double value))
            {
                field.SetValue(this, FromDouble(value, field.FieldType));
            }
        }

        if (SpiderRespawnTimer <= 0f)
            SpawnSpider(); // A live Wolf Spider turns up somewhere new; a slain one comes back when its timer runs out.
        RebuildSpatialGrids();
        RebuildGroups();
        SyncPondLevel();
    }

    // --- The numbers, by reflection ----------------------------------------------------------

    /// <summary>World properties worth saving: every settable number, flag or enum (counters, weather, time).</summary>
    private static IEnumerable<PropertyInfo> SavedProperties =>
        typeof(World).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.CanRead && p.SetMethod is not null && p.GetIndexParameters().Length == 0 && IsSavedNumber(p.PropertyType));

    /// <summary>World fields worth saving: every number, flag or enum (timers, running totals) and array of numbers (statistics) — but not a property's own backing field.</summary>
    private static IEnumerable<FieldInfo> SavedFields =>
        typeof(World).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => !f.Name.Contains("k__BackingField") &&
                        (IsSavedNumber(f.FieldType) || (f.FieldType.IsArray && IsSavedNumber(f.FieldType.GetElementType()!))));

    private static bool IsSavedNumber(Type type) =>
        type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(bool) || type.IsEnum;

    private static object FromDouble(double value, Type type) =>
        type == typeof(int) ? (int)Math.Round(value)
        : type == typeof(float) ? (float)value
        : type == typeof(bool) ? value != 0
        : type.IsEnum ? Enum.ToObject(type, (int)Math.Round(value))
        : value;

    /// <summary>
    /// Puts saved numbers back into <paramref name="array"/>. A 2D array is
    /// matched row by row and column by column, so a save from a build with
    /// fewer columns (say, before a new cause of death) still lines up —
    /// <paramref name="savedColumns"/> if the save recorded it, else worked
    /// out from its size.
    /// </summary>
    private static void RestoreArray(Array array, double[] values, int? savedColumns)
    {
        Type element = array.GetType().GetElementType()!;
        if (array.Rank == 1)
        {
            for (int i = 0; i < Math.Min(array.Length, values.Length); i++)
                array.SetValue(FromDouble(values[i], element), i);
            return;
        }
        int rows = array.GetLength(0), columns = array.GetLength(1);
        int saved = savedColumns ?? Math.Max(1, values.Length / rows);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < Math.Min(columns, saved); column++)
            {
                int i = row * saved + column;
                if (i < values.Length)
                    array.SetValue(FromDouble(values[i], element), row, column);
            }
        }
    }
}
