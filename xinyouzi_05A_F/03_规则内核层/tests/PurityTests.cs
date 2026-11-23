// ============================================================================
//  纯净性测试—— Resolver 必须是纯函数
// ============================================================================
//  这是全项目最重要的架构约束。无头模拟、回放、存档全部依赖它。
//  验证方式：调用 Resolve 前后对state 做逐字段快照对比，必须完全相同。
//
//  【为什么逐字段而不是引用比较】Resolve 返回的 Delta 若被误写入 state，
//  引用不变但值变了。只有逐字段展开才能发现这类污染。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Resolvers;
using StrategyGame.Core.Sample;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>
/// 六个 Resolver 的纯净性验证。
/// </summary>
public sealed class PurityTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>收集六个结算器的实例。</summary>
    /// <returns>六个原型结算器。</returns>
    private static IResolver[] BuildAllResolvers()
        => new IResolver[]
        {
            new RecruitResolver(Config),
            new FortifyResolver(Config),
            new DevelopResolver(Config),
            new DispatchResolver(Config),
            new AssignPostResolver(Config),
            new RestResolver(Config),
        };

    /// <summary>各结算器对应的可成功执行的指令。</summary>
    /// <param name="state">世界状态。</param>
    /// <returns>与结算器一一对应的指令列表。</returns>
    private static List<Order> BuildSuccessOrders(WorldState state)
        => new()
        {
            // 征兵：玩家在洛阳（城 0）征 50 人
            Order.CreatePlayerOrder(ActionType.Recruit, target: 0, amount: 50),

            // 修城防：玩家在洛阳投 100 银
            Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 100),

            // 屯田：玩家在洛阳投 100 银
            Order.CreatePlayerOrder(ActionType.Develop, target: 0, amount: 100),

            // 出兵：玩家从洛阳（0）出兵至长安（1），两城相邻
            Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 50, secondary: 0),

            // 任命人才：张良（0）任命到洛阳（0）
            Order.CreatePlayerOrder(ActionType.AssignPost, target: 0, secondary: 0),

            // 休整：玩家部队 0 休整
            Order.CreatePlayerOrder(ActionType.Rest, target: 0),
        };

    /// <summary>每个结算器调用 Resolve 前后，state 必须逐字段不变。</summary>
    [Fact]
    public void Resolve_AllResolvers_ShouldNotMutateState()
    {
        var state = SampleData.CreateWorld();
        var pristine = state.Clone();

        var resolvers = BuildAllResolvers();
        var orders = BuildSuccessOrders(state);

        for (int i = 0; i < resolvers.Length; i++)
        {
            var resolver = resolvers[i];
            var order = orders[i];

            // 先确认这条指令确实会成功——若被拒绝则本测试失去意义。
            Assert.Equal(ResolveResultCode.Success, resolver.Validate(state, order));

            var result = resolver.Resolve(state, order);

            Assert.True(result.IsSuccess, $"{resolver.Action} 应执行成功，实际: {result.Message}");
            Assert.NotEmpty(result.Deltas);
        }

        // 与一份从未被碰过的原始副本逐字段比对。
        string? diff = StateSnapshot.Diff(pristine, state);
        Assert.True(diff == null, $"Resolve 调用后 state 被修改了。\n{diff}");
    }

    /// <summary>Validate 是纯函数：调用前后 state 不变，且可重复调用。</summary>
    [Fact]
    public void Validate_ShouldBeIdempotent_AndNotMutateState()
    {
        var state = SampleData.CreateWorld();
        string before = StateSnapshot.Capture(state);

        var resolvers = BuildAllResolvers();
        var orders = BuildSuccessOrders(state);

        for (int i = 0; i < resolvers.Length; i++)
        {
            // 连续两次校验必须得到相同结果，且不改变状态。
            ResolveResultCode first = resolvers[i].Validate(state, orders[i]);
            ResolveResultCode second = resolvers[i].Validate(state, orders[i]);

            Assert.Equal(first, second);
        }

        Assert.Equal(before, StateSnapshot.Capture(state));
    }

    /// <summary>被拒绝的指令同样不得修改 state。</summary>
    [Fact]
    public void Resolve_RejectedOrder_ShouldNotMutateState()
    {
        var state = SampleData.CreateWorld();
        string before = StateSnapshot.Capture(state);

        var resolver = new RecruitResolver(Config);

        // 目标城池不存在 → 必然被拒绝
        var order = Order.CreatePlayerOrder(ActionType.Recruit, target: 999, amount: 50);
        var result = resolver.Resolve(state, order);

        Assert.False(result.IsSuccess);
        Assert.Equal(before, StateSnapshot.Capture(state));
    }

    /// <summary>反复调用 Resolve 不得累积任何状态变化。</summary>
    /// <remarks>
    /// 若某处不慎把 Delta 写回了 state，第一次调用后状态就变了，
    /// 第二次的结果会与第一次不同。这个测试能抓住那类隐蔽污染。
    /// </remarks>
    [Fact]
    public void Resolve_RepeatedCalls_ShouldProduceIdenticalResults()
    {
        var state = SampleData.CreateWorld();
        var resolver = new FortifyResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 100);

        var first = resolver.Resolve(state, order);
        var second = resolver.Resolve(state, order);

        Assert.Equal(first.Deltas.Count, second.Deltas.Count);
        for (int i = 0; i < first.Deltas.Count; i++)
        {
            Assert.Equal(first.Deltas[i].TargetType, second.Deltas[i].TargetType);
            Assert.Equal(first.Deltas[i].TargetId, second.Deltas[i].TargetId);
            Assert.Equal(first.Deltas[i].Field, second.Deltas[i].Field);
            Assert.Equal(first.Deltas[i].OldValue, second.Deltas[i].OldValue);
            Assert.Equal(first.Deltas[i].NewValue, second.Deltas[i].NewValue);
        }
    }

    /// <summary>StateApplier 是唯一写回通道：Delta 写回后状态确实改变。</summary>
    /// <remarks>
    /// 这条测试与上面的纯净性测试互为对照：
    /// 纯函数测试证明「不写」，本测试证明「能写」。
    /// 两者同时成立，才说明写回被正确隔离在 Applier 中。
    /// </remarks>
    [Fact]
    public void StateApplier_ShouldBeTheChannel_ThatActuallyMutates()
    {
        var state = SampleData.CreateWorld();
        var resolver = new FortifyResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 100);

        var result = resolver.Resolve(state, order);
        Assert.Equal(state.GetCity(0)!.Defense, result.Deltas[1].OldValue);

        // 写回前状态不变
        Assert.Equal(100, state.GetCity(0)!.Defense);

        new StateApplier().Apply(state, result.Deltas);

        // 写回后状态改变，且等于 Delta 的 NewValue
        Assert.Equal(result.Deltas[1].NewValue, state.GetCity(0)!.Defense);
    }
}
