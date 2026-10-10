// =============================================================================
//  The rewarded video (Google AdMob). The ad itself is handled by Platforms/Android/java/RewardedBridge.java (the SDK's load callback is
//  generic, which a C# subclass cannot express); this file calls it over JNI. It is started from the Settings page's Ad test (Game.AdTest),
//  and every step goes into a breadcrumb file (RewardedDiag) that page shows, because opening it once closed the app and there is no crash log.
// =============================================================================

using Android.App;
using Android.Runtime;
using Android.Util;

namespace GardenGuardians;

internal static class AndroidRewarded
{
    private const string LogTag = "GardenGuardians";

    private static Activity? _activity;
    private static IntPtr _class, _load, _show, _isReady, _consume, _result, _info;
    private static Action? _onReward;
    private static bool _begun;
    private static float _sincePoll;
    private static int _lastResult;
    private static string _lastInfo = "";

    /// <summary>Makes the rewarded video available to the game; nothing is loaded and no Java is called until <see cref="Begin"/>.</summary>
    public static void Start(Activity activity)
    {
        if (string.IsNullOrEmpty(AdConfig.RewardedUnitId))
            return;
        _activity = activity;
        AdBanner.BeginRewarded = Begin;
        AdBanner.RewardedStatus = "not started";
    }

    private static void Say(string text)
    {
        AdBanner.RewardedStatus = text;
        RewardedDiag.Step(text);
    }

    /// <summary>Sets the Java side up and starts loading a video, on the UI thread, writing each step down first.</summary>
    private static void Begin()
    {
        if (_begun || _activity is not { } activity)
            return;
        _begun = true;
        Say("starting (unit " + AdConfig.RewardedUnitId + ")");
        activity.RunOnUiThread(() =>
        {
            try
            {
                Say("finding the Java class");
                IntPtr local = JNIEnv.FindClass("com/bramblekin/ads/RewardedBridge");
                _class = JNIEnv.NewGlobalRef(local);
                JNIEnv.DeleteLocalRef(local);
                Say("finding the Java methods");
                const string activityAndUnit = "(Landroid/app/Activity;Ljava/lang/String;)V";
                _load = JNIEnv.GetStaticMethodID(_class, "load", activityAndUnit);
                _show = JNIEnv.GetStaticMethodID(_class, "show", activityAndUnit);
                _isReady = JNIEnv.GetStaticMethodID(_class, "isReady", "()Z");
                _consume = JNIEnv.GetStaticMethodID(_class, "consumeReward", "()Z");
                _result = JNIEnv.GetStaticMethodID(_class, "result", "()I");
                _info = JNIEnv.GetStaticMethodID(_class, "info", "()Ljava/lang/String;");
                Say("installing the crash log");
                IntPtr logPath = JNIEnv.NewString(RewardedDiag.Path);
                IntPtr install = JNIEnv.GetStaticMethodID(_class, "installCrashLog", "(Ljava/lang/String;)V");
                JNIEnv.CallStaticVoidMethod(_class, install, new JValue(logPath));
                JNIEnv.DeleteLocalRef(logPath);
                Say("asking for an ad");
                Call(_load);
                AdBanner.ShowRewarded = Show;
                AdBanner.PollRewarded = Poll;
                Say("load requested, waiting for an answer");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Could not set up the rewarded ad: {ex}");
                _class = IntPtr.Zero;
                Say($"FAILED to start: {ex.GetType().Name}: {ex.Message}");
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
        Say("showing the ad");
        activity.RunOnUiThread(() =>
        {
            try
            {
                Call(_show);
            }
            catch (Exception ex)
            {
                Say($"FAILED to show: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>Called every frame from the game thread: about twice a second, asks the UI thread whether a video is loaded and whether the reward was just earned.</summary>
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
                int result = JNIEnv.CallStaticIntMethod(_class, _result);
                if (result != _lastResult)
                {
                    _lastResult = result;
                    Say(result == 1 ? "an ad is loaded and ready" : "the ad failed to load");
                }
                IntPtr text = JNIEnv.CallStaticObjectMethod(_class, _info);
                string info = JNIEnv.GetString(text, JniHandleOwnership.TransferLocalRef) ?? "";
                if (info != _lastInfo)
                {
                    _lastInfo = info;
                    Say(info);
                }
                if (JNIEnv.CallStaticBooleanMethod(_class, _consume))
                {
                    Action? give = _onReward;
                    _onReward = null;
                    Say("the reward was earned");
                    give?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Say($"poll failed: {ex.GetType().Name}: {ex.Message}");
                AdBanner.RewardedReady = false;
                AdBanner.PollRewarded = null;
            }
        });
    }
}
