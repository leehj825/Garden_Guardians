// =============================================================================
//  Keeps track of the window's size and the system bars, for the game thread
// -----------------------------------------------------------------------------
//  raylib's Android backend never hears of a rotation, so the game asks here for the
//  window's current size and tells raylib itself (Game.SyncWindowSize). This watches
//  the activity's top view from the UI thread; the game thread only reads the result.
//  The system bars (and a camera notch) are reported as insets, kept in ScreenInsets,
//  so the buttons stay clear of them.
// =============================================================================

using Android.Views;

namespace GardenGuardians;

internal static class WindowWatcher
{
    // Width in the high half, height in the low: one value, so a reader never sees a width from one shape and a height from another.
    private static long _size;

    /// <summary>The window's current size in pixels, or null until it has been laid out.</summary>
    public static (int Width, int Height)? Current => Volatile.Read(ref _size) is var packed && packed != 0 ? ((int)(packed >> 32), (int)(packed & 0xFFFFFFFF)) : null;

    /// <summary>Starts watching <paramref name="activity"/> (call from the UI thread, after its content is set up).</summary>
    public static void Start(Activity activity)
    {
        View? decor = activity.Window?.DecorView;
        if (decor?.ViewTreeObserver is not { } observer)
            return;
        void Refresh()
        {
            if (decor.Width > 0 && decor.Height > 0)
                Volatile.Write(ref _size, ((long)decor.Width << 32) | (uint)decor.Height);
            if (decor.RootWindowInsets is { } insets)
            {
                // (read, never consumed: taking over the decor view's own inset handling would change the window's layout)
                int top, bottom;
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
                    top = bars.Top;
                    bottom = bars.Bottom;
                }
                else
                {
#pragma warning disable CA1422 // (the replacement needs API 30)
                    top = insets.SystemWindowInsetTop;
                    bottom = insets.SystemWindowInsetBottom;
#pragma warning restore CA1422
                }
                if (ScreenInsets.Bottom != bottom)
                    AndroidAds.SetBottomMargin(bottom);
                ScreenInsets.Top = top;
                ScreenInsets.Bottom = bottom;
            }
        }
        observer.GlobalLayout += (_, _) => Refresh();
        Refresh();
    }
}
