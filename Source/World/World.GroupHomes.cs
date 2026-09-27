using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>After failing to find a site for a group home, the group waits this long before looking again.</summary>
    private const float GroupSiteRetryDelay = 5f;

    /// <summary>
    /// Group homes, once a frame. A group without a home adopts the best
    /// one any member already has (a House over a Tent, a finished home
    /// over a site, then the fuller store); failing that, its Leader marks
    /// out a site near where it last found Food. Every member then lives
    /// there, carrying over what was in its old store. A group bigger than
    /// a Tent holds starts upgrading it into a House.
    /// </summary>
    private void UpdateGroupHomes(float deltaTime)
    {
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Home is { IsCollapsed: true })
                group.Home = null;

            group.Home ??= BestMemberHome(group);
            if (group.Home is null)
            {
                group.HomeSiteRetryTimer -= deltaTime;
                if (group.HomeSiteRetryTimer > 0f || group.Leader is not { } leader)
                    continue;

                group.Home = TryCreateShelterSite(leader.FoodMemory ?? leader.Position, owner: null, groupId: group.Id);
                if (group.Home is null)
                {
                    group.HomeSiteRetryTimer = GroupSiteRetryDelay;
                    continue;
                }
                Game.AddEventLog($"[SETTLE] Group {group.ShortId} is building a home");
            }

            Shelter home = group.Home;
            home.GroupId = group.Id;
            home.Owner = null;
            home.AbandonedSeconds = 0f;
            foreach (Bramblekin member in group.Members)
            {
                if (member.Home != home)
                    MoveIn(member, home);
            }

            if (ShouldUpgrade(group, home))
            {
                home.BeginUpgrade();
                Game.AddEventLog($"[SETTLE] Group {group.ShortId} ({group.Members.Count} strong) is upgrading its Tent into a House");
            }
        }
    }

    /// <summary>A group outgrowing its finished Tent upgrades it into a House.</summary>
    private static bool ShouldUpgrade(KinGroup group, Shelter home) =>
        home.IsBuilt && home.Tier == ShelterTier.Tent && !home.IsUpgrading &&
        group.Members.Count > Shelter.TentResidentCapacity;

    /// <summary>The best home any member of <paramref name="group"/> already has, if any.</summary>
    private static Shelter? BestMemberHome(KinGroup group)
    {
        Shelter? best = null;
        int bestScore = int.MinValue;
        foreach (Bramblekin member in group.Members)
        {
            if (member.Home is not { IsCollapsed: false } candidate)
                continue;

            int score = (candidate.Tier == ShelterTier.House ? 1000 : 0) + (candidate.IsBuilt ? 100 : 0) + candidate.StoredFood;
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>Moves <paramref name="member"/> into its group's home, carrying over what was in its old store (whatever doesn't fit stays behind).</summary>
    private static void MoveIn(Bramblekin member, Shelter home)
    {
        if (member.Home is { } old && old != home && home.IsBuilt)
        {
            while (old.StoredFood > 0 && !home.StoreIsFull && old.TryWithdraw())
                home.TryDeposit();
        }
        member.SetHome(home);
    }

    /// <summary>Counts, for every shelter, how many of its residents are inside it right now — see <see cref="Shelter.IsOvercrowded"/>.</summary>
    private void CountShelterOccupants()
    {
        foreach (Shelter shelter in Shelters)
            shelter.Occupants = 0;

        foreach (Bramblekin kin in Colony)
        {
            if (!kin.IsDead && kin.IsInsideHome)
                kin.Home!.Occupants++;
        }
    }

    /// <summary>
    /// Grouping for survival: when a struggling loner (hungry, hurt, or
    /// homeless) meets a member of a group that has a finished home, it may
    /// ask to join — more readily the more Sociable it is — and the group
    /// takes it in if there's room and its Leader is welcoming enough (any
    /// Leader takes in newcomers while the group is small).
    /// </summary>
    private bool TryJoinSettledGroup(Bramblekin a, Bramblekin b)
    {
        (Bramblekin? loner, Bramblekin? member) =
            a.GroupId is null && b.GroupId is not null ? (a, b)
            : b.GroupId is null && a.GroupId is not null ? (b, a)
            : (null, null);
        if (loner is null || member is null || GroupOf(member) is not { Home.IsBuilt: true } group)
            return false;

        bool struggling = loner.IsHungry || loner.Health < Bramblekin.MaxHealth * 0.6f || loner.Home is not { IsBuilt: true };
        if (!struggling || group.Members.Count >= MaxGroupSize || HasEnemyIn(loner, group))
            return false;
        if (Rng.NextDouble() >= 0.3 + 0.5 * loner.Personality.Sociability)
            return false;
        if (group.Members.Count >= 3 && group.Leader is { } leader && Rng.NextDouble() >= 0.3 + 0.7 * leader.Personality.Sociability)
            return false;

        loner.JoinGroup(group.Id);
        group.Members.Add(loner);
        SetMutualRelationship(loner, member, RelationshipState.Friend);
        AlliancesFormed++;
        QueueFloatingText(loner.Position, "+Joined", group.Color);
        Game.AddEventLog($"[JOIN] #{loner.ID} asked to join group {group.ShortId} for its home, and was taken in ({group.Members.Count} strong)");
        return true;
    }
}
