// A diagnostic app (see AdTest.csproj): a plain Android screen that asks for Google's test banner and says what came back.

using Android.Views;
using Android.Widget;
using Google.Android.Gms.Ads;

namespace AdTest;

[Activity(Label = "GG Ad Test", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    private const string TestBannerUnitId = "ca-app-pub-3940256099942544/6300978111";

    private TextView? _status;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(48, 160, 48, 48);
        _status = new TextView(this) { TextSize = 18 };
        layout.AddView(_status);
        var ad = new AdView(this) { AdUnitId = TestBannerUnitId };
        ad.AdSize = AdSize.Banner;
        ad.AdListener = new Listener(this);
        layout.AddView(ad, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        SetContentView(layout);

        Report($"GG Ad Test: package {PackageName}, SDK {MobileAds.Version}");
        Report("Asking for Google's test banner...");
        MobileAds.Initialize(this);
        ad.LoadAd(new AdRequest.Builder().Build());
    }

    public void Report(string line) => RunOnUiThread(() =>
    {
        if (_status is not null)
            _status.Text += line + "\n";
    });

    private sealed class Listener : AdListener
    {
        private readonly MainActivity _owner;

        public Listener(MainActivity owner) => _owner = owner;

        public override void OnAdLoaded() => _owner.Report("LOADED: a test ad should be showing below. Ads work in this app.");

        public override void OnAdFailedToLoad(LoadAdError error) =>
            _owner.Report($"FAILED: code {error.Code} [{error.Domain}]: {error.Message}");
    }
}
