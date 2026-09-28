namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>A Leader naming its heir favours its own close kin by this much of a claim to lead…</summary>
    private const float HeirKinBonus = 0.3f;

    /// <summary>…and its Friends by this much.</summary>
    private const float HeirFriendBonus = 0.15f;

    /// <summary>A named heir takes over with this much Reputation to go with it.</summary>
    private const float HeirReputation = 0.5f;

    /// <summary>A Leader lost with no heir named leaves a scramble: every follower's loyalty drops this much.</summary>
    private const float LeaderlessLoyaltyCost = 0.08f;

    public int HeirsNamed { get; private set; }

    /// <summary>Leaders succeeded by the heir they named.</summary>
    public int Successions { get; private set; }

    /// <summary>Leaders lost with no heir named, leaving a scramble for the role.</summary>
    public int LeaderlessScrambles { get; private set; }

    /// <summary>
    /// At each Leader decision: a Leader feeling its years (an elder), sick
    /// or badly hurt names an heir if it hasn't one — the member with the
    /// best claim to lead, leaning toward its own close kin and friends.
    /// </summary>
    private void ConsiderHeir(KinGroup group, Bramblekin leader)
    {
        if (group.Heir is { IsDead: false } named && named.GroupId == group.Id)
            return;
        group.Heir = null;
        if (!leader.IsElder && !leader.IsSick && leader.Health >= Bramblekin.MaxHealth / 2)
            return;

        Bramblekin? heir = group.Members
            .Where(m => m != leader && !m.IsDead && !m.IsYoung)
            .MaxBy(m => m.LeadershipScore + (m.IsCloseKinOf(leader) ? HeirKinBonus : 0f) +
                        (leader.RelationshipTo(m) == RelationshipState.Friend ? HeirFriendBonus : 0f));
        if (heir is null)
            return;

        group.Heir = heir;
        HeirsNamed++;
        string tie = heir.IsCloseKinOf(leader) ? "its own kin " : "";
        Game.AddEventLog($"[LEADER] {leader.Name} named {tie}{heir.Name} heir to {group.Title}");
    }

    /// <summary>
    /// The Leader is gone: its named heir, if still here, takes over (see
    /// <see cref="ConsiderHeir"/>); otherwise the group elects one, and the
    /// scramble costs everyone some loyalty. Returns true if the heir took over.
    /// </summary>
    private bool Succeed(KinGroup group, Bramblekin? previous)
    {
        Bramblekin? heir = group.Heir;
        group.Heir = null;
        if (heir is { IsDead: false, IsYoung: false } && heir.GroupId == group.Id)
        {
            group.SetLeader(heir);
            heir.AddReputation(HeirReputation);
            Successions++;
            return true;
        }

        group.ElectLeader();
        if (previous is not null && group.Members.Count > 2)
        {
            LeaderlessScrambles++;
            foreach (Bramblekin member in group.Members)
            {
                if (member != group.Leader && !member.IsDead)
                    member.SetLoyalty(member.Loyalty - LeaderlessLoyaltyCost);
            }
        }
        return false;
    }
}
