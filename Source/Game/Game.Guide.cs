using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The Guide: a bar along the bottom with the player's favour and, once opened, the gentle nudges it can buy (rain, clear sky, bless, calm, hush,
/// site) — see World.Guide. A nudge that needs a Bramblekin or clan uses the one tapped in the Kin Inspector; the Site nudge then asks for a tap
/// on the ground.
/// </summary>
public static partial class Game
{
    private static bool _guideOpen;

    /// <summary>The Site nudge has been chosen and the next tap on the ground names the place.</summary>
    private static bool _guideWaitingForSite;

    /// <summary>Where the bottom HUD begins (last frame's), so the Guide sits above it.</summary>
    private static int _guideBase;

    private static string? _guideMessage;
    private static float _guideMessageLeft;

    private static readonly GuideAction[] GuideActions = Enum.GetValues<GuideAction>();

    private static int GuideBarHeight => (int)(86 * UiScale);

    /// <summary>The Guide button and, when it is open, the row of nudges (and the rewarded-video button if one is ready), left to right.</summary>
    private static (UiButton Toggle, UiButton[] Actions, UiButton? Watch) GuideLayout()
    {
        int margin = (int)(14 * UiScale), gap = (int)(8 * UiScale), height = GuideBarHeight;
        int y = (_guideBase > 0 ? Math.Min(_guideBase, UiBottom) : UiBottom) - height - margin;
        int toggleWidth = (int)(300 * UiScale);
        var toggle = new UiButton(new Rectangle(margin, y, toggleWidth, height));
        bool watch = AdBanner.RewardedReady;
        int count = GuideActions.Length + (watch ? 1 : 0);
        int room = Raylib.GetScreenWidth() - margin * 2 - toggleWidth - gap;
        int width = (room - gap * (count - 1)) / count;
        var actions = new UiButton[GuideActions.Length];
        int x = margin + toggleWidth + gap;
        for (int i = 0; i < actions.Length; i++, x += width + gap)
            actions[i] = new UiButton(new Rectangle(x, y, width, height));
        UiButton? watchButton = watch ? new UiButton(new Rectangle(x, y, width, height)) : null;
        return (toggle, actions, watchButton);
    }

    /// <summary>Handles a press for the Guide. Returns true if it was the Guide's (so nothing else reacts to it).</summary>
    private static bool GuideTap(Vector2 point, World world, Camera3D camera)
    {
        if (_showChronicle)
            return false;
        var (toggle, actions, watch) = GuideLayout();
        if (toggle.Contains(point))
        {
            _guideOpen = !_guideOpen;
            if (_guideOpen)
                AdBanner.BeginRewarded?.Invoke();
            _guideWaitingForSite = false;
            return true;
        }
        if (_guideOpen)
        {
            for (int i = 0; i < actions.Length; i++)
            {
                if (!actions[i].Contains(point))
                    continue;
                GuideAction action = GuideActions[i];
                KinGroup? clan = world.SelectedKin is { IsDead: false } kin ? world.GroupOf(kin) : world.SelectedClan;
                if (action == GuideAction.Site)
                {
                    if (clan is null)
                        SayGuide("Tap a Bramblekin or home of a clan first");
                    else if (world.Favour < World.GuideCost(action))
                        SayGuide("Not enough favour yet");
                    else
                    {
                        _guideWaitingForSite = true;
                        SayGuide("Tap the ground where " + clan.CapitalTitle + " should settle");
                    }
                    return true;
                }
                SayGuide(world.Guide(action, world.SelectedKin, clan, null) ?? World.GuideHelp(action));
                return true;
            }
            if (watch is not null && watch.Contains(point))
            {
                AdBanner.ShowRewarded?.Invoke(() => world.GrantFavour(3f));
                return true;
            }
        }
        if (_guideWaitingForSite)
        {
            _guideWaitingForSite = false;
            KinGroup? clan = world.SelectedKin is { IsDead: false } kin ? world.GroupOf(kin) : world.SelectedClan;
            if (WorldTapInput.PickGround(camera, world.Terrain, point) is { } ground)
                SayGuide(world.Guide(GuideAction.Site, world.SelectedKin, clan, ground) ?? "A place is chosen");
            return true;
        }
        return false;
    }

    private static void SayGuide(string text)
    {
        _guideMessage = text;
        _guideMessageLeft = 4f;
    }

    private static void DrawGuide(World world)
    {
        if (_showChronicle)
            return;
        _guideMessageLeft = MathF.Max(0f, _guideMessageLeft - Raylib.GetFrameTime());
        AdBanner.PollRewarded?.Invoke();
        var (toggle, actions, watch) = GuideLayout();
        toggle.Draw($"Guide  {world.Favour:0.0}/{World.MaxFavour:0}", highlighted: _guideOpen);
        int fontSize = Math.Max(12, (int)(actions[0].Bounds.Height * 0.38f));
        if (_guideOpen)
        {
            for (int i = 0; i < actions.Length; i++)
            {
                GuideAction action = GuideActions[i];
                bool affordable = world.Favour >= World.GuideCost(action);
                DrawGuideButton(actions[i], $"{World.GuideLabel(action)} {World.GuideCost(action):0}", affordable, highlighted: action == GuideAction.Site && _guideWaitingForSite);
            }
            if (watch is not null)
                DrawGuideButton(watch, "Ad: +3", true, highlighted: false);
        }
        string? message = _guideMessageLeft > 0f ? _guideMessage : _guideWaitingForSite ? "Tap the ground to choose the place" : null;
        if (message is not null)
        {
            int size = ScaledFontSize(0.6f), pad = (int)(8 * UiScale);
            int width = Raylib.MeasureText(message, size);
            int x = Math.Max(0, (Raylib.GetScreenWidth() - width) / 2), y = (int)toggle.Bounds.Y - size - pad * 3;
            Raylib.DrawRectangle(x - pad, y - pad, width + pad * 2, size + pad * 2, PanelFill);
            Raylib.DrawText(message, x, y, size, PanelInk);
        }
    }

    /// <summary>A Guide button with its label shrunk to fit.</summary>
    private static void DrawGuideButton(UiButton button, string label, bool enabled, bool highlighted)
    {
        Color fill = highlighted ? new Color(230, 190, 60, 255) : enabled ? new Color(220, 232, 205, 255) : new Color(185, 175, 170, 255);
        Raylib.DrawRectangleRec(button.Bounds, fill);
        Raylib.DrawRectangleLinesEx(button.Bounds, 2f, Color.DarkGray);
        int size = FitFontSize(label, (int)(button.Bounds.Height * 0.4f), (int)button.Bounds.Width - 8);
        int x = (int)(button.Bounds.X + (button.Bounds.Width - Raylib.MeasureText(label, size)) / 2f), y = (int)(button.Bounds.Y + (button.Bounds.Height - size) / 2f);
        Raylib.DrawText(label, x, y, size, enabled ? Color.Black : new Color(90, 80, 75, 255));
    }
}
