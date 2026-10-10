using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public static partial class Game
{
    private const string MusicVolumeSetting = "musicvolume";
    private const string DetailSetting = "detail";

    private enum DetailLevel
    {
        Normal,
        Low,
    }

    /// <summary>The "back to the start menu?" question is open (see the Menu button): the garden waits, and the question takes every tap.</summary>
    private static bool _confirmMenu;

    // --- The Menu button and its question ------------------------------------------------------

    /// <summary>The Menu button of the game page, after the last button of the top row.</summary>
    private static UiButton MenuButton(Rectangle last, float y, int margin, float height, float uiScale) =>
        new(new Rectangle(last.X + last.Width + margin, y, (int)(150 * uiScale), height * 0.62f)); // (smaller than the others: it is not for everyday use, and the hint box is next to it)

    /// <summary>The box the question is drawn in, centred.</summary>
    private static Rectangle MenuConfirmBox(float uiScale)
    {
        float width = MathF.Min(Raylib.GetScreenWidth() * 0.8f, 1000 * uiScale), height = 420 * uiScale;
        return new Rectangle((Raylib.GetScreenWidth() - width) / 2f, (Raylib.GetScreenHeight() - height) / 2f, width, height);
    }

    private static void MenuConfirmButtons(float uiScale, out UiButton yes, out UiButton no)
    {
        Rectangle box = MenuConfirmBox(uiScale);
        float gap = 24 * uiScale, width = (box.Width - gap * 3) / 2f, height = 120 * uiScale, y = box.Y + box.Height - height - gap;
        yes = new UiButton(new Rectangle(box.X + gap, y, width, height));
        no = new UiButton(new Rectangle(box.X + gap * 2 + width, y, width, height));
    }

    private static void DrawMenuConfirm(float uiScale)
    {
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0, 0, 0, 150));
        Rectangle box = MenuConfirmBox(uiScale);
        Raylib.DrawRectangleRec(box, new Color(235, 232, 215, 255));
        Raylib.DrawRectangleLinesEx(box, 3f, Color.DarkGray);
        Color ink = new(40, 55, 30, 255);
        int title = ScaledFontSize(1.1f), text = ScaledFontSize(0.7f);
        DrawCentred("Back to the main menu?", (int)(box.X + box.Width / 2), (int)(box.Y + 40 * uiScale), title, ink);
        DrawCentred(Fit("The garden is saved first.", text, (int)box.Width - 40), (int)(box.X + box.Width / 2), (int)(box.Y + 40 * uiScale) + title + (int)(24 * uiScale), text, ink);
        MenuConfirmButtons(uiScale, out UiButton yes, out UiButton no);
        yes.Draw("Yes", highlighted: false);
        no.Draw("No", highlighted: true);
    }

    // --- The Settings page ---------------------------------------------------------------------

    /// <summary>The Settings page of the start menu: how loud the music is (tap or drag the bar, or the - and + buttons), and Back. The music plays while it is open, so the change can be heard.</summary>
    private static void ShowSettings()
    {
        while (!Raylib.WindowShouldClose())
        {
            SyncWindowSize();
            MusicPlayer.Update(Raylib.GetFrameTime(), null, silent: false);
            float uiScale = UiScale;
            int width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight();
            int gap = (int)(18 * uiScale), buttonHeight = (int)(96 * uiScale);
            int titleSize = ScaledFontSize(2.6f), textSize = ScaledFontSize(0.9f);
            int wide = Math.Min((int)(width * 0.8f), (int)(1000 * uiScale)), left = (width - wide) / 2;

            int titleY = (int)(height * 0.08f);
            int labelY = titleY + titleSize + gap * 4;
            int barY = labelY + textSize + gap * 2;
            int stepWidth = (int)(130 * uiScale);
            var minus = new UiButton(new Rectangle(left, barY, stepWidth, buttonHeight));
            var plus = new UiButton(new Rectangle(left + wide - stepWidth, barY, stepWidth, buttonHeight));
            var bar = new Rectangle(left + stepWidth + gap, barY, wide - (stepWidth + gap) * 2, buttonHeight);
            var detail = new UiButton(new Rectangle(left, barY + buttonHeight + gap * 3, wide, buttonHeight));
            var back = new UiButton(new Rectangle(left, detail.Bounds.Y + buttonHeight + gap * 2, wide, buttonHeight));
            // (Debug builds on Android: a diagnostic screen with a test banner, see AdTestActivity.)
            UiButton? adTest = AdBanner.OpenTestScreen is null ? null : new UiButton(new Rectangle(left, back.Bounds.Y + buttonHeight + gap * 2, wide, buttonHeight));

            Vector2 mouse = Raylib.GetMousePosition();
            bool pressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
            float volume = MusicPlayer.Volume;
            if (pressed && back.Contains(mouse))
                return;
            if (pressed && detail.Contains(mouse))
            {
                World.LowDetail = !World.LowDetail;
                Preferences.Set(DetailSetting, World.LowDetail ? DetailLevel.Low : DetailLevel.Normal);
            }
            if (pressed && adTest is not null && adTest.Contains(mouse))
                AdBanner.OpenTestScreen?.Invoke();
            if (pressed && minus.Contains(mouse))
                volume = MathF.Round((volume - 0.1f) * 10f) / 10f;
            else if (pressed && plus.Contains(mouse))
                volume = MathF.Round((volume + 0.1f) * 10f) / 10f;
            else if ((pressed || Raylib.IsMouseButtonDown(MouseButton.Left)) && Raylib.CheckCollisionPointRec(mouse, bar))
                volume = MathF.Round((mouse.X - bar.X) / bar.Width * 20f) / 20f; // (in steps of 5 %)
            volume = Math.Clamp(volume, 0f, 1f);
            if (MathF.Abs(volume - MusicPlayer.Volume) > 0.001f)
            {
                MusicPlayer.Volume = volume;
                Preferences.SetNumber(MusicVolumeSetting, volume);
            }

            Raylib.BeginDrawing();
            Raylib.DrawRectangleGradientV(0, 0, width, height, new Color(150, 200, 235, 255), new Color(95, 150, 80, 255));
            Color ink = new(40, 55, 30, 255);
            DrawCentred("Settings", width / 2, titleY, titleSize, ink);
            DrawCentred($"Music volume: {(int)MathF.Round(MusicPlayer.Volume * 100f)}%{(MusicPlayer.Volume <= 0f ? " (off)" : "")}", width / 2, labelY, textSize, ink);
            minus.Draw("-", highlighted: false, disabled: MusicPlayer.Volume <= 0f);
            plus.Draw("+", highlighted: false, disabled: MusicPlayer.Volume >= 1f);
            Raylib.DrawRectangleRec(bar, new Color(235, 235, 225, 255));
            Raylib.DrawRectangle((int)bar.X + 4, (int)bar.Y + 4, (int)((bar.Width - 8) * MusicPlayer.Volume), (int)bar.Height - 8, new Color(230, 190, 60, 255));
            Raylib.DrawRectangleLinesEx(bar, 2f, ink);
            detail.Draw(World.LowDetail ? "Detail: low (no grass)" : "Detail: normal", highlighted: World.LowDetail);
            back.Draw("Back", highlighted: true);
            if (adTest is not null) // (a Debug build on Android: the game's own banner's state, and the plain test screen)
            {
                adTest.Draw("Ad test", highlighted: false);
                int statusSize = Math.Max(12, (int)(textSize * 0.8f));
                string status = $"Banner: {AdBanner.Status}";
                DrawCentred(Fit(status, statusSize, (int)(width * 0.94f)), width / 2, (int)adTest.Bounds.Y + buttonHeight + gap, statusSize, ink);
            }
            Raylib.EndDrawing();
        }
    }
}
