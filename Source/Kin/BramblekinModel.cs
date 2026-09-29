using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>Which of the four motion-captured clips a Bramblekin is currently playing — see <see cref="BramblekinModel.ClipFor"/>.</summary>
public enum BramblekinClip
{
    /// <summary>Held on the Walking clip's first frame: no need in particular, standing, eating, asleep.</summary>
    Idle,
    Walking,
    Fishing,
    Gathering,
    Combat,
}

/// <summary>
/// The Bramblekin character rig: one shared skinned mesh (loaded once from
/// Walking.glb, which — like the other three clips — was exported from
/// Tripo-rigged FBX to glTF, since raylib has no FBX importer) plus the four
/// motion clips, each read from its own glb but replayed against the shared
/// skeleton. <see cref="CreatePoseInstance"/> hands every Bramblekin its own
/// small bone-matrix buffer so hundreds of them can each be mid-stride at a
/// different frame while still sharing one GPU mesh and texture.
/// </summary>
internal static unsafe class BramblekinModel
{
    /// <summary>
    /// On Android, the SDK's own "Assets" project-folder convention strips
    /// that top-level folder name when packaging: a project file at
    /// Assets/Models/Bramblekin/Walking.glb is exposed to AssetManager (and
    /// so to raylib's own AAssetManager-backed file loader) as
    /// "Models/Bramblekin/Walking.glb", not "Assets/Models/...". Everywhere
    /// else, the process's current directory is whatever launched it — not
    /// necessarily the app's own folder — so the relative path (this time
    /// including "Assets", since desktop's CopyToOutputDirectory keeps the
    /// full project-relative path) is resolved against
    /// <see cref="AppContext.BaseDirectory"/> instead (see
    /// <see cref="SaveSystem.DefaultPath"/> for the same reasoning).
    /// </summary>
    private static readonly string AssetPath = OperatingSystem.IsAndroid()
        ? "Models/Bramblekin/"
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Bramblekin") + Path.DirectorySeparatorChar;

    /// <summary>The rig's height in its own units (see the FBX/glb bounding box) — divide by this to scale to <see cref="Bramblekin.BodyHeight"/> meters.</summary>
    public const float RawHeightUnits = 99.98168f;

    /// <summary>
    /// The rig's own rest-pose forward direction doesn't line up with the
    /// world +Z axis Bramblekin.Draw's yaw is measured from — this is the
    /// extra turn (radians) needed on top of that yaw so the model actually
    /// faces <see cref="GroundMover.Heading"/> instead of some fixed offset
    /// from it. Measured empirically by rendering the Walking clip with a
    /// known heading and reading off which way the rig actually faced.
    /// </summary>
    public const float ForwardYawOffset = 0f;

    /// <summary>Frames per second every clip was baked at (Tripo/Mixamo's usual export rate) — confirmed against each clip's own KeyFrameCount.</summary>
    private const float ClipFps = 60f;

    private static Model _baseModel;
    private static readonly Dictionary<BramblekinClip, ModelAnimation> _clips = new();
    private static bool _ready;

    /// <summary>
    /// Loads the shared mesh and every clip the first time any Bramblekin
    /// draws. Lazy, like <see cref="Bramblekin.EnsureBodyModel"/> was: this
    /// can't run before <see cref="Raylib.InitWindow"/> has created a GPU
    /// context to upload the mesh into.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_ready)
            return;

        _baseModel = Raylib.LoadModel(AssetPath + "Walking.glb");
        // The rig comes out of Tripo/FBX Z-up; raylib (like the rest of this
        // engine) is Y-up. Baking the fix-up into the model's own transform,
        // rather than into every draw call, keeps Bramblekin.Draw's terrain-tilt
        // rotation the only per-frame rotation it has to reason about.
        _baseModel.Transform = Matrix4x4.CreateRotationX(MathF.PI / 2f);

