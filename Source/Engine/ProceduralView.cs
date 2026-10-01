using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Draws a generated terrain (see <see cref="TerrainGenerator"/>): the ground as a simplified mesh
/// (<see cref="TerrainMesh"/>) under one texture baked from the models' own grass and dirt tiles, and the props
/// cut out of the hand-made models. The ground and its props are built the first time they're drawn (that needs
/// a GPU context); the texture is worked out on another thread meanwhile, so a phone never stalls on it — the
/// ground shows plain grass green until it's ready.
/// </summary>
public sealed unsafe class ProceduralView
{
    /// <summary>The ground mesh may be off the height grid by this much (m).</summary>
    private const float MeshTolerance = 0.04f;

    /// <summary>Coarser ground meshes, for when the camera is far off: allowed this far (m) off the height grid, from this far (m) from where the camera looks.</summary>
    private static readonly float[] LodTolerance = { MeshTolerance, 0.25f, 1.0f };
    private static readonly float[] LodFrom = { 0f, 60f, 130f };

    /// <summary>Where the camera is, what it looks at and how wide it sees (vertical field of view in degrees, width over height), set before the world is drawn: picks the ground's level of detail and which props are out of sight.</summary>
    public static Vector3 Eye, Focus;
    public static float FovDegrees = 45f, Aspect = 1.6f;

    /// <summary>How far (m) the ground mesh drawn last may stand off the true ground: what an overlay laid on the ground has to clear.</summary>
    public static float GroundTolerance { get; private set; } = MeshTolerance;

    /// <summary>The baked ground texture is this many pixels square (a power of two, so it can be mipmapped).</summary>
    private const int TextureSize = 2048;

    /// <summary>A ground tile repeats every this many metres; the noise that mixes the tiles is worked out on a grid of this many metres.</summary>
    private const float TileMetres = 6f, FieldMetres = 1f;

    private const int Grasses = 4, Dirts = 2;

    private readonly TerrainSet _set;
    private readonly Model[] _lods = new Model[3];
    private readonly bool[] _lodBuilt = new bool[3];
    private Texture2D _groundTexture;
    private Texture2D _flat;
    private bool _ready;
    private Task<byte[]>? _bake;

    private static readonly Dictionary<string, Model> PropModels = new();

    /// <summary>The view whose ground is built now: a new garden's replaces it, and frees its mesh and texture.</summary>
    private static ProceduralView? _built;

    public ProceduralView(TerrainSet set) => _set = set;

    private static string AssetPath(string relative) => OperatingSystem.IsAndroid()
        ? relative
        : Path.Combine(AppContext.BaseDirectory, "Assets", relative);

    // --- Building --------------------------------------------------------------------------------

    private void EnsureBuilt()
    {
        if (_ready)
            return;
        _built?.Release();
        _built = this;
        BuildLod(0);

        Image plain = Raylib.GenImageColor(2, 2, new Color(95, 120, 55, 255));
        _flat = Raylib.LoadTextureFromImage(plain);
        Raylib.UnloadImage(plain);
        _groundTexture = _flat;
        _lods[0].Materials[0].Maps[(int)MaterialMapIndex.Albedo].Texture = _flat;

        // The tiles are read here (they're files); the mixing of them, the slow part, runs on another thread.
        var grass = new (byte[] Pixels, int Width)[Grasses];
        var dirt = new (byte[] Pixels, int Width)[Dirts];
        for (int k = 0; k < Grasses; k++)
            grass[k] = LoadTile($"grass_{k + 1}.png");
        for (int k = 0; k < Dirts; k++)
            dirt[k] = LoadTile($"dirt_{k + 1}.png");
        float[] heights = _set.Heights;
        int size = TerrainData.Size, seed = _set.Seed;
        float half = TerrainData.Half, level = _set.PondLevel;
        _bake = Task.Run(() => BakePixels(grass, dirt, heights, size, half, level, seed));
        _ready = true;
    }

