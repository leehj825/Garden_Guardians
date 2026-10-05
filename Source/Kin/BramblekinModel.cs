using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>Which of the four motion-captured clips a Bramblekin is currently playing — see <see cref="BramblekinModel.ClipFor"/>.</summary>
public enum BramblekinClip
{
    /// <summary>The idle loop (Idle.glb): no need in particular, standing, eating, asleep.</summary>
    Idle,
    Walking,
    /// <summary>The guard's own walk, sword and shield at the ready (Sword_And_Shield_Walk): played whenever a guard or soldier moves.</summary>
    SwordsmanWalking,
    /// <summary>A guard standing still (and eating, resting): held on one frame, the sword arm down at its side with the blade pointing forward and up (Tools/make_guard_clips.py).</summary>
    SwordsmanIdle,
    Fishing,
    Gathering,
    /// <summary>The sword and shield slash (the first blow of a soldier's or fighter's pair).</summary>
    Combat,
    /// <summary>The sword and shield attack (the second blow of the pair).</summary>
    SwordAttack,
    /// <summary>A guard with a spear: the bayonet stab.</summary>
    SpearStab,
    /// <summary>A hunter: the standing aim and recoil.</summary>
    AimRecoil,
    /// <summary>Bending to pick something up (food, a twig).</summary>
    PickingUp,
    /// <summary>Running (Running.fbx): a controlled kin with the run toggle on, while it moves.</summary>
    Running,
    /// <summary>Jumping (Unarmed_Jump.fbx, hips kept down: the game lifts the body itself): crouch, spring, hang, land. Played once over the jump's airtime.</summary>
    Jump,
}

