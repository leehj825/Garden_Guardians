using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Draws the worn dirt paths and paved roads (see World.Trails) as textured ground-hugging meshes (see <see cref="SquareLayer"/>):
/// trampled earth on the paths, cobblestones on the roads. Each corner carries its own opacity, lower on the outside of a
/// patch, so the edges fade into the grass.
/// </summary>
public sealed class TrailRenderer
{
    /// <summary>One square of trail: its four corners (in the order the world lists them), whether it's road, and each corner's opacity (0-255).</summary>
    public readonly record struct Square(Vector3 A, Vector3 B, Vector3 C, Vector3 D, bool Paved, byte AlphaA, byte AlphaB, byte AlphaC, byte AlphaD);

    /// <summary>A texture repeats every this many metres across and down (the cobble picture is squeezed square to load, so its width is stretched back out here).</summary>
    private const float RoadTileX = 3.66f, RoadTileZ = 2f, PathTile = 3f;

    private readonly SquareLayer _paths = new(), _roads = new();

    private static SquareLayer.Square Layer(Square s) => new(s.A, s.B, s.C, s.D, s.AlphaA, s.AlphaB, s.AlphaC, s.AlphaD);

    /// <summary>Replaces the meshes with ones for <paramref name="squares"/>.</summary>
    public void Rebuild(List<Square> squares)
    {
        _paths.Rebuild(squares.Where(s => !s.Paved).Select(Layer), SquareLayer.Tile("terrain_path.png", 256), PathTile, PathTile);
        _roads.Rebuild(squares.Where(s => s.Paved).Select(Layer), SquareLayer.Tile("cobble_1.png", 512), RoadTileX, RoadTileZ);
    }

    public void Draw()
    {
        _paths.Draw();
        _roads.Draw();
    }
}
