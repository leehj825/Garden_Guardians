using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>A follower takes orders only while its Loyalty is at least this.</summary>
    public const float ObedienceThreshold = 0.3f;

    /// <summary>Below this, a follower may rebel — see <see cref="World.CheckRebellions"/>.</summary>
    public const float RebelThreshold = 0.2f;

    /// <summary>Reputation never climbs past this.</summary>
    public const float MaxReputation = 3f;

    /// <summary>How much each point of Reputation adds to a Bramblekin's claim to lead.</summary>
    private const float ReputationLeadershipWeight = 0.15f;

    /// <summary>A duellist yields once Health falls to this fraction — challenges are settled, not fought to the death.</summary>
    private const float DuelYieldFraction = 0.5f;

    /// <summary>A duel neither side has yielded after this long is called off.</summary>
    private const float DuelTimeout = 20f;

    /// <summary>Every group it has walked out of, split from, or been thrown out of — it never goes back.</summary>
    private readonly HashSet<Guid> _formerGroups = new();

    private bool _ateFromStoreSinceTick;
    private bool _defendedSinceTick;
    private Bramblekin? _duelOpponent;
    private float _duelTimer;

    /// <summary>A follower's loyalty to its group and Leader, 0..1 — see <see cref="UpdateLoyalty"/>. Meaningless outside a group.</summary>
    public float Loyalty { get; private set; } = 1f;

    /// <summary>Standing earned by deeds (big game, a finished home, won duels) — part of <see cref="LeadershipScore"/>.</summary>
    public float Reputation { get; private set; }

    /// <summary>The loyalty it settles back toward when nothing in particular is happening — higher the more Sociable it is.</summary>
    private float LoyaltyBaseline => 0.45f + 0.3f * Personality.Sociability;

    /// <summary>Its claim to lead a group: Intelligence, plus a little for each point of <see cref="Reputation"/>.</summary>
    public float LeadershipScore => Personality.Intelligence + ReputationLeadershipWeight * Reputation;

    /// <summary>The groupmate it's fighting a leadership duel with, if any.</summary>
    public Bramblekin? DuelOpponent => _duelOpponent;

    public bool IsDueling => _duelOpponent is not null;

    /// <summary>
    /// Takes orders only while this is true: a Leader, a loner, and any
    /// follower loyal enough (<see cref="ObedienceThreshold"/>).
    /// </summary>
    private bool IsObedient => GroupId is null || Loyalty >= ObedienceThreshold;

    public void AddReputation(float amount) => Reputation = Math.Min(MaxReputation, Reputation + amount);

    public void SetLoyalty(float loyalty) => Loyalty = Math.Clamp(loyalty, 0f, 1f);

    /// <summary>A groupmate just fought off something that was attacking it — see <see cref="World.NoteDefended"/>.</summary>
    public void NoteDefended() => _defendedSinceTick = true;

    /// <summary>
    /// A follower's loyalty, re-weighed at every Leader decision. Left alone
    /// it settles back toward its natural level (<see cref="LoyaltyBaseline"/>
    /// — Sociable Bramblekin are more content in a group), so neither
    /// devotion nor a grudge lasts forever. Meals from the store, a home with
    /// food in it, having been defended and friendship with the Leader win
    /// it; hunger (worse when starving), being turned away from the store, a
    /// Leader who eats first, dangerous orders (felt less by the Aggressive)
    /// and injury lose it. Since a badly run group's members share most of
    /// those grievances, they tend to grow unhappy together.
    /// </summary>
    public void UpdateLoyalty(KinGroup group)
    {
        if (group.Leader == this)
        {
            Loyalty = 1f;
            ResetLoyaltyFlags();
            return;
        }

        float delta = (LoyaltyBaseline - Loyalty) * 0.05f;
        if (_ateFromStoreSinceTick)
            delta += 0.04f;
        if (_defendedSinceTick)
            delta += 0.03f;
        if (group.Home is { IsBuilt: true, StoredFood: > 0 })
            delta += 0.01f;
        if (group.Leader is { } leader && RelationshipTo(leader) == RelationshipState.Friend)
            delta += 0.015f;

        if (_deniedFood)
            delta -= 0.1f;
        if (IsStarving)
            delta -= 0.05f;
        else if (IsHungry)
            delta -= 0.02f;
        if (Health < MaxHealth / 2)
            delta -= 0.02f;
        if (group.Sharing == SharingRule.LeaderFirst)
            delta -= 0.04f * (1f - 0.5f * Personality.Aggression);
        if ((Job == KinJob.Hunter && group.Goal == GroupGoal.Hunt) || (Job == KinJob.Guard && group.Goal == GroupGoal.Defend))
            delta -= 0.03f * (1f - Personality.Aggression);

        SetLoyalty(Loyalty + delta);
        ResetLoyaltyFlags();
    }

    private void ResetLoyaltyFlags()
    {
        _ateFromStoreSinceTick = false;
        _deniedFood = false;
        _defendedSinceTick = false;
    }

    /// <summary>Marks a meal taken from the group store, for <see cref="UpdateLoyalty"/>.</summary>
    public void NoteAteFromStore() => _ateFromStoreSinceTick = true;

    /// <summary>Leaves its group for good (by choice or exile): loses the group's home and store, and is Independent from now on.</summary>
    public void Desert()
    {
        if (GroupId is { } former)
            _formerGroups.Add(former);
        LeaveGroup();
        Home = null;
        HasLeftGroup = true;
        Loyalty = 1f;
    }

    /// <summary>True if it once left (or was thrown out of) group <paramref name="groupId"/>.</summary>
    public bool HasLeft(Guid groupId) => _formerGroups.Contains(groupId);

    /// <summary>Starts a leadership duel with <paramref name="opponent"/> (both sides call this).</summary>
    public void BeginDuel(Bramblekin opponent)
    {
        _duelOpponent = opponent;
        _duelTimer = DuelTimeout;
        _robTarget = null;
        _raidTarget = null;
    }

    /// <summary>Ends the duel on this side, forgetting the blows exchanged so they don't linger as a threat.</summary>
    public void EndDuel()
    {
        if (_lastAttacker == _duelOpponent)
            _lastAttacker = null;
        if (_perceivedThreat == _duelOpponent)
            _perceivedThreat = null;
        _duelOpponent = null;
        if (State == BramblekinState.Dueling)
            StartPause();
    }

    /// <summary>
    /// A leadership duel, fought ahead of every other need until someone
    /// yields (Health at <see cref="DuelYieldFraction"/>), someone dies, or it
    /// times out — see <see cref="World.ResolveDuel"/>.
    /// </summary>
    private bool UpdateDuel(float deltaTime, World world)
    {
        if (_duelOpponent is not { } opponent)
            return false;

        _duelTimer -= deltaTime;
        if (opponent.IsDead || opponent._duelOpponent != this || _duelTimer <= 0f)
        {
            world.ResolveDuel(this, opponent, timedOut: true);
            return false;
        }

        if (Health <= MaxHealth * DuelYieldFraction)
        {
            world.ResolveDuel(winner: opponent, loser: this, timedOut: false);
            return false;
        }

        SetState(BramblekinState.Dueling);
        CombatTarget = opponent;
        PursueAndStrike(opponent, WalkSpeed * PursuitSpeedMultiplier, deltaTime, world);
        return true;
    }
}
