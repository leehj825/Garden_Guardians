using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Draws a generated terrain (see <see cref="TerrainGenerator"/>): the ground as a simplified mesh
/// (<see cref="TerrainMesh"/>) under one texture baked from the models' own grass and dirt tiles, and the props
/// cut out of the hand-made models. Everything is built the first time it's drawn (that needs a GPU context).
/// </summary>
public sealed unsafe class ProceduralView
{
    /// <summary>The ground mesh may be off the height grid by this much (m).</summary>
    private const float MeshTolerance = 0.04f;

    /// <summary>The baked ground texture is this many pixels square (a power of two, so it can be mipmapped).</summary>
    private const int TextureSize = 2048;

    /// <summary>A ground tile repeats every this many metres.</summary>
    private const float TileMetres = 6f;

    private const int Grasses = 4, Dirts = 2;

    private readonly TerrainSet _set;
    private Model _ground;
    private bool _ready;

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
        var mesh = new TerrainMesh(_set.Heights, TerrainData.Size, TerrainData.Half, MeshTolerance);
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
        _ground = Raylib.LoadModelFromMesh(raw);
        _ground.Materials[0].Maps[(int)MaterialMapIndex.Albedo].Texture = BakeGround();
        _ready = true;
    }

    private void Release()
    {
        if (!_ready)
            return;
        Raylib.UnloadModel(_ground);
        _ready = false;
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

    private Texture2D BakeGround()
    {
        var grass = new (byte[] Pixels, int Width)[Grasses];
        var dirt = new (byte[] Pixels, int Width)[Dirts];
        for (int k = 0; k < Grasses; k++)
            grass[k] = LoadTile($"grass_{k + 1}.png");
        for (int k = 0; k < Dirts; k++)
            dirt[k] = LoadTile($"dirt_{k + 1}.png");

        float half = TerrainData.Half;
        int size = TerrainData.Size;
        float step = TerrainData.Step;
        float[] heights = _set.Heights;
        float waterLevel = _set.PondLevel;
        int seed = _set.Seed;

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

        float Bilinear(float[] grid, float x, float z)
        {
            float fx = Math.Clamp((x + half) / step, 0f, size - 1.001f), fz = Math.Clamp((z + half) / step, 0f, size - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            float near = grid[iz * size + ix] + (grid[iz * size + ix + 1] - grid[iz * size + ix]) * tx;
            float far = grid[(iz + 1) * size + ix] + (grid[(iz + 1) * size + ix + 1] - grid[(iz + 1) * size + ix]) * tx;
            return near + (far - near) * tz;
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

        Image image = Raylib.GenImageColor(TextureSize, TextureSize, Color.White);
        Color* pixels = (Color*)image.Data;
        float metresPerPixel = 2f * half / TextureSize;
        var weights = new float[Grasses];
        for (int py = 0; py < TextureSize; py++)
        {
            float z = -half + (py + 0.5f) * metresPerPixel;
            for (int px = 0; px < TextureSize; px++)
            {
                float x = -half + (px + 0.5f) * metresPerPixel;
                float h = Bilinear(heights, x, z);

                // Grass: the tiles mixed by slow noise, so no one tile shows its repeat.
                float total = 0f;
                for (int k = 0; k < Grasses; k++)
                {
                    weights[k] = MathF.Exp(1.2f * Noise(x, z, 6f, seed + 11 * k));
                    total += weights[k];
                }
                Vector3 colour = Vector3.Zero;
                for (int k = 0; k < Grasses; k++)
                    colour += weights[k] / total * Tile(grass[k], x, z, k * 2.3f, k * 1.7f);
                colour *= 1f + 0.10f * Noise(x, z, 9f, seed + 77);

                // Dirt: patches on the level ground; a band of it round each pond.
                Vector3 earth = Tile(dirt[0], x, z, 0f, 0f);
                float patch = SmoothStep((Noise(x, z, 5f, seed + 55) - 1.15f) / 0.5f) * SmoothStep((0.12f - Bilinear(slope, x, z)) / 0.06f);
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
                colour *= Bilinear(shade, x, z);
                pixels[py * TextureSize + px] = new Color(
                    (byte)Math.Clamp(colour.X, 0f, 255f), (byte)Math.Clamp(colour.Y, 0f, 255f), (byte)Math.Clamp(colour.Z, 0f, 255f), (byte)255);
            }
        }
        Texture2D texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        Raylib.GenTextureMipmaps(ref texture);
        Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
        return texture;
    }

    // --- Drawing ---------------------------------------------------------------------------------

    /// <summary>Draws the ground and the props, the season's tint multiplied over both and (for a tint that brightens) added over the ground.</summary>
    public void Draw(Color multiply, Color? additive)
    {
        EnsureBuilt();
        Raylib.DrawModel(_ground, Vector3.Zero, 1f, multiply);
        if (_set.Props is { } props)
        {
            foreach (PlacedProp prop in props)
                DrawProp(prop, multiply);
        }
        if (additive is not { } glow)
            return;
        Rlgl.DrawRenderBatchActive();
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawModel(_ground, Vector3.Zero, 1f, glow);
        Raylib.EndBlendMode();
    }

    private static Model PropModel(KitItem item)
    {
        if (!PropModels.TryGetValue(item.File, out Model model))
        {
            model = Raylib.LoadModel(AssetPath("Models/Procedural/" + item.File));
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
