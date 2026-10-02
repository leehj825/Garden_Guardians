using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed unsafe partial class World
{
    // --- Ground cover: the close-up garden floor ------------------------------------------------------------------------------
    // The ground is one painted texture, flat and smeared when seen from a kin's own height. Whenever the camera is close in (and always while the player controls a kin), the ground
    // within about 40 m of what it looks at is dressed in real geometry: blades of grass (many and fine up close, fewer and coarser farther out),
    // flowers and small stones, built in 6 m chunks as the kin walks (a few a frame), each chunk one mesh with colours on its vertices.
    // Nothing is simulated or saved: the same chunk always grows the same cover, and it is dropped when the kin is given back.

    private const float CoverChunkSize = 6f;

    /// <summary>Chunks within this far (m) of the controlled kin are dressed; those farther than <see cref="CoverKeepRadius"/> are dropped.</summary>
    private const float CoverRadius = 40f, CoverKeepRadius = 52f;

    /// <summary>Level of detail by distance from the kin (m): fine, medium, coarse.</summary>
    private static readonly float[] CoverTierFrom = { 0f, 12f, 24f };

    /// <summary>Tufts of grass per square metre, blades to a tuft, and whether a blade has a bend (three triangles) or is one triangle, for each tier.</summary>
    private static readonly float[] CoverTuftDensity = { 9f, 3f, 1.2f };
    private static readonly int[] CoverBlades = { 4, 3, 3 };
    private static readonly bool[] CoverBentBlades = { true, false, false };

    /// <summary>Flowers and stones per square metre, by tier (none in the coarse tier).</summary>
    private static readonly float[] CoverFlowerDensity = { 0.35f, 0.12f, 0f };
    private static readonly float[] CoverStoneDensity = { 0.15f, 0.05f, 0f };

    /// <summary>New chunks built per frame, so a phone does not stall when the kin crosses into new ground.</summary>
    private const int CoverBuildsPerFrame = 3;

    private sealed class CoverChunkMesh
    {
        public Mesh Mesh;
        public int Tier;
        public bool Empty;
    }

    private readonly Dictionary<(int X, int Z), CoverChunkMesh> _cover = new();
    private Raylib_cs.Material _coverMaterial;
    private bool _coverMaterialReady;

    /// <summary>Zoomed out beyond this far (m, eye to what it looks at) there is no cover; closer than <see cref="CoverFullZoom"/> it reaches its full radius, between them it shrinks.</summary>
    private const float CoverOffZoom = 45f, CoverFullZoom = 20f, CoverFarRadius = 20f;

    /// <summary>Seconds without any cover wanted before what was built is freed.</summary>
    private const float CoverIdleSeconds = 4f;

    private float _coverIdle;

    /// <summary>
    /// The grass, flowers and stones where the camera is close in: round the controlled kin if there is one, else round what the camera looks
    /// at. Detail is picked by the nearer of that spot and the camera's own, so the ground at the bottom of the screen is the finest.
    /// Called after the ground is drawn.
    /// </summary>
    private void DrawGroundCover(Camera3D camera, Color seasonTint, float seasonAmount)
    {
        Bramblekin? kin = Colony.FirstOrDefault(k => k.IsPlayerControlled && !k.IsDead);
        float zoom = Vector3.Distance(camera.Position, camera.Target);
        float radius = kin is not null || zoom <= CoverFullZoom ? CoverRadius
            : zoom >= CoverOffZoom ? 0f
            : CoverRadius + (CoverFarRadius - CoverRadius) * (zoom - CoverFullZoom) / (CoverOffZoom - CoverFullZoom);
        if (radius <= 0f)
        {
            _coverIdle += Raylib.GetFrameTime();
            if (_cover.Count > 0 && _coverIdle > CoverIdleSeconds)
                ReleaseGroundCover();
            return;
        }
        _coverIdle = 0f;
        if (!_coverMaterialReady)
        {
            _coverMaterial = Raylib.LoadMaterialDefault();
            _coverMaterialReady = true;
        }

        Vector3 at = kin?.Position ?? camera.Target;
        Vector3 eye = camera.Position;
        int reach = (int)MathF.Ceiling(radius / CoverChunkSize) + 1;
        int kx = (int)MathF.Floor(at.X / CoverChunkSize), kz = (int)MathF.Floor(at.Z / CoverChunkSize);
        int ex = (int)MathF.Floor(eye.X / CoverChunkSize), ez = (int)MathF.Floor(eye.Z / CoverChunkSize);
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        var wanted = new Dictionary<(int X, int Z), (float Distance, int Tier)>();
        foreach ((int cx0, int cz0) in new[] { (kx, kz), (ex, ez) })
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                for (int dz = -reach; dz <= reach; dz++)
                {
                    (int X, int Z) key = (cx0 + dx, cz0 + dz);
                    float centreX = (key.X + 0.5f) * CoverChunkSize, centreZ = (key.Z + 0.5f) * CoverChunkSize;
                    float fromAt = MathF.Sqrt((centreX - at.X) * (centreX - at.X) + (centreZ - at.Z) * (centreZ - at.Z));
                    float fromEye = MathF.Sqrt((centreX - eye.X) * (centreX - eye.X) + (centreZ - eye.Z) * (centreZ - eye.Z));
                    float distance = MathF.Min(fromAt, fromEye);
                    if (distance > radius || wanted.ContainsKey(key))
                        continue;
                    int tier = distance < CoverTierFrom[1] ? 0 : distance < CoverTierFrom[2] ? 1 : 2;
                    wanted[key] = (distance, tier);
                }
            }
        }

        int built = 0;
        foreach (var (key, (distance, tier)) in wanted.OrderBy(w => w.Value.Distance))
        {
            if (!_cover.TryGetValue(key, out CoverChunkMesh? chunk) || chunk.Tier != tier)
            {
                if (built >= CoverBuildsPerFrame)
                    continue;
                built++;
                if (chunk is not null && !chunk.Empty)
                    Raylib.UnloadMesh(chunk.Mesh);
                chunk = BuildCoverChunk(key.X, key.Z, tier);
                _cover[key] = chunk;
            }
            if (chunk.Empty)
                continue;

            // Not drawn behind the camera or far from it.
            var middle = new Vector3((key.X + 0.5f) * CoverChunkSize, GetHeightAt((key.X + 0.5f) * CoverChunkSize, (key.Z + 0.5f) * CoverChunkSize), (key.Z + 0.5f) * CoverChunkSize);
            Vector3 toChunk = middle - camera.Position;
            if (Vector3.Dot(toChunk, forward) < -CoverChunkSize || toChunk.LengthSquared() > 110f * 110f)
                continue;

            Color tint = LerpTint(seasonTint, seasonAmount);
            _coverMaterial.Maps[(int)MaterialMapIndex.Albedo].Color = tint;
            Rlgl.DisableBackfaceCulling();
            Raylib.DrawMesh(chunk.Mesh, _coverMaterial, Matrix4x4.Identity);
            Rlgl.EnableBackfaceCulling();
        }

        // Chunks left far behind are dropped.
        foreach ((int X, int Z) key in _cover.Keys.ToList())
        {
            float centreX = (key.X + 0.5f) * CoverChunkSize, centreZ = (key.Z + 0.5f) * CoverChunkSize;
            float nearest = MathF.Min((centreX - at.X) * (centreX - at.X) + (centreZ - at.Z) * (centreZ - at.Z), (centreX - eye.X) * (centreX - eye.X) + (centreZ - eye.Z) * (centreZ - eye.Z));
            if (nearest <= CoverKeepRadius * CoverKeepRadius)
                continue;
            if (!_cover[key].Empty)
                Raylib.UnloadMesh(_cover[key].Mesh);
            _cover.Remove(key);
        }
    }

    private static Color LerpTint(Color tint, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new Color((byte)(255 + (tint.R - 255) * amount), (byte)(255 + (tint.G - 255) * amount), (byte)(255 + (tint.B - 255) * amount), (byte)255);
    }

    /// <summary>Frees the cover (the kin was given back, or the garden is over).</summary>
    private void ReleaseGroundCover()
    {
        foreach (CoverChunkMesh chunk in _cover.Values)
        {
            if (!chunk.Empty)
                Raylib.UnloadMesh(chunk.Mesh);
        }
        _cover.Clear();
    }

    // --- Building a chunk ------------------------------------------------------------------------------------------------------

    private sealed class CoverBuilder
    {
        public readonly List<Vector3> Positions = new();
        public readonly List<Color> Colors = new();
        public readonly List<ushort> Indices = new();

        public int Add(Vector3 position, Color colour)
        {
            Positions.Add(position);
            Colors.Add(colour);
            return Positions.Count - 1;
        }

        public void Triangle(int a, int b, int c)
        {
            Indices.Add((ushort)a);
            Indices.Add((ushort)b);
            Indices.Add((ushort)c);
        }
    }

    private static Color Mix(Color a, Color b, float t) => new(
        (byte)Math.Clamp(a.R + (b.R - a.R) * t, 0f, 255f), (byte)Math.Clamp(a.G + (b.G - a.G) * t, 0f, 255f), (byte)Math.Clamp(a.B + (b.B - a.B) * t, 0f, 255f), (byte)255);

    private CoverChunkMesh BuildCoverChunk(int cx, int cz, int tier)
    {
        var rng = new Random(unchecked(cx * 73856093 ^ cz * 19349663 ^ 0x5bd1e995));
        float x0 = cx * CoverChunkSize, z0 = cz * CoverChunkSize;
        float area = CoverChunkSize * CoverChunkSize;
        var b = new CoverBuilder();

        // What stands near enough to keep clear (homes, plantings, wells), gathered once for the chunk.
        float reach = CoverChunkSize + 8f;
        var homes = Shelters.Where(s => !s.IsCollapsed && MathF.Abs(s.Position.X - (x0 + 3f)) < reach && MathF.Abs(s.Position.Z - (z0 + 3f)) < reach).ToList();
        var plants = Crops.Where(c => MathF.Abs(c.Position.X - (x0 + 3f)) < reach && MathF.Abs(c.Position.Z - (z0 + 3f)) < reach).ToList();
        var wells = Wells.Where(w => MathF.Abs(w.Position.X - (x0 + 3f)) < reach && MathF.Abs(w.Position.Z - (z0 + 3f)) < reach).ToList();

        bool Clear(float x, float z, float margin)
        {
            var p = new Vector3(x, GetHeightAt(x, z), z);
            if (!Terrain.Contains(p, 0.3f))
                return false; // Off the edge of the map.
            if (IsWaterNear(p, 0.35f) || IsOnOak(p, margin) || PathSpeed(p) > 1.01f)
                return false;
            foreach (Shelter home in homes)
            {
                float reachHome = home.Radius + 0.5f + margin;
                if ((home.Position.X - x) * (home.Position.X - x) + (home.Position.Z - z) * (home.Position.Z - z) < reachHome * reachHome)
                    return false;
            }
            foreach (Crop plant in plants)
            {
                float reachPlant = plant.Footprint * 0.8f + margin;
                if ((plant.Position.X - x) * (plant.Position.X - x) + (plant.Position.Z - z) * (plant.Position.Z - z) < reachPlant * reachPlant)
                    return false;
            }
            foreach (Well well in wells)
            {
                float reachWell = Well.Radius + 0.4f + margin;
                if ((well.Position.X - x) * (well.Position.X - x) + (well.Position.Z - z) * (well.Position.Z - z) < reachWell * reachWell)
                    return false;
            }
            return true;
        }

        // A slow patchwork: lush green here, a little dry and yellow there.
        static float Dryness(float x, float z) => Math.Clamp(0.5f + 0.35f * MathF.Sin(x * 0.11f + MathF.Cos(z * 0.07f) * 2f) + 0.25f * MathF.Cos(z * 0.13f - x * 0.05f), 0f, 1f);

        // Grass.
        int tufts = (int)(area * CoverTuftDensity[tier]);
        for (int t = 0; t < tufts && b.Positions.Count < 60000; t++)
        {
            float x = x0 + (float)rng.NextDouble() * CoverChunkSize, z = z0 + (float)rng.NextDouble() * CoverChunkSize;
            float scatter = (float)rng.NextDouble(); // (drawn first so a tuft's look does not change with what is cleared)
            if (!Clear(x, z, 0f))
                continue;
            float dry = Dryness(x, z) * 0.7f + 0.3f * scatter;
            Color baseColor = Mix(new Color(48, 82, 30, 255), new Color(90, 90, 40, 255), dry);
            Color tipColor = Mix(new Color(128, 172, 62, 255), new Color(196, 172, 84, 255), dry);
            for (int k = 0; k < CoverBlades[tier]; k++)
            {
                float bx = x + ((float)rng.NextDouble() - 0.5f) * 0.18f, bz = z + ((float)rng.NextDouble() - 0.5f) * 0.18f;
                float height = (0.085f + (float)rng.NextDouble() * 0.14f) * (tier == 2 ? 1.25f : 1f);
                float width = 0.014f + (float)rng.NextDouble() * 0.014f + (tier == 2 ? 0.01f : 0f);
                float facing = (float)rng.NextDouble() * MathF.Tau, lean = (float)rng.NextDouble() * MathF.Tau;
                Vector3 across = new(MathF.Cos(facing) * width, 0f, MathF.Sin(facing) * width);
                Vector3 bend = new(MathF.Cos(lean), 0f, MathF.Sin(lean));
                float y = GetHeightAt(bx, bz) - 0.02f;
                Vector3 root = new(bx, y, bz);
                Color jitter = Mix(baseColor, tipColor, 0.1f * (float)rng.NextDouble());
                if (CoverBentBlades[tier])
                {
                    int bl = b.Add(root - across, baseColor), br = b.Add(root + across, baseColor);
                    Vector3 mid = root + new Vector3(0f, height * 0.55f, 0f) + bend * height * 0.1f;
                    int ml = b.Add(mid - across * 0.6f, Mix(baseColor, tipColor, 0.55f)), mr = b.Add(mid + across * 0.6f, Mix(baseColor, tipColor, 0.55f));
                    int tip = b.Add(root + new Vector3(0f, height, 0f) + bend * height * 0.35f, tipColor);
                    b.Triangle(bl, br, mr);
                    b.Triangle(bl, mr, ml);
                    b.Triangle(ml, mr, tip);
                }
                else
                {
                    int bl = b.Add(root - across * 1.2f, jitter), br = b.Add(root + across * 1.2f, jitter);
                    int tip = b.Add(root + new Vector3(0f, height, 0f) + bend * height * 0.3f, tipColor);
                    b.Triangle(bl, br, tip);
                }
            }
        }

        // Flowers: a stem, and a ring of petals round a yellow heart.
        Color[] petals = { new(244, 244, 236, 255), new(250, 214, 70, 255), new(236, 130, 170, 255), new(150, 120, 220, 255), new(240, 240, 120, 255) };
        int flowers = (int)(area * CoverFlowerDensity[tier]);
        for (int f = 0; f < flowers; f++)
        {
            float x = x0 + (float)rng.NextDouble() * CoverChunkSize, z = z0 + (float)rng.NextDouble() * CoverChunkSize;
            Color petal = petals[rng.Next(petals.Length)];
            float stem = 0.1f + (float)rng.NextDouble() * 0.09f, spin = (float)rng.NextDouble() * MathF.Tau;
            if (!Clear(x, z, 0.1f))
                continue;
            Vector3 root = new(x, GetHeightAt(x, z) - 0.02f, z);
            Vector3 head = root + new Vector3(0f, stem, 0f);
            int s0 = b.Add(root + new Vector3(0.012f, 0f, 0f), new Color(60, 110, 40, 255)), s1 = b.Add(root - new Vector3(0.012f, 0f, 0f), new Color(60, 110, 40, 255)), s2 = b.Add(head, new Color(90, 150, 55, 255));
            b.Triangle(s0, s1, s2);
            int heart = b.Add(head + new Vector3(0f, 0.012f, 0f), new Color(250, 200, 40, 255));
            const int petalCount = 5;
            for (int p = 0; p < petalCount; p++)
            {
                float a0 = spin + p * MathF.Tau / petalCount, a1 = a0 + MathF.Tau / petalCount * 0.7f;
                int p0 = b.Add(head + new Vector3(MathF.Cos(a0) * 0.04f, 0.006f, MathF.Sin(a0) * 0.04f), petal);
                int p1 = b.Add(head + new Vector3(MathF.Cos(a1) * 0.04f, 0.006f, MathF.Sin(a1) * 0.04f), petal);
                b.Triangle(heart, p0, p1);
            }
        }

        // Small stones: a rounded lump of eight facets, lighter on top.
        int stones = (int)(area * CoverStoneDensity[tier]);
        for (int s = 0; s < stones; s++)
        {
            float x = x0 + (float)rng.NextDouble() * CoverChunkSize, z = z0 + (float)rng.NextDouble() * CoverChunkSize;
            float size = 0.04f + (float)rng.NextDouble() * 0.09f, squash = 0.5f + (float)rng.NextDouble() * 0.3f, spin = (float)rng.NextDouble() * MathF.Tau;
            float grey = 0.8f + 0.4f * (float)rng.NextDouble();
            if (!Clear(x, z, size))
                continue;
            Vector3 centre = new(x, GetHeightAt(x, z) + size * squash * 0.3f, z);
            Color top = new((byte)Math.Min(255f, 165 * grey), (byte)Math.Min(255f, 162 * grey), (byte)Math.Min(255f, 155 * grey), (byte)255);
            Color side = new((byte)Math.Min(255f, 108 * grey), (byte)Math.Min(255f, 106 * grey), (byte)Math.Min(255f, 100 * grey), (byte)255);
            int apex = b.Add(centre + new Vector3(0f, size * squash, 0f), top);
            const int ring = 6;
            int[] rim = new int[ring];
            for (int r = 0; r < ring; r++)
            {
                float angle = spin + r * MathF.Tau / ring;
                float wobble = 0.85f + 0.3f * (float)rng.NextDouble();
                rim[r] = b.Add(centre + new Vector3(MathF.Cos(angle) * size * wobble, 0f, MathF.Sin(angle) * size * wobble), side);
            }
            for (int r = 0; r < ring; r++)
                b.Triangle(apex, rim[r], rim[(r + 1) % ring]);
        }

        if (b.Positions.Count == 0)
            return new CoverChunkMesh { Tier = tier, Empty = true };

        Mesh raw = default;
        raw.VertexCount = b.Positions.Count;
        raw.TriangleCount = b.Indices.Count / 3;
        raw.Vertices = (float*)Raylib.MemAlloc((uint)(b.Positions.Count * 3 * sizeof(float)));
        raw.Normals = (float*)Raylib.MemAlloc((uint)(b.Positions.Count * 3 * sizeof(float)));
        raw.TexCoords = (float*)Raylib.MemAlloc((uint)(b.Positions.Count * 2 * sizeof(float)));
        raw.Colors = (byte*)Raylib.MemAlloc((uint)(b.Positions.Count * 4));
        raw.Indices = (ushort*)Raylib.MemAlloc((uint)(b.Indices.Count * sizeof(ushort)));
        for (int i = 0; i < b.Positions.Count; i++)
        {
            raw.Vertices[i * 3] = b.Positions[i].X;
            raw.Vertices[i * 3 + 1] = b.Positions[i].Y;
            raw.Vertices[i * 3 + 2] = b.Positions[i].Z;
            raw.Normals[i * 3] = 0f;
            raw.Normals[i * 3 + 1] = 1f;
            raw.Normals[i * 3 + 2] = 0f;
            raw.TexCoords[i * 2] = 0f;
            raw.TexCoords[i * 2 + 1] = 0f;
            raw.Colors[i * 4] = b.Colors[i].R;
            raw.Colors[i * 4 + 1] = b.Colors[i].G;
            raw.Colors[i * 4 + 2] = b.Colors[i].B;
            raw.Colors[i * 4 + 3] = 255;
        }
        for (int i = 0; i < b.Indices.Count; i++)
            raw.Indices[i] = b.Indices[i];
        Raylib.UploadMesh(ref raw, false);
        return new CoverChunkMesh { Mesh = raw, Tier = tier };
    }
}
