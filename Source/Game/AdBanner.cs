using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The banner ad along the bottom of the screen while a garden is being watched. The ad itself is a platform view (on Android, an
/// AdMob AdView laid over the game: see Platforms/Android/AndroidAds.cs); this class is the platform-independent side. It says
/// whether the banner is wanted, remembers how tall it is, and so lets the UI keep clear of it (<c>Game.UiBottom</c>).
/// The desktop has no ads; GARDEN_AD_TEST=1 draws a grey stand-in bar there so the layout can be checked.
/// </summary>
internal static class AdBanner
{
    /// <summary>Set by the platform: shows or hides its banner view. Null where there are no ads.</summary>
    public static Action<bool>? SetPlatformVisible;

    /// <summary>Set by the platform: the banner view's height in pixels (0 until an ad has loaded; the space is not reserved before then).</summary>
    public static volatile int LoadedHeightPx;

    private static bool _wanted;

    /// <summary>The stand-in's height on the desktop, in pixels, when GARDEN_AD_TEST=1.</summary>
    private static readonly int TestHeightPx = Environment.GetEnvironmentVariable("GARDEN_AD_TEST") == "1" ? 100 : 0;

    /// <summary>The pixels along the bottom edge the banner covers right now, or 0 when none is showing.</summary>
    public static int HeightPx => _wanted ? Math.Max(LoadedHeightPx, TestHeightPx) : 0;

    /// <summary>Wanted while a garden is on the screen; not on the menu, the settings or the loading screen.</summary>
    public static void Show(bool wanted)
    {
        if (_wanted == wanted)
            return;
        _wanted = wanted;
        SetPlatformVisible?.Invoke(wanted);
    }

    /// <summary>On the desktop test run: a grey bar where the ad would be.</summary>
    public static void DrawTestBar()
    {
        if (!_wanted || TestHeightPx == 0)
            return;
        int width = Raylib.GetScreenWidth(), y = Raylib.GetScreenHeight() - TestHeightPx;
        Raylib.DrawRectangle(0, y, width, TestHeightPx, new Color(70, 70, 70, 255));
        const string label = "Banner ad (test)";
        int size = TestHeightPx / 3;
        Raylib.DrawText(label, (width - Raylib.MeasureText(label, size)) / 2, y + (TestHeightPx - size) / 2, size, Color.RayWhite);
    }
}
