namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>Bramblekin who became masters, by <see cref="Skill"/>.</summary>
    private readonly int[] _masteries = new int[Enum.GetValues<Skill>().Length];

    /// <summary>Extra pieces a skilled farmer coaxed out of a crop.</summary>
    public int SkilledHarvests { get; private set; }

    public int Masteries(Skill skill) => _masteries[(int)skill];

    /// <summary><paramref name="kin"/> just became a master of <paramref name="skill"/> — a line in its clan's chronicle.</summary>
    public void NoteMastery(Bramblekin kin, Skill skill)
    {
        _masteries[(int)skill]++;
        string noun = Bramblekin.TradeNoun(skill);
        Game.AddEventLog($"[SKILL] {kin.Name} has become a master {noun}");
        Chronicle($"{kin.Name} became a master {noun}", GroupOf(kin));
    }

    /// <summary>A skilled farmer's extra piece from <paramref name="crop"/>, straight into <paramref name="store"/>.</summary>
    public bool DepositBonusHarvest(Shelter store, Crop crop)
    {
        if (!store.TryDeposit())
            return false;
        _foodByKind[(int)crop.Yields]++;
        FruitHarvested++;
        SkilledHarvests++;
        return true;
    }
}
