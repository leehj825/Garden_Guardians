using System.Numerics;

namespace GardenGuardians;

/// <summary>
/// Grows a terrain from a seed: gently rolling ground, one to three ponds at one water level, the Giant Oak
/// on a levelled patch, boulders and plant clumps along the banks, and the spring a creek runs from. The same
/// seed always gives the same garden. The props are cut out of the hand-made terrain models (see
/// <see cref="ProceduralKit"/>); this only decides where they stand. It's the C# twin of
/// Tools/procedural/generate_terrain.py, which is where the look was worked out.
/// </summary>
public static class TerrainGenerator
{
    /// <summary>The water level, and how far dry ground keeps above it (m). The mean ground is 0.</summary>
    private const float WaterLevel = -1.2f, Guard = 0.5f;

    private sealed class Pond
    {
        public float X, Z, R, Stretch, Angle;
        public float Wobble0, Wobble1, Wobble2;

        public float Ring(float theta) => 1f + 0.16f * MathF.Sin(2f * theta + Wobble0) + 0.10f * MathF.Sin(3f * theta + Wobble1) + 0.06f * MathF.Sin(5f * theta + Wobble2);

        /// <summary>How far from the pond's middle (1 = its water's edge) the point (x, z) lies, as a share of the pond's own size.</summary>
        public float NormalisedDistance(float x, float z)
        {
            float dx = x - X, dz = z - Z;
            float c = MathF.Cos(Angle), s = MathF.Sin(Angle);
            float u = (dx * c + dz * s) / Stretch, v = -dx * s + dz * c;
            return MathF.Sqrt(u * u + v * v) / (R * Ring(MathF.Atan2(v, u)));
        }

        /// <summary>A point on the bank at angle <paramref name="theta"/>, <paramref name="d"/> times the water's edge out.</summary>
        public (float X, float Z) BankPoint(float theta, float d)
        {
            float c = MathF.Cos(Angle), s = MathF.Sin(Angle);
            float u = MathF.Cos(theta) * R * Ring(theta) * d * Stretch;
            float v = MathF.Sin(theta) * R * Ring(theta) * d;
            return (X + u * c - v * s, Z + u * s + v * c);
        }
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Grows the terrain for <paramref name="seed"/>.</summary>
    public static TerrainSet Generate(int seed)
    {
        var rng = new SeededRandom(seed);
        float half = TerrainData.DefaultHalf;
        float step = TerrainData.Step;
        int size = (int)MathF.Round(2f * half / step) + 1;
        float X(int i) => -half + i * step;

        // Gentle rolling ground: broad swells a few metres high, a little grain on top.
        float[] ground = Noise(rng, size, new (float Sigma, float Amplitude)[] { (24f, 0.55f), (10f, 0.30f), (4f, 0.08f) });

        // Ponds.
        var ponds = new List<Pond>();
        float roll = (float)rng.NextDouble();
        int wanted = roll < 0.3f ? 1 : roll < 0.9f ? 2 : 3;
        for (int tries = 0; tries < 200 && ponds.Count < wanted; tries++)
        {
            var pond = new Pond { R = rng.Uniform(5.5f, 12f), Stretch = rng.Uniform(1f, 1.9f), Angle = rng.Uniform(0f, MathF.PI) };
            float reach = pond.R * pond.Stretch + 12f;
            if (reach >= half - 2f)
                continue;
            pond.X = rng.Uniform(-half + reach, half - reach);
            pond.Z = rng.Uniform(-half + reach, half - reach);
            pond.Wobble0 = rng.Uniform(0f, 100f);
            pond.Wobble1 = rng.Uniform(0f, 100f);
            pond.Wobble2 = rng.Uniform(0f, 100f);
            bool crowded = false;
            foreach (Pond other in ponds)
                crowded |= MathF.Sqrt((pond.X - other.X) * (pond.X - other.X) + (pond.Z - other.Z) * (pond.Z - other.Z)) < pond.R * pond.Stretch + other.R * other.Stretch + 14f;
            if (!crowded)
                ponds.Add(pond);
        }

        // Each pond is a bowl below the water level, its banks easing back up to the ground; the land is never wet.
        float floor = WaterLevel + Guard;
        var pondDistance = new float[size * size]; // The smallest normalised distance to any pond: below 1 is water.
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                float h = ground[j * size + i];
                float lifted = floor + 0.5f * ((h - floor) + MathF.Sqrt((h - floor) * (h - floor) + 0.25f));
                float result = lifted;
                float nearest = 9f;
                foreach (Pond pond in ponds)
                {
                    float d = pond.NormalisedDistance(X(i), X(j));
                    float profile = d < 1f ? WaterLevel - 0.55f * MathF.Pow(Math.Clamp(1f - d * d, 0f, 1f), 0.8f) : WaterLevel + 0.8f * (d - 1f);
                    float w = SmoothStep((1.9f - d) / 0.9f);
                    result = result * (1f - w) + profile * w;
                    nearest = MathF.Min(nearest, d);
                }
                ground[j * size + i] = result;
                pondDistance[j * size + i] = nearest;
            }
        }

