using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// An emergent band of Bramblekin sharing one <see cref="Bramblekin.GroupId"/>.
/// Membership is owned by the Bramblekin themselves; <see cref="World"/>
/// rebuilds this view of it every frame, dissolving a group down to its
/// last survivor and electing a new Leader whenever the old one is gone.
/// </summary>
public sealed class KinGroup
{
    /// <summary>The age the clan has reached (see <see cref="World.EraOf"/>); only ever rises.</summary>
    public Era Era { get; set; }

    /// <summary>Cloth and cut stone the clan holds to trade (see <c>World.Goods</c>).</summary>
    public int Cloth { get; set; }

    public int CutStone { get; set; }

    /// <summary>Seconds until the clan next makes a good and next trades one (not saved).</summary>
    public float GoodsTimer { get; set; }

    public float TradeCooldown { get; set; }

    private static readonly Color[] Palette =
    {
        new(60, 120, 220, 255),  // Blue
        new(225, 195, 55, 255),  // Yellow
        new(205, 60, 55, 255),   // Red
        new(150, 80, 195, 255),  // Purple
        new(40, 180, 90, 255),   // Green
        new(240, 130, 40, 255),  // Orange
        new(40, 190, 200, 255),  // Teal
        new(230, 90, 170, 255),  // Pink
    };

    public Guid Id { get; }

    /// <summary>The group's colour: its members' head highlight, its Leader's banner, and the tethers between them.</summary>
    public Color Color { get; }

    /// <summary>First few hex digits of <see cref="Id"/>.</summary>
    /// <summary>Who keeps watch by home tonight (see World.UpdateNight); null by day.</summary>
    public Bramblekin? NightWatch { get; set; }

    /// <summary>Until when (game seconds) the clan is awake after an alarm.</summary>
    public float AlarmUntil { get; set; }

    /// <summary>Combs of honey its members have taken from the hive (see World.Beehive).</summary>
    public int HoneyTaken { get; set; }

    /// <summary>The year it last held a harvest feast (see World.Feasts).</summary>
    public int LastFeastYear { get; set; }

    /// <summary>Year × 4 + season of the last solstice festival kept (see World.Calendar).</summary>
    public int LastSolstice { get; set; }

    /// <summary>True while someone in the clan knows Medicine (see World.Crafts); refreshed at each Leader decision.</summary>
    public bool HasMedicine { get; set; }

    /// <summary>The clan this one owes fealty to, if any (see World.Kingdoms).</summary>
    public Guid? LiegeId { get; set; }

    /// <summary>What the clan reveres (see World.Beliefs).</summary>
    public Belief Belief { get; set; }

    /// <summary>Where its shrine stands (or is going up), if it has one.</summary>
    public Vector3? Shrine { get; set; }

    /// <summary>How far its shrine is raised, 0..1.</summary>
    public float ShrineRaised { get; set; }

    public string ShortId => Id.ToString("N")[..4];

    /// <summary>The group's name — "Thornwood clan", after the family of the Leader it was founded under (see World.NameGroup). Null for a moment while it's being founded.</summary>
    public string? Name { get; set; }

    /// <summary>How the event log refers to it: "the Thornwood clan".</summary>
    public string Title => Name is null ? $"group {ShortId}" : $"the {Name}";

    /// <summary><see cref="Title"/> to start a sentence with: "The Thornwood clan".</summary>
    public string CapitalTitle => char.ToUpperInvariant(Title[0]) + Title[1..];

    public List<Bramblekin> Members { get; } = new();

    public Bramblekin? Leader { get; private set; }

    /// <summary>Taken off the World's books (dissolved, merged or conquered) — see World.Disband.</summary>
    public bool IsDisbanded { get; set; }

    /// <summary>The member an ageing or ailing Leader has named to follow it, if any (see World.Succession).</summary>
    public Bramblekin? Heir { get; set; }

    /// <summary>The temper its decisions are made in this time round: the Leader's, tempered by its council (see World.Council).</summary>
    public Personality Counsel { get; set; } = new(0.5f, 0.5f, 0.5f);

    /// <summary>The Leader its council last talked out of eating first — so each is counted once.</summary>
    public Bramblekin? OverruledOnSharing { get; set; }

    /// <summary>A splinter being plotted in the group, if any (see World.Plots). Not saved: a loaded garden's plots start afresh.</summary>
    public Plot? Plot { get; set; }

    /// <summary>The group's shared home (and store), once it has one — see <see cref="World.UpdateGroupHomes"/>.</summary>
    public Shelter? Home { get; set; }

    /// <summary>A village's other homes, besides <see cref="Home"/> — see <see cref="World.PlanConstruction"/>.</summary>
    public List<Shelter> Annexes { get; } = new();

