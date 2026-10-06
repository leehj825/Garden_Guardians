// =============================================================================
//  The banner ad (Google AdMob), laid over the bottom of the game's window
// -----------------------------------------------------------------------------
//  The game draws with raylib straight into the NativeActivity's window surface, and
//  Android draws no views into a window whose surface an app has taken for itself, so
//  a view added to the activity (AddContentView) never shows. The banner is therefore
//  put in a PopupWindow, a window of its own along the bottom edge. Everything
//  here runs on, or is posted to, the Android UI thread. The game thread only
//  talks to it through AdBanner (Source/Game/AdBanner.cs): it asks for the banner
//  to be shown or hidden, and reads back how tall it is so the UI stays clear of it.
// =============================================================================

using Android.Content;
using Android.Gms.Ads;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace GardenGuardians;

internal static class AndroidAds
{
    private const string LogTag = "GardenGuardians";

    private static AdView? _view;
    private static PopupWindow? _popup;
    private static View? _anchor;
    private static bool _visible;
    private static int _bottomMargin;

    /// <summary>Starts AdMob and puts a hidden banner at the bottom of <paramref name="activity"/>; it is shown once the game asks (<see cref="AdBanner.Show"/>).</summary>
    public static void Start(Activity activity)
    {
        AdBanner.Status = "starting";
        _visible = AdBanner.Wanted; // (the game may have asked before this was hooked up)
        AdBanner.SetPlatformVisible = visible =>
        {
            _visible = visible;
            activity.RunOnUiThread(Apply);
        };
        try
        {
            MobileAds.Initialize(activity);
            activity.RunOnUiThread(() =>
            {
              try
              {
                var view = new AdView(activity) { AdUnitId = AdConfig.BannerUnitId };
                view.AdSize = AdSize.Banner; // (320 x 50 dp, centred along the bottom edge)
                view.AdListener = new Listener(AdSize.Banner.GetHeightInPixels(activity));
                var popup = new PopupWindow(view, ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
                {
                    Focusable = false, // (touches outside the banner still reach the game)
                    OutsideTouchable = false,
                };
                popup.SetBackgroundDrawable(null);
                _view = view;
                _popup = popup;
                view.LoadAd(new AdRequest.Builder().Build());
                // A popup can only be shown once the activity's window is attached.
                _anchor = activity.Window?.DecorView;
                _anchor?.Post(Apply);
                AdBanner.Status = "requested, waiting for an ad";
              }
              catch (Exception ex)
              {
                Log.Error(LogTag, $"Banner ad setup failed: {ex}");
                AdBanner.Status = $"SETUP FAILED {ex.GetType().Name}: {ex.Message}";
              }
            });
        }
        catch (Exception ex)
        {
            // No ads is better than no game: carry on without the banner.
            Log.Error(LogTag, $"Could not start the banner ad: {ex}");
            AdBanner.SetPlatformVisible = null;
            AdBanner.Status = $"START FAILED {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Keeps the banner above the navigation bar (the bottom inset, in pixels).</summary>
    public static void SetBottomMargin(int pixels)
    {
        _bottomMargin = pixels;
        Apply();
    }

    public static void Pause() => _view?.Pause();

    public static void Resume() => _view?.Resume();

    public static void Destroy()
    {
        _popup?.Dismiss();
        _popup = null;
        _anchor = null;
        _view?.Destroy();
        _view = null;
    }

    /// <summary>Brings the banner in line with whether it is wanted and where the navigation bar is: shown or taken down, above the bar. UI thread only.</summary>
    private static void Apply()
    {
        if (_popup is not { } popup || _anchor is not { WindowToken: not null } anchor)
            return;
        if (!_visible)
        {
            if (popup.IsShowing)
                popup.Dismiss();
        }
        else if (!popup.IsShowing)
        {
            popup.ShowAtLocation(anchor, GravityFlags.Bottom | GravityFlags.CenterHorizontal, 0, _bottomMargin);
            if (!AdBanner.Status.Contains("loaded") && !AdBanner.Status.Contains("FAILED"))
                AdBanner.Status = "popup shown, waiting for an ad";
        }
        else
            popup.Update(0, _bottomMargin, -1, -1); // (keeps it above the navigation bar; the size stays as it is)
    }

    /// <summary>Reserves the banner's height in the UI once an ad has loaded (and gives it back if the next one fails).</summary>
    private sealed class Listener : AdListener
    {
        private readonly int _heightPixels;

        public Listener(int heightPixels) => _heightPixels = heightPixels;

        public override void OnAdLoaded()
        {
            AdBanner.LoadedHeightPx = _heightPixels;
            AdBanner.Status = $"loaded ({_heightPixels} px tall)";
        }

        public override void OnAdFailedToLoad(LoadAdError error)
        {
            Log.Warn(LogTag, $"Banner ad failed to load: {error.Message}");
            AdBanner.LoadedHeightPx = 0;
            AdBanner.Status = $"FAILED to load, code {error.Code}: {error.Message}";
        }

        public override void OnAdImpression() => AdBanner.Status += " - shown";
    }
}
