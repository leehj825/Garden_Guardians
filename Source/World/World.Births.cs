using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>After a birth, a group waits at least this long before the next one.</summary>
    public const float BirthCooldownSeconds = 60f;

    /// <summary>A House needs at least this much Food stored before the group will raise young in it…</summary>
    public const int BirthStoreThreshold = 6;

    /// <summary>…and a birth uses up this much of it.</summary>
    public const int BirthFoodCost = 3;

    /// <summary>A group won't raise young while more than this fraction of its members are hungry.</summary>
    private const float BirthMaxHungryFraction = 1f / 3f;

    /// <summary>In autumn a group only raises young if its stores hold at least this much Food per member — enough to see everyone through the winter.</summary>
    private const int AutumnBirthFoodPerMember = 2;

    /// <summary>At or above this fraction of <see cref="Bramblekin.MaxHealth"/>, a fed adult is fit to be a parent.</summary>
    private const float ParentHealthFraction = 0.7f;

    public int Births { get; private set; }

    /// <summary>The highest generation born so far (0 = only newcomers).</summary>
    public int MaxGeneration { get; private set; }

    /// <summary>
    /// Growth: a thriving group raises young. It needs a House with at
    /// least <see cref="BirthStoreThreshold"/> Food stored, two healthy, fed
    /// adults to be the parents, few hungry members, room to grow (see
    /// <see cref="BirthLimit"/>) and <see cref="BirthCooldownSeconds"/>
    /// since its last birth — and the right time of year: never in winter,
    /// and in autumn only with <see cref="AutumnBirthFoodPerMember"/> Food
    /// stored per member for the winter ahead. The newborn costs <see cref="BirthFoodCost"/>
    /// from the store, inherits a mix of its parents' Personalities, and
    /// joins the group at home. Checked at each Leader decision.
    /// </summary>
    private void TryBirth(KinGroup group)
    {
        if (group.BirthCooldown > 0f || group.Members.Count >= BirthLimit(group))
            return;
        if (CurrentSeason == Season.Winter ||
            (CurrentSeason == Season.Autumn && StoredFood(group) < group.Members.Count * AutumnBirthFoodPerMember))
            return;
        if (Colony.Count(k => !k.IsDead) + _pendingKinSpawns.Count >= MaxPopulation)
            return;
        if (group.Members.Count(m => m.IsHungry) > group.Members.Count * BirthMaxHungryFraction)
            return;

        Shelter? nursery = null;
        foreach (Shelter home in GroupHomes(group))
        {
            if (home is { IsBuilt: true, Tier: ShelterTier.House } && home.StoredFood >= BirthStoreThreshold &&
                (nursery is null || home.StoredFood > nursery.StoredFood))
                nursery = home;
        }
        if (nursery is null)
            return;

        List<Bramblekin> parents = group.Members
            .Where(m => !m.IsDead && !m.IsYoung && !m.IsHungry && !m.IsDueling && m.Health >= Bramblekin.MaxHealth * ParentHealthFraction)
            .OrderBy(_ => Rng.Next())
            .Take(2)
            .ToList();
        if (parents.Count < 2)
            return;

        for (int i = 0; i < BirthFoodCost; i++)
            nursery.TryWithdraw();

        float angle = (float)(Rng.NextDouble() * MathF.Tau);
        Vector3 spot = nursery.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (nursery.Radius * 0.5f);
        Bramblekin child = Bramblekin.BornTo(parents[0], parents[1], spot, Rng);
        child.JoinGroup(group.Id);
        child.SetHome(nursery);
        _pendingKinSpawns.Add(child);

        Births++;
        MaxGeneration = Math.Max(MaxGeneration, child.Generation);
        group.BirthCooldown = BirthCooldownSeconds;
        QueueFloatingText(nursery.Position, "Born!", group.Color);
        Game.AddEventLog($"[BIRTH] #{child.ID} was born to #{parents[0].ID} and #{parents[1].ID} in group {group.ShortId} (generation {child.Generation})");
    }
}
