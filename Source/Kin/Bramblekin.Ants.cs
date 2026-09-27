namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>An ant within this many meters of its home is swatted.</summary>
    private const float AntDefenseRadius = 6f;

    private Ant? _perceivedAnt;

    /// <summary>
    /// Ants (see World.Ants): a grown Bramblekin that isn't starving goes
    /// for any ant it can see near its home — or one close enough to bite
    /// it. Returns true while it's busy with one.
    /// </summary>
    private bool UpdateAntDefense(float deltaTime, World world)
    {
        if (_perceivedAnt is not { IsDead: false } ant || IsYoung || IsStarving)
            return false;
        bool nearHome = Home is { } home &&
                        GroundMover.HorizontalDistanceSquared(ant.Position, home.Position) <= AntDefenseRadius * AntDefenseRadius;
        bool onMe = GroundMover.HorizontalDistanceSquared(ant.Position, Position) <= 1.5f * 1.5f;
        if (!nearHome && !onMe)
            return false;

        SetState(BramblekinState.Fighting);
        CombatTarget = ant;
        PursueAndStrike(ant, WalkSpeed * PursuitSpeedMultiplier, deltaTime, world);
        return true;
    }
}
