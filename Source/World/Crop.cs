using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>What a clan grows (see <see cref="Crop"/>) — each kind needs its own craft, and bears in its own seasons.</summary>
public enum CropKind
{
    /// <summary>A berry bush (<see cref="Craft.Farming"/>): bears most in summer, a little all year.</summary>
    Berry,

    /// <summary>A patch of seed grass (<see cref="Craft.Grain"/>): nothing in winter, a big harvest from high summer into autumn.</summary>
    Grain,

    /// <summary>A mushroom bed in the damp shade against a House wall (<see cref="Craft.Mushrooms"/>): best in autumn, and bears a little on through winter.</summary>
    Mushroom,

    /// <summary>A cress bed on the pond's shore (<see cref="Craft.Cress"/>): best in spring, and never minds a drought.</summary>
    Cress,
}

/// <summary>
/// Farming: a crop a clan planted near its home (see World.Farming). It
/// grows from a seedling until it bears, then ripens a piece of food every
/// <see cref="FruitInterval"/> seconds — at its own kind's pace through the
/// year (see <see cref="SeasonPace"/>) and the weather's, faster by the
/// water — holding up to <see cref="MaxFruit"/> for its Farmers to pick.
/// Fruit nobody picks falls, free to anyone. When its clan is gone the crop
/// runs wild: it keeps bearing for everyone until it withers.
/// </summary>
public sealed class Crop
{
    /// <summary>A crop left without its clan withers this long after.</summary>
    public const float WildLifespan = 600f;

    /// <summary>Its footprint (m), for spacing and picking.</summary>
    public const float Radius = 0.45f;

    private static readonly Color LeafColor = new(55, 125, 50, 255);
    private static readonly Color WinterLeafColor = new(110, 100, 70, 255);
    private static readonly Color BerryColor = new(200, 35, 60, 255);
    private static readonly Color StalkColor = new(120, 160, 70, 255);
    private static readonly Color RipeStalkColor = new(205, 175, 90, 255);
    private static readonly Color SeedHeadColor = new(225, 190, 95, 255);
    private static readonly Color SoilColor = new(78, 58, 40, 255);
    private static readonly Color MushroomStemColor = new(235, 225, 205, 255);
    private static readonly Color MushroomCapColor = new(170, 95, 60, 255);
    private static readonly Color CressColor = new(90, 185, 70, 255);
    private static readonly Color CressSprigColor = new(160, 225, 110, 255);

    private float _fruitTimer;

    public Crop(Vector3 groundPoint, Guid? groupId, CropKind kind = CropKind.Berry)
    {
        Position = World.Grounded(groundPoint);
        GroupId = groupId;
        Kind = kind;
    }

    public Vector3 Position { get; }

    public CropKind Kind { get; }

    /// <summary>The clan that planted (and tends) it — null once it has gone wild.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Grows and bears faster (and shrugs off a drought) for standing near the water — see World.Farming.</summary>
    public bool IsWatered { get; set; }

    /// <summary>Seconds (at its pace) from planting to its first fruit.</summary>
    public float GrowSeconds => Kind switch
    {
        CropKind.Grain => 60f,
        CropKind.Mushroom => 70f,
        CropKind.Cress => 45f,
        _ => 90f,
    };

    /// <summary>Seconds (at its pace) between ripe pieces.</summary>
    public float FruitInterval => Kind switch
    {
        CropKind.Grain => 26f,
        CropKind.Mushroom => 45f,
        CropKind.Cress => 32f,
        _ => 40f,
    };

    /// <summary>It holds at most this many ripe; the next one falls.</summary>
    public int MaxFruit => Kind switch
    {
        CropKind.Grain => 6,
        CropKind.Mushroom or CropKind.Cress => 3,
        _ => 4,
    };

    /// <summary>What it bears.</summary>
    public FoodShardKind Yields => Kind switch
    {
        CropKind.Grain => FoodShardKind.Seed,
        CropKind.Mushroom => FoodShardKind.Mushroom,
        CropKind.Cress => FoodShardKind.Cress,
        _ => FoodShardKind.Berry,
    };

    /// <summary>"berry bush", "grain patch"… for the log.</summary>
    public string Name => NameOf(Kind);

    public static string NameOf(CropKind kind) => kind switch
    {
        CropKind.Grain => "grain patch",
        CropKind.Mushroom => "mushroom bed",
        CropKind.Cress => "cress bed",
        _ => "berry bush",
    };

