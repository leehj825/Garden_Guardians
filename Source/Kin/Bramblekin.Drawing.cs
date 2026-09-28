using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    // --- Drawing ---------------------------------------------------------------------------

    /// <summary>
    /// A bark-brown body tinted redder the more Aggressive it is (alarm red
    /// while fleeing), topped with a head in its group's colour (off-white
    /// while solitary). A Leader carries its group's banner; anything
    /// fighting, robbing or hunting holds a thorn out front; carried Food
    /// rides on its head. Elders go grey.
    /// </summary>
    public void Draw(World world)
    {
        KinGroup? group = world.GroupOf(this);
        Color color = State == BramblekinState.Fleeing
            ? PanicColor
            : LerpColor(CalmColor, AggressiveColor, Personality.Aggression);
        if (IsElder)
            color = LerpColor(color, ElderColor, ElderGreying);

        // A small, dark, semi-transparent drop shadow at this unit's own X/Z
        // on the ground, drawn before the body itself — a flat disc laid on
        // the XZ plane at a tiny epsilon above the terrain to avoid
        // z-fighting with it.
        var shadowCenter = new Vector3(Position.X, Position.Y + 0.02f, Position.Z);
        Raylib.DrawCircle3D(shadowCenter, BodyRadius * 1.3f, new Vector3(1, 0, 0), 90f, new Color(0, 0, 0, 90));

        // Cached-Model body: a cylinder tilted to the terrain's own surface
        // normal. GenMeshCylinder's mesh runs from local y=0 (base) to
        // y=BodyHeight (top), so it pivots flush on the ground at Position.
        EnsureBodyModel();
        Vector3 normal = World.GetNormalAt(Position.X, Position.Z);
        Vector3 axis = Vector3.Cross(Vector3.UnitY, normal);
        float angleDegrees = 0f;
        if (axis.LengthSquared() > 1e-6f)
            angleDegrees = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitY, normal), -1f, 1f)) * (180f / MathF.PI);
        else
            axis = Vector3.UnitY; // Flat ground: any axis is fine at a 0-degree rotation.
        float scale = BodyScale;
        Raylib.DrawModelEx(_bodyModel, Position, axis, angleDegrees, new Vector3(scale), color);

        var top = Position + new Vector3(0, (BodyHeight - BodyRadius) * scale, 0);
        Detail.Sphere(top + new Vector3(0, BodyRadius * 0.5f, 0), BodyRadius * 0.35f, group?.Color ?? SolitaryHeadColor);
        DrawSickness(top);

        Vector2 facing = _mover.Heading.LengthSquared() > 1e-6f ? _mover.Heading : Vector2.UnitX;

        if (group is not null && group.Leader == this)
        {
            var poleBase = Position + new Vector3(0, BodyHeight, 0);
            var poleTop = poleBase + new Vector3(0, 0.4f, 0);
            Raylib.DrawLine3D(poleBase, poleTop, BannerPoleColor);
            var flagCenter = poleTop + new Vector3(-facing.X * 0.12f, -0.07f, -facing.Y * 0.12f);
            Raylib.DrawCube(flagCenter, 0.2f, 0.14f, 0.02f, group.Color);
        }

        if (State is BramblekinState.Fighting or BramblekinState.Dueling or BramblekinState.Guarding or BramblekinState.Raiding &&
            !IsYoung && Knows(Craft.Shields))
            DrawShield(facing, group);

        if (State is BramblekinState.Fighting or BramblekinState.Attacking or BramblekinState.Hunting or BramblekinState.Dueling)
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

        _carried?.Draw(Position + new Vector3(0, BodyHeight, 0));
        DrawSack(facing);
        if (_carriedTwig is not null)
            Twig.DrawCarried(Position + new Vector3(0, BodyHeight * 0.55f, 0), facing);
        _carriedMaterial?.DrawCarried(Position, BodyHeight, facing);
        if (_carryingWater)
            DrawWaterCup(facing);
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

    private static readonly Color ShieldColor = new(95, 55, 35, 255);

    /// <summary>A round shield of glossy beetle shell on its arm, with a boss in its clan's colour.</summary>
    private void DrawShield(Vector2 facing, KinGroup? group)
    {
        var side = new Vector3(-facing.Y, 0f, facing.X);
        Vector3 at = Position + new Vector3(0f, BodyHeight * 0.5f, 0f) + side * 0.22f + new Vector3(facing.X, 0f, facing.Y) * 0.08f;
        Raylib.DrawCylinderEx(at, at + side * 0.04f, 0.17f, 0.17f, 10, ShieldColor);
        Detail.Sphere(at + side * 0.05f, 0.05f, group?.Color ?? ShieldColor);
    }
}
