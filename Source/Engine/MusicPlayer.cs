using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The background music (<c>Assets/Audio/Music</c>, Ogg streamed by raylib): the main theme (the menu, and days in spring and summer), a slower piece for
/// autumn and winter days, the night piece, and the combat piece while an invasion or a major fight is on. One track plays at a time and the music crossfades
/// by fading the old one out, then starting the new one; a track is kept for <see cref="MinSeconds"/> before the situation may change it (so a fight that
/// flickers does not flip the music). While the game is sped up to 10x and over there is no music: it fades out, and carries on from where it was when the speed comes back down.
/// </summary>
internal static class MusicPlayer
{
    public enum Track { Main, AutumnWinter, Night, Combat }

    private static readonly string[] Files = { "MainTheme", "AutumnWinter", "NightTime", "InvasionCombat" };

    private static readonly string Folder = OperatingSystem.IsAndroid()
        ? "Audio/Music/"
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", "Music") + Path.DirectorySeparatorChar;

    /// <summary>Seconds a fade takes, and the least a track stays once chosen (a fight's music may start at once, and ends only after this).</summary>
    private const float FadeSeconds = 1.8f, MinSeconds = 25f;

    /// <summary>The loudness of the music (0 to 1), set on the Settings page and kept in the settings file.</summary>
    public static float Volume { get; set; } = 0.5f;

    /// <summary>This many fighting kin at once is a major fight.</summary>
    private const int MajorFightKin = 4;

    private static readonly Music[] _music = new Music[Files.Length];
    private static readonly bool[] _loaded = new bool[Files.Length];
    private static bool _device, _failed;

    private static int _playing = -1;   // the track on the stream (or paused), -1 none
    private static bool _paused;
    private static float _gain;         // 0 to 1, of the one playing
    private static Track _picked = Track.Main;
    private static float _dwell = MinSeconds;

    internal static bool EnsureDevice()
    {
        if (_failed)
            return false;
        if (_device)
            return true;
        Raylib.InitAudioDevice();
        _device = Raylib.IsAudioDeviceReady();
        _failed = !_device; // (no sound card, or the phone would not give one: no music, and not another try every frame)
        return _device;
    }

    private static bool Load(int track)
    {
        if (_loaded[track])
            return true;
        if (!File.Exists(Folder + Files[track] + ".ogg") && !OperatingSystem.IsAndroid())
            return false;
        _music[track] = Raylib.LoadMusicStream(Folder + Files[track] + ".ogg");
        if (_music[track].FrameCount == 0)
            return false;
        _music[track].Looping = true;
        _loaded[track] = true;
        return true;
    }

    /// <summary>What the garden calls for just now: the combat piece in a major fight, else the night piece, else the season's.</summary>
    public static Track For(World world)
    {
        bool invaded = false;
        foreach (InvaderSpider invader in world.Invaders)
        {
            if (!invader.IsDead)
            {
                invaded = true;
                break;
            }
        }
        if (invaded || world.CurrentAssault is not null)
            return Track.Combat;
        int fighting = 0;
        foreach (Bramblekin kin in world.Colony)
        {
            if (!kin.IsDead && kin.State is BramblekinState.Fighting or BramblekinState.Attacking or BramblekinState.Dueling && ++fighting >= MajorFightKin)
                return Track.Combat;
        }
        if (world.IsNight)
            return Track.Night;
        return world.CurrentSeason is Season.Autumn or Season.Winter ? Track.AutumnWinter : Track.Main;
    }

    /// <summary>Call once a frame. <paramref name="wanted"/> is what the situation calls for (null: no say, the main theme, as in the menu); <paramref name="silent"/> while sped up.</summary>
    public static void Update(float deltaTime, Track? wanted, bool silent)
    {
        if (!EnsureDevice())
            return;

        _dwell += deltaTime;
        Track target = wanted ?? Track.Main;
        if (target != _picked && (target == Track.Combat || _dwell >= MinSeconds))
        {
            _picked = target;
            _dwell = 0f;
        }

        if (_playing >= 0 && (silent || _playing != (int)_picked))
        {
            // Fade out what is playing: for good if another track is wanted, else it is only paused (to carry on later).
            _gain = MathF.Max(0f, _gain - deltaTime / FadeSeconds);
            _music[_playing].Looping = true;
            Raylib.SetMusicVolume(_music[_playing], _gain * Volume);
            if (!_paused)
                Raylib.UpdateMusicStream(_music[_playing]);
            if (_gain <= 0f)
            {
                if (silent && _playing == (int)_picked)
                {
                    if (!_paused)
                        Raylib.PauseMusicStream(_music[_playing]);
                    _paused = true;
                }
                else
                {
                    Raylib.StopMusicStream(_music[_playing]);
                    _playing = -1;
                    _paused = false;
                }
            }
            return;
        }
        if (silent)
            return;

        if (_playing < 0)
        {
            if (!Load((int)_picked))
                return;
            _playing = (int)_picked;
            _gain = 0f;
            _paused = false;
            Raylib.PlayMusicStream(_music[_playing]);
        }
        else if (_paused)
        {
            Raylib.ResumeMusicStream(_music[_playing]);
            _paused = false;
        }
        _gain = MathF.Min(1f, _gain + deltaTime / FadeSeconds);
        Raylib.SetMusicVolume(_music[_playing], _gain * Volume);
        Raylib.UpdateMusicStream(_music[_playing]);
    }

    /// <summary>Stops everything and closes the sound device (at exit).</summary>
    public static void Shutdown()
    {
        if (!_device)
            return;
        for (int i = 0; i < _music.Length; i++)
        {
            if (_loaded[i])
                Raylib.UnloadMusicStream(_music[i]);
        }
        Raylib.CloseAudioDevice();
        _device = false;
    }
}
