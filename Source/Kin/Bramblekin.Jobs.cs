namespace GardenGuardians;

public sealed partial class Bramblekin
{
    // --- What each job means in a fight ----------------------------------------------------------
    //
    // One table for the whole balance of the fighting jobs (see Garden_Guardians_Society_Design.md, "Job balance"). Everything is relative to a
    // plain kin (the None row), who strikes for the Personality-and-Strength damage, takes every blow in full, has 30 Health, strikes once a second
    // and reaches 0.35 m past the bodies:
    //   Fisher     a provider, no fighter.
    //   Hunter     a skirmisher: weak in melee and fragile, but its arrows (and blows at creatures) hit hard from a distance.
    //   Swordsman  the front-liner: the most Health, the best defence, a fast combo, a good all-round blow.
    //   Spearman   the heavy hitter: the longest reach and a heavy blow, slower, and not as sturdy as the Swordsman.
    //   Raider     as before: a soldier's training in a smaller measure.

    /// <summary>A job's fighting profile; see <see cref="ProfileOf"/>.</summary>
    /// <param name="VsKin">Multiplies the damage of a blow against another Bramblekin.</param>
    /// <param name="VsCreature">Multiplies the damage of a blow against a creature (before the spear bonus and hunting skill).</param>
    /// <param name="Arrow">Multiplies the damage of an arrow (a Hunter's bow).</param>
    /// <param name="Taken">Multiplies every blow and bite taken (lower is better).</param>
    /// <param name="Health">The most Health it can have.</param>
    /// <param name="Cooldown">Multiplies the pause between a kin's own blows (lower is faster); a controlled kin's pace comes from its clips.</param>
    /// <param name="Reach">Metres added to a blow's reach.</param>
    private readonly record struct JobProfile(float VsKin, float VsCreature, float Arrow, float Taken, int Health, float Cooldown, float Reach);

    private static JobProfile ProfileOf(KinJob job) => job switch
    {
        KinJob.Fisher => new JobProfile(0.9f, 0.9f, 1f, 1f, 30, 1f, 0f),
        KinJob.Hunter => new JobProfile(0.8f, 1.3f, 1.2f, 1.05f, 28, 1f, 0f),
        KinJob.Swordsman => new JobProfile(1.2f, 1.3f, 1f, 0.85f, 40, 0.8f, 0f),
        KinJob.Spearman => new JobProfile(1.25f, 1.15f, 1f, 0.95f, 34, 1.2f, 0.6f),
        KinJob.Raider => new JobProfile(1.15f, 1.15f, 1f, 1f, 30, 1f, 0f),
        _ => new JobProfile(1f, 1f, 1f, 1f, MaxHealth, 1f, 0f),
    };

    private JobProfile Profile => ProfileOf(Job);

    /// <summary>The most Health this kin can have: its job's (see <see cref="ProfileOf"/>).</summary>
    public int HealthCap => Profile.Health;

    /// <summary>The kin's own damage before its job: Personality, Strength and age (see <see cref="StrikeDamage"/>).</summary>
    private float BaseStrike => (BaseStrikeDamage + StrikeDamagePerAggression * Personality.Aggression + StrikeDamagePerStrength * (Strength - 0.5f)) * (IsElder ? ElderStrikeFactor : 1f);
}
