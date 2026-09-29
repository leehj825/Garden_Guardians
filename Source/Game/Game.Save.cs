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

    /// <summary>The garden button, shown beside "New" while the History screen is open: tapping it keeps this garden and opens the next.</summary>
    private static UiButton GardenSlotButton(UiButton newGardenButton, int margin) =>
        new(new Rectangle(newGardenButton.Bounds.X + newGardenButton.Bounds.Width + margin, newGardenButton.Bounds.Y,
            (int)(340 * UiScale), newGardenButton.Bounds.Height));

    /// <summary>
    /// Keeps <paramref name="world"/> (saved to its slot) and opens the next
    /// kept garden — carrying on where it was left, or a fresh one if that
    /// slot's empty. The History screen stays open on the new garden's story.
    /// </summary>
    private static World SwitchGarden(World world)
    {
        SaveSystem.Save(world, GardenPath);
        _gardenSlot = _gardenSlot % SaveSystem.Slots + 1;
        Preferences.Set(GardenSetting, (GardenSlot)_gardenSlot);
        _debugLogs.Clear();
        _newGardenConfirm = 0f;
        _chronicleScroll = 0f;
        _chronicleDragY = null;
        if (SaveSystem.TryLoad(GardenPath, new Random()) is { } saved)
        {
            AddEventLog($"[SAVE] Garden {_gardenSlot}: the garden carries on in Year {saved.Year}");
            return saved;
        }
        World fresh = NewWorld();
        SaveSystem.Save(fresh, GardenPath);
        AddEventLog($"[SAVE] Garden {_gardenSlot}: a new garden begins");
        return fresh;
    }

    private static World NewWorld()
    {
        var rng = new Random();
        return new World(new Terrain(TerrainData.RandomIndex(rng)), rng, InitialKinCount);
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
