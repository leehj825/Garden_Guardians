using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Responsive layout: the game runs upright (portrait) as well as sideways (landscape), and the screen can change under it —
/// the phone turned, a desktop window dragged to another shape. Every panel and button reads the CURRENT screen size each
/// frame (never a size remembered from startup), through the helpers here.
/// </summary>
public static partial class Game
{
    /// <summary>The screen width the UI constants were tuned against when the screen is wider than tall (see <see cref="UiScale"/>).</summary>
    private const float ReferenceScreenWidth = 1920f;

    /// <summary>The screen width the UI constants are scaled against when the screen is taller than wide: the buttons and text are the same shapes, but a column of them must fit across a phone held upright.</summary>
    private const float PortraitReferenceWidth = 1600f;

    /// <summary>True while the screen is taller than it is wide.</summary>
    internal static bool IsPortrait => Raylib.GetScreenHeight() > Raylib.GetScreenWidth();

    /// <summary>
    /// One scale factor that every hardcoded UI pixel constant (button geometry, font sizes) is multiplied by, so the UI keeps
    /// its proportions — and stays tappable exactly where it is drawn — on any screen. Wide screens scale off 1920 units across,
    /// tall ones off 1600, so turning the phone keeps the buttons about as big as a finger.
    /// </summary>
    internal static float UiScale => Raylib.GetScreenWidth() / (IsPortrait ? PortraitReferenceWidth : ReferenceScreenWidth);

    /// <summary>The screen Y where the usable screen ends: the bottom edge, above the system's navigation bar and the ad banner when they sit there (see <see cref="ScreenInsets"/>, <see cref="AdBanner"/>).</summary>
    internal static int UiBottom => Raylib.GetScreenHeight() - ScreenInsets.Bottom - AdBanner.HeightPx;

    /// <summary>The bottom edge of the row(s) of buttons along the top, set each frame by the main loop; panels and banners start below it.</summary>
    private static int _topBarBottom;

    /// <summary>Where the free space under the top buttons starts: below the map-guide toggles when they run across the screen (portrait), else the same as <see cref="_topBarBottom"/> (landscape stacks them down the left side instead).</summary>
    private static int _contentTop;

    /// <summary>The widest a centred caption or banner may be: most of the screen.</summary>
    private static int CaptionMaxWidth(float landscapeShare) => (int)(Raylib.GetScreenWidth() * (IsPortrait ? 0.92f : landscapeShare));

    /// <summary>The largest font size up to <paramref name="size"/> at which <paramref name="text"/> fits <paramref name="maxWidth"/> pixels across (never below 10).</summary>
    private static int FitFontSize(string text, int size, int maxWidth)
    {
        int width = Raylib.MeasureText(text, size);
        if (width <= maxWidth || width <= 0)
            return size;
        return Math.Max(10, (int)(size * (long)maxWidth / width));
    }

    // --- The window's size, when the platform changes it behind raylib's back ---------------------

    /// <summary>
    /// Set by a platform whose window can change shape without raylib hearing of it (Android turning the phone): the window's
    /// current size in pixels, or null while it is not known. Desktop leaves it null — raylib follows a resized window itself.
    /// </summary>
    public static Func<(int Width, int Height)?>? PollNativeWindowSize;

    /// <summary>Set with <see cref="PollNativeWindowSize"/>: tells raylib the window is now this size (its screen, viewport and touch mapping).</summary>
    public static Action<int, int>? ResizeNativeWindow;

    /// <summary>The size the platform last reported (null until it first has).</summary>
    private static (int Width, int Height)? _lastNativeSize;

    /// <summary>Called at the top of every frame of every screen: when the platform reports a new window size, carries it over to raylib before anything is laid out.</summary>
    private static void SyncWindowSize()
    {
        if (PollNativeWindowSize?.Invoke() is not { } size || size.Width <= 0 || size.Height <= 0)
            return;
        // The first size is the one raylib started with; only a change from it is a turn of the phone.
        if (_lastNativeSize is { } last && last != size)
            ResizeNativeWindow?.Invoke(size.Width, size.Height);
        _lastNativeSize = size;
    }
}
