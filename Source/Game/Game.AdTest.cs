using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public static partial class Game
{
    /// <summary>
    /// The Ad test page (Settings, Debug builds): loads and shows Google's test rewarded video and writes down what happens, step by step, in a file
    /// that is shown here — so if opening the ad closes the app, reopening this page shows the last step reached (and, for a crash inside Java, its
    /// first few lines). "Clear log" empties the file.
    /// </summary>
    private static void ShowAdTest()
    {
        while (!Raylib.WindowShouldClose())
        {
            SyncWindowSize();
            MusicPlayer.Update(Raylib.GetFrameTime(), null, silent: false);
            AdBanner.PollRewarded?.Invoke();
            float uiScale = UiScale;
            int width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight();
            int gap = (int)(14 * uiScale), buttonHeight = (int)(84 * uiScale);
            int titleSize = ScaledFontSize(1.6f), textSize = ScaledFontSize(0.62f);
            int wide = Math.Min((int)(width * 0.94f), (int)(1400 * uiScale)), left = (width - wide) / 2;
            int half = (wide - gap) / 2;

            int y = (int)(height * 0.03f) + titleSize + gap;
            int statusY = y;
            y += textSize * 2 + gap;
            var load = new UiButton(new Rectangle(left, y, half, buttonHeight));
            var show = new UiButton(new Rectangle(left + half + gap, y, half, buttonHeight));
            y += buttonHeight + gap;
            var clear = new UiButton(new Rectangle(left, y, half, buttonHeight));
            var back = new UiButton(new Rectangle(left + half + gap, y, half, buttonHeight));
            y += buttonHeight + gap;
            int logY = y;

            bool supported = AdBanner.BeginRewarded is not null;
            bool started = AdBanner.ShowRewarded is not null;
            Vector2 mouse = Raylib.GetMousePosition();
            bool pressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
            if (pressed && back.Contains(mouse))
                return;
            if (pressed && load.Contains(mouse) && supported && !started)
                AdBanner.BeginRewarded?.Invoke();
            if (pressed && show.Contains(mouse) && AdBanner.RewardedReady)
                AdBanner.ShowRewarded?.Invoke(() => RewardedDiag.Step("game: the reward was handed over (+3 favour in the game)"));
            if (pressed && clear.Contains(mouse))
                RewardedDiag.Clear();

            Raylib.BeginDrawing();
            Raylib.DrawRectangleGradientV(0, 0, width, height, new Color(150, 200, 235, 255), new Color(95, 150, 80, 255));
            Color ink = new(40, 55, 30, 255);
            DrawCentred("Rewarded ad test", width / 2, (int)(height * 0.03f), titleSize, ink);
            string status = supported ? $"Status: {AdBanner.RewardedStatus}   Ready: {(AdBanner.RewardedReady ? "yes" : "no")}" : "No rewarded ads on this platform (or no unit id)";
            Raylib.DrawText(Fit(status, textSize, wide), left, statusY, textSize, ink);
            Raylib.DrawText(Fit("If the app closes: reopen this page; the last line below is where it stopped.", (int)(textSize * 0.85f), wide), left, statusY + textSize + gap / 2, (int)(textSize * 0.85f), ink with { A = 200 });
            load.Draw(started ? "Load (done)" : "1. Load test ad", highlighted: false, disabled: started || !supported);
            show.Draw("2. Show test ad", highlighted: AdBanner.RewardedReady, disabled: !AdBanner.RewardedReady);
            clear.Draw("Clear log", highlighted: false);
            back.Draw("Back", highlighted: true);

            int lineHeight = textSize + textSize / 5;
            int rows = Math.Max(1, (height - logY - gap) / lineHeight);
            string[] lines = RewardedDiag.Tail(rows);
            if (lines.Length == 0)
                lines = new[] { "(the log is empty)" };
            Raylib.DrawRectangle(left - gap / 2, logY - gap / 2, wide + gap, height - logY, new Color(255, 255, 255, 120));
            for (int i = 0; i < lines.Length; i++)
                Raylib.DrawText(Fit(lines[i], textSize, wide), left, logY + i * lineHeight, textSize, ink);
            Raylib.EndDrawing();
        }
    }
}
