namespace GardenGuardians;

/// <summary>
/// Turns a height grid into a few triangles: a right-triangulated irregular network (after Evans et al., as in
/// the Martini library) — flat stretches become big triangles, bumpy ones small, and the pieces always meet
/// edge to edge, so there are no cracks. The height grid is first resampled to 2^k cells a side.
/// </summary>
public sealed class TerrainMesh
{
    /// <summary>Vertex positions (x, y, z metres, three floats each), their normals and texture coordinates, and the triangles (three indices each, front faces up).</summary>
    public float[] Positions { get; }
    public float[] Normals { get; }
    public float[] TexCoords { get; }
    public ushort[] Indices { get; }

    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;

    /// <summary>The most vertices one mesh may have (raylib indexes with 16 bits).</summary>
    private const int MaxVertices = 65000;

    /// <summary>
    /// Builds the mesh of <paramref name="heights"/> (<paramref name="size"/> x <paramref name="size"/> samples,
    /// rows running along z, spanning -<paramref name="half"/>..<paramref name="half"/>): no point off the grid's height by more than
    /// <paramref name="tolerance"/> metres, unless that would need too many vertices (then the tolerance is eased).
    /// </summary>
    public TerrainMesh(float[] heights, int size, float half, float tolerance)
    {
        int cells = 1;
        while (cells < size - 1)
            cells *= 2;
        int n = cells + 1;
        float cell = 2f * half / cells;

        // Resample onto the 2^k grid.
        var grid = new float[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
                grid[j * n + i] = Sample(heights, size, half, -half + i * cell, -half + j * cell);
        }

        var martini = new Martini(n);
        float[] errors = martini.Errors(grid);
        (ushort X, ushort Y)[] vertices;
        uint[] triangles;
        float allowed = tolerance;
        while (true)
        {
            (vertices, triangles) = martini.Mesh(errors, allowed);
            if (vertices.Length <= MaxVertices)
                break;
            allowed *= 1.5f;
        }

        Positions = new float[vertices.Length * 3];
        Normals = new float[vertices.Length * 3];
        TexCoords = new float[vertices.Length * 2];
        for (int v = 0; v < vertices.Length; v++)
        {
            int gx = vertices[v].X, gy = vertices[v].Y;
            float x = -half + gx * cell, z = -half + gy * cell;
            Positions[v * 3] = x;
            Positions[v * 3 + 1] = grid[gy * n + gx];
            Positions[v * 3 + 2] = z;
            float left = grid[gy * n + Math.Max(gx - 1, 0)], right = grid[gy * n + Math.Min(gx + 1, n - 1)];
            float back = grid[Math.Max(gy - 1, 0) * n + gx], front = grid[Math.Min(gy + 1, n - 1) * n + gx];
            float nx = left - right, nz = back - front, ny = 2f * cell;
            float length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            Normals[v * 3] = nx / length;
            Normals[v * 3 + 1] = ny / length;
            Normals[v * 3 + 2] = nz / length;
            TexCoords[v * 2] = (x + half) / (2f * half);
            TexCoords[v * 2 + 1] = (z + half) / (2f * half);
        }

        Indices = new ushort[triangles.Length];
        for (int t = 0; t < triangles.Length; t += 3)
        {
            uint a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            // Front faces look up: the triangle must run anticlockwise seen from above (+y).
            float ux = Positions[b * 3] - Positions[a * 3], uz = Positions[b * 3 + 2] - Positions[a * 3 + 2];
            float vx = Positions[c * 3] - Positions[a * 3], vz = Positions[c * 3 + 2] - Positions[a * 3 + 2];
            if (uz * vx - ux * vz < 0f)
                (b, c) = (c, b);
            Indices[t] = (ushort)a;
            Indices[t + 1] = (ushort)b;
            Indices[t + 2] = (ushort)c;
        }
    }

