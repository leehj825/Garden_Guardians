# Android native libraries

`Platforms/Android/build-raylib.sh` writes `libraylib.so` here, one copy per
ABI. The build output is gitignored:

```
Platforms/Android/libs/arm64-v8a/libraylib.so   (phones)
Platforms/Android/libs/x86_64/libraylib.so      (emulator)
```

Each library contains raylib 6.0 (the version Raylib-cs 8.x binds),
the NDK's `android_native_app_glue`, and `native/gg_android_main.c`. That last
file supplies the `main()` raylib calls on Android and forwards it to the C#
game loop. `native/gg_resize_window.inc.c` is appended to raylib's `rcore_android.c`
by the script and adds `gg_resize_window`, so the window follows a rotation. The Android build fails with a clear error if either file is
missing.
