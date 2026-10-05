using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public static partial class Game
{
    /// <summary>What the start menu decided: which kept garden, and whether to carry on with it or begin a new one (with the fixed or a grown terrain).</summary>
    private sealed record MenuChoice(int Slot, bool Resume, bool GrowTerrain, int Size, Era StartEra = Era.StoneAge);

    /// <summary>A second tap within this many seconds confirms starting a new garden over a kept one.</summary>
    private const float MenuConfirmSeconds = 4f;

    /// <summary>
    /// The start menu: pick a kept garden (its year and how many Bramblekin live in it, or "empty") and either
    /// resume it or begin a new one — on the original terrain, or on one grown from a random seed. A new garden
    /// over a kept one asks for a second tap. Returns null if the window is closed.
    /// </summary>
    private static MenuChoice? ShowMenu()
    {
        var summaries = new SaveSystem.SaveSummary?[SaveSystem.Slots + 1];
        for (int slot = 1; slot <= SaveSystem.Slots; slot++)
            summaries[slot] = SaveSystem.Peek(SaveSystem.SlotPath(slot));

        // Start on the garden last played; if that one is gone, the most recently saved.
        int chosen = _gardenSlot is >= 1 and <= SaveSystem.Slots ? _gardenSlot : 1;
        if (summaries[chosen] is null)
        {
            for (int slot = 1; slot <= SaveSystem.Slots; slot++)
            {
                if (summaries[slot] is { } candidate && (summaries[chosen] is null || candidate.SavedAt > summaries[chosen]!.SavedAt))
                    chosen = slot;
            }
        }

        string? auto = Environment.GetEnvironmentVariable("GARDEN_MENU_AUTO"); // A development aid: "resume", "original" or "random" picks after a few frames.
        int frames = 0;
        int size = Math.Clamp((int)Preferences.Get(MapSizeSetting, MapSize.Small), 0, TerrainData.MapSizes.Length - 1);
        int startAge = Math.Clamp((int)Preferences.Get(StartAgeSetting, StartAge.Stone), 0, 3);
        int armed = 0; // 0 nothing, 1 new (fixed), 2 new (grown): tapped once over a kept garden.
        float armedFor = 0f;
        while (!Raylib.WindowShouldClose())
        {
            MusicPlayer.Update(Raylib.GetFrameTime(), null, silent: false); // the main theme
            float uiScale = UiScale;
            int width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight();
            int buttonHeight = (int)(96 * uiScale), gap = (int)(18 * uiScale), chipHeight = (int)(68 * uiScale);
            int wide = Math.Min((int)(width * 0.8f), (int)(1000 * uiScale));
            int left = (width - wide) / 2;
            int titleSize = ScaledFontSize(2.6f), subtitleSize = ScaledFontSize(0.8f);

            int y = (int)(height * 0.08f);
            int titleY = y;
            y += titleSize + gap * 2;

            // The kept gardens, side by side.
            int chipWidth = (wide - gap * (SaveSystem.Slots - 1)) / SaveSystem.Slots;
            var chips = new UiButton[SaveSystem.Slots + 1];
            for (int slot = 1; slot <= SaveSystem.Slots; slot++)
                chips[slot] = new UiButton(new Rectangle(left + (slot - 1) * (chipWidth + gap), y, chipWidth, buttonHeight));
            y += buttonHeight + gap;
            int detailY = y;
            y += (subtitleSize + gap / 2) * 2 + gap * 2;

            int sizeCaptionY = y;
            y += subtitleSize + gap / 2;
            var sizeChips = new UiButton[TerrainData.MapSizes.Length];
            int sizeWidth = (wide - gap * (sizeChips.Length - 1)) / sizeChips.Length;
            for (int k = 0; k < sizeChips.Length; k++)
                sizeChips[k] = new UiButton(new Rectangle(left + k * (sizeWidth + gap), y, sizeWidth, chipHeight));
            y += chipHeight + gap;
            int ageCaptionY = y;
            y += subtitleSize + gap / 2;
            var ageChips = new UiButton[4];
            int ageWidth = (wide - gap * (ageChips.Length - 1)) / ageChips.Length;
            for (int k = 0; k < ageChips.Length; k++)
                ageChips[k] = new UiButton(new Rectangle(left + k * (ageWidth + gap), y, ageWidth, chipHeight));
            y += chipHeight + gap * 2;

            var resume = new UiButton(new Rectangle(left, y, wide, buttonHeight));
            y += buttonHeight + gap;
            var newFixed = new UiButton(new Rectangle(left, y, wide, buttonHeight));
            y += buttonHeight + gap;
            var newGrown = new UiButton(new Rectangle(left, y, wide, buttonHeight));

            SaveSystem.SaveSummary? kept = summaries[chosen];
            armedFor = MathF.Max(0f, armedFor - Raylib.GetFrameTime());
            if (armedFor <= 0f)
                armed = 0;

            bool pressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
            Vector2 mouse = Raylib.GetMousePosition();
            if (pressed)
            {
                for (int slot = 1; slot <= SaveSystem.Slots; slot++)
                {
                    if (chips[slot].Contains(mouse) && slot != chosen)
                    {
                        chosen = slot;
                        armed = 0;
                    }
                }
                for (int k = 0; k < sizeChips.Length; k++)
                {
                    if (sizeChips[k].Contains(mouse) && k != size)
                    {
                        size = k;
                        armed = 0;
                        Preferences.Set(MapSizeSetting, (MapSize)k);
                    }
                }
                for (int k = 0; k < ageChips.Length; k++)
                {
                    if (ageChips[k].Contains(mouse) && k != startAge)
                    {
                        startAge = k;
                        armed = 0;
                        Preferences.Set(StartAgeSetting, (StartAge)k);
                    }
                }
                if (kept is not null && resume.Contains(mouse))
                    return new MenuChoice(chosen, Resume: true, GrowTerrain: false, size);
                foreach ((UiButton button, int which) in new[] { (newFixed, 1), (newGrown, 2) })
                {
                    if (!button.Contains(mouse))
                        continue;
                    if (kept is null || armed == which)
                        return new MenuChoice(chosen, Resume: false, GrowTerrain: which == 2, size, (Era)startAge);
                    armed = which;
                    armedFor = MenuConfirmSeconds;
                }
            }

            if (auto is not null && ++frames > 5)
                return new MenuChoice(chosen, Resume: auto == "resume", GrowTerrain: auto == "random", size, (Era)startAge);

            Raylib.BeginDrawing();
            Raylib.DrawRectangleGradientV(0, 0, width, height, new Color(150, 200, 235, 255), new Color(95, 150, 80, 255));
            DrawCentred("Garden Guardians", width / 2, titleY, titleSize, new Color(40, 55, 30, 255));
            for (int slot = 1; slot <= SaveSystem.Slots; slot++)
                chips[slot].Draw($"Garden {slot}", highlighted: slot == chosen, disabled: summaries[slot] is null && slot != chosen);
            string first = kept is null ? $"Garden {chosen} is empty" : $"Garden {chosen}: year {kept.Year}, {kept.Kin} Bramblekin in {kept.Groups} {(kept.Groups == 1 ? "clan" : "clans")}";
            string second = kept is null ? "Begin a new garden below" : $"{(kept.Grown ? "A grown terrain" : "The original terrain")}, saved {kept.SavedAt.ToLocalTime():d MMM HH:mm}";
            Color ink = new(40, 55, 30, 255);
            DrawCentred(Fit(first, subtitleSize, wide), width / 2, detailY, subtitleSize, ink);
            DrawCentred(Fit(second, subtitleSize, wide), width / 2, detailY + subtitleSize + gap / 2, subtitleSize, ink);
            DrawCentred("Size of a random terrain", width / 2, sizeCaptionY, subtitleSize, ink);
            for (int k = 0; k < sizeChips.Length; k++)
                sizeChips[k].Draw(TerrainData.MapSizes[k].Name, highlighted: k == size);
 DrawCentred("Age a new garden starts in (for testing)", width / 2, ageCaptionY, subtitleSize, ink);
            string[] ageNames = { "Stone", "Farming", "Village", "Kingdom" };
            for (int k = 0; k < ageChips.Length; k++)
                ageChips[k].Draw(ageNames[k], highlighted: k == startAge);
            resume.Draw(kept is null ? "Resume" : $"Resume garden {chosen}", highlighted: kept is not null, disabled: kept is null);
            string erase = kept is null ? "" : $"Erase garden {chosen}? Tap again";
            newFixed.Draw(armed == 1 ? erase : "New garden: original terrain", highlighted: armed == 1);
            newGrown.Draw(armed == 2 ? erase : "New garden: random terrain", highlighted: armed == 2);
            Raylib.EndDrawing();
            if (Environment.GetEnvironmentVariable("GARDEN_MENU") == "1" && DebugShot.Finished())
                return null; // A development picture of the menu itself.
        }
        return null;
    }

    private static void DrawCentred(string text, int centreX, int y, int fontSize, Color colour) =>
        Raylib.DrawText(text, centreX - Raylib.MeasureText(text, fontSize) / 2, y, fontSize, colour);

    /// <summary>
    /// Makes a world (growing a big terrain, measuring its water and settling its people takes a while) on another thread, while this one keeps
    /// the window alive and shows a loading screen with a progress bar (see <see cref="Loading"/>).
    /// </summary>
    private static World MakeWorld(Func<World> make)
    {
        Loading.Reset();
        Task<World> task = Task.Run(make);
        while (!task.IsCompleted)
        {
            DrawLoadingFrame();
            if (Environment.GetEnvironmentVariable("GARDEN_MENU") == "3" && DebugShot.Finished())
                Environment.Exit(0); // A development picture of the loading screen.
        }
        return task.GetAwaiter().GetResult();
    }

    private static void DrawLoadingFrame()
    {
        float uiScale = UiScale;
        int width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight();
        int titleSize = ScaledFontSize(2.6f), textSize = ScaledFontSize(0.9f);
        Color ink = new(40, 55, 30, 255);
        Raylib.BeginDrawing();
        Raylib.DrawRectangleGradientV(0, 0, width, height, new Color(150, 200, 235, 255), new Color(95, 150, 80, 255));
        DrawCentred("Garden Guardians", width / 2, (int)(height * 0.08f), titleSize, ink);
        int barWidth = Math.Min((int)(width * 0.7f), (int)(900 * uiScale)), barHeight = (int)(44 * uiScale);
        int barX = (width - barWidth) / 2, barY = (int)(height * 0.45f);
        DrawCentred(Loading.Text + "...", width / 2, barY - textSize - (int)(24 * uiScale), textSize, ink);
        Raylib.DrawRectangle(barX, barY, barWidth, barHeight, new Color(235, 235, 225, 255));
        Raylib.DrawRectangle(barX + 4, barY + 4, (int)((barWidth - 8) * Loading.Fraction), barHeight - 8, new Color(230, 190, 60, 255));
        Raylib.DrawRectangleLines(barX, barY, barWidth, barHeight, ink);
        DrawCentred($"{(int)(Loading.Fraction * 100f)}%", width / 2, barY + barHeight + (int)(20 * uiScale), textSize, ink);
        Raylib.EndDrawing();
    }
}
