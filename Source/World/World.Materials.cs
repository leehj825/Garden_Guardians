using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Stones and branches ------------------------------------------------------------------

    private const int MaterialPoolCapacity = 80;

    /// <summary>Stones lying at the foot of the rocks when the garden begins…</summary>
    private const int InitialStones = 16;

    /// <summary>…and at most this many at once, one more working loose every <see cref="StoneSpawnInterval"/> seconds.</summary>
    private const int MaxLooseStones = 24;

    private const float StoneSpawnInterval = 12f;

    /// <summary>At most this many branches lie about at once.</summary>
    private const int MaxLooseBranches = 10;

    /// <summary>In a storm the oak sheds a branch this often (s)…</summary>
    private const float StormBranchInterval = 6f;

    /// <summary>…and in any weather the big sticks on the lawn shed one this often.</summary>
    private const float BranchSpawnInterval = 90f;

    /// <summary>A Builder looks for stones and branches this far (m) from where it stands.</summary>
    public const float MaterialSearchRadius = 45f;

    private static readonly Color MaterialTextColor = new(110, 110, 120, 255);

    /// <summary>Object Pooling: the fixed pool of stone and branch slots; only the active ones are real.</summary>
    public List<Material> Materials { get; } = new();

    private float _stoneTimer = StoneSpawnInterval, _branchTimer = BranchSpawnInterval, _stormBranchTimer = StormBranchInterval;

    /// <summary>Stones laid into footings, and footings finished.</summary>
    public int StonesLaid { get; private set; }

    public int FootingsLaid { get; private set; }

    /// <summary>Branches staked into palisades, and palisades finished.</summary>
    public int StakesSet { get; private set; }

    public int PalisadesRaised { get; private set; }

    /// <summary>Fills the pool — and, in a fresh garden (or one saved before stones were used), sets the first stones down.</summary>
    private void InitializeMaterials(bool scatterStones)
    {
        for (int i = 0; i < MaterialPoolCapacity; i++)
            Materials.Add(new Material());
        if (!scatterStones)
            return;
        for (int i = 0; i < InitialStones; i++)
            SpawnStone();
    }

    private Material? ActivateMaterial(Vector3 at, MaterialKind kind)
    {
        foreach (Material material in Materials)
        {
            if (!material.IsActive)
            {
                material.Activate(at, kind, (float)(Rng.NextDouble() * MathF.Tau));
                return material;
            }
        }
        return null;
    }

    private int LooseMaterial(MaterialKind kind) => Materials.Count(m => m is { IsActive: true, IsCarried: false } && m.Kind == kind);

    /// <summary>A stone works loose at the foot of one of the big rocks.</summary>
    private void SpawnStone()
    {
        GardenProp[] rocks = GardenProps.Where(p => p.Kind == GardenPropKind.Pebble).ToArray();
        if (rocks.Length == 0)
            return;
        GardenProp rock = rocks[Rng.Next(rocks.Length)];
        float angle = (float)(Rng.NextDouble() * MathF.Tau);
        Vector3 spot = rock.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (rock.FootprintRadius + 0.3f + (float)Rng.NextDouble() * 1.2f);
        if (Terrain.Contains(spot, 1f) && !IsBlocked(spot, Material.StoneRadius))
            ActivateMaterial(spot, MaterialKind.Stone);
    }

    /// <summary>A branch comes down between <paramref name="near"/> and <paramref name="far"/> meters from <paramref name="center"/>.</summary>
    private void SpawnBranch(Vector3 center, float near, float far)
    {
        float angle = (float)(Rng.NextDouble() * MathF.Tau);
        Vector3 spot = center + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (near + (float)Rng.NextDouble() * (far - near));
        if (Terrain.Contains(spot, 2f) && !IsBlocked(spot, Material.BranchLength / 2f))
            ActivateMaterial(spot, MaterialKind.Branch);
    }

    /// <summary>Stones work loose at the rocks; branches come down off the oak in storms, and now and then off the big sticks; a branch left lying rots, and a flood carries branches off.</summary>
    private void UpdateMaterials(float deltaTime)
    {
        float level = WaterLevel;
        foreach (Material material in Materials)
        {
            if (!material.IsActive || material.IsCarried || material.Kind != MaterialKind.Branch)
                continue;
            material.DespawnTimer -= deltaTime;
            if (material.DespawnTimer <= 0f || (IsFlooded && GetHeightAt(material.Position.X, material.Position.Z) < level))
                material.Deactivate();
        }

        if (Tick(ref _stoneTimer, StoneSpawnInterval, 1f, deltaTime) && LooseMaterial(MaterialKind.Stone) < MaxLooseStones)
            SpawnStone();

        bool roomForBranch = LooseMaterial(MaterialKind.Branch) < MaxLooseBranches;
        if (IsStorming && Tick(ref _stormBranchTimer, StormBranchInterval, 1f, deltaTime) && roomForBranch)
            SpawnBranch(OakCenter, OakRadius + 2f, OakRadius + 18f);
        if (Tick(ref _branchTimer, BranchSpawnInterval, 1f, deltaTime) && roomForBranch && _twigPatches.Count > 0)
            SpawnBranch(_twigPatches[Rng.Next(_twigPatches.Count)], 0.8f, 3f);
    }

    /// <summary>The nearest loose <paramref name="kind"/> within <paramref name="radius"/> that nobody else is fetching.</summary>
    public Material? NearestMaterial(Vector3 from, MaterialKind kind, float radius, Bramblekin claimant)
    {
        Material? best = null;
        float bestDistance = radius * radius;
        foreach (Material material in Materials)
        {
            if (!material.IsActive || material.IsCarried || material.Kind != kind ||
                (material.ClaimedBy is { IsDead: false } other && other != claimant && other.FetchingMaterial == material))
                continue;
            float distance = GroundMover.HorizontalDistanceSquared(from, material.Position);
            if (distance <= bestDistance)
            {
                best = material;
                bestDistance = distance;
            }
        }
        return best;
    }

    public static void PickUpMaterial(Material material)
    {
        material.IsCarried = true;
        material.ClaimedBy = null;
    }

    /// <summary>Puts carried <paramref name="material"/> down at <paramref name="position"/> (its carrier died, or gave up).</summary>
    public static void DropMaterial(Material material, Vector3 position)
    {
        material.Position = Grounded(position);
        material.IsCarried = false;
        material.ClaimedBy = null;
    }

    /// <summary>
    /// The home of <paramref name="group"/>'s nearest <paramref name="near"/>
    /// that still needs <paramref name="kind"/> — stones for a House's footing
    /// once the clan knows <see cref="Craft.Stonework"/>, branches for a
    /// palisade once it knows <see cref="Craft.Palisade"/>. Null if none does.
    /// </summary>
    public Shelter? HomeNeeding(KinGroup group, MaterialKind kind, Vector3 near)
    {
        Craft known = CraftsOf(group);
        if ((kind == MaterialKind.Stone && (known & Craft.Stonework) == 0) || (kind == MaterialKind.Branch && (known & Craft.Palisade) == 0))
            return null;
        Shelter? best = null;
        float bestDistance = float.MaxValue;
        foreach (Shelter home in GroupHomes(group))
        {
            if (home.IsCollapsed || !(kind == MaterialKind.Stone ? home.NeedsStones : home.NeedsStakes))
                continue;
            float distance = GroundMover.HorizontalDistanceSquared(near, home.Position);
            if (distance < bestDistance)
            {
                best = home;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>A Builder brings <paramref name="material"/> home: a stone laid into the footing, or a branch staked into the palisade — finishing it, maybe.</summary>
    public void DeliverMaterial(Bramblekin builder, Shelter home, Material material)
    {
        material.Deactivate();
        string whose = GroupOf(builder)?.Title ?? builder.Name;
        if (material.Kind == MaterialKind.Stone)
        {
            if (!home.LayStone())
                return;
            StonesLaid++;
            if (!home.HasFooting)
                return;
            FootingsLaid++;
            QueueFloatingText(home.Position, "Footing laid!", MaterialTextColor);
            Game.AddEventLog($"[BUILD] {builder.Name} laid the last stone of a stone footing for {whose}");
            return;
        }

        if (!home.SetStake())
            return;
        StakesSet++;
        if (!home.HasPalisade)
            return;
        PalisadesRaised++;
        QueueFloatingText(home.Position, "Palisade up!", MaterialTextColor);
        Game.AddEventLog($"[BUILD] {builder.Name} set the last stake of a palisade for {whose}");
    }

    private void DrawMaterials(Camera3D camera)
    {
        foreach (Material material in Materials)
        {
            if (material.IsActive && !material.IsCarried && IsVisible(material.Position, camera))
                material.Draw();
        }
    }
}
