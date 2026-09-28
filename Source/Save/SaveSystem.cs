using System.Text.Json;
using System.Text.Json.Serialization;

namespace GardenGuardians;

/// <summary>
/// Save/Load: writes the garden to a JSON file and reads it back. The
/// serializer is source-generated (see <see cref="SaveJsonContext"/>), so it
/// keeps working in trimmed (Android Release) builds.
/// </summary>
public static class SaveSystem
{
    /// <summary>How many gardens can be kept at once (see <see cref="SlotPath"/>).</summary>
    public const int Slots = 3;

    /// <summary>Where the game keeps garden <paramref name="slot"/> (1 to <see cref="Slots"/>): the first is <see cref="DefaultPath"/>, the others beside it.</summary>
    public static string SlotPath(int slot) =>
        slot <= 1 ? DefaultPath : Path.Combine(Path.GetDirectoryName(DefaultPath)!, $"garden{slot}.json");

    /// <summary>Where the game keeps its first saved garden.</summary>
    public static string DefaultPath
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
            if (string.IsNullOrEmpty(root))
                root = AppContext.BaseDirectory;
            return Path.Combine(root, "GardenGuardians", "garden.json");
        }
    }

    /// <summary>
    /// Writes <paramref name="world"/> to <paramref name="path"/>: first to a
    /// temporary file, then moved into place, so a crash mid-write never
    /// leaves a half-written save behind. Returns false (and logs why) on
    /// failure.
    /// </summary>
    public static bool Save(World world, string path)
    {
        try
        {
            SaveGame save = world.ToSave();
            save.SavedAt = DateTime.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + ".tmp";
            using (FileStream stream = File.Create(temporary))
                JsonSerializer.Serialize(stream, save, SaveJsonContext.Default.SaveGame);
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            // Never let a failed save take the game down with it.
            Console.Error.WriteLine($"Garden Guardians: couldn't save to {path}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// The garden saved at <paramref name="path"/>, or null if there isn't
    /// one — or it can't be read, or is from an incompatible version (it's
    /// left in place, and a fresh garden starts instead).
    /// </summary>
    public static World? TryLoad(string path, Random rng)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            SaveGame? save;
            using (FileStream stream = File.OpenRead(path))
                save = JsonSerializer.Deserialize(stream, SaveJsonContext.Default.SaveGame);
            if (save is null || save.Version != SaveGame.CurrentVersion)
            {
                Console.Error.WriteLine($"Garden Guardians: ignoring {path} (save version {save?.Version}, expected {SaveGame.CurrentVersion})");
                return null;
            }
            return World.FromSave(save, new Terrain(size: 100f), rng);
        }
        catch (Exception e)
        {
            // A broken save must never stop the game from starting.
            Console.Error.WriteLine($"Garden Guardians: couldn't load {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>Forgets the saved garden (for "New garden").</summary>
    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Garden Guardians: couldn't delete {path}: {e.Message}");
        }
    }
}

/// <summary>Source-generated JSON for <see cref="SaveGame"/> (no runtime reflection, so trimming can't break it).</summary>
[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(SaveGame))]
internal sealed partial class SaveJsonContext : JsonSerializerContext;
