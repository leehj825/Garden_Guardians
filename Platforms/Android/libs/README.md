# Android native libraries

Raylib-cs doesn't include an Android build of raylib. To run the renderer on
Android, build `libraylib.so` with the Android NDK and place one copy per ABI:

```
Platforms/Android/libs/arm64-v8a/libraylib.so
Platforms/Android/libs/x86_64/libraylib.so   (emulator)
```

`GardenGuardians.csproj` picks up any `libraylib.so` under this folder and
infers the ABI from the parent directory name.
