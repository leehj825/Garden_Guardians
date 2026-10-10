using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>How often (s) each clan's people mark the ground they can see.</summary>
    private const float ExploreInterval = 1f;

    /// <summary>Everyone in a clan knows the ground within this far (m) of where they stand, a scout this much farther.</summary>
    private const float SightMarkRadius = 9f;
    public const float ScoutMarkRadius = 16f;

    /// <summary>A clan knows this far (m) around each of its homes from the start.</summary>
    private const float HomeMarkRadius = 22f;

    /// <summary>Scouts range this far (m) from home.</summary>
    public const float ScoutReach = 45f;

    /// <summary>A clan with its scouts out stops sending them once it knows this much of the garden.</summary>
    public const float ScoutingDoneFraction = 0.9f;

    /// <summary>Pioneers count unknown ground this much worse (score points) than known ground when picking a place to settle.</summary>
    private const float UnknownGroundPenalty = 18f;

    /// <summary>The far shore must lie at least this far (m) from every home of the clan to count as far.</summary>
    private const float FarShoreMinDistance = 20f;

    /// <summary>A clan reaching the far shore finds a cache of food there (the first clan ever, a bigger one).</summary>
    private const int FarShoreCache = 4, FarShoreFirstCache = 7;

    /// <summary>A clan that knows the far shore rates ground within this far (m) of it this much better for a new village.</summary>
    private const float FarShoreSettleReach = 14f, FarShoreSettleBonus = 12f;

    /// <summary>Ground within this far (m) of the spot the Guide pointed a clan to rates this much better for a new village.</summary>
    private const float GuidedSiteReach = 12f, GuidedSiteBonus = 45f;

    private bool _farShoreClaimed;

    /// <summary>Clans that have reached the pond's far side (for the headless report).</summary>
    public int FarShoresFound { get; private set; }

    private float _exploreTimer;

    /// <summary>Cells scouts newly mapped (for the headless report).</summary>
    public int CellsMapped { get; private set; }

    /// <summary>
    /// Each second every clan's people mark what they can see: the ground round its homes and round each
    /// member (a scout, farther). Milestones — a quarter, half, three quarters known — are set down for the chronicle.
    /// </summary>
    private void UpdateExploration(float deltaTime)
    {
        _exploreTimer -= deltaTime;
        if (_exploreTimer > 0f)
            return;
        _exploreTimer = ExploreInterval;

        foreach (KinGroup group in _groups.Values)
        {
            int before = group.Known.KnownCount;
            foreach (Shelter home in GroupHomes(group))
            {
                if (!home.IsCollapsed)
                    group.Known.Mark(home.Position, HomeMarkRadius);
            }
            foreach (Bramblekin member in group.Members)
            {
                if (member.IsDead)
                    continue;
                float radius = member.Job == KinJob.Scout ? ScoutMarkRadius : SightMarkRadius;
                int mapped = group.Known.Mark(member.Position, radius);
                if (member.Job == KinJob.Scout)
                    CellsMapped += mapped;
            }
            CheckFarShore(group);
            if (group.Known.KnownCount == before)
                continue;

            int milestone = (int)(group.Known.Fraction * 4f);
            if (milestone > group.ExploredMilestone && Knows(group, Craft.Exploration))
            {
                group.ExploredMilestone = milestone;
                if (milestone is >= 1 and <= 3)
                {
                    string share = milestone == 1 ? "a quarter" : milestone == 2 ? "half" : "three quarters";
                    Game.AddEventLog($"[EXPLORE] Scouts of {group.Title} have mapped {share} of the garden");
                    Chronicle($"Scouts of {group.Title} have mapped {share} of the garden", group);
                    Carve(group, $"scouts mapped {share} of the garden");
                }
            }
            else if (milestone > group.ExploredMilestone)
            {
                group.ExploredMilestone = milestone; // Seen by ordinary walking: quiet.
            }
        }
    }

    /// <summary>The pond's bank farthest from <paramref name="group"/>'s main home, a few metres back from the water — worked out once.</summary>
    public Vector3? FarShoreOf(KinGroup group)
    {
        if (group.FarShore is { } known)
            return known;
        Vector3[] shore = WaterMap.UsualShore;
        if (group.Home is not { } home || shore.Length == 0)
            return null;
        Vector3 centre = Vector3.Zero;
        foreach (Vector3 p in shore)
            centre += p;
        centre /= shore.Length;
        Vector3 far = shore[0];
        float best = -1f;
        foreach (Vector3 p in shore)
        {
            float d = GroundMover.HorizontalDistanceSquared(p, home.Position);
            if (d > best)
            {
                best = d;
                far = p;
            }
        }
        Vector3 outward = new(far.X - centre.X, 0f, far.Z - centre.Z);
        Vector3 site = outward.LengthSquared() > 0.01f ? far + Vector3.Normalize(outward) * 3f : far;
        if (!Terrain.Contains(site, 3f))
            site = far;
        group.FarShore = Grounded(site);
        return group.FarShore;
    }

    /// <summary>
    /// The pond's far side is a place to discover: the first time anyone of a clan knows the bank farthest
    /// from its home (and it really is far from every home), the clan finds a cache of food there — the first
    /// clan ever finds more — and thereafter counts it good ground for a new village.
    /// </summary>
    private void CheckFarShore(KinGroup group)
    {
        if (group.FarShoreFound || FarShoreOf(group) is not { } site || !group.Known.IsKnown(site))
            return;
        foreach (Shelter home in GroupHomes(group))
        {
            if (!home.IsCollapsed && GroundMover.HorizontalDistance(home.Position, site) < FarShoreMinDistance)
                return; // Home ground, not far at all.
        }

        group.FarShoreFound = true;
        FarShoresFound++;
        bool first = !_farShoreClaimed;
        _farShoreClaimed = true;
        ScatterFoodAround(site, first ? FarShoreFirstCache : FarShoreCache, 1.6f, FoodShardKind.Berry);
        QueueFloatingText(site, "Far shore!", FeastTextColor);
        string what = first ? "the first to reach the pond's far shore" : "reached the pond's far shore";
        Game.AddEventLog($"[EXPLORE] {group.CapitalTitle} {what}, and found a cache of food");
        Headline("Far shore", $"{group.CapitalTitle} {what}, and found a cache of food", site, false, group);
        Carve(group, $"{what}");
    }

    /// <summary>A place worth sending a scout: unknown ground within <see cref="ScoutReach"/> of <paramref name="home"/> that isn't water.</summary>
    public Vector3? ScoutTarget(KinGroup group, Vector3 from)
    {
        // Half the time, head for the far shore while it's still unseen.
        if (!group.FarShoreFound && FarShoreOf(group) is { } far && !group.Known.IsKnown(far) &&
            GroundMover.HorizontalDistance(far, from) <= ScoutReach + 20f && Rng.NextDouble() < 0.5)
            return far;
        if (group.Known.NearestUnknown(from, ScoutReach, Rng) is not { } target)
            return null;
        return Grounded(target);
    }

    /// <summary>How much of the garden <paramref name="group"/> knows, as a percentage.</summary>
    public static int ExploredPercent(KinGroup group) => (int)MathF.Round(group.Known.Fraction * 100f);

    /// <summary>How well a pioneer's clan knows <paramref name="candidate"/>: a penalty to add to its settling score.</summary>
    private static float UnknownPenalty(KinGroup? knowing, Vector3 candidate)
    {
        if (knowing is null)
            return 0f;
        float penalty = knowing.Known.IsKnown(candidate) ? 0f : UnknownGroundPenalty;
        if (knowing is { FarShoreFound: true, FarShore: { } far } && GroundMover.HorizontalDistance(far, candidate) <= FarShoreSettleReach)
            penalty -= FarShoreSettleBonus; // Good ground on the far bank, known from the scouts' visit.
        if (knowing.GuidedSite is { } guided && GroundMover.HorizontalDistance(guided, candidate) <= GuidedSiteReach)
            penalty -= GuidedSiteBonus; // The player has pointed the way.
        return penalty;
    }

    /// <summary>Greys out the ground the selected clan (or the selected Bramblekin's clan) hasn't been near — the Fog toggle.</summary>
    private void DrawFog(Camera3D camera)
    {
        KinGroup? clan = SelectedKin is { IsDead: false } kin ? GroupOf(kin) : SelectedClan;
        if (clan is null)
            return;
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        var fog = new Color(60, 60, 70, 150);
        const float e = KnownMap.CellSize / 2f;
        for (int cz = 0; cz < clan.Known.Cells; cz++)
        {
            for (int cx = 0; cx < clan.Known.Cells; cx++)
            {
                if (clan.Known.IsKnown(cx, cz))
                    continue;
                Vector3 c = clan.Known.CenterOf(cx, cz);
                if (!IsVisible(c, camera))
                    continue;
                DrawFogSquare(c.X - e, c.Z - e, KnownMap.CellSize, fog, 2);
            }
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    /// <summary>
    /// One square of fog laid over the ground from (<paramref name="x"/>, <paramref name="z"/>), <paramref name="size"/> metres a side. Where the ground
    /// is lumpy — a hill would poke through a flat square — it is split into four (up to <paramref name="splits"/> times), and what still pokes
    /// through is covered by lifting the square by the worst of it.
    /// </summary>
    private static void DrawFogSquare(float x, float z, float size, Color fog, int splits)
    {
        float h00 = GetHeightAt(x, z), h10 = GetHeightAt(x + size, z), h01 = GetHeightAt(x, z + size), h11 = GetHeightAt(x + size, z + size);
        float mid = size / 2f;
        // Bumps above the flat square: the centre, the edges' middles, and the points between.
        float worst = 0f;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (i is 0 or 2 && j is 0 or 2)
                    continue;
                float u = i / 2f, v = j / 2f;
                float flat = h00 * (1 - u) * (1 - v) + h10 * u * (1 - v) + h01 * (1 - u) * v + h11 * u * v;
                worst = MathF.Max(worst, GetHeightAt(x + u * size, z + v * size) - flat);
            }
        }
        if (worst > 0.08f && splits > 0)
        {
            DrawFogSquare(x, z, mid, fog, splits - 1);
            DrawFogSquare(x + mid, z, mid, fog, splits - 1);
            DrawFogSquare(x, z + mid, mid, fog, splits - 1);
            DrawFogSquare(x + mid, z + mid, mid, fog, splits - 1);
            return;
        }
        float lift = 0.12f + worst + (ProceduralView.GroundTolerance);
        var a = new Vector3(x, h00 + lift, z);
        var b = new Vector3(x + size, h10 + lift, z);
        var d = new Vector3(x, h01 + lift, z + size);
        var f = new Vector3(x + size, h11 + lift, z + size);
        Raylib.DrawTriangle3D(a, f, b, fog);
        Raylib.DrawTriangle3D(a, d, f, fog);
    }

    // --- Rafts ---------------------------------------------------------------------------------

    /// <summary>Raft crossings made and rafts capsized (for the headless report).</summary>
    public int RaftCrossings { get; private set; }
    public int RaftMishaps { get; private set; }

    /// <summary>The point on the pond's usual bank nearest <paramref name="point"/>, a metre back from the water, or null if the pond has no bank.</summary>
    public Vector3? NearestShore(Vector3 point)
    {
        Vector3[] shore = WaterMap.UsualShore;
        if (shore.Length == 0)
            return null;
        Vector3 centre = Vector3.Zero;
        Vector3 nearest = shore[0];
        float best = float.MaxValue;
        foreach (Vector3 p in shore)
        {
            centre += p;
            float d = GroundMover.HorizontalDistanceSquared(p, point);
            if (d < best)
            {
                best = d;
                nearest = p;
            }
        }
        centre /= shore.Length;
        var outward = new Vector3(nearest.X - centre.X, 0f, nearest.Z - centre.Z);
        return Grounded(outward.LengthSquared() > 0.01f ? nearest + Vector3.Normalize(outward) * 1f : nearest);
    }

    public void NoteRaftLaunch(Bramblekin scout)
    {
        QueueFloatingText(scout.Position, "Raft!", FeastTextColor);
        if (RaftCrossings == 0)
            Game.AddEventLog($"[EXPLORE] {scout.Name} of {GroupOf(scout)?.Title ?? "a clan"} set out across the pond on a raft");
    }

    public void NoteRaftCrossing(Bramblekin scout)
    {
        RaftCrossings++;
        if (RaftCrossings == 1 && GroupOf(scout) is { } group)
        {
            Headline("Raft", $"{scout.Name} of {group.Title} crossed the pond on a raft", scout.Position, false, group);
            Carve(group, $"{scout.Name} crossed the pond on a raft");
        }
    }

    public void NoteRaftMishap(Bramblekin scout)
    {
        RaftMishaps++;
        QueueFloatingText(scout.Position, "Capsized!", HostileTextColor);
    }

    private static readonly Color RaftLogColor = new(150, 110, 70, 255);

    /// <summary>A raft under each scout poling across: logs lashed side by side on the water, and the scout standing on it.</summary>
    private void DrawRafts()
    {
        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsDead || !kin.IsOnRaft)
                continue;
            Vector3 p = new(kin.Position.X, WaterMap.SurfaceHeight + 0.04f, kin.Position.Z);
            for (int i = -2; i <= 2; i++)
                Raylib.DrawCube(p + new Vector3(i * 0.17f, 0f, 0f), 0.15f, 0.1f, 0.9f, RaftLogColor);
            Raylib.DrawCube(p + new Vector3(0f, 0.07f, -0.25f), 0.9f, 0.05f, 0.06f, RaftLogColor);
            Raylib.DrawCube(p + new Vector3(0f, 0.07f, 0.25f), 0.9f, 0.05f, 0.06f, RaftLogColor);
            Color body = GroupOf(kin)?.Color ?? new Color(190, 140, 90, 255);
            Detail.Sphere(p + new Vector3(0f, 0.3f, 0f), 0.2f, body);
            Detail.Sphere(p + new Vector3(0f, 0.6f, 0f), 0.13f, new Color(235, 215, 185, 255));
        }
    }
}
