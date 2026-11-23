// ============================================================================
//  时间推进
// ============================================================================
//  【职责】把游戏时间往前推，并处理「到月」「到年」的边界。
//  【纯度】GameClock 修改的是 WorldState 的时间与计数字段，
//  这属于调度职责而非玩法逻辑，因此不经过 StateDelta。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core;

/// <summary>
/// 游戏时钟。负责年/月推进与回合边界处理。
/// </summary>
/// <remarks>
/// 【为什么时间不走 StateDelta】StateDelta 用于表达「玩法造成的变化」。
/// 时间推进是调度行为，每回合必然发生且与指令无关，
/// 走Delta 只会污染回放日志。两者职责必须分开。
/// </remarks>
public sealed class GameClock
{
    /// <summary>推进一个月。</summary>
    /// <param name="state">世界状态。将修改其 Year / Month / ActionsThisMonthByFaction。</param>
    /// <returns>本次推进后是否跨年（12 月 → 1 月）。</returns>
    /// <remarks>
    /// 月 +1；超过 12 则年 +1、月归 1。同时把本月已执行指令数清零（契约 v1.5：
    /// 按势力分账的字典整体重建），因为人力上限是按月计算的。
    /// </remarks>
    public static bool AdvanceMonth(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        bool crossedYear = false;

        state.Month++;
        if (state.Month > TimeUtil.MonthsPerYear)
        {
            state.Month = 1;
            state.Year++;
            crossedYear = true;
        }

        // 新的一月，人力重新满额。
        // 【契约 v1.5】额度按势力分账（WorldState.ActionsThisMonthByFaction），
        // 月初直接重建空字典：本方法在第 1 步执行，Factions 在建局时已固定、
        // 月内不会增删，空字典对任何势力都读到 0，与逐键置 0 等价。
        state.ActionsThisMonthByFaction = new Dictionary<int, int>();

        return crossedYear;
    }

    /// <summary>把年+月转为可比较的单调整数。</summary>
    /// <param name="year">年份。</param>
    /// <param name="month">月份（1-12）。</param>
    /// <returns>单调整数，供指令执行时间排序。</returns>
    /// <remarks>直接转发契约的 <see cref="TimeUtil.TotalMonths"/>，避免两套换算。</remarks>
    public static int TotalMonths(int year, int month) => TimeUtil.TotalMonths(year, month);

    /// <summary>把单调整数还原为年+月。</summary>
    /// <param name="totalMonths">单调整数。</param>
    /// <returns>年与月的元组。</returns>
    public static (int Year, int Month) FromTotalMonths(int totalMonths)
        => TimeUtil.FromTotalMonths(totalMonths);

    /// <summary>取本回合的到期延迟指令。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>本年本月到期的延迟指令副本列表。</returns>
    /// <remarks>
    /// 【为什么返回副本】原队列会被流水线移除，若直接返回原对象引用，
    /// 调用方一旦修改就会污染队列。这里用 <see cref="OrderExtensions.ToOrder"/>
    /// 生成等价副本，队列本身保持只读语义。
    /// </remarks>
    public static List<Order> GetDueOrders(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var due = new List<Order>();
        foreach (var pending in state.PendingOrders)
        {
            if (pending.ExecuteYear == state.Year && pending.ExecuteMonth == state.Month)
            {
                due.Add(pending.ToOrder());
            }
        }

        return due;
    }

    /// <summary>移除已到期的延迟指令。</summary>
    /// <param name="state">世界状态。将修改其 PendingOrders。</param>
    /// <returns>被移除的指令数量。</returns>
    public static int RemoveDueOrders(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.PendingOrders.RemoveAll(p =>
            p.ExecuteYear == state.Year && p.ExecuteMonth == state.Month);
    }
}
