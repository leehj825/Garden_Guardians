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

    /// <summary>Hooks the rewarded video up and starts loading one. Does nothing without a rewarded unit id (see AdConfig).</summary>
    public static void Start(Activity activity)
    {
        if (string.IsNullOrEmpty(AdConfig.RewardedUnitId))
            return;
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
            _activity = activity;
            Call(_load);
            AdBanner.ShowRewarded = Show;
            AdBanner.PollRewarded = Poll;
        }
        catch (Exception ex)
        {
            // No rewarded video is better than no game.
            Log.Error(LogTag, $"Could not set up the rewarded ad: {ex}");
            AdBanner.ShowRewarded = null;
            AdBanner.PollRewarded = null;
        }
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
        if (!AdBanner.RewardedReady)
            return;
        _onReward = onReward;
        Call(_show);
    }

    /// <summary>Once a frame, from the game thread: is a video loaded, and has the viewer just earned the reward?</summary>
    private static void Poll()
    {
        if (_class == IntPtr.Zero)
            return;
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
    }
}
