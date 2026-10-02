using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A development aid: with GARDEN_SCREENSHOT=file.png the game saves a picture after GARDEN_SHOT_FRAMES frames
/// (default 30) and quits; GARDEN_CAMERA="x,y,z,tx,ty,tz" places the camera (position, then what it looks at).
/// Lets a build be looked at without a person at the screen (for instance on a virtual display).
/// </summary>
public static class DebugShot
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("GARDEN_SCREENSHOT");
    private static readonly int Frames = int.TryParse(Environment.GetEnvironmentVariable("GARDEN_SHOT_FRAMES"), out int frames) ? frames : 30;
    private static int _frame;

    /// <summary>Puts the camera where GARDEN_CAMERA says, if it does.</summary>
    public static void Place(ref Camera3D camera, World world)
    {
        string? text = Environment.GetEnvironmentVariable("GARDEN_CAMERA");
        if (text is null)
            return;
        if (text == "hill" && world.Anthill is { } hill) // an aid: the camera looks at the ant hill from the garden side
        {
            camera.Position = hill.Position + hill.Facing * 11f + new Vector3(0f, 3.2f, 0f);
            camera.Target = hill.Position + hill.Facing * 3f + new Vector3(0f, 0.5f, 0f);
            return;
        }
        if (text == "oak") // an aid: the camera looks at the oak's foot
        {
            camera.Position = World.OakCenter + new Vector3(30f, 4f, 0f);
            camera.Target = World.OakCenter + new Vector3(0f, 1.5f, 0f);
            return;
        }
        string[] parts = text.Split(',');
        if (parts.Length != 6)
            return;
        float[] v = parts.Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        camera.Position = new Vector3(v[0], v[1], v[2]);
        camera.Target = new Vector3(v[3], v[4], v[5]);
    }

    /// <summary>Call after each frame is drawn: true once the picture is saved and the game should stop.</summary>
    public static bool Finished()
    {
        if (Path is null || ++_frame < Frames)
            return false;
        Image picture = Raylib.LoadImageFromScreen();
        Raylib.ExportImage(picture, Path);
        Raylib.UnloadImage(picture);
        return true;
    }
}
