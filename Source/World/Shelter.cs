using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>How far a <see cref="Shelter"/> has been built up.</summary>
public enum ShelterTier
{
    /// <summary>A small canvas-and-twig tent: room for a couple, a small food store.</summary>
    Tent,

    /// <summary>A walled house with a roof — a group's upgrade of its Tent: room for a whole group and a big store.</summary>
    House,

    /// <summary>Dug into a hillside by a loner: cheap, warm in winter, its store hidden from raiders and ants — but only room for two, and it can't be built up (see <see cref="Shelter.MakeBurrow"/>).</summary>
    Burrow,
}

/// <summary>
/// Settling: a home built from Twigs. It starts as a construction site
/// (<see cref="IsBuilt"/> false) that takes <see cref="TentTwigCost"/>
/// twigs to become a Tent, which a group can later upgrade into a House
/// with another <see cref="HouseUpgradeTwigCost"/>. A built shelter:
///   * stores Food that never rots (<see cref="StoredFood"/>),
///   * heals whoever rests inside it,
///   * hides whoever is inside it from the Wolf Spider's pounce and from
///     Hornets (see <see cref="Bramblekin.IsSheltered"/>).
/// It belongs either to one Bramblekin (<see cref="Owner"/>) or to a group
/// (<see cref="GroupId"/>). With neither it's abandoned: anyone may take its
/// food or move in, and after <see cref="AbandonedCollapseSeconds"/> it
/// collapses, spilling whatever was stored.
/// </summary>
public sealed class Shelter
{
    private static int _nextId;

    public const int TentTwigCost = 3;

    /// <summary>A burrow only needs a couple of twigs to shore up its doorway — the digging is the work.</summary>
    public const int BurrowTwigCost = 2;

    public const float BurrowRadius = 0.65f;
    public const int HouseUpgradeTwigCost = 6;
    public const int TentStoreCapacity = 4;
    public const int HouseStoreCapacity = 12;

    /// <summary>How many a shelter comfortably houses — a group bigger than its Tent's capacity has a reason to upgrade.</summary>
    public const int TentResidentCapacity = 2;
    public const int HouseResidentCapacity = 6;

    public const float TentRadius = 0.7f;
    public const float HouseRadius = 1.3f;

    /// <summary>An abandoned shelter collapses after this many seconds without an owner.</summary>
    public const float AbandonedCollapseSeconds = 90f;

    // Acorn village: a Tent is an acorn cap propped on twigs, a House a whole
    // hollowed acorn under its cap, a granary a hazelnut, a palisade rose thorns.
    private static readonly Color NutColor = new(176, 116, 52, 255);
    private static readonly Color NutShadeColor = new(140, 88, 38, 255);
    private static readonly Color CapColor = new(128, 104, 70, 255);
    private static readonly Color CapScaleColor = new(112, 90, 60, 255);
    private static readonly Color StalkColor = new(92, 72, 46, 255);
    private static readonly Color DoorColor = new(62, 38, 22, 255);
    private static readonly Color WindowColor = new(245, 210, 120, 255);
    private static readonly Color HazelColor = new(150, 100, 55, 255);
    private static readonly Color HazelBaseColor = new(205, 180, 135, 255);
    private static readonly Color ThornColor = new(128, 58, 44, 255);
    private static readonly Color FootingColor = new(140, 140, 146, 255);
    private static readonly Color SiteColor = new(125, 95, 62, 255);
    private static readonly Color CisternWaterColor = new(80, 140, 210, 255);
    private static readonly Color StickColor = new(115, 80, 45, 255);
    private static readonly Color StoredFoodColor = new(210, 40, 45, 255);
    private static readonly Color AbandonedTint = new(150, 150, 150, 255);
    private static readonly Color EarthColor = new(118, 88, 58, 255);
    private static readonly Color TurfColor = new(92, 140, 60, 255);
    private static readonly Color FlameColor = new(235, 120, 30, 255);
    private static readonly Color EmberColor = new(255, 215, 90, 255);
    private static readonly Color AshColor = new(95, 90, 88, 255);

    public Shelter(Vector3 groundPoint)
    {
        Position = World.Grounded(groundPoint);
    }

    public int ID { get; private set; } = _nextId++;

    /// <summary>Loading a saved world: puts back a shelter's own state (see World.Save).</summary>
    public void Restore(int id, ShelterTier tier, bool built, bool upgrading, int twigs, int stored)
    {
        ID = id;
        _nextId = Math.Max(_nextId, id + 1);
        Tier = tier;
        IsBuilt = built;
        IsUpgrading = upgrading;
        TwigsDelivered = twigs;
        StoredFood = stored;
    }

