using Android.App;

// Release builds use the game's own app id and banner unit (from the AdMob console). Debug builds (the APKs CI makes for every
// branch) use the app id of another app in the same AdMob account, one that AdMob has already approved, together with Google's
// TEST banner unit (always fills with a clearly marked test ad, safe to tap): while "Garden Guardians" itself was still being
// reviewed by AdMob the SDK was refused its app settings (HTTP 403, "Not retrying to fetch app settings"). Once the game's own app
// is approved, the Debug app id below can go back to the game's own. Google's sample app id is refused too. Do not tap live ads
// while testing: clicking your own ads can get an AdMob account suspended.
[assembly: MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = GardenGuardians.AdConfig.AppId)]

namespace GardenGuardians;

internal static class AdConfig
{
    /// <summary>Google's test banner: always fills, from any app.</summary>
    public const string TestBannerUnitId = "ca-app-pub-3940256099942544/6300978111";

    /// <summary>The game's own banner unit.</summary>
    public const string LiveBannerUnitId = "ca-app-pub-4400173019354346/4753521775";

    /// <summary>The game's own AdMob app id.</summary>
    public const string GameAppId = "ca-app-pub-4400173019354346~3719730997";

    /// <summary>An already approved app of the same AdMob account, used by Debug builds (see above).</summary>
    public const string BorrowedAppId = "ca-app-pub-4400173019354346~8731792633";

#if DEBUG
    /// <summary>The AdMob app id written into the manifest by the attribute above.</summary>
    public const string AppId = BorrowedAppId;
#else
    /// <summary>The AdMob app id written into the manifest by the attribute above.</summary>
    public const string AppId = GameAppId;
#endif

#if DEBUG
    /// <summary>The banner ad unit shown along the bottom of the garden: Google's test banner.</summary>
    public const string BannerUnitId = TestBannerUnitId;
#else
    /// <summary>The banner ad unit shown along the bottom of the garden.</summary>
    public const string BannerUnitId = LiveBannerUnitId;
#endif
}
