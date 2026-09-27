using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>The four seasons of the garden's year — see <see cref="World.CurrentSeason"/>.</summary>
public enum Season
{
    Spring,
    Summer,
    Autumn,
    Winter,
}

public sealed partial class World
{
    /// <summary>Seconds per season; a year is four of them.</summary>
    public const float SeasonLength = 150f;

    /// <summary>A season's colour cast over the lawn, blended in by amount (0 = none) — summer is the lawn's own green.</summary>
    private static readonly (Color Tint, float Amount)[] SeasonTints =
    {
        (new Color(150, 220, 110, 255), 0.12f), // Spring: fresh, pale green.
        (new Color(0, 0, 0, 255), 0f),          // Summer: the lawn as it is.
        (new Color(195, 145, 55, 255), 0.35f),  // Autumn: yellowing, browning.
        (new Color(230, 235, 240, 255), 0.55f), // Winter: frosted over.
    };

    private static readonly Color[] SeasonSkies =
    {
        new(140, 200, 240, 255), // Spring
        new(135, 190, 235, 255), // Summer
        new(160, 180, 200, 255), // Autumn
        new(185, 195, 205, 255), // Winter
    };

    /// <summary>How the year's quarters differ: how fast wild Berries grow, and how many the lawn can hold, relative to normal.</summary>
    public static float AbundanceOf(Season season) => season switch
    {
        Season.Spring => 1.0f,
        Season.Summer => 1.3f,
        Season.Autumn => 0.8f,
        _ => 0.3f, // Winter: lean — the stores and the hunt have to carry everyone through.
    };

    private Season _announcedSeason = Season.Spring;

    public Season CurrentSeason => (Season)((int)(ElapsedSeconds / SeasonLength) % 4);

    public int Year => (int)(ElapsedSeconds / (SeasonLength * 4)) + 1;

    /// <summary>0 at the start of the current season, approaching 1 at its end.</summary>
    public float SeasonProgress => ElapsedSeconds % SeasonLength / SeasonLength;

    /// <summary>The current season's Food abundance — see <see cref="AbundanceOf"/> — scaled by its weather (see World.Weather).</summary>
    public float FoodAbundance => AbundanceOf(CurrentSeason) * WeatherFoodFactor;

    /// <summary>Announces each change of season in the event log.</summary>
    private void UpdateSeason()
    {
        Season season = CurrentSeason;
        if (season == _announcedSeason)
            return;

        _announcedSeason = season;
        RollWeather(season);
        Game.AddEventLog(season switch
        {
            Season.Spring => $"[SEASON] Year {Year}: Spring - the berries come back",
            Season.Summer => $"[SEASON] Year {Year}: Summer - food is plentiful",
            Season.Autumn => $"[SEASON] Year {Year}: Autumn - time to stock up for winter",
            _ => $"[SEASON] Year {Year}: Winter - food is scarce",
        });
    }

    /// <summary>
    /// The lawn's seasonal colour cast right now: the current season's tint,
    /// easing into the next one's over the last fifth of the season so the
    /// change is gradual rather than a sudden switch.
    /// </summary>
    public (Color Tint, float Amount) SeasonTint => WeatherTint(SeasonalTint);

    private (Color Tint, float Amount) SeasonalTint
    {
        get
        {
            int current = (int)CurrentSeason;
            int next = (current + 1) % 4;
            float blend = Math.Clamp((SeasonProgress - 0.8f) / 0.2f, 0f, 1f);
            var (tintA, amountA) = SeasonTints[current];
            var (tintB, amountB) = SeasonTints[next];
            // Summer's "tint" has no colour, so borrow the neighbour's while easing in or out of it.
            if (amountA == 0f)
                tintA = tintB;
            if (amountB == 0f)
                tintB = tintA;
            return (Blend(tintA, tintB, blend), amountA + (amountB - amountA) * blend);
        }
    }

    /// <summary>The sky colour for the current season, easing into the next one like <see cref="SeasonTint"/>.</summary>
    public Color SkyColor
    {
        get
        {
            int current = (int)CurrentSeason;
            float blend = Math.Clamp((SeasonProgress - 0.8f) / 0.2f, 0f, 1f);
            Color sky = Blend(SeasonSkies[current], SeasonSkies[(current + 1) % 4], blend);
            return IsStorming ? Blend(sky, StormSky, 0.75f) : sky;
        }
    }

    private static Color Blend(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        (byte)255);
}
