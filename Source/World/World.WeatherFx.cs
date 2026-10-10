using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>How wet the ground is, 0..1: a storm soaks it quickly, then it dries over about a minute (drawing only — nothing in the simulation reads it).</summary>
    [NotSaved]
    private float _wetness;

    private const float PuddleCell = 6f;

    private static readonly Color LeafOrange = new(214, 118, 36, 255), LeafRed = new(176, 66, 38, 255), LeafGold = new(224, 178, 52, 255);

    /// <summary>A repeatable pseudo-random 0..1 for a grid cell, so puddles stay where they are.</summary>
    private static float CellHash(int x, int z, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 65535f;
    }

    /// <summary>Puddles after rain and drifting leaves in autumn, round the camera's focus (skipped in low detail).</summary>
    private void DrawWeatherFx(Camera3D camera)
    {
        if (LowDetail)
            return;
        _wetness = IsStorming || GoodRainLeft > 0f ? MathF.Min(1f, _wetness + Raylib.GetFrameTime() * 0.4f) : MathF.Max(0f, _wetness - Raylib.GetFrameTime() / 60f);
        float spread = MathF.Min(30f, Vector3.Distance(camera.Position, camera.Target) * 0.5f);
        int reach = (int)(spread / PuddleCell) + 1;
        int cx = (int)MathF.Floor(camera.Target.X / PuddleCell), cz = (int)MathF.Floor(camera.Target.Z / PuddleCell);

        if (_wetness > 0.02f)
        {
            var water = new Color((byte)110, (byte)140, (byte)175, (byte)(120 * _wetness));
            for (int gx = cx - reach; gx <= cx + reach; gx++)
            {
                for (int gz = cz - reach; gz <= cz + reach; gz++)
                {
                    if (CellHash(gx, gz, 1) > 0.3f)
                        continue;
                    float x = (gx + CellHash(gx, gz, 2)) * PuddleCell, z = (gz + CellHash(gx, gz, 3)) * PuddleCell;
                    if (IsBlocked(new Vector3(x, 0f, z), 0.4f) || IsWater(new Vector3(x, 0f, z)))
                        continue;
                    float radius = (0.5f + CellHash(gx, gz, 4) * 0.9f) * (0.4f + 0.6f * _wetness);
                    Raylib.DrawCylinder(new Vector3(x, GetHeightAt(x, z) + 0.015f, z), radius, radius, 0.01f, 14, water);
                }
            }
        }

        if (CurrentSeason == Season.Autumn || (CurrentSeason == Season.Winter && SeasonProgress < 0.2f))
        {
            float t = (float)Raylib.GetTime();
            for (int i = 0; i < 36; i++)
            {
                float phase = CellHash(i, 7, 5), fall = (t * (0.07f + 0.04f * CellHash(i, 7, 6)) + phase) % 1f;
                float x = camera.Target.X + (CellHash(i, 7, 8) * 2f - 1f) * spread + MathF.Sin(t * 0.8f + i) * 0.8f;
                float z = camera.Target.Z + (CellHash(i, 7, 9) * 2f - 1f) * spread + MathF.Cos(t * 0.6f + i * 2f) * 0.8f;
                float y = GetHeightAt(x, z) + 0.05f + (1f - fall) * 5f;
                Color leaf = i % 3 == 0 ? LeafOrange : i % 3 == 1 ? LeafRed : LeafGold;
                Raylib.DrawCube(new Vector3(x, y, z), 0.09f, 0.012f, 0.06f, leaf);
            }
        }
    }
}