    public Vector3 Position { get; }

    public ShelterTier Tier { get; private set; } = ShelterTier.Tent;

    /// <summary>False while it's still a construction site — see <see cref="TwigsNeeded"/>.</summary>
    public bool IsBuilt { get; private set; }

    /// <summary>True while a built Tent is being upgraded into a House; it keeps working as a Tent meanwhile.</summary>
    public bool IsUpgrading { get; private set; }

    /// <summary>Twigs delivered toward the current stage (the Tent, or the House upgrade).</summary>
    public int TwigsDelivered { get; private set; }

    /// <summary>Twigs the current stage needs in total — 0 once there's nothing left to build.</summary>
    public int TwigsNeeded => !IsBuilt ? (IsBurrow ? BurrowTwigCost : TentTwigCost) : IsUpgrading ? HouseUpgradeTwigCost : 0;

    public bool IsBurrow => Tier == ShelterTier.Burrow;

    /// <summary>Marks a fresh site out as a burrow rather than a Tent.</summary>
    public void MakeBurrow()
    {
        if (!IsBuilt)
            Tier = ShelterTier.Burrow;
    }

    /// <summary>True while a construction stage is under way and still short of twigs.</summary>
    public bool NeedsTwigs => TwigsDelivered < TwigsNeeded;

    public int StoredFood { get; private set; }

    public int StoreCapacity => Tier == ShelterTier.House
        ? (HasGranary ? HouseStoreCapacity * 3 / 2 : HouseStoreCapacity) + (HasFooting ? FootingStoreBonus : 0)
        : TentStoreCapacity;

    /// <summary>Its clan knows <see cref="Craft.Granary"/> (Houses only): half as much again in store.</summary>
    public bool HasGranary { get; set; }

    /// <summary>True if its clan knows tools: a workbench stands by the home (see <see cref="Craft.Tools"/>).</summary>
    public bool HasWorkshop { get; set; }

    /// <summary>True if its clan holds a market: a stall stands by the home (see <see cref="Craft.Markets"/>).</summary>
    public bool HasMarket { get; set; }

    /// <summary>True if its clan writes: a standing stone with runes stands by the home (see <see cref="Craft.Writing"/>).</summary>
    public bool HasRuneStone { get; set; }

    /// <summary>True if its clan keeps a watch: a tower with an alarm horn stands by the home (see <see cref="Craft.Watchtowers"/>).</summary>
    public bool HasWatchtower { get; set; }

    /// <summary>True if its clan keeps a calendar: a sundial stands by the home (see <see cref="Craft.Calendar"/>).</summary>
    public bool HasSundial { get; set; }

    /// <summary>True if its clan knows Medicine: a herb garden grows by the home (see <see cref="Craft.Medicine"/>).</summary>
    public bool HasHerbGarden { get; set; }

    /// <summary>Seconds the alarm horn has left to sound (drives its drawn blast).</summary>
    public float HornSeconds { get; set; }

    /// <summary>A cistern holds this many sips.</summary>
    public const int CisternSips = 6;

    /// <summary>Its clan knows <see cref="Craft.Cisterns"/>: an acorn-cup cistern by the door (Houses only), filled by the rain and by cupfuls carried home.</summary>
    public bool HasCistern
    {
        get => _hasCistern && Tier == ShelterTier.House;
        set => _hasCistern = value;
    }

    private bool _hasCistern;

    public int CisternCapacity => HasCistern ? CisternSips : 0;

    /// <summary>Its clan knows <see cref="Craft.Hearth"/>: a ring of stones out front of the House where a fire burns while it's fed twigs.</summary>
    public bool HasHearth
    {
        get => _hasHearth && Tier == ShelterTier.House;
        set => _hasHearth = value;
    }

    private bool _hasHearth;

    /// <summary>One twig keeps a hearth burning this long (s)…</summary>
    public const float HearthSecondsPerTwig = 120f;

    /// <summary>…and it holds at most this many twigs' worth at once.</summary>
    public const int HearthTwigCapacity = 3;

    /// <summary>Seconds of burning left in its hearth.</summary>
    public float HearthFuel { get; set; }

    /// <summary>A fire is burning in its hearth (and someone still lives here to tend it).</summary>
    public bool IsHearthLit => HasHearth && HearthFuel > 0f && !IsAbandoned;

