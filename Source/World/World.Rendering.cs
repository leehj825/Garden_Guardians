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
    /// Frustum/Distance Culling: nothing culled from drawing here is ever
    /// gated in Update — every entity keeps simulating regardless of what
    /// the camera can see. Radius (m), measured in 2D (X/Z) from
    /// <see cref="Camera3D.Target"/>, beyond which things simply aren't drawn.
    /// </summary>
    public const float RenderRadius = 60.0f;

    /// <summary>True if <paramref name="worldPosition"/> is within <see cref="RenderRadius"/> (2D, X/Z) of the camera's target.</summary>
    private static bool IsWithinRenderRadius(Vector3 worldPosition, Camera3D camera)
    {
        float dx = worldPosition.X - camera.Target.X;
        float dz = worldPosition.Z - camera.Target.Z;
        return dx * dx + dz * dz <= RenderRadius * RenderRadius;
    }

    private bool IsVisible(Vector3 worldPosition, Camera3D camera) =>
        IsWithinRenderRadius(worldPosition, camera) && IsOnScreen(worldPosition, camera);

    public void Draw(Camera3D camera)
    {
        Terrain.Draw(camera.Target, RenderRadius);
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

        foreach (Twig twig in Twigs)
        {
            if (twig.IsActive && !twig.IsCarried && IsVisible(twig.Position, camera))
                twig.Draw();
        }

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
                if (member == leader || member.IsDead || !IsWithinRenderRadius(member.Position, camera))
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

        // Kin Inspector: ring the selected Bramblekin, and trace its
        // Intelligence-scaled detection radius over the hills.
        if (SelectedKin is { IsDead: false } selected)
        {
            DrawTerrainRing(selected.Position, 0.5f, new Color(255, 230, 60, 255));
            DrawTerrainRing(selected.Position, selected.DetectionRadius, new Color(255, 255, 255, 140));
        }
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
