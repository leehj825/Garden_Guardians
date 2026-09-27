using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>Seconds it takes to plant a bush once at the spot.</summary>
    private const float PlantingSeconds = 3f;

    /// <summary>A Farmer tends its group's bushes within this many meters.</summary>
    private const float FarmRange = 25f;

    private Vector3? _plantSpot;
    private float _plantTimer;

    /// <summary>The crafts it knows — worked out by a clever group, then taught to every member, passed on to children, and carried along wherever it goes (see <see cref="Craft"/>).</summary>
    public Craft Crafts { get; private set; }

    public bool Knows(Craft craft) => (Crafts & craft) == craft;

    /// <summary>Learns <paramref name="crafts"/> (any number at once).</summary>
    public void Learn(Craft crafts) => Crafts |= crafts;

    /// <summary>Whether it knows how to grow berry bushes from seed.</summary>
    public bool KnowsFarming => Knows(Craft.Farming);

    public void LearnFarming() => Learn(Craft.Farming);

    /// <summary>
    /// Farmer: picks ripe berries off the group's bushes and carries them
    /// to the stores; with none ripe, plants a new bush near home while the
    /// group has room for one (and a berry to spare as seed); otherwise
    /// gathers like a Gatherer.
    /// </summary>
    private bool DoFarmDuty(KinGroup group, float deltaTime, World world)
    {
        if (Home is not { IsBuilt: true } home)
            return false;

        if (_carried is not null)
        {
            if (StoreToStock(world) is not { } store)
                return false;
            CarryFoodHome(store, deltaTime, world);
            return true;
        }

        if (world.NearestRipeBush(this, group, FarmRange) is { } bush)
        {
            Harvest(bush, deltaTime, world, eat: false);
            return true;
        }

        if (world.WantsToPlant(group))
        {
            _plantSpot ??= world.FindPlantingSpot(home);
            if (_plantSpot is { } spot)
            {
                SetState(BramblekinState.Farming);
                if (GroundMover.HorizontalDistance(Position, spot) > 0.6f)
                {
                    _plantTimer = 0f;
                    MoveTo(spot, WalkSpeed, deltaTime, world);
                    return true;
                }

                _plantTimer += deltaTime;
                if (_plantTimer >= PlantingSeconds)
                {
                    world.PlantBush(this, group, spot);
                    _plantSpot = null;
                    _plantTimer = 0f;
                }
                return true;
            }
        }

        _plantSpot = null;
        return DoGatherDuty(deltaTime, world);
    }

    /// <summary>Walks to <paramref name="bush"/> and picks a ripe berry — to eat on the spot if <paramref name="eat"/>, else to carry home.</summary>
    private void Harvest(BerryBush bush, float deltaTime, World world, bool eat)
    {
        SetState(BramblekinState.Farming);
        float reach = BerryBush.Radius + PickupDistance + 0.2f;
        if (GroundMover.HorizontalDistanceSquared(Position, bush.Position) > reach * reach)
        {
            MoveTo(bush.Position, WalkSpeed * (IsStarving ? 1.25f : 1f), deltaTime, world);
            return;
        }

        if (world.PickFruit(bush) is not { } fruit)
            return;
        _carried = fruit;
        if (eat)
            StartEating();
    }
}