    /// <summary>True while its hearth has room for another twig — its folk keep it topped up.</summary>
    public bool NeedsFuel => HasHearth && IsBuilt && !IsAbandoned && HearthFuel <= HearthSecondsPerTwig * (HearthTwigCapacity - 1);

    /// <summary>A twig on the fire.</summary>
    public void AddFuel() => HearthFuel = MathF.Min(HearthSecondsPerTwig * HearthTwigCapacity, HearthFuel + HearthSecondsPerTwig);

    /// <summary>Where the hearth sits: out front, off to the side away from the cistern.</summary>
    /// <summary>Where a lived-in House's window is (lit at night — see World.DrawNightLights); null for anything else.</summary>
    public Vector3? WindowPosition => IsBuilt && !IsBurrow && !IsAbandoned && Tier == ShelterTier.House
        ? Position + new Vector3(0f, 0.02f, 0f) + new Vector3(0f, 0.36f * Radius * 2.6f * 0.8f / PropModels.HouseWidth, -(0.36f * Radius * 2.6f * 0.8f / PropModels.HouseWidth))
        : null;

    public Vector3 HearthPosition => Position + new Vector3(Radius * 0.2f, 0f, -(Radius + 0.5f));

    /// <summary>Sips of water in its cistern.</summary>
    public int Water { get; set; }

    /// <summary>A palisade takes this many branches staked round the home (see <see cref="Craft.Palisade"/>)…</summary>
    public const int PalisadeStakeCost = 3;

    /// <summary>…and a House's stone footing this many stones (see <see cref="Craft.Stonework"/>), which gives its store this much more room.</summary>
    public const int FootingStoneCost = 4;

    public const int FootingStoreBonus = 2;

    /// <summary>Branches staked into its palisade so far.</summary>
    public int StakesSet { get; set; }

    /// <summary>Stones laid into its footing so far.</summary>
    public int StonesLaid { get; set; }

    /// <summary>Its palisade is up (every stake set): a ring of stakes (<see cref="PalisadeRadius"/>) round it.</summary>
    public bool HasPalisade => StakesSet >= PalisadeStakeCost;

    /// <summary>A House raised on a stone footing: its store holds more, stays dry in a flood, and ants can't dig into it.</summary>
    public bool HasFooting => Tier == ShelterTier.House && StonesLaid >= FootingStoneCost;

    /// <summary>True while a built home's palisade still wants branches.</summary>
    public bool NeedsStakes => IsBuilt && StakesSet < PalisadeStakeCost;

    /// <summary>True while a House (not mid-upgrade) still wants stones for its footing.</summary>
    public bool NeedsStones => IsBuilt && Tier == ShelterTier.House && StonesLaid < FootingStoneCost;

    /// <summary>Stakes a branch into the palisade. False if it's already up.</summary>
    public bool SetStake()
    {
        if (!NeedsStakes)
            return false;
        StakesSet++;
        return true;
    }

    /// <summary>Lays a stone into the footing. False if it's done (or this isn't a House).</summary>
    public bool LayStone()
    {
        if (!NeedsStones)
            return false;
        StonesLaid++;
        return true;
    }

    /// <summary>How far out a palisade stands from the home's centre.</summary>
    public float PalisadeRadius => Radius + 1.6f;

    public int ResidentCapacity => Tier == ShelterTier.House ? HouseResidentCapacity : TentResidentCapacity;

    public bool StoreIsFull => StoredFood >= StoreCapacity;

    public float Radius => Tier switch { ShelterTier.House => HouseRadius, ShelterTier.Burrow => BurrowRadius, _ => TentRadius };

    /// <summary>The one Bramblekin it belongs to, for a personal home.</summary>
    public Bramblekin? Owner { get; set; }

    /// <summary>The group it belongs to, for a group home.</summary>
    public Guid? GroupId { get; set; }

    public bool IsAbandoned => Owner is null && GroupId is null;

    /// <summary>Seconds spent abandoned; World collapses it at <see cref="AbandonedCollapseSeconds"/>.</summary>
    public float AbandonedSeconds { get; set; }

    /// <summary>
    /// How many of its residents are inside it right now (counted once a
    /// frame). A shelter crammed past <see cref="ResidentCapacity"/>
    /// protects nobody — see <see cref="IsOvercrowded"/>.
    /// </summary>
    public int Occupants { get; set; }

    public bool IsOvercrowded => Occupants > ResidentCapacity;

    /// <summary>Game time (s) its current construction stage began — the site marked out, or the House upgrade started. For progress statistics.</summary>
    public float StageStartedAt { get; set; }

