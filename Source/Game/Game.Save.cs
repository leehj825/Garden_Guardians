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

    /// <summary>The garden saved last time, or a fresh one if there's none (or it can't be read).</summary>
    private static World LoadOrCreateWorld(string savePath)
    {
        if (SaveSystem.TryLoad(savePath, new Random()) is { } saved)
        {
            AddEventLog($"[SAVE] Welcome back - the garden carries on in Year {saved.Year}");
            return saved;
        }
        return NewWorld();
    }

    private static World NewWorld() => new(new Terrain(size: 100f), new Random(), InitialKinCount);

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
        AddEventLog("[SAVE] A new garden begins");
        return world;
    }
}
