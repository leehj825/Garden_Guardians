using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    private static readonly Color SackColor = new(150, 115, 70, 255);

    private Errand? _errand;

    /// <summary>The trip it's making for its group, if any — see <see cref="Errand"/>.</summary>
    public Errand? Errand => _errand;

    /// <summary>Food in its errand sack.</summary>
    public int SackLoad => _errand?.Load ?? 0;

    public void GiveErrand(Errand errand) => _errand = errand;

    public void EndErrand() => _errand = null;

    /// <summary>Takes one piece of food out of its sack (a thief's grab). False if it's empty.</summary>
    public bool TakeFromSack()
    {
        if (_errand is not { Load: > 0 } errand)
            return false;
        errand.Load--;
        return true;
    }

    /// <summary>
    /// Duty, ahead of any job: an errand for its group. Aid: carry the sack
    /// to the ally's home and fill its store. Labour: fetch twigs for the
    /// ally's construction until the work is done, collect the pay, and
    /// carry it home to its own store. Haul: carry a stone or branch from
    /// near home to the ally's well or home (see <see cref="DoHaul"/>), and
    /// the pay home. Returns false once there's nothing left to do (the
    /// errand ends).
    /// </summary>
    private bool DoErrand(Errand errand, float deltaTime, World world)
    {
        if (!world.IsErrandValid(this, errand))
        {
            world.AbandonErrand(this, errand);
            return false;
        }

        if (errand.Returning)
        {
            if (Home is not { IsBuilt: true } home)
            {
                world.AbandonErrand(this, errand);
                return false;
            }
            SetState(BramblekinState.Traveling);
            if (!home.Contains(Position))
            {
                MoveTo(home.Position, WalkSpeed, deltaTime, world);
                return true;
            }
            world.UnpackErrand(this, errand);
            return true;
        }

        switch (errand.Kind)
        {
            case ErrandKind.Aid:
            case ErrandKind.Tribute:
                SetState(BramblekinState.Traveling);
                if (!errand.Destination.Contains(Position))
                {
                    MoveTo(errand.Destination.Position, WalkSpeed, deltaTime, world);
                    return true;
                }
                world.DeliverAid(this, errand);
                return true;

            case ErrandKind.Haul:
                DoHaul(errand, deltaTime, world);
                return true;

            default:
                if (errand.TwigsOwed > 0 && errand.Destination is { NeedsTwigs: true, IsCollapsed: false } site)
                {
                    DoBuildWork(site, deltaTime, world);
                    return true;
                }
                world.PayForLabour(this, errand);
                return true;
        }
    }

    /// <summary>A hauler looks this far (m) for the stone or branch it's promised.</summary>
    private const float HaulSearchRadius = 25f;

    /// <summary>
    /// A haul for its allies: picks up the nearest loose stone (or branch)
    /// and carries it to wherever they need it — their well being dug, or a
    /// home's footing or palisade — then collects its pay. If there's none
    /// to be found, or they don't need it any more, it goes home unpaid.
    /// </summary>
    private void DoHaul(Errand errand, float deltaTime, World world)
    {
        if (world.GroupWithId(errand.To) is not { } buyers)
            return; // Gone: IsErrandValid ends the errand next frame.

        if (_carriedMaterial is { } carried)
        {
            if (world.MaterialTarget(buyers, carried.Kind, Position) is not { } target)
            {
                PutDownMaterial();
                world.PayForHaul(this, errand, delivered: false);
                return;
            }
            SetState(BramblekinState.Traveling);
            if (GroundMover.HorizontalDistanceSquared(Position, target.Position) <= target.Reach * target.Reach)
            {
                _carriedMaterial = null;
                world.DeliverMaterial(this, target, carried);
                world.PayForHaul(this, errand, delivered: true);
                StartPause();
                return;
            }
            MoveTo(target.Position, WalkSpeed * (carried.Kind == MaterialKind.Branch ? BranchDragPace : StoneCarryPace), deltaTime, world);
            return;
        }

        if (_materialTarget is { IsActive: true, IsCarried: false } wanted && wanted.Kind == errand.Material)
        {
            SetState(BramblekinState.Collecting);
            if (GroundMover.HorizontalDistance(Position, wanted.Position) <= PickupDistance + (wanted.Kind == MaterialKind.Branch ? 0.4f : 0f))
            {
                World.PickUpMaterial(wanted);
                _carriedMaterial = wanted;
                _materialTarget = null;
                return;
            }
            MoveTo(wanted.Position, WalkSpeed, deltaTime, world);
            return;
        }

        PutDownMaterial();
        if (world.MaterialTarget(buyers, errand.Material, Position) is null ||
            world.NearestMaterial(Position, errand.Material, HaulSearchRadius, this) is not { } found)
        {
            world.PayForHaul(this, errand, delivered: false);
            return;
        }
        _materialTarget = found;
        found.ClaimedBy = this;
    }

    /// <summary>A helper's twig just went into the site it was hired for.</summary>
    private void NoteTwigDelivered(Shelter site)
    {
        if (_errand is { Kind: ErrandKind.Labour, Returning: false } errand && errand.Destination == site)
            errand.TwigsOwed--;
    }

    /// <summary>Its errand sack, slung on its back.</summary>
    private void DrawSack(Vector2 facing)
    {
        if (SackLoad <= 0)
            return;
        Vector3 back = Position + new Vector3(-facing.X * 0.2f, BodyHeight * 0.75f * BodyScale, -facing.Y * 0.2f);
        float width = 0.24f + 0.04f * MathF.Min(SackLoad, 4);
        float yaw = MathF.Atan2(-facing.X, -facing.Y) * 180f / MathF.PI;
        VillageModels.Draw(VillageItem.FoodSack, back - new Vector3(0f, VillageModels.HeightAt(VillageItem.FoodSack, width) * 0.6f, 0f), yaw, width, Color.White);
    }
}
