using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The Spectator Camera: a Google Maps-style controller for the fixed
/// overhead view — Pure Simulation means the player has no lever on the
/// world any more, just on how they're looking at it. One finger (or a
/// held left mouse button, for testing on desktop) drags to pan across the
/// terrain's X/Z plane; two fingers twisting around each other rotates the
/// whole world around the camera's own Target on the Y axis; two fingers
/// pinching in/out zooms; and two fingers sliding up or down together
/// tilts the camera's pitch. <see cref="WorldTapInput"/> still gets a
/// clean, undragged tap for inspecting a Bramblekin — see its own
/// drag-threshold check — so this and that never fight over the same
/// touch.
/// </summary>
public sealed class TouchCameraController
{
    /// <summary>Closest the camera may zoom in, in meters from its Target.</summary>
    private const float MinZoomDistance = 8f;

    /// <summary>
    /// Furthest the camera may zoom out, in meters from its Target: just far
    /// enough that the whole garden (the circle through its corners) fits
    /// on screen whichever way the camera faces — no further, so zooming
    /// out never leaves the garden a speck. Worked out from the screen's
    /// shape each time, so a tall phone screen can pull back further than a
    /// wide one; never nearer than <see cref="MinOverviewDistance"/>, the
    /// starting overview's distance (see Game.Run's initial Camera3D), so
    /// the Map button's view is always reachable by pinching too.
    /// </summary>
    private static float MaxZoomDistance(Camera3D camera, float worldHalfSize)
    {
        float halfFovY = camera.FovY * MathF.PI / 360f;
        float aspect = Raylib.GetScreenWidth() / (float)Math.Max(1, Raylib.GetScreenHeight());
        float halfFovX = MathF.Atan(MathF.Tan(halfFovY) * aspect);
        float gardenRadius = worldHalfSize * MathF.Sqrt(2f);
        return MathF.Max(MinOverviewDistance, gardenRadius / MathF.Sin(MathF.Min(halfFovY, halfFovX)));
    }

    /// <summary>The starting overview's distance from its Target (~156m), which zooming out can always reach.</summary>
    private const float MinOverviewDistance = 160f;

    /// <summary>Each notch of a mouse wheel zooms by this fraction of the current distance (desktop).</summary>
    private const float WheelZoomFraction = 0.12f;

    /// <summary>How many meters of pinch-distance change it takes to move the camera one meter.</summary>
    private const float PinchZoomSensitivity = 0.05f;

    /// <summary>Keeps the Target from panning off the playable terrain, in meters from its edge.</summary>
    private const float PanEdgeMargin = 5f;

    /// <summary>
    /// Camera Sensitivity Tuning: the single knob on One-Finger Panning's
    /// overall feel — multiplies the already-distance-scaled screen delta
    /// (see <see cref="Pan"/>) before it's ever added to camera.Position/
    /// camera.Target. Turn this down if panning still feels too fast at
    /// every zoom level, up if it feels sluggish.
    /// </summary>
    private const float PanSensitivity = 0.12f;

    /// <summary>
    /// Camera Sensitivity Tuning: the single knob on Two-Finger Rotation's
    /// overall feel — multiplies the raw angle delta between the two touch
    /// points (see <see cref="Rotate"/>) before it's applied. Kept low: the
    /// raw angle between two close-together fingers swings wildly for even
    /// a small physical movement, so without this a twist gesture rotates
    /// the world far more than the fingers actually moved.
    /// </summary>
    private const float RotationSensitivity = 0.3f;

    // Camera-gesture state, tracked frame to frame. One controller is
    // constructed once in Game.Run and lives for the whole session, so
    // instance fields here serve exactly the same purpose static fields
    // would in a single long-running loop, without reaching for actual
    // global/static mutable state.
    private Vector2 _lastTouchPos;
    private bool _isOneFingerGesture;

    /// <summary>Pixels the current one-finger gesture has travelled — see <see cref="DraggedThisGesture"/>.</summary>
    private float _gesturePanPixels;

