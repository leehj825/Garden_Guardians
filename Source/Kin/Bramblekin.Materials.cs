using System.Numerics;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>Dragging a branch home goes at this fraction of its pace; a stone, a little less slowly.</summary>
    private const float BranchDragPace = 0.75f;

    private const float StoneCarryPace = 0.9f;

    /// <summary>The stone or branch it's carrying home.</summary>
    private Material? _carriedMaterial;

    /// <summary>The stone or branch it's walking to.</summary>
    private Material? _materialTarget;

    /// <summary>The loose stone or branch it's on its way to pick up (its claim — see <see cref="World.NearestMaterial"/>).</summary>
    public Material? FetchingMaterial => _materialTarget;

    /// <summary>
    /// A Builder with nothing to build: fetches a stone for a House's
    /// footing, or drags a branch home for a palisade — whichever its clan
    /// knows how to use, still needs, and can find within
    /// <see cref="World.MaterialSearchRadius"/>. False if there's none to
    /// fetch.
    /// </summary>
    private bool TryFetchMaterial(KinGroup group, float deltaTime, World world)
    {
        if (_carriedMaterial is { } carried)
        {
            if (world.HomeNeeding(group, carried.Kind, Position) is not { } home)
            {
                PutDownMaterial();
                return false;
            }
            SetState(BramblekinState.Building);
            float reach = home.Radius + 0.4f;
            if (GroundMover.HorizontalDistanceSquared(Position, home.Position) <= reach * reach)
            {
                _carriedMaterial = null;
                world.DeliverMaterial(this, home, carried);
                StartPause();
                return true;
            }
            MoveTo(home.Position, WalkSpeed * (carried.Kind == MaterialKind.Branch ? BranchDragPace : StoneCarryPace), deltaTime, world);
            return true;
        }

        if (_materialTarget is { IsActive: true, IsCarried: false } target && world.HomeNeeding(group, target.Kind, Position) is not null)
        {
            SetState(BramblekinState.Collecting);
            if (GroundMover.HorizontalDistance(Position, target.Position) <= PickupDistance + (target.Kind == MaterialKind.Branch ? 0.4f : 0f))
            {
                World.PickUpMaterial(target);
                _carriedMaterial = target;
                _materialTarget = null;
                return true;
            }
            MoveTo(target.Position, WalkSpeed, deltaTime, world);
            return true;
        }

        _materialTarget = null;
        foreach (MaterialKind kind in Enum.GetValues<MaterialKind>())
        {
            if (world.HomeNeeding(group, kind, Position) is not null &&
                world.NearestMaterial(Position, kind, World.MaterialSearchRadius, this) is { } found)
            {
                _materialTarget = found;
                found.ClaimedBy = this;
                return true;
            }
        }
        return false;
    }

    /// <summary>Sets down whatever stone or branch it's carrying, and gives up on any it was going for.</summary>
    private void PutDownMaterial()
    {
        if (_carriedMaterial is { } carried)
            World.DropMaterial(carried, Position);
        _carriedMaterial = null;
        if (_materialTarget is { } target && target.ClaimedBy == this)
            target.ClaimedBy = null;
        _materialTarget = null;
    }
}
