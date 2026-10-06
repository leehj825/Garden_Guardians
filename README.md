# Garden_Guardians
Garden Guardians

An emergent survival simulation set in a hilly backyard modelled in 3D, at the foot of a giant oak and beside a pond, where the Bramblekin live in villages of acorns. You watch, tap any Bramblekin to follow it and see what makes it tick, or take the wheel of one yourself.

**Individuals.** Every Bramblekin has a randomly rolled Personality (aggressive or gentle, extrovert or introvert, clever or simple, rebellious or obedient, brave or cautious, diligent or idle) and a strict hierarchy of needs: thirst and hunger, then safety, duties, home, and company. Every drink means a walk to the pond, until clans catch rain in cisterns and dig wells. They pair up, raise young who inherit traits and family name, grow old and die.

**Groups and society.** Loners build tents from twigs; groups form from encounters, share a home, and hunt big game under a Leader. Followers who dislike their leader walk out, split off, or challenge them. Clans grow into **villages** with a headman and a shared store that pays soldiers, healers, builders and scouts, and villages unite into **kingdoms** with a king, tribute and pledged soldiers. Neighbours ally or go to war; swarms of invader spiders test the defences.

**The garden.** Seasons, day and night, droughts, storms, floods and sickness shape good and bad years. Clans farm, fish, herd aphids, harvest honey, build granaries, palisades and stone walls, hold feasts, raise shrines, and trade. Thief ants raid from a great ant hill that a kingdom's soldiers may storm for its eggs. The History screen records the chronicle and timeline; the garden saves itself, and you can keep up to three.

## Documents

* [Game Design Document](Garden_Guardians_Design.md): how the game works today.
* [Society & Jobs Design](Garden_Guardians_Society_Design.md): rationale and balance numbers for villages, rations and kingdoms.
* [Development Roadmap](Garden_Guardians_Roadmap.md): what was built, phase by phase, and what is left.
* [To-do list](Garden_Guardians_TODO.md): what is waiting on others, and the ideas for the release, Explore and the simulation.
* [Archive](docs/archive/Garden_Guardians_Superseded_Design.md): the superseded macro-RTS and original designs.

## Building

Requires the .NET 8 SDK for the desktop build (the Android build uses .NET 9: see below).

- **Desktop prototype:** `dotnet run -f net8.0 -p:DesktopOnly=true`. The `DesktopOnly` flag skips the Android target, so you don't need the Android workload.
- **Headless simulation:** `dotnet run -f net8.0 -p:DesktopOnly=true -- --headless 600 --seed 1` runs 600 simulated seconds with no window and prints population reports, notable events, and a summary of the survival trend (deaths per kin-hour by social status and cause), leadership styles, rebellions, villages and lineage, and the chronicle — handy for tuning, or on a machine without a GPU. Add `--save garden.json` to save the world at the end, and `--load garden.json` to carry on a saved one.

Code lives under `Source/` (engine, world, kin, wildlife, game), one type per file; `Program.cs` is just the entry point.
- **Android APK:** install the Android workload (`dotnet workload install android`) and the Android NDK. Then run `Platforms/Android/build-raylib.sh` to compile raylib for Android, and `dotnet publish -f net9.0-android -c Debug -p:EmbedAssembliesIntoApk=true`.

GitHub Actions builds the Android APKs automatically:

- `debug-build.yml` runs on every push to a branch other than `main` and uploads a debug APK.
- `release-build.yml` runs on every push to `main` and uploads a signed APK (`garden-guardians-release-apk`, to sideload) and a signed Android App Bundle (`garden-guardians-release-aab`, the file Google Play takes). Each build's version code is the workflow's run number, so every upload to Google Play is higher than the last. It needs the `KEYSTORE_BASE64`, `KEYSTORE_PASSWORD`, `KEY_ALIAS` and `KEY_PASSWORD` repository secrets.

The game plays upright (portrait) or sideways (landscape) and lays itself out again when the screen changes; `GARDEN_SIZE=720x1280` opens the desktop window at another shape and `GARDEN_AD_TEST=1` draws a stand-in for the banner ad. On Android a Google AdMob banner (the real app id always; Google's test banner unit in Debug builds, the live one in Release: `Platforms/Android/AdConfig.cs`) lies along the bottom while a garden is shown.

On Android the same C# game loop runs inside a `NativeActivity`. See `Platforms/Android/MainActivity.cs` for how it starts up.