    /// <summary>True once it has fallen down and been removed from the map; anyone still calling it home must find another.</summary>
    public bool IsCollapsed { get; private set; }

    /// <summary>True if <paramref name="point"/> is inside its footprint.</summary>
    public bool Contains(Vector3 point) =>
        GroundMover.HorizontalDistanceSquared(point, Position) <= Radius * Radius;

    /// <summary>Adds one twig to the current stage. Returns true if that twig finished it (a Tent built, or a House completed).</summary>
    public bool AddTwig()
    {
        if (!NeedsTwigs)
            return false;

        TwigsDelivered++;
        if (TwigsDelivered < TwigsNeeded)
            return false;

        if (!IsBuilt)
            IsBuilt = true;
        else if (IsUpgrading)
        {
            Tier = ShelterTier.House;
            IsUpgrading = false;
        }
        TwigsDelivered = 0;
        return true;
    }

    /// <summary>Starts upgrading a built Tent into a House.</summary>
    public void BeginUpgrade()
    {
        if (IsBuilt && Tier == ShelterTier.Tent && !IsUpgrading)
        {
            IsUpgrading = true;
            TwigsDelivered = 0;
        }
    }

    public bool TryDeposit()
    {
        if (!IsBuilt || StoreIsFull)
            return false;
        StoredFood++;
        return true;
    }

    public bool TryWithdraw()
    {
        if (StoredFood <= 0)
            return false;
        StoredFood--;
        return true;
    }

    /// <summary>Marks it collapsed and returns how much Food was left inside to spill.</summary>
    public int Collapse()
    {
        IsCollapsed = true;
        int spilled = StoredFood;
        StoredFood = 0;
        return spilled;
    }

    /// <summary>A home looking at least this many pixels across (see <see cref="Detail"/>) is drawn in full: cap scales, footing stones, thorn tips, hearth pebbles.</summary>
    private const float FineDetailPixels = 25f;

    /// <summary>Whether the home being drawn right now is close enough for its fine detail.</summary>
    private static bool _fineDetail = true;

