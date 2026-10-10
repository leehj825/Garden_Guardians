// =============================================================================
//  The rewarded video (Google AdMob): the Guide's "Ad: +3" button. The ad itself is handled by
//  Platforms/Android/java/RewardedBridge.java (the SDK's load callback is generic, which a C# subclass cannot express);
//  this file calls it over JNI and polls it from the game thread.
// =============================================================================

using Android.App;
using Android.Runtime;
using Android.Util;

namespace GardenGuardians;

internal static class AndroidRewarded
{
    private const string LogTag = "GardenGuardians";

    private static Activity? _activity;
    private static IntPtr _class, _load, _show, _isReady, _consume;
    private static Action? _onReward;

    /// <summary>
    /// Offers the rewarded video to the game without touching it: nothing is loaded and no Java is called until the player first opens the
    /// Guide (see <see cref="Begin"/>), so a problem with the ad can never stop the game from starting.
    /// </summary>
    public static void Start(Activity activity)
    {
        if (string.IsNullOrEmpty(AdConfig.RewardedUnitId))
            return;
        _activity = activity;
        AdBanner.BeginRewarded = Begin;
    }

    private static bool _begun;
    private static float _sincePoll;

    /// <summary>The first time the Guide is opened: sets the Java side up and starts loading a video (on the UI thread).</summary>
    private static void Begin()
    {
        if (_begun || _activity is not { } activity)
            return;
        _begun = true;
        activity.RunOnUiThread(() =>
        {
            try
            {
                IntPtr local = JNIEnv.FindClass("com/bramblekin/ads/RewardedBridge");
                _class = JNIEnv.NewGlobalRef(local);
                JNIEnv.DeleteLocalRef(local);
                const string activityAndUnit = "(Landroid/app/Activity;Ljava/lang/String;)V";
                _load = JNIEnv.GetStaticMethodID(_class, "load", activityAndUnit);
                _show = JNIEnv.GetStaticMethodID(_class, "show", activityAndUnit);
                _isReady = JNIEnv.GetStaticMethodID(_class, "isReady", "()Z");
                _consume = JNIEnv.GetStaticMethodID(_class, "consumeReward", "()Z");
                Call(_load);
                AdBanner.ShowRewarded = Show;
                AdBanner.PollRewarded = Poll;
            }
            catch (Exception ex)
            {
                // No rewarded video is better than no game.
                Log.Error(LogTag, $"Could not set up the rewarded ad: {ex}");
                _class = IntPtr.Zero;
            }
        });
    }

    /// <summary>Calls the bridge's load or show with the activity and the unit id.</summary>
    private static void Call(IntPtr method)
    {
        if (_activity is not { } activity)
            return;
        IntPtr unit = JNIEnv.NewString(AdConfig.RewardedUnitId);
        try
        {
            JNIEnv.CallStaticVoidMethod(_class, method, new JValue(activity.Handle), new JValue(unit));
        }
        finally
        {
            JNIEnv.DeleteLocalRef(unit);
        }
    }

    private static void Show(Action onReward)
    {
        if (!AdBanner.RewardedReady || _activity is not { } activity)
            return;
        _onReward = onReward;
        activity.RunOnUiThread(() =>
        {
            try
            {
                Call(_show);
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Could not show the rewarded ad: {ex}");
            }
        });
    }

    /// <summary>Called by the Guide every frame, from the game thread: about twice a second it asks the UI thread whether a video is loaded and whether the reward was just earned.</summary>
    private static void Poll()
    {
        _sincePoll += 1f / 60f;
        if (_sincePoll < 0.5f || _class == IntPtr.Zero || _activity is not { } activity)
            return;
        _sincePoll = 0f;
        activity.RunOnUiThread(() =>
        {
            try
            {
                AdBanner.RewardedReady = JNIEnv.CallStaticBooleanMethod(_class, _isReady);
                if (JNIEnv.CallStaticBooleanMethod(_class, _consume))
                {
                    Action? give = _onReward;
                    _onReward = null;
                    give?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Rewarded ad poll failed: {ex}");
                AdBanner.RewardedReady = false;
                AdBanner.PollRewarded = null;
            }
        });
    }
}
