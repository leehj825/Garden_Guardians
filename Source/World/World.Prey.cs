using System.Numerics;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>Small game a Bramblekin could hunt within <paramref name="radius"/>: the nearest Grub.</summary>
    public ICombatant? NearestPrey(Vector3 from, float radius) => NearestLiveGrub(from, radius);

    /// <summary>True if <paramref name="spot"/> lies within <paramref name="margin"/> of any standing home's edge.</summary>
    public bool IsNearHome(Vector3 spot, float margin) =>
        Shelters.Any(s => !s.IsCollapsed && GroundMover.HorizontalDistance(s.Position, spot) < s.Radius + margin);
}
