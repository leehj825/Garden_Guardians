using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The backyard ground: a 100 m square modelled in 3D (grass, dirt patches,
/// two ponds, and the Giant Oak's foot) and drawn from
/// <c>Assets/Models/Terrain/terrain.glb</c>. What walkers stand on is the
/// height grid sampled from that model (see <see cref="TerrainData"/>).
/// </summary>
public sealed class Terrain
{
    /// <summary>The terrain surface height. Everything rests on this plane.</summary>
    public const float GroundHeight = 0f;

    /// <summary>Edge length of the square terrain, in meters.</summary>
    public float Size { get; }

    /// <summary>The ground of terrain <paramref name="terrainIndex"/> (see <see cref="TerrainData"/>) — choosing it, if it isn't the one in use.</summary>
    public Terrain(int terrainIndex = 0)
    {
        TerrainData.Select(terrainIndex);
        Size = 2f * TerrainData.Half;
        _modelFile = TerrainData.Current.ModelFile;
        if (TerrainData.Current.IsProcedural)
            _procedural = new ProceduralView(TerrainData.Current);
    }

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
    /// Where a terrain model lives: on Android the packaged asset (the
    /// "Assets/" prefix is dropped), on desktop next to the executable — see
    /// <c>BramblekinModel.AssetPath</c> for why.
    /// </summary>
    private static string ModelPathOf(string file) => OperatingSystem.IsAndroid()
        ? "Models/Terrain/" + file
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Terrain", file);

    /// <summary>Models already loaded, by file, so a garden that starts over (or another garden's) reuses one rather than loading it again.</summary>
    private static readonly Dictionary<string, Model> LoadedModels = new();

    /// <summary>The model file of the terrain this ground was made for.</summary>
    private readonly string _modelFile;

    /// <summary>How a generated terrain is drawn (null for a baked one, drawn from its model).</summary>
    private readonly ProceduralView? _procedural;

    /// <summary>How much of a season's tint is also added over the lawn (a multiply alone can't frost it white).</summary>
    private const float SeasonGlow = 0.3f;

    private Model _model;
    private bool _modelReady;

    /// <summary>Loads the model the first time it's drawn (needs a GPU context, so not eager).</summary>
    private void EnsureModel()
    {
        if (_modelReady)
            return;
        if (!LoadedModels.TryGetValue(_modelFile, out _model))
        {
            _model = Raylib.LoadModel(ModelPathOf(_modelFile));
            LoadedModels[_modelFile] = _model;
        }
        _modelReady = true;
    }

    /// <summary>
    /// Draws the terrain model. The season's colour cast is laid over it in
    /// two passes: the texture multiplied by the tint blended toward white,
    /// then (for a tint that brightens, like winter's frost) a share of the
    /// tint added on top — both skipped when the season has no tint.
    /// </summary>
    public void Draw(Color seasonTint, float seasonAmount)
    {
        seasonAmount = Math.Clamp(seasonAmount, 0f, 1f);
        Color multiply = LerpColor(Color.White, seasonTint, seasonAmount);
        if (_procedural is not null)
        {
            _procedural.Draw(multiply, seasonAmount <= 0f ? null : new Color(
                (byte)(seasonTint.R * seasonAmount * SeasonGlow),
                (byte)(seasonTint.G * seasonAmount * SeasonGlow),
                (byte)(seasonTint.B * seasonAmount * SeasonGlow), (byte)255));
            return;
        }
        EnsureModel();
        Raylib.DrawModel(_model, Vector3.Zero, 1f, multiply);
        if (seasonAmount <= 0f)
            return;

        Rlgl.DrawRenderBatchActive();
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawModel(_model, Vector3.Zero, 1f, new Color(
            (byte)(seasonTint.R * seasonAmount * SeasonGlow),
            (byte)(seasonTint.G * seasonAmount * SeasonGlow),
            (byte)(seasonTint.B * seasonAmount * SeasonGlow), (byte)255));
        Raylib.EndBlendMode();
    }

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
}
