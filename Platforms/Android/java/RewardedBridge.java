package com.bramblekin.ads;

import android.app.Activity;
import android.util.Log;

import java.io.FileOutputStream;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

import com.google.android.gms.ads.AdError;
import com.google.android.gms.ads.AdRequest;
import com.google.android.gms.ads.FullScreenContentCallback;
import com.google.android.gms.ads.LoadAdError;
import com.google.android.gms.ads.OnUserEarnedRewardListener;
import com.google.android.gms.ads.rewarded.RewardItem;
import com.google.android.gms.ads.rewarded.RewardedAd;
import com.google.android.gms.ads.rewarded.RewardedAdLoadCallback;

/**
 * The rewarded video, in Java because the SDK's load callback is generic and cannot be subclassed cleanly from C#.
 * The game (AndroidRewarded.cs) calls load/show and polls isReady/consumeReward from its own thread.
 */
public final class RewardedBridge {
    private static volatile RewardedAd ad;
    private static volatile boolean loading;
    private static volatile boolean earned;
    private static volatile int result; // 0: no answer yet, 1: an ad loaded, 2: loading failed

    private static volatile String logPath;
    private static volatile String info = "";

    private RewardedBridge() {}

    /** Appends a line to the breadcrumb file the game shows on its Ad test page (and to logcat). */
    public static void log(String text) {
        Log.i("GardenGuardians", "rewarded: " + text);
        String path = logPath;
        if (path == null) {
            return;
        }
        try {
            FileOutputStream out = new FileOutputStream(path, true);
            try {
                String line = new SimpleDateFormat("HH:mm:ss", Locale.US).format(new Date()) + " java: " + text + "\n";
                out.write(line.getBytes("UTF-8"));
            } finally {
                out.close();
            }
        } catch (Throwable ignored) {
            // a log that cannot be written is not worth a crash
        }
    }

    /** Sets where the breadcrumbs go, and writes any crash of the app there too before letting it die as usual. */
    public static void installCrashLog(String path) {
        logPath = path;
        final Thread.UncaughtExceptionHandler previous = Thread.getDefaultUncaughtExceptionHandler();
        Thread.setDefaultUncaughtExceptionHandler(new Thread.UncaughtExceptionHandler() {
            @Override
            public void uncaughtException(Thread thread, Throwable error) {
                String trace = Log.getStackTraceString(error);
                String[] lines = trace.split("\n");
                log("CRASH on thread " + thread.getName() + ": " + lines[0]);
                for (int i = 1; i < lines.length && i < 9; i++) {
                    log("   " + lines[i].trim());
                }
                if (previous != null) {
                    previous.uncaughtException(thread, error);
                }
            }
        });
        log("crash log installed");
    }

    /** The last thing worth telling the player: a load error's code and message, or a caught exception. */
    public static String info() {
        return info;
    }

    public static void load(final Activity activity, final String unitId) {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    log("load: on the UI thread");
                    if (loading || ad != null) {
                        log("load: skipped (loading or already loaded)");
                        return;
                    }
                    loading = true;
                    log("load: calling RewardedAd.load");
                    RewardedAd.load(activity, unitId, new AdRequest.Builder().build(), new RewardedAdLoadCallback() {
                        @Override
                        public void onAdLoaded(RewardedAd loaded) {
                            log("callback: onAdLoaded");
                            ad = loaded;
                            loading = false;
                            result = 1;
                        }

                        @Override
                        public void onAdFailedToLoad(LoadAdError error) {
                            info = "load failed, code " + error.getCode() + ": " + error.getMessage();
                            log("callback: onAdFailedToLoad " + info);
                            ad = null;
                            loading = false;
                            result = 2;
                        }
                    });
                    log("load: RewardedAd.load returned");
                } catch (Throwable t) {
                    loading = false; // no rewarded video, but never a crash
                    info = "load threw " + t;
                    log("load: threw " + t);
                }
            }
        });
    }

    /** 0 until the first load has answered, then 1 (loaded) or 2 (failed). */
    public static int result() {
        return result;
    }

    public static boolean isReady() {
        return ad != null;
    }

    /** True once, after the viewer has earned the reward. */
    public static boolean consumeReward() {
        boolean was = earned;
        earned = false;
        return was;
    }

    public static void show(final Activity activity, final String unitId) {
        final RewardedAd shown = ad;
        if (shown == null) {
            log("show: no ad loaded");
            return;
        }
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                log("show: on the UI thread");
                shown.setFullScreenContentCallback(new FullScreenContentCallback() {
                    @Override
                    public void onAdDismissedFullScreenContent() {
                        log("callback: ad dismissed");
                        ad = null;
                        load(activity, unitId);
                    }

                    @Override
                    public void onAdFailedToShowFullScreenContent(AdError error) {
                        info = "show failed, code " + error.getCode() + ": " + error.getMessage();
                        log("callback: " + info);
                        ad = null;
                        load(activity, unitId);
                    }
                });
                shown.show(activity, new OnUserEarnedRewardListener() {
                    @Override
                    public void onUserEarnedReward(RewardItem item) {
                        log("callback: reward earned");
                        earned = true;
                    }
                });
                log("show: ad.show returned");
                } catch (Throwable t) {
                    ad = null;
                    info = "show threw " + t;
                    log("show: threw " + t);
                }
            }
        });
    }
}
