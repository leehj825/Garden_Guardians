using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// What a Bramblekin carries, stuck to its hands so it walks and swings with it: a sword in the right hand and a shield on the left wrist (soldiers,
/// raiders), a bow in the left hand and a quiver on the back (hunters). Where a hand is comes from the skinned mesh itself — the average of the
/// hand's own vertices, moved by this frame's bone matrices — and which way the fingers point from the forearm to the hand; the bones' own frames
/// (the pose's transforms) do not line up with the mesh, so they are not used. Every item lies along +X in its model, 1 unit long, centred.
/// </summary>
public static unsafe class KinGear
{
    public const float SwordLength = 0.5f, BowLength = 0.7f, QuiverLength = 0.5f, ShieldWidth = 0.36f;

    public static float SwordBindUp = 0.3f, BowBindUp = 0.6f;

    private static readonly Dictionary<(nint Mesh, int Bone), int[]> _vertices = new();

    /// <summary>Where the vertices that belong mostly to <paramref name="bone"/> are now (their average, skinned), in the mesh's own space. False if the mesh has none.</summary>
    internal static bool Centroid(in Model pose, string bone, out Vector3 centre)
    {
        centre = default;
        int index = BramblekinModel.BoneIndex(pose, bone);
        if (index < 0)
            return false;
        Mesh mesh = pose.Meshes[0];
        var key = ((nint)pose.Meshes, index);
        if (!_vertices.TryGetValue(key, out int[]? list))
        {
            var found = new List<int>();
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                for (int k = 0; k < 4; k++)
                {
                    if (mesh.BoneIndices[v * 4 + k] == index && mesh.BoneWeights[v * 4 + k] >= 0.5f)
                    {
                        found.Add(v);
                        break;
                    }
                }
            }
            _vertices[key] = list = found.ToArray();
        }
        if (list.Length == 0)
            return false;

