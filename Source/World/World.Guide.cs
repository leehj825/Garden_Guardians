using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>The gentle nudges a player can give the garden, each costing some favour (see World.Guide).</summary>
public enum GuideAction
{
    Rain,
    ClearSky,
    Bless,
    Calm,
    Quiet,
    Site,
}

public sealed partial class World
{
    /// <summary>Favour never builds up past this.</summary>
    public const float MaxFavour = 5f;

    /// <summary>Favour refills this fast (a point a minute) while the garden runs.</summary>
    private const float FavourPerSecond = 1f / 60f;

    /// <summary>What a nudge costs.</summary>
    public static float GuideCost(GuideAction action) => action switch
    {
        GuideAction.Rain => 2f,
        GuideAction.Quiet => 2f,
        GuideAction.Site => 2f,
        _ => 1f,
    };

    public static string GuideLabel(GuideAction action) => action switch
    {
        GuideAction.Rain => "Rain",
        GuideAction.ClearSky => "Clear",
        GuideAction.Bless => "Bless",
        GuideAction.Calm => "Calm",
        GuideAction.Quiet => "Hush",
        _ => "Site",
    };

    /// <summary>What a nudge does, in a line (for the Guide panel).</summary>
    public static string GuideHelp(GuideAction action) => action switch
    {
        GuideAction.Rain => "A day of good rain: more berries",
        GuideAction.ClearSky => "End a storm",
        GuideAction.Bless => "Heal the selected Bramblekin; it learns twice as fast for a while",
        GuideAction.Calm => "Ease the selected clan's quarrels with its neighbours",
        GuideAction.Quiet => "The Wolf Spider rests for a day",
        _ => "Point the selected clan to a place for a new village, then tap the ground",
    };

    /// <summary>The player's favour, 0 to <see cref="MaxFavour"/>: it refills with time and is spent on nudges. Saved with the garden.</summary>
    public float Favour { get; private set; } = 3f;

    /// <summary>Seconds of good rain left (the Rain nudge).</summary>
    public float GoodRainLeft { get; private set; }

    /// <summary>Seconds the Wolf Spider has left to rest (the Hush nudge).</summary>
    public float SpiderQuietLeft { get; private set; }

    /// <summary>Nudges given so far (for the achievements).</summary>
    public int NudgesGiven { get; private set; }

    private void UpdateGuide(float deltaTime)
    {
        Favour = MathF.Min(MaxFavour, Favour + FavourPerSecond * deltaTime);
        GoodRainLeft = MathF.Max(0f, GoodRainLeft - deltaTime);
        SpiderQuietLeft = MathF.Max(0f, SpiderQuietLeft - deltaTime);
    }

    /// <summary>Adds favour (a rewarded video), up to the cap.</summary>
    public void GrantFavour(float amount) => Favour = MathF.Min(MaxFavour, Favour + amount);

    /// <summary>
    /// Gives a nudge. <paramref name="kin"/>, <paramref name="clan"/> and <paramref name="site"/> are what the player has selected or tapped; returns
    /// null on success, else why it did not happen (nothing is spent then).
    /// </summary>
    public string? Guide(GuideAction action, Bramblekin? kin, KinGroup? clan, Vector3? site)
    {
        float cost = GuideCost(action);
        if (Favour < cost)
            return "Not enough favour yet";

        string told;
        switch (action)
        {
            case GuideAction.Rain:
                if (IsStorming)
                    return "A storm is already raining";
                if (GoodRainLeft > 0f)
                    return "It is already raining well";
                GoodRainLeft = DayLength;
                told = "Good rain falls on the garden for a day";
                break;
            case GuideAction.ClearSky:
                if (!IsStorming)
                    return "The sky is already clear";
                StormTimeLeft = 0f;
                told = "The storm clears";
                break;
            case GuideAction.Bless:
                if (kin is not { IsDead: false })
                    return "Tap a Bramblekin first";
                kin.Bless();
                QueueFloatingText(kin.Position, "Blessed!", Color.Gold, 1.5f);
                told = $"{kin.Name} is blessed";
                break;
            case GuideAction.Calm:
                if (clan is null)
                    return "Tap a Bramblekin or home of a clan first";
                int eased = 0;
                foreach (KinGroup other in _groups.Values)
                {
                    if (other == clan)
                        continue;
                    GroupRelation relation = RelationBetween(clan.Id, other.Id);
                    if (relation.Grievance > 0f)
                    {
                        relation.Grievance = MathF.Max(0f, relation.Grievance - 3f);
                        eased++;
                    }
                }
                if (eased == 0)
                    return $"{clan.CapitalTitle} has no quarrels";
                told = $"{clan.CapitalTitle}'s quarrels ease ({eased})";
                break;
            case GuideAction.Quiet:
                if (SpiderQuietLeft > 0f)
                    return "The spider is already resting";
                SpiderQuietLeft = DayLength;
                told = "The Wolf Spider rests for a day";
                break;
            default:
                if (clan is null)
                    return "Tap a Bramblekin or home of a clan first";
                if (site is not { } spot)
                    return "Tap the ground where it should settle";
                clan.GuidedSite = spot;
                QueueFloatingText(spot + new Vector3(0f, 1.2f, 0f), "New village?", Color.Gold, 1.5f);
                told = $"{clan.CapitalTitle} turns toward a new place";
                break;
        }

        Favour -= cost;
        NudgesGiven++;
        Game.AddEventLog($"[GUIDE] {told}");
        Chronicle($"A gentle hand: {told.ToLowerInvariant()}");
        return null;
    }
}
