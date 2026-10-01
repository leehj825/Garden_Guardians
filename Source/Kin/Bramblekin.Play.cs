using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    // --- Under the player's control (see PlayControl): the kin walks where the stick says and strikes when told ----------------------

    /// <summary>A kin under control jogs a little faster than it walks.</summary>
    private const float PlayerRunSpeed = WalkSpeed * 1.35f;

    /// <summary>A strike reaches this far (m) past the edges of the two bodies, and takes this long (s) to recover from; its swing lasts this long (s).</summary>
    private const float PlayerReach = 1.4f, PlayerStrikeCooldown = 0.7f, PlayerSwingSeconds = 0.5f;

    /// <summary>It stands still this near (m) to food, its store or the water, hungry or thirsty, and eats or drinks on its own.</summary>
    private const float PlayerFoodReach = 2f, PlayerStoreReach = 3.5f, PlayerWaterReach = 2.5f;

    private float _swing;

    /// <summary>True while the player is steering it (see <see cref="PlayControl"/>).</summary>
    public bool IsPlayerControlled { get; private set; }

    /// <summary>The way it faces.</summary>
    public Vector2 Facing => _mover.Heading;

    /// <summary>Where the stick says to go, in world X/Z, length 0 to 1.</summary>
    public Vector2 PlayerMove { get; set; }

    /// <summary>Set while the attack button is held.</summary>
    public bool PlayerWantsStrike { get; set; }

    /// <summary>Whether a strike may hit Bramblekin of other clans too (it always hits wildlife).</summary>
    public bool PlayerMayHitKin { get; set; }

    /// <summary>Takes the wheel, or gives it back to the kin's own mind.</summary>
    public void SetPlayerControlled(bool on)
    {
        IsPlayerControlled = on;
        PlayerMove = Vector2.Zero;
        PlayerWantsStrike = false;
        _swing = 0f;
        CombatTarget = null;
        _fleeTimer = 0f;
        _robTarget = null;
        SetState(BramblekinState.Idle);
    }

    /// <summary>One step under control: a swing, a meal or a drink under way, the strike button, eating or drinking where it stands, else the stick.</summary>
    private void UpdatePlayerControl(float deltaTime, World world)
    {
        _perceptionTimer -= deltaTime;
        if (_perceptionTimer <= 0f)
        {
            _perceptionTimer += PerceptionInterval;
            Perceive(world); // (Only for the food it sees; nothing here reacts to a threat.)
        }

        if (_swing > 0f)
        {
            _swing -= deltaTime;
            SetState(BramblekinState.Fighting);
            return;
        }
        if (State == BramblekinState.Eating)
        {
            UpdateHunger(deltaTime, world);
            return;
        }
        if (State == BramblekinState.Drinking && _drinkTimer > 0f)
        {
            UpdateThirst(deltaTime, world);
            return;
        }

        if (PlayerWantsStrike && _strikeCooldown <= 0f)
        {
            PlayerStrike(world);
            return;
        }

        bool moving = PlayerMove.LengthSquared() > 0.01f;
        if (!moving && TryPlayerSustain(deltaTime, world))
            return;

        if (moving)
        {
            SetState(BramblekinState.Idle);
            float speed = PlayerRunSpeed * AgeSpeedFactor * VigorSpeedFactor * (IsSick ? SickSpeedFactor : 1f) * world.PathSpeed(Position);
            _mover.Step(PlayerMove, speed, deltaTime, world);
        }
        else
        {
            SetState(BramblekinState.Idle);
            _mover.Idle();
        }
    }

    /// <summary>Standing still, hungry or thirsty, with food, its store or the water at hand: it eats or drinks. True while it does.</summary>
    private bool TryPlayerSustain(float deltaTime, World world)
    {
        if (IsThirsty && World.NearestShoreSpot(Position, PlayerWaterReach, creek: true) is { } shore)
        {
            if (State != BramblekinState.Drinking || _waterSpot is null)
            {
                SetState(BramblekinState.Drinking);
                _waterSpot = shore;
                _drinkFrom = null;
                _drinkWell = null;
                _drinkGeneration = WaterMap.Generation;
                _drinkTimer = 0f;
            }
            UpdateThirst(deltaTime, world);
            return true;
        }
        if (!IsHungry)
            return false;
        if (_carried is not null)
        {
            StartEating();
            return true;
        }
        if (world.NearestAvailableFood(Position, PlayerFoodReach, this) is { } food)
        {
            ApproachFood(food, WalkSpeed, deltaTime, world, eatOnArrival: true);
            return true;
        }
        if (StoreToEatFrom(world) is { } store && GroundMover.HorizontalDistanceSquared(Position, store.Position) <= PlayerStoreReach * PlayerStoreReach)
        {
            GoHomeAndEat(store, deltaTime, world);
            return true;
        }
        return false;
    }

    /// <summary>A swing at whatever is nearest in front: wildlife, an ant, an enemy — and, if the player allows it, Bramblekin of other clans.</summary>
    private void PlayerStrike(World world)
    {
        _strikeCooldown = PlayerStrikeCooldown;
        _swing = PlayerSwingSeconds;
        SetState(BramblekinState.Fighting);

        Vector2 heading = _mover.Heading.LengthSquared() > 1e-6f ? Vector2.Normalize(_mover.Heading) : Vector2.UnitX;
        ICombatant? best = null;
        float bestDistance = float.MaxValue;

        void Consider(ICombatant? candidate)
        {
            if (candidate is null || candidate.IsDead || ReferenceEquals(candidate, this))
                return;
            float distance = GroundMover.HorizontalDistance(Position, candidate.Position);
            if (distance > BodyRadius + candidate.CollisionRadius + PlayerReach || distance >= bestDistance)
                return;
            if (distance > 0.7f)
            {
                var toward = new Vector2(candidate.Position.X - Position.X, candidate.Position.Z - Position.Z) / distance;
                if (Vector2.Dot(heading, toward) < 0.25f)
                    return; // Behind it.
            }
            best = candidate;
            bestDistance = distance;
        }

        Consider(world.Spider);
        foreach (Hornet hornet in world.Hornets)
            Consider(hornet);
        foreach (InvaderSpider invader in world.Invaders)
            Consider(invader);
        foreach (StagBeetle beetle in world.Beetles)
            Consider(beetle);
        foreach (Grub grub in world.Grubs)
            Consider(grub);
        foreach (Ant ant in world.Ants)
            Consider(ant);
        foreach (HillGuard guard in world.HillGuards)
        {
            if (!guard.IsHidden)
                Consider(guard);
        }
        if (PlayerMayHitKin)
        {
            foreach (Bramblekin other in world.QueryColonyWithin(Position, PlayerReach + 2f))
            {
                if (other != this && !other.IsDead && !other.IsSheltered && (GroupId is null || other.GroupId != GroupId))
                    Consider(other);
            }
        }

        if (best is null)
            return;
        var face = new Vector2(best.Position.X - Position.X, best.Position.Z - Position.Z);
        if (face.LengthSquared() > 1e-6f)
            _mover.Heading = Vector2.Normalize(face);
        if (best is not Bramblekin)
            Train(Skill.Hunting, world, best is StagBeetle or WolfSpider ? 2f : 1f);
        best.TakeHit(best is Bramblekin ? StrikeDamage : HuntingDamage, this, world);
    }

    /// <summary>For the HUD: what a controlled kin can do about being hungry or thirsty.</summary>
    public string? PlayerHint =>
        IsThirsty ? "Thirsty: stand still at the water's edge to drink"
        : IsHungry ? "Hungry: stand still by food or at your store to eat"
        : null;
}
