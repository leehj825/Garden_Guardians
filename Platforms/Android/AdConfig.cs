using Android.App;

// Google's published TEST ids: they always fill with a clearly marked test ad and are safe to click.
// Before the game is released, swap in the real ones from the AdMob console (the app id here, and the
// banner's ad unit id below). Never click your own live ads: that gets an AdMob account suspended.
[assembly: MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = GardenGuardians.AdConfig.AppId)]

namespace GardenGuardians;

internal static class AdConfig
{
    /// <summary>The AdMob app id (also written into the manifest by the attribute above).</summary>
    public const string AppId = "ca-app-pub-3940256099942544~3347511713";

    /// <summary>The banner ad unit shown along the bottom of the garden.</summary>
    public const string BannerUnitId = "ca-app-pub-3940256099942544/6300978111";
}
