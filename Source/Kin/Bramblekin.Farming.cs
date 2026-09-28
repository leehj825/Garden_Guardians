using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>Seconds it takes to plant a bush once at the spot.</summary>
    private const float PlantingSeconds = 3f;

    /// <summary>A Farmer tends its group's bushes within this many meters.</summary>
    private const float FarmRange = 25f;

    private (Vector3 Spot, CropKind Kind)? _plantPlan;
    private float _plantTimer;

    /// <summary>The crafts it knows — worked out by a clever group, then taught to every member, passed on to children, and carried along wherever it goes (see <see cref="Craft"/>).</summary>
    public Craft Crafts { get; private set; }

    public bool Knows(Craft craft) => (Crafts & craft) == craft;

    /// <summary>Learns <paramref name="crafts"/> (any number at once).</summary>
    public void Learn(Craft crafts) => Crafts |= crafts;

    /// <summary>Whether it knows how to grow crops from seed (berry bushes first — see <see cref="Craft"/>).</summary>
    public bool KnowsFarming => Knows(Craft.Farming);

    public void LearnFarming() => Learn(Craft.Farming);

    /// <summary>
    /// Farmer: picks what's ripe off the group's crops and carries it to the
    /// stores; with nothing ripe, plants a new crop near home — whichever
    /// kind the clan has fewest of (see <see cref="World.ChooseCrop"/>) —
    /// while the group has room for one (and food to spare as seed);
    /// otherwise gathers like a Gatherer.
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

        if (world.NearestRipeCrop(this, group, FarmRange) is { } bush)
        {
            Harvest(bush, deltaTime, world, eat: false);
            return true;
        }

        if (world.WantsToPlant(group))
        {
            if (_plantPlan is null)
            {
                CropKind kind = world.ChooseCrop(group, home);
                if (world.FindPlantingSpot(home, kind) is { } found)
                    _plantPlan = (found, kind);
            }
            if (_plantPlan is var (spot, cropKind))
            {
                SetState(BramblekinState.Farming);
                if (GroundMover.HorizontalDistance(Position, spot) > 0.6f)
                {
                    _plantTimer = 0f;
                    MoveTo(spot, WalkSpeed, deltaTime, world);
                    return true;
                }

                _plantTimer += deltaTime * WorkPace;
                if (_plantTimer >= PlantingSeconds)
                {
                    if (world.PlantCrop(this, group, spot, cropKind) is not null)
                        Train(Skill.Farming, world, 2f);
                    _plantPlan = null;
                    _plantTimer = 0f;
                }
                return true;
            }
        }

        _plantPlan = null;
        return DoGatherDuty(deltaTime, world);
    }

    /// <summary>Walks to <paramref name="bush"/> and picks what's ripe — to eat on the spot if <paramref name="eat"/>, else to carry home.</summary>
    private void Harvest(Crop bush, float deltaTime, World world, bool eat)
    {
        SetState(BramblekinState.Farming);
        float reach = Crop.Radius + PickupDistance + 0.2f;
        if (GroundMover.HorizontalDistanceSquared(Position, bush.Position) > reach * reach)
        {
            MoveTo(bush.Position, WalkSpeed * (IsStarving ? 1.25f : 1f), deltaTime, world);
            return;
        }

        if (world.PickFruit(bush) is not { } fruit)
            return;
        _carried = fruit;
        if (eat)
        {
            StartEating();
            return;
        }
        Train(Skill.Farming, world, 0.3f); // Picking is the everyday part of farming; planting teaches more.
        // A skilled hand coaxes more out of a crop: now and then a second piece, straight into the store.
        if (_rng.NextDouble() < SkilledHarvestChance * SkillAt(Skill.Farming) && StoreToStock(world) is { } store)
            world.DepositBonusHarvest(store, bush);
    }

    /// <summary>At full farming skill, a picking yields a second piece this often.</summary>
    private const float SkilledHarvestChance = 0.35f;
}