    /// <summary>The height at (x, z) in metres, between the grid's samples.</summary>
    private static float Sample(float[] heights, int size, float half, float x, float z)
    {
        float step = 2f * half / (size - 1);
        float fx = Math.Clamp((x + half) / step, 0f, size - 1.001f), fz = Math.Clamp((z + half) / step, 0f, size - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        float near = heights[iz * size + ix] + (heights[iz * size + ix + 1] - heights[iz * size + ix]) * tx;
        float far = heights[(iz + 1) * size + ix] + (heights[(iz + 1) * size + ix + 1] - heights[(iz + 1) * size + ix]) * tx;
        return near + (far - near) * tz;
    }

    /// <summary>The right-triangle hierarchy over a (2^k + 1)-point square grid.</summary>
    private sealed class Martini
    {
        private readonly int _size, _tile, _triangles, _parents;
        private readonly ushort[] _coords;

        public Martini(int size)
        {
            _size = size;
            _tile = size - 1;
            _triangles = _tile * _tile * 2 - 2;
            _parents = _triangles - _tile * _tile;
            _coords = new ushort[_triangles * 4];
            // Each triangle's corners, from its place in the implicit binary tree.
            for (int i = 0; i < _triangles; i++)
            {
                int id = i + 2;
                int ax = 0, ay = 0, bx = 0, by = 0, cx = 0, cy = 0;
                if ((id & 1) != 0)
                    bx = by = cx = _tile;
                else
                    ax = ay = cy = _tile;
                while ((id >>= 1) > 1)
                {
                    int mx = (ax + bx) >> 1, my = (ay + by) >> 1;
                    if ((id & 1) != 0)
                    {
                        bx = ax;
                        by = ay;
                        ax = cx;
                        ay = cy;
                    }
                    else
                    {
                        ax = bx;
                        ay = by;
                        bx = cx;
                        by = cy;
                    }
                    cx = mx;
                    cy = my;
                }
                int k = i * 4;
                _coords[k] = (ushort)ax;
                _coords[k + 1] = (ushort)ay;
                _coords[k + 2] = (ushort)bx;
                _coords[k + 3] = (ushort)by;
            }
        }

        /// <summary>For each grid point, the most it would be off if the mesh left it out (accumulated up the hierarchy).</summary>
        public float[] Errors(float[] terrain)
        {
            var errors = new float[terrain.Length];
            for (int i = _triangles - 1; i >= 0; i--)
            {
                int k = i * 4;
                int ax = _coords[k], ay = _coords[k + 1], bx = _coords[k + 2], by = _coords[k + 3];
                int mx = (ax + bx) >> 1, my = (ay + by) >> 1;
                int cx = mx + my - ay, cy = my + ax - mx;
                float interpolated = (terrain[ay * _size + ax] + terrain[by * _size + bx]) / 2f;
                int middle = my * _size + mx;
                errors[middle] = MathF.Max(errors[middle], MathF.Abs(interpolated - terrain[middle]));
                if (i < _parents)
                {
                    int left = ((ay + cy) >> 1) * _size + ((ax + cx) >> 1);
                    int right = ((by + cy) >> 1) * _size + ((bx + cx) >> 1);
                    errors[middle] = MathF.Max(errors[middle], MathF.Max(errors[left], errors[right]));
                }
            }
            return errors;
        }

        private float[] _errors = Array.Empty<float>();
        private float _maxError;
        private int[] _indices = Array.Empty<int>();
        private int _vertexCount, _triangleCount;
        private (ushort X, ushort Y)[] _vertices = Array.Empty<(ushort, ushort)>();
        private uint[] _out = Array.Empty<uint>();
        private int _outAt;

        /// <summary>The triangles that keep every point within <paramref name="maxError"/>: vertices (grid x, y) and triangles (three vertex indices each).</summary>
        public ((ushort X, ushort Y)[] Vertices, uint[] Triangles) Mesh(float[] errors, float maxError)
        {
            _errors = errors;
            _maxError = maxError;
            _indices = new int[_size * _size];
            _vertexCount = 0;
            _triangleCount = 0;
            int max = _size - 1;
            Count(0, 0, max, max, max, 0);
            Count(max, max, 0, 0, 0, max);
            _vertices = new (ushort, ushort)[_vertexCount];
            _out = new uint[_triangleCount * 3];
            _outAt = 0;
            Fill(0, 0, max, max, max, 0);
            Fill(max, max, 0, 0, 0, max);
            return (_vertices, _out);
        }

        private bool Splits(int ax, int ay, int bx, int by, int cx, int cy) =>
            Math.Abs(ax - cx) + Math.Abs(ay - cy) > 1 && _errors[((ay + by) >> 1) * _size + ((ax + bx) >> 1)] > _maxError;

        private void Count(int ax, int ay, int bx, int by, int cx, int cy)
        {
            if (Splits(ax, ay, bx, by, cx, cy))
            {
                int mx = (ax + bx) >> 1, my = (ay + by) >> 1;
                Count(cx, cy, ax, ay, mx, my);
                Count(bx, by, cx, cy, mx, my);
                return;
            }
            if (_indices[ay * _size + ax] == 0)
                _indices[ay * _size + ax] = ++_vertexCount;
            if (_indices[by * _size + bx] == 0)
                _indices[by * _size + bx] = ++_vertexCount;
            if (_indices[cy * _size + cx] == 0)
                _indices[cy * _size + cx] = ++_vertexCount;
            _triangleCount++;
        }

        private void Fill(int ax, int ay, int bx, int by, int cx, int cy)
        {
            if (Splits(ax, ay, bx, by, cx, cy))
            {
                int mx = (ax + bx) >> 1, my = (ay + by) >> 1;
                Fill(cx, cy, ax, ay, mx, my);
                Fill(bx, by, cx, cy, mx, my);
                return;
            }
            int a = _indices[ay * _size + ax] - 1, b = _indices[by * _size + bx] - 1, c = _indices[cy * _size + cx] - 1;
            _vertices[a] = ((ushort)ax, (ushort)ay);
            _vertices[b] = ((ushort)bx, (ushort)by);
            _vertices[c] = ((ushort)cx, (ushort)cy);
            _out[_outAt++] = (uint)a;
            _out[_outAt++] = (uint)b;
            _out[_outAt++] = (uint)c;
        }
    }
}
