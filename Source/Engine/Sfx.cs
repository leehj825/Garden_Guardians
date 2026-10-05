using Raylib_cs;

namespace GardenGuardians;

/// <summary>Short sound effects (<c>Assets/Audio/Sfx</c>, Ogg): a sword or spear blow, an arrow loosed. Loaded the first time each is played.</summary>
internal static class Sfx
{
    public enum Effect { SwordSpear, ShootingArrow }

    private static readonly string[] Files = { "SwordSpear", "ShootingArrow" };

    private static readonly string Folder = OperatingSystem.IsAndroid()
        ? "Audio/Sfx/"
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", "Sfx") + Path.DirectorySeparatorChar;

    private const float Volume = 0.8f;

    private static readonly Sound[] _sounds = new Sound[Files.Length];
    private static readonly bool[] _loaded = new bool[Files.Length];
    private static readonly bool[] _failed = new bool[Files.Length];

    /// <summary>The kin the player is controlling. Sounds are heard only while there is one.</summary>
    public static Bramblekin? Listener { get; set; }

    /// <summary>Sounds this close (m) to the controlled kin are played.</summary>
    private const float HearingRange = 20f;

    /// <summary>Plays <paramref name="effect"/> if the player is controlling a kin and it happens within <see cref="HearingRange"/> of it. Silent when the player is not controlling anyone.</summary>
    public static void PlayNear(Effect effect, System.Numerics.Vector3 at)
    {
        if (Listener is not { IsDead: false, IsPlayerControlled: true } listener || GroundMover.HorizontalDistanceSquared(listener.Position, at) > HearingRange * HearingRange)
            return;
        Play(effect);
    }

    private static void Play(Effect effect)
    {
        int index = (int)effect;
        if (_failed[index] || !MusicPlayer.EnsureDevice())
            return;
        if (!_loaded[index])
        {
            string path = Folder + Files[index] + ".ogg";
            if (!OperatingSystem.IsAndroid() && !File.Exists(path))
            {
                _failed[index] = true;
                return;
            }
            _sounds[index] = Raylib.LoadSound(path);
            if (_sounds[index].FrameCount == 0)
            {
                _failed[index] = true;
                return;
            }
            Raylib.SetSoundVolume(_sounds[index], Volume);
            _loaded[index] = true;
        }
        Raylib.PlaySound(_sounds[index]);
    }
}