    /// <summary>A one-finger drag this far (px) is a pan rather than a tap — the same threshold <see cref="WorldTapInput"/> uses.</summary>
    private const float PanThresholdPixels = 12f;

    /// <summary>True once the current one-finger press has dragged far enough to count as a pan (it stops the <see cref="FollowCamera"/>).</summary>
    public bool DraggedThisGesture => _isOneFingerGesture && _gesturePanPixels > PanThresholdPixels;

    private float _lastTouchAngle;
    private float _lastPinchDistance;
    private float _lastTwoFingerMidpointY;
    private bool _isTwoFingerGesture;

    // The Flip Fix: raylib reports touch points by index (0, 1, ...), but
    // which physical finger gets which index is NOT stable frame to frame —
    // the OS/driver can silently swap them mid-gesture. Since the angle
    // between the two points flips by ~180° the instant "first" and
    // "second" swap (Atan2 of a negated vector), that swap alone was
    // enough to make the world appear to suddenly flip during a twist —
    // and, because a real vertical two-finger drag is never perfectly
    // symmetric, during a tilt too. UpdateTwoFingerGesture instead matches
    // this frame's two points to whichever of last frame's it's actually
    // closest to, so "first"/"second" stay tied to the same physical
    // finger regardless of what order raylib reports them in.
    private Vector2 _lastFirstPos;
    private Vector2 _lastSecondPos;

    /// <summary>
    /// Radians of camera tilt (pitch) per pixel the two-finger midpoint
    /// moves vertically. Camera Sensitivity Tuning: the up/down half of
    /// two-finger orbiting, so it's damped by the same <see cref="RotationSensitivity"/>
    /// as the left/right twist — see <see cref="Tilt"/>.
    /// </summary>
    private const float TiltSensitivity = 0.005f;

    /// <summary>
    /// Steepest the camera may tilt down toward the horizon, in radians
    /// above it. Kept well clear of 0 (dead level, which would put the
    /// horizon in frame and let Position dip toward/through the ground)
    /// and of a perfect 90° top-down (where azimuth becomes meaningless).
    /// </summary>
    private const float MinPitch = 0.26f; // ~15 degrees.

    /// <summary>Flattest the camera may tilt toward straight-down.</summary>
    private const float MaxPitch = 1.48f; // ~85 degrees.

    public void Update(ref Camera3D camera, float worldHalfSize)
    {
        int touchCount = Raylib.GetTouchPointCount();

        if (touchCount >= 2)
        {
            UpdateTwoFingerGesture(ref camera, worldHalfSize);
            _isOneFingerGesture = false; // A second finger landing mid-pan shouldn't jump-pan once it lifts back to one.
        }
        else
        {
            UpdateOneFingerPan(ref camera, touchCount);
            _isTwoFingerGesture = false;
        }

        // Desktop: the mouse wheel zooms too.
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0f)
            ZoomTo(ref camera, Vector3.Distance(camera.Position, camera.Target) * (1f - wheel * WheelZoomFraction), worldHalfSize);

