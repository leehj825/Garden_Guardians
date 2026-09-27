using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>What a season's weather turned out like — see World.Weather.</summary>
public enum Weather
{
    Fair,

    /// <summary>A good year: berries grow half again as fast.</summary>
    Bountiful,

    /// <summary>Summer or autumn only: berries (and bushes) grow at half the pace.</summary>
    Drought,

    /// <summary>Winter only: even less food, and bitter cold for anyone caught outdoors.</summary>
    HarshWinter,
}

public sealed partial class World
{
    /// <summary>Odds a spring, summer or autumn is bountiful…</summary>
    private const double BountifulChance = 0.15;

    /// <summary>…or (summer and autumn only) a drought.</summary>
    private const double DroughtChance = 0.15;

    /// <summary>Odds a winter is a harsh one.</summary>
    private const double HarshWinterChance = 0.3;

    /// <summary>Outside winter and droughts, a storm blows up with this chance each second…</summary>
    private const float StormChancePerSecond = 1f / 400f;

    /// <summary>…and lasts this long.</summary>
    private const float StormDuration = 25f;

    /// <summary>A storm brings down a twig this often (s), on top of the usual…</summary>
    private const float StormTwigInterval = 0.4f;

    /// <summary>…until this many more than usual are lying about.</summary>
    private const int StormExtraTwigs = 25;

    /// <summary>In a harsh winter, anyone outdoors gets hungry this much faster.</summary>
    public const float HarshWinterCold = 1.25f;

    private float _stormTwigTimer;

    public Weather CurrentWeather { get; private set; } = Weather.Fair;

    /// <summary>Seconds left of the storm now blowing, if any.</summary>
    public float StormTimeLeft { get; private set; }

    public bool IsStorming => StormTimeLeft > 0f;

    public int Droughts { get; private set; }
    public int HarshWinters { get; private set; }
    public int BountifulSeasons { get; private set; }
    public int Storms { get; private set; }

    /// <summary>How the season's weather scales its Food — see <see cref="FoodAbundance"/>.</summary>
    public float WeatherFoodFactor => CurrentWeather switch
    {
        Weather.Bountiful => 1.5f,
        Weather.Drought => 0.5f,
        Weather.HarshWinter => 0.5f,
        _ => 1f,
    };

    /// <summary>How much faster Hunger rises for anyone not sheltered (see <see cref="HarshWinterCold"/>).</summary>
    public float ColdFactor => CurrentWeather == Weather.HarshWinter ? HarshWinterCold : 1f;

    /// <summary>"Drought", "Harsh winter" or "Bountiful" (plus "storm!") for the HUD, or null in fair weather.</summary>
    public string? WeatherLabel
    {
        get
        {
            string? kind = CurrentWeather switch
            {
                Weather.Bountiful => "Bountiful",
                Weather.Drought => "Drought",
                Weather.HarshWinter => "Harsh winter",
                _ => null,
            };
            if (IsStorming)
                kind = kind is null ? "Storm!" : $"{kind}, storm!";
            return kind;
        }
    }

    /// <summary>Each new season rolls its weather: winters may be harsh; the rest of the year may be bountiful, or (in summer and autumn) droughty.</summary>
    private void RollWeather(Season season)
    {
        double roll = Rng.NextDouble();
        CurrentWeather = season == Season.Winter
            ? roll < HarshWinterChance ? Weather.HarshWinter : Weather.Fair
            : season != Season.Spring && roll < DroughtChance ? Weather.Drought
            : roll > 1 - BountifulChance ? Weather.Bountiful
            : Weather.Fair;

        switch (CurrentWeather)
        {
            case Weather.Drought:
                Droughts++;
                Game.AddEventLog($"[WEATHER] Year {Year}: a drought has set in - berries will be scarce this {season.ToString().ToLowerInvariant()}");
                Chronicle($"A drought in the {season.ToString().ToLowerInvariant()}");
                break;
            case Weather.HarshWinter:
                HarshWinters++;
                Game.AddEventLog($"[WEATHER] Year {Year}: a harsh winter - bitter cold, and hardly any food");
                Chronicle("A harsh winter");
                break;
            case Weather.Bountiful:
                BountifulSeasons++;
                Game.AddEventLog($"[WEATHER] Year {Year}: a bountiful {season.ToString().ToLowerInvariant()} - berries everywhere");
                break;
        }
    }

