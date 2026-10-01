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

        var shadowCenter = new Vector3(Position.X, Position.Y + 0.02f, Position.Z);
        if (!speck)
            Raylib.DrawCircle3D(shadowCenter, BodyRadius * 1.3f, new Vector3(1, 0, 0), 90f, new Color(0, 0, 0, 90));

        // The skinned rig, tilted to the terrain's own surface normal and
        // posed to whatever clip its current State plays (see
        // BramblekinModel.ClipFor). Its own local origin already sits at its
        // feet (baked in when it was rigged), so — like the cylinder it
        // replaced — it pivots flush on the ground at Position.
        EnsureAnimModel();
        bool guardLook = WearsGuardKit;
        BramblekinClip clip = BramblekinModel.ClipFor(State, _mover.IsMoving, guardLook);
        // Skinning is done on the CPU into the mesh being drawn, so it is done on the mesh of the level of detail in
        // use (a cheaper mesh skins faster too) — skinning the full one and drawing another would draw the other unposed.
        Model pose = BramblekinModel.LodView(_animModel, Sex, lod, guardLook);
        if (!speck)
            BramblekinModel.Play(ref pose, clip, clip == BramblekinClip.Idle ? 0f : _animTime);

        // The cylinder this replaced was rotationally symmetric, so it never
        // needed to face any particular way; the rig is not, so it must be
        // yawed to face Heading before the terrain tilt is applied — applied
        // as a single combined rotation (DrawModelEx only takes one
        // axis/angle) rather than two separate draws-with-rotation.
        Vector2 facing = _mover.Heading.LengthSquared() > 1e-6f ? _mover.Heading : Vector2.UnitX;
        float yawRadians = MathF.Atan2(facing.X, facing.Y) + BramblekinModel.ForwardYawOffset;
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


            if (State is BramblekinState.Fighting or BramblekinState.Attacking or BramblekinState.Hunting or BramblekinState.Dueling && Job is not (KinJob.Guard or KinJob.Raider))
            {
                Color thornColor = State == BramblekinState.Attacking ? BloodyThornColor : ThornColor;
                var grip = Position + new Vector3(0, BodyHeight * 0.6f, 0);
                var tip = grip + new Vector3(facing.X, 0.55f, facing.Y) * 0.6f;
                Raylib.DrawLine3D(grip, tip, thornColor);
                Detail.Sphere(tip, 0.025f, thornColor);
            }

            if (State == BramblekinState.Fishing && _fishingSpot is { } spot && GroundMover.HorizontalDistanceSquared(Position, spot) < 1f)
                DrawFishingRod(facing);
            if (State == BramblekinState.Healing)
                DrawPoultice(facing);
            if (State == BramblekinState.Sleeping)
                DrawSleep(world);
        }

        if (props) // what it carries is too small to see from far off
        {
            _carried?.Draw(Position + new Vector3(0, BodyHeight, 0));
            DrawSack(facing);
            if (_carriedTwig is not null)
                Twig.DrawCarried(Position + new Vector3(0, BodyHeight * 0.55f, 0), facing);
            _carriedMaterial?.DrawCarried(Position, BodyHeight, facing);
            if (_carryingWater)
                DrawWaterCup(facing);
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

    /// <summary>The sword and shield of a soldier or raider, a hunter's bow and quiver: hung on the bones of the pose (see <see cref="KinGear"/>), so they walk and swing with it.</summary>
    private void DrawGear(in Model pose, Vector3 axis, float angleDegrees, float scale)
    {
        bool hunter = Job == KinJob.Hunter;
        if (IsYoung || !hunter)
            return; // A guard's sword and shield are part of its own (guard) model.
        Matrix4x4 body = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), angleDegrees * MathF.PI / 180f) * Matrix4x4.CreateTranslation(Position);
        KinGear.Draw(pose, body, sword: false, shield: false, bow: hunter, quiver: hunter);
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
