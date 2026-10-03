using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Playing a Bramblekin: the player takes one kin's wheel (the "Control" button under the Kin Inspector) and walks it about from a third-person
/// camera behind it, like an action game. A stick on the left of the screen moves it (relative to where the camera looks); a drag anywhere else
/// turns the camera; the Attack button swings (see <see cref="Bramblekin.PlayerMayHitKin"/> for whether that may hit other clans' kin). Desktop:
/// W A S D (or the arrows) to move, Space to strike, the mouse to look. The kin still gets hungry and thirsty and can be hurt; stood still
/// by food, its store or the water it eats and drinks (see <c>Bramblekin.Play.cs</c>). Everything else in the garden goes on as usual.
/// </summary>
public sealed class PlayControl
{
    /// <summary>The camera sits this far (m) behind the kin's head.</summary>
    private const float CameraDistance = 4.8f;

    /// <summary>How high above the kin's feet (m) the camera looks.</summary>
    private const float LookHeight = 1.1f;

    /// <summary>The camera aims this far (m) below the kin's head, which lifts the kin up the screen, clear of the thumbs.</summary>
    private const float LookBelow = 1.0f;

    /// <summary>Radians the view turns per pixel dragged, sideways and up/down.</summary>
    private const float TurnPerPixel = 0.006f, PitchPerPixel = 0.004f;

    /// <summary>The camera may look this far (radians) down from the horizontal at the most, and up at the least.</summary>
    private const float MaxPitch = 1.2f, MinPitch = -0.1f;

    /// <summary>The stick's dead zone (share of its radius).</summary>
    private const float DeadZone = 0.15f;

    /// <summary>A development aid: GARDEN_PLAY_FORWARD=1 holds the stick forward.</summary>
    private static readonly bool DebugForward = Environment.GetEnvironmentVariable("GARDEN_PLAY_FORWARD") == "1";

    private static readonly bool DebugAttack = Environment.GetEnvironmentVariable("GARDEN_PLAY_ATTACK") == "1";

    private float _yaw;
    private float _pitch = 0.4f;
    private Vector3 _focus;

    // Touches by id: the one on the stick, the one turning the camera, the one on the attack button.
    private int _stickTouch = -1, _lookTouch = -1, _attackTouch = -1;
    private Vector2 _lookLast;
    private Vector2 _stick;
    private readonly HashSet<int> _spent = new();

    public Bramblekin? Kin { get; private set; }

    public bool IsActive => Kin is not null;

    /// <summary>Whether the player's strikes may hit other clans' kin (off to begin with).</summary>
    public bool HitKin { get; private set; }

