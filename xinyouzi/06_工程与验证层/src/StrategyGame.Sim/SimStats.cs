// ============================================================================
//  模拟统计累加器 —— 逐月采样，供平衡分析与 CSV 输出
//  【为什么单独一个类】统计必须与模拟过程解耦：
//  统计只读不改模拟状态，且可以被序列化进存档，使「读档继续跑」
//  与「一口气跑到底」得到完全相同的统计结果。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Sim;

/// <summary>
/// 单局（或跨局）的模拟统计。
/// </summary>
public sealed class SimStats
{
    /// <summary>各行动类型的发起数（含被拒绝的尝试）。</summary>
    public Dictionary<ActionType, int> OrdersByAction { get; set; } = new();

    /// <summary>各类结算结果码的计数（不分行动类型）。</summary>
    public Dictionary<ResolveResultCode, int> ResultCodes { get; set; } = new();

    /// <summary>按行动类型分列的拒绝码计数。键为行动类型，值为「拒绝码 → 次数」。</summary>
    public Dictionary<ActionType, Dictionary<ResolveResultCode, int>> RejectsByAction { get; set; } = new();

    /// <summary>事件总数。</summary>
    public int EventCount { get; set; }

    /// <summary>哗变事件次数。</summary>
    public int MutinyCount { get; set; }

    /// <summary>攻陷事件次数。</summary>
    public int CityCapturedCount { get; set; }

    /// <summary>各势力兵源存量（Resources.Soldier）的累计值。</summary>
    public Dictionary<int, long> SoldierStockSumByFaction { get; set; } = new();

    /// <summary>各势力兵源存量的采样次数。</summary>
    public Dictionary<int, int> SoldierStockSamplesByFaction { get; set; } = new();

    /// <summary>部队士气样本累计值。</summary>
    public long ArmyMoraleSum { get; set; }

    /// <summary>部队士气样本数。</summary>
    public int ArmyMoraleSamples { get; set; }

    /// <summary>士气低于 30 的样本数。</summary>
    public int LowMoraleSamples { get; set; }

    /// <summary>AI 产出的指令总数（各行动发起数之和）。</summary>
    public int AiOrderTotal
    {
        get
        {
            int total = 0;
            foreach (var count in OrdersByAction.Values)
            {
                total += count;
            }

            return total;
        }
    }

    /// <summary>记录一次行动发起。</summary>
    /// <param name="action">行动类型。</param>
    public void AddOrder(ActionType action)
    {
        OrdersByAction.TryGetValue(action, out int v);
        OrdersByAction[action] = v + 1;
    }

    /// <summary>记录一次结算结果。</summary>
    /// <param name="code">结算结果码。</param>
    public void AddResult(ResolveResultCode code)
    {
        ResultCodes.TryGetValue(code, out int v);
        ResultCodes[code] = v + 1;
    }

    /// <summary>记录一次拒绝，并归入对应的行动类型。</summary>
    /// <param name="action">行动类型。</param>
    /// <param name="code">拒绝码。</param>
    public void AddReject(ActionType action, ResolveResultCode code)
    {
        if (!RejectsByAction.TryGetValue(action, out var map))
        {
            map = new Dictionary<ResolveResultCode, int>();
            RejectsByAction[action] = map;
        }

        map.TryGetValue(code, out int v);
        map[code] = v + 1;
    }

    /// <summary>采样本月的势力兵源存量与部队士气。</summary>
    /// <param name="state">世界状态。只读。</param>
    public void SampleMonth(WorldState state)
    {
        foreach (var faction in state.Factions)
        {
            SoldierStockSumByFaction.TryGetValue(faction.Id, out long sum);
            SoldierStockSamplesByFaction.TryGetValue(faction.Id, out int samples);
            SoldierStockSumByFaction[faction.Id] = sum + faction.Resources.Soldier;
            SoldierStockSamplesByFaction[faction.Id] = samples + 1;
        }

        foreach (var army in state.Armies)
        {
            ArmyMoraleSum += army.Morale;
            ArmyMoraleSamples++;
            if (army.Morale < 30)
            {
                LowMoraleSamples++;
            }
        }
    }

    /// <summary>取某势力的月均兵源存量。无采样时返回 0。</summary>
    /// <param name="factionId">势力 Id。</param>
    /// <returns>月均兵源存量。</returns>
    public int AverageSoldierStock(int factionId)
    {
        if (!SoldierStockSumByFaction.TryGetValue(factionId, out long sum)
            || !SoldierStockSamplesByFaction.TryGetValue(factionId, out int samples)
            || samples == 0)
        {
            return 0;
        }

        return (int)(sum / samples);
    }

    /// <summary>部队士气样本均值。向下取整。</summary>
    public int AverageArmyMorale
        => ArmyMoraleSamples == 0 ? 0 : (int)(ArmyMoraleSum / ArmyMoraleSamples);

    /// <summary>低士气样本占比（万分比）。</summary>
    public int LowMoraleRatioPerMille
        => ArmyMoraleSamples == 0
            ? 0
            : (int)((long)LowMoraleSamples * 10_000 / ArmyMoraleSamples);
}
