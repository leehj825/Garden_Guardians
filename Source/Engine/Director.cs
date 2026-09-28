using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The Director: an automatic camera for watching the garden hands-free —
/// at 20x or 50x most of all, when too much happens too fast to go looking
/// for it. It looks over everything worth watching (see
/// <see cref="World.DirectorShots"/>), flies over to the best of it and
/// keeps the camera on it for a few seconds, then moves on — cutting away
/// early for something much bigger (a war, a coup, the spider pouncing).
/// It never shows the same thing twice running if there's anything else.
/// Any pan, or picking a Bramblekin to follow, hands the camera back.
/// </summary>
public sealed class Director
{
    /// <summary>A shot is held at least this long (real seconds)…</summary>
    private const float MinHoldSeconds = 3f;

    /// <summary>…and moved on from after this long, if there's anything else to see.</summary>
    private const float HoldSeconds = 8f;

    /// <summary>Something this much more worth watching cuts in once the minimum hold is up.</summary>
    private const float CutInMargin = 3f;

    /// <summary>A subject (or label) shown within this long (real seconds) is worth this much less.</summary>
    private const float RepeatSeconds = 40f;
    private const float RepeatPenalty = 3f;

    /// <summary>How far (m) back the camera sits from a shot.</summary>
    private const float ShotDistance = 24f;

    private const float Stiffness = 2.5f;

    /// <summary>How often (real seconds) it looks over what's going on.</summary>
    private const float RethinkSeconds = 1f;

    private Shot? _shot;
    private float _held;
    private float _rethink;
    private float _clock;
    private readonly Dictionary<object, float> _shown = new();

    public bool IsOn { get; private set; }

    /// <summary>What it's showing, for the caption; null when off.</summary>
    public string? Caption => IsOn && _shot is { } shot ? shot.Label : null;

    public void Toggle()
    {
        IsOn = !IsOn;
        _shot = null;
        _rethink = 0f;
    }

    public void Stop()
    {
        IsOn = false;
        _shot = null;
    }

    /// <summary>Once a frame, after the touch gestures: picks what to show and eases the camera over to it.</summary>
    public void Update(ref Camera3D camera, World world, float realDeltaTime, bool playerTookOver)
    {
        if (!IsOn)
            return;
        if (playerTookOver)
        {
            Stop();
            return;
        }

        _clock += realDeltaTime;
        _held += realDeltaTime;
        _rethink -= realDeltaTime;
        if (_rethink <= 0f)
        {
            _rethink = RethinkSeconds;
            Rethink(world.DirectorShots());
        }
        if (_shot is not { } shot)
            return;

        // Glide the focus over, and in (or out) to the shot's distance, keeping the viewing angle.
        float t = 1f - MathF.Exp(-Stiffness * realDeltaTime);
        Vector3 offset = camera.Position - camera.Target;
        float distance = MathF.Max(offset.Length(), 1e-3f);
        camera.Target = Vector3.Lerp(camera.Target, shot.Focus, t);
        camera.Position = camera.Target + offset / distance * (distance + (ShotDistance - distance) * t);
    }

    private void Rethink(List<Shot> shots)
    {
        Shot? current = null;
        if (_shot is { } showing)
        {
            foreach (Shot shot in shots)
            {
                if (shot.Label == showing.Label)
                {
                    current = shot;
                    break;
                }
            }
        }

        Shot? best = null;
        float bestScore = float.MinValue;
        foreach (Shot shot in shots)
        {
            if (current is { } now && shot.Label == now.Label)
                continue;
            float score = shot.Score - (RecentlyShown(shot) ? RepeatPenalty : 0f);
            if (score > bestScore)
            {
                best = shot;
                bestScore = score;
            }
        }

        bool cut = _shot is null
                   || (current is null && _held >= MinHoldSeconds)
                   || (_held >= HoldSeconds && best is not null)
                   || (current is { } live && _held >= MinHoldSeconds && bestScore >= live.Score + CutInMargin);
        if (cut && best is { } next)
        {
            _shot = next;
            _held = 0f;
            _shown[next.Label] = _clock;
            if (next.Subject is not null)
                _shown[next.Subject] = _clock;
        }
        else if (current is { } refreshed)
        {
            _shot = refreshed; // Same shot, its focus brought up to date.
        }
    }

    private bool RecentlyShown(Shot shot) =>
        (_shown.TryGetValue(shot.Label, out float at) && _clock - at < RepeatSeconds) ||
        (shot.Subject is not null && _shown.TryGetValue(shot.Subject, out float seen) && _clock - seen < RepeatSeconds);
}