    /// <summary>
    /// How fast each kind grows and bears through the year, relative to its
    /// normal pace: berries follow the lawn (see World.AbundanceOf), grain
    /// ripens from high summer into autumn, mushrooms like autumn and keep
    /// on a little through winter, and cress is best in spring.
    /// </summary>
    public static float SeasonPace(CropKind kind, Season season) => kind switch
    {
        CropKind.Grain => season switch { Season.Spring => 0.5f, Season.Summer => 1.3f, Season.Autumn => 1.3f, _ => 0f },
        CropKind.Mushroom => season switch { Season.Spring => 0.9f, Season.Summer => 0.5f, Season.Autumn => 1.4f, _ => 0.4f },
        CropKind.Cress => season switch { Season.Spring => 1.3f, Season.Summer => 1f, Season.Autumn => 0.8f, _ => 0.3f },
        _ => World.AbundanceOf(season),
    };

    /// <summary>Seconds it has grown so far, up to <see cref="GrowSeconds"/>.</summary>
    public float Growth { get; private set; }

    public bool IsMature => Growth >= GrowSeconds;

    /// <summary>Ripe pieces on it right now.</summary>
    public int Fruit { get; private set; }

    /// <summary>Seconds since it was planted.</summary>
    public float Age { get; set; }

    /// <summary>A crop wears out this long after planting, and its Farmers plant afresh: grain is sown every year, beds last two, a bush three.</summary>
    public float Lifespan => Kind switch
    {
        CropKind.Grain => 600f,
        CropKind.Mushroom or CropKind.Cress => 1200f,
        _ => 1800f,
    };

    public bool IsWornOut => Age >= Lifespan;

    /// <summary>Seconds it has been wild (see <see cref="WildLifespan"/>).</summary>
    public float WildSeconds { get; set; }

    public bool IsWithered => WildSeconds >= WildLifespan;

    /// <summary>Grows or ripens at <paramref name="pace"/> (see World.CropPace). Returns true if a piece just fell off a full crop.</summary>
    public bool Update(float deltaTime, float pace)
    {
        if (!IsMature)
        {
            Growth = MathF.Min(GrowSeconds, Growth + deltaTime * pace);
            return false;
        }

        _fruitTimer += deltaTime * pace;
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

    /// <summary>Seconds toward its next ripe piece, for saving.</summary>
    public float FruitTimer => _fruitTimer;

    /// <summary>Loading a saved world: puts back how grown and laden it was.</summary>
    public void Restore(float growth, int fruit, float fruitTimer, float wildSeconds)
    {
        Growth = growth;
        Fruit = fruit;
        _fruitTimer = fruitTimer;
        WildSeconds = wildSeconds;
    }

    /// <summary>A flood strips what's ripe.</summary>
    public void LoseFruit() => Fruit = 0;

    /// <summary>Picks one ripe piece. False if there's none.</summary>
    public bool TryPick()
    {
        if (Fruit <= 0)
            return false;
        Fruit--;
        return true;
    }

    /// <summary>The crop (smaller while young, browner in winter or once wild and withering), with what's ripe on it, and a stake in its clan's colour.</summary>
    public void Draw(bool winter, Color? groupColor)
    {
        float grown = Growth / GrowSeconds;
        float withering = GroupId is null ? Math.Clamp(WildSeconds / WildLifespan, 0f, 1f) : 0f;
        float size = Kind switch
        {
            CropKind.Berry => 0.25f + 0.25f * grown,
            _ => 0.2f + 0.25f * grown,
        };

        switch (Kind)
        {
            case CropKind.Grain:
                DrawGrain(size, winter, withering);
                break;
            case CropKind.Mushroom:
                DrawMushrooms(size);
                break;
            case CropKind.Cress:
                DrawCress(size, winter, withering);
                break;
            default:
                DrawBush(size, winter, withering);
                break;
        }

        // A little stake in its clan's colour marks whose it is.
        if (groupColor is { } color)
        {
            Vector3 stakeBase = Position + new Vector3(Radius + 0.1f, 0f, 0f);
            Raylib.DrawCylinderEx(stakeBase, stakeBase + new Vector3(0f, 0.45f, 0f), 0.02f, 0.02f, 4, new Color(120, 90, 60, 255));
            Raylib.DrawCube(stakeBase + new Vector3(0.06f, 0.4f, 0f), 0.12f, 0.08f, 0.02f, color);
        }
    }

    /// <summary>A round leafy clump dotted with its ripe berries.</summary>
    private void DrawBush(float size, bool winter, float withering)
    {
        Color leaves = Blend(winter ? WinterLeafColor : LeafColor, WinterLeafColor, withering);
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
    }

    /// <summary>A tuft of tall stalks, green while growing and golden once they bear, each ripe one nodding under a seed head; stubble in winter.</summary>
    private void DrawGrain(float size, bool winter, float withering)
    {
        float height = winter ? size * 0.5f : size * 2.2f;
        Color stalk = Blend(Fruit > 0 || winter ? RipeStalkColor : StalkColor, WinterLeafColor, withering);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * MathF.Tau / 6f + 0.3f;
            var foot = Position + new Vector3(MathF.Cos(angle) * 0.18f, 0f, MathF.Sin(angle) * 0.18f);
            Vector3 lean = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * height * 0.15f;
            Vector3 top = foot + lean + new Vector3(0f, height * (0.85f + 0.05f * (i % 3)), 0f);
            Raylib.DrawCylinderEx(foot, top, 0.025f, 0.015f, 4, stalk);
            if (i < Fruit)
                Raylib.DrawCylinderEx(top, top + lean * 0.5f + new Vector3(0f, 0.16f, 0f), 0.05f, 0.025f, 5, SeedHeadColor);
        }
    }

