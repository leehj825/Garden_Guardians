# Garden_Guardians_TODO.md

*Written 2026-10-06. A to-do list, not a plan of record: nothing here is built yet unless it says so. What was built, phase by phase, is in `Garden_Guardians_Roadmap.md`; how the game works is in `Garden_Guardians_Design.md`.*

Legend: `[ ]` to do, `[~]` waiting on something outside the code, `[x]` done.

---

## 1. Waiting on others (no code until these clear)

*   `[ ]` **The game is now called Bramblekin (package `com.bramblekin.game`).** Create a new Play Console app and a new AdMob app for that package; the AdMob IDs are already in `Platforms/Android/AdConfig.cs` (app `~9600481746`, banner `/2239023160`); Debug builds use Google's sample app id and test banner, so test ads need no store link; upload the feature graphic and text from `store/` (see `store/listing.md`). The old `com.gardenguardians.game` listing is unpublished; AdMob refused every ad request from it (HTTP 403, even for Google's test ads), the reason it was dropped.
*   `[~]` **Test the app installed from Google Play** (Internal testing track), with the tablet added under AdMob > Settings > Test devices. A sideloaded debug APK may never get ads for a store-linked app (signing certificate); the Play-installed one should.
*   `[ ]` **When AdMob approves the app:** check the Release build's banner on a real phone; remove the Debug-only ad diagnostics from the stats bar (the `Banner ad`, `Ad probes`, `Ad setup`, `Ad net` and `Ad SDK log` lines; the SDK log also shows Android's own codec warnings, which are harmless).
*   `[ ]` **Update the Play store text** to mention Explore (one line: "Take the wheel of a Bramblekin and explore the garden yourself."), in `store/` and in the console.

## 2. Before and after the first public release

