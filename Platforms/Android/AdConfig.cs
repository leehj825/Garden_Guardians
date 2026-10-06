using Android.App;

// Debug builds (the APKs CI makes for every branch) use Google's sample app id and TEST banner unit: they always fill with a
// clearly marked test ad, from any app, and are safe to tap. (With the old 23.x and 24.x SDKs even those got HTTP 403, so the
// game's own app id was tried in a Debug build too; with SDK 25.5 the app settings load, but the ad request itself still got 403
// for the game's own app id: trying the sample id again tells whether that is down to the AdMob app.) Release builds use the
// game's own app id and banner unit, from the AdMob console. Do not tap live ads while testing: clicking your own ads can get an
// AdMob account suspended.
[assembly: MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = GardenGuardians.AdConfig.AppId)]

namespace GardenGuardians;

internal static class AdConfig
{
    /// <summary>Google's test banner: always fills, from any app.</summary>
    public const string TestBannerUnitId = "ca-app-pub-3940256099942544/6300978111";

    /// <summary>The game's own banner unit.</summary>
    public const string LiveBannerUnitId = "ca-app-pub-4400173019354346/4753521775";

#if DEBUG
    /// <summary>The AdMob app id (also written into the manifest by the attribute above): Google's sample app.</summary>
    public const string AppId = "ca-app-pub-3940256099942544~3347511713";

    /// <summary>The banner ad unit shown along the bottom of the garden: Google's test banner.</summary>
    public const string BannerUnitId = TestBannerUnitId;
#else
    /// <summary>The AdMob app id (also written into the manifest by the attribute above).</summary>
    public const string AppId = "ca-app-pub-4400173019354346~3719730997";

    /// <summary>The banner ad unit shown along the bottom of the garden.</summary>
    public const string BannerUnitId = LiveBannerUnitId;
#endif
}
