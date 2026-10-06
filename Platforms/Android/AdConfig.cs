using Android.App;

// The game's own AdMob ids (from the AdMob console). Do not tap the live ads while testing: clicking
// your own ads can get an AdMob account suspended. For a test device, add its id in the AdMob console
// (Settings > Test devices), or put Google's test ids back here:
//   app    ca-app-pub-3940256099942544~3347511713
//   banner ca-app-pub-3940256099942544/6300978111
[assembly: MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = GardenGuardians.AdConfig.AppId)]

namespace GardenGuardians;

internal static class AdConfig
{
    /// <summary>The AdMob app id (also written into the manifest by the attribute above).</summary>
    public const string AppId = "ca-app-pub-4400173019354346~3719730997";

    /// <summary>The banner ad unit shown along the bottom of the garden.</summary>
    public const string BannerUnitId = "ca-app-pub-4400173019354346/4753521775";
}
