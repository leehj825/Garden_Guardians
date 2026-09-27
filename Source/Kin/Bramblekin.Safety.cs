using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>
    /// Safety: responds to the perceived threat, rolling fight-or-flight
    /// once per new threat — Aggression, plus courage from nearby
    /// groupmates, plus a big bonus when defending one, minus fear of the
    /// Wolf Spider. A fighter whose Health drops below
    /// <see cref="FightBreakHealthFraction"/> breaks and flees. Returns false
    /// when there's nothing to fear.
    /// </summary>
    private bool UpdateSafety(float deltaTime, World world)
    {
        ICombatant? threat = _perceivedThreat;
        if (threat is not null)
        {
            float leash = DetectionRadius * ThreatLeashMultiplier;
            if (threat.IsDead || GroundMover.HorizontalDistanceSquared(Position, threat.Position) > leash * leash)
                threat = null;
        }

        if (threat is null)
        {
            _respondingTo = null;
            if (_fleeTimer > 0f)
            {
                // Keep running for a moment after losing sight of it.
                _fleeTimer -= deltaTime;
                SetState(BramblekinState.Fleeing);
                FleeFrom(_lastThreatPosition, deltaTime, world);
                return true;
            }
            return false;
        }

        if (!ReferenceEquals(threat, _respondingTo))
        {
            _respondingTo = threat;
            _fightDecision = RollFightOrFlight(threat, world);
        }
        if (_fightDecision && Health <= MaxHealth * FightBreakHealthFraction)
            _fightDecision = false; // Nerve breaks.

        _lastThreatPosition = threat.Position;
        if (_fightDecision)
        {
            _fleeTimer = 0f;
            SetState(BramblekinState.Fighting);
            CombatTarget = threat;
            PursueAndStrike(threat, WalkSpeed * PursuitSpeedMultiplier, deltaTime, world);
        }
        else
        {
            SetState(BramblekinState.Fleeing);
            _fleeTimer = FleeMinDuration;
            FleeFrom(threat.Position, deltaTime, world);
        }
        return true;
    }

    /// <summary>The Aggression check: true to fight <paramref name="threat"/>, false to flee it.</summary>
    private bool RollFightOrFlight(ICombatant threat, World world)
    {
        if (Health <= MaxHealth * FightBreakHealthFraction)
            return false;

        float chance = Personality.Aggression;
        if (world.GroupOf(this) is { } group)
        {
            foreach (Bramblekin member in group.Members)
            {
                if (member != this && !member.IsDead &&
                    GroundMover.HorizontalDistanceSquared(Position, member.Position) <= AllySupportRadius * AllySupportRadius)
                    chance += AllySupportBonus;
            }
        }
        if (_threatIsAllyDefense)
            chance += GroupDefenseBonus;
        if (threat is WolfSpider)
            chance -= SpiderFearPenalty;

        return _rng.NextDouble() < chance;
    }
}
