namespace GardenGuardians;

/// <summary>
/// The strips along the screen's top and bottom edges that something else covers or the system draws over (a camera notch, the
/// navigation bar), in pixels. A platform fills them in; the UI keeps its buttons and panels clear of them (see Game.Layout.cs).
/// Zero where nothing intrudes, as on the desktop.
/// </summary>
internal static class ScreenInsets
{
    public static volatile int Top, Bottom;
}