    /// <summary>A low mound of dark soil sprouting mushrooms — little white buttons while growing, one full cap for each that's ripe.</summary>
    private void DrawMushrooms(float size)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(Position.X, Position.Y, Position.Z);
        Rlgl.Scalef(1f, 0.35f, 1f);
        Raylib.DrawSphere(Vector3.Zero, Radius, SoilColor);
        Rlgl.PopMatrix();

        int shown = Math.Max(Fruit, IsMature ? 0 : 2);
        for (int i = 0; i < Math.Max(shown, 1); i++)
        {
            float angle = i * MathF.Tau / 3f + 0.8f;
            Vector3 foot = Position + new Vector3(MathF.Cos(angle) * 0.18f, Radius * 0.25f, MathF.Sin(angle) * 0.18f);
            bool ripe = i < Fruit;
            float stem = ripe ? 0.16f + size * 0.2f : 0.06f;
            float cap = ripe ? 0.13f + size * 0.12f : 0.05f;
            Raylib.DrawCylinderEx(foot, foot + new Vector3(0f, stem, 0f), 0.035f, 0.03f, 6, MushroomStemColor);
            Raylib.DrawCylinderEx(foot + new Vector3(0f, stem - 0.02f, 0f), foot + new Vector3(0f, stem + cap * 0.7f, 0f), cap, 0.01f, 10,
                ripe ? MushroomCapColor : MushroomStemColor);
        }
    }

    /// <summary>A low mat of round green leaves, a pale sprig standing up for each bunch that's ripe.</summary>
    private void DrawCress(float size, bool winter, float withering)
    {
        Color leaves = Blend(winter ? WinterLeafColor : CressColor, WinterLeafColor, withering);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * MathF.Tau / 6f;
            Vector3 at = Position + new Vector3(MathF.Cos(angle) * size * 0.7f, size * 0.25f, MathF.Sin(angle) * size * 0.7f);
            Raylib.DrawSphere(at, size * 0.4f, leaves);
        }
        Raylib.DrawSphere(Position + new Vector3(0f, size * 0.35f, 0f), size * 0.45f, leaves);
        for (int i = 0; i < Fruit; i++)
        {
            float angle = i * MathF.Tau / MaxFruit + 0.5f;
            Vector3 foot = Position + new Vector3(MathF.Cos(angle) * size * 0.4f, size * 0.4f, MathF.Sin(angle) * size * 0.4f);
            Raylib.DrawCylinderEx(foot, foot + new Vector3(0f, 0.22f, 0f), 0.02f, 0.02f, 4, CressSprigColor);
            Raylib.DrawSphere(foot + new Vector3(0f, 0.24f, 0f), 0.06f, CressSprigColor);
        }
    }

    private static Color Blend(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        (byte)255);
}
