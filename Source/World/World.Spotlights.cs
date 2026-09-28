using System.Numerics;

namespace GardenGuardians;

/// <summary>
/// Something worth watching, for the Director (see <see cref="Director"/>):
/// what it is, how worth watching (higher is better), who to keep the
/// camera on while it lasts — and where, once they're gone.
/// </summary>
public readonly record struct Shot(string Label, float Score, ICombatant? Subject, Vector3 Where)
{
    /// <summary>Where to look right now: on its subject while it's alive, else where it happened.</summary>
    public Vector3 Focus => Subject is { IsDead: false } subject ? subject.Position : Where;
}

public sealed partial class World
{
    /// <summary>A spotlit moment stays worth watching this long (s of game time), fading as it goes.</summary>
    private const float SpotlightSeconds = 30f;

    private const int SpotlightCapacity = 16;

    private readonly List<(Shot Shot, float At)> _spotlights = new();

    /// <summary>Puts a moment in the Director's spotlight (see <see cref="DirectorShots"/>): a birth, a headline, a feast…</summary>
    public void Spotlight(string label, float score, Vector3 where, ICombatant? subject = null)
    {
        _spotlights.Add((new Shot(label, score, subject, where), ElapsedSeconds));
        if (_spotlights.Count > SpotlightCapacity)
            _spotlights.RemoveAt(0);
    }

    /// <summary>
    /// Everything worth watching right now, for the Director: moments in the
    /// spotlight (fading with age), leadership duels, fights between kin,
    /// raids, the Wolf Spider on the hunt, the Owl and the Heron at work,
    /// beetle hunts — and, when nothing's happening, the busiest village.
    /// </summary>
    public List<Shot> DirectorShots()
    {
        var shots = new List<Shot>();
        for (int i = _spotlights.Count - 1; i >= 0; i--)
        {
            var (shot, at) = _spotlights[i];
            float age = ElapsedSeconds - at;
            if (age > SpotlightSeconds)
            {
                _spotlights.RemoveAt(i);
                continue;
            }
            shots.Add(shot with { Score = shot.Score * (1f - 0.5f * age / SpotlightSeconds) });
        }

        var fights = new HashSet<(Guid?, Guid?)>();
        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsDead)
                continue;
            if (kin.DuelOpponent is { IsDead: false } rival && kin.ID < rival.ID)
            {
                shots.Add(BoutOf(kin) is { } bout
                    ? new Shot($"Champions: {bout.FirstChampion.Name} of {bout.First.Title} against {bout.SecondChampion.Name} of {bout.Second.Title}", 9.5f, kin, kin.Position)
                    : new Shot($"{kin.Name} and {rival.Name} fight to lead {GroupOf(kin)?.Title ?? "their clan"}", 9f, kin, kin.Position));
                continue;
            }
            if (kin.State == BramblekinState.Fighting && kin.CombatTarget is Bramblekin foe && !foe.IsDead)
            {
                // One shot per pair of sides, however many are in it.
                var key = kin.GroupId?.CompareTo(foe.GroupId ?? Guid.Empty) < 0 ? (kin.GroupId, foe.GroupId) : (foe.GroupId, kin.GroupId);
                if (fights.Add(key))
                    shots.Add(new Shot($"A fight: {Side(kin)} against {Side(foe)}", 7f, kin, kin.Position));
                continue;
            }
            if (kin.State == BramblekinState.Raiding && kin.RaidTarget is { } raided && fights.Add((kin.GroupId, raided.GroupId)))
            {
                shots.Add(new Shot($"{Capitalized(Side(kin))} raid {(raided.GroupId is { } id && _groups.TryGetValue(id, out KinGroup? victim) ? victim.Title : "a store")}", 7.5f, kin, kin.Position));
                continue;
            }
            if (kin.State == BramblekinState.Hunting && kin.CombatTarget is StagBeetle beetle && !beetle.IsDead && fights.Add((kin.GroupId, null)))
                shots.Add(new Shot($"{Capitalized(Side(kin))} hunt a stag beetle", 5f, beetle, beetle.Position));
        }

        if (Spider is { IsDead: false } spider && spider.State is SpiderState.Hunting or SpiderState.Pouncing or SpiderState.Feeding)
        {
            string what = spider.State switch
            {
                SpiderState.Pouncing => "The Wolf Spider pounces",
                SpiderState.Feeding => "The Wolf Spider feeds",
                _ => "The Wolf Spider is on the hunt",
            };
            shots.Add(new Shot(what, spider.State == SpiderState.Pouncing ? 8f : spider.State == SpiderState.Hunting ? 5.5f : 3f, spider, spider.Position));
        }
        if (Owl is { IsSlain: false, IsLeaving: false } owl)
            shots.Add(new Shot(owl.IsLanded ? "The owl has struck" : "The owl is out hunting", owl.IsLanded ? 8f : 4f, null, Grounded(owl.Position)));
        foreach (Feast feast in _feasts)
            shots.Add(new Shot($"A harvest feast in {feast.Host.Title}", 5f + 0.2f * Math.Min(feast.Attended.Count, 15), null, feast.Site));
        if (Heron is { IsLanded: true } heron)
            shots.Add(new Shot("The heron stalks the shallows", 3.5f, heron, heron.Position));

        // Nothing much happening: the liveliest village.
        KinGroup? busiest = null;
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Home is { IsBuilt: true } && (busiest is null || group.Members.Count > busiest.Members.Count))
                busiest = group;
        }
        if (busiest?.Home is { } home)
            shots.Add(new Shot($"Life in {busiest.Title}", 1.5f + 0.1f * busiest.Members.Count, null, home.Position));
        return shots;

        string Side(Bramblekin who) => GroupOf(who)?.Title ?? who.Name;
        static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];
    }
}