        _clips[BramblekinClip.Walking] = LoadClip("Walking.glb");
        _clips[BramblekinClip.Fishing] = LoadClip("FishingCast.glb");
        _clips[BramblekinClip.Combat] = LoadClip("SwordAndShieldSlash.glb");
        _clips[BramblekinClip.Gathering] = LoadClip("GatheringObjects.glb");
        // No separate idle clip was supplied: holding Walking's first frame stands in for one.
        _clips[BramblekinClip.Idle] = _clips[BramblekinClip.Walking];

        _ready = true;
    }

    private static ModelAnimation LoadClip(string fileName)
    {
        Span<ModelAnimation> animations = Raylib.LoadModelAnimations(AssetPath + fileName);
        if (animations.Length == 0)
            throw new InvalidOperationException($"No animation found in '{AssetPath + fileName}' — is the Assets folder missing or not copied next to the app?");
        return animations[0];
    }

    /// <summary>Which clip a given <see cref="BramblekinState"/> plays — see the type's own doc for the four clips this maps onto.</summary>
    public static BramblekinClip ClipFor(BramblekinState state) => state switch
    {
        BramblekinState.Fishing => BramblekinClip.Fishing,
        BramblekinState.Fighting or BramblekinState.Attacking or BramblekinState.Hunting or
            BramblekinState.Dueling or BramblekinState.Guarding => BramblekinClip.Combat,
        BramblekinState.Collecting or BramblekinState.Building or BramblekinState.Farming or
            BramblekinState.Foraging or BramblekinState.Stockpiling or BramblekinState.GatheringHoney or
            BramblekinState.Raiding => BramblekinClip.Gathering,
        BramblekinState.Wandering or BramblekinState.Socializing or BramblekinState.Following or
            BramblekinState.HeadingHome or BramblekinState.Traveling or BramblekinState.Searching or
            BramblekinState.Fleeing or BramblekinState.Drinking => BramblekinClip.Walking,
        _ => BramblekinClip.Idle,
    };

    /// <summary>The keyframe index <paramref name="timeSeconds"/> lands on within <paramref name="clip"/>, looping.</summary>
    public static int FrameAt(BramblekinClip clip, float timeSeconds)
    {
        ModelAnimation animation = _clips[clip];
        if (animation.KeyFrameCount <= 0)
            return 0;
        int frame = (int)(timeSeconds * ClipFps) % animation.KeyFrameCount;
        return frame < 0 ? frame + animation.KeyFrameCount : frame;
    }

    public static void Play(ref Model instance, BramblekinClip clip, float timeSeconds) =>
        Raylib.UpdateModelAnimation(instance, _clips[clip], FrameAt(clip, timeSeconds));

    /// <summary>
    /// A cheap per-Bramblekin clone: same Meshes/Materials/Skeleton pointers
    /// as the shared <see cref="_baseModel"/> (nothing is re-uploaded to the
    /// GPU), but its own <see cref="Model.BoneMatrices"/>/<see cref="Model.CurrentPose"/>
    /// buffers, so <see cref="Raylib.UpdateModelAnimation"/> can pose it
    /// independently of every other Bramblekin sharing the same mesh.
    /// Release with <see cref="DestroyPoseInstance"/> — never
    /// <see cref="Raylib.UnloadModel"/>, which would free the shared mesh out
    /// from under every other Bramblekin.
    /// </summary>
    public static Model CreatePoseInstance()
    {
        EnsureLoaded();
        Model instance = _baseModel;
        int boneCount = _baseModel.Skeleton.BoneCount;
        instance.BoneMatrices = (Matrix4x4*)NativeMemory.AllocZeroed((nuint)boneCount, (nuint)sizeof(Matrix4x4));
        instance.CurrentPose = (Transform*)NativeMemory.AllocZeroed((nuint)boneCount, (nuint)sizeof(Transform));
        return instance;
    }

    /// <summary>Frees a pose instance's own buffers (see <see cref="CreatePoseInstance"/>) — the shared mesh they point at is untouched.</summary>
    public static void DestroyPoseInstance(ref Model instance)
    {
        if (instance.BoneMatrices is not null)
            NativeMemory.Free(instance.BoneMatrices);
        if (instance.CurrentPose is not null)
            NativeMemory.Free(instance.CurrentPose);
        instance.BoneMatrices = null;
        instance.CurrentPose = null;
    }
}
