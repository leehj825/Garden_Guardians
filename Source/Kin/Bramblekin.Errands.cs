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
    /// carry it home to its own store. Returns false once there's nothing
    /// left to do (the errand ends).
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
        Raylib.DrawSphere(back, 0.1f + 0.02f * MathF.Min(SackLoad, 4), SackColor);
    }
}
