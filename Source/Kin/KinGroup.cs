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
    public string ShortId => Id.ToString("N")[..4];

    /// <summary>The group's name — "Thornwood clan", after the family of the Leader it was founded under (see World.NameGroup). Null for a moment while it's being founded.</summary>
    public string? Name { get; set; }

    /// <summary>How the event log refers to it: "the Thornwood clan".</summary>
    public string Title => Name is null ? $"group {ShortId}" : $"the {Name}";

    /// <summary><see cref="Title"/> to start a sentence with: "The Thornwood clan".</summary>
    public string CapitalTitle => char.ToUpperInvariant(Title[0]) + Title[1..];

    public List<Bramblekin> Members { get; } = new();

    public Bramblekin? Leader { get; private set; }

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

    public KinGroup(Guid id)
    {
        Id = id;
        Color = Palette[(int)((uint)id.GetHashCode() % (uint)Palette.Length)];
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
