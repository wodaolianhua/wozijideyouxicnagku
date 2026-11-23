// ============================================================================
//  StrategyGame.AI —— 人才政策实现
//  铁律：只读 state，产出 Order/EventRecord，绝不修改 WorldState
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.AI.Personnel;

/// <summary>
/// 选才政策。岗位由结算端按专长强制推导，本层只决定"用谁"：
/// General 岗取 Command 最高，其余岗位一律取 Governance 最高（与专长无关）。
/// </summary>
public sealed class BestFitPolicy : IPersonnelPolicy
{
    private readonly BalanceConfig _config;
    private readonly IRandom _random;

    public BestFitPolicy(BalanceConfig config, IRandom random)
    {
        _config = config;
        _random = random;
    }

    /// <summary>
    /// 为指定岗位选出最佳人才。General→Command 最高；其余非 None 岗位→Governance 最高。
    /// 只从本势力、存活、当前未任职（Post == None）的人才中选。
    /// 最高分并列时在并列集合中随机选 1。
    /// </summary>
    public Officer? SelectBestFor(WorldState state, int factionId, PostType post)
        => SelectBestFor(state, factionId, post, excludedOfficerIds: null);

    /// <summary>
    /// 非接口重载：在候选过滤中排除指定 Id（用于 Planner 防止同一人在一个 Plan 里被派到两座城）。
    /// </summary>
    public Officer? SelectBestFor(WorldState state, int factionId, PostType post, HashSet<int>? excludedOfficerIds)
    {
        if (post == PostType.None) return null;

        var candidates = state.Officers
            .Where(o => o.OwnerFactionId == factionId && o.IsAlive && o.Post == PostType.None
                     && (excludedOfficerIds == null || !excludedOfficerIds.Contains(o.Id)))
            .ToList();

        if (candidates.Count == 0) return null;

        // General 取 Command，其余取 Governance
        Func<Officer, int> keySelector = post == PostType.General
            ? static o => o.Command
            : static o => o.Governance;

        int top = candidates.Max(keySelector);
        var tied = candidates.Where(o => keySelector(o) == top).ToList();
        return tied[_random.Next(tied.Count)];
    }

    /// <summary>选出最佳将领：Command 最高者（General 岗）。</summary>
    public Officer? SelectBestGeneralFor(WorldState state, int factionId)
        => SelectBestFor(state, factionId, PostType.General);

    /// <summary>
    /// 忠诚度检查（风险预警语义，不执行叛离）。
    /// Loyalty < LoyaltyThreshold 时按概率触发"叛离风险"事件，仅预警，不修改 state。
    /// </summary>
    public List<EventRecord> CheckLoyalty(WorldState state, int factionId)
    {
        var events = new List<EventRecord>();
        int threshold = _config.LoyaltyThreshold;

        foreach (var o in state.Officers.Where(o =>
                     o.OwnerFactionId == factionId && o.IsAlive && o.Loyalty < threshold))
        {
            // 风险概率：阈值以下越低越容易预警。(threshold - loyalty) / threshold
            int diff = threshold - o.Loyalty;
            double p = Math.Min(1.0, diff / (double)Math.Max(1, threshold));
            if (_random.NextDouble() < p)
            {
                events.Add(new EventRecord
                {
                    Year = state.Year,
                    Month = state.Month,
                    Category = EventCategory.Personnel,
                    EventKey = "officer_defection_risk",
                    Message = $"{o.Name}（忠诚{o.Loyalty}）有叛离风险，宜尽早安排职事。",
                    FactionId = factionId
                });
            }
        }

        return events;
    }
}