        Vector3 sum = default;
        foreach (int v in list)
        {
            var bind = new Vector3(mesh.Vertices[v * 3], mesh.Vertices[v * 3 + 1], mesh.Vertices[v * 3 + 2]);
            Vector3 skinned = default;
            for (int k = 0; k < 4; k++)
            {
                float w = mesh.BoneWeights[v * 4 + k];
                if (w <= 0f)
                    continue;
                Matrix4x4 m = pose.BoneMatrices[mesh.BoneIndices[v * 4 + k]]; // raylib's: translation in the last column
                skinned += w * new Vector3(m.M11 * bind.X + m.M12 * bind.Y + m.M13 * bind.Z + m.M14,
                                           m.M21 * bind.X + m.M22 * bind.Y + m.M23 * bind.Z + m.M24,
                                           m.M31 * bind.X + m.M32 * bind.Y + m.M33 * bind.Z + m.M34);
            }
            sum += skinned;
        }
        centre = sum / list.Length;
        return true;
    }

    /// <summary>A hand's place and which way its fingers point (from the forearm to the hand), or false if the mesh can't say.</summary>
    internal static bool Hand(in Model pose, string hand, string forearm, out Vector3 at, out Vector3 fingers)
    {
        at = fingers = default;
        if (!Centroid(pose, hand, out at))
            return false;
        fingers = Centroid(pose, forearm, out Vector3 arm) && Vector3.DistanceSquared(arm, at) > 1e-6f ? Vector3.Normalize(at - arm) : -Vector3.UnitY;
        return true;
    }

    /// <summary>A direction fixed in the bind pose (<paramref name="bindDirection"/>) carried along by the bone's own skinning rotation, so it turns exactly as that part of the body does.</summary>
    internal static Vector3 Turned(in Model pose, string bone, Vector3 bindDirection)
    {
        int index = BramblekinModel.BoneIndex(pose, bone);
        if (index < 0)
            return bindDirection;
        Matrix4x4 m = pose.BoneMatrices[index]; // raylib's: translation in the last column
        var d = Vector3.Normalize(bindDirection);
        return Vector3.Normalize(new Vector3(m.M11 * d.X + m.M12 * d.Y + m.M13 * d.Z, m.M21 * d.X + m.M22 * d.Y + m.M23 * d.Z, m.M31 * d.X + m.M32 * d.Y + m.M33 * d.Z));
    }

    /// <summary>A matrix (row-vector order) putting an item's long axis along <paramref name="along"/> and its model Z along <paramref name="normal"/> (made square to it), centred at <paramref name="at"/>.</summary>
    private static Matrix4x4 Frame(Vector3 along, Vector3 normal, Vector3 at)
    {
        Vector3 x = Vector3.Normalize(along);
        Vector3 z = normal - Vector3.Dot(normal, x) * x;
        z = z.LengthSquared() < 1e-6f ? Vector3.UnitZ : Vector3.Normalize(z);
        Vector3 y = Vector3.Cross(z, x);
        return new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, at.X, at.Y, at.Z, 1f);
    }

    /// <summary>Draws the gear a Bramblekin of this job carries; <paramref name="body"/> is where it stands and how big (mesh units to world).</summary>
    public static void Draw(in Model pose, Matrix4x4 body, bool sword, bool shield, bool bow, bool quiver)
    {
        var forward = Vector3.UnitZ; // the mesh faces +Z; its left hand is at +X
        if (sword && Hand(pose, "mixamorig:RightHand", "mixamorig:RightForeArm", out Vector3 right, out Vector3 rightFingers))
        {
            // The grip across the fist: the blade is the fingers' direction turned a quarter-turn forward-and-up (hanging arm: blade out in front; arm raised: blade up).
            Vector3 along = Turned(pose, "mixamorig:RightHand", new Vector3(-0.5f, -1f, SwordBindUp));
            Vector3 centre = right + along * (0.3f * SwordLength);
            GearModels.Draw(GearModels.Gear.Sword, Matrix4x4.CreateScale(SwordLength) * Frame(along, -Vector3.UnitX, centre) * body);
        }
        if ((shield || bow) && Hand(pose, "mixamorig:LeftHand", "mixamorig:LeftForeArm", out Vector3 left, out Vector3 leftFingers))
        {
            if (shield)
            {
                // Strapped to the wrist, face out to the left, along the forearm.
                Vector3 outward = Vector3.UnitX;
                Vector3 centre = left - leftFingers * 0.1f + outward * 0.02f;
                GearModels.Draw(GearModels.Gear.Shield, Matrix4x4.CreateScale(ShieldWidth) * Frame(leftFingers, outward, centre) * body);
            }
            if (bow)
            {
                // Held by its grip against the inside of the palm, standing along the hand with its belly forward.
                // Hanging from the fist by its grip, tips down (and the whole bow swings with the hand).
                Vector3 along = Vector3.Normalize(-Vector3.UnitY + BowBindUp * Turned(pose, "mixamorig:LeftHand", Vector3.UnitX));
                Vector3 centre = left - Vector3.UnitX * 0.03f + forward * 0.02f;
                GearModels.Draw(GearModels.Gear.Bow, Matrix4x4.CreateScale(BowLength) * Frame(along, forward, centre) * body);
            }
        }
        if (quiver && Centroid(pose, "mixamorig:Head", out Vector3 head))
        {
            // On the back, up on the right shoulder side, arrows up and out; it keeps to the back, not to any bone's turning.
            Vector3 along = Vector3.Normalize(new Vector3(-0.35f, 0.9f, -0.2f));
            Vector3 centre = head + QuiverFromHead;
            GearModels.Draw(GearModels.Gear.Quiver, Matrix4x4.CreateScale(QuiverLength) * Frame(along, new Vector3(0f, 0f, 1f), centre) * body);
        }
    }

    /// <summary>Where the quiver sits from the middle of the head (mesh units): well below it, behind the back, up on the right side — it rides with the upper body.</summary>
    private static readonly Vector3 QuiverFromHead = new(-0.06f, -0.28f, -0.43f);
}