        ClampTargetToWorld(ref camera, worldHalfSize);
    }

    /// <summary>
    /// One-Finger Panning: follows a single touch, or (for desktop testing)
    /// a held left mouse button — Raylib maps a primary touch to the left
    /// mouse button anyway, so touchCount == 1 and IsMouseButtonDown both
    /// read true together on an actual phone; this just means either is
    /// enough to drive it.
    /// </summary>
    private void UpdateOneFingerPan(ref Camera3D camera, int touchCount)
    {
        bool isDown = touchCount == 1 || Raylib.IsMouseButtonDown(MouseButton.Left);
        if (!isDown)
        {
            _isOneFingerGesture = false;
            return;
        }

        Vector2 currentPos = touchCount == 1 ? Raylib.GetTouchPosition(0) : Raylib.GetMousePosition();
        if (_isOneFingerGesture)
        {
            _gesturePanPixels += Vector2.Distance(currentPos, _lastTouchPos);
            Pan(ref camera, currentPos - _lastTouchPos);
        }
        else
        {
            _gesturePanPixels = 0f;
        }

        _lastTouchPos = currentPos;
        _isOneFingerGesture = true;
    }

    /// <summary>
    /// Two-Finger Rotation + Tilt + the old pinch-zoom, all read off the
    /// same two touch points: the angle between them drives yaw, the
    /// distance between them drives zoom (as before), and — since both of
    /// those are already relative-to-each-other measures — the midpoint's
    /// own vertical movement (both fingers sliding up or down together) is
    /// free to drive pitch without fighting either one.
    /// </summary>
    private void UpdateTwoFingerGesture(ref Camera3D camera, float worldHalfSize)
    {
        Vector2 pointA = Raylib.GetTouchPosition(0);
        Vector2 pointB = Raylib.GetTouchPosition(1);

        // The Flip Fix: raylib's index-to-finger assignment isn't stable
        // frame to frame, so pick whichever of the two possible pairings
        // (A/B as-is, or swapped) keeps each point closest to where it
        // already was last frame, rather than trusting index order.
        Vector2 first = pointA;
        Vector2 second = pointB;
        if (_isTwoFingerGesture)
        {
            float straight = Vector2.DistanceSquared(pointA, _lastFirstPos) + Vector2.DistanceSquared(pointB, _lastSecondPos);
            float swapped = Vector2.DistanceSquared(pointA, _lastSecondPos) + Vector2.DistanceSquared(pointB, _lastFirstPos);
            if (swapped < straight)
            {
                first = pointB;
                second = pointA;
            }
        }

        float angle = MathF.Atan2(second.Y - first.Y, second.X - first.X);
        float distance = Vector2.Distance(first, second);
        float midpointY = (first.Y + second.Y) / 2f;

        if (_isTwoFingerGesture)
        {
            // The Other Flip: Atan2 only ever returns a value in (-π, π],
            // so the instant the two-finger vector swings past that
            // branch cut (pointing due "west" on screen — easily crossed
            // mid-rotation), raw angle jumps from just under +π to just
            // over -π (or back), a spurious ~2π delta that would otherwise
            // get applied as a huge, instant rotation. Wrapping the delta
            // back into (-π, π] turns that into the tiny real delta it
            // actually was.
            float rawDelta = angle - _lastTouchAngle;
            float angleDelta = rawDelta - MathF.Tau * MathF.Round(rawDelta / MathF.Tau);

            // Negated: dragging clockwise should turn the world clockwise
            // beneath the camera, not the reverse.
            Rotate(ref camera, -angleDelta * RotationSensitivity);
            Zoom(ref camera, distance - _lastPinchDistance, worldHalfSize);
            Tilt(ref camera, midpointY - _lastTwoFingerMidpointY);
        }

        _lastTouchAngle = angle;
        _lastPinchDistance = distance;
        _lastTwoFingerMidpointY = midpointY;
        _lastFirstPos = first;
        _lastSecondPos = second;
        _isTwoFingerGesture = true;
    }

    /// <summary>Translates Position and Target together across the ground plane, following the drag.</summary>
    private static void Pan(ref Camera3D camera, Vector2 screenDelta)
    {
        if (screenDelta == Vector2.Zero)
            return;

        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));

        // Flatten both basis vectors onto the X/Z plane: dragging the finger
        // should slide the camera across the terrain, not up into the sky.
        var forwardXZ = new Vector3(forward.X, 0, forward.Z);
        var rightXZ = new Vector3(right.X, 0, right.Z);
        if (forwardXZ.LengthSquared() > 1e-6f) forwardXZ = Vector3.Normalize(forwardXZ);
        if (rightXZ.LengthSquared() > 1e-6f) rightXZ = Vector3.Normalize(rightXZ);

        // Scale by how far back the camera is sitting, so a zoomed-out view
        // (which shows more ground per pixel) still pans at a matching
        // on-screen speed instead of feeling sluggish.
        float distance = Vector3.Distance(camera.Position, camera.Target);
        float metersPerPixel = distance * 0.0016f;

        // Dragging a finger right/up should slide the world the same way
        // under it, which means moving the camera left/back. Camera
        // Sensitivity Tuning: PanSensitivity is the final overall-feel
        // multiplier, applied on top of the distance-based scaling above.
        Vector3 worldDelta = (-rightXZ * screenDelta.X + forwardXZ * screenDelta.Y) * metersPerPixel * PanSensitivity;
        camera.Position += worldDelta;
        camera.Target += worldDelta;
    }

    /// <summary>
    /// Two-Finger Rotation: spins Position around Target strictly on the Y
    /// axis by <paramref name="angleDelta"/> radians (standard 2D rotation
    /// applied to the X/Z offset) — the world appears to turn beneath a
    /// camera that stays locked on the same focus point, height unchanged.
    /// </summary>
    private static void Rotate(ref Camera3D camera, float angleDelta)
    {
        if (angleDelta == 0f)
            return;

        Vector3 offset = camera.Position - camera.Target;
        float cos = MathF.Cos(angleDelta);
        float sin = MathF.Sin(angleDelta);
        var rotatedOffset = new Vector3(
            offset.X * cos - offset.Z * sin,
            offset.Y,
            offset.X * sin + offset.Z * cos);
        camera.Position = camera.Target + rotatedOffset;
    }

    /// <summary>
    /// Two-Finger Tilt: both fingers sliding up or down together changes
    /// the camera's pitch (its elevation angle above the Target) while
    /// holding its distance and azimuth (compass direction around the
    /// Target) fixed — dragging down flattens toward a top-down view,
    /// dragging up tilts it into a lower, more oblique angle. Clamped to
    /// [MinPitch, MaxPitch] so it can never flatten past dead-level (which
    /// would put the horizon in frame) or flip past straight-down.
    /// </summary>
    private static void Tilt(ref Camera3D camera, float midpointDeltaY)
    {
        if (midpointDeltaY == 0f)
            return;

        Vector3 offset = camera.Position - camera.Target;
        float distance = offset.Length();
        if (distance < 1e-4f)
            return;

        float horizontalDistance = MathF.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
        float azimuth = MathF.Atan2(offset.Z, offset.X);
        float pitch = Math.Clamp(MathF.Atan2(offset.Y, horizontalDistance) + midpointDeltaY * TiltSensitivity * RotationSensitivity, MinPitch, MaxPitch);

        float newHorizontalDistance = distance * MathF.Cos(pitch);
        camera.Position = camera.Target + new Vector3(
            newHorizontalDistance * MathF.Cos(azimuth),
            distance * MathF.Sin(pitch),
            newHorizontalDistance * MathF.Sin(azimuth));
    }

    /// <summary>Moves Position along the Target->Position axis: fingers spreading apart zooms in.</summary>
    private static void Zoom(ref Camera3D camera, float pinchDistanceDelta, float worldHalfSize)
    {
        if (pinchDistanceDelta == 0f)
            return;
        ZoomTo(ref camera, Vector3.Distance(camera.Position, camera.Target) - pinchDistanceDelta * PinchZoomSensitivity, worldHalfSize);
    }

    /// <summary>Puts the camera <paramref name="distance"/> from its Target (within the zoom limits), keeping its viewing angle.</summary>
    private static void ZoomTo(ref Camera3D camera, float distance, float worldHalfSize)
    {
        Vector3 offset = camera.Position - camera.Target;
        float current = offset.Length();
        if (current < 1e-4f)
            return;

        float newDistance = Math.Clamp(distance, MinZoomDistance, MaxZoomDistance(camera, worldHalfSize));
        camera.Position = camera.Target + offset / current * newDistance;
    }

    /// <summary>Keeps the camera's Target from drifting off the playable terrain.</summary>
    private static void ClampTargetToWorld(ref Camera3D camera, float worldHalfSize)
    {
        float limit = worldHalfSize + PanEdgeMargin;
        var clampedTarget = new Vector3(
            Math.Clamp(camera.Target.X, -limit, limit),
            camera.Target.Y,
            Math.Clamp(camera.Target.Z, -limit, limit));

        Vector3 correction = clampedTarget - camera.Target;
        if (correction == Vector3.Zero)
            return;

        camera.Target += correction;
        camera.Position += correction;
    }
}
