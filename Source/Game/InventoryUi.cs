using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>Drawing a pack as a 3x3 grid of item boxes, each with its stack size; used by the Kin Inspector and by the player's Bag.</summary>
public static class InventoryUi
{
    public const int Columns = 3;

    public static float GridSize(float box, float gap) => Columns * box + (Columns - 1) * gap;

    public static Rectangle SlotRect(float x, float y, float box, float gap, int slot) =>
        new(x + slot % Columns * (box + gap), y + slot / Columns * (box + gap), box, box);

    /// <summary>The slot under <paramref name="point"/>, or -1.</summary>
    public static int SlotAt(Vector2 point, float x, float y, float box, float gap)
    {
        for (int i = 0; i < Inventory.Slots; i++)
            if (Raylib.CheckCollisionPointRec(point, SlotRect(x, y, box, gap, i)))
                return i;
        return -1;
    }

    /// <summary>Draws the grid with its top-left at (<paramref name="x"/>, <paramref name="y"/>) and returns the slot under the pointer (-1 if none).</summary>
    public static int DrawGrid(Inventory pack, float x, float y, float box, float gap, Color ink)
    {
        int hovered = SlotAt(Raylib.GetMousePosition(), x, y, box, gap);
        int countFont = Math.Max(10, (int)(box * 0.34f));
        for (int i = 0; i < Inventory.Slots; i++)
        {
            Rectangle slot = SlotRect(x, y, box, gap, i);
            Raylib.DrawRectangleRec(slot, i == hovered ? new Color(255, 255, 255, 120) : new Color(0, 0, 0, 55));
            Raylib.DrawRectangleLinesEx(slot, 1f, ink with { A = 170 });
            if (pack.KindAt(i) is not { } kind)
                continue;
            DrawIcon(kind, slot);
            string count = pack.CountAt(i).ToString();
            Raylib.DrawText(count, (int)(slot.X + slot.Width - Raylib.MeasureText(count, countFont) - 2), (int)(slot.Y + slot.Height - countFont - 1), countFont, Color.White);
        }
        return hovered;
    }

    /// <summary>The name of what's in <paramref name="slot"/> on a small tag just above its box (nothing for an empty slot).</summary>
    public static void DrawTooltip(Inventory pack, float x, float y, float box, float gap, int slot, int fontSize)
    {
        if (slot < 0 || pack.KindAt(slot) is not { } kind)
            return;
        string text = ItemInfo.Name(kind);
        Rectangle rect = SlotRect(x, y, box, gap, slot);
        int width = Raylib.MeasureText(text, fontSize);
        int tagX = (int)Math.Clamp(rect.X + rect.Width / 2f - width / 2f, 4f, Raylib.GetScreenWidth() - width - 12f);
        int tagY = (int)(rect.Y - fontSize - 8);
        if (tagY < 2)
            tagY = (int)(rect.Y + rect.Height + 4);
        Raylib.DrawRectangle(tagX - 4, tagY - 2, width + 8, fontSize + 4, new Color(30, 25, 20, 235));
        Raylib.DrawText(text, tagX, tagY, fontSize, Color.White);
    }

    private static void DrawIcon(ItemKind kind, Rectangle r)
    {
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, u = r.Width / 10f;
        int icx = (int)cx, icy = (int)cy;
        switch (kind)
        {
            case ItemKind.Berry:
                Raylib.DrawCircle(icx, icy + (int)u, u * 3f, new Color(200, 40, 55, 255));
                Raylib.DrawRectangle((int)(cx - u * 0.4f), (int)(cy - u * 3.2f), Math.Max(2, (int)(u * 0.8f)), (int)(u * 2f), new Color(70, 130, 50, 255));
                break;
            case ItemKind.Meat:
                Raylib.DrawEllipse(icx, icy, u * 3.4f, u * 2.4f, new Color(205, 105, 60, 255));
                Raylib.DrawCircle(icx + (int)(u * 2.4f), icy, u * 1.2f, new Color(240, 225, 200, 255));
                break;
            case ItemKind.Acorn:
                Raylib.DrawEllipse(icx, icy + (int)u, u * 2.2f, u * 2.8f, new Color(170, 115, 55, 255));
                Raylib.DrawEllipse(icx, icy - (int)(u * 1.2f), u * 2.5f, u * 1.4f, new Color(110, 75, 40, 255));
                break;
            case ItemKind.Seed:
                for (int i = 0; i < 5; i++)
                    Raylib.DrawEllipse((int)(cx + (i - 2) * u * 1.3f), (int)(cy + (i % 2 == 0 ? -u : u)), u * 0.9f, u * 1.6f, new Color(225, 190, 70, 255));
                break;
            case ItemKind.Mushroom:
                Raylib.DrawRectangle((int)(cx - u * 0.9f), icy, (int)(u * 1.8f), (int)(u * 3f), new Color(235, 225, 205, 255));
                Raylib.DrawEllipse(icx, icy, u * 3.2f, u * 2f, new Color(170, 95, 60, 255));
                break;
            case ItemKind.Cress:
                Raylib.DrawCircle(icx, icy, u * 2.2f, new Color(90, 185, 70, 255));
                Raylib.DrawCircle(icx + (int)(u * 1.8f), icy - (int)u, u * 1.5f, new Color(120, 205, 85, 255));
                Raylib.DrawCircle(icx - (int)(u * 1.8f), icy + (int)u, u * 1.5f, new Color(120, 205, 85, 255));
                break;
            case ItemKind.Fish:
                Raylib.DrawEllipse(icx - (int)u, icy, u * 3.2f, u * 1.8f, new Color(185, 200, 215, 255));
                Raylib.DrawTriangle(new Vector2(cx + u * 1.6f, cy), new Vector2(cx + u * 4f, cy + u * 1.8f), new Vector2(cx + u * 4f, cy - u * 1.8f), new Color(150, 170, 190, 255));
                Raylib.DrawCircle(icx - (int)(u * 2.6f), icy - (int)(u * 0.4f), Math.Max(1f, u * 0.35f), Color.Black);
                break;
            case ItemKind.Honeydew:
                Raylib.DrawCircle(icx, icy + (int)(u * 0.6f), u * 2.6f, new Color(235, 170, 40, 255));
                Raylib.DrawTriangle(new Vector2(cx - u * 1.6f, cy - u * 0.4f), new Vector2(cx + u * 1.6f, cy - u * 0.4f), new Vector2(cx, cy - u * 3.6f), new Color(235, 170, 40, 255));
                break;
            case ItemKind.Water:
                // A wooden bottle: round body, narrow neck, cork.
                Raylib.DrawRectangleRounded(new Rectangle(cx - u * 2.2f, cy - u * 0.8f, u * 4.4f, u * 4.6f), 0.4f, 6, new Color(150, 100, 55, 255));
                Raylib.DrawRectangle((int)(cx - u * 0.9f), (int)(cy - u * 2.6f), (int)(u * 1.8f), (int)(u * 2f), new Color(150, 100, 55, 255));
                Raylib.DrawRectangle((int)(cx - u * 1.1f), (int)(cy - u * 3.6f), (int)(u * 2.2f), (int)(u * 1.1f), new Color(205, 175, 120, 255));
                Raylib.DrawRectangle((int)(cx - u * 2.2f), (int)(cy + u * 0.6f), (int)(u * 4.4f), (int)(u * 0.5f), new Color(90, 150, 220, 255));
                break;
        }
    }
}
