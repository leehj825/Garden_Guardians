namespace GardenGuardians;

/// <summary>
/// The terrain in use: one of several models (see <see cref="TerrainSet"/>), chosen for each garden
/// (a new garden picks one at random; a saved one keeps its own). Choosing one (see <see cref="Select"/>)
/// re-measures everything that depends on it (see <see cref="World.OnTerrainChanged"/>).
/// </summary>
public static class TerrainData
{
    /// <summary>Metres between height samples — the same for every terrain.</summary>
    public const float Step = 0.5f;

    /// <summary>Half the width (m) of the maps made so far: every terrain is 100 m across.</summary>
    public const float DefaultHalf = 50f;

    /// <summary>
    /// Terrain numbers from here up are grown from a seed rather than baked in (see <see cref="TerrainGenerator"/>): the number is
    /// this + the seed + <see cref="SizeStride"/> x the size (0 small, 1 medium, 2 large — see <see cref="HalfOfSize"/>).
    /// </summary>
    public const int ProceduralBase = 1_000_000;

    /// <summary>What one step of size adds to a grown terrain's number (a seed is always below this).</summary>
    public const int SizeStride = 2_000_000;

    /// <summary>Whether the garden has a creek at all. Off: no brook is traced, drawn, drunk from or built round — the ponds are the only water.</summary>
    public static readonly bool CreekEnabled = false;

    /// <summary>The map sizes a grown terrain can have, by size number: half the width in metres, and a name.</summary>
    public static readonly (float Half, string Name)[] MapSizes = { (50f, "Small 100 m"), (75f, "Medium 150 m"), (100f, "Large 200 m") };

    public static float HalfOfSize(int size) => MapSizes[Math.Clamp(size, 0, MapSizes.Length - 1)].Half;

    /// <summary>The number of the terrain grown from <paramref name="seed"/> at map size <paramref name="size"/>.</summary>
    public static int ProceduralIndex(int seed, int size) => ProceduralBase + seed + SizeStride * Math.Clamp(size, 0, MapSizes.Length - 1);

    /// <summary>The map size a new garden's grown terrain gets (an index into <see cref="MapSizes"/>).</summary>
    public static int NewGardenSize { get; set; }

    /// <summary>Half the map's width (m) and the height grid's samples per side, for the terrain in use.</summary>
    public static float Half { get; private set; } = 50f;
    public static int Size { get; private set; } = 201;

    /// <summary>The terrains, by number: 0 the original; the rest from Tools/convert_terrain.py.</summary>
    private static readonly Func<TerrainSet>[] Makers = { Terrain0.Make, Terrain1.Make, Terrain2.Make, Terrain3.Make };

    private static TerrainSet? _current;

    /// <summary>How many terrains there are.</summary>
    public static int Count => Makers.Length;

    /// <summary>The number of the terrain in use.</summary>
    public static int CurrentIndex { get; private set; }

    /// <summary>The terrain in use (the original until another is chosen).</summary>
    public static TerrainSet Current => _current ??= Makers[CurrentIndex]();

    /// <summary>The terrains a new garden may be given, by number. For now only the original: the others are baked and ship with the game, but aren't offered yet.</summary>
    private static readonly int[] NewGardenTerrains = { 0 };

    /// <summary>A terrain number picked at random from those a new garden may have.</summary>
    public static int RandomIndex(Random rng) =>
        GrowNewGardens ? ProceduralIndex(1 + rng.Next(899_999), NewGardenSize) : NewGardenTerrains[rng.Next(NewGardenTerrains.Length)];

    /// <summary>True: a new garden grows a fresh terrain from a random seed. False: it gets one of <see cref="NewGardenTerrains"/> (the History screen's terrain button).</summary>
    public static bool GrowNewGardens { get; set; }

    /// <summary>Makes terrain <paramref name="index"/> (out of range: the original) the one in use, re-measuring everything that depends on it.</summary>
    public static void Select(int index)
    {
        if (index < 0 || (index >= Count && index < ProceduralBase))
            index = 0;
        if (_current is not null && index == CurrentIndex)
            return;
        CurrentIndex = index;
        _current = index >= ProceduralBase ? TerrainGenerator.Generate((index - ProceduralBase) % SizeStride, (index - ProceduralBase) / SizeStride) : Makers[index]();
        Half = _current.Half;
        Size = (int)MathF.Round(2f * Half / Step) + 1;
        World.OnTerrainChanged();
    }

    public static float[] Heights => Current.Heights;
    public static float PondLevel => Current.PondLevel;
    public static float OakX => Current.OakX;
    public static float OakZ => Current.OakZ;
    public static float HiveAngle => Current.HiveAngle;
    public static float HiveSurface => Current.HiveSurface;
    public static float OakTrunkRadius => Current.OakTrunkRadius;
    public static float OakTrunkHeight => Current.OakTrunkHeight;
    public static float SpringX => Current.SpringX;
    public static float SpringZ => Current.SpringZ;
    public static float[] PropCircles => Current.PropCircles;
    public static float[] OakCircles => Current.OakCircles;
}
