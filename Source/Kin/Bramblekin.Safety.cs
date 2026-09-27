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

        // A raider on a war raid pushes through the defenders to the store
        // and back, until it's hurt badly enough to stand down.
        if (threat is Bramblekin && IsOnWarRaid(world) && Health > MaxHealth * DutyStandDownHealthFraction)
            return false;

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
                FleeFrom(_lastThreatPosition, _lastThreatStopsAtHome, deltaTime, world);
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
        _lastThreatStopsAtHome = threat is not Bramblekin;
        if (_fightDecision)
        {
            _fleeTimer = 0f;
            SetState(BramblekinState.Fighting);
            CombatTarget = threat;
            PursueAndStrike(threat, WalkSpeed * PursuitSpeedMultiplier, deltaTime, world);
        }
        else if (IsSheltered && _lastThreatStopsAtHome)
        {
            // Hiding at home: the Wolf Spider can't pounce and Hornets won't
            // follow it in here, so it simply stays put. Walls don't stop
            // another Bramblekin, though — from one of those it runs.
            _fleeTimer = FleeMinDuration;
            SetState(BramblekinState.Resting);
        }
        else
        {
            SetState(BramblekinState.Fleeing);
            _fleeTimer = FleeMinDuration;
            FleeFrom(threat.Position, _lastThreatStopsAtHome, deltaTime, world);
        }
        return true;
    }

    /// <summary>The Aggression check: true to fight <paramref name="threat"/>, false to flee it.</summary>
    private bool RollFightOrFlight(ICombatant threat, World world)
    {
        // The young never fight — they run (home, if it's close).
        if (IsYoung || Health <= MaxHealth * FightBreakHealthFraction)
            return false;

        // Nobody picks a fight with a Hornet nest: an idle swarm is just
        // avoided; only a chasing one gets swatted back.
        if (threat is Hornet { IsChasing: false })
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
