namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>When the sick meet someone (see ResolveEncounters), the other catches it with these odds.</summary>
    private const double ContagionChance = 0.2;

    /// <summary>This many ill at once in a clan is an outbreak (a headline, at most one a year per clan).</summary>
    private const int OutbreakSize = 3;

    /// <summary>Everyone who has fallen ill (by catching it or not).</summary>
    public int SicknessCases { get; private set; }

    /// <summary>Deaths from sickness.</summary>
    public int DeathsBySickness { get; private set; }

    /// <summary>Clans' last outbreak headlines (when), so each gets at most one a year.</summary>
    private readonly Dictionary<Guid, float> _lastOutbreak = new();

    /// <summary>A clan that knows Medicine keeps its sick apart: contagion from them is this fraction as likely.</summary>
    private const double QuarantineFactor = 0.35;

    /// <summary>When allies trade goods and the seller's clan has sick folk, the sickness rides along with these odds — a third as likely if the buyer knows Medicine.</summary>
    private const double TradeContagionChance = 0.15;

    /// <summary>Times sickness came along a trade road (for the headless report).</summary>
    public int TradeInfections { get; private set; }

    private double ContagionFrom(Bramblekin sick) =>
        GroupOf(sick) is { HasMedicine: true } ? ContagionChance * QuarantineFactor : ContagionChance;

    /// <summary>Sickness rides a caravan: called when <paramref name="seller"/> and <paramref name="buyer"/> have just traded.</summary>
    private void CarryInfectionAlongTrade(KinGroup seller, KinGroup buyer)
    {
        if (!seller.Members.Any(m => !m.IsDead && m.IsSick))
            return;
        if (Rng.NextDouble() >= TradeContagionChance * (buyer.HasMedicine ? 0.33 : 1.0))
            return;
        if (buyer.Members.Where(m => !m.IsDead && m.CanCatchSickness).ToList() is not { Count: > 0 } healthy)
            return;
        Bramblekin victim = healthy[Rng.Next(healthy.Count)];
        victim.FallSick();
        TradeInfections++;
        NoteFellSick(victim);
        Game.AddEventLog($"[SICKNESS] Sickness came to {buyer.Title} along the trade road from {seller.Title}");
    }

    /// <summary>Contagion: when two meet and one is ill, the other may catch it.</summary>
    private void SpreadSickness(Bramblekin a, Bramblekin b)
    {
        if (a.IsSick && b.CanCatchSickness && Rng.NextDouble() < ContagionFrom(a))
        {
            b.FallSick();
            NoteFellSick(b);
        }
        else if (b.IsSick && a.CanCatchSickness && Rng.NextDouble() < ContagionFrom(b))
        {
            a.FallSick();
            NoteFellSick(a);
        }
    }

    /// <summary>Someone fell ill: counted, and — if it makes an outbreak in its clan — a headline.</summary>
    public void NoteFellSick(Bramblekin kin)
    {
        SicknessCases++;
        if (GroupOf(kin) is not { } group)
            return;
        int sick = 0;
        foreach (Bramblekin member in group.Members)
        {
            if (!member.IsDead && member.IsSick)
                sick++;
        }
        if (sick < OutbreakSize || (_lastOutbreak.TryGetValue(group.Id, out float last) && ElapsedSeconds - last < Bramblekin.SecondsPerYear))
            return;
        _lastOutbreak[group.Id] = ElapsedSeconds;
        Game.AddEventLog($"[SICKNESS] A sickness is spreading through {group.Title} ({sick} ill)");
        Headline("Sickness", $"A sickness is spreading through {group.Title}", PlaceOf(group), false, group);
    }

    /// <summary>Times a Healer tended someone.</summary>
    public int Tendings { get; private set; }

    /// <summary>Health restored by Healers.</summary>
    public int HealthTended { get; private set; }

    /// <summary>Seconds of sickness Healers eased away.</summary>
    public float SicknessEased { get; private set; }

    /// <summary>A Healer tends <paramref name="patient"/> (see <see cref="Bramblekin.ReceiveCare"/>).</summary>
    public void Tend(Bramblekin healer, Bramblekin patient)
    {
        var (healed, eased) = patient.ReceiveCare(healer.SkillAt(Skill.Healing));
        Tendings++;
        HealthTended += healed;
        SicknessEased += eased;
        QueueFloatingText(patient.Position, "Tended", new Raylib_cs.Color(90, 150, 70, 255));
    }

    /// <summary>How many are ill right now.</summary>
    public int SickCount => Colony.Count(k => !k.IsDead && k.IsSick);
}
