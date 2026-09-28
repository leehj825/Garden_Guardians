namespace GardenGuardians;

/// <summary>What a Bramblekin gets better at by doing it — see Bramblekin.Skills.</summary>
public enum Skill
{
    Hunting,
    Farming,
    Building,
    Fishing,
}

public sealed partial class Bramblekin
{
    private static readonly int SkillCount = Enum.GetValues<Skill>().Length;

    /// <summary>A skill this high makes it a master (a line in the chronicle).</summary>
    public const float MasterySkill = 0.75f;

    /// <summary>Each act of practice closes this fraction of the gap to perfect (times its learning pace — see <see cref="Practice"/>).</summary>
    private const float PracticeGain = 0.025f;

    /// <summary>Skills rust by this much a second.</summary>
    private const float SkillRustPerSecond = 0.00002f;

    /// <summary>A newborn starts with this fraction of its handier parent's skill in each — a family trade.</summary>
    private const float InheritedSkill = 0.25f;

    private readonly float[] _skills = new float[SkillCount];

    /// <summary>Which skills it has already mastered — so the chronicle hears of each only once.</summary>
    private int _mastered;

    /// <summary>How good it is at <paramref name="skill"/>, 0..1.</summary>
    public float SkillAt(Skill skill) => _skills[(int)skill];

    /// <summary>
    /// Practice makes perfect: each act brings <paramref name="skill"/> a
    /// little closer to 1 — quickly at first, then ever more slowly — and
    /// a sharp mind learns faster. Returns true the moment it becomes a master.
    /// </summary>
    public bool Practice(Skill skill, float acts = 1f)
    {
        ref float level = ref _skills[(int)skill];
        level = MathF.Min(1f, level + PracticeGain * acts * (0.7f + 0.6f * Personality.Intelligence) * (1f - level));
        int bit = 1 << (int)skill;
        if (level < MasterySkill || (_mastered & bit) != 0)
            return false;
        _mastered |= bit;
        return true;
    }

    /// <summary>Practises <paramref name="skill"/>, telling the World if that makes it a master.</summary>
    private void Train(Skill skill, World world, float acts = 1f)
    {
        if (Practice(skill, acts))
            world.NoteMastery(this, skill);
    }

    /// <summary>Skills rust a little without use (practice easily outpaces it).</summary>
    private void RustSkills(float deltaTime)
    {
        for (int i = 0; i < _skills.Length; i++)
            _skills[i] = MathF.Max(0f, _skills[i] - SkillRustPerSecond * deltaTime);
    }

    /// <summary>A newborn picks up a start in its parents' trades.</summary>
    private void InheritSkills(Bramblekin mother, Bramblekin father)
    {
        for (int i = 0; i < _skills.Length; i++)
            _skills[i] = InheritedSkill * MathF.Max(mother._skills[i], father._skills[i]);
    }

    /// <summary>How much faster its skill makes it at the work it's doing right now (see <see cref="WorkPace"/>).</summary>
    private float SkillPace => State switch
    {
        BramblekinState.Building or BramblekinState.Collecting => 1f + 0.3f * SkillAt(Skill.Building),
        BramblekinState.Farming => 1f + 0.3f * SkillAt(Skill.Farming),
        BramblekinState.Fishing => 1f + 0.3f * SkillAt(Skill.Fishing),
        _ => 1f,
    };

    /// <summary>"a master hunter", "a skilled farmer" — its best trade, if any worth a word.</summary>
    public string? DescribeTrade()
    {
        int best = 0;
        for (int i = 1; i < _skills.Length; i++)
        {
            if (_skills[i] > _skills[best])
                best = i;
        }
        float level = _skills[best];
        string? grade = level >= MasterySkill ? "master" : level >= 0.5f ? "skilled" : level >= 0.25f ? "practised" : null;
        return grade is null ? null : $"{grade} {TradeNoun((Skill)best)}";
    }

    public static string TradeNoun(Skill skill) => skill switch
    {
        Skill.Hunting => "hunter",
        Skill.Farming => "farmer",
        Skill.Building => "builder",
        _ => "fisher",
    };

    /// <summary>"hunting 0.52, building 0.21" for the Kin Inspector — every skill it has any of.</summary>
    public string DescribeSkills()
    {
        string list = string.Join(", ", Enum.GetValues<Skill>().Where(s => SkillAt(s) >= 0.05f).Select(s => $"{s.ToString().ToLowerInvariant()} {SkillAt(s):0.00}"));
        return list.Length > 0 ? list : "none yet";
    }

    /// <summary>Save/Load: its skills, and which it has mastered.</summary>
    public float[] SkillsForSave => (float[])_skills.Clone();

    private void RestoreSkills(float[]? skills)
    {
        if (skills is null)
            return;
        for (int i = 0; i < Math.Min(skills.Length, _skills.Length); i++)
        {
            _skills[i] = skills[i];
            if (skills[i] >= MasterySkill)
                _mastered |= 1 << i;
        }
    }
}