        var placed = new List<PlacedProp>();
        PlacedProp? oak = PlaceOak(rng, ground, size, half, step, ponds);
        if (oak is not null)
        {
            LevelUnder(ground, size, half, step, oak);
            placed.Add(oak);
        }
        var context = new PlaceContext(rng, ground, pondDistance, size, half, step, placed);
        ScatterProps(context, ponds);

        (float springX, float springZ) = PickSpring(ground, size, half, step, oak);

        // What the game reads: the oak's trunk and hive, and the circles walkers keep out of.
        var oakCircles = new List<float>();
        var propCircles = new List<float>();
        foreach (PlacedProp prop in placed)
            (prop.Item.Kind == KitKind.Oak ? oakCircles : propCircles).AddRange(prop.Circles);
        float c = oak is null ? 1f : MathF.Cos(oak.Yaw), s = oak is null ? 0f : MathF.Sin(oak.Yaw);
        KitItem? oakItem = oak?.Item;
        return new TerrainSet
        {
            ModelFile = "",
            Half = half,
            PondLevel = WaterLevel,
            OakX = oak is null || oakItem is null ? 0f : oak.X + (oakItem.TrunkDx * c - oakItem.TrunkDz * s) * oak.Scale,
            OakZ = oak is null || oakItem is null ? 0f : oak.Z + (oakItem.TrunkDx * s + oakItem.TrunkDz * c) * oak.Scale,
            HiveAngle = oak is null || oakItem is null ? 0f : oakItem.HiveAngle + oak.Yaw,
            HiveSurface = oakItem?.HiveSurface ?? 1f,
            OakTrunkRadius = (oakItem?.TrunkRadius ?? 1f) * (oak?.Scale ?? 1f),
            OakTrunkHeight = (oakItem?.TrunkHeight ?? 1f) * (oak?.Scale ?? 1f),
            SpringX = springX,
            SpringZ = springZ,
            PropCircles = propCircles.ToArray(),
            OakCircles = oakCircles.ToArray(),
            GeneratedHeights = ground,
            Seed = seed,
            Props = placed,
        };
    }

    // --- Noise -----------------------------------------------------------------------------------

    /// <summary>Smooth zero-mean noise on a size x size grid: white noise blurred by each octave's sigma (in cells), scaled to its amplitude, summed.</summary>
    private static float[] Noise(SeededRandom rng, int size, (float Sigma, float Amplitude)[] octaves)
    {
        var total = new float[size * size];
        foreach (var (sigma, amplitude) in octaves)
        {
            var layer = new float[size * size];
            for (int i = 0; i < layer.Length; i++)
                layer[i] = rng.Normal();
            Blur(layer, size, sigma);
            double mean = 0, sq = 0;
            foreach (float v in layer)
            {
                mean += v;
                sq += (double)v * v;
            }
            mean /= layer.Length;
            double deviation = Math.Sqrt(Math.Max(sq / layer.Length - mean * mean, 1e-12));
            for (int i = 0; i < layer.Length; i++)
                total[i] += amplitude * (float)((layer[i] - mean) / deviation);
        }
        double average = 0;
        foreach (float v in total)
            average += v;
        average /= total.Length;
        for (int i = 0; i < total.Length; i++)
            total[i] -= (float)average;
        return total;
    }

    /// <summary>A gaussian blur (edges mirrored), one axis and then the other.</summary>
    private static void Blur(float[] data, int size, float sigma)
    {
        int radius = Math.Min((int)MathF.Ceiling(3f * sigma), size - 1);
        var kernel = new float[2 * radius + 1];
        float sum = 0f;
        for (int k = -radius; k <= radius; k++)
        {
            kernel[k + radius] = MathF.Exp(-0.5f * k * k / (sigma * sigma));
            sum += kernel[k + radius];
        }
        for (int k = 0; k < kernel.Length; k++)
            kernel[k] /= sum;

        var line = new float[size];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int a = 0; a < size; a++)
            {
                for (int b = 0; b < size; b++)
                {
                    float acc = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int t = Mirror(b + k, size);
                        acc += kernel[k + radius] * (pass == 0 ? data[a * size + t] : data[t * size + a]);
                    }
                    line[b] = acc;
                }
                for (int b = 0; b < size; b++)
                {
                    if (pass == 0)
                        data[a * size + b] = line[b];
                    else
                        data[b * size + a] = line[b];
                }
            }
        }
    }

    private static int Mirror(int i, int n) => i < 0 ? -i - 1 : i >= n ? 2 * n - i - 1 : i;

    // --- The oak ---------------------------------------------------------------------------------

    private static float DistanceToPondEdge(List<Pond> ponds, float x, float z)
    {
        float best = 1e9f;
        foreach (Pond pond in ponds)
            best = MathF.Min(best, MathF.Sqrt((x - pond.X) * (x - pond.X) + (z - pond.Z) * (z - pond.Z)) - pond.R * MathF.Max(1f, pond.Stretch));
        return best;
    }

    private static float HeightAt(float[] ground, int size, float half, float step, float x, float z)
    {
        int i = Math.Clamp((int)MathF.Round((x + half) / step), 0, size - 1);
        int j = Math.Clamp((int)MathF.Round((z + half) / step), 0, size - 1);
        return ground[j * size + i];
    }

    private static float[] Rotated(KitItem item, float x, float z, float yaw, float scale)
    {
        float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
        var circles = new float[item.Circles.Length];
        for (int k = 0; k + 2 < item.Circles.Length; k += 3)
        {
            float cx = item.Circles[k], cz = item.Circles[k + 1];
            circles[k] = x + (cx * c - cz * s) * scale;
            circles[k + 1] = z + (cx * s + cz * c) * scale;
            circles[k + 2] = item.Circles[k + 2] * scale;
        }
        return circles;
    }

    private static PlacedProp? PlaceOak(SeededRandom rng, float[] ground, int size, float half, float step, List<Pond> ponds)
    {
        List<KitItem> oaks = ProceduralKit.Items.Where(i => i.Kind == KitKind.Oak).ToList();
        if (oaks.Count == 0)
            return null;
        KitItem item = oaks[rng.Next(oaks.Count)];
        for (int attempt = 0; attempt < 600; attempt++)
        {
            if (attempt > 0 && attempt % 200 == 0)
                item = oaks.MinBy(o => o.Reach)!; // The roomiest map can still take the smallest oak.
            float margin = item.Reach * 0.8f + 3f;
            float x = rng.Uniform(-half + margin, half - margin), z = rng.Uniform(-half + margin, half - margin);
            if (DistanceToPondEdge(ponds, x, z) < item.Reach * 0.45f + 4f - attempt * 0.01f)
                continue;
            float yaw = rng.Uniform(0f, MathF.Tau);
            // It stands on the ground round its foot, levelled below (see LevelUnder).
            var inside = new List<float>();
            float reach = item.Reach * 0.6f;
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    float dx = -half + i * step - x, dz = -half + j * step - z;
                    if (dx * dx + dz * dz < reach * reach)
                        inside.Add(ground[j * size + i]);
                }
            }
            if (inside.Count == 0)
                continue;
            inside.Sort();
            float target = inside[inside.Count / 2];
            return new PlacedProp { Item = item, X = x, Z = z, Base = target, Yaw = yaw, Scale = 1f, Circles = Rotated(item, x, z, yaw, 1f) };
        }
        return null;
    }

    /// <summary>Levels the ground round the oak to the height its foot stands at, blending back into the lawn.</summary>
    private static void LevelUnder(float[] ground, int size, float half, float step, PlacedProp oak)
    {
        float rr = oak.Item.Reach * 0.6f + 4f;
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                float dx = -half + i * step - oak.X, dz = -half + j * step - oak.Z;
                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance >= rr)
                    continue;
                float w = SmoothStep((rr - distance) / 4f);
                ground[j * size + i] = ground[j * size + i] * (1f - w) + oak.Base * w;
            }
        }
    }

    // --- Rocks and plants ------------------------------------------------------------------------

    private sealed class PlaceContext
    {
        public readonly SeededRandom Rng;
        public readonly float[] Ground, PondDistance;
        public readonly int Size;
        public readonly float Half, Step;
        public readonly List<PlacedProp> Placed;
        public readonly List<KitItem> Rocks, Plants;

        public PlaceContext(SeededRandom rng, float[] ground, float[] pondDistance, int size, float half, float step, List<PlacedProp> placed)
        {
            Rng = rng;
            Ground = ground;
            PondDistance = pondDistance;
            Size = size;
            Half = half;
            Step = step;
            Placed = placed;
            Rocks = ProceduralKit.Items.Where(i => i.Kind == KitKind.Rock).ToList();
            Plants = ProceduralKit.Items.Where(i => i.Kind == KitKind.Plant).ToList();
        }

        public float PondDistanceAt(float x, float z)
        {
            int i = Math.Clamp((int)MathF.Round((x + Half) / Step), 0, Size - 1);
            int j = Math.Clamp((int)MathF.Round((z + Half) / Step), 0, Size - 1);
            return PondDistance[j * Size + i];
        }

        public bool Clear(float[] circles)
        {
            foreach (PlacedProp other in Placed)
            {
                for (int a = 0; a + 2 < circles.Length; a += 3)
                {
                    for (int b = 0; b + 2 < other.Circles.Length; b += 3)
                    {
                        float dx = circles[a] - other.Circles[b], dz = circles[a + 1] - other.Circles[b + 1];
                        float reach = circles[a + 2] + other.Circles[b + 2] + 0.3f;
                        if (dx * dx + dz * dz < reach * reach)
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Tries a few times to stand a prop from <paramref name="pool"/> at (x, z), clear of everything else (a rock never in the water).</summary>
        public void TryPlace(List<KitItem> pool, float x, float z)
        {
            if (pool.Count == 0)
                return;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                KitItem item = pool[Rng.Next(pool.Count)];
                float yaw = Rng.Uniform(0f, MathF.Tau), scale = Rng.Uniform(0.8f, 1.3f);
                if (item.Kind == KitKind.Rock && PondDistanceAt(x, z) < 1.08f)
                    continue;
                float[] circles = Rotated(item, x, z, yaw, scale);
                if (!Clear(circles))
                    continue;
                Placed.Add(new PlacedProp { Item = item, X = x, Z = z, Base = HeightAt(Ground, Size, Half, Step, x, z), Yaw = yaw, Scale = scale, Circles = circles });
                return;
            }
        }
    }

    private static void ScatterProps(PlaceContext ctx, List<Pond> ponds)
    {
        SeededRandom rng = ctx.Rng;
        float limit = ctx.Half - 3f;
        foreach (Pond pond in ponds)
        {
            // A ring of plants along the bank, and a pile of boulders on one side.
            float perimeter = MathF.Tau * pond.R * (1f + pond.Stretch) / 2f;
            int plants = (int)(perimeter / 9f) + 2;
            for (int k = 0; k < plants; k++)
            {
                (float x, float z) = pond.BankPoint(rng.Uniform(0f, MathF.Tau), rng.Uniform(1.0f, 1.25f));
                if (MathF.Abs(x) < limit && MathF.Abs(z) < limit)
                    ctx.TryPlace(ctx.Plants, x, z);
            }
            float theta = rng.Uniform(0f, MathF.Tau);
            int boulders = rng.Next(2, 6);
            for (int k = 0; k < boulders; k++)
            {
                (float x, float z) = pond.BankPoint(theta + rng.Normal() * 0.25f, rng.Uniform(1.0f, 1.45f));
                if (MathF.Abs(x) < limit && MathF.Abs(z) < limit)
                    ctx.TryPlace(ctx.Rocks, x, z);
            }
        }
        int strays = (int)(ctx.Half * ctx.Half / 900f); // A few on dry ground.
        for (int k = 0; k < strays; k++)
        {
            float x = rng.Uniform(-ctx.Half + 4f, ctx.Half - 4f), z = rng.Uniform(-ctx.Half + 4f, ctx.Half - 4f);
            if (HeightAt(ctx.Ground, ctx.Size, ctx.Half, ctx.Step, x, z) > WaterLevel + Guard && ctx.PondDistanceAt(x, z) > 1.6f)
                ctx.TryPlace(rng.NextDouble() < 0.5 ? ctx.Rocks : ctx.Plants, x, z);
        }
    }

    // --- The creek's spring ----------------------------------------------------------------------

    /// <summary>Where the creek rises: in the map's outer band, well away from the water and the oak, where the game's own trace runs longest downhill.</summary>
    private static (float X, float Z) PickSpring(float[] ground, int size, float half, float step, PlacedProp? oak)
    {
        // Distance (m) to the nearest water, by two sweeps over the grid.
        var toWater = new float[size * size];
        for (int i = 0; i < toWater.Length; i++)
            toWater[i] = ground[i] < WaterLevel ? 0f : 1e6f;
        const float diagonal = 1.4142f;
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                float best = toWater[j * size + i];
                if (i > 0) best = MathF.Min(best, toWater[j * size + i - 1] + 1f);
                if (j > 0) best = MathF.Min(best, toWater[(j - 1) * size + i] + 1f);
                if (i > 0 && j > 0) best = MathF.Min(best, toWater[(j - 1) * size + i - 1] + diagonal);
                if (i < size - 1 && j > 0) best = MathF.Min(best, toWater[(j - 1) * size + i + 1] + diagonal);
                toWater[j * size + i] = best;
            }
        }
        for (int j = size - 1; j >= 0; j--)
        {
            for (int i = size - 1; i >= 0; i--)
            {
                float best = toWater[j * size + i];
                if (i < size - 1) best = MathF.Min(best, toWater[j * size + i + 1] + 1f);
                if (j < size - 1) best = MathF.Min(best, toWater[(j + 1) * size + i] + 1f);
                if (i < size - 1 && j < size - 1) best = MathF.Min(best, toWater[(j + 1) * size + i + 1] + diagonal);
                if (i > 0 && j < size - 1) best = MathF.Min(best, toWater[(j + 1) * size + i - 1] + diagonal);
                toWater[j * size + i] = best;
            }
        }

        var quantised = new float[ground.Length]; // The game reads heights in whole centimetres.
        for (int i = 0; i < ground.Length; i++)
            quantised[i] = MathF.Round(ground[i] * 100f) / 100f;

        float oakDistance(float x, float z)
        {
            float best = 99f;
            if (oak is null)
                return best;
            for (int k = 0; k + 2 < oak.Circles.Length; k += 3)
                best = MathF.Min(best, MathF.Sqrt((x - oak.Circles[k]) * (x - oak.Circles[k]) + (z - oak.Circles[k + 1]) * (z - oak.Circles[k + 1])) - oak.Circles[k + 2]);
            return best;
        }

        (float X, float Z) chosen = (half * 0.7f, half * 0.7f);
        float chosenScore = -1f;
        for (int pass = 0; pass < 2 && chosenScore < 0f; pass++)
        {
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    if ((i + j) % 3 != 0)
                        continue;
                    float x = -half + i * step, z = -half + j * step;
                    float edge = MathF.Max(MathF.Abs(x), MathF.Abs(z));
                    if (edge < half * 0.6f || edge > half - 5f)
                        continue;
                    if (pass == 0 ? toWater[j * size + i] <= 20f || oakDistance(x, z) <= 14f : toWater[j * size + i] <= 8f)
                        continue;
                    float score = CreekSteps(quantised, size, half, step, x, z) + 0.001f * quantised[j * size + i];
                    if (score > chosenScore)
                    {
                        chosenScore = score;
                        chosen = (x, z);
                    }
                }
            }
        }
        return chosen;
    }

    /// <summary>How many one-metre steps the game's creek trace (WaterMap.TraceCreek) takes downhill from (x, z).</summary>
    private static int CreekSteps(float[] g, int size, float half, float step, float x, float z)
    {
        float H(float px, float pz)
        {
            float fx = Math.Clamp((px + half) / step, 0f, size - 1.001f), fz = Math.Clamp((pz + half) / step, 0f, size - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            return g[iz * size + ix] * (1 - tx) * (1 - tz) + g[iz * size + ix + 1] * tx * (1 - tz) + g[(iz + 1) * size + ix] * (1 - tx) * tz + g[(iz + 1) * size + ix + 1] * tx * tz;
        }

        int steps = 0;
        for (int k = 0; k < 60; k++)
        {
            const float e = 0.05f;
            float sx = (H(x + e, z) - H(x - e, z)) / (2f * e), sz = (H(x, z + e) - H(x, z - e)) / (2f * e);
            float norm = MathF.Sqrt(sx * sx + sz * sz);
            if (norm < 0.02f)
                break;
            float nx = x - sx / norm, nz = z - sz / norm;
            if (H(nx, nz) >= H(x, z) - 0.005f || MathF.Abs(nx) > half - 4f || MathF.Abs(nz) > half - 4f)
                break;
            x = nx;
            z = nz;
            steps++;
        }
        return steps;
    }
}
