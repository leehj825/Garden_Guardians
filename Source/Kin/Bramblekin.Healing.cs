using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>A Healer spends this long (s) tending a patient — less when diligent or skilled (see <see cref="WorkPace"/>).</summary>
    private const float TendSeconds = 4f;

    /// <summary>A Healer tends from this close (m).</summary>
    private const float TendReach = 0.9f;

    /// <summary>A Healer looks after groupmates within this far (m) of home.</summary>
    private const float HealerRange = 25f;

    /// <summary>Hurt below this fraction of its Health, it's worth tending.</summary>
    private const float CareHealthFraction = 0.6f;

    private static readonly Color PoulticeColor = new(90, 150, 70, 255);

    private Bramblekin? _patient;
    private float _tendTimer;

    /// <summary>Sick, or hurt enough to be worth a Healer's time.</summary>
    public bool NeedsCare => !IsDead && (IsSick || Health < MaxHealth * CareHealthFraction);

    /// <summary>
    /// Healer: goes to the groupmate near home most in need — the sick
    /// first, then the worst hurt — and tends it with herbs; with nobody to
    /// tend, it gathers like anyone else.
    /// </summary>
    private bool DoHealerDuty(KinGroup group, float deltaTime, World world)
    {
        if (Home is not { IsBuilt: true } home)
            return DoGatherDuty(deltaTime, world);

        if (_patient is not { NeedsCare: true } kept || kept.GroupId != GroupId)
            _patient = FindPatient(group, home.Position);
        if (_patient is not { } patient)
            return DoGatherDuty(deltaTime, world);

        SetState(BramblekinState.Healing);
        if (GroundMover.HorizontalDistance(Position, patient.Position) > TendReach)
        {
            _tendTimer = 0f;
            MoveTo(patient.Position, WalkSpeed, deltaTime, world);
            return true;
        }

        _tendTimer += deltaTime * WorkPace;
        if (_tendTimer < TendSeconds)
            return true;
        _tendTimer = 0f;
        world.Tend(this, patient);
        Train(Skill.Healing, world);
        return true;
    }

    /// <summary>The groupmate near home most in need of tending: the sick before the hurt, the nearer first.</summary>
    private Bramblekin? FindPatient(KinGroup group, Vector3 home)
    {
        Bramblekin? best = null;
        float bestScore = float.MaxValue;
        foreach (Bramblekin member in group.Members)
        {
            if (member == this || !member.NeedsCare ||
                GroundMover.HorizontalDistanceSquared(member.Position, home) > HealerRange * HealerRange)
                continue;
            float score = GroundMover.HorizontalDistance(Position, member.Position) + (member.IsSick ? 0f : 30f);
            if (score < bestScore)
            {
                best = member;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// Tended by a Healer of <paramref name="skill"/>: herbs ease a sickness
    /// (it passes sooner) and close wounds — healing even the sick, who
    /// can't otherwise mend. Returns the Health restored and the seconds of
    /// sickness taken off.
    /// </summary>
    public (int Healed, float Eased) ReceiveCare(float skill)
    {
        float eased = 0f;
        if (IsSick)
        {
            float before = _sickness;
            _sickness = MathF.Max(0.01f, _sickness - (20f + 20f * skill));
            eased = before - _sickness;
        }
        int healed = Math.Min(MaxHealth - Health, 3 + (int)MathF.Round(3f * skill));
        Health += healed;
        return (healed, eased);
    }

    /// <summary>A Healer at work: a green poultice held out.</summary>
    private void DrawPoultice(Vector2 facing)
    {
        var hand = Position + new Vector3(facing.X * 0.25f, BodyHeight * 0.55f, facing.Y * 0.25f);
        VillageModels.Draw(VillageItem.Poultice, hand - new Vector3(0f, 0.08f, 0f), 0f, 0.2f, Color.White);
    }
}
