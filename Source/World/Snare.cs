using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A grass-noose snare a clan that knows <see cref="Craft.Snares"/> sets
/// near its Houses, baited with a crumb: a Grub that smells it and crawls
/// in is caught and killed, leaving its meat on the spot for Gatherers to
/// bring home. A sprung snare waits for a Gatherer to set it again (see
/// World.Snares).
/// </summary>
public sealed class Snare
{
    /// <summary>A Grub this close (m) to a set snare is caught.</summary>
    public const float CatchRadius = 0.3f;

    /// <summary>Setting a sprung snare again takes this long (s).</summary>
    public const float ResetSeconds = 2.5f;

    private static readonly Color PegColor = new(115, 80, 45, 255);
    private static readonly Color NooseColor = new(170, 160, 90, 255);
    private static readonly Color BaitColor = new(210, 40, 45, 255);

    public Snare(Vector3 position, Guid? groupId, bool isSet = true)
    {
        Position = World.Grounded(position);
        GroupId = groupId;
        IsSet = isSet;
    }

    public Vector3 Position { get; }

    public Guid? GroupId { get; set; }

    /// <summary>Set and baited; false once sprung, until a Gatherer sets it again.</summary>
    public bool IsSet { get; set; }

    /// <summary>A bent twig peg holding a grass loop: taut and baited while set, lying slack once sprung.</summary>
    public void Draw(Color? clanColor)
    {
        Vector3 peg = Position + new Vector3(0.12f, 0f, 0f);
        Vector3 bend = peg + new Vector3(-0.04f, IsSet ? 0.32f : 0.1f, 0f);
        Raylib.DrawCylinderEx(peg, bend, 0.025f, 0.018f, 5, PegColor);
        if (IsSet)
        {
            Raylib.DrawLine3D(bend, Position + new Vector3(0f, 0.08f, 0f), NooseColor);
            Raylib.DrawCircle3D(Position + new Vector3(0f, 0.04f, 0f), 0.12f, new Vector3(1, 0, 0), 90f, NooseColor);
            Raylib.DrawSphere(Position + new Vector3(0f, 0.03f, 0f), 0.035f, BaitColor);
        }
        else
        {
            Raylib.DrawCircle3D(Position + new Vector3(0f, 0.01f, 0f), 0.08f, new Vector3(1, 0, 0), 90f, NooseColor);
        }
        if (clanColor is { } color)
            Raylib.DrawCube(bend + new Vector3(0.03f, 0f, 0f), 0.05f, 0.04f, 0.01f, color);
    }
}
