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
            {
                JsonSerializer.Serialize(stream, save, SaveJsonContext.Default.SaveGame);
                stream.Flush(flushToDisk: true); // (on disk before it replaces the old save, so a power cut can't leave an empty file)
            }
            if (File.Exists(path))
                File.Replace(temporary, path, BackupPath(path)); // the save it replaces is kept as the backup
            else
                File.Move(temporary, path);
            return true;
        }
        catch (Exception e)
        {
            // Never let a failed save take the game down with it.
            Console.Error.WriteLine($"Garden Guardians: couldn't save to {path}: {e.Message}");
            return false;
        }
    }

    /// <summary>Where the save before the latest one is kept, in case the latest can't be read.</summary>
    private static string BackupPath(string path) => path + ".bak";

    /// <summary>What the start menu shows of a saved garden.</summary>
    public sealed record SaveSummary(int Year, int Kin, int Groups, DateTime SavedAt, bool Grown);

    /// <summary>A quick look at the garden saved at <paramref name="path"/> (its year, how many Bramblekin and clans, when it was saved, whether its terrain was grown from a seed), or null if there isn't a readable one from this version.</summary>
    public static SaveSummary? Peek(string path) => PeekFile(path) ?? PeekFile(BackupPath(path));

    private static SaveSummary? PeekFile(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            SaveGame? save = JsonSerializer.Deserialize(stream, SaveJsonContext.Default.SaveGame);
            if (save is null || save.Version != SaveGame.CurrentVersion)
                return null;
            double elapsed = save.Numbers.TryGetValue("p:ElapsedSeconds", out double seconds) ? seconds : 0.0;
            bool grown = save.Numbers.TryGetValue(TerrainKey, out double terrain) && terrain >= TerrainData.ProceduralBase;
            return new SaveSummary((int)(elapsed / (World.SeasonLength * 4f)) + 1, save.Kin.Count, save.Groups.Count, save.SavedAt, grown);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Garden Guardians: couldn't read {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>The name a garden's terrain number is saved under (see <see cref="SaveGame.Numbers"/>).</summary>
    public const string TerrainKey = "terrain";

    /// <summary>
    /// The garden saved at <paramref name="path"/>, or null if there isn't
    /// one — or it can't be read, or is from an incompatible version (it's
    /// left in place, and a fresh garden starts instead).
    /// </summary>
    public static World? TryLoad(string path, Random rng) => LoadFile(path, rng) ?? LoadFile(BackupPath(path), rng);

    /// <summary>When the garden last loaded was saved (UTC): how long the player has been away.</summary>
    public static DateTime LastLoadedAt { get; private set; }

    private static World? LoadFile(string path, Random rng)
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
                KeepOldVersion(path, save?.Version);
                return null;
            }
            LastLoadedAt = save.SavedAt;
            int terrain = save.Numbers.TryGetValue(TerrainKey, out double saved) ? (int)saved : 0; // A garden from before terrains were chosen kept the original.
            return World.FromSave(save, new Terrain(terrain), rng);
        }
        catch (Exception e)
        {
            // A broken save must never stop the game from starting.
            Console.Error.WriteLine($"Garden Guardians: couldn't load {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>A save from another version is about to be written over by a fresh garden: set a copy aside first, once, so nothing is ever lost for good.</summary>
    private static void KeepOldVersion(string path, int? version)
    {
        try
        {
            string copy = $"{path}.v{version}.old";
            if (!File.Exists(copy))
                File.Copy(path, copy);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Garden Guardians: couldn't keep a copy of {path}: {e.Message}");
        }
    }

    /// <summary>Forgets the saved garden (for "New garden").</summary>
    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
            File.Delete(BackupPath(path)); // (a new garden must not come back from its backup)
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
