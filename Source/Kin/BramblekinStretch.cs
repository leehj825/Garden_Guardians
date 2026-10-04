using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A development aid: GARDEN_STRETCH=Running,Combat,... (and GARDEN_STRETCH_SEX=Female) poses the kin mesh through every frame of those clips, the way
/// the game does, and prints which edges of the mesh stretch the most (and which bones carry the two ends of each), to find skinning that tears the cloth.
/// </summary>
internal static unsafe class BramblekinStretch
{
    public static void Run(string clips)
    {
        Sex sex = Environment.GetEnvironmentVariable("GARDEN_STRETCH_SEX") == "Female" ? Sex.Female : Sex.Male;
        float factor = float.TryParse(Environment.GetEnvironmentVariable("GARDEN_STRETCH_FACTOR"), System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 2.0f;
        Model pose = BramblekinModel.CreatePoseInstance(sex);
        Mesh mesh = pose.Meshes[0];
        int vertexCount = mesh.VertexCount;
        int triangles = mesh.TriangleCount;
        string BoneName(int index) => (System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)pose.Skeleton.Bones[index].Name) ?? "?").Replace("mixamorig:", "");
        int Dominant(int v)
        {
            int best = 0;
            for (int k = 1; k < 4; k++)
                if (mesh.BoneWeights[v * 4 + k] > mesh.BoneWeights[v * 4 + best])
                    best = k;
            return mesh.BoneIndices[v * 4 + best];
        }

        var edges = new HashSet<(int, int)>();
        for (int t = 0; t < triangles; t++)
        {
            int a = mesh.Indices[t * 3], b = mesh.Indices[t * 3 + 1], c = mesh.Indices[t * 3 + 2];
            edges.Add((Math.Min(a, b), Math.Max(a, b)));
            edges.Add((Math.Min(b, c), Math.Max(b, c)));
            edges.Add((Math.Min(c, a), Math.Max(c, a)));
        }
        var armBone = new bool[pose.Skeleton.BoneCount];
        for (int i = 0; i < armBone.Length; i++)
        {
            string n = BoneName(i);
            armBone[i] = (n.Contains("Arm") || n.Contains("Hand")) && !n.Contains("Shoulder");
        }
        float ArmShare(int v)
        {
            float share = 0f;
            for (int k = 0; k < 4; k++)
                if (armBone[mesh.BoneIndices[v * 4 + k]])
                    share += mesh.BoneWeights[v * 4 + k];
            return share;
        }
        Vector3 Bind(int v) => new(mesh.Vertices[v * 3], mesh.Vertices[v * 3 + 1], mesh.Vertices[v * 3 + 2]);
        Vector3 Anim(int v) => new(mesh.AnimVertices[v * 3], mesh.AnimVertices[v * 3 + 1], mesh.AnimVertices[v * 3 + 2]);

        foreach (string name in clips.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            BramblekinClip clip = Enum.Parse<BramblekinClip>(name);
            float seconds = BramblekinModel.NaturalSeconds(clip);
            int armEdgeHits = 0;
            float armEdgeWorst = 0f;
            var byPair = new Dictionary<(string, string), (int Count, float Worst)>();
            float peak = 0f;
            int frames = 40;
            for (int i = 0; i < frames; i++)
            {
                BramblekinModel.PlayProgress(ref pose, clip, i / (float)frames);
                // (The animated mesh is in the skeleton's own units: the median edge says by how much, and an edge is stretched only beyond that.)
                var all = new List<float>();
                foreach ((int a, int b) in edges)
                {
                    float length = Vector3.Distance(Bind(a), Bind(b));
                    if (length >= 0.01f)
                        all.Add(Vector3.Distance(Anim(a), Anim(b)) / length);
                }
                all.Sort();
                float unit = all[all.Count / 2];
                foreach ((int a, int b) in edges)
                {
                    float bind = Vector3.Distance(Bind(a), Bind(b));
                    if (bind < 0.01f)
                        continue;
                    float ratio = Vector3.Distance(Anim(a), Anim(b)) / bind / unit;
                    peak = MathF.Max(peak, ratio);
                    if (MathF.Abs(ArmShare(a) - ArmShare(b)) > 0.4f && ratio >= factor) // (an edge between a vertex the arm drives and one it hardly does)
                    {
                        armEdgeHits++;
                        armEdgeWorst = MathF.Max(armEdgeWorst, ratio);
                    }
                    if (ratio < factor)
                        continue;
                    int da = Dominant(a), db = Dominant(b);
                    var key = da <= db ? (BoneName(da), BoneName(db)) : (BoneName(db), BoneName(da));
                    byPair.TryGetValue(key, out var seen);
                    byPair[key] = (seen.Count + 1, MathF.Max(seen.Worst, ratio));
                }
            }
            Console.WriteLine($"STRETCH {sex} {clip} ({seconds:0.00}s): longest edge x{peak:0.00}; edges over x{factor:0.0}, by the bones at their two ends:");
            Console.WriteLine($"ARMEDGES {sex} {clip}: {armEdgeHits} edge-frames over x{factor:0.0} between arm-driven and other vertices, worst x{armEdgeWorst:0.0}");
            foreach (var pair in byPair.OrderByDescending(p => p.Value.Count).Take(8))
                Console.WriteLine($"   {pair.Value.Count,6}  worst x{pair.Value.Worst,5:0.0}   {pair.Key.Item1} <-> {pair.Key.Item2}");
        }
    }
}
