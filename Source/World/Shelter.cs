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

    private static readonly Color CanvasColor = new(222, 205, 160, 255);
    private static readonly Color WallColor = new(170, 120, 70, 255);
    private static readonly Color RoofColor = new(120, 70, 40, 255);
    private static readonly Color EdgeColor = new(70, 45, 25, 255);
    private static readonly Color DoorColor = new(45, 30, 20, 255);
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
        ? HasGranary ? HouseStoreCapacity * 3 / 2 : HouseStoreCapacity
        : TentStoreCapacity;

    /// <summary>Its clan knows <see cref="Craft.Granary"/> (Houses only): half as much again in store.</summary>
    public bool HasGranary { get; set; }

    /// <summary>Its clan knows <see cref="Craft.Palisade"/>: a ring of stakes (<see cref="PalisadeRadius"/>) round it.</summary>
    public bool HasPalisade { get; set; }

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

    /// <summary>A construction site shows the twigs laid so far; a Tent is a canvas pyramid; a House is walls under a roof. Stored Food piles up by the door, and a group home flies its group's colour.</summary>
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
        if (Tier == ShelterTier.Tent)
        {
            const float height = 1.1f;
            Raylib.DrawCylinder(basePosition, 0f, radius, height, 4, Tint(CanvasColor));
            Raylib.DrawCylinderWires(basePosition, 0f, radius, height, 4, EdgeColor);
            Raylib.DrawCube(basePosition + new Vector3(radius * 0.55f, 0.18f, 0f), 0.05f, 0.36f, 0.24f, DoorColor);
            if (IsUpgrading)
                DrawSticks(basePosition, HouseRadius, TwigsDelivered, HouseUpgradeTwigCost);
        }
        else
        {
            float wall = radius * 1.4f;
            const float wallHeight = 0.9f;
            Vector3 wallCenter = basePosition + new Vector3(0f, wallHeight / 2f, 0f);
            Raylib.DrawCube(wallCenter, wall, wallHeight, wall, Tint(WallColor));
            Raylib.DrawCubeWires(wallCenter, wall, wallHeight, wall, EdgeColor);
            Vector3 roofBase = basePosition + new Vector3(0f, wallHeight, 0f);
            Raylib.DrawCylinder(roofBase, 0f, radius * 1.15f, 0.75f, 4, Tint(RoofColor));
            Raylib.DrawCylinderWires(roofBase, 0f, radius * 1.15f, 0.75f, 4, EdgeColor);
            Raylib.DrawCube(basePosition + new Vector3(wall / 2f + 0.01f, 0.25f, 0f), 0.04f, 0.5f, 0.3f, DoorColor);
        }

        if (groupColor is { } color)
        {
            float top = Tier == ShelterTier.House ? 0.9f + 0.75f : 1.1f;
            Vector3 poleBase = basePosition + new Vector3(0f, top, 0f);
            Vector3 poleTop = poleBase + new Vector3(0f, 0.45f, 0f);
            Raylib.DrawLine3D(poleBase, poleTop, StickColor);
            Raylib.DrawCube(poleTop + new Vector3(0.12f, -0.08f, 0f), 0.22f, 0.15f, 0.02f, color);
        }

        if (HasGranary && Tier == ShelterTier.House)
        {
            // A little round granary on the far side from the door.
            Vector3 granary = basePosition + new Vector3(-radius - 0.35f, 0f, 0.25f);
            Raylib.DrawCylinder(granary, 0.28f, 0.28f, 0.55f, 8, Tint(CanvasColor));
            Raylib.DrawCylinder(granary + new Vector3(0f, 0.55f, 0f), 0f, 0.36f, 0.3f, 8, Tint(RoofColor));
        }

        if (HasPalisade)
        {
            // A ring of stakes, with a gap for the door.
            float ring = PalisadeRadius;
            for (int i = 1; i < 22; i++)
            {
                float angle = i * MathF.Tau / 22f;
                Vector3 foot = World.Grounded(Position + new Vector3(MathF.Cos(angle) * ring, 0f, MathF.Sin(angle) * ring));
                Raylib.DrawCylinder(foot, 0.05f, 0.03f, 0.55f, 4, Tint(StickColor));
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
