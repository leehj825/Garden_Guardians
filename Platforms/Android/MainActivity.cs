// =============================================================================
//  Android entry point
// -----------------------------------------------------------------------------
//  Android apps don't start from Program.Main; the OS launches the Activity
//  marked MainLauncher instead.
//
//  For now this is a placeholder screen. The Raylib-cs NuGet package has no
//  Android build of the native raylib library, so calling into Raylib here
//  would crash. Once libraylib.so is supplied (see Platforms/Android/libs/)
//  and wired to an OpenGL ES surface, this Activity will host the game loop
//  from Program.cs.
// =============================================================================

using Android.Content.PM;
using Android.Views;

namespace GardenGuardians;

[Activity(
    Label = "Garden Guardians",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var label = new TextView(this)
        {
            Text = "Garden Guardians\n\nAndroid build pipeline OK.\nRaylib renderer not wired up yet.",
            Gravity = GravityFlags.Center,
            TextSize = 20f,
        };
        SetContentView(label);
    }
}
