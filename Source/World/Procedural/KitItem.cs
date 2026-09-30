namespace GardenGuardians;

public enum KitKind
{
    Oak,
    Rock,
    Plant,
}

/// <summary>One prop a procedural garden can hold: its model, size, and the circles walkers and builders keep out of (relative to its origin, at its base).</summary>
public sealed class KitItem
{
    /// <summary>The prop's model under Assets/Models/Procedural.</summary>
    public required string File { get; init; }

    public required KitKind Kind { get; init; }

    /// <summary>How tall it stands (m), and how far its footprint reaches from its origin (m).</summary>
    public required float Height { get; init; }
    public required float Reach { get; init; }

    /// <summary>Footprint circles (x, z, radius), flattened, relative to the prop's origin.</summary>
    public required float[] Circles { get; init; }

    // An oak only: where its trunk stands relative to its origin, the trunk's size, and the way (radians from +x towards +z) its hive can face.
    public float TrunkDx { get; init; }
    public float TrunkDz { get; init; }
    public float TrunkRadius { get; init; }
    public float TrunkHeight { get; init; }
    public float HiveAngle { get; init; }
    public float HiveSurface { get; init; }
}

/// <summary>A prop placed on a generated garden: which kit item, where, how it's turned (radians about the vertical, in the x-z plane) and scaled.</summary>
public sealed class PlacedProp
{
    public required KitItem Item { get; init; }
    public required float X { get; init; }
    public required float Z { get; init; }

    /// <summary>The ground height at its foot.</summary>
    public required float Base { get; init; }

    public required float Yaw { get; init; }
    public required float Scale { get; init; }

    /// <summary>Its footprint circles (x, z, radius) in the garden, flattened.</summary>
    public required float[] Circles { get; init; }
}
