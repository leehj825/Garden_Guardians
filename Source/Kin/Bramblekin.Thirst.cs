using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    // --- Thirst ------------------------------------------------------------------------

    public const float MaxThirst = 100f;

    /// <summary>Thirst gained per second — a drink lasts two and a half minutes before it's thirsty again.</summary>
    public const float ThirstPerSecond = 0.4f;

    /// <summary>At or above this, Thirst is Critical: it goes for a drink (before food, unless it's hungrier than it is thirsty).</summary>
    public const float ThirstyThreshold = 60f;

    /// <summary>Standing at the water (or the cistern) this long to drink.</summary>
    private const float DrinkSeconds = 2.5f;

    /// <summary>A sip from its home's cistern takes this much off its Thirst; the pond slakes it entirely.</summary>
    private const float CisternSip = 70f;

    /// <summary>At full Thirst, one point of damage every this many seconds until it drinks or dies.</summary>
    private const float DehydrationDamageInterval = 2f;

    /// <summary>Out in a storm it drinks the rain: Thirst falls this fast.</summary>
    private const float RainDrinkPerSecond = 2f;

    /// <summary>A cupful carried home from the pond fills this many sips of a cistern (see <see cref="Craft.Cisterns"/>).</summary>
    public const int CupfulSips = 1;

    /// <summary>New Bramblekin arrive with a random Thirst between 0 and this.</summary>
    private const float StartingThirstMax = 40f;

    private static readonly Color CupColor = new(140, 100, 60, 255);
    private static readonly Color CupWaterColor = new(80, 140, 210, 255);

    /// <summary>0 (just drunk) to <see cref="MaxThirst"/> (dying of thirst).</summary>
    public float Thirst { get; private set; }

    public bool IsThirsty => Thirst >= ThirstyThreshold;

    /// <summary>Where it's going for a drink: a stretch of shore…</summary>
    private Vector3? _waterSpot;

    /// <summary>…or its home's cistern…</summary>
    private Shelter? _drinkFrom;

    /// <summary>…or a well.</summary>
    private Well? _drinkWell;

    /// <summary>Drawing water up from a well takes a little longer than drinking at the pond.</summary>
    private const float WellDrinkSeconds = 3.5f;

    private float _drinkTimer;
    private float _dehydrationTimer;

    /// <summary>The water level (see <see cref="WaterMap.Generation"/>) its drink was planned at — the shore moves when the pond shrinks or fills.</summary>
    private int _drinkGeneration;

    /// <summary>Crouched at the water's edge, drinking — busy enough for the Wolf Spider to feel it (see <see cref="IsVibrating"/>).</summary>
    private bool IsDrinkingAtPond => State == BramblekinState.Drinking && _drinkTimer > 0f && _drinkFrom is null && _drinkWell is null;

    /// <summary>Summer heat dries it out faster; winter, slower.</summary>
    private static float SeasonThirst(Season season) => season switch
    {
        Season.Summer => 1.25f,
        Season.Winter => 0.75f,
        _ => 1f,
    };

    /// <summary>
    /// Thirst always rises (faster in summer, or sick) — except out in a
    /// storm, where it drinks the rain. At the very top it starts costing
    /// Health. Returns true if that killed it.
    /// </summary>
    private bool UpdateThirstMetabolism(float deltaTime, World world)
    {
        if (world.IsStorming && !IsSheltered)
            Thirst = MathF.Max(0f, Thirst - RainDrinkPerSecond * deltaTime);
        else
            Thirst = MathF.Min(MaxThirst, Thirst + ThirstPerSecond * SeasonThirst(world.CurrentSeason) * (IsSick ? SickHungerFactor : 1f) * (IsAsleep ? SleepMetabolism : 1f) * deltaTime);

        if (Thirst < MaxThirst)
        {
            _dehydrationTimer = 0f;
            return false;
        }
        _dehydrationTimer += deltaTime;
        if (_dehydrationTimer < DehydrationDamageInterval)
            return false;
        _dehydrationTimer -= DehydrationDamageInterval;
        TakeDamage(1, world, DeathCause.Thirst, source: null);
        return IsDead;
    }

    /// <summary>How much Thirst a mouthful of <paramref name="kind"/> takes off: watercress most, then fish, berries and mushrooms; seed, meat and acorns none.</summary>
    private static float Juiciness(FoodShardKind kind) => kind switch
    {
        FoodShardKind.Cress => 15f,
        FoodShardKind.Fish => 5f,
        FoodShardKind.Honeydew => 4f,
        FoodShardKind.Berry or FoodShardKind.Mushroom => 3f,
        _ => 0f,
    };

    private void QuenchWith(FoodShardKind kind) => Thirst = MathF.Max(0f, Thirst - Juiciness(kind));

    /// <summary>True when Thirst, not Hunger, should drive it now: thirsty, and no hungrier than it is thirsty (a drink already under way is finished unless it's starving and hungrier still).</summary>
    private bool ThirstComesFirst =>
        IsThirsty && (!IsHungry || Thirst >= Hunger || (State == BramblekinState.Drinking && !(IsStarving && Hunger > Thirst)));

    /// <summary>
    /// Critical need: a drink. From whichever is nearest: its home's cistern
    /// (if it has water), a well it may use (see World.NearestUsableWell), or
    /// the nearest stretch of shore — however far that is. At a well or the
    /// pond it drinks its fill, and a Bramblekin that knows
    /// <see cref="Craft.Cisterns"/> carries a cupful home afterwards.
    /// </summary>
    private void UpdateThirst(float deltaTime, World world)
    {
        // A bottle in the pack is a drink anywhere (unless it is already at the water: that one is free).
        if (State != BramblekinState.Drinking && Pack.Count(ItemKind.Water) == 0 && TakeFromStore(ItemKind.Water, 3, deltaTime, world, maxDistance: 25f))
            return; // a few bottles from the home store, if it's near
        if (State != BramblekinState.Drinking && DrinkBottle(world))
            return;
        if (State != BramblekinState.Drinking || (_waterSpot is null && _drinkFrom is null && _drinkWell is null) || _drinkGeneration != WaterMap.Generation)
            PlanDrink(world);
        SetState(BramblekinState.Drinking);

        if (_drinkFrom is { } cistern)
        {
            if (cistern.IsCollapsed || cistern.Water <= 0)
            {
                _drinkFrom = null; // Run dry: to the pond after all.
                return;
            }
            if (!cistern.Contains(Position))
            {
                _drinkTimer = 0f;
                MoveTo(cistern.Position, WalkSpeed, deltaTime, world);
                return;
            }
            _drinkTimer += deltaTime;
            if (_drinkTimer < DrinkSeconds)
                return;
            if (world.DrinkFromCistern(cistern))
                Thirst = MathF.Max(0f, Thirst - CisternSip);
            _drinkFrom = null;
            _drinkTimer = 0f;
            StartPause();
            return;
        }

        if (_drinkWell is { } well)
        {
            float reach = Well.Radius + BodyRadius + 0.3f;
            if (GroundMover.HorizontalDistanceSquared(Position, well.Position) > reach * reach)
            {
                _drinkTimer = 0f;
                MoveTo(well.Position, WalkSpeed * (Thirst >= 90f ? 1.2f : 1f), deltaTime, world);
                return;
            }
            _drinkTimer += deltaTime;
            if (_drinkTimer < WellDrinkSeconds)
                return;
            Thirst = 0f;
            world.NoteWellDrink();
            FinishDrinkingFill(world);
            return;
        }

        if (_waterSpot is not { } spot)
        {
            Explore(deltaTime, world); // No water anywhere: can't happen in the garden as it is.
            return;
        }
        if (GroundMover.HorizontalDistanceSquared(Position, spot) > 0.5f * 0.5f)
        {
            _drinkTimer = 0f;
            MoveTo(spot, WalkSpeed * (Thirst >= 90f ? 1.2f : 1f), deltaTime, world);
            return;
        }

        _mover.Heading = TowardWater(spot);
        _drinkTimer += deltaTime;
        if (_drinkTimer < DrinkSeconds)
            return;
        Thirst = 0f;
        world.NoteDrinkAtPond(this);
        FinishDrinkingFill(world);
    }

    /// <summary>Drunk its fill at the pond or a well: a cupful for home, if it knows cisterns and home's isn't full.</summary>
    private void FinishDrinkingFill(World world)
    {
        if (IsHeard(world))
            Sfx.Play(Sfx.Effect.Drink);
        Pack.Add(ItemKind.Water, BottlesPerDrink); // it tops its bottles up while it's there
        _waterSpot = null;
        _drinkWell = null;
        _drinkTimer = 0f;
        StartPause();
    }

    /// <summary>Where to drink: the nearest of the pond's shore, a well it may use, and its home's cistern (if that has water).</summary>
    private void PlanDrink(World world)
    {
        _drinkTimer = 0f;
        _drinkGeneration = WaterMap.Generation;
        _waterSpot = World.NearestShoreSpot(Position, 200f, creek: true);
        float nearest = _waterSpot is { } shore ? GroundMover.HorizontalDistance(Position, shore) : float.MaxValue;

        _drinkWell = world.NearestUsableWell(this, nearest);
        if (_drinkWell is { } well)
            nearest = GroundMover.HorizontalDistance(Position, well.Position);

        _drinkFrom = Home is { HasCistern: true, Water: > 0, IsCollapsed: false } home && GroundMover.HorizontalDistance(Position, home.Position) < nearest
            ? home
            : null;
        if (_drinkFrom is not null)
            _drinkWell = null;
    }

    /// <summary>With spare bottles in the pack, it pours them into its home's cistern (keeping a couple to drink). False when it has none to pour or no cistern to pour into.</summary>
    private bool UpdateWaterCarry(float deltaTime, World world)
    {
        if (Pack.Count(ItemKind.Water) < BottleReserve + PourTrip || !Knows(Craft.Cisterns) ||
            Home is not { HasCistern: true, IsCollapsed: false } home || home.Water >= home.CisternCapacity)
            return false;
        SetState(BramblekinState.Stockpiling);
        if (!home.Contains(Position))
        {
            MoveTo(home.Position, WalkSpeed, deltaTime, world);
            return true;
        }
        int pour = Pack.Count(ItemKind.Water) - BottleReserve;
        Pack.Remove(ItemKind.Water, pour);
        world.PourWater(home, CupfulSips * pour);
        StartPause();
        return true;
    }
}
