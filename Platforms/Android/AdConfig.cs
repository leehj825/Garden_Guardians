using Android.App;

// Release builds (the AAB for Google Play) use the game's own AdMob app id and banner unit. Debug builds (the APKs CI makes for every branch)
// use Google's sample app id and TEST banner unit, which work from any package whatever state its AdMob app is in (linked to the store or
// not, in review or not), and always fill with a clearly marked test ad that is safe to tap. Do not tap live ads while testing: clicking your
// own ads can get an AdMob account suspended.
[assembly: MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = GardenGuardians.AdConfig.AppId)]

namespace GardenGuardians;

internal static class AdConfig
{
    /// <summary>Google's test banner: always fills, from any app.</summary>
    public const string TestBannerUnitId = "ca-app-pub-3940256099942544/6300978111";

    /// <summary>The game's own banner unit.</summary>
    public const string LiveBannerUnitId = "ca-app-pub-4400173019354346/2239023160";

    /// <summary>Google's sample app id: pairs with the test banner unit.</summary>
    public const string SampleAppId = "ca-app-pub-3940256099942544~3347511713";

    /// <summary>The game's own AdMob app id.</summary>
    public const string LiveAppId = "ca-app-pub-4400173019354346~9600481746";

    /// <summary>Google's test rewarded video: always fills, from any app.</summary>
    public const string TestRewardedUnitId = "ca-app-pub-3940256099942544/5224354917";

    /// <summary>The game's own rewarded video unit. EMPTY until one is made in the AdMob console (Ad units > Rewarded) and pasted here: with none, a Release build shows no "Watch" button.</summary>
    public const string LiveRewardedUnitId = "";

#if DEBUG
    /// <summary>The AdMob app id written into the manifest by the attribute above, and the banner unit shown along the bottom of the garden: Google's test ones.</summary>
    public const string AppId = SampleAppId;
    public const string BannerUnitId = TestBannerUnitId;
    public const string RewardedUnitId = TestRewardedUnitId;
#else
    public const string AppId = LiveAppId;
    /// <summary>The banner ad unit shown along the bottom of the garden.</summary>
    public const string BannerUnitId = LiveBannerUnitId;
    public const string RewardedUnitId = LiveRewardedUnitId;
#endif
}
