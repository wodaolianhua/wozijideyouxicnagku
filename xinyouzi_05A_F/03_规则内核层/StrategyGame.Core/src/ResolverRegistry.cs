// ============================================================================
//  结算器注册表
// ============================================================================
//  【职责】ActionType → IResolver 的映射与分发。
//  【容错】未注册的 ActionType 必须返回拒绝码，绝不抛异常——
//  上层可能传入 P1/P2 保留行动类型，抛异常会让整个回合结算崩溃。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Resolvers;

namespace StrategyGame.Core;

/// <summary>
/// 结算器注册表。按 <see cref="ActionType"/> 查找并分发结算器。
/// </summary>
/// <remarks>
/// 【为什么用字典】分发是每条指令都要走的热路径，字典查找复杂度 O(1)。
/// 注册表在开局构建一次后全程只读，因此不做任何并发保护——
/// 若运行期需要动态注册，请改为 ConcurrentDictionary。
/// </remarks>
public sealed class ResolverRegistry : IResolverRegistry
{
    /// <summary>ActionType → 结算器映射。</summary>
    private readonly Dictionary<ActionType, IResolver> _resolvers = new();

    /// <summary>注册一个结算器。</summary>
    /// <param name="action">行动类型。同名重复注册会覆盖旧值。</param>
    /// <param name="resolver">结算器实例。</param>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 为 null 时抛出。</exception>
    public void Register(ActionType action, IResolver resolver)
    {
        if (resolver == null)
        {
            throw new ArgumentNullException(nameof(resolver));
        }

        _resolvers[action] = resolver;
    }

    /// <summary>查找结算器。未注册时返回 null，不抛异常。</summary>
    /// <param name="action">行动类型。</param>
    /// <returns>对应结算器；未注册时返回 null。</returns>
    public IResolver? Get(ActionType action)
        => _resolvers.TryGetValue(action, out var resolver) ? resolver : null;

    /// <summary>分发指令。</summary>
    /// <param name="state">世界状态。只读——本方法及其调用的结算器都不修改它。</param>
    /// <param name="order">待结算指令。</param>
    /// <returns>
    /// 已注册则返回结算结果；未注册返回
    /// <see cref="ResolveResultCode.RejectedNotImplemented"/>，绝不抛异常。
    /// </returns>
    /// <remarks>
    /// 【与任务书的差异】任务书写「未注册返回 Rejected_InvalidTarget」，
    /// 但契约 <c>IResolverRegistry.Dispatch</c> 的注释与
    /// <c>RejectedNotImplemented</c> 枚举的语义（「该 ActionType 尚未实现」）
    /// 都指向 RejectedNotImplemented。契约是唯一真相来源，故从契约。
    /// </remarks>
    public ResolveResult Dispatch(WorldState state, Order order)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(order);

        var resolver = Get(order.Action);
        if (resolver == null)
        {
            return ResolveResult.Fail(
                ResolveResultCode.RejectedNotImplemented,
                $"行动类型 {order.Action} 尚未实现。");
        }

        return resolver.Resolve(state, order);
    }

    /// <summary>
    /// 创建一个已注册全部六个原型结算器的注册表。
    /// </summary>
    /// <param name="config">数值配置。所有结算器共享同一份配置。</param>
    /// <param name="random">可播种随机源。所有结算器共享同一实例，保证随机序列确定。</param>
    /// <returns>注册完毕的注册表。</returns>
    /// <remarks>
    /// 【随机源共享的必要性】若每个结算器各持一个随机源，
    /// 结算顺序会改变随机序列，破坏无头模拟的确定性。因此统一注入。
    /// </remarks>
    public static ResolverRegistry CreateDefault(BalanceConfig config, IRandom? random = null)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var registry = new ResolverRegistry();
        registry.Register(ActionType.Recruit, new RecruitResolver(config, random));
        registry.Register(ActionType.Fortify, new FortifyResolver(config, random));
        registry.Register(ActionType.Develop, new DevelopResolver(config, random));
        registry.Register(ActionType.Dispatch, new DispatchResolver(config, random));
        registry.Register(ActionType.AssignPost, new AssignPostResolver(config, random));
        registry.Register(ActionType.Rest, new RestResolver(config, random));
        return registry;
    }
}
