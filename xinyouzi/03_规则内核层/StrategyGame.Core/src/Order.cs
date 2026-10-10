// ============================================================================
//  Order 的延迟执行辅助
// ============================================================================
//  【不重新声明任何契约类型】
//  Order 及其 CreatePlayerOrder / CreateOfficerOrder / CreateAiOrder
//  三个工厂方法均定义在 StrategyGame.Contracts，此处只做只读辅助。
//
//  【字段结构完全相同】这是"AI 与玩家同路"的代码基础：
//  无论 Player / Officer / AiFaction，产出的 Order 结构完全一致，
//  Resolver 因此不需要区分来源，只校验资源与目标。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core;

/// <summary>
/// <see cref="Order"/> 与 <see cref="PendingOrder"/> 的辅助方法。
/// </summary>
public static class OrderExtensions
{
    /// <summary>
    /// 判断该指令是否为「延迟指令」（需要在指定年月执行）。
    /// </summary>
    /// <param name="order">指令。只读。</param>
    /// <returns>
    /// 当 <see cref="Order.ExecuteYear"/> 与 <see cref="Order.ExecuteMonth"/>
    /// 均为正数（即指定了一个具体时点）时返回 true；否则视为即时执行，返回 false。
    /// </returns>
    public static bool IsDelayed(this Order order)
        => order.ExecuteYear > 0 && order.ExecuteMonth > 0;

    /// <summary>
    /// 判断延迟指令是否在给定年月到期。
    /// </summary>
    /// <param name="order">指令。只读。</param>
    /// <param name="year">当前年份。</param>
    /// <param name="month">当前月份（1-12）。</param>
    /// <returns>到期返回 true。</returns>
    public static bool IsDueIn(this Order order, int year, int month)
        => order.IsDelayed()
           && order.ExecuteYear == year
           && order.ExecuteMonth == month;

    /// <summary>
    /// 把 <see cref="PendingOrder"/> 转换为可交给结算器的 <see cref="Order"/>。
    /// </summary>
    /// <param name="pending">队列中的延迟指令。只读。</param>
    /// <returns>结构等价的即时指令。</returns>
    /// <remarks>
    /// 转换只做字段搬运，不修改 pending。<see cref="PendingOrder"/> 与
    /// <see cref="Order"/> 字段一一对应，因此这是无损转换。
    /// </remarks>
    public static Order ToOrder(this PendingOrder pending)
        => new()
        {
            OrderId = pending.OrderId,
            ActorType = pending.ActorType,
            ActorId = pending.ActorId,
            Action = pending.Action,
            TargetId = pending.TargetId,
            SecondaryTargetId = pending.SecondaryTargetId,
            Amount = pending.Amount,
            ExecuteYear = pending.ExecuteYear,
            ExecuteMonth = pending.ExecuteMonth,
        };

    /// <summary>
    /// 把即时 <see cref="Order"/> 转换为可入队的 <see cref="PendingOrder"/>。
    /// </summary>
    /// <param name="order">即时指令。只读。</param>
    /// <returns>可入队的延迟指令。</returns>
    public static PendingOrder ToPendingOrder(this Order order)
        => new()
        {
            OrderId = order.OrderId,
            ActorType = order.ActorType,
            ActorId = order.ActorId,
            Action = order.Action,
            TargetId = order.TargetId,
            SecondaryTargetId = order.SecondaryTargetId,
            Amount = order.Amount,
            ExecuteYear = order.ExecuteYear,
            ExecuteMonth = order.ExecuteMonth,
        };
}
