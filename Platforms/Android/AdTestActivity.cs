#if DEBUG
// =============================================================================
//  A Debug-only diagnostic screen: a plain Android activity (not the game's
//  NativeActivity, no raylib, no PopupWindow) inside the game's own app, with
//  the game's own app id, asking for Google's test banner. Opened from the
//  Settings page ("Ad test"). If a test ad shows here but not in the game, the
//  game's NativeActivity setup is the cause; if it fails here too, Google is
//  refusing the app itself (compare with the separate GG Ad Test app).
// =============================================================================

using Android.Views;
using Android.Widget;
using Google.Android.Gms.Ads;

namespace GardenGuardians;

[Activity(Label = "Ad test", Exported = false)]
public class AdTestActivity : Activity
{
    private TextView? _status;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(48, 160, 48, 48);
        _status = new TextView(this) { TextSize = 18 };
        layout.AddView(_status);
        var ad = new AdView(this) { AdUnitId = AdConfig.TestBannerUnitId };
        ad.AdSize = AdSize.Banner;
        ad.AdListener = new Listener(this);
        layout.AddView(ad, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        SetContentView(layout);

        Report($"Ad test inside the game's app: package {PackageName}, app id {AdConfig.AppId}, SDK {MobileAds.Version}");
        Report("Asking for Google's test banner on a plain Android screen... (Back returns to the game)");
        ad.LoadAd(new AdRequest.Builder().Build());
    }

    public void Report(string line) => RunOnUiThread(() =>
    {
        if (_status is not null)
            _status.Text += line + "\n";
    });

    private sealed class Listener : AdListener
    {
        private readonly AdTestActivity _owner;

        public Listener(AdTestActivity owner) => _owner = owner;

        public override void OnAdLoaded() => _owner.Report("LOADED: a test ad should be showing below.");

        public override void OnAdFailedToLoad(LoadAdError error) =>
            _owner.Report($"FAILED: code {error.Code} [{error.Domain}]: {error.Message}");
    }
}
#endif
