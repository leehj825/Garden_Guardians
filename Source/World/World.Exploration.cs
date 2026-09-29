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

    /// <summary>A place worth sending a scout: unknown ground within <see cref="ScoutReach"/> of <paramref name="home"/> that isn't water.</summary>
    public Vector3? ScoutTarget(KinGroup group, Vector3 from)
    {
        if (group.Known.NearestUnknown(from, ScoutReach, Rng) is not { } target)
            return null;
        return Grounded(target);
    }

    /// <summary>How much of the garden <paramref name="group"/> knows, as a percentage.</summary>
    public static int ExploredPercent(KinGroup group) => (int)MathF.Round(group.Known.Fraction * 100f);

    /// <summary>How well a pioneer's clan knows <paramref name="candidate"/>: a penalty to add to its settling score.</summary>
    private static float UnknownPenalty(KinGroup? knowing, Vector3 candidate) =>
        knowing is not null && !knowing.Known.IsKnown(candidate) ? UnknownGroundPenalty : 0f;

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
        for (int cz = 0; cz < KnownMap.Cells; cz++)
        {
            for (int cx = 0; cx < KnownMap.Cells; cx++)
            {
                if (clan.Known.IsKnown(cx, cz))
                    continue;
                Vector3 c = KnownMap.CenterOf(cx, cz);
                if (!IsVisible(c, camera))
                    continue;
                Vector3 a = Grounded(c + new Vector3(-e, 0f, -e), 0.12f), b = Grounded(c + new Vector3(e, 0f, -e), 0.12f);
                Vector3 d = Grounded(c + new Vector3(-e, 0f, e), 0.12f), f = Grounded(c + new Vector3(e, 0f, e), 0.12f);
                Raylib.DrawTriangle3D(a, f, b, fog);
                Raylib.DrawTriangle3D(a, d, f, fog);
            }
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }
}
