// =============================================================================
//  The rewarded video (Google AdMob): the Guide's "Watch" button. One ad is kept loaded; when the viewer
//  has watched it, the reward (favour) is handed back through AdBanner.ShowRewarded's action.
// =============================================================================

using Android.App;
using Android.Runtime;
using Android.Util;
using Google.Android.Gms.Ads;
using Google.Android.Gms.Ads.Rewarded;

namespace GardenGuardians;

internal static class AndroidRewarded
{
    private const string LogTag = "GardenGuardians";

    private static Activity? _activity;
    private static RewardedAd? _ad;
    private static bool _loading;
    private static Action? _onReward;

    /// <summary>Hooks the rewarded video up and starts loading one. Does nothing until the game has a rewarded unit id (see AdConfig).</summary>
    public static void Start(Activity activity)
    {
        if (string.IsNullOrEmpty(AdConfig.RewardedUnitId))
            return;
        _activity = activity;
        AdBanner.ShowRewarded = Show;
        Load();
    }

    private static void Load()
    {
        if (_loading || _ad is not null || _activity is not { } activity)
            return;
        _loading = true;
        activity.RunOnUiThread(() =>
        {
            try
            {
                RewardedAd.Load(activity, AdConfig.RewardedUnitId, new AdRequest.Builder().Build(), new LoadCallback());
            }
            catch (Exception ex)
            {
                _loading = false;
                Log.Error(LogTag, $"Rewarded ad load failed: {ex}");
            }
        });
    }

    private static void Show(Action onReward)
    {
        if (_ad is not { } ad || _activity is not { } activity)
            return;
        _onReward = onReward;
        activity.RunOnUiThread(() =>
        {
            ad.FullScreenContentCallback = new ContentCallback();
            ad.Show(activity, new Earned());
        });
    }

    /// <summary>The ad has been shown (or could not be): it is spent, and the next one starts loading.</summary>
    private static void Spent()
    {
        _ad = null;
        AdBanner.RewardedReady = false;
        Load();
    }

    private sealed class LoadCallback : RewardedAdLoadCallback
    {
        // The SDK's callback is generic (AdLoadCallback<RewardedAd>); the binding sees it as taking an Object, and Java then finds two
        // methods that clash after erasure. Registering the real signature makes the generated Java override the right one.
        [Register("onAdLoaded", "(Lcom/google/android/gms/ads/rewarded/RewardedAd;)V", "")]
        public override void OnAdLoaded(Java.Lang.Object ad)
        {
            _loading = false;
            _ad = ad as RewardedAd;
            AdBanner.RewardedReady = _ad is not null;
        }

        public override void OnAdFailedToLoad(LoadAdError error)
        {
            _loading = false;
            AdBanner.RewardedReady = false;
            Log.Warn(LogTag, $"Rewarded ad failed to load: {error.Message}");
        }
    }

    private sealed class ContentCallback : FullScreenContentCallback
    {
        public override void OnAdDismissedFullScreenContent() => Spent();

        public override void OnAdFailedToShowFullScreenContent(AdError error)
        {
            Log.Warn(LogTag, $"Rewarded ad failed to show: {error.Message}");
            Spent();
        }
    }

    private sealed class Earned : Java.Lang.Object, IOnUserEarnedRewardListener
    {
        public void OnUserEarnedReward(IRewardItem reward)
        {
            Action? give = _onReward;
            _onReward = null;
            give?.Invoke();
        }
    }
}
