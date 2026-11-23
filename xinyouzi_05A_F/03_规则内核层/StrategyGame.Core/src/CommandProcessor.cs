// ============================================================================
//  指令批量处理
// ============================================================================
//  【职责】把一批 Order 依次交给注册表结算，并把 Delta 统一写回。
//  【关键】结算与写回分离：先全部结算拿到结果，再由 Applier 写回。
//  这样每条指令结算时看到的是同一份基准状态，
//  避免「第 1 条改完状态影响第 2 条校验」造成的顺序耦合。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core;

/// <summary>
/// 指令处理器。批量结算指令并统一写回状态。
/// </summary>
/// <remarks>
/// 【纯函数链路上的一环】本类调用 Resolver（纯函数）拿到 Delta，
/// 再通过 <see cref="IStateApplier"/> 写回。Resolver 本身永不接触写操作。
/// </remarks>
public sealed class CommandProcessor : ICommandProcessor
{
    private readonly IResolverRegistry _registry;
    private readonly IStateApplier _applier;

    /// <summary>创建指令处理器。</summary>
    /// <param name="registry">结算器注册表。</param>
    /// <param name="applier">状态写回器。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null 时抛出。</exception>
    public CommandProcessor(IResolverRegistry registry, IStateApplier applier)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
    }

    /// <summary>
    /// 处理单条指令。
    /// </summary>
    /// <param name="state">世界状态。</param>
    /// <param name="order">待处理指令。</param>
    /// <returns>结算结果。失败时携带明确拒绝码与中文原因。</returns>
    /// <remarks>
    /// 本方法只结算不写回（delta 留在结果里由调用方决定何时应用），
    /// 这样调用方可以在「全部成功」或「逐条应用」之间自行选择策略。
    /// </remarks>
    public ResolveResult Process(WorldState state, Order order)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(order);
        return _registry.Dispatch(state, order);
    }

    /// <summary>
    /// 批量处理指令。
    /// </summary>
    /// <param name="state">世界状态。</param>
    /// <param name="orders">待处理指令列表。</param>
    /// <returns>全部结算结果，顺序与传入一致。</returns>
    /// <remarks>
    /// 【写入策略】成功指令的 Delta 会被写回，失败指令的 Delta 为空
    /// 不产生任何写入。事件记录追加到 <see cref="WorldState.EventLog"/>。
    /// </remarks>
    public List<ResolveResult> ProcessAll(WorldState state, List<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(orders);

        var results = new List<ResolveResult>(orders.Count);

        foreach (var order in orders)
        {
            var result = Process(state, order);
            results.Add(result);

            if (result.IsSuccess && result.Deltas.Count > 0)
            {
                _applier.Apply(state, result.Deltas);
            }

            if (result.Events.Count > 0)
            {
                state.EventLog.AddRange(result.Events);
            }
        }

        return results;
    }

    /// <summary>
    /// 结算单条指令并立即写回。
    /// </summary>
    /// <param name="state">世界状态。</param>
    /// <param name="order">待处理指令。</param>
    /// <returns>结算结果。</returns>
    /// <remarks>
    /// 回合流水线的第 7 步使用本方法：延迟指令必须逐条结算并立即生效，
    /// 因为后一条指令的校验需要看到前一条的结果。
    /// </remarks>
    public ResolveResult ProcessAndApply(WorldState state, Order order)
    {
        var result = Process(state, order);

        if (result.IsSuccess && result.Deltas.Count > 0)
        {
            _applier.Apply(state, result.Deltas);
        }

        if (result.Events.Count > 0)
        {
            state.EventLog.AddRange(result.Events);
        }

        return result;
    }
}
