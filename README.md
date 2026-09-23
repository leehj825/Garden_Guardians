# Garden_Guardians
Garden Guardians

A god simulation / real-time strategy game where you play a benevolent backyard spirit guiding the Bramblekin through physics-based "miracles."

See the [Game Design Document](Garden_Guardians_Design.md) for the full design, and the [Development Roadmap](Garden_Guardians_Roadmap.md) for the planned phases.

## Building

Requires the .NET 8 SDK.

- **Desktop prototype:** `dotnet run -f net8.0 -p:DesktopOnly=true`. The `DesktopOnly` flag skips the Android target, so you don't need the Android workload.
- **Android APK:** `dotnet workload install android`, then `dotnet publish -f net8.0-android -c Debug -p:EmbedAssembliesIntoApk=true`.

GitHub Actions builds the Android APKs automatically:

- `debug-build.yml` runs on every push to a branch other than `main` and uploads a debug APK.
- `release-build.yml` runs on every push to `main` and uploads a signed APK. It needs the `KEYSTORE_BASE64`, `KEYSTORE_PASSWORD`, `KEY_ALIAS` and `KEY_PASSWORD` repository secrets.

The Android app currently opens a placeholder screen, because Raylib-cs doesn't include an Android build of raylib. See `Platforms/Android/libs/README.md`.
