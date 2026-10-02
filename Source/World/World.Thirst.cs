using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Water to drink ------------------------------------------------------------------------

    /// <summary>In a storm, every cistern fills by a sip this often (s).</summary>
    private const float RainFillInterval = 2f;

    private float _rainFillTimer = RainFillInterval;

    public int DeathsByThirst { get; private set; }

    /// <summary>Drinks taken at the pond, and sips from cisterns.</summary>
    public int DrinksAtPond { get; private set; }

    public int CisternDrinks { get; private set; }

    /// <summary>Cupfuls carried home from the pond to a cistern.</summary>
    public int CupfulsCarried { get; private set; }

    /// <summary>Metres walked to the pond, summed over every drink there — how far the garden's folk live from water.</summary>
    public float WaterTrekMeters { get; private set; }

    /// <summary>A Bramblekin drank its fill at the pond.</summary>
    /// <summary>Drinks taken at the creek (counted among <see cref="DrinksAtPond"/> too).</summary>
    public int CreekDrinks { get; private set; }

    public void NoteDrinkAtPond(Bramblekin kin)
    {
        DrinksAtPond++;
        if (WaterMap.IsNearCreek(kin.Position.X, kin.Position.Z, 2f))
            CreekDrinks++;
        if (kin.Home is { } home)
            WaterTrekMeters += MathF.Min(WaterMap.DistanceToWater(home.Position.X, home.Position.Z), 500f);
    }

    /// <summary>A sip from <paramref name="cistern"/>. False if it's dry.</summary>
    public bool DrinkFromCistern(Shelter cistern)
    {
        if (cistern.Water <= 0)
            return false;
        cistern.Water--;
        CisternDrinks++;
        return true;
    }

    /// <summary>A cupful from the pond poured into <paramref name="cistern"/>.</summary>
    public void PourWater(Shelter cistern, int sips)
    {
        cistern.Water = Math.Min(cistern.CisternCapacity, cistern.Water + sips);
        CupfulsCarried++;
    }

    /// <summary>The rain fills the cisterns.</summary>
    private void UpdateCisterns(float deltaTime)
    {
        if (!IsStorming)
            return;
        _rainFillTimer -= deltaTime;
        if (_rainFillTimer > 0f)
            return;
        _rainFillTimer = RainFillInterval;
        foreach (Shelter shelter in Shelters)
        {
            if (shelter is { HasCistern: true, IsCollapsed: false } && shelter.Water < shelter.CisternCapacity)
                shelter.Water++;
        }
    }
}
