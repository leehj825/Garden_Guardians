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
    private static IntPtr _class, _load, _show, _isReady, _consume, _result;
    private static bool _showing;
    private static Action? _onReward;

    /// <summary>
    /// Offers the rewarded video to the game without touching it: nothing is loaded and no Java is called until the player first opens the
    /// Guide (see <see cref="Begin"/>), so a problem with the ad can never stop the game from starting.
    /// </summary>
    public static void Start(Activity activity)
    {
        if (!AdConfig.RewardedEnabled || string.IsNullOrEmpty(AdConfig.RewardedUnitId))
            return;
        _activity = activity;
        AdBanner.BeginRewarded = Begin;
    }

    private static bool _begun;

    // The rewarded video has crashed the app before, for a reason not yet known. So each attempt is marked in the settings file before it starts and
    // cleared once the ad has answered; if the app finds the mark still there next time, that attempt killed it. After two such deaths the video
    // stays off (delete "rewardedfails" from settings.txt to try again).
    private const string TryingKey = "rewardedtrying", FailsKey = "rewardedfails";

    private static bool GuardAllows()
    {
        float fails = Preferences.GetNumber(FailsKey, 0f);
        if (Preferences.GetNumber(TryingKey, 0f) > 0f)
        {
            fails++;
            Preferences.SetNumber(FailsKey, fails);
            Preferences.SetNumber(TryingKey, 0f);
        }
        if (fails >= 2f)
            return false;
        Preferences.SetNumber(TryingKey, 1f);
        return true;
    }

    private static void GuardCleared()
    {
        if (Preferences.GetNumber(TryingKey, 0f) > 0f)
            Preferences.SetNumber(TryingKey, 0f);
        if (Preferences.GetNumber(FailsKey, 0f) > 0f)
            Preferences.SetNumber(FailsKey, 0f);
    }
    private static float _sincePoll;

    /// <summary>The first time the Guide is opened: sets the Java side up and starts loading a video (on the UI thread).</summary>
    private static void Begin()
    {
        if (_begun || _activity is not { } activity)
            return;
        _begun = true;
        if (!GuardAllows())
            return;
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
                _result = JNIEnv.GetStaticMethodID(_class, "result", "()I");
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
        Preferences.SetNumber(TryingKey, 1f);
        _showing = true;
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
                if (JNIEnv.CallStaticIntMethod(_class, _result) != 0 && (!_showing || !AdBanner.RewardedReady))
                {
                    GuardCleared(); // the ad has answered, and a video that was shown has been closed: it did not kill the app
                    _showing = false;
                }
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