    /// <summary>
    /// A construction site shows the twigs laid so far; a Tent is an acorn
    /// cap propped on twig legs; a House is a whole hollowed acorn under its
    /// cap, with a round door and a lit window. A granary is a hazelnut
    /// beside the House, a palisade a ring of rose thorns. Stored Food piles
    /// up by the door, and a group home flies its group's colour.
    /// </summary>
    public void Draw(Color? groupColor)
    {
        Vector3 basePosition = Position + new Vector3(0f, 0.02f, 0f);
        Color Tint(Color c) => IsAbandoned ? Blend(c, AbandonedTint, 0.6f) : c;
        _fineDetail = Detail.Pixels(Position, Radius) >= FineDetailPixels;

        if (IsBurrow)
        {
            DrawBurrow(basePosition, groupColor, Tint);
            return;
        }

        if (!IsBuilt)
        {
            // A site marked out: a patch of bare earth, a stake flying its clan's colour, and the twigs laid so far.
            VillageModels.Draw(VillageItem.ConstructionSite, Position, 0f, (TentRadius + 0.3f) * 2f, Color.White);
            DrawSticks(basePosition, TentRadius, TwigsDelivered, TentTwigCost);
            if (groupColor is { } siteFlag)
            {
                Vector3 stake = basePosition + new Vector3(TentRadius + 0.15f, 0f, 0f);
                Raylib.DrawCylinderEx(stake, stake + new Vector3(0f, 0.8f, 0f), 0.03f, 0.025f, 5, StickColor);
                Raylib.DrawCube(stake + new Vector3(0.12f, 0.7f, 0f), 0.22f, 0.15f, 0.02f, siteFlag);
            }
            return;
        }

        float radius = Radius;
        float roofTop;
        if (Tier == ShelterTier.Tent)
        {
            // The tent model: an acorn cap propped on twigs and leaves.
            float tentWidth = radius * 2.2f;
            float tentScale = tentWidth * VillageModels.Scale / PropModels.TentWidth;
            PropModels.Draw(PropModels.Prop.Tent, basePosition, 0f, tentScale, Tint(Color.White));
            roofTop = basePosition.Y + tentScale; // The model is 1 unit tall.
            if (IsUpgrading)
                DrawSticks(basePosition, HouseRadius, TwigsDelivered, HouseUpgradeTwigCost);
        }
        else
        {
            // The acorn house model, its door turned to face out (+X).
            float scale = radius * 2.6f * 0.8f / PropModels.HouseWidth;
            PropModels.Draw(PropModels.Prop.House, basePosition, 180f, scale, Tint(Color.White));
            roofTop = basePosition.Y + PropModels.HouseCapTop * scale;
        }

        if (groupColor is { } color)
        {
            Vector3 poleBase = new(basePosition.X, roofTop, basePosition.Z);
            Vector3 poleTop = poleBase + new Vector3(0f, 0.45f, 0f);
            Raylib.DrawLine3D(poleBase, poleTop, StickColor);
            Raylib.DrawCube(poleTop + new Vector3(0.12f, -0.08f, 0f), 0.22f, 0.15f, 0.02f, color);
        }

        if (HasGranary && Tier == ShelterTier.House)
        {
            // A hazelnut store on the far side from the door.
            VillageModels.Draw(VillageItem.Granary, basePosition + new Vector3(-radius - 0.3f, 0f, 0.3f), 90f, 0.7f, Tint(Color.White));
        }

        if (HasCistern)
        {
            // A stone basin on the ground out front.
            VillageModels.Draw(VillageItem.Cistern, basePosition + new Vector3(-radius * 0.35f, 0f, radius + 0.35f), 0f, 0.8f, Tint(Color.White));
        }

        if (HasWorkshop)
        {
            // A workbench beside the home: a plank on trestles with a stone anvil and a mallet.
            Vector3 bench = basePosition + new Vector3(radius * 0.9f, 0f, -(radius + 0.7f));
            Raylib.DrawCube(bench + new Vector3(0f, 0.42f, 0f), 0.9f, 0.08f, 0.45f, Tint(WorkbenchColor));
            for (int side = -1; side <= 1; side += 2)
                Raylib.DrawCylinderEx(bench + new Vector3(side * 0.35f, 0f, 0f), bench + new Vector3(side * 0.35f, 0.4f, 0f), 0.05f, 0.04f, 5, Tint(StickColor));
            Detail.Sphere(bench + new Vector3(-0.2f, 0.56f, 0f), 0.11f, Tint(FootingColor));
            Raylib.DrawCylinderEx(bench + new Vector3(0.15f, 0.47f, 0.05f), bench + new Vector3(0.35f, 0.5f, 0.12f), 0.025f, 0.025f, 4, Tint(StickColor));
            Raylib.DrawCube(bench + new Vector3(0.38f, 0.52f, 0.13f), 0.12f, 0.08f, 0.08f, Tint(FootingColor));
        }

        if (HasMarket)
        {
            // A market stall by the home: four posts, a striped awning and a table of goods.
            Vector3 stall = basePosition + new Vector3(-radius * 0.8f, 0f, radius + 1.5f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 post = stall + new Vector3((i % 2 == 0 ? -0.5f : 0.5f), 0f, (i < 2 ? -0.35f : 0.35f));
                Raylib.DrawCylinderEx(post, post + new Vector3(0f, 0.9f, 0f), 0.03f, 0.025f, 5, Tint(StickColor));
            }
            Raylib.DrawCube(stall + new Vector3(0f, 0.95f, -0.12f), 1.2f, 0.04f, 0.5f, Tint(AwningColor));
            Raylib.DrawCube(stall + new Vector3(0f, 0.93f, 0.2f), 1.2f, 0.04f, 0.3f, Tint(AwningStripeColor));
            Raylib.DrawCube(stall + new Vector3(0f, 0.38f, 0f), 0.9f, 0.06f, 0.4f, Tint(WorkbenchColor));
            Detail.Sphere(stall + new Vector3(-0.25f, 0.47f, 0f), 0.09f, Tint(AwningStripeColor));
            Detail.Sphere(stall + new Vector3(0.2f, 0.46f, 0.05f), 0.08f, Tint(FootingColor));
        }

        if (HasRuneStone)
        {
            // A standing stone by the door, cut with rows of runes.
            Vector3 stone = basePosition + new Vector3(-(radius + 1.3f), 0f, -0.3f);
            Raylib.DrawCube(stone + new Vector3(0f, 0.5f, 0f), 0.5f, 1f, 0.28f, Tint(FootingColor));
            Raylib.DrawCube(stone + new Vector3(0f, 1.05f, 0f), 0.36f, 0.16f, 0.24f, Tint(FootingColor));
            var rune = Tint(new Color(45, 40, 35, 255));
            for (int row = 0; row < 3; row++)
            {
                Raylib.DrawCube(stone + new Vector3(-0.08f, 0.3f + row * 0.25f, 0.15f), 0.05f, 0.14f, 0.02f, rune);
                Raylib.DrawCube(stone + new Vector3(0.08f, 0.3f + row * 0.25f, 0.15f), 0.14f, 0.05f, 0.02f, rune);
            }
        }

        if (HasHerbGarden)
        {
            // A fenced bed of herbs: a low frame of sticks round little leafy tufts in pale green, grey-green and purple.
            Vector3 bed = basePosition + new Vector3(-(radius + 1.2f), 0f, radius * 0.5f + 0.6f);
            Raylib.DrawCube(bed + new Vector3(0f, 0.04f, 0f), 1.1f, 0.06f, 0.7f, Tint(new Color(100, 75, 50, 255)));
            for (int side = -1; side <= 1; side += 2)
            {
                Raylib.DrawCube(bed + new Vector3(0f, 0.14f, side * 0.35f), 1.1f, 0.06f, 0.05f, Tint(StickColor));
                Raylib.DrawCube(bed + new Vector3(side * 0.55f, 0.14f, 0f), 0.05f, 0.06f, 0.7f, Tint(StickColor));
            }
            for (int i = 0; i < 6; i++)
            {
                Color leaf = i % 3 == 0 ? new Color(150, 200, 120, 255) : i % 3 == 1 ? new Color(110, 150, 110, 255) : new Color(150, 110, 180, 255);
                Detail.Sphere(bed + new Vector3(-0.35f + (i % 3) * 0.35f, 0.15f, i < 3 ? -0.15f : 0.15f), 0.1f, Tint(leaf));
            }
        }

        if (HasSundial)
        {
            // A round stone dial on a short pillar, with a slanted pointer and hour marks round the rim.
            Vector3 dial = basePosition + new Vector3(radius * 0.4f, 0f, radius + 1.6f);
            Raylib.DrawCylinderEx(dial, dial + new Vector3(0f, 0.45f, 0f), 0.16f, 0.16f, 8, Tint(FootingColor));
            Raylib.DrawCylinderEx(dial + new Vector3(0f, 0.45f, 0f), dial + new Vector3(0f, 0.5f, 0f), 0.34f, 0.34f, 10, Tint(FootingColor));
            Raylib.DrawCylinderEx(dial + new Vector3(0f, 0.5f, 0f), dial + new Vector3(0.05f, 0.75f, -0.12f), 0.03f, 0.01f, 4, Tint(StickColor));
            for (int mark = 0; mark < 6; mark++)
            {
                float a = mark * MathF.PI / 3f;
                Raylib.DrawCube(dial + new Vector3(MathF.Cos(a) * 0.28f, 0.51f, MathF.Sin(a) * 0.28f), 0.03f, 0.02f, 0.03f, Tint(new Color(45, 40, 35, 255)));
            }
        }

        if (HasWatchtower)
        {
            // A lookout on four leaning posts: a platform, a rail, a little roof, and the alarm horn (a blast of rings when it sounds).
            Vector3 tower = basePosition + new Vector3(radius + 1.4f, 0f, radius * 0.4f + 0.6f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 foot = tower + new Vector3(i % 2 == 0 ? -0.4f : 0.4f, 0f, i < 2 ? -0.4f : 0.4f);
                Raylib.DrawCylinderEx(foot, tower + new Vector3(foot.X > tower.X ? 0.28f : -0.28f, 2.2f, foot.Z > tower.Z ? 0.28f : -0.28f), 0.06f, 0.05f, 5, Tint(StickColor));
            }
            Raylib.DrawCube(tower + new Vector3(0f, 2.2f, 0f), 0.9f, 0.08f, 0.9f, Tint(WorkbenchColor));
            Raylib.DrawCube(tower + new Vector3(0f, 2.5f, 0.42f), 0.9f, 0.05f, 0.04f, Tint(StickColor));
            Raylib.DrawCube(tower + new Vector3(0f, 3.05f, 0f), 1.15f, 0.07f, 1.15f, Tint(AwningColor));
            Raylib.DrawCube(tower + new Vector3(0f, 3.15f, 0f), 0.7f, 0.07f, 0.7f, Tint(AwningColor));
            Raylib.DrawCylinderEx(tower + new Vector3(0.2f, 2.4f, 0.3f), tower + new Vector3(0.45f, 2.55f, 0.55f), 0.03f, 0.1f, 6, Tint(new Color(225, 215, 190, 255)));
            if (HornSeconds > 0f)
            {
                Vector3 mouth = tower + new Vector3(0.45f, 2.55f, 0.55f);
                for (int ring = 1; ring <= 3; ring++)
                    Raylib.DrawCircle3D(mouth, 0.2f * ring, new Vector3(0f, 1f, 0f), 0f, new Color(255, 230, 120, 200 - ring * 50));
            }
        }

        if (HasHearth)
        {
            // A ring of stones round a fire pit; lit, a little fire of orange and yellow tongues that shrinks as the fuel burns down.
            Vector3 hearth = World.Grounded(HearthPosition) + new Vector3(0f, 0.03f, 0f);
            VillageModels.Draw(VillageItem.Hearth, hearth - new Vector3(0f, 0.03f, 0f), 0f, 0.7f, IsHearthLit ? Color.White : Tint(new Color(150, 150, 150, 255)));
            if (IsHearthLit)
            {
                float flame = 0.18f + 0.22f * HearthFuel / (HearthSecondsPerTwig * HearthTwigCapacity);
                Raylib.DrawCylinder(hearth + new Vector3(0f, 0.12f, 0f), 0.15f, 0f, flame, 6, FlameColor);
                Raylib.DrawCylinder(hearth + new Vector3(0f, 0.14f, 0f), 0.08f, 0f, flame * 0.7f, 6, EmberColor);
            }
        }

        if (StonesLaid > 0 && Tier == ShelterTier.House)
        {
            // The stone footing: grey stones set round the foot of the nut, filling in as they're laid.
            int stones = 16 * Math.Min(StonesLaid, FootingStoneCost) / FootingStoneCost;
            if (StonesLaid >= FootingStoneCost)
            {
                // Finished: a pad of set stones under the whole home.
                VillageModels.Draw(VillageItem.StoneFooting, basePosition, 0f, radius * 2f + 0.5f, Tint(Color.White));
                stones = 0;
            }
            else if (!_fineDetail && stones > 0)
            {
                // Seen from afar, the stones are just a grey band round the foot.
                Raylib.DrawCylinder(basePosition, radius * 0.95f + 0.12f, radius * 0.95f + 0.1f, 0.18f, 10, Tint(FootingColor));
                stones = 0;
            }
            for (int i = 0; i < stones; i++)
            {
                float angle = i * MathF.Tau / 16f + 0.2f;
                Vector3 stone = basePosition + new Vector3(MathF.Cos(angle) * radius * 0.95f, 0.08f, MathF.Sin(angle) * radius * 0.95f);
                Raylib.DrawSphereEx(stone, 0.17f, 4, 6, Tint(FootingColor));
            }
        }

        if (StakesSet > 0)
        {
            // A ring of thorny stakes, curving outward, with a gap for the door — going up branch by branch.
            float ring = PalisadeRadius;
            int standing = 1 + 21 * Math.Min(StakesSet, PalisadeStakeCost) / PalisadeStakeCost;
            if (StakesSet >= PalisadeStakeCost)
            {
                // Finished: the ring of rose thorns.
                VillageModels.Draw(VillageItem.Palisade, World.Grounded(Position), 0f, ring * 2f + 0.4f, Tint(Color.White));
                standing = 1;
            }
            for (int i = 1; i < standing; i++)
            {
                float angle = i * MathF.Tau / 22f;
                var outward = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                Vector3 foot = World.Grounded(Position + outward * ring);
                Vector3 bend = foot + new Vector3(0f, 0.28f, 0f) + outward * 0.05f;
                Raylib.DrawCylinderEx(foot, bend, 0.09f, 0.05f, 5, Tint(ThornColor));
                if (_fineDetail)
                    Raylib.DrawCylinderEx(bend, bend + new Vector3(0f, 0.14f, 0f) + outward * 0.16f, 0.05f, 0f, 5, Tint(ThornColor));
            }
        }

        // Stored Food: a little pile by the door, one berry per piece (up to 8 shown).
        int shown = Math.Min(StoredFood, 8);
        for (int i = 0; i < shown; i++)
        {
            float angle = i * 0.8f;
            Vector3 berry = basePosition + new Vector3(radius + 0.2f + 0.12f * MathF.Cos(angle), 0.08f + 0.1f * (i / 4), 0.12f * MathF.Sin(angle));
            Detail.Sphere(berry, 0.08f, StoredFoodColor);
        }
    }

