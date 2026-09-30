using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Draws the worn dirt paths and paved roads (see World.Trails) as textured ground-hugging meshes: dirt on the
/// paths, cobblestones on the roads, each texture laid by world position so neighbouring squares join up. Each
/// corner carries its own opacity, lower on the outside of a patch, so the edges fade into the grass.
/// </summary>
public sealed unsafe class TrailRenderer
{
    /// <summary>One square of trail: its four corners (in the order the world lists them), whether it's road, and each corner's opacity (0-255).</summary>
    public readonly record struct Square(Vector3 A, Vector3 B, Vector3 C, Vector3 D, bool Paved, byte AlphaA, byte AlphaB, byte AlphaC, byte AlphaD);

    /// <summary>Squares per mesh, so a big road network stays in a few meshes.</summary>
    private const int SquaresPerMesh = 8000;

    /// <summary>A texture repeats every this many metres across and down (the cobble picture is squeezed square to load, so its width is stretched back out here).</summary>
    private const float RoadTileX = 3.66f, RoadTileZ = 2f, PathTile = 3f;

    private static Texture2D _roadTexture, _pathTexture;
    private static bool _texturesLoaded;

    private readonly List<Model> _paths = new(), _roads = new();

    private static string AssetPath(string relative) => OperatingSystem.IsAndroid()
        ? relative
        : Path.Combine(AppContext.BaseDirectory, "Assets", relative);

    private static Texture2D LoadTile(string name, int size)
    {
        Image image = Raylib.LoadImage(AssetPath("Textures/Ground/" + name));
        Raylib.ImageResize(ref image, size, size); // A power of two, so it repeats and mipmaps on any device.
        Texture2D texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        Raylib.GenTextureMipmaps(ref texture);
        Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
        Raylib.SetTextureWrap(texture, TextureWrap.Repeat);
        return texture;
    }

    private static void EnsureTextures()
    {
        if (_texturesLoaded)
            return;
        _texturesLoaded = true;
        _roadTexture = LoadTile("cobble_1.png", 512);
        _pathTexture = LoadTile("dirt_1.png", 256);
    }

    /// <summary>Replaces the meshes with ones for <paramref name="squares"/>.</summary>
    public void Rebuild(List<Square> squares)
    {
        EnsureTextures();
        Clear();
        Build(squares, paved: false, _paths, _pathTexture);
        Build(squares, paved: true, _roads, _roadTexture);
    }

    public void Draw()
    {
        foreach (Model model in _paths)
            Raylib.DrawModel(model, Vector3.Zero, 1f, Color.White);
        foreach (Model model in _roads)
            Raylib.DrawModel(model, Vector3.Zero, 1f, Color.White);
    }

    public void Clear()
    {
        foreach (Model model in _paths.Concat(_roads))
            Unload(model);
        _paths.Clear();
        _roads.Clear();
    }

    private static void Unload(Model model)
    {
        // The textures are shared and kept; make sure unloading the model can't take one with it.
        model.Materials[0].Maps[(int)MaterialMapIndex.Albedo].Texture = default;
        Raylib.UnloadModel(model);
    }

    private static void Build(List<Square> squares, bool paved, List<Model> into, Texture2D texture)
    {
        float tileX = paved ? RoadTileX : PathTile, tileZ = paved ? RoadTileZ : PathTile;
        var batch = new List<Square>(SquaresPerMesh);
        foreach (Square square in squares)
        {
            if (square.Paved != paved)
                continue;
            batch.Add(square);
            if (batch.Count == SquaresPerMesh)
            {
                into.Add(MakeModel(batch, texture, tileX, tileZ));
                batch.Clear();
            }
        }
        if (batch.Count > 0)
            into.Add(MakeModel(batch, texture, tileX, tileZ));
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
            // The same two triangles the flat version drew: (A, D, C) and (A, C, B).
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
