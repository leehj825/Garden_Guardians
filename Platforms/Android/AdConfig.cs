using Android.App;

// Debug builds (the APKs CI makes for every branch) use Google's TEST ids: they always fill with a clearly
// marked test ad, and are safe to tap. Release builds use the game's own AdMob ids (from the AdMob console);
// do not tap those live ads while testing: clicking your own ads can get an AdMob account suspended.
// (A new ad unit of one's own can also take hours to start filling; test ids show at once.)
[assembly: MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = GardenGuardians.AdConfig.AppId)]

namespace GardenGuardians;

internal static class AdConfig
{
#if DEBUG
    /// <summary>The AdMob app id (also written into the manifest by the attribute above): Google's test app.</summary>
    public const string AppId = "ca-app-pub-3940256099942544~3347511713";

    /// <summary>The banner ad unit shown along the bottom of the garden: Google's test banner.</summary>
    public const string BannerUnitId = "ca-app-pub-3940256099942544/6300978111";
#else
    /// <summary>The AdMob app id (also written into the manifest by the attribute above).</summary>
    public const string AppId = "ca-app-pub-4400173019354346~3719730997";

    /// <summary>The banner ad unit shown along the bottom of the garden.</summary>
    public const string BannerUnitId = "ca-app-pub-4400173019354346/4753521775";
#endif
}
