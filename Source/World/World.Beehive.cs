using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- The beehive in the oak --------------------------------------------------------------

    /// <summary>The hive holds at most this many combs of honey…</summary>
    public const int MaxHoney = 8;

    /// <summary>…and the bees make one about this often (s), from spring to autumn.</summary>
    private const float HoneySeconds = 45f;

    /// <summary>Odds that taking a comb rouses the bees…</summary>
    private const double RouseChance = 0.55;

    /// <summary>…and for a clan that smokes them out first (see <see cref="Craft.Smoking"/>).</summary>
    private const double SmokedRouseChance = 0.1;

    /// <summary>A comb of honey fills this much more than any other food.</summary>
    public const float HoneyNourishmentBonus = 20f;

    /// <summary>A comb carried home counts as this many pieces in the store.</summary>
    private const int HoneyStoreValue = 2;

    private static readonly Color HiveColor = new(205, 160, 80, 255);
    private static readonly Color HiveBandColor = new(160, 115, 50, 255);
    private static readonly Color HoneyDripColor = new(240, 175, 40, 255);
    private static readonly Color HoneyTextColor = new(220, 150, 30, 255);

    private float _honeyTimer = HoneySeconds;

    /// <summary>Combs of honey in the hive right now.</summary>
    public int HiveHoney { get; private set; } = 3;

    public List<BeeSwarm> Swarms { get; } = new();

    public int HoneyTaken { get; private set; }
    public int SwarmsRoused { get; private set; }
    public int BeeStings { get; private set; }
    public int BeesSwatted { get; private set; }
    public int HoneyGifts { get; private set; }

    /// <summary>Which way the hive faces: out from the trunk where it is bare of roots and dry underfoot (found in the model).</summary>
    private static Vector3 HiveFacing => new(MathF.Cos(TerrainData.HiveAngle), 0f, MathF.Sin(TerrainData.HiveAngle));

    /// <summary>How far from the oak's centre the trunk's surface is, that way.</summary>
    private static float HiveSurface => TerrainData.HiveSurface;

    /// <summary>The hive itself, hanging on the trunk a little above a Bramblekin's reach.</summary>
    public static Vector3 HivePosition => OakCenter + HiveFacing * (HiveSurface + 0.35f) + new Vector3(0f, 2.6f, 0f);

    /// <summary>Where a honey-taker stands to climb up to it.</summary>
    public static Vector3 HiveFoot
    {
        get
        {
            // Step out from the trunk until clear of roots (solid to walkers) and out of the water.
            float reach = HiveSurface + 0.9f;
            Vector3 foot = Grounded(OakCenter + HiveFacing * reach);
            for (int i = 0; i < 80 && (IsOnOak(foot, 0.7f) || IsWater(foot)); i++)
            {
                reach += 0.5f;
                foot = Grounded(OakCenter + HiveFacing * reach);
            }
            return foot;
        }
    }

    /// <summary>The bees make honey from spring to autumn; roused swarms chase and sting.</summary>
    private void UpdateBeehive(float deltaTime)
    {
        if (CurrentSeason != Season.Winter && HiveHoney < MaxHoney)
        {
            _honeyTimer -= deltaTime * (CurrentSeason == Season.Summer ? 1.3f : 1f);
            if (_honeyTimer <= 0f)
            {
                _honeyTimer = HoneySeconds;
                HiveHoney++;
            }
        }
        for (int i = Swarms.Count - 1; i >= 0; i--)
        {
            if (!Swarms[i].Update(deltaTime, this))
                Swarms.RemoveAt(i);
        }
    }

    /// <summary>True if there's honey to be had right now: daylight, the bees awake, and combs in the hive.</summary>
    public bool HoneyToHave => HiveHoney > 0 && !IsNight && CurrentSeason != Season.Winter;

    /// <summary>
    /// <paramref name="taker"/>, up at the hive, takes a comb — in hand, to
    /// carry home. The bees may rouse and chase it (much less likely if its
    /// clan knows to smoke them out and has a lit hearth to take the smoke
    /// from). Null if the hive is empty.
    /// </summary>
    public FoodShard? TakeHoney(Bramblekin taker)
    {
        if (HiveHoney <= 0 || ActivateFood(taker.Position, FoodShardKind.Honey) is not { } comb)
            return null;
        HiveHoney--;
        HoneyTaken++;
        PickUpFood(comb);
        KinGroup? clan = GroupOf(taker);
        bool smoked = clan is not null && Knows(clan, Craft.Smoking) && GroupHomes(clan).Any(h => h.IsHearthLit);
        if (clan is not null)
            clan.HoneyTaken++;
        if (Rng.NextDouble() < (smoked ? SmokedRouseChance : RouseChance))
        {
            Swarms.Add(new BeeSwarm(HivePosition, taker));
            SwarmsRoused++;
            QueueFloatingText(HivePosition, "Bees!", HostileTextColor);
            Game.AddEventLog($"[WILD] {taker.Name} took honey from the hive in the oak - and roused the bees");
            Spotlight($"{taker.Name} runs from the bees", 6.5f, taker.Position, taker);
        }
        else
        {
            QueueFloatingText(HivePosition, smoked ? "Smoked out: honey!" : "Honey!", HoneyTextColor);
            Spotlight($"{taker.Name} braves the hive for honey", 4.5f, taker.Position, taker);
        }
        return comb;
    }

    /// <summary>At each Leader decision, a clan living within reach of the hive sends its boldest for a comb with these odds, when there's honey to have.</summary>
    private const double HoneyErrandChance = 0.15;

    /// <summary>A Leader sends its boldest free member up the oak for honey, now and then.</summary>
    private void SendForHoney(KinGroup group)
    {
        if (!HoneyToHave || group.Goal is GroupGoal.Defend or GroupGoal.Raid || Rng.NextDouble() >= HoneyErrandChance)
            return;
        Bramblekin? boldest = null;
        foreach (Bramblekin member in group.Members)
        {
            if (member.Job != KinJob.Guard && member.CanGoForHoney(this) &&
                (boldest is null || member.Personality.Courage > boldest.Personality.Courage))
                boldest = member;
        }
        boldest?.GoForHoney();
    }

    /// <summary>A comb of honey stored counts double (see <see cref="HoneyStoreValue"/>).</summary>
    private static void StoreHoneyExtra(Shelter shelter, FoodShard food)
    {
        if (food.Kind != FoodShardKind.Honey)
            return;
        for (int i = 1; i < HoneyStoreValue; i++)
            shelter.TryDeposit();
    }

    public void NoteBeeSting() => BeeStings++;
    public void NoteBeeSwatted() => BeesSwatted++;
    public void NoteHoneyGift() => HoneyGifts++;

    /// <summary>The nearest roused swarm within <paramref name="radius"/> of <paramref name="from"/>, if any.</summary>
    public BeeSwarm? NearestSwarm(Vector3 from, float radius)
    {
        BeeSwarm? best = null;
        float bestDistance = radius * radius;
        foreach (BeeSwarm swarm in Swarms)
        {
            if (swarm.IsDead)
                continue;
            float distance = GroundMover.HorizontalDistanceSquared(from, swarm.Position);
            if (distance <= bestDistance)
            {
                best = swarm;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>A papery hive on the trunk, banded, with honey dripping from it while it's full enough — and any roused swarms.</summary>
    private void DrawBeehive(Camera3D camera)
    {
        if (IsVisible(HivePosition, camera))
        {
            Vector3 hive = HivePosition;
            Rlgl.PushMatrix();
            Rlgl.Translatef(hive.X, hive.Y, hive.Z);
            Rlgl.Scalef(0.55f, 0.75f, 0.55f);
            Raylib.DrawSphereEx(Vector3.Zero, 1f, 8, 12, HiveColor);
            Rlgl.PopMatrix();
            for (int band = -1; band <= 1; band++)
                Raylib.DrawCylinder(hive + new Vector3(0f, band * 0.25f - 0.03f, 0f), 0.56f - MathF.Abs(band) * 0.12f, 0.56f - MathF.Abs(band) * 0.12f, 0.06f, 12, HiveBandColor);
            Raylib.DrawCircle3D(hive + HiveFacing * 0.52f - new Vector3(0f, 0.3f, 0f), 0.09f, new Vector3(0f, 1f, 0f), MathF.Atan2(HiveFacing.X, HiveFacing.Z) * 180f / MathF.PI, new Color(40, 30, 20, 255));
            if (HiveHoney >= 4)
                Detail.Sphere(hive - new Vector3(0f, 0.8f + 0.05f * MathF.Sin(ElapsedSeconds * 2f), 0f), 0.06f, HoneyDripColor);
        }
        foreach (BeeSwarm swarm in Swarms)
        {
            if (!swarm.IsDead && IsVisible(swarm.Position, camera))
                swarm.Draw(ElapsedSeconds);
        }
    }
}
