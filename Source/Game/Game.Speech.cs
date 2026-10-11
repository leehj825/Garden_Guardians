using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public static partial class Game
{
    /// <summary>Real seconds between one nearby Bramblekin piping up on its own.</summary>
    private const float ChatterGap = 4.5f;

    /// <summary>Only Bramblekin this close (m) to what the camera looks at chat on their own.</summary>
    private const float ChatterReach = 22f;

    private static float _chatterTimer;
    private static readonly Dictionary<int, string> _lastSpoken = new();

    /// <summary>
    /// Now and then a Bramblekin near the camera says what is on its mind (see <see cref="Bramblekin.Utterance"/>), in a bubble over its head — the
    /// tapped one speaks up first. Nothing is said from afar, upright or not at all when the camera is pulled right back.
    /// </summary>
    private static void UpdateChatter(World world, Camera3D camera, float realDeltaTime)
    {
        _chatterTimer -= realDeltaTime;
        if (_chatterTimer > 0f || Detail.FarView || world.Speeches.Count >= 4)
            return;
        _chatterTimer = ChatterGap;

        Bramblekin? speaker = world.SelectedKin is { IsDead: false } picked && !world.IsSpeaking(picked) ? picked : null;
        if (speaker is null)
        {
            var near = new List<Bramblekin>();
            foreach (Bramblekin kin in world.Colony)
            {
                if (!kin.IsDead && !kin.IsYoung && !kin.IsPlayerControlled && !world.IsSpeaking(kin) &&
                    GroundMover.HorizontalDistanceSquared(kin.Position, camera.Target) <= ChatterReach * ChatterReach)
                    near.Add(kin);
            }
            if (near.Count == 0)
                return;
            speaker = near[Random.Shared.Next(near.Count)];
        }
        string line = speaker.Utterance(world);
        if (_lastSpoken.TryGetValue(speaker.ID, out string? before) && before == line)
            return; // (never the same line twice running)
        _lastSpoken[speaker.ID] = line;
        world.Say(speaker, line, 5f);
    }

    /// <summary>The speech bubbles: a rounded white box with a small tail, over the speaker's head, fading out at the end.</summary>
    private static void DrawSpeechBubbles(Camera3D camera, World world)
    {
        if (Detail.FarView)
            return;
        int font = Math.Max(14, ScaledFontSize(0.5f));
        int pad = Math.Max(6, font / 3), maxWidth = (int)(Math.Min(Raylib.GetScreenWidth() * 0.4f, 360 * UiScale));
        foreach (Speech speech in world.Speeches)
        {
            Bramblekin kin = speech.Kin;
            var head = kin.Position + new Vector3(0f, Bramblekin.BodyHeight * kin.BodyScale * kin.VeteranScale + 0.35f, 0f);
            if (!IsPointOnScreen(camera, head))
                continue;
            Vector2 at = Raylib.GetWorldToScreen(head, camera);
            float fade = Math.Clamp(speech.TimeLeft / 0.6f, 0f, 1f) * Math.Clamp((speech.Total - speech.TimeLeft) / 0.15f + 0.2f, 0f, 1f);
            byte alpha = (byte)(255 * fade);

            List<string> lines = Wrap(speech.Text, font, maxWidth).ToList();
            int width = lines.Max(l => Raylib.MeasureText(l, font)) + pad * 2, height = lines.Count * (font + font / 6) + pad * 2;
            var box = new Rectangle(at.X - width / 2f, at.Y - height - font * 0.8f, width, height);
            Raylib.DrawRectangleRounded(box, 0.35f, 8, new Color(255, 252, 240, (int)(alpha * 0.95f)));
            Raylib.DrawRectangleRoundedLines(box, 0.35f, 8, new Color(70, 60, 40, (int)alpha));
            // the tail, pointing down at the speaker
            var tipA = new Vector2(at.X - font * 0.35f, box.Y + height - 1f);
            var tipB = new Vector2(at.X + font * 0.35f, box.Y + height - 1f);
            var tail = new Vector2(at.X, at.Y - font * 0.1f);
            Raylib.DrawTriangle(tail, tipB, tipA, new Color(255, 252, 240, (int)(alpha * 0.95f)));
            Raylib.DrawLineV(tipA, tail, new Color(70, 60, 40, (int)alpha));
            Raylib.DrawLineV(tipB, tail, new Color(70, 60, 40, (int)alpha));
            for (int i = 0; i < lines.Count; i++)
                Raylib.DrawText(lines[i], (int)box.X + pad, (int)box.Y + pad + i * (font + font / 6), font, new Color(40, 35, 25, (int)alpha));
        }
    }
}
