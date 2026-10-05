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

    /// <summary>
    /// A woven grass trap on a bent twig peg (Assets/Models/Props/Village/Fittings.glb): upright and baited while set, squashed flat once sprung,
    /// with its clan's flag on the peg.
    /// </summary>
    public void Draw(Color? clanColor)
    {
        float width = 0.5f / 0.6f; // (the model is 0.6 wide; a snare is about half a metre across)
        FittingModels.Draw(FittingModels.Kind.Snare, Position, 0f, new Vector3(width, IsSet ? width : width * 0.45f, width), Color.White);
        if (clanColor is { } color)
            Raylib.DrawCube(Position + new Vector3(0.09f, IsSet ? 0.4f : 0.2f, 0f), 0.05f, 0.04f, 0.01f, color);
    }
}
