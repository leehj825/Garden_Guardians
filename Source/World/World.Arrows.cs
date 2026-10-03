using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Arrows --------------------------------------------------------------------------

    /// <summary>How fast (m/s) an arrow flies while it still has range, and how fast it falls (m/s²) once it has none.</summary>
    private const float ArrowSpeed = 20f, ArrowFall = 14f;

    /// <summary>Past its range an arrow keeps this share of its speed, and slows to a stop as it drops.</summary>
    private const float ArrowSpentSpeed = 0.3f;

    /// <summary>An arrow that has landed lies this long (s) before it is gone.</summary>
    private const float ArrowLieSeconds = 4f;

    /// <summary>Drawn length (m) of an arrow.</summary>
    private const float ArrowLength = 0.6f;

    private static readonly Color ArrowShaftColor = new(150, 105, 60, 255), ArrowHeadColor = new(120, 120, 125, 255), ArrowFletchColor = new(235, 230, 215, 255);

    private sealed class ArrowShot
    {
        public required Bramblekin Shooter { get; init; }
        public Vector3 At { get; set; }
        public Vector3 Direction { get; set; }
        public float Traveled { get; set; }
        public float FallSpeed { get; set; }
        public float Lying { get; set; }
        public bool Landed { get; set; }
    }

    private readonly List<ArrowShot> _arrows = new();

    public int ArrowsLoosed { get; private set; }

    public int ArrowHits { get; private set; }

    /// <summary>Everything an arrow of <paramref name="shooter"/> can hit: all the wildlife and enemies, and other clans' kin if the player allows it.</summary>
    public IEnumerable<ICombatant> ArrowTargets(Bramblekin shooter)
    {
        if (Spider is { IsDead: false } spider)
            yield return spider;
        foreach (Hornet hornet in Hornets)
            if (!hornet.IsDead) yield return hornet;
        foreach (InvaderSpider invader in Invaders)
            if (!invader.IsDead) yield return invader;
        foreach (StagBeetle beetle in Beetles)
            if (!beetle.IsDead) yield return beetle;
        foreach (Grub grub in Grubs)
            if (!grub.IsDead) yield return grub;
        foreach (Ant ant in Ants)
            if (!ant.IsDead) yield return ant;
        foreach (HillGuard guard in HillGuards)
            if (!guard.IsDead && !guard.IsHidden) yield return guard;
        if (shooter.PlayerMayHitKin)
        {
            foreach (Bramblekin other in QueryColonyWithin(shooter.Position, Bramblekin.ArrowRange + 2f))
                if (other != shooter && !other.IsDead && !other.IsSheltered && !shooter.IsClanmate(other))
                    yield return other;
        }
    }

    /// <summary>An arrow leaves <paramref name="from"/> along <paramref name="direction"/> (a unit vector). It flies straight for <see cref="Bramblekin.ArrowRange"/> metres, hitting the first target in its way, then drops.</summary>
    public void LooseArrow(Bramblekin shooter, Vector3 from, Vector3 direction)
    {
        ArrowsLoosed++;
        _arrows.Add(new ArrowShot { Shooter = shooter, At = from, Direction = Vector3.Normalize(direction) });
    }

    /// <summary>Whether a segment of an arrow's flight (from <paramref name="a"/> to <paramref name="b"/>) passes through <paramref name="target"/>'s body, taken as an upright cylinder.</summary>
    private static bool ArrowPassesThrough(Vector3 a, Vector3 b, ICombatant target)
    {
        float radius = target.CollisionRadius + 0.08f;
        float bottom = target is Hornet ? -0.3f : -0.05f;
        float top = target is Hornet ? 0.8f : MathF.Max(target.CollisionRadius * 2f, 0.5f);
        Vector3 p = target.Position;
        for (int i = 0; i <= 4; i++) // (a step is at most a third of a metre, and these bodies are bigger than that)
        {
            Vector3 at = Vector3.Lerp(a, b, i / 4f);
            float dx = at.X - p.X, dz = at.Z - p.Z, dy = at.Y - p.Y;
            if (dx * dx + dz * dz <= radius * radius && dy >= bottom && dy <= top)
                return true;
        }
        return false;
    }

    private void UpdateArrows(float deltaTime)
    {
        for (int i = _arrows.Count - 1; i >= 0; i--)
        {
            ArrowShot arrow = _arrows[i];
            if (arrow.Landed)
            {
                arrow.Lying += deltaTime;
                if (arrow.Lying >= ArrowLieSeconds)
                    _arrows.RemoveAt(i);
                continue;
            }

            Vector3 before = arrow.At;
            if (arrow.Traveled < Bramblekin.ArrowRange)
            {
                float step = MathF.Min(ArrowSpeed * deltaTime, Bramblekin.ArrowRange - arrow.Traveled);
                arrow.At += arrow.Direction * step;
                arrow.Traveled += step;
            }
            else
            {
                // Out of range: the arrow loses its push and falls.
                arrow.FallSpeed += ArrowFall * deltaTime;
                Vector3 flat = new Vector3(arrow.Direction.X, 0f, arrow.Direction.Z);
                flat = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : Vector3.Zero;
                float remaining = MathF.Max(0f, ArrowSpentSpeed * ArrowSpeed * (1f - arrow.FallSpeed / 6f));
                arrow.At += (flat * remaining + new Vector3(0f, -arrow.FallSpeed, 0f)) * deltaTime;
                Vector3 heading = flat * MathF.Max(remaining, 0.5f) + new Vector3(0f, -arrow.FallSpeed, 0f);
                arrow.Direction = Vector3.Normalize(heading); // (nose dipping as it falls)
            }

            ICombatant? struck = null;
            foreach (ICombatant target in ArrowTargets(arrow.Shooter))
            {
                if (ArrowPassesThrough(before, arrow.At, target))
                {
                    struck = target;
                    break;
                }
            }
            if (struck is not null)
            {
                ArrowHits++;
                _arrows.RemoveAt(i);
                struck.TakeHit(arrow.Shooter.ArrowHit(struck, this), arrow.Shooter, this);
                continue;
            }

            float ground = GetHeightAt(arrow.At.X, arrow.At.Z);
            if (arrow.At.Y <= ground + 0.03f)
            {
                arrow.At = new Vector3(arrow.At.X, ground + 0.03f, arrow.At.Z);
                arrow.Landed = true; // (stuck in the ground, or lying in it)
            }
        }
    }

    /// <summary>Each arrow: a wooden shaft, a stone head and a white fletching, along the way it flies.</summary>
    private void DrawArrows()
    {
        foreach (ArrowShot arrow in _arrows)
        {
            Vector3 d = arrow.Landed ? Vector3.Normalize(new Vector3(arrow.Direction.X, MathF.Min(arrow.Direction.Y, -0.3f), arrow.Direction.Z)) : arrow.Direction;
            Vector3 tip = arrow.At;
            Vector3 tail = tip - d * ArrowLength;
            Vector3 headBase = tip - d * 0.07f;
            Raylib.DrawCylinderEx(tail, headBase, 0.014f, 0.014f, 5, ArrowShaftColor);
            Raylib.DrawCylinderEx(headBase, tip, 0.035f, 0f, 5, ArrowHeadColor);

            Vector3 side = Vector3.Cross(d, Vector3.UnitY);
            side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : Vector3.UnitX;
            Vector3 up = Vector3.Normalize(Vector3.Cross(side, d));
            Vector3 feather = tail + d * 0.09f;
            foreach (Vector3 way in new[] { side, up })
            {
                Raylib.DrawTriangle3D(tail, feather + way * 0.06f, feather - way * 0.06f, ArrowFletchColor);
                Raylib.DrawTriangle3D(tail, feather - way * 0.06f, feather + way * 0.06f, ArrowFletchColor);
            }
        }
    }
}