    /// <summary>A burrow: a low mound of earth capped with turf and a dark round doorway, shored with twigs; while it's being dug, a raw heap beside an open hole.</summary>
    private void DrawBurrow(Vector3 basePosition, Color? groupColor, Func<Color, Color> tint)
    {
        float radius = BurrowRadius;
        var door = basePosition + new Vector3(radius * 0.9f, 0.18f, 0f);
        if (!IsBuilt)
        {
            DrawEllipsoid(basePosition + new Vector3(-radius * 0.4f, 0.05f, 0.3f), radius * 0.6f, 0.25f, tint(EarthColor));
            Raylib.DrawCylinder(basePosition + new Vector3(radius * 0.3f, 0.01f, 0f), 0.28f, 0.28f, 0.02f, 12, DoorColor);
            DrawSticks(basePosition, radius, TwigsDelivered, BurrowTwigCost);
            return;
        }
        VillageModels.Draw(VillageItem.Burrow, basePosition, 90f, radius * 2.8f, tint(Color.White));
        if (groupColor is { } color)
        {
            Vector3 pole = basePosition + new Vector3(-0.1f, 0.5f, 0f);
            Raylib.DrawLine3D(pole, pole + new Vector3(0f, 0.4f, 0f), StickColor);
            Raylib.DrawCube(pole + new Vector3(0.12f, 0.32f, 0f), 0.22f, 0.15f, 0.02f, color);
        }
        int shown = Math.Min(StoredFood, 4);
        for (int i = 0; i < shown; i++)
            Detail.Sphere(door + new Vector3(0.25f, -0.1f, (i - 1.5f) * 0.1f), 0.07f, StoredFoodColor);
    }

