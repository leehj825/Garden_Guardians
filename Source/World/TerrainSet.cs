namespace GardenGuardians;

/// <summary>
/// One terrain: its model, and what was measured off it (see Tools/convert_terrain.py) — the ground's
/// height grid, the pond's water level, the oak, the reed clumps and boulders to keep clear of, and
/// where the creek rises. <see cref="TerrainData"/> holds the one in use.
/// </summary>
public sealed class TerrainSet
{
    /// <summary>Half the map's width and depth (m): the ground spans -Half..Half on x and z. Every baked terrain is 100 m across.</summary>
    public float Half { get; init; } = 50f;

    /// <summary>The model's file under Assets/Models/Terrain.</summary>
    public required string ModelFile { get; init; }

    /// <summary>The one water level the ponds share (world Y).</summary>
    public required float PondLevel { get; init; }

    /// <summary>Where the origin of the oak's own models stands (x, z), if the oak is drawn from them rather than being part of the ground model.</summary>
    public float? OakModelX { get; init; }
    public float? OakModelZ { get; init; }

    public required float OakX { get; init; }
    public required float OakZ { get; init; }

    /// <summary>The way the hive faces (radians from +x towards +z) and how far out from the oak's centre the trunk's bare surface is that way, 2.6 m up.</summary>
    public required float HiveAngle { get; init; }
    public required float HiveSurface { get; init; }

    /// <summary>The trunk's radius at the height of a Bramblekin's head, and the top of its broken crown above the ground.</summary>
    public required float OakTrunkRadius { get; init; }
    public required float OakTrunkHeight { get; init; }

    /// <summary>Where the creek rises: the driest corner, far from the ponds.</summary>
    public required float SpringX { get; init; }
    public required float SpringZ { get; init; }

    /// <summary>Circles (x, z, radius) covering the reed clumps and boulders round the ponds: where walkers may not go.</summary>
    public required float[] PropCircles { get; init; }

    /// <summary>Circles (x, z, radius) covering the trunk and roots: where walkers may not go.</summary>
    public required float[] OakCircles { get; init; }

    /// <summary>Ground height in centimetres, row by row (z from -50 m, then x from -50 m), little-endian shorts, base64.</summary>
    public string Encoded { get; init; } = "";

    /// <summary>The ground heights of a generated terrain (in place of <see cref="Encoded"/>).</summary>
    public float[]? GeneratedHeights { get; init; }

    /// <summary>The seed a generated terrain grew from, and the props on it (null for a baked terrain, whose props are part of its model).</summary>
    public int Seed { get; init; }
    public IReadOnlyList<PlacedProp>? Props { get; init; }

    /// <summary>True for a terrain grown from a seed (see <see cref="TerrainGenerator"/>).</summary>
    public bool IsProcedural => Props is not null;

    private float[]? _heights;

    /// <summary>The ground heights (world Y) on the grid, decoded on first use.</summary>
    public float[] Heights => _heights ??= GeneratedHeights ?? Decode();

    private float[] Decode()
    {
        byte[] bytes = Convert.FromBase64String(Encoded);
        var heights = new float[bytes.Length / 2];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = BitConverter.ToInt16(bytes, i * 2) / 100f;
        return heights;
    }
}
