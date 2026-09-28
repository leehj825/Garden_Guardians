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
    private static readonly Color CisternWaterColor = new(80, 140, 210, 255);
    private static readonly Color StickColor = new(115, 80, 45, 255);
    private static readonly Color StoredFoodColor = new(210, 40, 45, 255);
    private static readonly Color AbandonedTint = new(150, 150, 150, 255);

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
    public int TwigsNeeded => !IsBuilt ? TentTwigCost : IsUpgrading ? HouseUpgradeTwigCost : 0;

    /// <summary>True while a construction stage is under way and still short of twigs.</summary>
    public bool NeedsTwigs => TwigsDelivered < TwigsNeeded;

    public int StoredFood { get; private set; }

    public int StoreCapacity => Tier == ShelterTier.House
        ? (HasGranary ? HouseStoreCapacity * 3 / 2 : HouseStoreCapacity) + (HasFooting ? FootingStoreBonus : 0)
        : TentStoreCapacity;

    /// <summary>Its clan knows <see cref="Craft.Granary"/> (Houses only): half as much again in store.</summary>
    public bool HasGranary { get; set; }

    /// <summary>A cistern holds this many sips.</summary>
    public const int CisternSips = 8;

    /// <summary>Its clan knows <see cref="Craft.Cisterns"/>: an acorn-cup cistern by the door (Houses only), filled by the rain and by cupfuls carried home.</summary>
    public bool HasCistern
    {
        get => _hasCistern && Tier == ShelterTier.House;
        set => _hasCistern = value;
    }

    private bool _hasCistern;

    public int CisternCapacity => HasCistern ? CisternSips : 0;

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

    public float Radius => Tier == ShelterTier.House ? HouseRadius : TentRadius;

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

        if (!IsBuilt)
        {
            DrawSticks(basePosition, TentRadius, TwigsDelivered, TentTwigCost);
            return;
        }

        float radius = Radius;
        float roofTop;
        if (Tier == ShelterTier.Tent)
        {
            // Three twig legs holding an acorn cap up like a little umbrella.
            const float legHeight = 0.62f;
            for (int i = 0; i < 3; i++)
            {
                float angle = MathF.PI / 3f + i * MathF.Tau / 3f;
                Vector3 foot = basePosition + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * radius * 0.7f;
                Raylib.DrawCylinderEx(foot, basePosition + new Vector3(0f, legHeight, 0f) + (foot - basePosition) * 0.35f, 0.04f, 0.03f, 5, Tint(StickColor));
            }
            roofTop = DrawCap(basePosition + new Vector3(0f, legHeight, 0f), radius * 1.05f, Tint);
            if (IsUpgrading)
                DrawSticks(basePosition, HouseRadius, TwigsDelivered, HouseUpgradeTwigCost);
        }
        else
        {
            // The nut: a tall egg, sitting a little into the ground.
            float bodyHeight = radius * 1.25f;
            Vector3 bodyCenter = basePosition + new Vector3(0f, bodyHeight * 0.82f, 0f);
            DrawEllipsoid(bodyCenter, radius * 0.92f, bodyHeight, Tint(NutColor));
            DrawEllipsoid(bodyCenter + new Vector3(0f, -bodyHeight * 0.55f, 0f), radius * 0.7f, bodyHeight * 0.35f, Tint(NutShadeColor));

            // A round door facing out (+X), and a warm round window above to one side.
            Vector3 door = basePosition + new Vector3(radius * 0.86f, 0.36f, 0f);
            Raylib.DrawCylinderEx(door, door + new Vector3(0.06f, 0f, 0f), 0.3f, 0.3f, 14, DoorColor);
            Vector3 window = basePosition + new Vector3(radius * 0.6f, bodyHeight * 1.12f, radius * 0.55f);
            Raylib.DrawCylinderEx(window, window + new Vector3(0.05f, 0f, 0.05f), 0.13f, 0.13f, 10, IsAbandoned ? DoorColor : WindowColor);

            roofTop = DrawCap(bodyCenter + new Vector3(0f, bodyHeight * 0.62f, 0f), radius * 1.02f, Tint);
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
            // A hazelnut on the far side from the door, its pale base to the ground.
            Vector3 hazel = basePosition + new Vector3(-radius - 0.3f, 0.32f, 0.3f);
            Raylib.DrawSphere(hazel, 0.33f, Tint(HazelColor));
            DrawEllipsoid(hazel + new Vector3(0f, -0.2f, 0f), 0.27f, 0.12f, Tint(HazelBaseColor));
            Raylib.DrawCylinderEx(hazel + new Vector3(0f, 0.3f, 0f), hazel + new Vector3(0.04f, 0.42f, 0f), 0.04f, 0.02f, 4, Tint(StickColor));
        }

        if (HasCistern)
        {
            // An acorn cup on the ground out front, water showing as it fills.
            Vector3 cup = basePosition + new Vector3(-radius * 0.35f, 0f, radius + 0.35f);
            Raylib.DrawCylinder(cup, 0.3f, 0.22f, 0.32f, 10, Tint(CapColor));
            if (Water > 0)
                Raylib.DrawCylinder(cup + new Vector3(0f, 0.05f + 0.25f * Water / CisternSips, 0f), 0.2f + 0.08f * Water / CisternSips, 0.2f + 0.08f * Water / CisternSips, 0.02f, 10, CisternWaterColor);
        }

        if (StonesLaid > 0 && Tier == ShelterTier.House)
        {
            // The stone footing: grey stones set round the foot of the nut, filling in as they're laid.
            int stones = 16 * Math.Min(StonesLaid, FootingStoneCost) / FootingStoneCost;
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
            for (int i = 1; i < standing; i++)
            {
                float angle = i * MathF.Tau / 22f;
                var outward = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                Vector3 foot = World.Grounded(Position + outward * ring);
                Vector3 bend = foot + new Vector3(0f, 0.28f, 0f) + outward * 0.05f;
                Raylib.DrawCylinderEx(foot, bend, 0.09f, 0.05f, 5, Tint(ThornColor));
                Raylib.DrawCylinderEx(bend, bend + new Vector3(0f, 0.14f, 0f) + outward * 0.16f, 0.05f, 0f, 5, Tint(ThornColor));
            }
        }

        // Stored Food: a little pile by the door, one berry per piece (up to 8 shown).
        int shown = Math.Min(StoredFood, 8);
        for (int i = 0; i < shown; i++)
        {
            float angle = i * 0.8f;
            Vector3 berry = basePosition + new Vector3(radius + 0.2f + 0.12f * MathF.Cos(angle), 0.08f + 0.1f * (i / 4), 0.12f * MathF.Sin(angle));
            Raylib.DrawSphere(berry, 0.08f, StoredFoodColor);
        }
    }

    /// <summary>An acorn cap: a low scaly dome with a stalk, sitting on <paramref name="rim"/>. Returns the height of its top.</summary>
    private static float DrawCap(Vector3 rim, float radius, Func<Color, Color> tint)
    {
        float height = radius * 0.5f;
        Vector3 center = rim + new Vector3(0f, height * 0.15f, 0f);
        DrawEllipsoid(center, radius, height, tint(CapColor));
        // The scales: rings of small, low-poly bumps round the dome, staggered like a woven cup.
        for (int ring = 0; ring < 3; ring++)
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
        Raylib.DrawSphereEx(Vector3.Zero, 1f, 8, 12, color);
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

    private static Color Blend(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        a.A);
}
