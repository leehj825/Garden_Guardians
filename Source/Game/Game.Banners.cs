using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public static partial class Game
{
    /// <summary>Real seconds a banner stays up…</summary>
    private const float BannerSeconds = 5f;

    /// <summary>…or an urgent one (war, conquest, famine).</summary>
    private const float UrgentBannerSeconds = 8f;

    /// <summary>At most this many banners wait their turn (the oldest routine ones are dropped).</summary>
    private const int BannerQueueCapacity = 4;

    private static readonly Color BannerFill = new(250, 243, 222, 240);
    private static readonly Color UrgentBannerFill = new(250, 222, 205, 245);
    private static readonly Color UrgentInk = new(160, 50, 35, 255);

    private static readonly List<Moment> _bannerQueue = new();
    private static Moment? _banner;
    private static float _bannerTimeLeft;

    /// <summary>Where the banner was last drawn (for taps).</summary>
    private static Rectangle _bannerBounds;

    /// <summary>
    /// Big-moment banners: picks up the World's headlines, shows them one
    /// at a time at the top of the screen (an urgent one longer, and never dropped
    /// for a routine one). It never touches the speed: that's the player's
    /// alone. Held while the History screen is open.
    /// </summary>
    private static void UpdateBanners(World world, float realDeltaTime)
    {
        foreach (Moment moment in world.TakeMoments())
        {
            if (!_alertsOn)
                continue; // Alerts are off: the headline is still in the log and the chronicle.
            _bannerQueue.Add(moment);
            if (_bannerQueue.Count > BannerQueueCapacity)
            {
                int routine = _bannerQueue.FindIndex(m => !m.Urgent);
                _bannerQueue.RemoveAt(routine >= 0 ? routine : 0);
            }
        }
        if (_showChronicle)
            return;

        if (_banner is not null)
        {
            _bannerTimeLeft -= realDeltaTime;
            if (_bannerTimeLeft > 0f)
                return;
            _banner = null;
        }
        if (_bannerQueue.Count == 0)
            return;

        Moment next = _bannerQueue[0];
        _bannerQueue.RemoveAt(0);
        _banner = next;
        _bannerTimeLeft = next.Urgent ? UrgentBannerSeconds : BannerSeconds;
    }

    /// <summary>A tap on the banner: fly the camera to where it happened. Returns true if the tap was the banner's.</summary>
    private static bool TapBanner(Vector2 point, FollowCamera followCamera)
    {
        if (_banner is not { } banner || _showChronicle || !Raylib.CheckCollisionPointRec(point, _bannerBounds))
            return false;
        if (banner.Where is { } where)
            followCamera.FlyTo(where);
        _banner = null;
        return true;
    }

    /// <summary>Forgets every banner (a new garden).</summary>
    private static void ClearBanners()
    {
        _bannerQueue.Clear();
        _banner = null;
    }

    private enum AlertsView
    {
        On,
        Off,
    }

    private const string AlertsSetting = "alerts";

    /// <summary>Whether alert banners are shown (the Alerts button beside Stats switches them; kept in <see cref="Preferences"/>). Off, the headlines still reach the log and the chronicle.</summary>
    private static bool _alertsOn = true;

    /// <summary>Where the Alerts button was drawn last frame, for taps.</summary>
    private static Rectangle _alertsButtonBounds;

    /// <summary>A tap on the Alerts button shows or hides the banners, and remembers the choice. Returns true if the tap was on it.</summary>
    private static bool TapAlertsButton(Vector2 point)
    {
        if (!Raylib.CheckCollisionPointRec(point, _alertsButtonBounds))
            return false;
        _alertsOn = !_alertsOn;
        if (!_alertsOn)
            ClearBanners();
        Preferences.Set(AlertsSetting, _alertsOn ? AlertsView.On : AlertsView.Off);
        return true;
    }

    /// <summary>The current banner, centred at the top of the screen under the buttons: its title, the headline, and what a tap does.</summary>
    private static void DrawBanner(int top)
    {
        if (_banner is not { } banner || _showChronicle || !_alertsOn)
            return;

        int titleSize = ScaledFontSize(0.8f);
        int textSize = ScaledFontSize(0.55f);
        int pad = (int)(16 * UiScale) + 4;
        int maxWidth = CaptionMaxWidth(0.62f) - pad * 2;
        string hint = banner.Where is null ? "" : "Tap to see";
        string text = Fit(banner.Text, textSize, maxWidth);
        int width = Math.Max(Raylib.MeasureText(banner.Title, titleSize),
            Math.Max(Raylib.MeasureText(text, textSize), Raylib.MeasureText(hint, textSize))) + pad * 2;
        int height = titleSize + textSize + pad * 2 + (hint.Length > 0 ? textSize + pad / 2 : 0) + pad / 2;
        int x = (Raylib.GetScreenWidth() - width) / 2;
        int y = top + pad / 2;
        _bannerBounds = new Rectangle(x, y, width, height);

        // Fades out over its last second.
        float fade = Math.Clamp(_bannerTimeLeft, 0f, 1f);
        Color ink = banner.Urgent ? UrgentInk : PanelInk;
        Color fill = banner.Urgent ? UrgentBannerFill : BannerFill;
        Raylib.DrawRectangleRec(_bannerBounds, fill with { A = (byte)(fill.A * fade) });
        Raylib.DrawRectangleLinesEx(_bannerBounds, 3f, ink with { A = (byte)(255 * fade) });
        int line = y + pad;
        Raylib.DrawText(banner.Title, x + pad, line, titleSize, ink with { A = (byte)(255 * fade) });
        line += titleSize + pad / 2;
        Raylib.DrawText(text, x + pad, line, textSize, PanelInk with { A = (byte)(255 * fade) });
        if (hint.Length > 0)
            Raylib.DrawText(hint, x + pad, line + textSize + pad / 2, textSize, PanelInk with { A = (byte)(150 * fade) });
    }
}
