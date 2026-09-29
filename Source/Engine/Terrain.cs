using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The flat backyard ground: a green plane lying on y = 0 with a grid overlay
/// so that scale (1 cell = 1 meter) is easy to read.
/// </summary>
public sealed unsafe class Terrain
{
    /// <summary>The terrain surface height. Everything rests on this plane.</summary>
    public const float GroundHeight = 0f;

    /// <summary>Edge length of the square terrain, in meters.</summary>
    public float Size { get; }

    public Terrain(float size) => Size = size;

    /// <summary>True if the (x, z) point lies on the terrain surface.</summary>
    public bool Contains(Vector3 point)
    {
        float half = Size / 2f;
        return point.X >= -half && point.X <= half && point.Z >= -half && point.Z <= half;
    }

    /// <summary>
    /// True if the (x, z) point is on the terrain and at least
    /// <paramref name="margin"/> meters away from every edge.
    /// </summary>
    public bool Contains(Vector3 point, float margin)
    {
        float half = Size / 2f - margin;
        return point.X >= -half && point.X <= half && point.Z >= -half && point.Z <= half;
    }

    /// <summary>A uniformly random ground point, keeping <paramref name="margin"/> meters from the edges.</summary>
    public Vector3 RandomPoint(Random rng, float margin)
    {
        float half = Size / 2f - margin;
        float x = (float)(rng.NextDouble() * 2 - 1) * half;
        float z = (float)(rng.NextDouble() * 2 - 1) * half;
        return new Vector3(x, GroundHeight, z);
    }

    /// <summary>
    /// Intersects a ray with the infinite horizontal plane y = GroundHeight.
    /// Returns null if the ray is parallel to the plane, points away from it,
    /// or hits it outside the terrain bounds.
    /// </summary>
    public Vector3? Raycast(Ray ray)
    {
        Vector3? hit = RaycastGroundPlane(ray);
        return hit is not null && Contains(hit.Value) ? hit : null;
    }

    /// <summary>
    /// Intersects a ray with the infinite horizontal plane y = GroundHeight,
    /// with no bound on where that point falls — unlike <see cref="Raycast"/>,
    /// a hit off the edge of the terrain (or well beyond it) still counts.
    /// </summary>
    public Vector3? RaycastGroundPlane(Ray ray)
    {
        // Plane: y = GroundHeight. Ray: P(t) = origin + t·direction.
        // Solve origin.y + t·direction.y = GroundHeight for t.
        if (MathF.Abs(ray.Direction.Y) < 1e-6f)
            return null; // Parallel to the ground — never intersects.

        float t = (GroundHeight - ray.Position.Y) / ray.Direction.Y;
        if (t < 0f)
            return null; // Intersection is behind the camera.

        return ray.Position + ray.Direction * t;
    }

    /// <summary>
    /// Part 3, The Lush 3D Lawn: edge length (m) of each ground cell the
    /// hilly lawn is drawn in — 2x2m, per the spec.
    /// </summary>
    private const float CellSize = 2f;

    /// <summary>Forest Green — the lawn's single base grass color, height-tinted per cell (see <see cref="Draw"/>) rather than alternated in a checkerboard.</summary>
    private static readonly Color GrassBase = new(34, 139, 34, 255);

    /// <summary>Yellow-Green — sunlit tint blended in for a cell's higher (peak) ground.</summary>
    private static readonly Color GrassPeak = new(154, 205, 50, 255);

    /// <summary>Dark shadow-green — blended in for a cell's lower (valley) ground.</summary>
    private static readonly Color GrassValley = new(20, 80, 20, 255);

    /// <summary>Brown dirt patch color, scattered deterministically across the lawn as a rare, sparse embellishment.</summary>
    private static readonly Color Dirt = new(120, 85, 55, 255);

    /// <summary>How far a dirt patch cell blends from the grass toward <see cref="Dirt"/> (1 = bare dirt).</summary>
    private const float DirtPatchStrength = 0.6f;

    /// <summary>
    /// The height function's total amplitude (sum of its three stacked
    /// sine/cosine terms' coefficients — see <see cref="World.GetHeightAt"/>),
    /// used to normalize a cell's average height into a -1..1 tint factor.
    /// </summary>
    private const float HeightAmplitude = 5.5f;

