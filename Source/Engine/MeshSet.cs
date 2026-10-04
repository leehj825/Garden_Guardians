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
    private Model _model;
    private bool _ready;

    /// <param name="file">The file under <c>Assets/Models/Props</c>, e.g. "Loose.glb" or "Village/Fittings.glb".</param>
    public MeshSet(string file)
    {
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
        _ready = true;
    }

    /// <summary>Draws mesh number <paramref name="mesh"/> standing on <paramref name="position"/>, turned <paramref name="yawDegrees"/> about the vertical, scaled by <paramref name="scale"/> (per axis), tipped <paramref name="pitchDegrees"/> about its own Z.</summary>
    public void Draw(int mesh, Vector3 position, float yawDegrees, Vector3 scale, Color tint, float pitchDegrees = 0f)
    {
        EnsureLoaded();
        if (mesh < 0 || mesh >= _model.MeshCount)
            return;
        Raylib_cs.Material material = _model.Materials[_model.MeshMaterial[mesh]];
        material.Maps[(int)MaterialMapIndex.Albedo].Color = tint;
        Rlgl.PushMatrix();
        Rlgl.Translatef(position.X, position.Y, position.Z);
        Rlgl.Rotatef(yawDegrees, 0f, 1f, 0f);
        if (pitchDegrees != 0f)
            Rlgl.Rotatef(pitchDegrees, 0f, 0f, 1f);
        Rlgl.Scalef(scale.X, scale.Y, scale.Z);
        Rlgl.DisableBackfaceCulling(); // (leaves, thorns and basket walls are thin: seen from behind they must not vanish)
        Raylib.DrawMesh(_model.Meshes[mesh], material, Matrix4x4.Identity);
        Rlgl.EnableBackfaceCulling();
        Rlgl.PopMatrix();
    }
}
