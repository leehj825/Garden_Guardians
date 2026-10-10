using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>Things in the garden worth finding for oneself in Explore mode (see <see cref="World.Discover"/>).</summary>
[Flags]
public enum Discovery
{
    None = 0,
    AntHill = 1,
    Shrine = 2,
    Village = 4,
    WolfSpider = 8,
}

public sealed partial class World
{
    /// <summary>How near (m) the explorer must come to count as having found something.</summary>
    private const float DiscoveryRange = 9f;

    /// <summary>Which <see cref="Discovery"/> kinds the player has found (saved with the garden).</summary>
    public int DiscoveredFlags { get; private set; }

    public bool HasDiscovered(Discovery what) => (DiscoveredFlags & (int)what) != 0;

    /// <summary>Explore mode: <paramref name="explorer"/> looks about — anything new within <see cref="DiscoveryRange"/> is found, once, for the whole garden.</summary>
    public void LookForDiscoveries(Bramblekin explorer)
    {
        Vector3 at = explorer.Position;
        bool Near(Vector3 spot) => GroundMover.HorizontalDistanceSquared(at, spot) <= DiscoveryRange * DiscoveryRange;

        if (!HasDiscovered(Discovery.AntHill) && Anthill is { } hill && Near(hill.Position))
            Discover(Discovery.AntHill, explorer, "the ant hill");
        if (!HasDiscovered(Discovery.Shrine) && Groups.FirstOrDefault(g => g.Shrine is { } shrine && Near(shrine)) is { } shrineClan)
            Discover(Discovery.Shrine, explorer, $"the shrine of {shrineClan.Title}");
        if (!HasDiscovered(Discovery.Village) && Villages.FirstOrDefault(v => Near(v.Centre)) is { } village)
            Discover(Discovery.Village, explorer, $"the village of {village.Name}");
        if (!HasDiscovered(Discovery.WolfSpider) && Spider is { IsDead: false } spider && Near(spider.Position))
            Discover(Discovery.WolfSpider, explorer, "the Wolf Spider");
    }

    private void Discover(Discovery what, Bramblekin explorer, string text)
    {
        DiscoveredFlags |= (int)what;
        QueueFloatingText(explorer.Position, "Discovered!", Color.Gold, 1.5f);
        Game.AddEventLog($"[FOUND] {explorer.Name} found {text}");
        Chronicle($"{explorer.Name} found {text}");
    }

    /// <summary>"ant hill, shrine (2 of 4)" — for the History screen.</summary>
    public string DescribeDiscoveries()
    {
        var names = new List<string>();
        foreach (Discovery what in Enum.GetValues<Discovery>())
        {
            if (what != Discovery.None && HasDiscovered(what))
                names.Add(what switch { Discovery.AntHill => "ant hill", Discovery.Shrine => "a shrine", Discovery.Village => "a village", _ => "Wolf Spider" });
        }
        int all = Enum.GetValues<Discovery>().Length - 1;
        return names.Count == 0 ? $"nothing yet (0 of {all}) - walk the garden in Explore" : $"{string.Join(", ", names)} ({names.Count} of {all})";
    }
}
