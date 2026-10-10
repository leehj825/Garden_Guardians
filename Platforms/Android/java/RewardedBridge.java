package com.bramblekin.ads;

import android.app.Activity;

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

    private RewardedBridge() {}

    public static void load(final Activity activity, final String unitId) {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (loading || ad != null) {
                    return;
                }
                loading = true;
                RewardedAd.load(activity, unitId, new AdRequest.Builder().build(), new RewardedAdLoadCallback() {
                    @Override
                    public void onAdLoaded(RewardedAd loaded) {
                        ad = loaded;
                        loading = false;
                    }

                    @Override
                    public void onAdFailedToLoad(LoadAdError error) {
                        ad = null;
                        loading = false;
                    }
                });
            }
        });
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
            return;
        }
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                shown.setFullScreenContentCallback(new FullScreenContentCallback() {
                    @Override
                    public void onAdDismissedFullScreenContent() {
                        ad = null;
                        load(activity, unitId);
                    }

                    @Override
                    public void onAdFailedToShowFullScreenContent(AdError error) {
                        ad = null;
                        load(activity, unitId);
                    }
                });
                shown.show(activity, new OnUserEarnedRewardListener() {
                    @Override
                    public void onUserEarnedReward(RewardItem item) {
                        earned = true;
                    }
                });
            }
        });
    }
}