    /// <summary>Builds the ground mesh of one level of detail, with the ground's texture on it.</summary>
    private void BuildLod(int level)
    {
        var mesh = new TerrainMesh(_set.Heights, TerrainData.Size, TerrainData.Half, LodTolerance[level]);
        Mesh raw = default;
        raw.VertexCount = mesh.VertexCount;
        raw.TriangleCount = mesh.TriangleCount;
        raw.Vertices = (float*)Raylib.MemAlloc((uint)(mesh.Positions.Length * sizeof(float)));
        raw.Normals = (float*)Raylib.MemAlloc((uint)(mesh.Normals.Length * sizeof(float)));
        raw.TexCoords = (float*)Raylib.MemAlloc((uint)(mesh.TexCoords.Length * sizeof(float)));
        raw.Indices = (ushort*)Raylib.MemAlloc((uint)(mesh.Indices.Length * sizeof(ushort)));
        for (int i = 0; i < mesh.Positions.Length; i++)
        {
            raw.Vertices[i] = mesh.Positions[i];
            raw.Normals[i] = mesh.Normals[i];
        }
        for (int i = 0; i < mesh.TexCoords.Length; i++)
            raw.TexCoords[i] = mesh.TexCoords[i];
        for (int i = 0; i < mesh.Indices.Length; i++)
            raw.Indices[i] = mesh.Indices[i];
        Raylib.UploadMesh(ref raw, false);
        _lods[level] = Raylib.LoadModelFromMesh(raw);
        _lodBuilt[level] = true;
        if (_groundTexture.Id != 0)
            _lods[level].Materials[0].Maps[(int)MaterialMapIndex.Albedo].Texture = _groundTexture;

    }

    private void Release()
    {
        if (!_ready)
            return;
        _bake = null; // Its result, if it ever finishes, is not wanted.
        for (int i = 0; i < _lods.Length; i++)
        {
            if (_lodBuilt[i])
                Raylib.UnloadModel(_lods[i]);
            _lodBuilt[i] = false;
        }
        _ready = false;
    }

    /// <summary>Once the texture has been worked out, puts it on the ground.</summary>
    private void ApplyBake()
    {
        if (_bake is not { IsCompleted: true } task)
            return;
        _bake = null;
        if (!task.IsCompletedSuccessfully)
        {
            Console.Error.WriteLine($"Garden Guardians: couldn't bake the ground texture: {task.Exception?.GetBaseException().Message}");
            return;
        }
        byte[] pixels = task.Result;
        Image image = Raylib.GenImageColor(TextureSize, TextureSize, Color.White);
        fixed (byte* source = pixels)
            Buffer.MemoryCopy(source, image.Data, pixels.Length, pixels.Length);
        Texture2D texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        Raylib.GenTextureMipmaps(ref texture);
        Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
        _groundTexture = texture;
        for (int i = 0; i < _lods.Length; i++)
        {
            if (_lodBuilt[i])
                _lods[i].Materials[0].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
        }
        Raylib.UnloadTexture(_flat);
    }

    // --- The ground's texture --------------------------------------------------------------------

    /// <summary>The colours of a tile file as bytes (R, G, B, per pixel), and its width.</summary>
    private static (byte[] Pixels, int Width) LoadTile(string name)
    {
        Image image = Raylib.LoadImage(AssetPath("Textures/Ground/" + name));
        Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
        int width = image.Width;
        var pixels = new byte[width * image.Height * 3];
        Color* source = (Color*)image.Data;
        for (int i = 0; i < width * image.Height; i++)
        {
            pixels[i * 3] = source[i].R;
            pixels[i * 3 + 1] = source[i].G;
            pixels[i * 3 + 2] = source[i].B;
        }
        Raylib.UnloadImage(image);
        return (pixels, width);
    }

