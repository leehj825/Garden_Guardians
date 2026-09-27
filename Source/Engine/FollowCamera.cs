using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Follow Camera: tapping a Bramblekin (see <see cref="WorldTapInput"/>)
/// swoops the Spectator Camera in on it and keeps it centred as it goes
/// about its life. The player can still pinch-zoom, twist and tilt while
/// following; a one-finger pan (see <see cref="TouchCameraController.DraggedThisGesture"/>)
/// takes the camera back and stops following. The "Map" button flies back
/// out to the whole-garden view.
/// </summary>
public sealed class FollowCamera
{
    /// <summary>Selecting a Bramblekin from further out than this (m) zooms in to it.</summary>
    private const float FollowDistance = 18f;

    /// <summary>How quickly the camera catches up with whoever it's following (per second; higher is snappier).</summary>
    private const float FollowStiffness = 4f;

    /// <summary>How quickly the camera flies in (to a Bramblekin) or out (to the whole map).</summary>
    private const float FlyStiffness = 3f;

    private readonly Vector3 _overviewTarget;
    private readonly Vector3 _overviewPosition;

    private Bramblekin? _lastSelected;
    private bool _zoomingIn;
    private bool _flyingToOverview;

    /// <summary>Somewhere the camera is flying to (see <see cref="FlyTo"/>), if anywhere.</summary>
    private Vector3? _flyTarget;

    /// <summary>How close (m) <see cref="FlyTo"/> brings the camera.</summary>
    private const float FlyToDistance = 28f;

    /// <param name="overview">The whole-garden camera pose the "Map" button flies back to.</param>
    public FollowCamera(Camera3D overview)
    {
        _overviewTarget = overview.Target;
        _overviewPosition = overview.Position;
    }

    /// <summary>True while the camera is tracking the selected Bramblekin.</summary>
    public bool IsFollowing { get; private set; }

    /// <summary>Starts or stops following the selected Bramblekin.</summary>
    public void ToggleFollow(World world)
    {
        IsFollowing = !IsFollowing && world.SelectedKin is { IsDead: false };
        _zoomingIn = IsFollowing;
        _flyingToOverview = false;
    }

    /// <summary>Stops following and flies back out to the whole-garden view.</summary>
    public void ShowWholeMap()
    {
        IsFollowing = false;
        _zoomingIn = false;
        _flyTarget = null;
        _flyingToOverview = true;
    }

    /// <summary>Stops following and flies over to look at <paramref name="where"/> (a banner's big moment).</summary>
    public void FlyTo(Vector3 where)
    {
        IsFollowing = false;
        _zoomingIn = false;
        _flyingToOverview = false;
        _flyTarget = where;
    }

    /// <summary>
    /// Once a frame, after the touch gestures and the tap input: starts
    /// following a newly selected Bramblekin, stops if it's gone or the
    /// player panned away (<paramref name="playerPanned"/>), and eases the
    /// camera toward where it should be.
    /// </summary>
    public void Update(ref Camera3D camera, World world, float deltaTime, bool playerPanned)
    {
        Bramblekin? selected = world.SelectedKin is { IsDead: false } kin ? kin : null;
        if (selected != _lastSelected)
        {
            _lastSelected = selected;
            IsFollowing = selected is not null;
            _zoomingIn = IsFollowing;
            if (IsFollowing)
            {
                _flyingToOverview = false;
                _flyTarget = null;
            }
        }

        if (selected is null)
            IsFollowing = false;
        if (playerPanned)
        {
            IsFollowing = false;
            _zoomingIn = false;
            _flyingToOverview = false;
            _flyTarget = null;
        }

        if (_flyTarget is { } destination)
        {
            // Glide the focus over, and in (or out) to a village-sized view, keeping the viewing angle.
            float t = Ease(FlyStiffness, deltaTime);
            Vector3 offset = camera.Position - camera.Target;
            float distance = MathF.Max(offset.Length(), 1e-3f);
            camera.Target = Vector3.Lerp(camera.Target, destination, t);
            camera.Position = camera.Target + offset / distance * (distance + (FlyToDistance - distance) * t);
            if (Vector3.DistanceSquared(camera.Target, destination) < 0.05f && MathF.Abs(distance - FlyToDistance) < 0.5f)
                _flyTarget = null;
            return;
        }

        if (_flyingToOverview)
        {
            float t = Ease(FlyStiffness, deltaTime);
            camera.Target = Vector3.Lerp(camera.Target, _overviewTarget, t);
            camera.Position = Vector3.Lerp(camera.Position, _overviewPosition, t);
            if (Vector3.DistanceSquared(camera.Position, _overviewPosition) < 0.25f)
                _flyingToOverview = false;
            return;
        }

        if (!IsFollowing || selected is null)
            return;

        // Glide the focus onto it, carrying the camera along at the same offset.
        Vector3 shift = (selected.Position - camera.Target) * Ease(FollowStiffness, deltaTime);
        camera.Target += shift;
        camera.Position += shift;

        // On first following, swoop in from wherever the camera was.
        if (_zoomingIn)
        {
            Vector3 offset = camera.Position - camera.Target;
            float distance = offset.Length();
            if (distance <= FollowDistance + 0.5f || distance < 1e-3f)
            {
                _zoomingIn = false;
                return;
            }
            float newDistance = distance + (FollowDistance - distance) * Ease(FlyStiffness, deltaTime);
            camera.Position = camera.Target + offset / distance * newDistance;
        }
    }

    /// <summary>Frame-rate independent easing: the fraction of the remaining gap to close this frame.</summary>
    private static float Ease(float stiffness, float deltaTime) => 1f - MathF.Exp(-stiffness * deltaTime);
}