/// <summary>
/// The Bramblekin character rig: one shared skinned mesh (loaded once from
/// Male.glb / Female.glb (Tools/convert_rigged_kin.py), whose skeleton — like the clips — was converted from the
/// Mixamo-rigged FBX to Y-up, metre-scaled glTF with Blender, since raylib
/// has no FBX importer and assimp's mangles Mixamo pre-rotations) plus the four
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

    /// <summary>The rig's height in its own units (metres, as Blender exports it) — divide by this to scale to <see cref="Bramblekin.BodyHeight"/>.</summary>
    public const float RawHeightUnits = 0.9998169f;

    /// <summary>
    /// Extra turn (radians) on top of Bramblekin.Draw's yaw, which assumes
    /// the rig faces +Z at rest. It does: the Walking clip's own root motion
    /// (stripped before shipping) ran along +Z.
    /// </summary>
    public const float ForwardYawOffset = 0f;

    /// <summary>Frames per second every clip was baked at (Tripo/Mixamo's usual export rate) — confirmed against each clip's own KeyFrameCount.</summary>
    private const float ClipFps = 60f;

    private static Model _baseModel;

    /// <summary>
    /// The female mesh, rigged (Tools/convert_female.py) to the male's own
    /// skeleton: it shares his clips, which are baked for that skeleton, so
    /// every clip drives both. Only the mesh, skin weights and texture differ.
    /// </summary>
    private static Model _femaleModel;

    /// <summary>Every mesh: [male 0 / female 1, level of detail].</summary>
    private static readonly Model[,] _lods = new Model[2, Lods];
    private static readonly Dictionary<BramblekinClip, ModelAnimation> _clips = new();
    private static bool _ready;

    /// <summary>
    /// Loads the shared mesh and every clip the first time any Bramblekin
    /// draws. Lazy: this can't run before <see cref="Raylib.InitWindow(int, int, string)"/> has created a GPU
    /// context to upload the mesh into.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_ready)
            return;

        _baseModel = Raylib.LoadModel(AssetPath + "Male.glb");
        _femaleModel = Raylib.LoadModel(AssetPath + "Female.glb");
        _lods[0, 0] = _baseModel;
        _lods[1, 0] = _femaleModel;
        for (int lod = 1; lod < Lods; lod++)
        {
            _lods[0, lod] = Raylib.LoadModel(AssetPath + $"Male_lod{lod}.glb");
            _lods[1, lod] = Raylib.LoadModel(AssetPath + $"Female_lod{lod}.glb");
        }
        // Same skeleton: about 4,000 triangles and a 1024 px picture, then 1,000 and 512 px, instead of 19,800 and 2048 px.

        _clips[BramblekinClip.Walking] = LoadClip("Walking.glb");
        _clips[BramblekinClip.Fishing] = LoadClip("FishingCast.glb");
        _clips[BramblekinClip.SwordsmanWalking] = LoadClip("GuardWalk.glb"); // (the sword walk with the sword arm brought down: Tools/make_guard_clips.py)
        _clips[BramblekinClip.SwordsmanIdle] = LoadClip("GuardIdle.glb");
        _clips[BramblekinClip.Combat] = LoadClip("SwordAndShieldSlash.glb");
        _clips[BramblekinClip.Gathering] = LoadClip("GatheringObjects.glb");
        _clips[BramblekinClip.SwordAttack] = LoadClip("SwordAndShieldAttack.glb");
        _clips[BramblekinClip.SpearStab] = LoadClip("BayonetStab.glb");
        _clips[BramblekinClip.AimRecoil] = LoadClip("StandingAimRecoil.glb");
        _clips[BramblekinClip.PickingUp] = LoadClip("PickingUp.glb");
        _clips[BramblekinClip.Running] = LoadClip("Running.glb");
        _clips[BramblekinClip.Jump] = LoadClip("Jump.glb");
        _clips[BramblekinClip.Idle] = LoadClip("Idle.glb");

        _ready = true;
    }

    private static ModelAnimation LoadClip(string fileName)
    {
        Span<ModelAnimation> animations = Raylib.LoadModelAnimations(AssetPath + fileName);
        if (animations.Length == 0)
            throw new InvalidOperationException($"No animation found in '{AssetPath + fileName}' — is the Assets folder missing or not copied next to the app?");
        return animations[0];
    }

    /// <summary>
    /// Which clip a given <see cref="BramblekinState"/> plays while
    /// <paramref name="isMoving"/> is false — see the type's own doc for the
    /// four clips this maps onto. States like Collecting, Farming or Fishing
    /// cover both the walk there and the action itself, so <see cref="ClipFor"/>
    /// checks <paramref name="isMoving"/> first: actually translating across
    /// the ground always plays Walking, whatever job it's walking to do,
    /// and only a Bramblekin standing still doing that job plays its own
    /// clip — otherwise a Bramblekin fetching a twig, say, would play the
    /// Gathering clip's stationary reach-and-lift motion while visibly still
    /// walking toward it.
    /// </summary>
    public static BramblekinClip ClipFor(BramblekinState state, bool isMoving, bool guard = false)
    {
        // (A guard has no sword and shield in its mesh now, so it moves like anyone: the guard walk and idle clips are not used for the time being.)
        if (isMoving)
            return BramblekinClip.Walking;

        return state switch
        {
            BramblekinState.Fishing => BramblekinClip.Fishing,
            // (No fighting loop of its own: a blow plays its own clip when it is struck, see Bramblekin.BeginBlow; between blows it stands.)
            BramblekinState.Collecting or BramblekinState.Building or BramblekinState.Farming or
                BramblekinState.Foraging or BramblekinState.Stockpiling or
                BramblekinState.Raiding => BramblekinClip.Gathering,
            _ => BramblekinClip.Idle,
        };
    }

    /// <summary>The keyframe index <paramref name="timeSeconds"/> lands on within <paramref name="clip"/>, looping.</summary>
    public static int FrameAt(BramblekinClip clip, float timeSeconds)
    {
        ModelAnimation animation = _clips[clip];
        if (animation.KeyFrameCount <= 0)
            return 0;
        int frame = (int)(timeSeconds * ClipFps) % animation.KeyFrameCount;
        return frame < 0 ? frame + animation.KeyFrameCount : frame;
    }

    /// <summary>How long (s) <paramref name="clip"/> takes at its own pace.</summary>
    public static float NaturalSeconds(BramblekinClip clip)
    {
        if (!_ready && !Raylib.IsWindowReady())
            return 1.2f; // (a headless run has no models: a blow takes about this long)
        EnsureLoaded(); // the simulation asks before anything is drawn
        return _clips[clip].KeyFrameCount / ClipFps;
    }

    /// <summary>The longest an action clip (a blow, a stab, bending to pick something up) is let run (s): a longer one is played faster so that it still ends within this.</summary>
    private static readonly Dictionary<BramblekinClip, float> ActionCap = new()
    {
        [BramblekinClip.Combat] = 0.8f,
        [BramblekinClip.SwordAttack] = 0.7f,
        [BramblekinClip.SpearStab] = 1.2f,
        [BramblekinClip.AimRecoil] = 0.6f,
        [BramblekinClip.PickingUp] = 1.6f,
    };

    /// <summary>How long (s) an action clip plays for: its own length, or the cap if that is shorter (it is then sped up).</summary>
    public static float ActionSeconds(BramblekinClip clip) =>
        ActionCap.TryGetValue(clip, out float cap) ? MathF.Min(cap, NaturalSeconds(clip)) : NaturalSeconds(clip);

    /// <summary>Poses <paramref name="instance"/> a share <paramref name="progress"/> (0 to 1) of the way through <paramref name="clip"/>.</summary>
    public static void PlayProgress(ref Model instance, BramblekinClip clip, float progress) =>
        Raylib.UpdateModelAnimation(instance, _clips[clip], Math.Clamp((int)(progress * _clips[clip].KeyFrameCount), 0, Math.Max(0, _clips[clip].KeyFrameCount - 1)));

    public static void Play(ref Model instance, BramblekinClip clip, float timeSeconds) =>
        Raylib.UpdateModelAnimation(instance, _clips[clip], FrameAt(clip, timeSeconds));

    /// <summary>
    /// A cheap per-Bramblekin clone: same Meshes/Materials/Skeleton pointers
    /// as the shared model for its sex, <see cref="_baseModel"/> or <see cref="_femaleModel"/> (nothing is re-uploaded to the
    /// GPU), but its own <see cref="Model.BoneMatrices"/>/<see cref="Model.CurrentPose"/>
    /// buffers, so <see cref="Raylib.UpdateModelAnimation"/> can pose it
    /// independently of every other Bramblekin sharing the same mesh.
    /// Release with <see cref="DestroyPoseInstance"/> — never
    /// <see cref="Raylib.UnloadModel"/>, which would free the shared mesh out
    /// from under every other Bramblekin.
    /// </summary>
    public static Model CreatePoseInstance(Sex sex)
    {
        EnsureLoaded();
        Model instance = sex == Sex.Female ? _femaleModel : _baseModel;
        int boneCount = instance.Skeleton.BoneCount;
        instance.BoneMatrices = (Matrix4x4*)NativeMemory.AllocZeroed((nuint)boneCount, (nuint)sizeof(Matrix4x4));
        instance.CurrentPose = (Transform*)NativeMemory.AllocZeroed((nuint)boneCount, (nuint)sizeof(Transform));
        return instance;
    }

    /// <summary>How many levels of detail each sex has: 0 the full mesh (~50,000 triangles), 1 ~9,000, 2 ~2,500 (Tools/convert_kin_lod.py).</summary>
    public const int Lods = 3;

    /// <summary>
    /// <paramref name="instance"/> drawn with the level-of-detail <paramref name="lod"/> mesh of its sex: its own pose
    /// (bone matrices), but a cheaper mesh and smaller texture. Every level shares the one skeleton (they are the same
    /// glb with the mesh swapped), so the same pose drives any of them.
    /// </summary>
    public static Model LodView(in Model instance, Sex sex, int lod, bool guard = false)
    {
        Model source = _lods[sex == Sex.Female ? 1 : 0, Math.Clamp(lod, 0, Lods - 1)];
        Model view = instance;
        view.MeshCount = source.MeshCount;
        view.MaterialCount = source.MaterialCount;
        view.Meshes = source.Meshes;
        view.Materials = source.Materials;
        view.MeshMaterial = source.MeshMaterial;
        return view;
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

    private static readonly Dictionary<string, int> _boneIndex = new();

    /// <summary>The index of the bone called <paramref name="name"/> ("mixamorig:RightHand") in the skeleton, or -1.</summary>
    public static int BoneIndex(in Model pose, string name)
    {
        if (_boneIndex.TryGetValue(name, out int cached))
            return cached;
        int found = -1;
        for (int i = 0; i < pose.Skeleton.BoneCount && found < 0; i++)
        {
            if ((System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)pose.Skeleton.Bones[i].Name) ?? "") == name)
                found = i;
        }
        _boneIndex[name] = found;
        return found;
    }

    /// <summary>The skeleton's bones are set out in units about 2.92 tall where the mesh is 1 tall: its poses are this many metres per bone unit.</summary>
    private const float SkeletonScale = 0.3425f;

    /// <summary>The world matrix of bone <paramref name="index"/> in the current pose, in the mesh's own units (row-vector order): its frame, with its place worked up through its parents.</summary>
    public static Matrix4x4 BoneWorld(in Model pose, int index)
    {
        Matrix4x4 world = Chain(pose, index);
        world.Translation *= SkeletonScale;
        return world;
    }

    private static Matrix4x4 Chain(in Model pose, int index)
    {
        Transform t = pose.CurrentPose[index];
        Matrix4x4 local = Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Translation);
        int parent = pose.Skeleton.Bones[index].Parent;
        return parent >= 0 ? local * Chain(pose, parent) : local;
    }
}