    /// <summary>Component-wise linear interpolation between two colors, alpha fixed at 255.</summary>
    private static Color LerpColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            (byte)255);
    }

    /// <summary>
    /// Deterministic (not System.Random) hash of a cell's integer indices,
    /// so the lawn's dirt-patch pattern is stable and reproducible frame to
    /// frame rather than flickering.
    /// </summary>
    private static uint CellHash(int cx, int cz)
    {
        unchecked
        {
            uint h = (uint)(cx * 374761393 + cz * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    /// <summary>
    /// Part 3: draws the whole 100x100m lawn as a single smooth-shaded mesh
    /// — one vertex per 2x2m grid corner, shared between its neighbouring
    /// cells, coloured and lit from its own exact height and surface normal
    /// (<see cref="World.GetHeightAt"/>/<see cref="World.GetNormalAt"/>)
    /// rather than one flat colour per cell. Flat per-cell triangles (this
    /// mesh's predecessor: <see cref="Raylib.DrawTriangle3D"/>, one solid
    /// colour each) gave every cell boundary a hard-edged seam — cosmetically
    /// a patchwork, but visually indistinguishable from an actual terrace,
    /// so a Bramblekin smoothly climbing the real (continuous) slope looked
    /// like it was climbing stairs. The vertex positions and normals never
    /// change, so the mesh is built once (<see cref="EnsureMesh"/>); only its
    /// vertex colours are re-blended toward <paramref name="seasonTint"/> and
    /// re-uploaded, and only when that blend actually changes (see
    /// <see cref="UpdateSeasonColors"/>) rather than every frame.
    /// </summary>
    public void Draw(Color seasonTint, float seasonAmount)
    {
        EnsureMesh();
        UpdateSeasonColors(seasonTint, seasonAmount);
        Raylib.DrawModel(_terrainModel, Vector3.Zero, 1f, Color.White);
    }

    private Model _terrainModel;
    private bool _meshReady;
    private int _vertsPerSide;
    private Color[] _baseColors = [];
    private Color _lastSeasonTint;
    private float _lastSeasonAmount = -1f; // Never matches a real amount on the first call.

    /// <summary>Builds the terrain <see cref="Model"/> the first time it's drawn (needs a GPU context, so not eager) — one shared vertex per grid corner, positioned and normaled from the exact same height function a Bramblekin's own feet follow.</summary>
    private void EnsureMesh()
    {
        if (_meshReady)
            return;

        float half = Size / 2f;
        int cellsPerSide = (int)MathF.Round(Size / CellSize);
        _vertsPerSide = cellsPerSide + 1;
        int vertexCount = _vertsPerSide * _vertsPerSide;
        int triangleCount = cellsPerSide * cellsPerSide * 2;

        var mesh = new Mesh { VertexCount = vertexCount, TriangleCount = triangleCount };
        mesh.Vertices = (float*)NativeMemory.Alloc((nuint)(vertexCount * 3), sizeof(float));
        mesh.Normals = (float*)NativeMemory.Alloc((nuint)(vertexCount * 3), sizeof(float));
        mesh.Colors = (byte*)NativeMemory.Alloc((nuint)(vertexCount * 4), sizeof(byte));
        mesh.Indices = (ushort*)NativeMemory.Alloc((nuint)(triangleCount * 3), sizeof(ushort));

        _baseColors = new Color[vertexCount];
        for (int j = 0; j < _vertsPerSide; j++)
        {
            for (int i = 0; i < _vertsPerSide; i++)
            {
                float x = -half + i * CellSize;
                float z = -half + j * CellSize;
                float y = World.GetHeightAt(x, z);
                Vector3 normal = World.GetNormalAt(x, z);

                int v = j * _vertsPerSide + i;
                mesh.Vertices[v * 3 + 0] = x;
                mesh.Vertices[v * 3 + 1] = y;
                mesh.Vertices[v * 3 + 2] = z;
                mesh.Normals[v * 3 + 0] = normal.X;
                mesh.Normals[v * 3 + 1] = normal.Y;
                mesh.Normals[v * 3 + 2] = normal.Z;

                // Organic height-based tinting: a single Forest Green base,
                // lightened toward a sunlit yellow-green on peaks and
                // darkened toward a shadowed green in valleys — no
                // checkerboard, just this vertex's own elevation. A rare,
                // sparse dirt fleck (~1 vertex in 40) is worn into it too.
                float t = Math.Clamp(y / HeightAmplitude, -1f, 1f);
                Color color = t >= 0f ? LerpColor(GrassBase, GrassPeak, t) : LerpColor(GrassBase, GrassValley, -t);
                if (CellHash(i, j) % 40 == 0)
                    color = LerpColor(color, Dirt, DirtPatchStrength);
                _baseColors[v] = color;
            }
        }

        int index = 0;
        for (int j = 0; j < cellsPerSide; j++)
        {
            for (int i = 0; i < cellsPerSide; i++)
            {
                ushort v00 = (ushort)(j * _vertsPerSide + i);
                ushort v10 = (ushort)(j * _vertsPerSide + i + 1);
                ushort v01 = (ushort)((j + 1) * _vertsPerSide + i);
                ushort v11 = (ushort)((j + 1) * _vertsPerSide + i + 1);

                // Same winding as the flat triangles this replaced: upward-facing when viewed from above/+Y.
                mesh.Indices[index++] = v00; mesh.Indices[index++] = v01; mesh.Indices[index++] = v11;
                mesh.Indices[index++] = v00; mesh.Indices[index++] = v11; mesh.Indices[index++] = v10;
            }
        }

        // Dynamic: UpdateSeasonColors re-uploads the colour buffer whenever the season's blend changes.
        Raylib.UploadMesh(ref mesh, true);
        _terrainModel = Raylib.LoadModelFromMesh(mesh);
        _meshReady = true;
    }

    /// <summary>Re-blends every vertex's <see cref="_baseColors"/> toward <paramref name="seasonTint"/> and re-uploads them — skipped when the blend hasn't actually moved since last frame, so a settled season costs nothing every frame.</summary>
    private void UpdateSeasonColors(Color seasonTint, float seasonAmount)
    {
        bool tintChanged = seasonTint.R != _lastSeasonTint.R || seasonTint.G != _lastSeasonTint.G || seasonTint.B != _lastSeasonTint.B;
        if (!tintChanged && MathF.Abs(seasonAmount - _lastSeasonAmount) < 0.001f)
            return;
        _lastSeasonTint = seasonTint;
        _lastSeasonAmount = seasonAmount;

        var blended = new byte[_baseColors.Length * 4];
        for (int v = 0; v < _baseColors.Length; v++)
        {
            Color color = seasonAmount > 0f ? LerpColor(_baseColors[v], seasonTint, seasonAmount) : _baseColors[v];
            blended[v * 4 + 0] = color.R;
            blended[v * 4 + 1] = color.G;
            blended[v * 4 + 2] = color.B;
            blended[v * 4 + 3] = color.A;
        }
        Raylib.UpdateMeshBuffer(_terrainModel.Meshes[0], 3, (ReadOnlySpan<byte>)blended, 0);
    }
}