    /// <summary>Storms blow up now and then (never in winter or a drought): they strip loose berries and bring down twigs.</summary>
    private void UpdateWeather(float deltaTime)
    {
        if (IsStorming)
        {
            StormTimeLeft -= deltaTime;
            _stormTwigTimer -= deltaTime;
            if (_stormTwigTimer <= 0f)
            {
                _stormTwigTimer = StormTwigInterval;
                if (LooseTwigCount < MaxLooseTwigs + StormExtraTwigs)
                    ActivateTwig(RandomOpenSpot());
            }
            return;
        }

        if (CurrentSeason == Season.Winter || CurrentWeather == Weather.Drought)
            return;
        if (Rng.NextDouble() < StormChancePerSecond * deltaTime)
            StartStorm();
    }

    private void StartStorm()
    {
        StormTimeLeft = StormDuration;
        _stormTwigTimer = 0f;
        Storms++;

        // Half the loose berries nobody is already going for are blown away.
        int stripped = 0;
        foreach (FoodShard food in FoodShards)
        {
            if (food is { IsActive: true, IsCarried: false, ClaimedBy: null, Kind: FoodShardKind.Berry } && Rng.NextDouble() < 0.5)
            {
                food.Deactivate();
                stripped++;
            }
        }
        Game.AddEventLog($"[WEATHER] A storm sweeps the garden, stripping {stripped} berries and bringing down twigs");
    }

    /// <summary>Anywhere open on the map (inside the edge margin, clear of obstacles).</summary>
    private Vector3 RandomOpenSpot() => RandomFreePoint(0.3f, Bramblekin.EdgeMargin);

    // --- The look of it -------------------------------------------------------------------

    /// <summary>Visual-only randomness (rain), so drawing never disturbs the simulation's own dice.</summary>
    private static readonly Random VisualRng = new();

    private static readonly Color RainColor = new(170, 185, 205, 150);

    /// <summary>Rain streaks around the camera's focus while a storm blows.</summary>
    private void DrawRain(Camera3D camera)
    {
        if (!IsStorming)
            return;
        float spread = MathF.Min(40f, Vector3.Distance(camera.Position, camera.Target) * 0.6f);
        for (int i = 0; i < 220; i++)
        {
            float x = camera.Target.X + ((float)VisualRng.NextDouble() * 2f - 1f) * spread;
            float z = camera.Target.Z + ((float)VisualRng.NextDouble() * 2f - 1f) * spread;
            float y = GetHeightAt(x, z) + (float)VisualRng.NextDouble() * 6f;
            var top = new Vector3(x, y + 0.6f, z);
            Raylib.DrawLine3D(top, top + new Vector3(0.12f, -0.6f, 0.05f), RainColor);
        }
    }

    /// <summary>Adjusts the season's lawn tint for the weather: parched gold in a drought, deeper frost in a harsh winter.</summary>
    private (Color Tint, float Amount) WeatherTint((Color Tint, float Amount) seasonal) => CurrentWeather switch
    {
        Weather.Drought => (Blend(seasonal.Amount > 0f ? seasonal.Tint : DroughtTint, DroughtTint, 0.7f), MathF.Max(seasonal.Amount, 0.4f)),
        Weather.HarshWinter => (seasonal.Tint, MathF.Min(0.8f, seasonal.Amount + 0.2f)),
        _ => seasonal,
    };

    private static readonly Color DroughtTint = new(205, 175, 95, 255);

    private static readonly Color StormSky = new(95, 105, 118, 255);
}
