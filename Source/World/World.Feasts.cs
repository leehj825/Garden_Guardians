using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>A harvest feast under way (see World.Feasts): who's hosting, where, until when, and who's invited.</summary>
public sealed class Feast
{
    public required KinGroup Host { get; init; }
    public required Vector3 Site { get; init; }
    public required float EndsAt { get; init; }

    /// <summary>The clans invited besides the host: its allies, and friendly neighbours.</summary>
    public required HashSet<Guid> Guests { get; init; }

    /// <summary>Everyone who has come so far (by ID).</summary>
    public HashSet<int> Attended { get; } = new();

    /// <summary>Guest clans that actually came.</summary>
    public HashSet<Guid> CameFrom { get; } = new();

    public bool IsInvited(Guid? clan) => clan is { } id && (id == Host.Id || Guests.Contains(id));
}

public sealed partial class World
{
    // --- Festivals: harvest feasts -----------------------------------------------------------

    /// <summary>A feast lasts this long (s).</summary>
    private const float FeastSeconds = 40f;

    /// <summary>A clan needs at least this much stored (and <see cref="FeastFoodPerMember"/> a member) to hold one…</summary>
    private const int FeastStoreThreshold = 12;
    private const int FeastFoodPerMember = 3;

    /// <summary>…and lays out this much to start with, plus a piece for every hungry guest.</summary>
    private const int FeastSpread = 3;

    /// <summary>At each autumn decision, a clan that can afford one holds its feast with odds this × (0.5 + its Leader's Sociability).</summary>
    private const double FeastChance = 0.25;

    /// <summary>Neighbours at peace within this far (m) of the host's home are invited too, if their grievance is below <see cref="FeastInviteGrievance"/>.</summary>
    private const float FeastInviteReach = 40f;
    private const float FeastInviteGrievance = 4f;

    /// <summary>Kin come to a feast from within this far (m).</summary>
    public const float FeastReach = 45f;

    /// <summary>A guest clan that came leaves with this much less grievance against the host (and the host against it).</summary>
    private const float FeastGrudgeRelief = 4f;

    /// <summary>A neutral guest clan that came in numbers (<see cref="FeastAllianceMinGuests"/>+) becomes an ally with these odds.</summary>
    private const double FeastAllianceChance = 0.3;
    private const int FeastAllianceMinGuests = 2;

    private static readonly Color FeastTextColor = new(220, 120, 40, 255);
    private static readonly Color LanternColor = new(255, 200, 110, 255);
    private static readonly Color FeastPoleColor = new(120, 85, 50, 255);

    private readonly List<Feast> _feasts = new();

    public IReadOnlyList<Feast> Feasts => _feasts;

    public int FeastsHeld { get; private set; }
    public int FeastGuests { get; private set; }
    public int FeastCouples { get; private set; }
    public int FeastAlliances { get; private set; }

    /// <summary>
    /// At an autumn decision: a clan with a good harvest in store holds a
    /// feast at its main home and invites its allies and friendly
    /// neighbours — once a year, by day, and not while defending or raiding.
    /// </summary>
    private void TryHoldFeast(KinGroup group, Bramblekin leader)
    {
        if (CurrentSeason != Season.Autumn || IsNight || group.LastFeastYear == Year || group.Home is not { IsBuilt: true } home)
            return;
        if (group.Goal is GroupGoal.Defend or GroupGoal.Raid || _feasts.Any(f => f.Host == group))
            return;
        int stored = StoredFood(group);
        if (stored < Math.Max(FeastStoreThreshold, group.Members.Count * FeastFoodPerMember))
            return;
        if (Rng.NextDouble() >= FeastChance * (0.5 + leader.Personality.Sociability))
            return;

        var guests = new HashSet<Guid>();
        foreach (KinGroup other in _groups.Values)
        {
            if (other == group || other.Home is not { } theirs)
                continue;
            GroupStance stance = StanceBetween(group.Id, other.Id);
            if (stance == GroupStance.Allied ||
                (stance == GroupStance.Neutral && RelationBetween(group.Id, other.Id).Grievance < FeastInviteGrievance &&
                 GroundMover.HorizontalDistance(theirs.Position, home.Position) <= FeastInviteReach))
                guests.Add(other.Id);
        }

        for (int i = 0; i < FeastSpread; i++)
            WithdrawFromStores(group);
        Vector3 site = FeastSite(home);
        _feasts.Add(new Feast { Host = group, Site = site, EndsAt = ElapsedSeconds + FeastSeconds, Guests = guests });
        group.LastFeastYear = Year;
        FeastsHeld++;
        leader.AddReputation(0.1f);
        QueueFloatingText(site, "Feast!", FeastTextColor);
        string invited = guests.Count == 0 ? "" : $", inviting {guests.Count} neighbouring {(guests.Count == 1 ? "clan" : "clans")}";
        Game.AddEventLog($"[FEAST] {leader.Name} of {group.Title} holds a harvest feast{invited}");
        Headline("Harvest feast", $"{group.CapitalTitle} holds a harvest feast{invited}", site, false, group);
    }

