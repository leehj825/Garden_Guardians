using Raylib_cs;

namespace GardenGuardians;

/// <summary>Short sound effects (<c>Assets/Audio/Sfx</c>, Ogg): a drink, a sword or spear blow, an arrow loosed. Loaded the first time each is played.</summary>
internal static class Sfx
{
    public enum Effect { Drink, SwordSpear, ShootingArrow }

    private static readonly string[] Files = { "Drink", "SwordSpear", "ShootingArrow" };

    private static readonly string Folder = OperatingSystem.IsAndroid()
        ? "Audio/Sfx/"
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", "Sfx") + Path.DirectorySeparatorChar;

    private const float Volume = 0.8f;

    private static readonly Sound[] _sounds = new Sound[Files.Length];
    private static readonly bool[] _loaded = new bool[Files.Length];
    private static readonly bool[] _failed = new bool[Files.Length];

    public static void Play(Effect effect)
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
