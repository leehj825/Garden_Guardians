namespace GardenGuardians;

/// <summary>
/// The terrain in use: one of several models (see <see cref="TerrainSet"/>), chosen for each garden
/// (a new garden picks one at random; a saved one keeps its own). Choosing one (see <see cref="Select"/>)
/// re-measures everything that depends on it (see <see cref="World.OnTerrainChanged"/>).
/// </summary>
public static class TerrainData
{
    /// <summary>Metres between samples, and samples per side — the same for every terrain.</summary>
    public const float Step = 0.5f;
    public const int Size = 201;

    /// <summary>The terrains, by number: 0 the original; the rest from Tools/convert_terrain.py.</summary>
    private static readonly Func<TerrainSet>[] Makers = { Terrain0.Make, Terrain1.Make, Terrain2.Make, Terrain3.Make };

    private static TerrainSet? _current;

    /// <summary>How many terrains there are.</summary>
    public static int Count => Makers.Length;

    /// <summary>The number of the terrain in use.</summary>
    public static int CurrentIndex { get; private set; }

    /// <summary>The terrain in use (the original until another is chosen).</summary>
    public static TerrainSet Current => _current ??= Makers[CurrentIndex]();

    /// <summary>A terrain number picked at random.</summary>
    public static int RandomIndex(Random rng) => rng.Next(Count);

    /// <summary>Makes terrain <paramref name="index"/> (out of range: the original) the one in use, re-measuring everything that depends on it.</summary>
    public static void Select(int index)
    {
        if (index < 0 || index >= Count)
            index = 0;
        if (_current is not null && index == CurrentIndex)
            return;
        CurrentIndex = index;
        _current = Makers[index]();
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
