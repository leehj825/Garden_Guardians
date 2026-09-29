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
    public static int RandomIndex(Random rng) => NewGardenTerrains[rng.Next(NewGardenTerrains.Length)];

    /// <summary>Makes terrain <paramref name="index"/> (out of range: the original) the one in use, re-measuring everything that depends on it.</summary>
    public static void Select(int index)
    {
        if (index < 0 || index >= Count)
            index = 0;
        if (_current is not null && index == CurrentIndex)
            return;
        CurrentIndex = index;
        _current = Makers[index]();
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
