namespace GardenGuardians;

/// <summary>What terrain a new garden gets: the fixed one, or a fresh one grown from a random seed.</summary>
public enum TerrainMode
{
    Fixed,
    Random,
}

/// <summary>How big a grown terrain is: 100, 150 or 200 m across (see <see cref="TerrainData.MapSizes"/>).</summary>
public enum MapSize
{
    Small,
    Medium,
    Large,
}

/// <summary>Which of the kept gardens (see <see cref="SaveSystem.Slots"/>) is being played.</summary>
public enum GardenSlot
{
    Garden1 = 1,
    Garden2 = 2,
    Garden3 = 3,
}

/// <summary>
/// The player's own settings (how much of the event log to show, and which garden is open),
/// kept as <c>key=value</c> lines in a small text file beside the saved
/// garden, so they survive a restart — and starting a new garden.
/// </summary>
public static class Preferences
{
    private static readonly Dictionary<string, string> Values = new();
    private static string? _path;

    /// <summary>settings.txt, in the same folder as the saved garden (see <see cref="SaveSystem.DefaultPath"/>).</summary>
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(SaveSystem.DefaultPath)!, "settings.txt");

    /// <summary>Reads the settings at <paramref name="path"/> (none yet, or unreadable: all defaults), and saves any changes back there.</summary>
    public static void Load(string path)
    {
        _path = path;
        Values.Clear();
        try
        {
            if (!File.Exists(path))
                return;
            foreach (string line in File.ReadAllLines(path))
            {
                int split = line.IndexOf('=');
                if (split > 0)
                    Values[line[..split].Trim()] = line[(split + 1)..].Trim();
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Garden Guardians: couldn't read settings from {path}: {e.Message}");
        }
    }

    /// <summary>The setting <paramref name="key"/> as a <typeparamref name="T"/>, or <paramref name="fallback"/> if it isn't set (or isn't one).</summary>
    public static T Get<T>(string key, T fallback) where T : struct, Enum =>
        Values.TryGetValue(key, out string? value) && Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    /// <summary>Changes a setting and saves them all straight away (quietly giving up if it can't).</summary>
    public static void Set<T>(string key, T value) where T : struct, Enum
    {
        Values[key] = value.ToString();
        if (_path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllLines(_path, Values.Select(pair => $"{pair.Key}={pair.Value}"));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Garden Guardians: couldn't save settings to {_path}: {e.Message}");
        }
    }
}
