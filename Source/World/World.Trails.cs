using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Paths and roads -------------------------------------------------------------------
    // Feet wear the ground: every metre-square cell a kin walks over gains wear, and loses it
    // slowly when unused. Worn cells are dirt paths (a little faster to walk); once any clan
    // knows Roads, cells worn hard enough are paved with set stones for good (faster still).

    /// <summary>Metre-square cells along each side of the garden.</summary>
    private static int TrailCells => (int)(2f * TerrainData.Half);

    /// <summary>Wear (seconds of footfalls) at which a cell shows as a path, and at which it can be paved.</summary>
    private const float PathWear = 4f, PaveWear = 14f;

    /// <summary>Wear lost per second by a path nobody walks (a busy one outlasts it).</summary>
    private const float WearDecay = 0.02f;

    /// <summary>Walking speed multiplier on a path and on a road.</summary>
    private const float PathSpeedBonus = 1.15f, RoadSpeedBonus = 1.4f;

    private readonly float[] _wear = new float[TrailCells * TrailCells];
    private readonly bool[] _paved = new bool[TrailCells * TrailCells];
    private float _trailTimer;
    private bool _trailsDirty = true;
    private bool _roadsKnown;

    /// <summary>Whether any clan can cut stone: paving then starts at 60% of the wear.</summary>
    private bool _stonecutting;

    [NotSaved] // A render cache, rebuilt when a cell crosses a threshold.
    private readonly List<TrailRenderer.Square> _trailSquares = new();

    [NotSaved] // The textured meshes made from them.
    private TrailRenderer? _trailRenderer;

    /// <summary>Cells paved into road so far.</summary>
    public int RoadCells { get; private set; }

    /// <summary>Cells showing as a dirt path or road.</summary>
    public int PathCells
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _wear.Length; i++)
                n += !_paved[i] && _wear[i] >= PathWear ? 1 : 0;
            return n;
        }
    }

    private static int TrailIndex(float x, float z)
    {
        int i = (int)MathF.Floor(x + TrailCells / 2f), j = (int)MathF.Floor(z + TrailCells / 2f);
        return i < 0 || j < 0 || i >= TrailCells || j >= TrailCells ? -1 : j * TrailCells + i;
    }

    /// <summary>The speed multiplier for walking at <paramref name="point"/>: 1 on grass, more on a worn path, most on a road.</summary>
    public float PathSpeed(Vector3 point)
    {
        int cell = TrailIndex(point.X, point.Z);
        if (cell < 0)
            return 1f;
        return _paved[cell] ? RoadSpeedBonus : _wear[cell] >= PathWear ? PathSpeedBonus : 1f;
    }

    private void UpdateTrails(float deltaTime)
    {
        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsDead || !kin.IsWalking)
                continue;
            int cell = TrailIndex(kin.Position.X, kin.Position.Z);
            if (cell < 0 || IsWater(kin.Position))
                continue;
            bool was = _wear[cell] >= PathWear;
            _wear[cell] += deltaTime;
            _trailsDirty |= !was && _wear[cell] >= PathWear;
        }

        _trailTimer += deltaTime;
        if (_trailTimer < 2f)
            return;
        float elapsed = _trailTimer;
        _trailTimer = 0f;
        _roadsKnown = Groups.Any(g => Knows(g, Craft.Roads));
        _stonecutting = Groups.Any(g => Knows(g, Craft.Stonecutting));
        float paveAt = _stonecutting ? PaveWear * 0.6f : PaveWear;
        for (int i = 0; i < _wear.Length; i++)
        {
            if (_wear[i] <= 0f)
                continue;
            bool was = _wear[i] >= PathWear;
            _wear[i] = MathF.Max(0f, _wear[i] - WearDecay * elapsed);
            if (_roadsKnown && !_paved[i] && _wear[i] >= paveAt)
            {
                _paved[i] = true;
                RoadCells++;
                _trailsDirty = true;
            }
            _trailsDirty |= was && _wear[i] < PathWear;
        }
    }

    /// <summary>Whether the cell at (<paramref name="i"/>, <paramref name="j"/>) is the same kind of trail (road, or dirt path) as asked.</summary>
    private bool IsTrail(int i, int j, bool paved)
    {
        if (i < 0 || j < 0 || i >= TrailCells || j >= TrailCells)
            return false;
        int cell = j * TrailCells + i;
        return paved ? _paved[cell] : !_paved[cell] && _wear[cell] >= PathWear;
    }

    /// <summary>Dirt paths and paved roads, laid just over the ground: textured, fading out at the edges of a patch.</summary>
    private void DrawTrails(Camera3D camera)
    {
        _trailRenderer ??= new TrailRenderer();
        if (_trailsDirty)
        {
            _trailsDirty = false;
            _trailSquares.Clear();
            for (int j = 0; j < TrailCells; j++)
            {
                for (int i = 0; i < TrailCells; i++)
                {
                    int cell = j * TrailCells + i;
                    bool paved = _paved[cell];
                    if (!paved && _wear[cell] < PathWear)
                        continue;
                    float x = i - TrailCells / 2f, z = j - TrailCells / 2f;
                    Vector3 Corner(float cx, float cz) => new(cx, GetHeightAt(cx, cz) + 0.05f, cz);

                    // A corner is solid when all four squares round it are the same trail, and fades as fewer are.
                    float most = paved ? 255f : 200f;
                    byte Fade(int ci, int cj)
                    {
                        int n = (IsTrail(ci - 1, cj - 1, paved) ? 1 : 0) + (IsTrail(ci, cj - 1, paved) ? 1 : 0)
                            + (IsTrail(ci - 1, cj, paved) ? 1 : 0) + (IsTrail(ci, cj, paved) ? 1 : 0);
                        return (byte)(most * (0.3f + 0.7f * (Math.Max(n, 1) - 1) / 3f));
                    }

                    _trailSquares.Add(new TrailRenderer.Square(
                        Corner(x, z), Corner(x + 1, z), Corner(x + 1, z + 1), Corner(x, z + 1), paved,
                        Fade(i, j), Fade(i + 1, j), Fade(i + 1, j + 1), Fade(i, j + 1)));
                }
            }
            _trailRenderer.Rebuild(_trailSquares);
        }
        if (_trailSquares.Count == 0)
            return;
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        _trailRenderer.Draw();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }
}
