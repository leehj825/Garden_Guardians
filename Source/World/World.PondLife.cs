using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Life at the pond: frogs and the Heron ---------------------------------------------

    /// <summary>At most this many frogs on the bank of a full pond (fewer as it shrinks)…</summary>
    private const int MaxFrogs = 6;

    /// <summary>…one more turning up every this many seconds (at its season's pace).</summary>
    private const float FrogSpawnInterval = 25f;

    /// <summary>The Heron comes down to the pond every this many seconds or so…</summary>
    private const float HeronVisitMin = 240f, HeronVisitMax = 480f;

    /// <summary>…and stays this long, unless it's driven off.</summary>
    private const float HeronStayMin = 80f, HeronStayMax = 140f;

    /// <summary>It only comes while the pond is at least this full, and leaves if it shrinks below <see cref="HeronLeavesBelowFullness"/>.</summary>
    private const float HeronComesAboveFullness = 0.5f;

    public const float HeronLeavesBelowFullness = 0.3f;

    private static readonly Color WildTextColor = new(90, 120, 150, 255);

    public List<Frog> Frogs { get; } = new();

    /// <summary>The Heron, while it's at the pond (or flying in or off).</summary>
    public Heron? Heron { get; private set; }

    private float _frogTimer = FrogSpawnInterval, _heronTimer = HeronVisitMin;

    public int FrogsCaught { get; private set; }

    public int FrogsTakenByHeron { get; private set; }

    public int HeronVisits { get; private set; }

    public int HeronStabs { get; private set; }

    public int HeronsDrivenOff { get; private set; }

    public int HeronsKilled { get; private set; }

    /// <summary>How fast frogs turn up: most in spring, none in winter (they're down in the mud).</summary>
    private static float FrogPace(Season season) => season switch
    {
        Season.Spring => 1.5f,
        Season.Summer => 1f,
        Season.Autumn => 0.6f,
        _ => 0f,
    };

    /// <summary>
    /// Frogs come up onto the bank (fewer in a drought, none in winter);
    /// the Heron comes down to the pond now and then, while it's full
    /// enough and it isn't winter.
    /// </summary>
    private void UpdatePondLife(float deltaTime)
    {
        for (int i = Frogs.Count - 1; i >= 0; i--)
        {
            if (Frogs[i].IsDead || !Frogs[i].Update(deltaTime, this))
                Frogs.RemoveAt(i);
        }
        Vector3[] shore = WaterMap.Shore;
        if (Tick(ref _frogTimer, FrogSpawnInterval, FrogPace(CurrentSeason), deltaTime) && Frogs.Count < MaxFrogs * WaterMap.Fullness && shore.Length > 0)
        {
            Vector3 spot = shore[Rng.Next(shore.Length)];
            if (!IsBlocked(spot, Frog.BodyRadius))
                Frogs.Add(new Frog(spot, Rng));
        }

        if (Heron is { } heron)
        {
            if (!heron.Update(deltaTime, this))
                Heron = null;
            return;
        }
        _heronTimer -= deltaTime;
        if (_heronTimer > 0f)
            return;
        _heronTimer = HeronVisitMin + (float)Rng.NextDouble() * (HeronVisitMax - HeronVisitMin);
        if (CurrentSeason == Season.Winter || WaterMap.Fullness < HeronComesAboveFullness || shore.Length == 0)
            return;
        if (QuietShoreSpot(shore) is not { } landing)
            return;
        Heron = new Heron(landing, HeronStayMin + (float)Rng.NextDouble() * (HeronStayMax - HeronStayMin), Rng);
        HeronVisits++;
        Game.AddEventLog("[WILD] A heron came down to the pond - beware the water's edge");
        if (HeronVisits == 1)
            Headline("The heron", "A heron came down to the pond: beware the water's edge", landing, true);
    }

    /// <summary>A stretch of shore for the Heron to come down on: clear, and away from anyone's home. Null if a few tries find none.</summary>
    private Vector3? QuietShoreSpot(Vector3[] shore)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector3 spot = shore[Rng.Next(shore.Length)];
            if (!IsBlocked(spot, Heron.BodyRadius) && !IsNearHome(spot, 5f))
                return spot;
        }
        return null;
    }

    /// <summary>True if <paramref name="spot"/> lies within <paramref name="margin"/> of any standing home's edge — somewhere the Heron won't set foot.</summary>
    public bool IsNearHome(Vector3 spot, float margin) =>
        Shelters.Any(s => !s.IsCollapsed && GroundMover.HorizontalDistance(s.Position, spot) < s.Radius + margin);

    /// <summary>The nearest frog out on the bank (not under the water, nor leaping for it) within <paramref name="radius"/> of <paramref name="from"/>.</summary>
    public Frog? NearestVisibleFrog(Vector3 from, float radius)
    {
        Frog? best = null;
        float bestDistance = radius * radius;
        foreach (Frog frog in Frogs)
        {
            if (frog.IsDead || frog.IsHidden)
                continue;
            float distance = GroundMover.HorizontalDistanceSquared(from, frog.Position);
            if (distance <= bestDistance)
            {
                best = frog;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Small game a Bramblekin could hunt within <paramref name="radius"/>: the nearest Grub, or frog out on the bank.</summary>
    public ICombatant? NearestPrey(Vector3 from, float radius)
    {
        Grub? grub = NearestLiveGrub(from, radius);
        Frog? frog = NearestVisibleFrog(from, radius);
        if (grub is null || frog is null)
            return (ICombatant?)grub ?? frog;
        return GroundMover.HorizontalDistanceSquared(from, grub.Position) <= GroundMover.HorizontalDistanceSquared(from, frog.Position) ? grub : frog;
    }

    /// <summary>A frog caught: a couple of pieces of meat on the bank.</summary>
    public void KillFrog(Frog frog, Bramblekin killer)
    {
        if (frog.IsDead)
            return;
        frog.MarkDead();
        FrogsCaught++;
        Vector3 at = frog.Position;
        if (WaterMap.IsWet(at.X, at.Z) && NearestShoreSpot(at, 6f) is { } bank)
            at = bank;
        ScatterFoodAround(at, Frog.MeatYield, 0.3f, FoodShardKind.Meat);
        CreditMeat(killer, Frog.MeatYield);
        if (FrogsCaught == 1)
            Game.AddEventLog($"[HUNT] {killer.Name} caught the first frog on the bank of the pond");
    }

    /// <summary>The Heron spears a frog, and swallows it whole.</summary>
    public void HeronTakesFrog(Frog frog)
    {
        if (frog.IsDead)
            return;
        frog.MarkDead();
        FrogsTakenByHeron++;
    }

    public void NoteHeronStab() => HeronStabs++;

    /// <summary>The Heron flies off — driven off by <paramref name="driver"/>, or just done with the pond for now.</summary>
    public void NoteHeronLeft(Heron heron, Bramblekin? driver)
    {
        if (driver is null)
            return;
        HeronsDrivenOff++;
        driver.AddReputation(0.5f);
        string who = GroupOf(driver) is { } clan ? $"{driver.Name} of {clan.Title}" : driver.Name;
        QueueFloatingText(heron.Position, "Driven off!", WildTextColor);
        Game.AddEventLog($"[HUNT] {who} drove the heron off the pond");
    }

    /// <summary>The Heron brought down: a feast of meat on the bank.</summary>
    public void KillHeron(Heron heron, Bramblekin killer)
    {
        if (heron.IsSlain)
            return;
        heron.MarkSlain();
        HeronsKilled++;
        killer.AddReputation(1.5f);
        Vector3 at = heron.Position;
        if (WaterMap.IsWet(at.X, at.Z) && NearestShoreSpot(at, 8f) is { } bank)
            at = bank;
        ScatterFoodAround(at, Heron.MeatYield, 0.7f, FoodShardKind.Meat);
        CreditMeat(killer, Heron.MeatYield);
        KinGroup? clan = GroupOf(killer);
        string who = clan is null ? killer.Name : $"{clan.CapitalTitle} (final blow by {killer.Name})";
        Game.AddEventLog($"[HUNT] {who} brought down the heron!");
        Headline("The heron", $"{who} brought down the heron", at, false, clan);
        if (clan is not null)
            Chronicle($"{clan.CapitalTitle} brought down a heron at the pond (final blow by {killer.Name})", clan);
    }

    private void DrawPondLife(Camera3D camera)
    {
        foreach (Frog frog in Frogs)
        {
            if (!frog.IsDead && IsVisible(frog.Position, camera))
                frog.Draw();
        }
        if (Heron is { IsSlain: false } heron && IsVisible(heron.Position, camera))
            heron.Draw();
    }
}
