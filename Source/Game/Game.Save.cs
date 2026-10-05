using Raylib_cs;

namespace GardenGuardians;

public static partial class Game
{
    /// <summary>Real seconds between autosaves (the game also saves on the way out).</summary>
    private const float AutosaveInterval = 30f;

    /// <summary>"New garden" asks to be tapped again within this many seconds before it wipes the garden.</summary>
    private const float NewGardenConfirmWindow = 3f;

    /// <summary>Seconds left to confirm "New garden"; 0 when not armed.</summary>
    private static float _newGardenConfirm;

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
            return saved;
        }
        return NewWorld();
    }

    /// <summary>The terrain button, shown beside "New" while the History screen is open: tapping it switches whether a new garden gets the fixed terrain or one grown from a random seed.</summary>
    private static UiButton TerrainModeButton(UiButton newGardenButton, int margin) =>
        new(new Rectangle(newGardenButton.Bounds.X + newGardenButton.Bounds.Width + margin, newGardenButton.Bounds.Y,
            (int)(250 * UiScale), newGardenButton.Bounds.Height));

    private static void ToggleTerrainMode()
    {
        TerrainData.GrowNewGardens = !TerrainData.GrowNewGardens;
        Preferences.Set(TerrainSetting, TerrainData.GrowNewGardens ? TerrainMode.Random : TerrainMode.Fixed);
        AddEventLog(TerrainData.GrowNewGardens ? "[SAVE] New gardens will grow a fresh terrain" : "[SAVE] New gardens will use the fixed terrain");
    }

    private static World NewWorld()
    {
        var rng = new Random();
        var world = new World(new Terrain(ForcedTerrain ?? TerrainData.RandomIndex(rng)), rng, InitialKinCount);
        world.GrantEra(_startEra);
        return world;
    }

    /// <summary>The "New" (garden) button, shown beside History while the History screen is open.</summary>
    private static UiButton NewGardenButton(UiButton historyButton, int margin) =>
        new(new Rectangle(historyButton.Bounds.X + historyButton.Bounds.Width + margin, historyButton.Bounds.Y,
            (int)(250 * UiScale), historyButton.Bounds.Height));

    /// <summary>First tap arms "New garden"; a second within <see cref="NewGardenConfirmWindow"/> starts over. Returns true on the second.</summary>
    private static bool ConfirmNewGarden()
    {
        if (_newGardenConfirm > 0f)
        {
            _newGardenConfirm = 0f;
            return true;
        }
        _newGardenConfirm = NewGardenConfirmWindow;
        return false;
    }

    /// <summary>Wipes the saved garden and starts a fresh one, closing the History screen and clearing the log.</summary>
    private static World StartNewGarden(string savePath)
    {
        SaveSystem.Delete(savePath);
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
