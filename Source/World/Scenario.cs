namespace GardenGuardians;

/// <summary>How a new garden begins (chosen on the start menu).</summary>
public enum Scenario
{
    Normal,
    Drought,
    Twins,
    LoneSurvivor,
}

public sealed partial class World
{
    /// <summary>How many Bramblekin a scenario starts with, or null for the usual garden's number (which grows with the map).</summary>
    public static int? ScenarioKinCount(Scenario scenario) => scenario switch
    {
        Scenario.Twins => 2,
        Scenario.LoneSurvivor => 1,
        _ => null,
    };

    public static string ScenarioName(Scenario scenario) => scenario switch
    {
        Scenario.Drought => "Drought year",
        Scenario.Twins => "Twins",
        Scenario.LoneSurvivor => "Lone survivor",
        _ => "Normal",
    };

    /// <summary>Sets up a fresh garden's scenario (its Bramblekin count was settled when it was made — see <see cref="ScenarioKinCount"/>).</summary>
    public void ApplyScenario(Scenario scenario)
    {
        switch (scenario)
        {
            case Scenario.Drought:
                StartInDrought();
                break;
            case Scenario.Twins when Colony.Count >= 2:
                // Two of one family, who set out together.
                Colony[1].Christen(Colony[1].GivenName, Colony[0].FamilyName);
                break;
        }
    }
}
