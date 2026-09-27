namespace GardenGuardians;

/// <summary>
/// A Bramblekin's sex, set at random (even odds) when it arrives or is
/// born. It only matters for births — it takes a mother and a father (see
/// <see cref="World.TryBirth"/>); otherwise the sexes behave the same.
/// </summary>
public enum Sex
{
    Female,
    Male,
}
