using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    private readonly Dictionary<int, LifeRecord> _lives = new();

    /// <summary>Every Bramblekin that has ever lived in the garden, by ID — see <see cref="LifeRecord"/>.</summary>
    public IReadOnlyDictionary<int, LifeRecord> Lives => _lives;

    /// <summary>Starts the record of a Bramblekin's life, as it's born or wanders in.</summary>
    private void RegisterLife(Bramblekin kin)
    {
        if (_lives.ContainsKey(kin.ID))
            return;
        _lives[kin.ID] = new LifeRecord
        {
            Id = kin.ID,
            Name = kin.Name,
            MotherId = kin.ParentIds?.Mother,
            FatherId = kin.ParentIds?.Father,
            Generation = kin.Generation,
            Arrived = ElapsedSeconds,
        };
    }

    /// <summary>Closes the record of a life that just ended, with how it ended and what it amounted to.</summary>
    private void CloseLife(Bramblekin kin, string fate)
    {
        RegisterLife(kin);
        LifeRecord life = _lives[kin.ID];
        life.Died = ElapsedSeconds;
        life.Fate = fate;
        life.Clan = GroupOf(kin)?.Title;
        life.Children = kin.Children;
        life.AgeYears = kin.AgeInYears;
        life.LeaderSeconds = kin.LeaderSeconds;
        life.SpiderKills = kin.SpiderKills;
        life.Honour = kin.LeaderSeconds >= Bramblekin.SecondsPerYear * 0.25f ? "Leader" : kin.IsMaster ? kin.DescribeTrade() : null;
        if (life.Honour is not null)
        {
            life.GraveX = kin.Position.X;
            life.GraveZ = kin.Position.Z;
            _gravesBuilt = -1; // (the memorials are listed afresh)
        }
    }

    /// <summary>Leaders earn their reign, second by second (for the hall of fame).</summary>
    private void UpdateReigns(float deltaTime)
    {
        foreach (KinGroup group in _groups.Values)
        {
            if (group.Leader is { IsDead: false } leader)
                leader.AddLeaderTime(deltaTime);
        }
    }

    /// <summary>The living Bramblekin with <paramref name="id"/>, if it's still alive.</summary>
    public Bramblekin? LivingKin(int id) => Colony.FirstOrDefault(k => k.ID == id && !k.IsDead);
}

public sealed partial class World
{
    [NotSaved]
    private int _gravesBuilt = -1;

    [NotSaved]
    private readonly List<LifeRecord> _graves = new();

    /// <summary>The remembered: Leaders and masters who have died, newest first (for the Hall of ancestors and the memorial stones).</summary>
    public IReadOnlyList<LifeRecord> Ancestors
    {
        get
        {
            if (_gravesBuilt != _lives.Count)
            {
                _graves.Clear();
                _graves.AddRange(_lives.Values.Where(l => l.Honour is not null && l.Died is not null).OrderByDescending(l => l.Died));
                _gravesBuilt = _lives.Count;
            }
            return _graves;
        }
    }

    private static readonly Color StoneGrey = new(150, 150, 146, 255), StoneDark = new(104, 104, 100, 255);

    /// <summary>A small memorial stone where each remembered Leader or master fell, near the camera's focus (skipped in low detail).</summary>
    private void DrawMemorials(Camera3D camera)
    {
        if (LowDetail)
            return;
        float reach = MathF.Max(25f, Vector3.Distance(camera.Position, camera.Target) * 0.8f);
        foreach (LifeRecord life in Ancestors.Take(300))
        {
            float dx = life.GraveX - camera.Target.X, dz = life.GraveZ - camera.Target.Z;
            if (dx * dx + dz * dz > reach * reach)
                continue;
            float y = GetHeightAt(life.GraveX, life.GraveZ);
            Raylib.DrawCube(new Vector3(life.GraveX, y + 0.05f, life.GraveZ), 0.5f, 0.1f, 0.3f, StoneDark);
            Raylib.DrawCube(new Vector3(life.GraveX, y + 0.3f, life.GraveZ), 0.26f, 0.5f, 0.07f, StoneGrey);
        }
    }
}
