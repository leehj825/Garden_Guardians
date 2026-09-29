using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>How often (s) the realm is looked over.</summary>
    private const float KingdomCheckInterval = 5f;

    /// <summary>Each look, a Kingdom Age clan with a small ally nearby gets that ally to swear fealty with these odds.</summary>
    private const double FealtyChance = 0.03;

    /// <summary>A vassal must be no bigger than this fraction of its would-be liege…</summary>
    private const float FealtyMaxSizeFraction = 0.5f;

    /// <summary>…and lie within this far (m) of its home.</summary>
    private const float FealtyReach = 45f;

    /// <summary>A vassal that grows to this fraction of its liege's size (or more) breaks free.</summary>
    private const float IndependenceSizeFraction = 1f;

    /// <summary>Tribute a sworn vassal owes each season is the usual <see cref="TributeAmount"/>; it never runs out until the vassal breaks free.</summary>
    private const int SwornSeasons = int.MaxValue / 2;

    /// <summary>Allies teach their liege's vassals this many times as readily.</summary>
    private const float LiegeTeachingBoost = 1.5f;

    private static readonly Color LiegeLineColor = new(230, 190, 60, 230);

    private float _kingdomTimer;

    /// <summary>Vassalages sworn freely, and vassals that broke free (for the headless report).</summary>
    public int FealtiesSworn { get; private set; }
    public int VassalsFreed { get; private set; }

    /// <summary>The clan <paramref name="group"/> owes fealty to, if any.</summary>
    public KinGroup? LiegeOf(KinGroup group) => group.LiegeId is { } id && _groups.TryGetValue(id, out KinGroup? liege) ? liege : null;

    /// <summary>The clans that answer to <paramref name="group"/>.</summary>
    public IEnumerable<KinGroup> VassalsOf(KinGroup group) => _groups.Values.Where(g => g.LiegeId == group.Id);

    /// <summary>A clan with vassals is the capital of a kingdom.</summary>
    public bool IsKingdom(KinGroup group) => _groups.Values.Any(g => g.LiegeId == group.Id);

    /// <summary>"The Kingdom of Thornwood (2 vassals)", "Vassal of the Thornwood clan", or null for an unbound clan.</summary>
    public string? DescribeRealm(KinGroup group)
    {
        if (LiegeOf(group) is { } liege)
            return $"Vassal of {liege.Title}";
        int vassals = VassalsOf(group).Count();
        return vassals == 0 ? null : $"Capital of a kingdom: {vassals} {(vassals == 1 ? "vassal" : "vassals")}";
    }

    /// <summary>A beaten clan that agrees to pay tribute (see <see cref="EndWar"/>) becomes its conqueror's vassal for as long as it pays.</summary>
    private void BindVassal(KinGroup vassal, KinGroup liege)
    {
        if (vassal.LiegeId is not null || LiegeOf(liege) == vassal)
            return;
        vassal.LiegeId = liege.Id;
    }

    /// <summary>
    /// Kingdoms: a Kingdom Age clan draws small allied neighbours into fealty (they pay tribute each season,
    /// and are taught its crafts more readily); a vassal whose tribute has run out is free, and one that grows
    /// as big as its liege breaks away.
    /// </summary>
    private void UpdateKingdoms(float deltaTime)
    {
        _kingdomTimer -= deltaTime;
        if (_kingdomTimer > 0f)
            return;
        _kingdomTimer = KingdomCheckInterval;

        foreach (KinGroup vassal in _groups.Values.Where(g => g.LiegeId is not null).ToList())
        {
            if (LiegeOf(vassal) is not { } liege)
            {
                vassal.LiegeId = null; // Its liege is gone.
                continue;
            }
            if (!_tributes.Any(t => t.Payer == vassal.Id && t.Receiver == liege.Id))
            {
                vassal.LiegeId = null; // Its term is served.
                VassalsFreed++;
                Game.AddEventLog($"[REALM] {vassal.CapitalTitle} is free of {liege.Title}");
                Chronicle($"{vassal.CapitalTitle} is free of {liege.Title}", vassal, liege);
                continue;
            }
            if (vassal.Members.Count >= Math.Max(3, liege.Members.Count * IndependenceSizeFraction))
                BreakFree(vassal, liege);
        }

        foreach (KinGroup liege in _groups.Values.ToList())
        {
            if (liege.LiegeId is not null || liege.Home is not { IsBuilt: true } home || EraOf(liege) < Era.KingdomAge)
                continue;
            foreach (KinGroup other in _groups.Values)
            {
                if (other == liege || other.LiegeId is not null || IsKingdom(other) || other.Home is not { IsBuilt: true } theirs)
                    continue;
                if (StanceBetween(liege.Id, other.Id) != GroupStance.Allied ||
                    other.Members.Count > liege.Members.Count * FealtyMaxSizeFraction ||
                    GroundMover.HorizontalDistance(theirs.Position, home.Position) > FealtyReach)
                    continue;
                if (Rng.NextDouble() >= FealtyChance)
                    continue;
                SwearFealty(other, liege);
                break;
            }
        }
    }

    private void SwearFealty(KinGroup vassal, KinGroup liege)
    {
        vassal.LiegeId = liege.Id;
        _tributes.Add(new Tribute { Payer = vassal.Id, Receiver = liege.Id, SeasonsLeft = SwornSeasons, NextDue = ElapsedSeconds + SeasonLength });
        FealtiesSworn++;
        Game.AddEventLog($"[REALM] {vassal.CapitalTitle} swore fealty to {liege.Title}");
        Headline("Fealty", $"{vassal.CapitalTitle} swore fealty to {liege.Title}, which now rules a kingdom", PlaceOf(liege), false, liege, vassal);
        Carve(liege, $"{vassal.Title} swore fealty");
    }

    private void BreakFree(KinGroup vassal, KinGroup liege)
    {
        vassal.LiegeId = null;
        _tributes.RemoveAll(t => t.Payer == vassal.Id && t.Receiver == liege.Id);
        VassalsFreed++;
        AddGrievance(liege.Id, vassal.Id, 2f);
        Game.AddEventLog($"[REALM] {vassal.CapitalTitle} broke free of {liege.Title}");
        Headline("Independence", $"{vassal.CapitalTitle} broke free of {liege.Title}", PlaceOf(vassal), false, vassal, liege);
    }

    /// <summary>Gold lines from each liege's home to its vassals'.</summary>
    private void DrawRealms(Camera3D camera)
    {
        foreach (KinGroup vassal in _groups.Values)
        {
            if (LiegeOf(vassal) is not { Home: { } liegeHome } || vassal.Home is not { } vassalHome)
                continue;
            if (!IsVisible(liegeHome.Position, camera) && !IsVisible(vassalHome.Position, camera))
                continue;
            var lift = new Vector3(0f, 2.6f, 0f);
            Raylib.DrawLine3D(liegeHome.Position + lift, vassalHome.Position + lift, LiegeLineColor);
            Raylib.DrawLine3D(liegeHome.Position + lift + new Vector3(0f, 0.05f, 0f), vassalHome.Position + lift + new Vector3(0f, 0.05f, 0f), LiegeLineColor);
        }
    }
}