    /// <summary>The one home the group is building or upgrading right now, if any: its main home first, then a new one in the village.</summary>
    public Shelter? ConstructionSite =>
        Home is { NeedsTwigs: true, IsCollapsed: false } home ? home
        : Annexes.FirstOrDefault(a => a is { NeedsTwigs: true, IsCollapsed: false });

    /// <summary>Where a newly founded group means to settle — open ground away from other villages (see World.FindOpenGround). Null once it has a home, or if it has nowhere in mind.</summary>
    public Vector3? SettleTarget { get; set; }

    /// <summary>Food a budded group took from its parent village, set aside for its new store once it's built.</summary>
    public int Dowry { get; set; }

    /// <summary>Grain set aside to sow (see <see cref="Craft.Grain"/>), not to eat — unless it's that or starve. See World.SeedCorn.</summary>
    public int SeedCorn { get; set; }

    /// <summary>Wolf Spiders this group has brought down (the chronicle marks its first, then every fifth).</summary>
    public int SpidersSlain { get; set; }

    /// <summary>The clan's traditions — see <see cref="ClanCulture"/>.</summary>
    public ClanCulture Culture { get; } = new();

    /// <summary>Danger spots its members have run into, shared by all of them — see Bramblekin.Memory.</summary>
    public PlaceMemory Dangers { get; } = new(capacity: 6, mergeRadius: 4f);

    /// <summary>Where its members have found food lately, shared by all of them.</summary>
    public PlaceMemory FoodSpots { get; } = new(capacity: 6, mergeRadius: 5f);

    /// <summary>Counts down after a failed attempt to find a site for a group home.</summary>
    public float HomeSiteRetryTimer { get; set; }

    /// <summary>What the Leader has decided the group should be doing.</summary>
    public GroupGoal Goal { get; set; } = GroupGoal.Stockpile;

    /// <summary>Who may eat from the shared store — set whenever a new Leader takes over.</summary>
    public SharingRule Sharing { get; set; } = SharingRule.Equal;

    /// <summary>The Stag Beetle the Leader sent its Hunters after, if any.</summary>
    public StagBeetle? HuntTarget { get; set; }

    /// <summary>The threat near home the Leader sent its Guards against, if any.</summary>
    public ICombatant? DefendTarget { get; set; }

    /// <summary>The enemy store the Leader sent its Raiders against, if any — see World.Neighbours.</summary>
    public Shelter? WarTarget { get; set; }

    /// <summary>When the current raiding party gives up and heads home.</summary>
    public float RaidEndsAt { get; set; }

    /// <summary>No new raid before this (World.ElapsedSeconds).</summary>
    public float NextRaidAt { get; set; }

    /// <summary>Counts down to the Leader's next decision.</summary>
    public float DecisionTimer { get; set; }

    /// <summary>Counts down after a birth before the group can raise another — see <see cref="World.TryBirth"/>.</summary>
    public float BirthCooldown { get; set; }

    /// <summary>The Leader's broad character — see <see cref="LeaderStyle"/>.</summary>
    public LeaderStyle Style => Leader is not { } leader ? LeaderStyle.Moderate
        : leader.Personality.Aggression >= 0.6f && leader.Personality.Aggression >= leader.Personality.Intelligence ? LeaderStyle.Warlike
        : leader.Personality.Intelligence >= 0.6f ? LeaderStyle.Planner
        : LeaderStyle.Moderate;

    /// <summary>The colour a clan with <paramref name="id"/> flies — worked out from the id, so it's known even after the clan is gone.</summary>
    public static Color ColorOf(Guid id) => Palette[(int)((uint)id.GetHashCode() % (uint)Palette.Length)];

    public KinGroup(Guid id)
    {
        Id = id;
        Color = ColorOf(id);
    }

    /// <summary>
    /// Elects a Leader: the living member with the highest
    /// <see cref="Bramblekin.LeadershipScore"/> (Intelligence plus earned
    /// Reputation; lowest ID breaks a tie). Only happens when the group
    /// forms or its Leader is gone — a sitting Leader can only be replaced
    /// by losing a challenge (see <see cref="World.ResolveDuel"/>).
    /// </summary>
    public void ElectLeader()
    {
        Bramblekin? best = null;
        bool anyAdult = Members.Any(m => !m.IsDead && !m.IsYoung);
        foreach (Bramblekin member in Members)
        {
            if (member.IsDead || (anyAdult && member.IsYoung))
                continue;
            if (best is null ||
                member.LeadershipScore > best.LeadershipScore ||
                (member.LeadershipScore == best.LeadershipScore && member.ID < best.ID))
                best = member;
        }
        Leader = best;
    }

    /// <summary>True while the Leader is alive and still in the group.</summary>
    public bool HasSittingLeader => Leader is { IsDead: false } leader && leader.GroupId == Id;

    /// <summary>A coup: <paramref name="leader"/> takes over.</summary>
    public void SetLeader(Bramblekin leader) => Leader = leader;
}
