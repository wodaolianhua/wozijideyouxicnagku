using StrategyGame.Contracts;
using StrategyGame.Core.Internal;

namespace StrategyGame.Core;

/// <summary>
/// 遭遇检测（快照式）：在给定世界状态的当前时刻，找出所有进入接战距离的敌对部队对。
/// Advance/FastForward 内部的逐跳扫描不经过这里（它需要单步轨迹），但判定规则一致。
/// </summary>
public sealed class EncounterDetector : IEncounterDetector
{
    private readonly ILossResolver _lossResolver;
    private readonly double _engagementDistance;

    public EncounterDetector(
        ILossResolver? lossResolver = null,
        double engagementDistance = MarchRules.DefaultEngagementDistance)
    {
        _lossResolver = lossResolver ?? new LossResolver();
        _engagementDistance = Math.Max(0.0, engagementDistance);
    }

    public List<EncounterEvent> Detect(WorldState state, int seed)
    {
        ArgumentNullException.ThrowIfNull(state);

        var result = new List<EncounterEvent>();
        var armies = state.Armies;

        for (var i = 0; i < armies.Count; i++)
        {
            for (var j = i + 1; j < armies.Count; j++)
            {
                var a = armies[i];
                var b = armies[j];
                if (!MarchRules.AreHostile(a.Faction, b.Faction))
                {
                    continue;
                }

                var (ax, ay) = MarchMath.PositionOf(state.Map, a);
                var (bx, by) = MarchMath.PositionOf(state.Map, b);
                var dx = ax - bx;
                var dy = ay - by;
                if (dx * dx + dy * dy > _engagementDistance * _engagementDistance + 1e-12)
                {
                    continue;
                }

                var encounter = EncounterRules.Create(
                    state.Map,
                    state.Time,
                    a,
                    b,
                    (ax + bx) / 2.0,
                    (ay + by) / 2.0,
                    !MarchMath.IsIdle(a),
                    !MarchMath.IsIdle(b));

                encounter.Loss = _lossResolver.Resolve(encounter, state, seed);
                result.Add(encounter);
            }
        }

        return result
            .OrderBy(e => e.Time)
            .ThenBy(e => e.NodeId, StringComparer.Ordinal)
            .ThenBy(e => string.Join("|", e.ArmyIds), StringComparer.Ordinal)
            .ToList();
    }
}
