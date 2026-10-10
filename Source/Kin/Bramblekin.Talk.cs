using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>How near (m) the explorer must be to talk to someone.</summary>
    public const float TalkRange = 5f;

    private static readonly Color TalkColor = new(255, 245, 200, 255);

    /// <summary>
    /// A line for what this Bramblekin is doing and feeling, from what the game already tracks: its health, hunger, grief, work, standing and
    /// nature, and its best skill. The most pressing thing comes first.
    /// </summary>
    public string Utterance(World world)
    {
        KinGroup? group = world.GroupOf(this);
        if (IsSick)
            return "I feel awful. I need to rest.";
        if (IsThirsty)
            return "I'm so thirsty. Where is the water?";
        if (IsHungry)
            return "I could eat a whole berry bush.";
        if (Health * 2 < HealthCap)
            return "Ow. I've been hurt.";
        if (IsWidowed || _mourningTimer > 0f)
            return "I miss them so much.";
        string? work = State switch
        {
            BramblekinState.Fishing => "The fish aren't biting yet. They will.",
            BramblekinState.Building => "Every twig counts. We're building something good.",
            BramblekinState.Farming => "The bushes look good this year.",
            BramblekinState.Healing => "Hold still. This will help.",
            BramblekinState.Fighting or BramblekinState.Attacking => "Stay back!",
            BramblekinState.Guarding => "Nothing gets past me.",
            BramblekinState.Hunting => "Something tasty is out here.",
            BramblekinState.Resting => "Just resting. Don't mind me.",
            BramblekinState.Traveling => "I have an errand for the clan.",
            BramblekinState.Collecting => "Gathering what we need.",
            _ => null,
        };
        if (work is not null)
            return work;
        if (group?.Leader == this)
            return $"{group.Title} looks to me. I must not let it down.";
        if (DescribeTrade() is { } trade)
            return $"They call me a {trade}. I've earned it.";
        if (world.IsNight)
            return "It's getting dark. Time to head home.";
        if (IsYoung)
            return "Look at me! I'm growing up!";
        if (IsElder)
            return "I've seen many seasons in this garden.";
        return Personality.Sociability > 0.65f ? "Lovely day to meet friends."
            : Personality.Aggression > 0.7f ? "Don't test me."
            : Personality.Courage < 0.3f ? "I hope the Wolf Spider stays away."
            : "Just another day in the garden.";
    }

    /// <summary>Explore mode: the nearest living Bramblekin within <see cref="TalkRange"/> says its piece, above its head.</summary>
    public void PlayerTalk(World world)
    {
        Bramblekin? nearest = null;
        float best = TalkRange * TalkRange;
        foreach (Bramblekin other in world.Colony)
        {
            if (other.IsDead || other == this)
                continue;
            float distance = GroundMover.HorizontalDistanceSquared(Position, other.Position);
            if (distance < best)
            {
                best = distance;
                nearest = other;
            }
        }
        if (nearest is null)
            world.QueueFloatingText(Position, "No one close enough to talk to", TalkColor, 1.5f);
        else
            world.QueueFloatingText(nearest.Position, $"\"{nearest.Utterance(world)}\"", TalkColor, 1.5f);
    }
}
