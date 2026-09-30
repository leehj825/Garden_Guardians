using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- The pond in a drought -------------------------------------------------------------

    /// <summary>Through a drought the pond sinks, reaching its lowest (see <see cref="WaterMap.Levels"/>) this many seconds in…</summary>
    private const float PondDrySeconds = 110f;

    /// <summary>…and afterwards it fills back over this long — four times as fast in the rain.</summary>
    private const float PondRefillSeconds = 120f;

    /// <summary>The terrain's own dirt tile, darkened to damp earth, repeats every this many metres.</summary>
    private const float MudTile = 4f;

    private static readonly Color MudTint = new(140, 125, 110, 255);

    /// <summary>How far the pond has sunk: 0 as usual, 1 at its lowest.</summary>
    private float _pondLow;

    /// <summary>Whether this drought's shrinking pond has been told of yet (so each is told once).</summary>
    private bool _pondShrinkTold, _pondDryTold;

    /// <summary>A drought drinks the pond down; rain and fair weather fill it back up.</summary>
    private void UpdatePond(float deltaTime)
    {
        if (CurrentWeather == Weather.Drought && !IsStorming)
            _pondLow = MathF.Min(1f, _pondLow + deltaTime / PondDrySeconds);
        else if (_pondLow > 0f)
            _pondLow = MathF.Max(0f, _pondLow - deltaTime / PondRefillSeconds * (IsStorming ? 4f : 1f));

        int level = PondLevelIndex;
        if (level == WaterMap.Level)
            return;
        bool sinking = level > WaterMap.Level;
        WaterMap.SetLevel(level);

        if (sinking && level >= 2 && !_pondShrinkTold)
        {
            _pondShrinkTold = true;
            Game.AddEventLog($"[WEATHER] The pond is shrinking in the drought - it's a longer walk for a drink, and fewer fish");
        }
        if (sinking && level == WaterMap.Levels - 1 && !_pondDryTold)
        {
            _pondDryTold = true;
            Game.AddEventLog("[WEATHER] The pond has all but dried up - only puddles left in the hollows");
            Headline("Drought", "The pond has all but dried up - only puddles left in the hollows", null, true);
        }
        if (level == 0)
        {
            if (_pondShrinkTold)
                Game.AddEventLog("[WEATHER] The pond has filled again");
            _pondShrinkTold = _pondDryTold = false;
        }
    }

    private int PondLevelIndex => (int)MathF.Round(_pondLow * (WaterMap.Levels - 1));

    /// <summary>Puts the water level in force to match this garden (the water map is shared, and a fresh or loaded garden starts it afresh).</summary>
    private void SyncPondLevel() => WaterMap.SetLevel(PondLevelIndex);

    /// <summary>The garden's mud: the pond's bed, laid bare as the water sinks — squares that follow the ground, drawn only while it's down.</summary>
    [NotSaved] // A render cache, like the water's (see _waterCellsLevel).
    private int _mudCellsLevel = -1;

    [NotSaved]
    private SquareLayer? _mudLayer;

    private void DrawPondBed()
    {
        if (WaterMap.Level == 0)
            return;
        if (_mudCellsLevel != WaterMap.Level)
        {
            _mudCellsLevel = WaterMap.Level;
            var cells = new HashSet<(int, int)>();
            float half = Terrain.Size / 2f;
            float surface = WaterMap.SurfaceHeight;
            int n = (int)MathF.Ceiling(Terrain.Size / WaterCell);
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    float x = -half + i * WaterCell, z = -half + j * WaterCell;
                    float middle = GetHeightAt(x + WaterCell / 2f, z + WaterCell / 2f);
                    if (middle >= PondLevel || middle < surface - 0.05f)
                        continue;
                    cells.Add((i, j));
                }
            }

            // Each corner is as solid as the number of mud squares round it, so the patch fades out at its rim.
            byte Fade(int ci, int cj)
            {
                int count = (cells.Contains((ci - 1, cj - 1)) ? 1 : 0) + (cells.Contains((ci, cj - 1)) ? 1 : 0)
                    + (cells.Contains((ci - 1, cj)) ? 1 : 0) + (cells.Contains((ci, cj)) ? 1 : 0);
                return (byte)(255 * (0.15f + 0.85f * (Math.Max(count, 1) - 1) / 3f));
            }

            var squares = new List<SquareLayer.Square>(cells.Count);
            foreach (var (i, j) in cells)
            {
                float x = -half + i * WaterCell, z = -half + j * WaterCell;
                Vector3 Corner(float cx, float cz) => new(cx, GetHeightAt(cx, cz) + 0.04f, cz);
                squares.Add(new SquareLayer.Square(
                    Corner(x, z), Corner(x + WaterCell, z), Corner(x + WaterCell, z + WaterCell), Corner(x, z + WaterCell),
                    Fade(i, j), Fade(i + 1, j), Fade(i + 1, j + 1), Fade(i, j + 1)));
            }
            _mudLayer ??= new SquareLayer();
            _mudLayer.Rebuild(squares, SquareLayer.Tile("dirt_1.png", 256), MudTile, MudTile, MudTint);
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        _mudLayer?.Draw();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }
}
