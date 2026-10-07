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
using Android.Content.PM;
using Google.Android.Gms.Ads; // (the 124.x binding renamed the namespace from Android.Gms.Ads)
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
                AdBanner.Status = $"requested {Label(AdConfig.BannerUnitId)}, waiting for an ad";
#if DEBUG
                string manifestId = "unreadable";
                try
                {
                    manifestId = activity.PackageManager?.GetApplicationInfo(activity.PackageName!, PackageInfoFlags.MetaData)?.MetaData?.GetString("com.google.android.gms.ads.APPLICATION_ID") ?? "MISSING from the manifest";
                }
                catch (Exception ex)
                {
                    manifestId = ex.GetType().Name;
                }
                _ = Task.Run(CheckNetwork);
                AdBanner.Setup = $"SDK {MobileAds.Version?.ToString() ?? "?"}, {manifestId}, {activity.PackageName}";
                // With no log to read, ask Google's test unit in a hidden view and say what it answered. (The game's live unit is not
                // asked from a Debug build: repeated live requests from a test install can count as invalid traffic.)
                Probe(activity, AdConfig.TestBannerUnitId);
                // The Settings page's "Ad test" button opens a plain Android screen with a test banner (AdTestActivity).
                AdBanner.OpenTestScreen = () => activity.RunOnUiThread(() => activity.StartActivity(new Android.Content.Intent(activity, typeof(AdTestActivity))));
#endif
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

    /// <summary>Plain HTTPS requests to Google's servers from managed code: shows whether the app can reach them at all, whatever the ad SDK says.</summary>
    private static async Task CheckNetwork()
    {
        var results = new List<string>();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        foreach (var (label, url) in new[]
        {
            ("google", "https://www.google.com/generate_204"),
            ("doubleclick", "https://googleads.g.doubleclick.net/"),
            ("pagead2", "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js"),
        })
        {
            try
            {
                using var response = await http.GetAsync(url);
                results.Add($"{label} {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                results.Add($"{label} {ex.GetType().Name}");
            }
            AdBanner.Net = string.Join(", ", results);
        }
    }

    /// <summary>Reads what the ad SDK wrote to the app's own log (an app may read its own lines of logcat) and keeps the last few, for the stats bar.</summary>
    private static async Task ReadSdkLog()
    {
        try
        {
            await Task.Delay(500); // (the SDK writes its reasons just after the failure is reported)
            // The whole of the app's own log, not just the ad SDK's tag: the lines that mention ads, Google Play services, HTTP or the
            // WebView, or are warnings and errors, so a refusal's reason can show up wherever it was written.
            Java.Lang.Process? process = Java.Lang.Runtime.GetRuntime()?.Exec(new[] { "logcat", "-d", "-t", "1500", "-v", "brief" });
            if (process?.InputStream is null)
                return;
            using var reader = new Java.IO.BufferedReader(new Java.IO.InputStreamReader(process.InputStream));
            var lines = new List<string>();
            string? line;
            string[] interesting = { "Ads", "ads", "403", "HTTP", "http", "gms", "Gms", "GMS", "settings", "WebView", "chromium", "Exception", "denied", "blocked", "refus" };
            while ((line = reader.ReadLine()) is not null)
            {
                bool warning = line.Length > 2 && (line[0] is 'W' or 'E') && line[1] == '/';
                if (!warning && !interesting.Any(word => line.Contains(word, StringComparison.Ordinal)))
                    continue;
                string text = line.Length > 175 ? line[..175] : line;
                if (lines.Count == 0 || lines[^1] != text) // (a line repeated straight away is shown once)
                    lines.Add(text);
            }
            AdBanner.SdkLog = lines.TakeLast(16).ToArray();
        }
        catch (Exception ex)
        {
            AdBanner.SdkLog = new[] { $"could not read the log: {ex.GetType().Name}: {ex.Message}" };
        }
    }

    private static string Short(string? message) => (message ?? "").Replace("Received error HTTP response code: ", "HTTP ");

    private static string Label(string unitId) => unitId == AdConfig.TestBannerUnitId ? "test unit" : "live unit";

    private static readonly Dictionary<string, string> ProbeResults = new();

    /// <summary>Loads one ad in a view that is never shown, and reports the answer in <see cref="AdBanner.Probes"/>. UI thread only.</summary>
    private static void Probe(Activity activity, string unitId)
    {
        var view = new AdView(activity) { AdUnitId = unitId };
        view.AdSize = AdSize.Banner;
        view.AdListener = new ProbeListener(unitId);
        ProbeResults[unitId] = "asking";
        UpdateProbes();
        view.LoadAd(new AdRequest.Builder().Build());
    }

    private static void UpdateProbes() =>
        AdBanner.Probes = string.Join("; ", ProbeResults.Select(p => $"{Label(p.Key)}: {p.Value}"));

    private sealed class ProbeListener : AdListener
    {
        private readonly string _unitId;

        public ProbeListener(string unitId) => _unitId = unitId;

        public override void OnAdLoaded()
        {
            ProbeResults[_unitId] = "FILLED";
            UpdateProbes();
        }

        public override void OnAdFailedToLoad(LoadAdError error)
        {
            ProbeResults[_unitId] = $"failed {error.Code} ({Short(error.Message)})";
            UpdateProbes();
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
            _ = Task.Run(ReadSdkLog);
            AdBanner.Status = $"FAILED to load, code {error.Code}: {Short(error.Message)} [{error.Domain}{(error.Cause is { } cause ? $", cause {Short(cause.Message)}" : "")}]";
        }

        public override void OnAdImpression() => AdBanner.Status += " - shown";
    }
}
