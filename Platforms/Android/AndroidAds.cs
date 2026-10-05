// =============================================================================
//  The banner ad (Google AdMob), laid over the bottom of the game's window
// -----------------------------------------------------------------------------
//  The game draws with raylib into the NativeActivity's surface; the banner is an
//  ordinary Android view added on top of it (Activity.AddContentView). Everything
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
    private static bool _visible;
    private static int _bottomMargin;

    /// <summary>Starts AdMob and puts a hidden banner at the bottom of <paramref name="activity"/>; it is shown once the game asks (<see cref="AdBanner.Show"/>).</summary>
    public static void Start(Activity activity)
    {
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
                var view = new AdView(activity) { AdUnitId = AdConfig.BannerUnitId };
                view.AdSize = AdSize.Banner; // (320 x 50 dp, centred along the bottom edge)
                view.AdListener = new Listener(AdSize.Banner.GetHeightInPixels(activity));
                var layout = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
                    GravityFlags.Bottom | GravityFlags.CenterHorizontal);
                activity.AddContentView(view, layout);
                _view = view;
                Apply();
                view.LoadAd(new AdRequest.Builder().Build());
            });
        }
        catch (Exception ex)
        {
            // No ads is better than no game: carry on without the banner.
            Log.Error(LogTag, $"Could not start the banner ad: {ex}");
            AdBanner.SetPlatformVisible = null;
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
        _view?.Destroy();
        _view = null;
    }

    /// <summary>Brings the banner view in line with whether it is wanted and where the navigation bar is. UI thread only.</summary>
    private static void Apply()
    {
        if (_view is not { } view)
            return;
        view.Visibility = _visible ? ViewStates.Visible : ViewStates.Gone;
        if (view.LayoutParameters is FrameLayout.LayoutParams layout && layout.BottomMargin != _bottomMargin)
        {
            layout.BottomMargin = _bottomMargin;
            view.LayoutParameters = layout;
        }
    }

    /// <summary>Reserves the banner's height in the UI once an ad has loaded (and gives it back if the next one fails).</summary>
    private sealed class Listener : AdListener
    {
        private readonly int _heightPixels;

        public Listener(int heightPixels) => _heightPixels = heightPixels;

        public override void OnAdLoaded() => AdBanner.LoadedHeightPx = _heightPixels;

        public override void OnAdFailedToLoad(LoadAdError error)
        {
            Log.Warn(LogTag, $"Banner ad failed to load: {error.Message}");
            AdBanner.LoadedHeightPx = 0;
        }
    }
}
