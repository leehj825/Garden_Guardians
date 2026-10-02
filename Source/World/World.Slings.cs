using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Slings --------------------------------------------------------------------------

    /// <summary>A pebble is in the air this long (s) — just long enough to see it fly.</summary>
    private const float PebbleFlightSeconds = 0.22f;

    private static readonly Color PebbleColor = new(120, 118, 112, 255);

    /// <summary>A pebble in flight (drawn only; its blow has already landed).</summary>
    private sealed class PebbleShot
    {
        public required Vector3 From { get; init; }
        public required Vector3 To { get; init; }
        public float Age { get; set; }
    }

    private readonly List<PebbleShot> _pebbles = new();

    /// <summary>Pebbles loosed from slings, and how many hit.</summary>
    public int PebblesLoosed { get; private set; }

    public int PebbleHits { get; private set; }

    /// <summary>Hornets brought down by a sling.</summary>
    public int SlingKills { get; private set; }

    /// <summary>
    /// A slinger looses a pebble at <paramref name="target"/>: on a
    /// <paramref name="hit"/> it takes <paramref name="damage"/> straight
    /// away; a miss sails past. Either way the pebble is drawn flying.
    /// </summary>
    public void LoosePebble(Bramblekin slinger, ICombatant target, bool hit, int damage)
    {
        PebblesLoosed++;
        Vector3 from = slinger.Position + new Vector3(0f, Bramblekin.BodyHeight * 0.8f, 0f);
        Vector3 to = target.Position + new Vector3(0f, target is Hornet ? 0.35f : target.CollisionRadius, 0f);
        if (!hit)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            to += new Vector3(MathF.Cos(angle), 0.1f, MathF.Sin(angle)) * 0.6f;
        }
        _pebbles.Add(new PebbleShot { From = from, To = to });
        if (!hit)
            return;

        PebbleHits++;
        target.TakeHit(damage, slinger, this);
        if (!target.IsDead)
            return;
        SlingKills++;
        if (SlingKills == 1)
            Game.AddEventLog($"[HUNT] {slinger.Name} brought down a hornet with a sling - the first sling kill");
    }

    private void UpdatePebbles(float deltaTime)
    {
        for (int i = _pebbles.Count - 1; i >= 0; i--)
        {
            _pebbles[i].Age += deltaTime;
            if (_pebbles[i].Age >= PebbleFlightSeconds)
                _pebbles.RemoveAt(i);
        }
    }

    /// <summary>Each pebble on its way, on a shallow arc.</summary>
    private void DrawPebbles()
    {
        foreach (PebbleShot pebble in _pebbles)
        {
            float t = pebble.Age / PebbleFlightSeconds;
            Vector3 at = Vector3.Lerp(pebble.From, pebble.To, t) + new Vector3(0f, 0.25f * 4f * t * (1f - t), 0f);
            Raylib.DrawSphereEx(at, 0.05f, 4, 5, PebbleColor);
        }
    }
}
