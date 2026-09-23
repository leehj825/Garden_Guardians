// =============================================================================
//  Android entry point
// -----------------------------------------------------------------------------
//  Android apps don't start from Program.Main; the OS launches the Activity
//  marked MainLauncher instead. raylib's Android backend is built on
//  NativeActivity + android_native_app_glue, so this Activity *is* a
//  NativeActivity. Start-up works like this:
//
//    1. Android starts the .NET runtime, then creates MainActivity.
//    2. OnCreate registers GameMain with the native library (gg_set_main).
//    3. base.OnCreate (NativeActivity) loads libraylib.so — named by the
//       "android.app.lib_name" meta-data below — and calls the glue's
//       ANativeActivity_onCreate, which starts a dedicated game thread.
//    4. On that thread raylib's android_main() calls main() in
//       native/gg_android_main.c, which calls back into GameMain here.
//    5. GameMain runs the same Game.Run loop as the desktop build.
//
//  libraylib.so is built by Platforms/Android/build-raylib.sh.
// =============================================================================

using System.Runtime.InteropServices;
using Android.Content.PM;
using Android.Util;

namespace GardenGuardians;

[Activity(
    Name = "com.gardenguardians.game.MainActivity",   // Stable Java name (used by adb/tests).
    Label = "Garden Guardians",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    // Handle these ourselves so rotating/resizing doesn't destroy the
    // activity (and with it the GL context and game thread).
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
                           ConfigChanges.ScreenLayout | ConfigChanges.KeyboardHidden |
                           ConfigChanges.Keyboard)]
[MetaData("android.app.lib_name", Value = "raylib")]
public class MainActivity : NativeActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Must happen before base.OnCreate: that call starts the native game
        // thread, which immediately looks for the registered entry point.
        NativeBridge.RegisterGameMain();
        base.OnCreate(savedInstanceState);
    }
}

/// <summary>
/// P/Invoke glue between the native game thread and the managed game loop.
/// </summary>
internal static unsafe class NativeBridge
{
    private const string LogTag = "GardenGuardians";

    // Resolves to libraylib.so — the same library NativeActivity loads, so
    // both sides share one copy of raylib's global state.
    [DllImport("raylib", EntryPoint = "gg_set_main")]
    private static extern void SetMain(delegate* unmanaged<void> entryPoint);

    public static void RegisterGameMain()
    {
        // Load libraylib.so through Java first. This is the same loader
        // NativeActivity uses, so the P/Invoke below and NativeActivity see
        // one shared copy of the library — and if loading fails, Java reports
        // the real linker error, which .NET's DllNotFoundException hides.
        try
        {
            Java.Lang.JavaSystem.LoadLibrary("raylib");
        }
        catch (Java.Lang.Throwable ex)
        {
            Log.Error(LogTag, $"Failed to load libraylib.so: {ex}");
            throw;
        }

        SetMain(&GameMain);
    }

    /// <summary>
    /// Runs on the native-app-glue thread (not the Android UI thread).
    /// Exceptions must never cross back into native code, so everything is
    /// caught and logged here.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void GameMain()
    {
        try
        {
            Log.Info(LogTag, "Native game thread started; entering Game.Run.");
            Game.Run(GamePlatform.Android);
            Log.Info(LogTag, "Game.Run returned; activity will finish.");
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"Unhandled exception in game loop: {ex}");
        }
    }
}
