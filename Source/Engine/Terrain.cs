using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The flat backyard ground: a green plane lying on y = 0 with a grid overlay
/// so that scale (1 cell = 1 meter) is easy to read.
/// </summary>
public sealed class Terrain
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
    /// Part 3 + Part 2: draws the 100x100m lawn as a grid of 2x2m cells from
    /// -50 to 50 on X/Z, using <see cref="World.GetHeightAt"/> for each
    /// corner's elevation so the lawn reads as rolling hills, and skipping
    /// any cell whose center is beyond <paramref name="renderRadius"/> of
    /// <paramref name="cameraTarget"/> (Part 2's mandatory distance cull —
    /// terrain is by far the most expensive thing drawn every frame).
    /// </summary>
    public void Draw(Vector3 cameraTarget, float renderRadius)
    {
        float half = Size / 2f;
        float renderRadiusSq = renderRadius * renderRadius;

        for (float x = -half; x < half; x += CellSize)
        {
            for (float z = -half; z < half; z += CellSize)
            {
                float centerX = x + CellSize / 2f;
                float centerZ = z + CellSize / 2f;
                float dx = centerX - cameraTarget.X;
                float dz = centerZ - cameraTarget.Z;
                if (dx * dx + dz * dz > renderRadiusSq)
                    continue; // Part 2: distance-culled — never drawn, never costs a frame.

                float x0 = x, x1 = x + CellSize, z0 = z, z1 = z + CellSize;
                var p00 = new Vector3(x0, World.GetHeightAt(x0, z0), z0);
                var p10 = new Vector3(x1, World.GetHeightAt(x1, z0), z0);
                var p01 = new Vector3(x0, World.GetHeightAt(x0, z1), z1);
                var p11 = new Vector3(x1, World.GetHeightAt(x1, z1), z1);

                int cx = (int)MathF.Floor(x / CellSize);
                int cz = (int)MathF.Floor(z / CellSize);
                uint hash = CellHash(cx, cz);

                Color color;
                if (hash % 40 == 0)
                {
                    // A rare, sparse dirt patch (~1 cell in 40) — an
                    // occasional embellishment, not a repeating pattern.
                    color = Dirt;
                }
                else
                {
                    // Organic height-based tinting: a single Forest Green
                    // base, lightened toward a sunlit yellow-green on peaks
                    // and darkened toward a shadowed green in valleys — no
                    // checkerboard, just the cell's own average elevation.
                    float avgHeight = (p00.Y + p10.Y + p01.Y + p11.Y) / 4f;
                    float t = Math.Clamp(avgHeight / HeightAmplitude, -1f, 1f);
                    color = t >= 0f
                        ? LerpColor(GrassBase, GrassPeak, t)
                        : LerpColor(GrassBase, GrassValley, -t);
                }

                // Two triangles, upward-facing winding (counter-clockwise
                // when viewed from above/+Y).
                Raylib.DrawTriangle3D(p00, p01, p11, color);
                Raylib.DrawTriangle3D(p00, p11, p10, color);
            }
        }
    }
}
