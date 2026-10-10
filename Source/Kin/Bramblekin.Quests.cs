using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>A small errand a clanmate asks of the player's explorer: bring <see cref="Amount"/> of <see cref="Item"/> to <see cref="Giver"/>.</summary>
public sealed record Quest(Bramblekin Giver, ItemKind Item, int Amount);

public sealed partial class Bramblekin
{
    /// <summary>Seconds before the first quest is asked, and between one and the next.</summary>
    private const float FirstQuestDelay = 10f, QuestGap = 25f;

    /// <summary>How near (m) the explorer must come to hand things over.</summary>
    private const float QuestHandOverRange = 2f;

    /// <summary>The giver's hunger eased by a handed-over meal.</summary>
    private const float QuestMealRelief = 25f;

    private static readonly Color QuestTextColor = new(255, 225, 120, 255);

    private float _questTimer = FirstQuestDelay, _questCallTimer;

    /// <summary>The explorer's errand now, if any.</summary>
    public Quest? PlayerQuest { get; private set; }

    /// <summary>Quests the explorer has done this time out.</summary>
    public int QuestsDone { get; private set; }

    /// <summary>Explore mode: asks a clanmate's small favour now and then — berries or a water bottle — and takes the thanks when it is brought.</summary>
    private void UpdateQuests(float deltaTime, World world)
    {
        if (PlayerQuest is not { } quest)
        {
            _questTimer -= deltaTime;
            if (_questTimer <= 0f)
                OfferQuest(world);
            return;
        }

        if (quest.Giver.IsDead || quest.Giver.IsPlayerControlled)
        {
            world.QueueFloatingText(Position, "The errand is off", QuestTextColor);
            EndQuest();
            return;
        }

        _questCallTimer -= deltaTime;
        if (_questCallTimer <= 0f)
        {
            _questCallTimer = 3f;
            world.QueueFloatingText(quest.Giver.Position, "!", QuestTextColor);
        }

        if (GroundMover.HorizontalDistance(Position, quest.Giver.Position) > QuestHandOverRange || !Pack.Remove(quest.Item, quest.Amount))
            return;
        if (quest.Item != ItemKind.Water)
            quest.Giver.Hunger = MathF.Max(0f, quest.Giver.Hunger - QuestMealRelief);
        AddReputation(0.5f);
        QuestsDone++;
        world.QueueFloatingText(quest.Giver.Position, $"Thank you, {GivenName}!", QuestTextColor);
        world.QueueFloatingText(Position, $"Quest done ({QuestsDone})", QuestTextColor);
        EndQuest();
    }

    private void EndQuest()
    {
        PlayerQuest = null;
        _questTimer = QuestGap;
    }

    private void OfferQuest(World world)
    {
        KinGroup? clan = AwayGroupId is { } id ? world.GroupWithId(id) : world.GroupOf(this);
        var candidates = clan?.Members.Where(m => !m.IsDead && !m.IsPlayerControlled && m != this).ToList();
        if (candidates is null || candidates.Count == 0)
        {
            _questTimer = QuestGap;
            return;
        }
        // The hungriest asks first for food; otherwise anyone.
        Bramblekin giver = candidates.OrderByDescending(m => m.Hunger + (float)world.Rng.NextDouble() * 40f).First();
        bool water = world.Rng.NextDouble() < 0.3;
        PlayerQuest = new Quest(giver, water ? ItemKind.Water : ItemKind.Berry, world.Rng.Next(2, 5));
        _questCallTimer = 0f;
        world.QueueFloatingText(Position, "A favour is asked", QuestTextColor);
    }

    /// <summary>"Bring 3 berries to Mira" — for the HUD.</summary>
    private string? QuestHint => PlayerQuest is { } quest
        ? $"Bring {quest.Amount} {(quest.Item == ItemKind.Water ? "water bottle" : "berr")}{(quest.Item == ItemKind.Water ? (quest.Amount > 1 ? "s" : "") : (quest.Amount > 1 ? "ies" : "y"))} to {quest.Giver.GivenName}"
        : null;
}