    /// <summary>An acorn cap: a low scaly dome with a stalk, sitting on <paramref name="rim"/>. Returns the height of its top.</summary>
    private static float DrawCap(Vector3 rim, float radius, Func<Color, Color> tint)
    {
        float height = radius * 0.5f;
        Vector3 center = rim + new Vector3(0f, height * 0.15f, 0f);
        DrawEllipsoid(center, radius, height, tint(CapColor));
        // The scales: rings of small, low-poly bumps round the dome, staggered like a woven cup — too small to see from afar.
        for (int ring = 0; ring < (_fineDetail ? 3 : 0); ring++)
        {
            float lift = (0.08f + ring * 0.3f) * height;
            float across = radius * MathF.Sqrt(MathF.Max(0f, 1f - (lift / height) * (lift / height))) * 1.005f;
            int bumps = 16 - ring * 4;
            for (int i = 0; i < bumps; i++)
            {
                float angle = (i + ring * 0.5f) * MathF.Tau / bumps;
                Raylib.DrawSphereEx(center + new Vector3(MathF.Cos(angle) * across, lift, MathF.Sin(angle) * across), radius * 0.08f, 3, 5, tint(CapScaleColor));
            }
        }
        Vector3 stalkBase = center + new Vector3(0f, height * 0.95f, 0f);
        Raylib.DrawCylinderEx(stalkBase, stalkBase + new Vector3(0.05f, 0.22f, 0f), 0.07f, 0.045f, 6, tint(StalkColor));
        return stalkBase.Y + 0.2f;
    }