*   `[ ]` **Try the Release build on a real phone first, in both orientations.** Rotate while playing, use Explore, play 30 minutes or more, and check the banner once approved. Write down and fix what breaks. *(Top priority.)*
*   `[ ]` **Performance on a mid-range phone.** Frame rate, heat and battery as villages and kingdoms grow; load time. If it is slow, consider a "low detail" setting.
*   `[ ]` **First-run hint.** Two lines on first start, for example: "Tap a Bramblekin to follow it, or tap Explore to walk as one." *(Top priority; cheap.)*
*   `[ ]` **Policy paperwork:** privacy policy page (the same site as app-ads.txt can host it), the EU/UK consent form for ads (Google's User Messaging Platform), the Data safety form (the AdMob advertising id), the content rating, the child-directed setting if children may play.
*   `[ ]` **Saves:** test that the three slots survive the app being killed mid-save and a Play Store update; a broken save is the quickest way to a one-star review.
*   `[ ]` **Release build cleanliness:** confirm no Debug-only screen shows (stats bar, log, speed buttons, the Control button).
*   `[ ]` **Crash reports.** Google Play's basic crash view first; Firebase Crashlytics later if crashes show up (needs the Data safety form and privacy policy updated). Optional: upload native debug symbols for `libraylib.so` (a Play warning, not an error).
*   `[ ]` **Target API level** is 36 in the manifest while the project compiles against 35 (.NET 9). Move the Android target to .NET 10 (`net10.0-android36.0`) when convenient.

## 3. Explore mode (Release): more fun

*   `[ ]` **Drop off food at the clan's store.** Walk into one of the clan's homes and the pack empties into the store, which feeds the clan; show "your clan now has N food". *(The small goal that makes walking worth it. Do first.)*
*   `[ ]` **Small quests from the garden:** a hungry child, a sick elder, a lost tool ("bring 3 berries to Mira").
*   `[x]` **Things to find:** the ant hill, the beehive in the oak, the shrines; a "discovered" list on the History screen.
*   `[x]` **A gentle risk:** keep the explorer unhurtable, but let a spider chase it so running (pushing the stick to its edge) matters.
*   `[ ]` **Photo mode:** hide the buttons and save a picture.
*   `[x]` **Talk to a Bramblekin:** tap one nearby for a line about what it is doing and thinking, from the traits the game already tracks.

## 4. The simulation: more fun to watch

*   `[x]` **"Story of the day" card:** the biggest event of the day as a sentence, with a button that flies the camera there (the Auto camera and the History screen already hold the material).
*   `[x]` **Favourites:** mark a Bramblekin, and say when it has a child, falls ill or becomes a leader.
*   `[x]` **"While you were away"** summary on return: a feast, a new village, a spider attack.
*   `[x]` **Gentle nudges:** one free action every so often (a day of rain, food sent to a village), so a watcher feels they are helping without a menu of buttons.
*   `[x]` **Start scenarios** for a new garden: drought year, twins, a lone survivor.
*   `[x]` **Pace options:** a slow "sleep mode", or an optional 2x for long waits (the Release build has no fast-forward).

## 5. Growing the game

*   `[ ]` **Store listing:** a 30-second trailer or GIF of a village with the camera flying; a first screenshot with a short caption; replace the feature graphic (a soft first draft, see `store/`) with real art.
*   `[ ]` **Ratings:** ask for a rating after a good moment, never on first launch.
*   `[ ]` **Share:** a "share this garden's story" button that exports a screenshot with the headline.
*   `[~]` **Money:** the rewarded video is in (the Guide's "Ad: +3" button, AdMob unit `…/3811439768`); a one-time "Remove ads" and cosmetic packs are not (see "Monetization" in the Design doc). *Original note:*  (see "Monetization" in the Design doc): rewarded video (a day of good rain, choose a newborn's traits), a one-time "Remove ads", cosmetic packs.

---

## Done in 0.2.0 (for context)

*   `[x]` Skills: Fighting (+ up to 0.15 Strength, masters grow up to 12% bigger), Gathering, Crafting; masters wear a gold mark; skill-gain pop-ups and skill bars in the Kin Inspector.
*   `[x]` The Guide: favour (a point a minute, 5 at most) spent on Rain, Clear, Bless, Calm, Hush the spider, Site, Feast, Aid and Peace; rewarded video for +3 favour; the garden's mood line above it.
*   `[x]` Story layer: favourites, "while you were away" catch-up (10 minutes of garden time at most), Story of the Day, friendship and rivalry headlines, memorial stones and a Hall of ancestors, 15 achievements, season recaps, a winter food check, speech bubbles.
*   `[x]` Explore: the Wolf Spider chases and knocks down (never hurts) a moving explorer, errands for clanmates, things to find, a Talk button.
*   `[x]` The garden: Planting Day, puddles, falling leaves, trade caravans, new-garden scenarios (Drought year, Twins, Lone survivor).
*   `[x]` Settings: Low detail, Sleep mode, Play as Release (Debug builds), the rewarded-ad test page (Debug builds).
*   `[x]` Saving: saves on Android pause, flushed to disk, a rolling `.bak`, a save from another version set aside as `.old`.

## Still to check by hand (nothing here could be tested without a phone)

*   `[ ]` **The Release build, both orientations, 30+ minutes:** the Guide bar against the bottom buttons, speech bubbles, caravans, the catch-up bar. *(Top priority; use Settings > Play as Release in a Debug build.)*
*   `[ ]` **Performance on a mid-range phone** with the new drawing (puddles, leaves, bubbles, caravans, memorials); Low detail switches most of it off.
*   `[ ]` **Saves:** kill the app mid-play and during a save, then update it from Play: the garden should come back (or its `.bak`).
*   `[ ]` **The rewarded video** in a Release build with the live unit, once AdMob serves it.

## Done earlier (for context)

*   `[x]` Upright and sideways layout; Android follows rotation (native patch for raylib).
*   `[x]` AdMob banner (SDK 25.5, .NET 9); app-ads.txt published and verified; AD_ID permission declared.
*   `[x]` Release AAB for Google Play (signed, version code from the run number); target API 36.
*   `[x]` Explore mode in the Release build; run by pushing the stick to its edge (Debug too).
*   `[x]` Store assets: icon, feature graphic (draft), screenshots (`store/`).