    /// <summary>Somewhere clear beside the main home for the tables.</summary>
    private Vector3 FeastSite(Shelter home)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            Vector3 spot = home.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (home.Radius + 3f);
            if (Terrain.Contains(spot, 2f) && !IsBlockedOrAntZone(spot, 1f) && !IsNearHome(spot, 0.5f))
                return Grounded(spot);
        }
        return Grounded(home.Position + new Vector3(home.Radius + 3f, 0f, 0f));
    }

    /// <summary>Takes one piece from <paramref name="group"/>'s stores (whichever home has some). False if they're empty.</summary>
    private bool WithdrawFromStores(KinGroup group)
    {
        foreach (Shelter home in GroupHomes(group))
        {
            if (home.TryWithdraw())
                return true;
        }
        return false;
    }

    /// <summary>The feast <paramref name="kin"/> is invited to and near enough to come to, if any.</summary>
    public Feast? FeastFor(Bramblekin kin)
    {
        if (_feasts.Count == 0 || kin.GroupId is not { } clan)
            return null;
        foreach (Feast feast in _feasts)
        {
            if (feast.IsInvited(clan) && (clan == feast.Host.Id || !kin.IsYoung) &&
                GroundMover.HorizontalDistanceSquared(kin.Position, feast.Site) <= FeastReach * FeastReach)
                return feast;
        }
        return null;
    }

    /// <summary>True if <paramref name="a"/> and <paramref name="b"/> are both at the same feast right now.</summary>
    public bool AtSameFeast(Bramblekin a, Bramblekin b)
    {
        foreach (Feast feast in _feasts)
        {
            if (feast.Attended.Contains(a.ID) && feast.Attended.Contains(b.ID))
                return true;
        }
        return false;
    }

    /// <summary><paramref name="guest"/> arrives at the feast: counted, and fed from the host's stores if it's peckish. Returns true if it was served.</summary>
    public bool ArriveAtFeast(Bramblekin guest, Feast feast)
    {
        if (!feast.Attended.Add(guest.ID))
            return false;
        if (guest.GroupId is { } clan && clan != feast.Host.Id)
        {
            FeastGuests++;
            feast.CameFrom.Add(clan);
        }
        return WithdrawFromStores(feast.Host);
    }

    /// <summary>Feasts come to an end: grudges cool between the host and every clan that came, and a neutral clan that came in numbers may become an ally.</summary>
    private void UpdateFeasts()
    {
        for (int i = _feasts.Count - 1; i >= 0; i--)
        {
            Feast feast = _feasts[i];
            if (ElapsedSeconds < feast.EndsAt && _groups.ContainsKey(feast.Host.Id))
                continue;
            _feasts.RemoveAt(i);
            if (!_groups.ContainsKey(feast.Host.Id))
                continue;
            foreach (Guid clan in feast.CameFrom)
            {
                if (!_groups.TryGetValue(clan, out KinGroup? guests))
                    continue;
                GroupRelation relation = RelationBetween(feast.Host.Id, clan);
                relation.Grievance = MathF.Max(0f, relation.Grievance - FeastGrudgeRelief);
                MaybeConvertAtFeast(feast.Host, guests);
                int came = 0;
                foreach (Bramblekin member in guests.Members)
                {
                    if (feast.Attended.Contains(member.ID))
                        came++;
                }
                if (relation.Stance == GroupStance.Neutral && came >= FeastAllianceMinGuests && Rng.NextDouble() < FeastAllianceChance)
                {
                    SetStance(feast.Host, guests, GroupStance.Allied);
                    FeastAlliances++;
                    Game.AddEventLog($"[ALLY] {feast.Host.CapitalTitle} and {guests.Title} became allies over the feast");
                    Chronicle($"{feast.Host.CapitalTitle} and {guests.Title} became allies at a harvest feast", feast.Host, guests);
                }
            }
            int guestsCame = feast.Attended.Count(id => feast.Host.Members.All(m => m.ID != id));
            if (guestsCame > 0)
                Chronicle($"{feast.Host.CapitalTitle} feasted {guestsCame} guests from {feast.CameFrom.Count} {(feast.CameFrom.Count == 1 ? "clan" : "clans")}", feast.Host);
        }
    }

    /// <summary>A couple met across clans at a feast.</summary>
    private void NoteFeastCourtship() => FeastCouples++;

    /// <summary>Bunting hung between lanterns on sticks round the tables in the host's colours, and the spread in the middle.</summary>
    private void DrawFeasts(Camera3D camera)
    {
        foreach (Feast feast in _feasts)
        {
            if (!IsVisible(feast.Site, camera))
                continue;
            const int poles = 8;
            const float ring = 3.2f;
            Vector3 previous = default;
            for (int i = 0; i <= poles; i++)
            {
                float angle = i * MathF.Tau / poles;
                Vector3 foot = Grounded(feast.Site + new Vector3(MathF.Cos(angle) * ring, 0f, MathF.Sin(angle) * ring));
                Vector3 top = foot + new Vector3(0f, 0.9f, 0f);
                if (i < poles)
                {
                    FittingModels.Draw(FittingModels.Kind.Lantern, foot, 0f, 1f, Color.White);
                }
                if (i > 0)
                {
                    Raylib.DrawLine3D(previous, top, feast.Host.Color);
                    Vector3 mid = Vector3.Lerp(previous, top, 0.5f) - new Vector3(0f, 0.12f, 0f);
                    Raylib.DrawTriangle3D(Vector3.Lerp(previous, top, 0.4f), mid, Vector3.Lerp(previous, top, 0.6f), feast.Host.Color);
                    Raylib.DrawTriangle3D(Vector3.Lerp(previous, top, 0.6f), mid, Vector3.Lerp(previous, top, 0.4f), feast.Host.Color);
                }
                previous = top;
            }
            // The spread: a low round table heaped with food.
            Raylib.DrawCylinder(feast.Site, 0.8f, 0.8f, 0.12f, 14, FeastPoleColor);
            for (int i = 0; i < 7; i++)
            {
                float a = i * MathF.Tau / 7f;
                Detail.Sphere(feast.Site + new Vector3(MathF.Cos(a) * 0.45f, 0.2f, MathF.Sin(a) * 0.45f), 0.12f, i % 2 == 0 ? new Color(210, 40, 45, 255) : new Color(225, 190, 95, 255));
            }
            Detail.Sphere(feast.Site + new Vector3(0f, 0.25f, 0f), 0.16f, new Color(235, 170, 45, 255));
        }
    }

    /// <summary>The feast's lanterns, glowing after dark.</summary>
    private void DrawFeastLanterns(float darkness)
    {
        foreach (Feast feast in _feasts)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = i * MathF.Tau / 8;
                Vector3 top = Grounded(feast.Site + new Vector3(MathF.Cos(angle) * 3.2f, 0f, MathF.Sin(angle) * 3.2f)) + new Vector3(0f, 0.9f, 0f);
                Detail.Sphere(top - new Vector3(0f, 0.35f, 0f), 0.22f, LanternColor with { A = (byte)(150 * darkness) });
            }
        }
    }
}
