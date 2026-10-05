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
            DrawItem(kind, slot);
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

    private const int IconPixels = 96;

    private static readonly Dictionary<ItemKind, RenderTexture2D> Icons = new();

    /// <summary>The item's model, shrunk to fit its box (each is rendered once into a small texture).</summary>
    private static void DrawItem(ItemKind kind, Rectangle box)
    {
        if (!Icons.TryGetValue(kind, out RenderTexture2D icon))
        {
            icon = RenderIcon(kind);
            Icons[kind] = icon;
        }
        var source = new Rectangle(0, 0, IconPixels, -IconPixels); // (a render texture is upside down)
        float pad = box.Width * 0.06f;
        Raylib.DrawTexturePro(icon.Texture, source, new Rectangle(box.X + pad, box.Y + pad, box.Width - pad * 2, box.Height - pad * 2), Vector2.Zero, 0f, Color.White);
    }

    /// <summary>How much room (m) each item's model takes up, so the camera can frame it.</summary>
    private static float Span(ItemKind kind) => kind switch
    {
        ItemKind.Berry or ItemKind.Acorn or ItemKind.Seed or ItemKind.Honeydew => 0.32f,
        ItemKind.Meat or ItemKind.Stone => 0.42f,
        ItemKind.Mushroom or ItemKind.Cress or ItemKind.Fish => 0.46f,
        ItemKind.Water => 0.5f,
        ItemKind.Twig => 0.6f,
        ItemKind.Branch => 1.5f,
        _ => 0.5f,
    };

    private static RenderTexture2D RenderIcon(ItemKind kind)
    {
        RenderTexture2D target = Raylib.LoadRenderTexture(IconPixels, IconPixels);
        float span = Span(kind);
        Vector3 centre = new(0f, span * 0.25f, 0f);
        var camera = new Camera3D
        {
            Target = centre,
            Position = centre + Vector3.Normalize(new Vector3(1f, 0.9f, 1f)) * span * 1.9f,
            Up = Vector3.UnitY,
            FovY = 30f,
            Projection = CameraProjection.Perspective,
        };

        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        Raylib.BeginMode3D(camera);
        switch (kind)
        {
            case ItemKind.Water:
                Bramblekin.DrawBottleModel(new Vector3(0f, 0f, 0f));
                break;
            case ItemKind.Twig:
                LooseModels.Draw(LooseModels.Kind.Twig, Vector3.Zero, 20f, 1f, Color.White);
                break;
            case ItemKind.Stone:
                LooseModels.Draw(LooseModels.Kind.Stone, Vector3.Zero, 20f, 1f, Color.White);
                break;
            case ItemKind.Branch:
                LooseModels.DrawBetween(LooseModels.Kind.Branch, new Vector3(-0.6f, 0.05f, 0.25f), new Vector3(0.6f, 0.05f, -0.25f), Color.White);
                break;
            default:
                FoodShard.DrawKind(ItemInfo.FoodOf(kind), Vector3.Zero, 30f);
                break;
        }
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
        return target;
    }
}
