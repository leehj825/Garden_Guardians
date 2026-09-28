using System.Numerics;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>Cooked food fills this much more than raw…</summary>
    public const float CookedNourishmentBonus = 12f;

    /// <summary>…and heals this much more.</summary>
    public const int CookedHealingBonus = 3;

    /// <summary>Wintering in beside a lit hearth, Hunger rises at this fraction of the usual rate (instead of <see cref="Bramblekin.WinterShelterMetabolism"/>).</summary>
    public const float HearthWinterMetabolism = 0.35f;

    /// <summary>Resting beside a lit hearth heals this many times as fast.</summary>
    public const float HearthRestHealFactor = 1.3f;

    /// <summary>Meals eaten cooked, at a lit hearth.</summary>
    public int CookedMeals { get; private set; }

    /// <summary>Twigs put on hearth fires.</summary>
    public int TwigsBurned { get; private set; }

    /// <summary>Every hearth burns down as it goes.</summary>
    private void UpdateHearths(float deltaTime)
    {
        foreach (Shelter shelter in Shelters)
        {
            if (shelter.HearthFuel > 0f)
                shelter.HearthFuel = MathF.Max(0f, shelter.HearthFuel - deltaTime);
        }
    }

    /// <summary>A twig carried to <paramref name="home"/>'s hearth goes on the fire.</summary>
    public void FuelHearth(Shelter home, Twig twig)
    {
        twig.Deactivate();
        if (!home.HasHearth)
            return;
        home.AddFuel();
        TwigsBurned++;
    }

    public void NoteCookedMeal() => CookedMeals++;

    /// <summary>The nearest of <paramref name="group"/>'s homes to <paramref name="from"/> whose hearth wants another twig, if any.</summary>
    public Shelter? HearthToFeed(KinGroup group, Vector3 from)
    {
        Shelter? best = null;
        float bestDistanceSquared = float.MaxValue;
        foreach (Shelter home in GroupHomes(group))
        {
            if (!home.NeedsFuel)
                continue;
            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, home.Position);
            if (distanceSquared < bestDistanceSquared)
            {
                best = home;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    private bool AnyHearthNeedsFuel(KinGroup group)
    {
        foreach (Shelter home in GroupHomes(group))
        {
            if (home.NeedsFuel)
                return true;
        }
        return false;
    }
}
