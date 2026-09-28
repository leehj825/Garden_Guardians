using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Rendering -----------------------------------------------------------------------

    /// <summary>
    /// Raylib Culling: margin (px) added around the screen rectangle when
    /// deciding whether a projected point is "on screen" for
    /// <see cref="IsOnScreen"/> — generous enough that an entity's body
    /// doesn't visibly pop in right at the screen edge.
    /// </summary>
    private const float CullScreenMargin = 40f;

    /// <summary>Basic bounds check: true unless <paramref name="worldPosition"/> projects to a screen point entirely outside the camera's current viewport (plus <see cref="CullScreenMargin"/>).</summary>
    private static bool IsOnScreen(Vector3 worldPosition, Camera3D camera)
    {
        Vector2 screen = Raylib.GetWorldToScreen(worldPosition, camera);
        return screen.X >= -CullScreenMargin && screen.X <= Raylib.GetScreenWidth() + CullScreenMargin &&
               screen.Y >= -CullScreenMargin && screen.Y <= Raylib.GetScreenHeight() + CullScreenMargin;
    }

    /// <summary>
    /// Frustum Culling: anything whose ground point projects off screen
    /// (see <see cref="IsOnScreen"/>) isn't drawn. There's no draw distance:
    /// zoomed out, the whole garden shows. Nothing culled here is ever gated
    /// in Update — every entity keeps simulating whatever the camera sees.
    /// </summary>
    private static bool IsVisible(Vector3 worldPosition, Camera3D camera) => IsOnScreen(worldPosition, camera);

    public void Draw(Camera3D camera)
    {
        Detail.BeginFrame(camera);
        var (seasonTint, seasonAmount) = SeasonTint;
        Terrain.Draw(seasonTint, seasonAmount);
        DrawTerritories(camera);
        DrawOak();
        for (int i = _splats.Count - 1; i >= 0; i--)
        {
            var (position, timeLeft) = _splats[i];
            // A dark stain that fades out.
            byte alpha = (byte)(200 * Math.Clamp(timeLeft / 2f, 0f, 1f));
            Raylib.DrawCylinder(position + new Vector3(0, 0.012f, 0), 0.9f, 0.9f, 0.005f, 20, new Color(30, 25, 20, (int)alpha));
        }

        for (int i = GardenProps.Count - 1; i >= 0; i--)
        {
            GardenProp prop = GardenProps[i];
            if (IsVisible(prop.Position, camera))
                prop.Draw();
        }

        foreach (Shelter shelter in Shelters)
        {
            if (!IsVisible(shelter.Position, camera))
                continue;
            Color? flag = shelter.GroupId is { } groupId && _groups.TryGetValue(groupId, out KinGroup? owner) ? owner.Color : null;
            shelter.Draw(flag);
        }

        DrawRelations(camera);
        DrawRain(camera);
        bool winter = CurrentSeason == Season.Winter;
        foreach (Crop bush in Crops)
        {
            if (!IsVisible(bush.Position, camera))
                continue;
            Color? stake = bush.GroupId is { } bushGroup && _groups.TryGetValue(bushGroup, out KinGroup? farmer) ? farmer.Color : null;
            bush.Draw(winter, stake);
        }
        DrawSnares(camera);
        DrawPens(camera);

        foreach (Twig twig in Twigs)
        {
            if (twig.IsActive && !twig.IsCarried && IsVisible(twig.Position, camera))
                twig.Draw();
        }
        DrawMaterials(camera);
        DrawWells(camera);

        // Object Pooling: most Food slots sit inactive at any given time, so
        // every loop over the pool must skip anything with IsActive false.
        for (int i = FoodShards.Count - 1; i >= 0; i--)
        {
            FoodShard food = FoodShards[i];
            if (food.IsActive && !food.IsCarried && IsVisible(food.Position, camera))
                food.Draw(food.Position);
        }

        // Reverse for-loops, skipping anything marked dead this frame: its
        // removal is deferred, so without the check a creature killed a
        // moment ago would still be drawn standing there.
        for (int i = Hornets.Count - 1; i >= 0; i--)
        {
            if (!Hornets[i].IsDead && IsVisible(Hornets[i].Position, camera))
                Hornets[i].Draw();
        }

        for (int i = Grubs.Count - 1; i >= 0; i--)
        {
            if (!Grubs[i].IsDead && IsVisible(Grubs[i].Position, camera))
                Grubs[i].Draw();
        }

        for (int i = Beetles.Count - 1; i >= 0; i--)
        {
            if (!Beetles[i].IsDead && IsVisible(Beetles[i].Position, camera))
                Beetles[i].Draw();
        }

        DrawAnts(camera);
        DrawPondLife(camera);
        DrawOwl(camera);

        // Group tethers: a faint line in the group's colour from every
        // follower's head to its Leader's, so who runs with whom reads at a
        // glance.
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Leader is not { IsDead: false } leader)
                continue;

            Vector3 leaderHead = leader.Position + new Vector3(0, Bramblekin.BodyHeight, 0);
            var tether = new Color(group.Color.R, group.Color.G, group.Color.B, (byte)120);
            foreach (Bramblekin member in group.Members)
            {
                if (member == leader || member.IsDead)
                    continue;
                Raylib.DrawLine3D(member.Position + new Vector3(0, Bramblekin.BodyHeight, 0), leaderHead, tether);
            }
        }

        for (int i = Colony.Count - 1; i >= 0; i--)
        {
            Bramblekin b = Colony[i];
            if (!b.IsDead && IsVisible(b.Position, camera))
                b.Draw(this);
        }

        if (Spider is { IsDead: false } spider)
            spider.Draw();
        DrawPebbles();

        DrawWater();
        DrawCreek();

        // Kin Inspector: ring the selected Bramblekin, and trace its
        // Intelligence-scaled detection radius over the hills.
        if (SelectedKin is { IsDead: false } selected)
        {
            DrawTerrainRing(selected.Position, 0.5f, new Color(255, 230, 60, 255));
            DrawTerrainRing(selected.Position, selected.DetectionRadius, new Color(255, 255, 255, 140));
        }
    }

    // --- Territory ---------------------------------------------------------------------------

    /// <summary>A village's territory reaches this far past its outermost home…</summary>
    private const float TerritoryMargin = 4f;

    /// <summary>…and at least this far from its main home.</summary>
    private const float MinTerritoryRadius = 6f;

    /// <summary>
    /// Every village's ground, faintly washed in its clan's colour with a
    /// stronger rim — so who lives where reads at a glance. Drawn without
    /// writing depth, so it never hides the berries and twigs lying on it.
    /// </summary>
    private void DrawTerritories(Camera3D camera)
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Home is not { IsCollapsed: false } home)
                continue;
            float radius = MinTerritoryRadius;
            foreach (Shelter shelter in GroupHomes(group))
                radius = MathF.Max(radius, GroundMover.HorizontalDistance(home.Position, shelter.Position) + TerritoryMargin);
            DrawTerrainBand(home.Position, 0f, radius - 0.5f, group.Color with { A = 26 });
            DrawTerrainBand(home.Position, radius - 0.5f, radius, group.Color with { A = 110 });
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    /// <summary>The ring between <paramref name="inner"/> and <paramref name="outer"/> around <paramref name="center"/>, filled, following the terrain's height.</summary>
    private static void DrawTerrainBand(Vector3 center, float inner, float outer, Color color)
    {
        const int segments = 40;
        int rings = Math.Max(1, (int)MathF.Ceiling((outer - inner) / 3f)); // Short steps across, so the fill hugs the hills.
        for (int r = 0; r < rings; r++)
        {
            float r0 = inner + (outer - inner) * r / rings;
            float r1 = inner + (outer - inner) * (r + 1) / rings;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * MathF.Tau / segments, a1 = (i + 1) * MathF.Tau / segments;
                Vector3 p00 = Around(r0, a0), p01 = Around(r0, a1), p10 = Around(r1, a0), p11 = Around(r1, a1);
                if (!OnMap(p00) || !OnMap(p01) || !OnMap(p10) || !OnMap(p11))
                    continue; // Nothing drawn off the edge of the garden.
                Raylib.DrawTriangle3D(p00, p11, p10, color);
                if (r0 > 0f)
                    Raylib.DrawTriangle3D(p00, p01, p11, color);
            }
        }

        Vector3 Around(float radius, float angle) =>
            Grounded(center + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius), 0.06f);

        static bool OnMap(Vector3 p) => MathF.Abs(p.X) <= 50f && MathF.Abs(p.Z) <= 50f;
    }

    /// <summary>A circle of <paramref name="radius"/> around <paramref name="center"/>, drawn as line segments that follow the terrain's height.</summary>
    private static void DrawTerrainRing(Vector3 center, float radius, Color color)
    {
        const int segments = 64;
        Vector3 previous = Grounded(center + new Vector3(radius, 0f, 0f), 0.08f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * MathF.Tau / segments;
            Vector3 point = Grounded(center + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius), 0.08f);
            Raylib.DrawLine3D(previous, point, color);
            previous = point;
        }
    }
}
