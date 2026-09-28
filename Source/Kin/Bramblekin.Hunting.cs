using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>A groupmate within this many meters makes a Stag Beetle worth taking on.</summary>
    private const float PackRadius = 8f;

    /// <summary>Only a hungry Bramblekin at least this Aggressive takes on a Stag Beetle alone.</summary>
    private const float SoloBigGameAggression = 0.75f;

    private StagBeetle? _perceivedBeetle;
    private Shelter? _raidTarget;

    /// <summary>The store it's on its way to raid, if any — residents who see it coming defend their home.</summary>
    public Shelter? RaidTarget => State == BramblekinState.Raiding ? _raidTarget : null;

    /// <summary>
    /// Big-game hunting: attacks a visible Stag Beetle if a groupmate is
    /// already on it or close enough to help — or, hungry and bold enough
    /// (Aggression ≥ <see cref="SoloBigGameAggression"/>), alone. Never while
    /// badly hurt.
    /// </summary>
    private bool TryPackHunt(float deltaTime, World world, bool hungry)
    {
        if (IsYoung || _perceivedBeetle is not { IsDead: false } beetle || Health <= MaxHealth * NerveBreaksAt)
            return false;

        bool packNearby = false;
        if (world.GroupOf(this) is { } group)
        {
            foreach (Bramblekin member in group.Members)
            {
                if (member == this || member.IsDead)
                    continue;
                if (ReferenceEquals(member.CombatTarget, beetle) ||
                    GroundMover.HorizontalDistanceSquared(member.Position, Position) <= PackRadius * PackRadius)
                {
                    packNearby = true;
                    break;
                }
            }
        }

        if (!packNearby && !(hungry && Personality.Aggression >= SoloBigGameAggression))
            return false;

        SetState(BramblekinState.Hunting);
        CombatTarget = beetle;
        PursueAndStrike(beetle, WalkSpeed * 1.2f, deltaTime, world);
        return true;
    }

    /// <summary>Hunts a visible Grub (hungry, or to stock a store).</summary>
    private void HuntGrub(Grub grub, float deltaTime, World world)
    {
        SetState(BramblekinState.Hunting);
        CombatTarget = grub;
        PursueAndStrike(grub, WalkSpeed * 1.2f, deltaTime, world);
    }

    /// <summary>
    /// Takes Food from a store that isn't its own: any hungry Bramblekin
    /// scavenges an abandoned one it can see; a starving, highly Aggressive
    /// one raids anyone's — making Enemies of everyone who lives there, and
    /// fair game for them as it approaches.
    /// </summary>
    private bool TryTakeFromStore(float deltaTime, World world, bool raid)
    {
        if (raid && (IsYoung || !IsStarving || Personality.Aggression < World.HighAggressionThreshold))
            return false;

        Shelter? target = _raidTarget is { IsCollapsed: false, StoredFood: > 0 } current && IsFairTarget(current, raid)
            ? current
            : world.NearestForeignStore(this, DetectionRadius, abandonedOnly: !raid);
        if (target is null)
        {
            _raidTarget = null;
            return false;
        }

        _raidTarget = target;
        SetState(BramblekinState.Raiding);
        if (!target.Contains(Position))
        {
            MoveTo(target.Position, WalkSpeed * 1.2f, deltaTime, world);
            return true;
        }

        if (!BreakIn(target, deltaTime))
            return true;
        _raidTarget = null;
        if (world.RaidStore(this, target) is { } food)
        {
            _carried = food;
            StartEating();
        }
        return true;
    }

    private bool IsFairTarget(Shelter shelter, bool raid) =>
        shelter != Home && (raid || shelter.IsAbandoned) &&
        !(GroupId is not null && shelter.GroupId == GroupId);
}
