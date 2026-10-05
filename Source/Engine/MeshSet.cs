using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A glb of several separate meshes sharing one picture (made by Tools/split_sheets.py), each standing on y = 0 centred on its own origin and already sized in
/// metres: <see cref="LooseModels"/> and <see cref="FittingModels"/> are sets of them. Loaded the first time a mesh is drawn (that needs a GPU context).
/// </summary>
internal sealed unsafe class MeshSet
{
    private readonly string _path;
    private readonly Color[] _standIns;
    private readonly bool[] _thin;
    private float[] _radius = Array.Empty<float>();
    private Model _model;
    private bool _ready;

    /// <summary>Under this many pixels across a mesh is not drawn; under <see cref="StandInPixels"/> a plain sphere of its colour stands in for it (see <see cref="Detail"/>).</summary>
    private const float SkipPixels = 1.5f, StandInPixels = 7f;

    /// <param name="file">The file under <c>Assets/Models/Props</c>, e.g. "Loose.glb" or "Village/Fittings.glb".</param>
    /// <param name="standIns">Each mesh's colour for the sphere that stands in for it from far off.</param>
    /// <param name="thin">Which meshes are made of thin sheets (leaves, thorns, basket walls), drawn without back-face culling.</param>
    public MeshSet(string file, Color[] standIns, bool[] thin)
    {
        _standIns = standIns;
        _thin = thin;
        _path = OperatingSystem.IsAndroid()
            ? "Models/Props/" + file
            : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Props", file);
    }

    private void EnsureLoaded()
    {
        if (_ready)
            return;
        _model = Raylib.LoadModel(_path);
        for (int m = 0; m < _model.MaterialCount; m++)
        {
            Texture2D texture = _model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture;
            Raylib.GenTextureMipmaps(ref texture);
            Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
            _model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
        }
        _radius = new float[_model.MeshCount];
        for (int m = 0; m < _model.MeshCount; m++)
        {
            Mesh mesh = _model.Meshes[m];
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                var p = new Vector3(mesh.Vertices[v * 3], mesh.Vertices[v * 3 + 1], mesh.Vertices[v * 3 + 2]);
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
            }
            _radius[m] = Vector3.Distance(lo, hi) / 2f;
        }
        _ready = true;
    }

    /// <summary>Draws mesh number <paramref name="mesh"/> standing on <paramref name="position"/>, turned <paramref name="yawDegrees"/> about the vertical, scaled by <paramref name="scale"/> (per axis), tipped <paramref name="pitchDegrees"/> about its own Z.</summary>
    public void Draw(int mesh, Vector3 position, float yawDegrees, Vector3 scale, Color tint, float pitchDegrees = 0f)
    {
        EnsureLoaded();
        if (mesh < 0 || mesh >= _model.MeshCount)
            return;
        float pixels = Detail.Pixels(position, _radius[mesh] * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)));
        if (pixels < SkipPixels)
            return;
        if (pixels < StandInPixels)
        {
            Detail.Sphere(position + new Vector3(0f, _radius[mesh] * scale.Y * 0.7f, 0f), _radius[mesh] * 0.6f * scale.X, _standIns[mesh]);
            return;
        }
        Raylib_cs.Material material = _model.Materials[_model.MeshMaterial[mesh]];
        material.Maps[(int)MaterialMapIndex.Albedo].Color = tint;
        Rlgl.PushMatrix();
        Rlgl.Translatef(position.X, position.Y, position.Z);
        Rlgl.Rotatef(yawDegrees, 0f, 1f, 0f);
        if (pitchDegrees != 0f)
            Rlgl.Rotatef(pitchDegrees, 0f, 0f, 1f);
        Rlgl.Scalef(scale.X, scale.Y, scale.Z);
        if (_thin[mesh])
            Rlgl.DisableBackfaceCulling(); // (leaves, thorns and basket walls are thin: seen from behind they must not vanish)
        Raylib.DrawMesh(_model.Meshes[mesh], material, Matrix4x4.Identity);
        if (_thin[mesh])
            Rlgl.EnableBackfaceCulling();
        Rlgl.PopMatrix();
    }
}
