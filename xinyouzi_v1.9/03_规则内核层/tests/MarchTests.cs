// ============================================================================
//  长途行军测试（第 8 轮）
// ============================================================================
//  覆盖：路径服务（BFS）、玩家非相邻出兵不再被拒、粮草校验、
//  行军中再下令拦截、2 跳端到端（逐月 tick 推进 → 到达触发既有攻城结算）、
//  友好到达、存档往返与旧存档兼容。
//
//  【第8轮口径】仅玩家（ActorType.Player）可长途行军；AI 保留相邻约束
//  （回归用例在 ResolverTests.Resolve_AiToNonAdjacentCity_ShouldRejectWithNotAdjacent）。
// ============================================================================

using System.Text.Json;
using StrategyGame.Contracts;
using StrategyGame.Core.Pathfinding;
using StrategyGame.Core.Sample;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>
/// 长途行军端到端测试。
/// </summary>
public sealed class MarchTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>构造不带 AI、不带事件的处理器与流水线。</summary>
    /// <param name="state">世界状态（输出参数）。</param>
    /// <returns>处理器与流水线。</returns>
    private static (CommandProcessor Processor, TurnPipeline Pipeline, WorldState State) Setup()
    {
        var state = SampleData.CreateWorld();
        var applier = new StateApplier();
        var registry = ResolverRegistry.CreateDefault(Config, null);
        var processor = new CommandProcessor(registry, applier);
        var pipeline = new TurnPipeline(processor, applier, Config);
        return (processor, pipeline, state);
    }

    // ========================================================================
    //  路径服务
    // ========================================================================

    /// <summary>BFS 最短路径：洛阳（0）→ 邯郸（2）应为 0-1-2，2 跳。</summary>
    [Fact]
    public void Pathfinder_ShouldReturnShortestPathWithHops()
    {
        var state = SampleData.CreateWorld();

        var path = MapPathfinder.FindPath(state, 0, 2);

        Assert.True(path.Found);
        Assert.Equal(2, path.Hops);
        Assert.Equal(new List<int> { 0, 1, 2 }, path.Path);

        // 同城路径：0 跳。
        var same = MapPathfinder.FindPath(state, 0, 0);
        Assert.True(same.Found);
        Assert.Equal(0, same.Hops);
        Assert.Equal(new List<int> { 0 }, same.Path);
    }

    /// <summary>无连通路径：返回未找到，且出兵被 RejectedNoPath 拒绝。</summary>
    [Fact]
    public void Pathfinder_NoConnection_ShouldReportNotFound()
    {
        var state = SampleData.CreateWorld();

        // 一座与全图不相邻的孤城。
        state.Cities.Add(new City
        {
            Id = 99,
            Name = "孤岛",
            OwnerFactionId = 2,
            Population = 1000,
            MaxPopulation = 2000,
            Defense = 50,
            MaxDefense = 100,
            AdjacentCityIds = new List<int>(),
        });

        var path = MapPathfinder.FindPath(state, 0, 99);
        Assert.False(path.Found);
        Assert.Equal(-1, path.Hops);

        var applier = new StateApplier();
        var processor = new CommandProcessor(
            ResolverRegistry.CreateDefault(Config, null), applier);

        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 99, amount: 50, secondary: 0);
        var result = processor.ProcessAndApply(state, order);

        Assert.Equal(ResolveResultCode.RejectedNoPath, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    // ========================================================================
    //  开拔校验
    // ========================================================================

    /// <summary>粮草不足：长途行军被 RejectedNoResources 拒绝。</summary>
    [Fact]
    public void March_InsufficientGrain_ShouldRejectWithNoResources()
    {
        var (processor, _, state) = Setup();
        state.GetArmy(0)!.Soldiers = 1000;
        state.GetFaction(0)!.Resources.Grain = 1; // 需要 1000×2×200/10000 = 40

        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 2, amount: 1000, secondary: 0);
        var result = processor.ProcessAndApply(state, order);

        Assert.Equal(ResolveResultCode.RejectedNoResources, result.Code);
        Assert.Equal(-1, state.GetArmy(0)!.MarchTargetCityId); // 状态未被改动
    }

    /// <summary>行军中的部队不可再下 Dispatch（同一月连点也被拦截）。</summary>
    [Fact]
    public void March_RedispatchWhileMarching_ShouldRejectWithArmyMarching()
    {
        var (processor, _, state) = Setup();

        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 2, amount: 50, secondary: 0);
        Assert.Equal(ResolveResultCode.Success, processor.ProcessAndApply(state, order).Code);

        // 开拔月部队仍在出发城，但已挂行军标记 → 再次下令必须被拦。
        var again = Order.CreatePlayerOrder(ActionType.Dispatch, target: 3, amount: 50, secondary: 0);
        var result = processor.ProcessAndApply(state, again);

        Assert.Equal(ResolveResultCode.RejectedArmyMarching, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    // ========================================================================
    //  端到端：2 跳行军 → 到达攻城
    // ========================================================================

    /// <summary>
    /// 2 跳端到端：开拔（1年1月）→ 次月途经长安（1）→ 到达月（1年3月）
    /// 触发既有攻城结算并攻陷邯郸（2）。中途 PositionCityId/行军状态正确。
    /// </summary>
    [Fact]
    public void March_TwoHops_ArrivesAndCapturesOnArrivalMonth()
    {
        var (processor, pipeline, state) = Setup();

        // 压倒性兵力保证到达即攻陷：攻方 3000 vs 守方 100（守军每月还会获得兵源补给，
        // 一次战斗必须能打光守军；双方无统帅 → 统帅比取中性值 0.5）。
        var attacker = state.GetArmy(0)!;
        attacker.Soldiers = 3000;
        attacker.OfficerId = -1;
        var defender = state.GetArmy(1)!;
        defender.Soldiers = 100;
        defender.OfficerId = -1;

        // 玩家自洛阳（0）远征邯郸（2）：路径 0-1-2，2 跳。
        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 2, amount: 3000, secondary: 0);
        var dispatch = processor.ProcessAndApply(state, order);

        Assert.Equal(ResolveResultCode.Success, dispatch.Code);

        var army = state.GetArmy(0)!;
        Assert.Equal(0, army.PositionCityId); // 开拔月仍在出发城
        Assert.Equal(2, army.MarchTargetCityId);
        Assert.Equal(ArmyStance.Moving, army.Stance);
        Assert.Equal(new List<int> { 0, 1, 2 }, army.MarchPathCityIds);
        // 开拔月 1年1月 + 2 跳 × 1 月/跳 → 到达 1年3月。
        Assert.Equal(1, army.MarchArriveYear);
        Assert.Equal(3, army.MarchArriveMonth);
        // 粮草 = 3000 × 2 × 200/10000 = 120。
        Assert.Equal(5000 - 120, state.GetFaction(0)!.Resources.Grain);

        // ---- 第 1 个月 tick（→ 1年2月）：行进到途经城长安（1），未到达 ----
        pipeline.RunMonth(state);
        army = state.GetArmy(0)!;
        Assert.Equal(1, army.PositionCityId);
        Assert.Equal(2, army.MarchTargetCityId);
        Assert.Equal(ArmyStance.Moving, army.Stance);
        Assert.Equal(1, state.GetCity(1)!.OwnerFactionId); // 路过不占城

        // ---- 第 2 个月 tick（→ 1年3月）：到达，触发既有攻城结算 → 攻陷 ----
        int logStart = state.EventLog.Count;
        pipeline.RunMonth(state);
        army = state.GetArmy(0)!;
        Assert.Equal(0, state.GetCity(2)!.OwnerFactionId); // 邯郸易主
        Assert.Equal(2, army.PositionCityId);              // 攻方进驻
        Assert.Equal(-1, army.MarchTargetCityId);          // 行军状态清空
        Assert.Equal(0, army.MarchArriveYear);
        Assert.Equal(0, army.MarchArriveMonth);
        Assert.Empty(army.MarchPathCityIds);
        Assert.Equal(ArmyStance.Idle, army.Stance);
        Assert.Contains(state.EventLog.Skip(logStart), e => e.EventKey == EventKeys.CityCaptured);
    }

    /// <summary>到达时目标城已是己方城：和平进驻，不发生战斗。</summary>
    [Fact]
    public void March_ArriveAtOwnCity_ShouldEnterPeacefully()
    {
        var (processor, pipeline, state) = Setup();

        // 把邯郸（2）改划给己方，构造一条 2 跳的「自家城」行军。
        state.GetCity(2)!.OwnerFactionId = 0;

        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 2, amount: 100, secondary: 0);
        Assert.Equal(ResolveResultCode.Success, processor.ProcessAndApply(state, order).Code);

        pipeline.RunMonth(state); // 1年2月：途经长安
        pipeline.RunMonth(state); // 1年3月：到达己方城

        var army = state.GetArmy(0)!;
        Assert.Equal(2, army.PositionCityId);
        Assert.Equal(-1, army.MarchTargetCityId);
        Assert.Equal(ArmyStance.Idle, army.Stance);
        Assert.Equal(0, state.GetCity(2)!.OwnerFactionId);
    }

    // ========================================================================
    //  存档往返
    // ========================================================================

    /// <summary>
    /// 行军字段可序列化/反序列化（存档往返）；旧存档缺字段按「不在行军中」处理。
    /// </summary>
    [Fact]
    public void March_SaveRoundTrip_PreservesFields_AndOldSavesDefaultToNotMarching()
    {
        var (processor, _, state) = Setup();
        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 2, amount: 100, secondary: 0);
        processor.ProcessAndApply(state, order);

        var json = JsonSerializer.Serialize(state, WireFormat.Options);
        var restored = JsonSerializer.Deserialize<WorldState>(json, WireFormat.Options)!;

        var army = restored.GetArmy(0)!;
        Assert.Equal(2, army.MarchTargetCityId);
        Assert.Equal(1, army.MarchArriveYear);
        Assert.Equal(3, army.MarchArriveMonth);
        Assert.Equal(new List<int> { 0, 1, 2 }, army.MarchPathCityIds);

        // 旧存档的部队 JSON（无任何行军字段）→ 按「不在行军中」默认值反序列化。
        const string oldArmyJson =
            "{\"id\":0,\"ownerFactionId\":0,\"officerId\":-1,\"soldiers\":300," +
            "\"morale\":80,\"moraleMax\":100,\"positionCityId\":0,\"targetCityId\":-1," +
            "\"stance\":\"Idle\",\"hasRestedThisMonth\":false,\"monthsWithoutBattle\":0," +
            "\"hasMutiniedThisMonth\":false,\"hasEngagedThisMonth\":false}";

        var oldArmy = JsonSerializer.Deserialize<Army>(oldArmyJson, WireFormat.Options)!;
        Assert.Equal(-1, oldArmy.MarchTargetCityId);
        Assert.Equal(0, oldArmy.MarchArriveYear);
        Assert.Equal(0, oldArmy.MarchArriveMonth);
        Assert.Empty(oldArmy.MarchPathCityIds);
    }
}
