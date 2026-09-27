using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>A minimal clickable rectangle with a centred text label.</summary>
public sealed class UiButton
{
    public Rectangle Bounds { get; }

    public UiButton(Rectangle bounds) => Bounds = bounds;

    public bool Contains(Vector2 point) => Raylib.CheckCollisionPointRec(point, Bounds);

    public void Draw(string label, bool highlighted, bool disabled = false)
    {
        bool hovered = Contains(Raylib.GetMousePosition());

        Color fill = highlighted ? new Color(230, 190, 60, 255)   // Gold when armed.
                   : disabled ? new Color(185, 175, 170, 255)     // Greyed out when unaffordable.
                   : hovered ? new Color(245, 245, 245, 255)      // Light on hover.
                   : new Color(220, 220, 220, 255);               // Default.

        Raylib.DrawRectangleRec(Bounds, fill);
        Raylib.DrawRectangleLinesEx(Bounds, 2f, Color.DarkGray);

        // UI Text Scaling: sized off the button's own height rather than a
        // fixed constant, so a bigger button (see the 3x-scaled Debug Time
        // Scale buttons) automatically gets bigger, still-centred text
        // instead of a tiny label lost in a large rectangle.
        int fontSize = (int)(Bounds.Height * 0.5f);
        int textWidth = Raylib.MeasureText(label, fontSize);
        int x = (int)(Bounds.X + (Bounds.Width - textWidth) / 2f);
        int y = (int)(Bounds.Y + (Bounds.Height - fontSize) / 2f);
        Raylib.DrawText(label, x, y, fontSize, disabled && !highlighted ? new Color(90, 80, 75, 255) : Color.Black);
    }
}
