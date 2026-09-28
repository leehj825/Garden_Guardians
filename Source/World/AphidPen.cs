using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A clan's aphid pen (see <see cref="Craft.Herding"/> and World.Herding):
/// a ring of grass stems by its main home, holding a little herd of fat
/// green aphids that graze and give honeydew — a sweet drop of food that
/// keeps far longer than a berry. The herd breeds up from spring to autumn;
/// ants and the Wolf Spider pick them off, and raiders drive them away.
/// </summary>
public sealed class AphidPen
{
    public const float Radius = 1.3f;

    /// <summary>A pen holds at most this many aphids.</summary>
    public const int MaxAphids = 5;

    private static readonly Color StemColor = new(120, 150, 60, 255);
    private static readonly Color AphidColor = new(150, 210, 90, 255);
    private static readonly Color AphidEyeColor = new(40, 40, 30, 255);

    public AphidPen(Vector3 position, Guid? groupId, int aphids)
    {
        Position = World.Grounded(position);
        GroupId = groupId;
        Aphids = aphids;
    }

    public Vector3 Position { get; }

    /// <summary>The clan it belongs to; null once that clan is gone (the herd then drifts away).</summary>
    public Guid? GroupId { get; set; }

    public int Aphids { get; set; }

    /// <summary>Seconds until the next drop of honeydew (per herd, faster the bigger it is).</summary>
    public float HoneydewTimer { get; set; }

    /// <summary>Seconds until the herd next grows by one.</summary>
    public float BreedTimer { get; set; }

    /// <summary>Seconds until a hungry predator can take another.</summary>
    public float PreyCooldown { get; set; }

    public bool Contains(Vector3 point) => GroundMover.HorizontalDistanceSquared(point, Position) <= Radius * Radius;

    /// <summary>A ring of bent grass stems, and its aphids ambling about inside — worked out from the time, not the simulation's dice.</summary>
    public void Draw(float time, Color? clanColor)
    {
        const int stems = 12;
        for (int i = 0; i < stems; i++)
        {
            float angle = i * MathF.Tau / stems;
            Vector3 foot = World.Grounded(Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * Radius);
            Vector3 tip = foot + new Vector3(MathF.Cos(angle) * 0.08f, 0.45f, MathF.Sin(angle) * 0.08f);
            Raylib.DrawCylinderEx(foot, tip, 0.03f, 0.01f, 4, i == 0 && clanColor is { } flag ? flag : StemColor);
        }

        for (int i = 0; i < Aphids; i++)
        {
            float phase = i * 2.1f + Position.X;
            float r = Radius * (0.25f + 0.45f * (0.5f + 0.5f * MathF.Sin(phase * 1.7f)));
            float a = phase + time * (0.12f + 0.03f * i);
            Vector3 at = World.Grounded(Position + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r), 0.1f);
            var ahead = new Vector3(-MathF.Sin(a), 0f, MathF.Cos(a));
            Detail.Sphere(at, 0.11f, AphidColor, Position);
            Detail.Sphere(at + ahead * 0.1f + new Vector3(0f, 0.02f, 0f), 0.06f, AphidColor, Position);
            Detail.Sphere(at + ahead * 0.15f + new Vector3(0f, 0.04f, 0f), 0.018f, AphidEyeColor, Position);
        }
    }
}
