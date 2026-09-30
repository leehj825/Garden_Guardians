using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A layer of textured squares laid on (or just over) the ground — paths, roads, the pond's water, flood water, drought
/// mud. The texture is laid by world position, so neighbouring squares join up, and each corner has its own opacity so a
/// patch can fade out at its edges. Meshes are rebuilt only when the squares change.
/// </summary>
public sealed unsafe class SquareLayer
{
    /// <summary>One square: its four corners (in the order the world lists them: A, B along x, then C, D back along it) and each corner's opacity (0-255).</summary>
    public readonly record struct Square(Vector3 A, Vector3 B, Vector3 C, Vector3 D, byte AlphaA, byte AlphaB, byte AlphaC, byte AlphaD);

    /// <summary>Squares per mesh, so a big layer stays in a few meshes.</summary>
    private const int SquaresPerMesh = 8000;

    private static readonly Dictionary<string, Texture2D> Tiles = new();

    private readonly List<Model> _models = new();

    private static string AssetPath(string relative) => OperatingSystem.IsAndroid()
        ? relative
        : Path.Combine(AppContext.BaseDirectory, "Assets", relative);

    /// <summary>A ground tile (a PNG - the Android raylib can't read JPGs) squeezed to <paramref name="size"/> pixels square (a power of two, so it repeats and mipmaps on any device); loaded once.</summary>
    public static Texture2D Tile(string name, int size)
    {
        if (Tiles.TryGetValue(name, out Texture2D cached))
            return cached;
        Image image = Raylib.LoadImage(AssetPath("Textures/Ground/" + name));
        Raylib.ImageResize(ref image, size, size);
        Texture2D texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        Raylib.GenTextureMipmaps(ref texture);
        Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
        Raylib.SetTextureWrap(texture, TextureWrap.Repeat);
        Tiles[name] = texture;
        return texture;
    }

    /// <summary>Replaces the meshes with ones for <paramref name="squares"/>, the texture repeating every <paramref name="tileX"/> by <paramref name="tileZ"/> metres.</summary>
    public void Rebuild(IEnumerable<Square> squares, Texture2D texture, float tileX, float tileZ)
    {
        Clear();
        var batch = new List<Square>(SquaresPerMesh);
        foreach (Square square in squares)
        {
            batch.Add(square);
            if (batch.Count == SquaresPerMesh)
            {
                _models.Add(MakeModel(batch, texture, tileX, tileZ));
                batch.Clear();
            }
        }
        if (batch.Count > 0)
            _models.Add(MakeModel(batch, texture, tileX, tileZ));
    }

    public void Draw()
    {
        foreach (Model model in _models)
            Raylib.DrawModel(model, Vector3.Zero, 1f, Color.White);
    }

    public void Clear()
    {
        foreach (Model model in _models)
        {
            // The textures are shared and kept; make sure unloading the model can't take one with it.
            model.Materials[0].Maps[(int)MaterialMapIndex.Albedo].Texture = default;
            Raylib.UnloadModel(model);
        }
        _models.Clear();
    }

    private static Model MakeModel(List<Square> batch, Texture2D texture, float tileX, float tileZ)
    {
        int vertices = batch.Count * 6;
        Mesh mesh = default;
        mesh.VertexCount = vertices;
        mesh.TriangleCount = batch.Count * 2;
        mesh.Vertices = (float*)Raylib.MemAlloc((uint)(vertices * 3 * sizeof(float)));
        mesh.Normals = (float*)Raylib.MemAlloc((uint)(vertices * 3 * sizeof(float)));
        mesh.TexCoords = (float*)Raylib.MemAlloc((uint)(vertices * 2 * sizeof(float)));
        mesh.Colors = (byte*)Raylib.MemAlloc((uint)(vertices * 4));

        int v = 0;
        void Put(Vector3 p, byte alpha)
        {
            mesh.Vertices[v * 3] = p.X;
            mesh.Vertices[v * 3 + 1] = p.Y;
            mesh.Vertices[v * 3 + 2] = p.Z;
            mesh.Normals[v * 3] = 0f;
            mesh.Normals[v * 3 + 1] = 1f;
            mesh.Normals[v * 3 + 2] = 0f;
            mesh.TexCoords[v * 2] = p.X / tileX;
            mesh.TexCoords[v * 2 + 1] = p.Z / tileZ;
            mesh.Colors[v * 4] = 255;
            mesh.Colors[v * 4 + 1] = 255;
            mesh.Colors[v * 4 + 2] = 255;
            mesh.Colors[v * 4 + 3] = alpha;
            v++;
        }

        foreach (Square s in batch)
        {
            // Two triangles: (A, D, C) and (A, C, B).
            Put(s.A, s.AlphaA);
            Put(s.D, s.AlphaD);
            Put(s.C, s.AlphaC);
            Put(s.A, s.AlphaA);
            Put(s.C, s.AlphaC);
            Put(s.B, s.AlphaB);
        }

        Raylib.UploadMesh(ref mesh, false);
        Model model = Raylib.LoadModelFromMesh(mesh);
        model.Materials[0].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
        return model;
    }
}
