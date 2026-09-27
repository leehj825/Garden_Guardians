# Garden_Guardians
Garden Guardians

An emergent survival simulation set in a procedurally hilly backyard. Every Bramblekin is an individual agent with its own randomly rolled Personality (Aggression, Sociability, Intelligence) and a strict hierarchy of needs — hunger, then safety, then its duties, its home, and company. Loners build tents from fallen twigs and stock them with food; groups form from how they meet, share a home they upgrade into a house, and hunt big game together under a Leader who decides what the group does and who eats first. Followers who don't like how they're led walk out, split off, or challenge the Leader — at the cost of the group's protection. The year turns from a plentiful summer to a lean winter; named Bramblekin pair up as couples, raise young who inherit their traits and family name, grow old and die, while thriving groups grow into villages, send settlers out to found new ones, learn to farm berry bushes, and ally with — or go to war against — their neighbours. Allies send runners with food and hire each other's hands; wars end in tribute or conquest; droughts, harsh winters, storms and floods make good and bad years; sickness spreads and a rival ant colony raids the stores, while clans work out granaries, spears and palisades; clans remember where danger lurks and grow warlike, hunting or farming traditions that outlast their Leaders. You watch, tap any Bramblekin to follow it with the camera and see what makes it tick, and open the History screen to read the chronicle of the garden's clans. The garden saves itself and carries on where you left it.

See the [Game Design Document](Garden_Guardians_Design.md) for the full design, and the [Development Roadmap](Garden_Guardians_Roadmap.md) for the planned phases.

## Building

Requires the .NET 8 SDK.

- **Desktop prototype:** `dotnet run -f net8.0 -p:DesktopOnly=true`. The `DesktopOnly` flag skips the Android target, so you don't need the Android workload.
- **Headless simulation:** `dotnet run -f net8.0 -p:DesktopOnly=true -- --headless 600 --seed 1` runs 600 simulated seconds with no window and prints population reports, notable events, and a summary of the survival trend (deaths per kin-hour by social status and cause), leadership styles, rebellions, villages and lineage, and the chronicle — handy for tuning, or on a machine without a GPU. Add `--save garden.json` to save the world at the end, and `--load garden.json` to carry on a saved one.

Code lives under `Source/` (engine, world, kin, wildlife, game), one type per file; `Program.cs` is just the entry point.
- **Android APK:** install the Android workload (`dotnet workload install android`) and the Android NDK. Then run `Platforms/Android/build-raylib.sh` to compile raylib for Android, and `dotnet publish -f net8.0-android -c Debug -p:EmbedAssembliesIntoApk=true`.

GitHub Actions builds the Android APKs automatically:

- `debug-build.yml` runs on every push to a branch other than `main` and uploads a debug APK.
- `release-build.yml` runs on every push to `main` and uploads a signed APK. It needs the `KEYSTORE_BASE64`, `KEYSTORE_PASSWORD`, `KEY_ALIAS` and `KEY_PASSWORD` repository secrets.

On Android the same C# game loop runs inside a `NativeActivity`. See `Platforms/Android/MainActivity.cs` for how it starts up.