    /// <summary>A sphere stretched to <paramref name="radius"/> across and <paramref name="height"/> tall.</summary>
    private static void DrawEllipsoid(Vector3 center, float radius, float height, Color color)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(center.X, center.Y, center.Z);
        Rlgl.Scalef(radius, height, radius);
        if (_fineDetail)
            Raylib.DrawSphereEx(Vector3.Zero, 1f, 8, 12, color);
        else
            Raylib.DrawSphereEx(Vector3.Zero, 1f, 5, 8, color);
        Rlgl.PopMatrix();
    }

    /// <summary>A construction stage in progress: <paramref name="delivered"/> of <paramref name="needed"/> sticks leaned in a ring, over a faint outline.</summary>
    private static void DrawSticks(Vector3 center, float radius, int delivered, int needed)
    {
        Raylib.DrawCircle3D(center, radius, new Vector3(1, 0, 0), 90f, new Color(80, 55, 30, 110));
        for (int i = 0; i < delivered; i++)
        {
            float angle = i * MathF.Tau / needed;
            Vector3 foot = center + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            Vector3 tip = center + new Vector3(0f, 0.9f, 0f) + (foot - center) * 0.15f;
            Raylib.DrawCylinderEx(foot, tip, 0.035f, 0.025f, 5, StickColor);
        }
    }

    private static readonly Color WorkbenchColor = new(140, 105, 70, 255);
    private static readonly Color AwningColor = new(190, 70, 60, 255);
    private static readonly Color AwningStripeColor = new(235, 225, 200, 255);

    private static Color Blend(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        a.A);
}
