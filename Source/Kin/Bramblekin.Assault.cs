using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    // --- The ant hill: a soldier in a Kingdom's assault, and the eggs (see World.Assault, Garden_Guardians_Design.md "The Ant Hill") --------

    /// <summary>At or below this share of its Health a soldier in the fight weighs running away against fighting on.</summary>
    private const float AssaultNerveHealth = 0.35f;

    /// <summary>A soldier that has been patched up above this share of its Health weighs it again next time it is hurt.</summary>
    private const float AssaultNerveRecovered = 0.6f;

    /// <summary>The braver, the likelier it stays: it fights on with odds of Courage × this (so the very brave never run).</summary>
    private const float AssaultNerveCourageFactor = 1.15f;

    /// <summary>A soldier that ran when the party had not been called back is "shaken": a fifth less Strength for this long (s), and a little Reputation lost.</summary>
    private const float ShakenStrengthFactor = 0.8f, ShakenSeconds = 2f * World.DayLength, ShakenReputation = 0.05f;

    /// <summary>An egg fills Hunger by this much…</summary>
    private const float EggNourishment = 40f;

    /// <summary>…and is a lasting gain: Strength +0.04 and 3% slower hunger with 1% faster walking for the first egg, each later egg adding this much of the one before.</summary>
    private const float EggStrengthFirst = 0.04f, EggHungerCutFirst = 0.03f, EggSpeedFirst = 0.01f, EggFade = 0.85f;

    private static readonly Color EggTextColor = new(250, 235, 190, 255);

    private bool _nerveRolled;
    private float _shakenUntil;

    /// <summary>The Kingdom's assault on the ant hill it is part of, if any.</summary>
    public Assault? AssaultParty
    {
        get => _assaultParty;
        set
        {
            _assaultParty = value;
            if (value is null)
            {
                _climb = ClimbStage.None;
                ClimbT = 0f;
                _carriesEgg = false;
            }
        }
    }

    private Assault? _assaultParty;

    private enum ClimbStage { None, Up, Pick, Down }

    /// <summary>It goes into the cave for the eggs in this long (s), takes one in this long, and comes down in this long.</summary>
    private const float ClimbUpSeconds = 3f, PickSeconds = 1.5f, ClimbDownSeconds = 2.5f;

    private static readonly Color EggColor = new(250, 238, 200, 255);

    private ClimbStage _climb;
    private float _pickTimer;
    private bool _carriesEgg;

    /// <summary>How far up the ant hill's mound it has climbed, 0 (at the foot) to 1 (at the top); it is drawn up the slope while this is above 0.</summary>
    public float ClimbT { get; private set; }

    /// <summary>Eggs from the ant hill it has eaten: each is a lasting gain in Strength and Vigor, less each time.</summary>
    public int EggsEaten { get; set; }

    /// <summary>True while it is shaken from running away from an assault.</summary>
    public bool IsShaken => _shakenUntil > 0f;

    /// <summary>The sum of a fading series: <paramref name="first"/>, then each term <see cref="EggFade"/> times the one before, for each egg eaten.</summary>
    private float EggSeries(float first) => first * (1f - MathF.Pow(EggFade, EggsEaten)) / (1f - EggFade);

    /// <summary>Born strength, plus what eggs have added, less a fifth while shaken. How hard it hits and how well it stands a blow.</summary>
    public float Strength => (Personality.Strength + EggSeries(EggStrengthFirst)) * (IsShaken ? ShakenStrengthFactor : 1f);

    /// <summary>Vigor, its energy: the eggs it has eaten slow its hunger…</summary>
    private float VigorHungerFactor => 1f - EggSeries(EggHungerCutFirst);

    /// <summary>…and quicken its walk.</summary>
    private float VigorSpeedFactor => 1f + EggSeries(EggSpeedFirst);

    /// <summary>A reward from the ant hill: an egg, eaten on the spot.</summary>
    public void EatEgg(World world)
    {
        EggsEaten++;
        _carriesEgg = false;
        Hunger = MathF.Max(0f, Hunger - EggNourishment);
        world.QueueFloatingText(Position, "Egg!", EggTextColor);
        Game.AddEventLog($"[ANTS] {Name} ate an egg from the ant hill (Strength {Strength:0.00}, {EggsEaten} eaten)");
    }

    /// <summary>A testing aid (see <see cref="World.StartTestAssault"/>): made a fit, fed soldier.</summary>
    public void MakeTestSoldier()
    {
        Job = KinJob.Guard;
        Health = MaxHealth;
        Hunger = 0f;
    }

    /// <summary>For the Kin Inspector: Strength (born, plus eggs, less shaken) and Vigor, and the eggs behind them.</summary>
    public string DescribeStrength() =>
        $"Strength {Strength:0.00} (born {Personality.Strength:0.00}){(IsShaken ? " SHAKEN" : "")}   Vigor: " +
        (EggsEaten == 0 ? "none" : $"{EggsEaten} {(EggsEaten == 1 ? "egg" : "eggs")}, hunger -{(1f - VigorHungerFactor) * 100f:0}%, pace +{(VigorSpeedFactor - 1f) * 100f:0.0}%");

    /// <summary>A testing aid: set down at <paramref name="where"/>.</summary>
    public void TestTeleport(Vector3 where) => _mover.Position = World.Grounded(where);

    private void UpdateShaken(World world)
    {
        if (_shakenUntil > 0f && world.ElapsedSeconds >= _shakenUntil)
            _shakenUntil = 0f;
    }

    /// <summary>Walks to <paramref name="point"/> as a band: true (and busy) while it is further than <paramref name="within"/> from it, else waits there on guard.</summary>
    private bool MarchTo(Vector3 point, float within, float deltaTime, World world)
    {
        SetState(BramblekinState.Guarding);
        if (GroundMover.HorizontalDistanceSquared(Position, point) > within * within)
            MoveTo(point, WalkSpeed, deltaTime, world);
        else
            _mover.Idle();
        return true;
    }

    /// <summary>
    /// A soldier in the Kingdom's assault on the ant hill: musters at the capital, marches to the staging point, fights the guards (running
    /// away on its own if it is badly hurt and its nerve is not up to it), goes in for the prize when every guard is down, and carries it
    /// home. Returns false when it is not in an assault, or has just run away.
    /// </summary>
    private bool UpdateAssault(float deltaTime, World world)
    {
        if (AssaultParty is not { } assault)
            return false;
        if (assault.Ended || IsDead || world.Anthill is not { } hill)
        {
            AssaultParty = null;
            return false;
        }
        if (assault.Fled.Contains(ID))
        {
            AssaultParty = null;
            return false;
        }

        switch (assault.Phase)
        {
            case AssaultPhase.Mustering:
                return MarchTo(assault.Muster, 6f, deltaTime, world);

            case AssaultPhase.Marching:
                return MarchTo(assault.Staging, 6f, deltaTime, world);

            case AssaultPhase.Fighting:
                if (Health > MaxHealth * AssaultNerveRecovered)
                    _nerveRolled = false;
                if (!_nerveRolled && Health <= MaxHealth * AssaultNerveHealth)
                {
                    _nerveRolled = true;
                    if (_rng.NextDouble() >= MathF.Min(1f, Personality.Courage * AssaultNerveCourageFactor))
                    {
                        RunFromAssault(assault, hill, world);
                        return false;
                    }
                }
                if (world.NearestHillGuard(Position, 80f) is { } guard)
                {
                    SetState(BramblekinState.Fighting);
                    CombatTarget = guard;
                    PursueAndStrike(guard, WalkSpeed * PursuitSpeedMultiplier, deltaTime, world);
                    return true;
                }
                return MarchTo(hill.Mouth, 6f, deltaTime, world); // The guards are inside: on into the zone, where they come out to meet it.

            case AssaultPhase.Looting:
                return Loot(assault, hill, deltaTime, world);

            case AssaultPhase.Returning:
                MarchTo(assault.Muster, 5f, deltaTime, world);
                if (assault.Carriers.Contains(ID) && GroundMover.HorizontalDistanceSquared(Position, assault.Muster) <= 8f * 8f)
                    world.AssaultDelivered(assault);
                return true;

            default: // Retreating: home, no blame.
                return MarchTo(assault.Muster, 5f, deltaTime, world);
        }
    }

    /// <summary>
    /// The prize: walks to the foot of the mound, climbs its garden side to the crater at the top, takes an egg and brings it down.
    /// (It stands at the mouth all the while; it is drawn up the slope by <see cref="ClimbT"/>.)
    /// </summary>
    private bool Loot(Assault assault, Anthill hill, float deltaTime, World world)
    {
        if (assault.Carriers.Contains(ID))
            return MarchTo(hill.Mouth, 6f, deltaTime, world); // Has its egg: waits for the others.
        if (_climb == ClimbStage.None)
        {
            ClimbT = 0f;
            MarchTo(hill.Mouth, 1.5f, deltaTime, world);
            if (GroundMover.HorizontalDistanceSquared(Position, hill.Mouth) <= 2f * 2f)
                _climb = ClimbStage.Up;
            return true;
        }
        _mover.Idle();
        Vector3 inward = -hill.Facing;
        switch (_climb)
        {
            case ClimbStage.Up:
                SetState(BramblekinState.Guarding);
                _mover.Heading = new Vector2(inward.X, inward.Z);
                ClimbT = MathF.Min(1f, ClimbT + deltaTime / ClimbUpSeconds);
                if (ClimbT >= 1f)
                {
                    _climb = ClimbStage.Pick;
                    _pickTimer = PickSeconds;
                }
                break;
            case ClimbStage.Pick:
                SetState(BramblekinState.Collecting);
                _pickTimer -= deltaTime;
                if (_pickTimer <= 0f)
                {
                    _carriesEgg = true;
                    _climb = ClimbStage.Down;
                }
                break;
            default:
                SetState(BramblekinState.Guarding);
                _mover.Heading = new Vector2(-inward.X, -inward.Z);
                ClimbT = MathF.Max(0f, ClimbT - deltaTime / ClimbDownSeconds);
                if (ClimbT <= 0f)
                {
                    _climb = ClimbStage.None;
                    assault.Carriers.Add(ID);
                }
                break;
        }
        return true;
    }

    /// <summary>It runs on its own: out of the fight and away from the hill, shaken, and the Kingdom remembers.</summary>
    private void RunFromAssault(Assault assault, Anthill hill, World world)
    {
        world.NoteDeserter(assault, this);
        AssaultParty = null;
        _shakenUntil = world.ElapsedSeconds + ShakenSeconds;
        if (Reputation > 0f)
            AddReputation(-ShakenReputation);
        _fleeTimer = FleeMinDuration * 3f;
        _lastThreatPosition = hill.Position;
        _lastThreatStopsAtHome = false;
        _perceivedThreat = null;
        _respondingTo = null;
        Game.AddEventLog($"[ANTS] {Name} ran from the ant hill");
    }
}
