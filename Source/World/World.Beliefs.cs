using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Shrines and beliefs ---------------------------------------------------------------

    /// <summary>At each decision, a settled clan with no belief comes to one (from what it has lived through) with odds this × (0.5 + its Leader's Intelligence and Persuasiveness, halved).</summary>
    private const double BeliefChance = 0.006;

    /// <summary>Each decision raises a new shrine this much of the way.</summary>
    private const float ShrineRaisePerDecision = 0.2f;

    /// <summary>A raised shrine lifts every member's loyalty this much each decision (settling it about 0.08 higher).</summary>
    private const float ShrineLoyalty = 0.004f;

    /// <summary>Neighbouring clans that share a belief let grievances fade this much faster (per second)…</summary>
    private const float SharedBeliefFade = 0.004f;

    /// <summary>…and neighbours with rival beliefs build up this much grievance a second.</summary>
    private const float RivalBeliefGrievance = 0.0015f;

    /// <summary>Two Leaders who share a belief are this many times likelier to ally.</summary>
    public const float SharedBeliefAllianceFactor = 2f;

    /// <summary>A guest clan with no belief of its own takes up its host's at a feast with these odds.</summary>
    private const double FeastConversionChance = 0.2;

    /// <summary>A prophet (persuasive, rebellious, not too loyal) in a clan with a shrine has a vision of another belief with these odds per decision…</summary>
    private const double ProphecyChance = 0.0015;
    private const float ProphetPersuasiveness = 0.6f;
    private const float ProphetRebelliousness = 0.5f;

    /// <summary>…and those in the clan restless enough (this Rebellious, or less loyal than <see cref="FollowerLoyalty"/>) may follow it out.</summary>
    private const float FollowerRebelliousness = 0.5f;
    private const float FollowerLoyalty = 0.6f;

    private static readonly Color CairnColor = new(150, 145, 135, 255);
    private static readonly Color CandleGlowColor = new(255, 220, 140, 255);

    public int BeliefsFound { get; private set; }
    public int ShrinesRaised { get; private set; }
    public int Conversions { get; private set; }
    public int Schisms { get; private set; }

    public static string Describe(Belief belief) => belief switch
    {
        Belief.Oak => "the Great Oak",
        Belief.Pond => "the Still Water",
        Belief.Spider => "the Spider",
        Belief.Moon => "the Moon",
        _ => "nothing",
    };

    /// <summary>
    /// At each Leader decision: a settled clan may come to revere something
    /// it has lived close to — the Oak (living under it, or having tasted
    /// its honey), the Pond (living by it), the Spider (having fought it,
    /// or lost kin to danger), the Moon (keeping a hearth through the
    /// night) — and raises a shrine to it by its main home. A raised shrine
    /// binds the clan closer. And now and then a prophet has a vision of
    /// another belief, and leads the unhappy out after it.
    /// </summary>
    private void UpdateBelief(KinGroup group, Bramblekin leader)
    {
        if (group.Belief == Belief.None)
        {
            if (group.Home is not { IsBuilt: true } home)
                return;
            float wisdom = 0.5f + 0.5f * (leader.Personality.Intelligence + leader.Personality.Persuasiveness);
            if (Rng.NextDouble() >= BeliefChance * wisdom)
                return;
            List<Belief> felt = new();
            if (GroundMover.HorizontalDistance(home.Position, OakCenter) < 35f || group.HoneyTaken > 0)
                felt.Add(Belief.Oak);
            if (WaterMap.UsualDistanceToWater(home.Position.X, home.Position.Z) < 12f)
                felt.Add(Belief.Pond);
            if (group.SpidersSlain > 0 || group.Dangers.Count >= 4)
                felt.Add(Belief.Spider);
            if (Knows(group, Craft.Hearth))
                felt.Add(Belief.Moon);
            if (felt.Count == 0)
                return;
            TakeUpBelief(group, felt[Rng.Next(felt.Count)], $"{leader.Name} of {group.Title} came to revere");
            return;
        }

        if (group.ShrineRaised < 1f)
        {
            if (group.Shrine is null && group.Home is { IsBuilt: true } site)
                group.Shrine = ShrineSite(site);
            if (group.Shrine is null)
                return;
            group.ShrineRaised = MathF.Min(1f, group.ShrineRaised + ShrineRaisePerDecision);
            if (group.ShrineRaised >= 1f)
            {
                ShrinesRaised++;
                Game.AddEventLog($"[BELIEF] {group.CapitalTitle} raised a shrine to {Describe(group.Belief)}");
            }
            return;
        }

        foreach (Bramblekin member in group.Members)
        {
            if (!member.IsDead && !member.IsYoung && member != leader)
                member.SetLoyalty(member.Loyalty + ShrineLoyalty);
        }

        TryProphecy(group, leader);
    }

    private void TakeUpBelief(KinGroup group, Belief belief, string who)
    {
        group.Belief = belief;
        group.Shrine = null;
        group.ShrineRaised = 0f;
        BeliefsFound++;
        Game.AddEventLog($"[BELIEF] {who} {Describe(belief)}");
        Headline("A belief", $"{group.CapitalTitle} came to revere {Describe(belief)}, and will raise a shrine to it", PlaceOf(group), false, group);
    }

    /// <summary>Visions had this round, acted on once every clan has decided (a schism changes the clans).</summary>
    private readonly List<(KinGroup Group, Bramblekin Prophet, Belief Vision)> _pendingProphecies = new();

    /// <summary>A prophet's vision: it will lead the unhappy out to follow a new belief (see <see cref="ProcessProphecies"/>).</summary>
    private void TryProphecy(KinGroup group, Bramblekin leader)
    {
        if (Rng.NextDouble() >= ProphecyChance)
            return;
        Bramblekin? prophet = group.Members
            .Where(m => m != leader && !m.IsDead && !m.IsYoung && !m.IsDueling && m.Loyalty < 0.8f &&
                        m.Personality.Persuasiveness >= ProphetPersuasiveness && m.Personality.Rebelliousness >= ProphetRebelliousness)
            .MaxBy(m => m.Personality.Persuasiveness);
        if (prophet is null)
            return;
        Belief[] others = Enum.GetValues<Belief>().Where(b => b != Belief.None && b != group.Belief).ToArray();
        _pendingProphecies.Add((group, prophet, others[Rng.Next(others.Length)]));
    }

    /// <summary>Each prophet leads the unhappy out of its clan (see <see cref="TrySplinter"/>) to raise a shrine to its vision.</summary>
    private void ProcessProphecies()
    {
        foreach (var (group, prophet, vision) in _pendingProphecies)
        {
            if (prophet.IsDead || prophet.GroupId != group.Id || !_groups.ContainsKey(group.Id))
                continue;
            Bramblekin? leader = group.Leader;
            List<Bramblekin> followers = group.Members
                .Where(m => m != leader && !m.IsDead && !m.IsYoung && !m.IsDueling &&
                            (m == prophet || m.Personality.Rebelliousness >= FollowerRebelliousness || m.Loyalty < FollowerLoyalty))
                .Take(5)
                .ToList();
            if (!TrySplinter(group, prophet, followers))
            {
                Game.AddEventLog($"[BELIEF] {prophet.Name} of {group.Title} had a vision of {Describe(vision)}, but nobody followed");
                continue;
            }
            if (GroupOf(prophet) is not { } flock)
                continue;
            flock.Belief = vision;
            flock.Shrine = null;
            flock.ShrineRaised = 0f;
            Schisms++;
            AddGrievance(group.Id, flock.Id, 3f);
            Headline("A schism", $"{Epithet(prophet)}{prophet.Name} had a vision of {Describe(vision)} and led followers out of {group.Title}", prophet.Position, false, group, flock);
        }
        _pendingProphecies.Clear();
    }

    /// <summary>A guest clan with no belief may take up its host's at a feast.</summary>
    private void MaybeConvertAtFeast(KinGroup host, KinGroup guests)
    {
        if (host.Belief == Belief.None || guests.Belief != Belief.None || Rng.NextDouble() >= FeastConversionChance)
            return;
        Conversions++;
        TakeUpBelief(guests, host.Belief, $"At {host.Title}'s feast, {guests.Title} came to revere");
    }

    /// <summary>Neighbours' beliefs work on their grievances: shared ones soothe, rival ones fester.</summary>
    private float BeliefGrievanceDrift(Guid a, Guid b)
    {
        if (!_groups.TryGetValue(a, out KinGroup? ga) || !_groups.TryGetValue(b, out KinGroup? gb))
            return 0f;
        if (ga.Belief == Belief.None || gb.Belief == Belief.None || !AreNeighbours(ga, gb))
            return 0f;
        return ga.Belief == gb.Belief ? -SharedBeliefFade : RivalBeliefGrievance;
    }

    /// <summary>True if both clans revere the same thing.</summary>
    public bool ShareBelief(KinGroup a, KinGroup b) => a.Belief != Belief.None && a.Belief == b.Belief;

    private Vector3? ShrineSite(Shelter home)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            Vector3 spot = home.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (home.Radius + 2.2f);
            if (Terrain.Contains(spot, 1f) && !IsBlocked(spot, 0.6f) && !IsNearHome(spot, 0.3f) &&
                !Pens.Any(p => p.Contains(spot)))
                return Grounded(spot);
        }
        return null;
    }

    /// <summary>
    /// Every clan's shrine: a little cairn of stones (rising as it's raised)
    /// with a ring of pebbles and its token on top — a carved acorn for the
    /// Oak, a blue pebble for the Pond, an eight-legged figure for the
    /// Spider, a pale disc for the Moon.
    /// </summary>
    private void DrawShrines(Camera3D camera)
    {
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Shrine is not { } at || !IsVisible(at, camera))
                continue;
            float raised = group.ShrineRaised;
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathF.Tau / 6f;
                Detail.Sphere(Grounded(at + new Vector3(MathF.Cos(a) * 0.7f, 0f, MathF.Sin(a) * 0.7f), 0.03f), 0.08f, CairnColor);
            }
            int stones = 1 + (int)(raised * 3f);
            float y = 0f;
            for (int i = 0; i < stones; i++)
            {
                float r = 0.3f - i * 0.06f;
                Detail.Sphere(at + new Vector3(0f, y + r * 0.8f, 0f), r, CairnColor);
                y += r * 1.4f;
            }
            if (raised < 1f)
                continue;
            Vector3 top = at + new Vector3(0f, y + 0.12f, 0f);
            switch (group.Belief)
            {
                case Belief.Oak:
                    Detail.Sphere(top, 0.12f, new Color(176, 116, 52, 255));
                    Raylib.DrawCylinder(top + new Vector3(0f, 0.06f, 0f), 0.08f, 0.13f, 0.08f, 8, new Color(112, 90, 60, 255));
                    break;
                case Belief.Pond:
                    Detail.Sphere(top, 0.13f, new Color(70, 140, 210, 255));
                    break;
                case Belief.Spider:
                    Detail.Sphere(top, 0.1f, new Color(50, 40, 35, 255));
                    for (int leg = 0; leg < 8; leg++)
                    {
                        float a = leg * MathF.Tau / 8f;
                        Raylib.DrawLine3D(top, top + new Vector3(MathF.Cos(a) * 0.22f, -0.08f, MathF.Sin(a) * 0.22f), new Color(50, 40, 35, 255));
                    }
                    break;
                case Belief.Moon:
                    Raylib.DrawCylinderEx(top - new Vector3(0.02f, 0f, 0f), top + new Vector3(0.02f, 0f, 0f), 0.14f, 0.14f, 12, new Color(235, 235, 215, 255));
                    break;
            }
            Raylib.DrawCube(top + new Vector3(0f, 0.2f, 0f), 0.06f, 0.04f, 0.01f, group.Color);
        }
    }

    /// <summary>A candle by each raised shrine, glowing after dark.</summary>
    private void DrawShrineCandles(float darkness)
    {
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Shrine is { } at && group.ShrineRaised >= 1f)
                Detail.Sphere(at + new Vector3(0.35f, 0.15f, 0f), 0.12f, CandleGlowColor with { A = (byte)(160 * darkness) });
        }
    }
}
