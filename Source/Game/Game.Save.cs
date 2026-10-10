using Raylib_cs;

namespace GardenGuardians;

public static partial class Game
{
    /// <summary>Real seconds between autosaves (the game also saves on the way out).</summary>
    private const float AutosaveInterval = 30f;

    private static volatile bool _saveRequested, _inGarden;
    private static readonly ManualResetEventSlim SaveDone = new(false);

    /// <summary>
    /// Android is pausing the app (it may be killed from the background): asks the game loop to save right now and waits for it, up to
    /// <paramref name="milliseconds"/>. Called from the UI thread before the pause reaches the game thread, so the loop is still running.
    /// </summary>
    public static void SaveNowAndWait(int milliseconds)
    {
        if (!_inGarden)
            return; // (in the menu there is nothing to save)
        SaveDone.Reset();
        _saveRequested = true;
        SaveDone.Wait(milliseconds);
    }

    /// <summary>Preferences key for the garden being played (see <see cref="GardenSlot"/>).</summary>
    private const string GardenSetting = "garden";
    private const string TerrainSetting = "terrain";
    private const string MapSizeSetting = "mapsize";
    private const string StartAgeSetting = "startage";

    /// <summary>The age a new garden starts in (chosen on the start menu).</summary>
    private static Era _startEra;

    /// <summary>Which kept garden is open, 1 to <see cref="SaveSystem.Slots"/>.</summary>
    private static int _gardenSlot = 1;

    /// <summary>Where the open garden is kept.</summary>
    private static string GardenPath => SaveSystem.SlotPath(_gardenSlot);

    /// <summary>The garden saved last time, or a fresh one if there's none (or it can't be read).</summary>
    private static World LoadOrCreateWorld(string savePath)
    {
        if (SaveSystem.TryLoad(savePath, new Random()) is { } saved)
        {
            AddEventLog($"[SAVE] Welcome back - garden {_gardenSlot} carries on in Year {saved.Year}");
            BeginCatchUp(saved);
            return saved;
        }
        return NewWorld();
    }

    private static World NewWorld()
    {
        var rng = new Random();
        var world = new World(new Terrain(ForcedTerrain ?? TerrainData.RandomIndex(rng)), rng, InitialKinCount);
        world.GrantEra(_startEra);
        return world;
    }

    /// <summary>Wipes the saved garden and starts a fresh one, closing the History screen and clearing the log.</summary>
    private static World StartNewGarden(string savePath)
    {
        SaveSystem.Delete(savePath);
        _catchUpLeft = 0f;
        _debugLogs.Clear();
        _showChronicle = false;
        _chronicleScroll = 0f;
        _chronicleDragY = null;
        World world = NewWorld();
        SaveSystem.Save(world, savePath);
        AddEventLog($"[SAVE] Garden {_gardenSlot}: a new garden begins");
        return world;
    }
}
