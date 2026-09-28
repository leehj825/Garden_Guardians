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

    /// <summary>Contagion: when two meet and one is ill, the other may catch it.</summary>
    private void SpreadSickness(Bramblekin a, Bramblekin b)
    {
        if (a.IsSick && b.CanCatchSickness && Rng.NextDouble() < ContagionChance)
        {
            b.FallSick();
            NoteFellSick(b);
        }
        else if (b.IsSick && a.CanCatchSickness && Rng.NextDouble() < ContagionChance)
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

    /// <summary>How many are ill right now.</summary>
    public int SickCount => Colony.Count(k => !k.IsDead && k.IsSick);
}
