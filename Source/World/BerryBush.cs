using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Farming: a berry bush a group planted near its home (see
/// World.Farming). It grows from a seedling to a fruiting bush, then ripens
/// a berry every <see cref="FruitInterval"/> seconds — at the season's pace,
/// so it fruits hardest in summer and barely at all in winter — holding up
/// to <see cref="MaxFruit"/> for its Farmers to pick. Fruit nobody picks
/// falls as loose Berries, free to anyone. When its group is gone the bush
/// runs wild: it keeps fruiting for everyone until it withers.
/// </summary>
public sealed class BerryBush
{
    /// <summary>Seconds (at the season's pace) from planting to its first fruit.</summary>
    public const float GrowSeconds = 90f;

    /// <summary>Seconds (at the season's pace) between ripe berries.</summary>
    public const float FruitInterval = 40f;

    /// <summary>It holds at most this many ripe berries; the next one falls.</summary>
    public const int MaxFruit = 4;

    /// <summary>A bush left without its group withers this long after.</summary>
    public const float WildLifespan = 600f;

    /// <summary>Its footprint (m), for spacing and picking.</summary>
    public const float Radius = 0.45f;

    private static readonly Color LeafColor = new(55, 125, 50, 255);
    private static readonly Color WinterLeafColor = new(110, 100, 70, 255);
    private static readonly Color BerryColor = new(200, 35, 60, 255);

    private float _fruitTimer;

    public BerryBush(Vector3 groundPoint, Guid? groupId)
    {
        Position = World.Grounded(groundPoint);
        GroupId = groupId;
    }

    public Vector3 Position { get; }

    /// <summary>The group that planted (and tends) it — null once it has gone wild.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Seconds it has grown so far, up to <see cref="GrowSeconds"/>.</summary>
    public float Growth { get; private set; }

    public bool IsMature => Growth >= GrowSeconds;

    /// <summary>Ripe berries on it right now.</summary>
    public int Fruit { get; private set; }

    /// <summary>Seconds it has been wild (see <see cref="WildLifespan"/>).</summary>
    public float WildSeconds { get; set; }

    public bool IsWithered => WildSeconds >= WildLifespan;

    /// <summary>Grows or ripens at the season's pace (see <see cref="World.FoodAbundance"/>). Returns true if a berry just fell off a full bush.</summary>
    public bool Update(float deltaTime, float abundance)
    {
        if (!IsMature)
        {
            Growth = MathF.Min(GrowSeconds, Growth + deltaTime * abundance);
            return false;
        }

        _fruitTimer += deltaTime * abundance;
        if (_fruitTimer < FruitInterval)
            return false;
        _fruitTimer -= FruitInterval;
        if (Fruit < MaxFruit)
        {
            Fruit++;
            return false;
        }
        return true; // Overripe: this one drops.
    }

    /// <summary>Seconds toward its next berry, for saving.</summary>
    public float FruitTimer => _fruitTimer;

    /// <summary>Loading a saved world: puts back how grown and laden it was.</summary>
    public void Restore(float growth, int fruit, float fruitTimer, float wildSeconds)
    {
        Growth = growth;
        Fruit = fruit;
        _fruitTimer = fruitTimer;
        WildSeconds = wildSeconds;
    }

    /// <summary>A flood strips its ripe berries.</summary>
    public void LoseFruit() => Fruit = 0;

    /// <summary>Picks one ripe berry. False if there's none.</summary>
    public bool TryPick()
    {
        if (Fruit <= 0)
            return false;
        Fruit--;
        return true;
    }

    /// <summary>A round leafy clump (smaller while young, browner in winter or once wild and withering), dotted with its ripe berries.</summary>
    public void Draw(bool winter, Color? groupColor)
    {
        float size = 0.25f + 0.25f * (Growth / GrowSeconds);
        Color leaves = winter ? WinterLeafColor : LeafColor;
        if (GroupId is null)
            leaves = Blend(leaves, WinterLeafColor, Math.Clamp(WildSeconds / WildLifespan, 0f, 1f));

        Vector3 center = Position + new Vector3(0f, size * 0.8f, 0f);
        Raylib.DrawSphere(center, size, leaves);
        Raylib.DrawSphere(center + new Vector3(size * 0.6f, -size * 0.3f, 0f), size * 0.7f, leaves);
        Raylib.DrawSphere(center + new Vector3(-size * 0.5f, -size * 0.35f, size * 0.3f), size * 0.65f, leaves);

        for (int i = 0; i < Fruit; i++)
        {
            float angle = i * MathF.Tau / MaxFruit + 0.4f;
            Vector3 berry = center + new Vector3(MathF.Cos(angle) * size * 0.95f, size * 0.1f * (i % 2), MathF.Sin(angle) * size * 0.95f);
            Raylib.DrawSphere(berry, 0.07f, BerryColor);
        }

        // A little stake in its group's colour marks whose it is.
        if (groupColor is { } color)
        {
            Vector3 stakeBase = Position + new Vector3(size + 0.15f, 0f, 0f);
            Raylib.DrawCylinderEx(stakeBase, stakeBase + new Vector3(0f, 0.45f, 0f), 0.02f, 0.02f, 4, new Color(120, 90, 60, 255));
            Raylib.DrawCube(stakeBase + new Vector3(0.06f, 0.4f, 0f), 0.12f, 0.08f, 0.02f, color);
        }
    }

    private static Color Blend(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        (byte)255);
}
