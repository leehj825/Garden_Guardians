using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// An emergent band of Bramblekin sharing one <see cref="Bramblekin.GroupId"/>.
/// Membership is owned by the Bramblekin themselves; <see cref="World"/>
/// rebuilds this view of it every frame, dissolving a group down to its
/// last survivor and re-electing the Leader — always the most Intelligent
/// member.
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

    /// <summary>First few hex digits of <see cref="Id"/>, for the HUD and event log.</summary>
    public string ShortId => Id.ToString("N")[..4];

    public List<Bramblekin> Members { get; } = new();

    public Bramblekin? Leader { get; private set; }

    /// <summary>The group's shared home (and store), once it has one — see <see cref="World.UpdateGroupHomes"/>.</summary>
    public Shelter? Home { get; set; }

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

    /// <summary>Counts down to the Leader's next decision.</summary>
    public float DecisionTimer { get; set; }

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

    /// <summary>The living member with the highest Intelligence leads (lowest ID breaks a tie).</summary>
    public void ElectLeader()
    {
        Bramblekin? best = null;
        foreach (Bramblekin member in Members)
        {
            if (member.IsDead)
                continue;
            if (best is null ||
                member.Personality.Intelligence > best.Personality.Intelligence ||
                (member.Personality.Intelligence == best.Personality.Intelligence && member.ID < best.ID))
                best = member;
        }
        Leader = best;
    }
}
