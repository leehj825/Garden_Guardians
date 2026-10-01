using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Aphid herding (see Craft.Herding and AphidPen) -----------------------------------------

    /// <summary>Each aphid gives a drop of honeydew about this often (s) — three times slower in winter.</summary>
    private const float HoneydewSeconds = 150f;

    /// <summary>With a pair or more, the herd grows by one about this often (s), from spring to autumn.</summary>
    private const float AphidBreedSeconds = 240f;

    /// <summary>A pen stops giving once this much honeydew lies in it uncollected.</summary>
    private const int MaxLooseHoneydew = 3;

    /// <summary>A clan whose herd has died out (or never started) gathers a new pair off the stems with these odds each decision, spring to autumn.</summary>
    private const double RegatherChance = 0.08;

    /// <summary>The Wolf Spider, passing a pen, takes an aphid — then can't take another for this long (s).</summary>
    private const float PenPreyCooldown = 40f;

    /// <summary>A war raider drives off an aphid from a pen of two or more with these odds.</summary>
    private const double RustleChance = 0.35;

    /// <summary>A pen left without its clan loses an aphid this often (s) as the herd drifts away.</summary>
    private const float StraySeconds = 60f;

    private static readonly Color HerdTextColor = new(120, 170, 60, 255);

    public List<AphidPen> Pens { get; } = new();

    public int HoneydewDrops { get; private set; }
    public int AphidsBred { get; private set; }
    public int AphidsLostToAnts { get; private set; }
    public int AphidsLostToSpider { get; private set; }
    public int AphidsRustled { get; private set; }
    public int PensFenced { get; private set; }

    /// <summary>The pen <paramref name="group"/> keeps, if any.</summary>
    public AphidPen? PenOf(KinGroup group)
    {
        foreach (AphidPen pen in Pens)
        {
            if (pen.GroupId == group.Id)
                return pen;
        }
        return null;
    }

    /// <summary>
    /// At each Leader decision, for a clan that knows herding: fences a pen
    /// by its main home and gathers a pair of aphids off the stems (spring to
    /// autumn) — again if its herd has died out; moves the herd along if the
    /// clan has moved house.
    /// </summary>
    private void TendHerd(KinGroup group)
    {
        if (!Knows(group, Craft.Herding) || group.Home is not { IsBuilt: true } home)
            return;
        AphidPen? pen = PenOf(group);
        if (pen is not null && GroundMover.HorizontalDistance(pen.Position, home.Position) > home.Radius + 8f)
        {
            // Moved house: the herd is driven along to a new pen.
            Pens.Remove(pen);
            if (FencePen(group, home, pen.Aphids) is null)
                return;
            pen = PenOf(group);
        }
        if (CurrentSeason == Season.Winter)
            return;
        if (pen is null)
        {
            pen = FencePen(group, home, 2);
            if (pen is not null)
                Game.AddEventLog($"[HERD] {group.CapitalTitle} fenced an aphid pen and gathered a pair off the stems");
            return;
        }
        if (pen.Aphids == 0 && Rng.NextDouble() < RegatherChance)
        {
            pen.Aphids = 2;
            Game.AddEventLog($"[HERD] {group.CapitalTitle} gathered a new pair of aphids for their pen");
        }
    }

    /// <summary>A new pen for <paramref name="group"/> beside <paramref name="home"/>, holding <paramref name="aphids"/>; null if there's no room.</summary>
    private AphidPen? FencePen(KinGroup group, Shelter home, int aphids)
    {
        if (FindPenSpot(home.Position, null, home) is not { } spot)
            return null;
        var pen = new AphidPen(spot, group.Id, aphids)
        {
            HoneydewTimer = HoneydewSeconds / 2f,
            BreedTimer = AphidBreedSeconds,
        };
        Pens.Add(pen);
        PensFenced++;
        return pen;
    }

    /// <summary>A free spot for a pen beside <paramref name="around"/>: clear of every home's palisade ring, wells, crops and other pens (<paramref name="ignore"/> being the pen itself, when it is moving).</summary>
    private Vector3? FindPenSpot(Vector3 around, AphidPen? ignore, Shelter? home = null)
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float distance = (home is null ? 3.5f : home.PalisadeRadius + AphidPen.DrawRadius + 0.4f) + attempt * 0.25f + (float)Rng.NextDouble() * 1.5f;
            Vector3 spot = Grounded(around + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance);
            if (!Terrain.Contains(spot, AphidPen.DrawRadius + 1f) || IsBlocked(spot, AphidPen.Radius) || IsCramped(spot))
                continue;
            if (Shelters.Any(s => !s.IsCollapsed && GroundMover.HorizontalDistance(s.Position, spot) < HomeYard(s) + AphidPen.DrawRadius))
                continue;
            if (Pens.Any(p => p != ignore && GroundMover.HorizontalDistance(p.Position, spot) < AphidPen.DrawRadius * 2f) || OverlapsLayout(spot, AphidPen.DrawRadius))
                continue;
            return spot;
        }
        return null;
    }

    /// <summary>
    /// Every pen: its aphids give honeydew (slower in winter) and breed up
    /// (not in winter); the Wolf Spider picks one off if it passes; a pen
    /// whose clan is gone slowly empties as the herd strays, and is taken
    /// down once it's empty.
    /// </summary>
    private void UpdatePens(float deltaTime)
    {
        for (int i = Pens.Count - 1; i >= 0; i--)
        {
            AphidPen pen = Pens[i];
            pen.PreyCooldown = MathF.Max(0f, pen.PreyCooldown - deltaTime);
            if (pen.GroupId is { } id && !_groups.ContainsKey(id))
                pen.GroupId = null;
            if (pen.GroupId is null)
            {
                pen.BreedTimer -= deltaTime;
                if (pen.BreedTimer <= 0f)
                {
                    pen.BreedTimer = StraySeconds;
                    pen.Aphids--;
                }
                if (pen.Aphids <= 0)
                    Pens.RemoveAt(i);
                continue;
            }
            if (pen.Aphids <= 0)
                continue;

            bool winter = CurrentSeason == Season.Winter;
            pen.HoneydewTimer -= deltaTime * pen.Aphids * (winter ? 1f / 3f : 1f);
            if (pen.HoneydewTimer <= 0f)
            {
                pen.HoneydewTimer = HoneydewSeconds;
                if (LooseHoneydewIn(pen) < MaxLooseHoneydew)
                {
                    float angle = (float)(Rng.NextDouble() * MathF.Tau);
                    float r = (float)Rng.NextDouble() * AphidPen.Radius * 0.7f;
                    _pendingFoodSpawns.Add((pen.Position + new Vector3(MathF.Cos(angle) * r, 0f, MathF.Sin(angle) * r), FoodShardKind.Honeydew));
                    HoneydewDrops++;
                }
            }

            if (!winter && pen.Aphids >= 2 && pen.Aphids < AphidPen.MaxAphids)
            {
                pen.BreedTimer -= deltaTime;
                if (pen.BreedTimer <= 0f)
                {
                    pen.BreedTimer = AphidBreedSeconds;
                    pen.Aphids++;
                    AphidsBred++;
                }
            }

            if (pen.PreyCooldown <= 0f && Spider is { IsDead: false } spider &&
                GroundMover.HorizontalDistance(spider.Position, pen.Position) <= AphidPen.Radius + 2f)
            {
                pen.Aphids--;
                pen.PreyCooldown = PenPreyCooldown;
                AphidsLostToSpider++;
                QueueFloatingText(pen.Position, "Aphid taken!", HostileTextColor);
            }
        }
    }

    private int LooseHoneydewIn(AphidPen pen)
    {
        int count = 0;
        foreach (FoodShard food in FoodShards)
        {
            if (food is { IsActive: true, IsCarried: false, Kind: FoodShardKind.Honeydew } && pen.Contains(food.Position))
                count++;
        }
        return count;
    }

    /// <summary>The nearest pen with aphids in it within reach of the ants' hill, for an ant with no store to rob.</summary>
    public AphidPen? PenForAnts(Vector3 from, Anthill hill)
    {
        AphidPen? best = null;
        float bestDistance = float.MaxValue;
        foreach (AphidPen pen in Pens)
        {
            if (pen.Aphids <= 0 || GroundMover.HorizontalDistanceSquared(pen.Position, hill.Position) > Anthill.ForageRadius * Anthill.ForageRadius)
                continue;
            float distance = GroundMover.HorizontalDistanceSquared(from, pen.Position);
            if (distance < bestDistance)
            {
                best = pen;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>An ant in a pen carries off an aphid to milk at its own hill. False if the pen's empty.</summary>
    public bool AntTakesAphid(Ant ant, AphidPen pen)
    {
        if (pen.Aphids <= 0)
            return false;
        pen.Aphids--;
        AphidsLostToAnts++;
        QueueFloatingText(pen.Position, "Ants took an aphid!", AntTextColor);
        return true;
    }

    /// <summary>A war raider at <paramref name="store"/> may drive off one of its clan's aphids to its own clan's pen (if it keeps one).</summary>
    private void TryRustle(Bramblekin raider, Shelter store)
    {
        if (store.GroupId is not { } victimId || !_groups.TryGetValue(victimId, out KinGroup? victims) || GroupOf(raider) is not { } raiders)
            return;
        if (PenOf(victims) is not { Aphids: >= 2 } theirs || PenOf(raiders) is not { Aphids: < AphidPen.MaxAphids } ours)
            return;
        if (Rng.NextDouble() >= RustleChance)
            return;
        theirs.Aphids--;
        ours.Aphids++;
        AphidsRustled++;
        QueueFloatingText(theirs.Position, "Rustled!", HostileTextColor);
        Game.AddEventLog($"[HERD] {raider.Name} of {raiders.Title} drove off one of {victims.Title}'s aphids");
        Spotlight($"{raiders.CapitalTitle} rustle {victims.Title}'s aphids", 7f, theirs.Position);
        AddGrievance(raiders.Id, victims.Id, RaidGrievance);
    }

    private void DrawPens(Camera3D camera)
    {
        foreach (AphidPen pen in Pens)
        {
            if (!IsVisible(pen.Position, camera))
                continue;
            Color? flag = pen.GroupId is { } id && _groups.TryGetValue(id, out KinGroup? clan) ? clan.Color : null;
            pen.Draw(ElapsedSeconds, flag);
        }
    }
}
