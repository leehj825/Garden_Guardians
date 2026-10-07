using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    // --- Drawing ---------------------------------------------------------------------------

    /// <summary>
    /// The rig's own painted texture, tinted redder the more Aggressive it
    /// is (alarm red while fleeing) and grey for an Elder — all blended from
    /// White rather than from a flat body colour, since <see cref="Raylib.DrawModelEx"/>
    /// multiplies this tint into the texture: tinting from anything darker
    /// than White (as the old, untextured cylinder body needed to) muddies
    /// the texture's own colours instead of just shading them. A Leader
    /// carries its group's banner; anything fighting, robbing or hunting
    /// holds a thorn out front; carried Food rides on its head.
    /// </summary>
    public void Draw(World world)
    {
        if (IsPlayerControlled && Job == KinJob.Hunter && !IsYoung)
            DrawAimMarker(world);

        // Climbing the ant hill's mound for the eggs: drawn up the slope, from where it stands at the foot.
        if (ClimbT > 0f && world.Anthill is { } hill)
        {
            Vector3 lift = hill.ClimbOffset(ClimbT);
            Rlgl.PushMatrix();
            Rlgl.Translatef(lift.X, lift.Y, lift.Z);
            DrawBody(world);
            Rlgl.PopMatrix();
            return;
        }
        if (_jumpHeight > 0f)
        {
            Rlgl.PushMatrix();
            Rlgl.Translatef(0f, _jumpHeight, 0f); // (the body is lifted; its shadow is put back on the ground in DrawBody)
            DrawBody(world);
            Rlgl.PopMatrix();
            return;
        }
        DrawBody(world);
    }

    private void DrawBody(World world)
    {
        KinGroup? group = world.GroupOf(this);
        Color color = State == BramblekinState.Fleeing
            ? LerpColor(Color.White, PanicColor, 0.6f)
            : LerpColor(Color.White, AggressiveColor, Personality.Aggression * 0.5f);
        if (IsElder)
            color = LerpColor(color, ElderColor, ElderGreying * 0.5f);

        // A small, dark, semi-transparent drop shadow at this unit's own X/Z
        // on the ground, drawn before the body itself — a flat disc laid on
        // the XZ plane at a tiny epsilon above the terrain to avoid
        // z-fighting with it.
        // Level of detail: by how tall it looks on screen right now. Up close
        // the full model; from a little way off the ~2,000-triangle one; and
        // zoomed right out (hundreds of them in view) just a coarse peg in
        // the same tint, with the small props skipped.
        float onScreen = Detail.Pixels(Position, BodyHeight * BodyScale);
        bool speck = onScreen < SpeckPixels;
        int lod = onScreen >= FinePixels ? 0 : onScreen >= MidPixels ? 1 : 2;
        bool props = onScreen >= PropPixels;

        var shadowCenter = new Vector3(Position.X, Position.Y + 0.02f - _jumpHeight, Position.Z);
        if (!speck)
            Raylib.DrawCircle3D(shadowCenter, BodyRadius * 1.3f, new Vector3(1, 0, 0), 90f, new Color(0, 0, 0, 90));

        // The skinned rig, tilted to the terrain's own surface normal and
        // posed to whatever clip its current State plays (see
        // BramblekinModel.ClipFor). Its own local origin already sits at its
        // feet (baked in when it was rigged), so — like the cylinder it
        // replaced — it pivots flush on the ground at Position.
        EnsureAnimModel();
        (BramblekinClip clip, float? progress) = ChooseClip(world);
        // Skinning is done on the CPU into the mesh being drawn, so it is done on the mesh of the level of detail in
        // use (a cheaper mesh skins faster too) — skinning the full one and drawing another would draw the other unposed.
        Model pose = BramblekinModel.LodView(_animModel, Sex, lod);
        if (!speck)
        {
            if (progress is { } share)
                BramblekinModel.PlayProgress(ref pose, clip, share);
            else
                BramblekinModel.Play(ref pose, clip, clip is BramblekinClip.SwordsmanIdle ? 0f : _animTime);
        }

        // The cylinder this replaced was rotationally symmetric, so it never
        // needed to face any particular way; the rig is not, so it must be
        // yawed to face Heading before the terrain tilt is applied — applied
        // as a single combined rotation (DrawModelEx only takes one
        // axis/angle) rather than two separate draws-with-rotation.
        Vector2 facing = _mover.Heading.LengthSquared() > 1e-6f ? _mover.Heading : Vector2.UnitX;
        float yawRadians = MathF.Atan2(facing.X, facing.Y) + BramblekinModel.ForwardYawOffset;
        // The whole body turns while the aim or stab clip plays, so that the bow arm (held out to the kin's left by the clip) or the spear point goes the
        // way the kin faces: eased in over the start of the clip and out again over its end, so that it turns round and back rather than snapping.
        if (clip is BramblekinClip.AimRecoil or BramblekinClip.SpearStab)
            yawRadians -= (clip == BramblekinClip.AimRecoil ? AimBodyTurn : StabBodyTurn) * TurnBlend(progress ?? 0.5f);
        Quaternion yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawRadians);

        Vector3 normal = World.GetNormalAt(Position.X, Position.Z);
        Vector3 tiltAxis = Vector3.Cross(Vector3.UnitY, normal);
        Quaternion tilt = Quaternion.Identity;
        if (tiltAxis.LengthSquared() > 1e-6f)
        {
            // A steep bank must not tip a kin over: lean with the slope, but only so far (and hardly at all while fishing).
            float tiltRadians = MathF.Min(MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitY, normal), -1f, 1f)), State == BramblekinState.Fishing ? 0.08f : 0.3f);
            tilt = Quaternion.CreateFromAxisAngle(Vector3.Normalize(tiltAxis), tiltRadians);
        }

        Quaternion rotation = Quaternion.Normalize(tilt * yaw);
        float angleDegrees = 2f * MathF.Acos(Math.Clamp(rotation.W, -1f, 1f)) * (180f / MathF.PI);
        float sinHalf = MathF.Sqrt(Math.Max(0f, 1f - rotation.W * rotation.W));
        Vector3 axis = sinHalf > 1e-6f ? new Vector3(rotation.X, rotation.Y, rotation.Z) / sinHalf : Vector3.UnitY;

        float scale = BodyScale * (BodyHeight / BramblekinModel.RawHeightUnits);
        if (speck)
        {
            // Grown so it never drops under a few pixels: zoomed right out, a
            // true-size Bramblekin would vanish and the colony would be unfindable.
            float grow = Math.Clamp(SpeckMinPixels / Math.Max(onScreen, 0.01f), 1f, 8f);
            Color peg = LerpColor(color, new Color(110, 90, 40, 255), 0.6f);
            Raylib.DrawCylinderEx(Position, Position + new Vector3(0, BodyHeight * BodyScale * grow, 0), BodyRadius * 0.7f * BodyScale * grow, BodyRadius * 0.5f * BodyScale * grow, 4, peg);
        }
        else
            Raylib.DrawModelEx(pose, Position, axis, angleDegrees, new Vector3(scale), color);

        if (props && !speck)
            DrawGear(pose, axis, angleDegrees, scale);

        var top = Position + new Vector3(0, (BodyHeight - BodyRadius) * scale, 0);
        if (props)
            DrawSickness(top);

        if (group is not null && group.Leader == this)
        {
            var poleBase = Position + new Vector3(0, BodyHeight, 0);
            var poleTop = poleBase + new Vector3(0, 0.4f, 0);
            Raylib.DrawLine3D(poleBase, poleTop, BannerPoleColor);
            var flagCenter = poleTop + new Vector3(-facing.X * 0.12f, -0.07f, -facing.Y * 0.12f);
            Raylib.DrawCube(flagCenter, 0.2f, 0.14f, 0.02f, group.Color);
        }

        if (props)
        {
            if (State == BramblekinState.Fishing && _fishingSpot is { } spot && GroundMover.HorizontalDistanceSquared(Position, spot) < 1f)
                DrawFishingRod(facing);
            if (State == BramblekinState.Healing)
                DrawPoultice(facing);
            if (State == BramblekinState.Sleeping)
                DrawSleep(world);
        }

        if (props) // what it carries is too small to see from far off
        {
            if (_carriesEgg)
            {
                PropModels.Draw(PropModels.Prop.Larvae, Position + new Vector3(0f, BodyHeight + 0.02f, 0f), 0f, 0.4f, Color.White);
            }
            DrawSack(facing);
            _carriedMaterial?.DrawCarried(Position, BodyHeight, facing);
        }
    }

    /// <summary>Below this many pixels tall on screen, a Bramblekin is drawn as a bare peg.</summary>
    private const float SpeckPixels = 22f;

    /// <summary>The least height, in pixels, a peg is drawn at.</summary>
    private const float SpeckMinPixels = 7f;

    /// <summary>From this many pixels tall, the full-detail model (~50,000 triangles); from <see cref="MidPixels"/> the ~9,000-triangle one; below that the ~2,500-triangle one.</summary>
    private const float FinePixels = 150f, MidPixels = 90f;

    /// <summary>From this many pixels tall, the small props (thorn, shield, rod, poultice…) are drawn.</summary>
    private const float PropPixels = 40f;

    // --- Which clip, and how far through it ------------------------------------------------------

    /// <summary>A debugging aid: GARDEN_CLIP=SpearStab (any <see cref="BramblekinClip"/>) plays that clip on every kin, over and over.</summary>
    private static readonly BramblekinClip? ForcedClip = Enum.TryParse(Environment.GetEnvironmentVariable("GARDEN_CLIP"), out BramblekinClip forced) ? forced : null;

    private BramblekinClip _actionClip;
    private float _actionStart;
    private bool _actionActive, _nextBlowIsAttack;

    /// <summary>Seconds left of an action the kin must finish before it moves or does anything else (a blow, a pick-up): see <see cref="StartAction"/>.</summary>
    private float _actionLock;

    /// <summary>Starts <paramref name="clip"/> playing from its first frame; with <paramref name="lockMovement"/> the kin stands where it is until the clip is done.</summary>
    private void StartAction(BramblekinClip clip, bool lockMovement)
    {
        _actionClip = clip;
        _actionStart = _animTime;
        _actionActive = true;
        _actionLock = lockMovement ? BramblekinModel.ActionSeconds(clip) : 0f;
    }

    /// <summary>
    /// A blow is struck: the clip for the kin's job plays through, and the kin stands still until it is done. A hunter (or anyone shooting) takes aim and
    /// recoils; a guard whose clan has Spears stabs; soldiers and other fighters alternate the sword and shield slash and the sword and shield attack.
    /// </summary>
    private void BeginBlow(World world, bool ranged = false)
    {
        BramblekinClip clip;
        if (ranged || (Job == KinJob.Hunter && !IsPlayerControlled)) // (a controlled Hunter's blow is a plain one: its bow is the Shoot button's)
            clip = BramblekinClip.AimRecoil;
        else if (Job == KinJob.Spearman || (Job == KinJob.Swordsman && world.GroupOf(this) is { } clan && World.Knows(clan, Craft.Spears)))
            clip = BramblekinClip.SpearStab;
        else
        {
            clip = _nextBlowIsAttack ? BramblekinClip.SwordAttack : BramblekinClip.Combat;
            _nextBlowIsAttack = !_nextBlowIsAttack;
        }
        StartAction(clip, lockMovement: true);
        if (clip != BramblekinClip.AimRecoil)
            Sfx.PlayNear(Sfx.Effect.SwordSpear, Position);
    }

    /// <summary>The action clip the kin's state and job call for while it stands still, if any: blows, stabs, aiming and picking things up. Null: nothing but the usual clips.</summary>
    private BramblekinClip? ActionWanted(World world)
    {
        switch (State)
        {
            // (Blows are started where they are struck: see BeginBlow.)
            case BramblekinState.Collecting or BramblekinState.Foraging or BramblekinState.Stockpiling or BramblekinState.Drinking:
                return BramblekinClip.PickingUp; // bending to pick up and to drink (eating does not: it would start the pick-up again and again for as long as the meal lasts)
            default:
                return null;
        }
    }

    /// <summary>
    /// The clip to pose the kin in, and how far through it (0 to 1) for an action clip, which always plays from its start to its end (sped up if it
    /// is long: see <see cref="BramblekinModel.ActionSeconds"/>) before anything else; null for the clips that just loop on the clock. Walking takes over
    /// from an action at once, since the kin is going somewhere.
    /// </summary>
    private (BramblekinClip Clip, float? Progress) ChooseClip(World world)
    {
        if (ForcedClip is { } forced)
        {
            float length = BramblekinModel.ActionSeconds(forced);
            return (forced, length <= 0f ? 0f : _animTime % length / length);
        }

        if (ClimbT > 0f && ClimbT < 1f && _climb != ClimbStage.Pick)
            return (BramblekinClip.Walking, null); // walking in to the eggs, or out with one

        if (_jumpClipPlaying)
            return (BramblekinClip.Jump, JumpProgress);

        if (_actionActive)
        {
            float length = BramblekinModel.ActionSeconds(_actionClip);
            float elapsed = _animTime - _actionStart;
            if (elapsed < length && (_actionLock > 0f || !_mover.IsMoving))
                return (_actionClip, elapsed / length);
            _actionActive = false;
        }

        if (!_mover.IsMoving && ActionWanted(world) is { } wanted)
        {
            StartAction(wanted, lockMovement: false);
            return (wanted, 0f);
        }
        if (_mover.IsMoving && IsRunning)
            return (BramblekinClip.Running, null);
        return (BramblekinModel.ClipFor(State, _mover.IsMoving), null);
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            (byte)255);
    }

    // --- What it carries ------------------------------------------------------------------------

    /// <summary>While the aim clip plays.</summary>
    private bool IsAiming => (_actionActive && _actionClip == BramblekinClip.AimRecoil) || ForcedClip == BramblekinClip.AimRecoil;

    /// <summary>In the aim clip the bow arm points this far (radians, about 49°: the shoulder-to-hand direction through the clip, 0.84 to 0.91) to the left of the way the body faces, measured from the clip's drawn pose.</summary>
    private const float AimBodyTurn = 0.85f;

    /// <summary>In the stab clip the spear's point, at the height of the thrust, is about 38° to the left of the way the body faces (measured from the hands in the clip).</summary>
    private const float StabBodyTurn = 1.25f;

    /// <summary>0 at the start and end of an action clip, 1 through the middle: the share of its body turn applied at that point (eased over a fifth of the clip at each end).</summary>
    private static float TurnBlend(float progress)
    {
        static float Smooth(float t) => t * t * (3f - 2f * t);
        return Smooth(Math.Clamp(progress / 0.2f, 0f, 1f)) * Smooth(Math.Clamp((1f - progress) / 0.2f, 0f, 1f));
    }

    /// <summary>The sword and shield of a soldier or raider, a hunter's bow and quiver: hung on the bones of the pose (see <see cref="KinGear"/>), so they walk and swing with it.</summary>
    private void DrawGear(in Model pose, Vector3 axis, float angleDegrees, float scale)
    {
        Matrix4x4 body = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), angleDegrees * MathF.PI / 180f) * Matrix4x4.CreateTranslation(Position);

        // The bite being eaten is held in the right hand, wherever the clip puts it (everything else it carries is in the pack).
        if (State == BramblekinState.Eating && _carried is { } bite && KinGear.Hand(pose, "mixamorig:RightHand", "mixamorig:RightForeArm", out Vector3 hand, out _))
            bite.Draw(Vector3.Transform(hand, body) - new Vector3(0f, FoodShard.Radius * 0.5f, 0f));

        bool hunter = Job == KinJob.Hunter, guard = Job == KinJob.Swordsman, spearman = Job == KinJob.Spearman;
        if (IsYoung || !(hunter || guard || spearman))
            return; // (A swordsman's wooden sword and shield, a spearman's spear and a hunter's bow for the time being: no quiver.)
        bool aiming = IsAiming;
        KinGear.Draw(pose, body, sword: guard, shield: guard, bow: hunter, quiver: false, spear: spearman, bowRaised: aiming, stabbing: (_actionActive && _actionClip == BramblekinClip.SpearStab) || ForcedClip == BramblekinClip.SpearStab);
    }

    private static readonly Color ShieldColor = new(95, 55, 35, 255);

    /// <summary>A round shield of glossy beetle shell held out in front of the arm, face forward — big enough to read from the camera — with a boss in its clan's colour.</summary>
    private void DrawShield(Vector2 facing, KinGroup? group)
    {
        float body = BodyHeight * BodyScale;
        var side = new Vector3(-facing.Y, 0f, facing.X);
        var forward = new Vector3(facing.X, 0f, facing.Y);
        float width = 0.55f * body;
        // Out beyond the arm and ahead of the chest, so the body does not hide it; face forward, like a soldier's.
        Vector3 at = Position + new Vector3(0f, body * 0.5f, 0f) + side * (0.42f * body) + forward * (0.3f * body);
        float yaw = MathF.Atan2(forward.X, forward.Z) * 180f / MathF.PI;
        VillageModels.Draw(VillageItem.Shield, at - new Vector3(0f, VillageModels.HeightAt(VillageItem.Shield, width) / 2f, 0f), yaw, width, Color.White);
        if (group?.Color is { } clan)
            Detail.Sphere(at + forward * (0.06f * body), 0.05f * body, clan);
    }
}
