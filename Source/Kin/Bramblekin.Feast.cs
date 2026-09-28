using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>At a feast, it mills about within this far (m) of the tables.</summary>
    private const float FeastMingleRadius = 2.6f;

    /// <summary>
    /// A harvest feast its clan is holding (or has been invited to), within
    /// reach (see <see cref="World.FeastFor"/>): it goes along, is fed from
    /// the host's stores on arrival if it's peckish, and mills about among
    /// the guests — which is where friendships, and couples between clans,
    /// are made (see World.TryCourt). Not for anyone on the night watch, a
    /// raid or an errand. Returns false when there's no feast for it.
    /// </summary>
    private bool UpdateFeast(float deltaTime, World world)
    {
        if (_errand is not null || Job == KinJob.Raider || world.FeastFor(this) is not { } feast ||
            world.GroupOf(this) is { } clan && (clan.NightWatch == this || clan.Goal == GroupGoal.Defend))
        {
            if (State == BramblekinState.Feasting)
                StartPause();
            return false;
        }

        if (!feast.Attended.Contains(ID))
        {
            if (GroundMover.HorizontalDistance(Position, feast.Site) > FeastMingleRadius)
            {
                SetState(BramblekinState.Feasting);
                MoveTo(feast.Site, WalkSpeed, deltaTime, world);
                return true;
            }
            if (Hunger > 25f && world.ArriveAtFeast(this, feast))
            {
                Hunger = MathF.Max(0f, Hunger - FoodNourishment);
                Heal(FoodHealing);
            }
            _wanderTarget = PointNear(feast.Site, FeastMingleRadius, world);
        }

        SetState(BramblekinState.Feasting);
        if (MoveTo(_wanderTarget, WalkSpeed * 0.5f, deltaTime, world))
            _wanderTarget = PointNear(feast.Site, FeastMingleRadius, world);
        return true;
    }
}
