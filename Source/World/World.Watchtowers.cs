using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>How far (m) a watchtower sees trouble coming.</summary>
    public const float TowerSightRange = 30f;

    /// <summary>Kin within this far (m) of their own tower see this much farther (see Bramblekin.Perceive).</summary>
    public const float TowerCoverRange = 22f, TowerSightBonus = 6f;

    /// <summary>How often (s) a watchtower looks about.</summary>
    private const float TowerLookInterval = 0.5f;

    /// <summary>Seconds the horn's blast is drawn.</summary>
    private const float HornBlastSeconds = 2.5f;

    private float _towerTimer;

    /// <summary>Alarm horns sounded from watchtowers (for the headless report).</summary>
    public int HornsSounded { get; private set; }

    /// <summary>
    /// A watchtower's lookout keeps watch all day and night: the Wolf Spider, a chasing Hornet swarm
    /// or a warring clan's fighter within <see cref="TowerSightRange"/> sounds the alarm horn, which
    /// wakes and warns the whole clan (see <see cref="RaiseAlarm"/>).
    /// </summary>
    private void UpdateWatchtowers(float deltaTime)
    {
        foreach (Shelter shelter in Shelters)
        {
            if (shelter.HornSeconds > 0f)
                shelter.HornSeconds = MathF.Max(0f, shelter.HornSeconds - deltaTime);
        }

        _towerTimer -= deltaTime;
        if (_towerTimer > 0f)
            return;
        _towerTimer = TowerLookInterval;

        foreach (KinGroup group in _groups.Values)
        {
            if (group.Home is not { HasWatchtower: true, IsCollapsed: false, IsBuilt: true } tower || IsAlarmed(group))
                continue;
            Vector3 top = tower.Position + new Vector3(0f, 2.6f, 0f);
            if (SightedTrouble(group, tower) is not { } what)
                continue;

            tower.HornSeconds = HornBlastSeconds;
            HornsSounded++;
            RaiseAlarm(group, top);
            QueueFloatingText(top + new Vector3(0f, 0.6f, 0f), "Horn!", HostileTextColor);
            Game.AddEventLog($"[WATCH] The horn of {group.Title} sounded: {what}");
            Carve(group, $"the horn sounded for {what}");
        }
    }

    /// <summary>What the lookout of <paramref name="group"/>'s tower can see coming, if anything.</summary>
    private string? SightedTrouble(KinGroup group, Shelter tower)
    {
        float rangeSquared = TowerSightRange * TowerSightRange;
        if (Spider is { IsDead: false } spider &&
            GroundMover.HorizontalDistanceSquared(spider.Position, tower.Position) <= rangeSquared)
            return "the Wolf Spider";
        foreach (Hornet hornet in Hornets)
        {
            if (!hornet.IsDead && hornet.IsChasing &&
                GroundMover.HorizontalDistanceSquared(hornet.Position, tower.Position) <= rangeSquared)
                return "a Hornet swarm";
        }
        foreach (Bramblekin other in QueryColonyWithin(tower.Position, TowerSightRange))
        {
            if (!other.IsDead && other.GroupId is { } id && id != group.Id && AreAtWar(group.Id, id) &&
                GroundMover.HorizontalDistanceSquared(other.Position, tower.Position) <= rangeSquared)
                return "raiders";
        }
        return null;
    }
}
