using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>After a birth, a group waits at least this long before the next one.</summary>
    public const float BirthCooldownSeconds = 60f;

    /// <summary>A group needs at least this much Food stored before it will raise young…</summary>
    public const int BirthStoreThreshold = 4;

    /// <summary>…and a birth uses up this much of it.</summary>
    public const int BirthFoodCost = 3;

    /// <summary>A group won't raise young while more than this fraction of its members are hungry.</summary>
    private const float BirthMaxHungryFraction = 1f / 3f;

    /// <summary>A group only raises young while its stores hold at least this much Food per member…</summary>
    private const int BirthFoodPerMember = 1;

    /// <summary>…or, in autumn, this much — enough to see everyone through the winter.</summary>
    private const int AutumnBirthFoodPerMember = 2;

    /// <summary>At or above this fraction of <see cref="Bramblekin.MaxHealth"/>, a fed adult is fit to be a parent.</summary>
    private const float ParentHealthFraction = 0.7f;

    public int Births { get; private set; }

    /// <summary>The highest generation born so far (0 = only newcomers).</summary>
    public int MaxGeneration { get; private set; }

    /// <summary>
    /// Growth: a thriving group raises young. It takes a couple in the
    /// group (see <see cref="TryCourt"/>) who are both fit to be parents —
    /// grown but not elders, fed and healthy — and live in one of the
    /// group's finished homes, which becomes the nursery. The group also
    /// needs Food stored: at least <see cref="BirthStoreThreshold"/>, and
    /// <see cref="BirthFoodPerMember"/> per member (in autumn
    /// <see cref="AutumnBirthFoodPerMember"/>, for the winter ahead; never
    /// in winter itself). Few of its members may be hungry, it must have
    /// room to grow (see <see cref="BirthLimit"/>), and
    /// <see cref="BirthCooldownSeconds"/> must have passed since its last
    /// birth. The newborn costs <see cref="BirthFoodCost"/> from the stores
    /// (the nursery's first), inherits a mix of its parents' Personalities
    /// and one of their family names, and joins the group at home. Checked
    /// at each Leader decision.
    /// </summary>
    private void TryBirth(KinGroup group)
    {
        if (group.BirthCooldown > 0f || group.Members.Count >= BirthLimit(group))
            return;
        if (CurrentSeason == Season.Winter)
            return;
        int foodPerMember = CurrentSeason == Season.Autumn ? AutumnBirthFoodPerMember : BirthFoodPerMember;
        if (StoredFood(group) < Math.Max(BirthStoreThreshold, group.Members.Count * foodPerMember))
            return;
        if (Colony.Count(k => !k.IsDead) + _pendingKinSpawns.Count >= MaxPopulation)
            return;
        if (group.Members.Count(m => m.IsHungry) > group.Members.Count * BirthMaxHungryFraction)
            return;

        if (RandomFitCouple(group) is not { } couple)
            return;
        var (mother, father, nursery) = couple;

        PayForBirth(group, nursery);

        float angle = (float)(Rng.NextDouble() * MathF.Tau);
        Vector3 spot = nursery.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (nursery.Radius * 0.5f);
        Bramblekin child = Bramblekin.BornTo(mother, father, spot, Rng, group.Culture);
        mother.NoteChildBorn();
        father.NoteChildBorn();
        NoteFavourite(mother, $"had a child, {child.Name}");
        NoteFavourite(father, $"had a child, {child.Name}");
        child.JoinGroup(group.Id);
        child.SetHome(nursery);
        _pendingKinSpawns.Add(child);

        Births++;
        MaxGeneration = Math.Max(MaxGeneration, child.Generation);
        group.BirthCooldown = BirthCooldownSeconds;
        QueueFloatingText(nursery.Position, "Born!", group.Color);
        Spotlight($"A {(child.Sex == Sex.Female ? "daughter" : "son")} is born in {group.Title}", 4f, nursery.Position);
        Game.AddEventLog($"[BIRTH] A {(child.Sex == Sex.Female ? "daughter" : "son")}, {child.Name}, was born to {mother.Name} and {father.Name} in {group.Title} (generation {child.Generation})");
    }

    /// <summary>A random couple in <paramref name="group"/> fit to raise young, and the group home they'll raise it in — null if there's none.</summary>
    private (Bramblekin Mother, Bramblekin Father, Shelter Nursery)? RandomFitCouple(KinGroup group)
    {
        (Bramblekin, Bramblekin, Shelter)? chosen = null;
        int seen = 0;
        foreach (Bramblekin mother in group.Members)
        {
            if (mother.Sex != Sex.Female || mother.Partner is not { } father || father.GroupId != group.Id)
                continue;
            if (!IsFitParent(mother) || !IsFitParent(father))
                continue;
            Shelter? nursery = mother.Home is { IsBuilt: true } a && IsGroupHome(group, a) ? a
                : father.Home is { IsBuilt: true } b && IsGroupHome(group, b) ? b
                : null;
            if (nursery is null)
                continue;
            // Reservoir sampling: each fit couple ends up chosen with equal odds.
            if (Rng.Next(++seen) == 0)
                chosen = (mother, father, nursery);
        }
        return chosen;
    }

    /// <summary>Healthy, fed and grown, but not yet an elder.</summary>
    private static bool IsFitParent(Bramblekin kin) =>
        !kin.IsDead && !kin.IsYoung && !kin.IsElder && !kin.IsHungry && !kin.IsDueling &&
        kin.Health >= Bramblekin.MaxHealth * ParentHealthFraction;

    /// <summary>Takes <see cref="BirthFoodCost"/> from the group's stores, the nursery's first.</summary>
    private void PayForBirth(KinGroup group, Shelter nursery)
    {
        int owed = BirthFoodCost;
        while (owed > 0 && nursery.TryWithdraw())
            owed--;
        foreach (Shelter home in GroupHomes(group))
        {
            while (owed > 0 && home.TryWithdraw())
                owed--;
        }
    }
}