    private static float Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x800000 - 1f;
    }

    /// <summary>Smooth noise, about -2..2, that changes over <paramref name="metres"/>.</summary>
    private static float Noise(float x, float z, float metres, int seed)
    {
        float fx = x / metres, fz = z / metres;
        int ix = (int)MathF.Floor(fx), iz = (int)MathF.Floor(fz);
        float tx = fx - ix, tz = fz - iz;
        tx = tx * tx * (3f - 2f * tx);
        tz = tz * tz * (3f - 2f * tz);
        float a = Hash(ix, iz, seed), b = Hash(ix + 1, iz, seed), c = Hash(ix, iz + 1, seed), d = Hash(ix + 1, iz + 1, seed);
        return 2.2f * (a + (b - a) * tx + (c - a) * tz + (a - b - c + d) * tx * tz);
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>A value between a grid's samples (the grid spans -half..half, <paramref name="cell"/> metres between samples, <paramref name="n"/> along a side).</summary>
    private static float Bilinear(float[] grid, int n, float half, float cell, float x, float z)
    {
        float fx = Math.Clamp((x + half) / cell, 0f, n - 1.001f), fz = Math.Clamp((z + half) / cell, 0f, n - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        float near = grid[iz * n + ix] + (grid[iz * n + ix + 1] - grid[iz * n + ix]) * tx;
        float far = grid[(iz + 1) * n + ix] + (grid[(iz + 1) * n + ix + 1] - grid[(iz + 1) * n + ix]) * tx;
        return near + (far - near) * tz;
    }

    /// <summary>
    /// The ground's colours as bytes (R, G, B, A per pixel, <see cref="TextureSize"/> square): the grass tiles mixed by slow noise,
    /// dirt patches on level ground, sand round the ponds, the ponds' beds, and the hills' shading. Pure arithmetic on what it's given,
    /// so it can run on any thread.
    /// </summary>
    private static byte[] BakePixels((byte[] Pixels, int Width)[] grass, (byte[] Pixels, int Width)[] dirt, float[] heights, int size, float half, float waterLevel, int seed)
    {
        float step = 2f * half / (size - 1);

        // The ground's slope (rise per metre) and its shading, per height sample.
        var slope = new float[size * size];
        var shade = new float[size * size];
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                float gx = (heights[j * size + Math.Min(i + 1, size - 1)] - heights[j * size + Math.Max(i - 1, 0)]) / (2f * step);
                float gz = (heights[Math.Min(j + 1, size - 1) * size + i] - heights[Math.Max(j - 1, 0) * size + i]) / (2f * step);
                slope[j * size + i] = MathF.Sqrt(gx * gx + gz * gz);
                shade[j * size + i] = 1f + Math.Clamp((-gx - gz) * 2.5f, -0.10f, 0.10f);
            }
        }

        // The noise, on a coarse grid: how much of each grass tile, a light and dark wobble, and where dirt patches lie.
        int n = (int)MathF.Ceiling(2f * half / FieldMetres) + 1;
        var mix = new float[Grasses][];
        for (int k = 0; k < Grasses; k++)
            mix[k] = new float[n * n];
        var tint = new float[n * n];
        var patches = new float[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                float x = -half + i * FieldMetres, z = -half + j * FieldMetres;
                float total = 0f;
                for (int k = 0; k < Grasses; k++)
                {
                    mix[k][j * n + i] = MathF.Exp(1.2f * Noise(x, z, 6f, seed + 11 * k));
                    total += mix[k][j * n + i];
                }
                for (int k = 0; k < Grasses; k++)
                    mix[k][j * n + i] /= total;
                tint[j * n + i] = 1f + 0.10f * Noise(x, z, 9f, seed + 77);
                patches[j * n + i] = SmoothStep((Noise(x, z, 5f, seed + 55) - 1.15f) / 0.5f);
            }
        }

        Vector3 Tile((byte[] Pixels, int Width) tile, float x, float z, float offsetX, float offsetZ)
        {
            float u = (x + offsetX) / TileMetres, v = (z + offsetZ) / TileMetres;
            u -= MathF.Floor(u);
            v -= MathF.Floor(v);
            int px = Math.Min((int)(u * tile.Width), tile.Width - 1), py = Math.Min((int)(v * tile.Width), tile.Width - 1);
            int at = (py * tile.Width + px) * 3;
            return new Vector3(tile.Pixels[at], tile.Pixels[at + 1], tile.Pixels[at + 2]);
        }

        var result = new byte[TextureSize * TextureSize * 4];
        float metresPerPixel = 2f * half / TextureSize;
        Parallel.For(0, TextureSize, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) }, py =>
        {
            float z = -half + (py + 0.5f) * metresPerPixel;
            for (int px = 0; px < TextureSize; px++)
            {
                float x = -half + (px + 0.5f) * metresPerPixel;
                float h = Bilinear(heights, size, half, step, x, z);

                Vector3 colour = Vector3.Zero;
                for (int k = 0; k < Grasses; k++)
                    colour += Bilinear(mix[k], n, half, FieldMetres, x, z) * Tile(grass[k], x, z, k * 2.3f, k * 1.7f);
                colour *= Bilinear(tint, n, half, FieldMetres, x, z);

                // Dirt: patches on the level ground; a band of it round each pond.
                Vector3 earth = Tile(dirt[0], x, z, 0f, 0f);
                float patch = Bilinear(patches, n, half, FieldMetres, x, z) * SmoothStep((0.12f - Bilinear(slope, size, half, step, x, z)) / 0.06f);
                colour = colour * (1f - patch) + earth * patch;
                float shore = SmoothStep((waterLevel + 0.5f - h) / 0.35f);
                Vector3 sand = Tile(dirt[Dirts - 1], x, z, 1f, 1f) * 1.08f;
                colour = colour * (1f - shore) + sand * shore;

                // Under the water: the pond's bed, darker the deeper it goes.
                if (h < waterLevel)
                {
                    float deep = SmoothStep((waterLevel - h) / 0.6f);
                    colour = earth * 0.7f * (1f - deep) + new Vector3(50f, 70f, 60f) * deep;
                }
                colour *= Bilinear(shade, size, half, step, x, z);
                int at = (py * TextureSize + px) * 4;
                result[at] = (byte)Math.Clamp(colour.X, 0f, 255f);
                result[at + 1] = (byte)Math.Clamp(colour.Y, 0f, 255f);
                result[at + 2] = (byte)Math.Clamp(colour.Z, 0f, 255f);
                result[at + 3] = 255;
            }
        });
        return result;
    }

    // --- Drawing ---------------------------------------------------------------------------------

    /// <summary>Draws the ground and the props, the season's tint multiplied over both and (for a tint that brightens) added over the ground.</summary>
    public void Draw(Color multiply, Color? additive)
    {
        EnsureBuilt();
        ApplyBake();
        int level = LodTolerance.Length - 1;
        float focusDistance = Vector3.Distance(Eye, Focus);
        while (level > 0 && focusDistance < LodFrom[level])
            level--;
        if (!_lodBuilt[level])
            BuildLod(level);
        Model ground = _lods[level];
        GroundTolerance = LodTolerance[level];
        Raylib.DrawModel(ground, Vector3.Zero, 1f, multiply);
        if (_set.Props is { } props)
        {
            Vector3 forward = Vector3.Normalize(Focus - Eye);
            // A cone round the view's axis reaching the screen's far corners; a prop wholly outside it is behind or beside the picture.
            float halfView = MathF.Atan(MathF.Tan(FovDegrees * MathF.PI / 360f) * MathF.Sqrt(1f + Aspect * Aspect));
            foreach (PlacedProp prop in props)
            {
                if (!InView(prop, forward, halfView))
                    continue;
                DrawProp(prop, multiply);
            }
        }
        if (additive is not { } glow)
            return;
        Rlgl.DrawRenderBatchActive();
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawModel(ground, Vector3.Zero, 1f, glow);
        Raylib.EndBlendMode();
    }

    /// <summary>False if <paramref name="prop"/> (a sphere of its own reach) lies entirely outside the camera's view cone.</summary>
    private static bool InView(PlacedProp prop, Vector3 forward, float halfView)
    {
        var to = new Vector3(prop.X - Eye.X, prop.Base + prop.Item.Height * prop.Scale * 0.5f - Eye.Y, prop.Z - Eye.Z);
        float distance = to.Length();
        float radius = MathF.Max(prop.Item.Reach, prop.Item.Height) * prop.Scale;
        if (distance <= radius)
            return true; // The camera is at or in it.
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(to / distance, forward), -1f, 1f));
        return angle - MathF.Asin(Math.Min(1f, radius / distance)) <= halfView;
    }

    private static Model PropModel(KitItem item)
    {
        if (!PropModels.TryGetValue(item.File, out Model model))
        {
            model = Raylib.LoadModel(AssetPath("Models/Procedural/" + item.File));
            if (item.Kind != KitKind.Rock)
            {
                // Oaks and plants wear big pictures: smoothed and mipmapped, they don't show as stair-stepped pixels
                // (raylib draws textures sharp by default). The rocks keep the sharp, rough look.
                for (int m = 0; m < model.MaterialCount; m++)
                {
                    Texture2D texture = model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture;
                    Raylib.GenTextureMipmaps(ref texture);
                    Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
                    model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
                }
            }
            PropModels[item.File] = model;
        }
        return model;
    }

    private static void DrawProp(PlacedProp prop, Color tint)
    {
        Model model = PropModel(prop.Item);
        Rlgl.PushMatrix();
        Rlgl.Translatef(prop.X, prop.Base, prop.Z);
        Rlgl.Rotatef(-prop.Yaw * 180f / MathF.PI, 0f, 1f, 0f); // Raylib turns the other way about y than the x-z plane's angle runs.
        Rlgl.Scalef(prop.Scale, prop.Scale, prop.Scale);
        for (int m = 0; m < model.MeshCount; m++)
        {
            Raylib_cs.Material material = model.Materials[model.MeshMaterial[m]];
            material.Maps[(int)MaterialMapIndex.Albedo].Color = tint;
            Raylib.DrawMesh(model.Meshes[m], material, Matrix4x4.Identity);
        }
        Rlgl.PopMatrix();
    }
}
