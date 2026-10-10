// =============================================================================
//  The rewarded video (Google AdMob). The ad itself is handled by Platforms/Android/java/RewardedBridge.java (the SDK's load callback is
//  generic, which a C# subclass cannot express); this file calls it through Java reflection (JNIEnv.FindClass closed the app on a device). It is started from the Settings page's Ad test (Game.AdTest),
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
    private static Java.Lang.Reflect.Method? _load, _show, _isReady, _consume, _result, _info;
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
                // (through the activity's own class loader: a thread coming in from native code only sees the system's classes)
                Java.Lang.Class cls = Java.Lang.Class.ForName("com.bramblekin.ads.RewardedBridge", true, activity.ClassLoader)
                    ?? throw new InvalidOperationException("class not found");
                Say("finding the Java methods");
                Java.Lang.Class activityType = Java.Lang.Class.FromType(typeof(Activity));
                Java.Lang.Class stringType = Java.Lang.Class.FromType(typeof(Java.Lang.String));
                _load = cls.GetMethod("load", activityType, stringType);
                _show = cls.GetMethod("show", activityType, stringType);
                _isReady = cls.GetMethod("isReady");
                _consume = cls.GetMethod("consumeReward");
                _result = cls.GetMethod("result");
                _info = cls.GetMethod("info");
                Say("installing the crash log");
                cls.GetMethod("installCrashLog", stringType)?.Invoke(null, new Java.Lang.String(RewardedDiag.Path));
                Say("asking for an ad");
                Call(_load);
                AdBanner.ShowRewarded = Show;
                AdBanner.PollRewarded = Poll;
                Say("load requested, waiting for an answer");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Could not set up the rewarded ad: {ex}");
                _load = null;
                Say($"FAILED to start: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>Calls the bridge's load or show with the activity and the unit id.</summary>
    private static void Call(Java.Lang.Reflect.Method? method)
    {
        if (method is null || _activity is not { } activity)
            return;
        method.Invoke(null, activity, new Java.Lang.String(AdConfig.RewardedUnitId));
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
        if (_sincePoll < 0.5f || _isReady is null || _activity is not { } activity)
            return;
        _sincePoll = 0f;
        activity.RunOnUiThread(() =>
        {
            try
            {
                AdBanner.RewardedReady = _isReady?.Invoke(null)?.JavaCast<Java.Lang.Boolean>().BooleanValue() ?? false;
                int result = _result?.Invoke(null)?.JavaCast<Java.Lang.Integer>().IntValue() ?? 0;
                if (result != _lastResult)
                {
                    _lastResult = result;
                    Say(result == 1 ? "an ad is loaded and ready" : "the ad failed to load");
                }
                string info = _info?.Invoke(null)?.ToString() ?? "";
                if (info != _lastInfo)
                {
                    _lastInfo = info;
                    Say(info);
                }
                if (_consume?.Invoke(null)?.JavaCast<Java.Lang.Boolean>().BooleanValue() ?? false)
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
