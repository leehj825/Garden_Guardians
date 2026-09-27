using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>At each Leader decision, a tradition grows this much from what the clan is doing…</summary>
    private const float TraditionGrowth = 0.01f;

    /// <summary>…and every tradition fades by this factor (half-life about two years of decisions).</summary>
    private const float TraditionFade = 0.997f;

    /// <summary>
    /// Clan culture, at each Leader decision: at war, raiding or defending
    /// home grows the Martial tradition; hunting big game, the Hunting one;
    /// tending bushes (in proportion to how many it keeps), the Farming one.
    /// Everything fades slowly. When a tradition first becomes what the clan
    /// is known for, the chronicle notes it.
    /// </summary>
    private void UpdateCulture(KinGroup group)
    {
        ClanCulture culture = group.Culture;
        Tradition before = culture.Leading;

        bool atWar = _relations.Any(r => r.Value.Stance == GroupStance.AtWar && (r.Key.Item1 == group.Id || r.Key.Item2 == group.Id));
        if (atWar || group.Goal is GroupGoal.Raid or GroupGoal.Defend)
            culture.Martial += TraditionGrowth;
        if (group.Goal == GroupGoal.Hunt)
            culture.Hunting += TraditionGrowth;
        int allowance = BushAllowance(group);
        if (allowance > 0)
            culture.Farming += TraditionGrowth * MathF.Min(1f, BushesOf(group) / (float)allowance);

        culture.Martial = Math.Clamp(culture.Martial * TraditionFade, 0f, 1f);
        culture.Hunting = Math.Clamp(culture.Hunting * TraditionFade, 0f, 1f);
        culture.Farming = Math.Clamp(culture.Farming * TraditionFade, 0f, 1f);

        if (culture.Leading != before && culture.Label is { } label)
        {
            Game.AddEventLog($"[CULTURE] {group.CapitalTitle} has become a {label} clan");
            Chronicle($"{group.CapitalTitle} became known as a {label} clan", group);
        }
    }
}