    public void Begin(Bramblekin kin, Camera3D camera, World world)
    {
        Kin = kin;
        Vector2 heading = kin.Facing.LengthSquared() > 1e-6f ? Vector2.Normalize(kin.Facing) : Vector2.UnitX;
        _yaw = MathF.Atan2(heading.X, heading.Y);
        if (float.TryParse(Environment.GetEnvironmentVariable("GARDEN_PLAY_YAW"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float turnDegrees))
            _yaw += turnDegrees * MathF.PI / 180f; // A development aid: look at the kin from another side (180 for its front).
        _pitch = float.TryParse(Environment.GetEnvironmentVariable("GARDEN_PLAY_PITCH"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float testPitch) ? testPitch : 0.4f; // (GARDEN_PLAY_PITCH: a development aid)
        _focus = kin.Position + new Vector3(0f, LookHeight, 0f);
        _stickTouch = _lookTouch = _attackTouch = -1;
        _stick = Vector2.Zero;
        _spent.Clear();
        for (int i = 0; i < Raylib.GetTouchPointCount(); i++)
            _spent.Add(Raylib.GetTouchPointId(i)); // The press that chose "Control" is not a first touch of the stick or the view.
        kin.PlayerMayHitKin = HitKin;
        kin.SetPlayerControlled(true, world);
    }

    /// <summary>Hands the kin back to its own mind and puts the camera high over it, ready for the Follow camera to take up.</summary>
    public void End(ref Camera3D camera, World world)
    {
        if (Kin is { } kin)
        {
            kin.SetPlayerControlled(false, world);
            camera.Target = kin.Position;
            camera.Position = kin.Position + new Vector3(-MathF.Sin(_yaw) * 12f, 14f, -MathF.Cos(_yaw) * 12f);
        }
        Kin = null;
    }

    private static float Scale => Raylib.GetScreenWidth() / 1920f;

    private static Vector2 StickCenter => new(Raylib.GetScreenWidth() * 0.15f, Raylib.GetScreenHeight() * 0.74f);
    private static float StickRadius => 150f * Scale;
    private static Vector2 AttackCenter => new(Raylib.GetScreenWidth() - 190f * Scale, Raylib.GetScreenHeight() * 0.72f);
    private static float AttackRadius => 115f * Scale;
    private static UiButton ExitButton => new(new Rectangle(20 * Scale, 20 * Scale, 260 * Scale, 110 * Scale));
    private static UiButton KinToggle => new(new Rectangle(Raylib.GetScreenWidth() - 520 * Scale, Raylib.GetScreenHeight() * 0.72f - 55 * Scale, 230 * Scale, 110 * Scale));

    private static UiButton JobToggle => new(new Rectangle(Raylib.GetScreenWidth() - 520 * Scale, Raylib.GetScreenHeight() * 0.72f - 185 * Scale, 230 * Scale, 110 * Scale));

    private static string JobLabel(KinJob job) => job switch { KinJob.Hunter => "Job: Hunter", KinJob.Guard => "Job: Guard", KinJob.Spearman => "Job: Spearman", KinJob.Fisher => "Job: Fisher", _ => "Job: Normal" };

    /// <summary>The part of the screen where a touch takes the stick: the lower left.</summary>
    private static bool InStickZone(Vector2 point) => point.X < Raylib.GetScreenWidth() * 0.42f && point.Y > Raylib.GetScreenHeight() * 0.3f;

    /// <summary>Reads the touches and keys, steers the kin and places the camera. Call once a frame while <see cref="IsActive"/>.</summary>
    public void Update(ref Camera3D camera, World world, float deltaTime)
    {
        if (Kin is not { IsDead: false } kin)
        {
            End(ref camera, world);
            return;
        }

        // --- Touches (a held mouse button counts as one touch on the desktop) ---
        int count = Raylib.GetTouchPointCount();
        var present = new HashSet<int>();
        bool attackHeld = false;
        for (int i = 0; i < count; i++)
        {
            int id = Raylib.GetTouchPointId(i);
            Vector2 at = Raylib.GetTouchPosition(i);
            present.Add(id);
            if (_spent.Contains(id))
                continue;
            if (id == _stickTouch)
            {
                _stick = Vector2.Clamp((at - StickCenter) / StickRadius, new Vector2(-1f), new Vector2(1f));
                if (_stick.Length() > 1f)
                    _stick = Vector2.Normalize(_stick);
            }
            else if (id == _lookTouch)
            {
                Vector2 delta = at - _lookLast;
                _lookLast = at;
                _yaw -= delta.X * TurnPerPixel;
                _pitch = Math.Clamp(_pitch + delta.Y * PitchPerPixel, MinPitch, MaxPitch);
            }
            else if (id == _attackTouch)
                attackHeld = true;
            else if (_stickTouch < 0 && _lookTouch != id && InStickZone(at) && Vector2.Distance(at, AttackCenter) > AttackRadius)
            {
                _stickTouch = id;
                _stick = Vector2.Clamp((at - StickCenter) / StickRadius, new Vector2(-1f), new Vector2(1f));
            }
            else if (Vector2.Distance(at, AttackCenter) <= AttackRadius * 1.15f)
            {
                _attackTouch = id;
                attackHeld = true;
            }
            else if (ExitButton.Contains(at))
            {
                End(ref camera, world);
                return;
            }
            else if (KinToggle.Contains(at))
            {
                HitKin = !HitKin;
                kin.PlayerMayHitKin = HitKin;
                _spent.Add(id); // This touch has done its one thing.
            }
            else if (JobToggle.Contains(at))
            {
                kin.CyclePlayerJob();
                _spent.Add(id);
            }
            else if (_lookTouch == -1)
            {
                _lookTouch = id;
                _lookLast = at;
            }
        }
        _spent.RemoveWhere(id => !present.Contains(id));
        if (!present.Contains(_stickTouch))
        {
            _stickTouch = -1;
            _stick = Vector2.Zero;
        }
        if (!present.Contains(_lookTouch))
            _lookTouch = -1;
        if (!present.Contains(_attackTouch))
            _attackTouch = -1;

        // --- Keys (desktop) ---
        Vector2 keys = Vector2.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up))
            keys.Y += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down))
            keys.Y -= 1f;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right))
            keys.X += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left))
            keys.X -= 1f;
        if (DebugForward)
            keys.Y += 1f;
        if (DebugAttack)
            attackHeld = true;
        if (Raylib.IsKeyDown(KeyboardKey.Q))
            _yaw += 1.8f * deltaTime;
        if (Raylib.IsKeyDown(KeyboardKey.E))
            _yaw -= 1.8f * deltaTime;
        if (Raylib.IsKeyDown(KeyboardKey.Space))
            attackHeld = true;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            End(ref camera, world);
            return;
        }

        // The stick has screen "up" = forward; the camera's forward on the ground is where it looks.
        Vector2 push = _stick.Length() > DeadZone ? new Vector2(_stick.X, -_stick.Y) : Vector2.Zero;
        if (keys != Vector2.Zero)
            push = Vector2.Normalize(keys);
        Vector3 forward = new(MathF.Sin(_yaw), 0f, MathF.Cos(_yaw));
        Vector3 right = Vector3.Cross(forward, Vector3.UnitY);
        Vector3 move = forward * push.Y + right * push.X;
        kin.PlayerMove = new Vector2(move.X, move.Z);
        kin.PlayerWantsStrike = attackHeld;

        // --- Camera: behind the kin, looking over its shoulder ---
        Vector3 head = kin.Position + new Vector3(0f, LookHeight, 0f);
        _focus = Vector3.Lerp(_focus, head, 1f - MathF.Exp(-12f * deltaTime));
        Vector3 offset = new Vector3(-MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), -MathF.Cos(_yaw) * MathF.Cos(_pitch)) * CameraDistance;
        Vector3 position = _focus + offset;
        float ground = World.GetHeightAt(position.X, position.Z) + 0.5f;
        if (position.Y < ground)
            position.Y = ground;
        camera.Position = position;
        camera.Target = _focus + new Vector3(0f, -LookBelow, 0f);
        camera.Up = Vector3.UnitY;
    }

    /// <summary>The on-screen controls and the kin's status, drawn over the 3D view.</summary>
    public void DrawHud(World world)
    {
        if (Kin is not { } kin)
            return;
        float s = Scale;

        // The stick.
        Vector2 center = StickCenter;
        Raylib.DrawCircleV(center, StickRadius, new Color(255, 255, 255, 50));
        Raylib.DrawCircleLinesV(center, StickRadius, new Color(255, 255, 255, 150));
        Raylib.DrawCircleV(center + _stick * StickRadius * 0.7f, 62f * s, new Color(255, 255, 255, 140));

        // Attack, and whether it may hit other clans.
        bool striking = _attackTouch >= 0 || Raylib.IsKeyDown(KeyboardKey.Space);
        Raylib.DrawCircleV(AttackCenter, AttackRadius, striking ? new Color(230, 90, 60, 200) : new Color(210, 70, 50, 130));
        Raylib.DrawCircleLinesV(AttackCenter, AttackRadius, new Color(255, 255, 255, 170));
        int attackFont = (int)(46 * s);
        string attackLabel = "Attack";
        Raylib.DrawText(attackLabel, (int)(AttackCenter.X - Raylib.MeasureText(attackLabel, attackFont) / 2f), (int)(AttackCenter.Y - attackFont / 2f), attackFont, Color.White);
        KinToggle.Draw(HitKin ? "Kin: on" : "Kin: off", highlighted: HitKin);
        JobToggle.Draw(JobLabel(kin.PlayerJob), highlighted: kin.PlayerJob != KinJob.None);
        ExitButton.Draw("Exit", highlighted: false);

        // Status.
        int font = (int)(40 * s);
        int x = (int)(310 * s);
        int y = (int)(24 * s);
        Raylib.DrawText($"{kin.Name}   Health {kin.Health}/{Bramblekin.MaxHealth}", x, y, font, Color.White);
        Raylib.DrawText($"Hunger {(int)kin.Hunger}%   Thirst {(int)kin.Thirst}%", x, y + (int)(font * 1.15f), font, kin.IsHungry || kin.IsThirsty ? new Color(255, 190, 120, 255) : Color.White);
        if (kin.PlayerHint is { } hint)
            Raylib.DrawText(hint, x, y + (int)(font * 2.3f), (int)(font * 0.85f), new Color(255, 220, 140, 255));
    }
}
