using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The player has no lever on the world — no miracles, no factions to
/// command. The only tap left inspects: a Bramblekin (see
/// <see cref="World.TrySelectAt"/>), whose Personality, needs and
/// relationships then show in the Kin Inspector panel, or — tapping one of
/// its homes — a clan, shown on a clan card.
/// </summary>
public sealed class WorldTapInput
{
    /// <summary>
    /// One-Finger Panning claimed the left mouse button/primary touch for
    /// the Spectator Camera (see <see cref="TouchCameraController"/>), so a
    /// press that turns into a drag past this many pixels is a pan, not a
    /// tap — <see cref="HandlePress"/> only fires on release, and only if
    /// the press never crossed this threshold.
    /// </summary>
    private const float TapDragThreshold = 12f;

    private Vector2 _pressStartPosition;
    private bool _isPressing;
    private bool _exceededDragThreshold;

    /// <summary>Polls the mouse/touch and handles a clean tap-and-release, if one just finished.</summary>
    public void Update(Camera3D camera, World world)
    {
        // Raylib maps a primary touch to the left mouse button, so the same
        // code path serves desktop clicks and phone taps.
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _isPressing = true;
            _exceededDragThreshold = false;
            _pressStartPosition = Raylib.GetMousePosition();
        }
        else if (_isPressing && Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            if (!_exceededDragThreshold && Vector2.Distance(Raylib.GetMousePosition(), _pressStartPosition) > TapDragThreshold)
                _exceededDragThreshold = true;
        }
        else if (_isPressing && Raylib.IsMouseButtonReleased(MouseButton.Left))
        {
            _isPressing = false;
            if (!_exceededDragThreshold)
                HandlePress(Raylib.GetMousePosition(), camera, world);
        }
    }

    /// <summary>
    /// Handles a completed tap at <paramref name="screenPosition"/> — called
    /// from <see cref="Update"/> on release, once it's confirmed the press
    /// never turned into a pan. Public so input can be driven directly, from
    /// a test harness or an alternate input source.
    /// </summary>
    public void HandlePress(Vector2 screenPosition, Camera3D camera, World world)
    {
        if (PickGround(camera, world.Terrain, screenPosition) is { } tapGround)
            world.TrySelectAt(tapGround);
    }

    /// <summary>
    /// Casts a ray from the camera through the given screen position and
    /// returns where it meets the terrain (or null if it misses the terrain,
    /// or the terrain's edge).
    ///
    /// Guards against every way this can go wrong on a phone: a touch
    /// reported before the window/surface has a real size yet (e.g. mid
    /// rotation, or the first frame or two after Android hands the activity
    /// its window), a tap slightly outside the rendered viewport, or a
    /// screen position that is already NaN/Infinity. Any of those would make
    /// GetScreenToWorldRay's projection math hand back a garbage ray; none of
    /// them should ever crash the raycast or the tap that triggered it.
    /// </summary>
    internal static Vector3? PickGround(Camera3D camera, Terrain terrain, Vector2 screenPosition)
    {
        Ray? ray = SafeScreenRay(screenPosition, camera);
        return ray is null ? null : terrain.Raycast(ray.Value);
    }

    /// <summary>
    /// The bounds/NaN/Infinity guards shared by <see cref="PickGround"/>:
    /// validates the screen position and the window before asking raylib to
    /// project it, and validates the ray it gets back.
    /// </summary>
    private static Ray? SafeScreenRay(Vector2 screenPosition, Camera3D camera)
    {
        int width = Raylib.GetScreenWidth();
        int height = Raylib.GetScreenHeight();
        if (width <= 0 || height <= 0)
            return null;

        if (!IsFinite(screenPosition) ||
            screenPosition.X < 0 || screenPosition.X > width ||
            screenPosition.Y < 0 || screenPosition.Y > height)
            return null;

        // GetScreenToWorldRay is raylib 5.5's name for GetMouseRay (the old
        // name still exists but is marked obsolete in Raylib-cs 8).
        Ray ray = Raylib.GetScreenToWorldRay(screenPosition, camera);
        return IsFinite(ray.Position) && IsFinite(ray.Direction) ? ray : null;
    }

    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
