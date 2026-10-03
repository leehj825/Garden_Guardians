using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Day and night ------------------------------------------------------------------------

    /// <summary>Seconds from one dawn to the next: two days to a season.</summary>
    public const float DayLength = 75f;

    /// <summary>Dusk (from nightfall) and dawn (to the day's end) each take this fraction of a day.</summary>
    private const float TwilightSpan = 0.07f;

    /// <summary>A night is darkest (and the sky lightens) until this far through the day; dawn runs from here to the end of it.</summary>
    private const float DawnAt = 1f - TwilightSpan;

    /// <summary>How far through the day night falls: early in winter, late in summer.</summary>
    private static float NightfallOf(Season season) => season switch
    {
        Season.Summer => 0.74f,
        Season.Winter => 0.62f,
        _ => 0.68f,
    };

    private static readonly Color NightSky = new(18, 24, 52, 255);

    /// <summary>0 at dawn, approaching 1 at the next.</summary>
    public float DayPhase => ElapsedSeconds % DayLength / DayLength;

    /// <summary>The day of the year, from 1.</summary>
    public int DayOfYear => (int)(ElapsedSeconds % (SeasonLength * 4f) / DayLength) + 1;

    /// <summary>How dark it is: 0 by day, 1 in the dead of night, in between at dusk and dawn.</summary>
    public float Darkness
    {
        get
        {
            float t = DayPhase;
            float nightfall = NightfallOf(CurrentSeason);
            if (t < nightfall)
                return 0f;
            if (t < nightfall + TwilightSpan)
                return (t - nightfall) / TwilightSpan;
            if (t < DawnAt)
                return 1f;
            return 1f - (t - DawnAt) / TwilightSpan;
        }
    }

    /// <summary>Night: time for bed — for most (see Bramblekin.Night).</summary>
    public bool IsNight => Darkness >= 0.5f;

    /// <summary>"Morning", "Evening", "Night"… for the HUD.</summary>
    public string TimeOfDayLabel
    {
        get
        {
            float t = DayPhase;
            float nightfall = NightfallOf(CurrentSeason);
            if (IsNight)
                return "Night";
            if (t >= nightfall - 0.08f && t < DawnAt)
                return "Dusk";
            if (t >= DawnAt || t < 0.12f)
                return "Dawn";
            return t < nightfall * 0.5f ? "Morning" : "Afternoon";
        }
    }

    // --- The night watch -------------------------------------------------------------------------

    /// <summary>A clan of at least this many posts a watch at night.</summary>
    private const int NightWatchMinMembers = 3;

    /// <summary>A watch's (or a sleeper's) alarm keeps its clan awake this long (s).</summary>
    public const float AlarmSeconds = 8f;

    /// <summary>How often (s) each clan's watch is checked, and a new one posted if needed.</summary>
    private const float NightWatchCheckInterval = 3f;

    private float _nightWatchTimer;
    private bool _wasNight;

    public int NightsPassed { get; private set; }
    public int WatchesPosted { get; private set; }
    public int AlarmsRaised { get; private set; }
    public int NightRaids { get; private set; }

    /// <summary>
    /// Nightfall and dawn: each settled clan of a few posts one member to
    /// keep watch by home through the night — a Swordsman if it has one, else
    /// its bravest — and stands it down at dawn.
    /// </summary>
    private void UpdateNight(float deltaTime)
    {
        bool night = IsNight;
        if (night != _wasNight)
        {
            _wasNight = night;
            if (night)
            {
                NightsPassed++;
                _nightWatchTimer = 0f;
            }
            else
            {
                foreach (KinGroup group in _groups.Values)
                    group.NightWatch = null;
            }
        }
        if (!night)
            return;

        _nightWatchTimer -= deltaTime;
        if (_nightWatchTimer > 0f)
            return;
        _nightWatchTimer = NightWatchCheckInterval;
        foreach (KinGroup group in _groups.Values)
        {
            if (group.NightWatch is { IsDead: false } watch && watch.GroupId == group.Id)
                continue;
            group.NightWatch = null;
            if (group.Home is not { IsBuilt: true } || group.Members.Count(m => !m.IsDead) < NightWatchMinMembers)
                continue;
            group.NightWatch = group.Members
                .Where(m => !m.IsDead && !m.IsYoung && m.Home is { IsBuilt: true } && m.Health > Bramblekin.MaxHealth / 2)
                .OrderByDescending(m => m.Job == KinJob.Swordsman)
                .ThenByDescending(m => m.Personality.Courage)
                .FirstOrDefault();
            if (group.NightWatch is not null)
                WatchesPosted++;
        }
    }

    /// <summary>Wakes <paramref name="group"/> for a while: its watch saw something, or one of its sleepers was attacked.</summary>
    public void RaiseAlarm(KinGroup group, Vector3 where)
    {
        if (group.AlarmUntil > ElapsedSeconds)
        {
            group.AlarmUntil = ElapsedSeconds + AlarmSeconds;
            return;
        }
        group.AlarmUntil = ElapsedSeconds + AlarmSeconds;
        AlarmsRaised++;
        QueueFloatingText(where, "Alarm!", HostileTextColor);
    }

    /// <summary>True while <paramref name="group"/> has been woken by an alarm.</summary>
    public bool IsAlarmed(KinGroup? group) => group is not null && group.AlarmUntil > ElapsedSeconds;

    /// <summary>A raiding party sets out in the dark.</summary>
    public void NoteNightRaid() => NightRaids++;

    // --- Drawing ---------------------------------------------------------------------------------

    /// <summary>The sky darkens toward a deep blue at night.</summary>
    private Color NightTinted(Color sky) => Blend(sky, NightSky, Darkness * 0.8f);

    private static readonly Color HearthGlowColor = new(255, 150, 60, 255);
    private static readonly Color WindowGlowColor = new(255, 210, 120, 255);
    private static readonly Color FireflyColor = new(210, 255, 120, 255);

    /// <summary>Fireflies over the grass on a warm night.</summary>
    private static int FireflyCount => Scaled(60);

    /// <summary>
    /// What still shines once night has fallen (drawn after the darkness is
    /// laid over the garden — see Game): the glow round every lit hearth,
    /// lit windows, fireflies over the grass (not in winter). Drawn additively, so light adds to whatever it falls on.
    /// </summary>
    public void DrawNightLights(Camera3D camera)
    {
        float darkness = Darkness;
        if (darkness <= 0f)
            return;

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);

        foreach (Shelter shelter in Shelters)
        {
            if (shelter.IsCollapsed || !IsVisible(shelter.Position, camera))
                continue;
            if (shelter.IsHearthLit)
            {
                Vector3 hearth = Grounded(shelter.HearthPosition);
                Raylib.DrawCylinder(hearth + new Vector3(0f, 0.05f, 0f), 2.6f, 2.6f, 0.02f, 20, HearthGlowColor with { A = (byte)(55 * darkness) });
                Raylib.DrawCylinder(hearth + new Vector3(0f, 0.06f, 0f), 1.2f, 1.2f, 0.02f, 16, HearthGlowColor with { A = (byte)(70 * darkness) });
                Detail.Sphere(hearth + new Vector3(0f, 0.2f, 0f), 0.22f, HearthGlowColor with { A = (byte)(200 * darkness) });
            }
            if (shelter.WindowPosition is { } window)
                Detail.Sphere(window, 0.16f, WindowGlowColor with { A = (byte)(170 * darkness) });
        }

        if (CurrentSeason != Season.Winter)
            DrawFireflies(camera, darkness);
        DrawFeastLanterns(darkness);
        DrawShrineCandles(darkness);


        Raylib.EndBlendMode();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }

    /// <summary>Each firefly drifts in a slow loop round its own spot, blinking — worked out from the time alone, so drawing never touches the simulation's dice.</summary>
    private void DrawFireflies(Camera3D camera, float darkness)
    {
        float time = ElapsedSeconds;
        for (int i = 0; i < FireflyCount; i++)
        {
            uint h = (uint)(i * 2654435761u);
            float span = 2f * (TerrainData.Half - 5f); // Fireflies keep 5 m off the edge.
            float x = (h % 9000) / 9000f * span - span / 2f;
            float z = (h / 9000 % 9000) / 9000f * span - span / 2f;
            float phase = (h >> 7 & 1023) / 1023f * MathF.Tau;
            var spot = new Vector3(x + MathF.Sin(time * 0.3f + phase) * 1.5f, 0f, z + MathF.Cos(time * 0.23f + phase) * 1.5f);
            if (IsWater(spot) || !IsVisible(spot, camera))
                continue;
            float blink = MathF.Max(0f, MathF.Sin(time * 2.1f + phase * 3f));
            if (blink <= 0.05f)
                continue;
            Vector3 at = Grounded(spot, 0.5f + 0.4f * MathF.Sin(time * 0.7f + phase));
            Detail.Sphere(at, 0.05f, FireflyColor with { A = (byte)(255 * blink * darkness) });
            Detail.Sphere(at, 0.16f, FireflyColor with { A = (byte)(60 * blink * darkness) });
        }
    }
}
