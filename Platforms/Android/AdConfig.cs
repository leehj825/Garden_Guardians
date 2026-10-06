using Android.App;

// The app id is always the game's own (from the AdMob console): Google's server refuses the sample app id from other apps
// (the SDK logs "Not retrying to fetch app settings"). Debug builds (the APKs CI makes for every branch) ask for Google's TEST
// banner unit, which always fills with a clearly marked test ad and is safe to tap; Release builds ask for the game's own
// banner unit. Do not tap live ads while testing: clicking your own ads can get an AdMob account suspended.
[assembly: MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = GardenGuardians.AdConfig.AppId)]

namespace GardenGuardians;

internal static class AdConfig
{
    /// <summary>Google's test banner: always fills, from any app.</summary>
    public const string TestBannerUnitId = "ca-app-pub-3940256099942544/6300978111";

    /// <summary>The game's own banner unit.</summary>
    public const string LiveBannerUnitId = "ca-app-pub-4400173019354346/4753521775";

    /// <summary>The AdMob app id (also written into the manifest by the attribute above).</summary>
    public const string AppId = "ca-app-pub-4400173019354346~3719730997";

#if DEBUG
    /// <summary>The banner ad unit shown along the bottom of the garden: Google's test banner.</summary>
    public const string BannerUnitId = TestBannerUnitId;
#else
    /// <summary>The banner ad unit shown along the bottom of the garden.</summary>
    public const string BannerUnitId = LiveBannerUnitId;
#endif
}
