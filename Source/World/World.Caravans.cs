using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>A trade on its way: three carriers walk the goods from the seller's home to the buyer's (drawing only — the trade itself is settled at once).</summary>
    private sealed class Caravan
    {
        public Caravan(Vector3 from, Vector3 to, bool cloth)
        {
            From = from;
            To = to;
            Cloth = cloth;
            Duration = Math.Clamp(Vector3.Distance(from, to) / CaravanSpeed, 6f, 40f);
        }

        public Vector3 From { get; }
        public Vector3 To { get; }
        public bool Cloth { get; }
        public float Duration { get; }
        public float Elapsed { get; set; }
    }

    /// <summary>Metres a second the carriers walk.</summary>
    private const float CaravanSpeed = 1.6f;

    [NotSaved]
    private readonly List<Caravan> _caravans = new();

    private void UpdateCaravans(float deltaTime)
    {
        for (int i = _caravans.Count - 1; i >= 0; i--)
        {
            _caravans[i].Elapsed += deltaTime;
            if (_caravans[i].Elapsed >= _caravans[i].Duration)
                _caravans.RemoveAt(i);
        }
    }

    private static readonly Color ClothBundle = new(150, 110, 190, 255), StoneBundle = new(150, 150, 140, 255), CarrierBody = new(120, 90, 50, 255);

    /// <summary>The carriers on the roads between villages (skipped in low detail).</summary>
    private void DrawCaravans(Camera3D camera)
    {
        if (LowDetail)
            return;
        foreach (Caravan caravan in _caravans)
        {
            float t = caravan.Elapsed / caravan.Duration;
            for (int k = 0; k < 3; k++)
            {
                float lead = Math.Clamp(t - k * 0.35f / MathF.Max(1f, Vector3.Distance(caravan.From, caravan.To)), 0f, 1f);
                Vector3 flat = Vector3.Lerp(caravan.From, caravan.To, lead);
                if (!IsVisible(flat, camera))
                    continue;
                float bob = MathF.Abs(MathF.Sin((caravan.Elapsed + k) * 6f)) * 0.03f;
                var foot = new Vector3(flat.X, GetHeightAt(flat.X, flat.Z) + bob, flat.Z);
                Raylib.DrawCylinder(foot, 0.07f, 0.09f, 0.28f, 6, CarrierBody);
                Raylib.DrawCube(foot + new Vector3(0f, 0.36f, 0f), 0.16f, 0.1f, 0.12f, caravan.Cloth ? ClothBundle : StoneBundle);
            }
        }
    }
}
