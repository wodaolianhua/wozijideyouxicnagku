// ============================================================================
//  兵源自动补给（第 8 步）与按势力指令额度（契约 v1.5）测试
// ============================================================================
//  【补给（裁决单 D3）】兵源池在第 8 步一次性下发给存活部队：
//  每队 floor(P/n)，余数 P % n 按 Army.Id 升序给最小的前若干队每队 +1，池归零；
//  Soldiers == 0 的部队视为已覆灭不补；池为 0 且无存活部队才触发饥荒。
//  【额度（裁决单 D4）】MaxOrdersPerFactionPerMonth 按势力分账：
//  两方各 5 条互不影响；单方第 6 条必拒；月初清零后额度恢复。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Sample;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>
/// 兵源自动补给与按势力指令额度测试。
/// </summary>
public sealed class SupplyQuotaTests
{
    // 本组测试只验证「分配规则」，不验证产出：把兵源产出系数置 0，
    // 这样池里有多少兵完全由测试显式设定，不会被第 5 步的产出污染。
    private static readonly BalanceConfig Config = CreateSupplyConfig();

    private static BalanceConfig CreateSupplyConfig()
    {
        var config = SampleData.CreateBalance();
        config.SoldierPerPopPerMonth = 0;
        return config;
    }

    /// <summary>构造不带 AI、不带事件的流水线。</summary>
    /// <returns>流水线与指令处理器（共享同一 Applier）。</returns>
    private static (TurnPipeline Pipeline, CommandProcessor Processor) CreatePipeline()
    {
        var applier = new StateApplier();
        var registry = ResolverRegistry.CreateDefault(Config);
        var processor = new CommandProcessor(registry, applier);
        return (new TurnPipeline(processor, applier, Config), processor);
    }

    /// <summary>
    /// 构造补给测试世界：关闭兵源产出（池存量完全由测试设定），
    /// 城市保留人口以便兵力上限有意义，部队清空后按参数重建（均归属势力 0）。
    /// </summary>
    /// <param name="pool">势力 0 的兵源池存量。</param>
    /// <param name="armies">（部队 Id, 初始兵力）列表。全部驻在 0 号城。</param>
    /// <returns>可直接 RunMonth 的世界状态。</returns>
    /// <remarks>
    /// 【v1.8 为什么改成关产出而不是清人口】兵力上限 = 人口 × 2，人口清零等于上限为 0，
    /// 任何部队都领不到兵，测试会退化成「永远分不出去」。改成把产出系数置 0，
    /// 既切断干扰，又让人口（也就是上限）保持真实。
    /// </remarks>
    private static WorldState CreateSupplyWorld(int pool, params (int Id, int Soldiers)[] armies)
        => CreateSupplyWorldAtCity(0, pool, armies);

    private static WorldState CreateSupplyWorldAtCity(
        int cityId, int pool, params (int Id, int Soldiers)[] armies)
    {
        var state = SampleData.CreateWorld();

        foreach (var city in state.Cities)
        {
            city.Population = 4000; // 上限 8000，远高于本组测试的兵力
        }

        state.Armies.Clear();
        foreach (var (id, soldiers) in armies)
        {
            state.Armies.Add(new Army
            {
                Id = id,
                OwnerFactionId = 0,
                OfficerId = -1,
                Soldiers = soldiers,
                Morale = 60,
                MoraleMax = 100,
                PositionCityId = cityId,
                TargetCityId = -1,
                Stance = ArmyStance.Idle,
            });
        }

        state.GetFaction(0)!.Resources.Soldier = pool;
        return state;
    }

    // ========================================================================
    //  第 8 步：兵源自动补给
    // ========================================================================

    /// <summary>同城两队：只有一支领兵（v1.8「每城一个补给口」）。</summary>
    /// <remarks>
    /// 【v1.8 规则变化】补给口按城分配，不再按部队平分。原因是合兵会把并入方清零，
    /// 若清零的番号也能领兵，它下月就自己长回来，合兵等于白合。
    /// 同城时优先补给「已经有兵」的那支（此处 Id 10），Id 20 保持不变。
    /// </remarks>
    [Fact]
    public void 补给_同城两队只补一支()
    {
        var state = CreateSupplyWorld(11, (10, 100), (20, 50));
        var (pipeline, _) = CreatePipeline();

        pipeline.RunMonth(state);

        Assert.Equal(111, state.GetArmy(10)!.Soldiers);
        Assert.Equal(50, state.GetArmy(20)!.Soldiers);
        Assert.Equal(0, state.GetFaction(0)!.Resources.Soldier);
    }

    /// <summary>分城驻守：每城一个补给口，兵源按城平分（余数给 Id 小的城）。</summary>
    [Fact]
    public void 补给_分城驻守按城均分余数()
    {
        // P=11、两座城各一支 → 每城 5，余 1 给 Id 小的部队（10）。
        var state = CreateSupplyWorld(11, (10, 100), (20, 50));
        state.GetCity(1)!.OwnerFactionId = 0;
        state.GetArmy(20)!.PositionCityId = 1;

        var (pipeline, _) = CreatePipeline();

        pipeline.RunMonth(state);

        Assert.Equal(106, state.GetArmy(10)!.Soldiers);
        Assert.Equal(55, state.GetArmy(20)!.Soldiers);
        Assert.Equal(0, state.GetFaction(0)!.Resources.Soldier);
    }

    /// <summary>补给确定性：同 seed 同状态两遍，逐字段相等。</summary>
    [Fact]
    public void 补给_同状态两遍逐字段相等()
    {
        var first = CreateSupplyWorld(11, (10, 100), (20, 50));
        var second = CreateSupplyWorld(11, (10, 100), (20, 50));
        var (pipelineA, _) = CreatePipeline();
        var (pipelineB, _) = CreatePipeline();

        pipelineA.RunMonth(first);
        pipelineB.RunMonth(second);

        Assert.Null(StateSnapshot.Diff(first, second));
    }

    /// <summary>边界：同城时零兵残部不抢补给，补给全归还有兵的那支。</summary>
    [Fact]
    public void 补给_同城残部不抢补给()
    {
        // 同城两队：Id 10 已打光，Id 20 还有 40 人 → 补给口给 20，10 保持 0。
        var state = CreateSupplyWorld(10, (10, 0), (20, 40));
        var (pipeline, _) = CreatePipeline();

        pipeline.RunMonth(state);

        Assert.Equal(0, state.GetArmy(10)!.Soldiers);
        Assert.Equal(50, state.GetArmy(20)!.Soldiers);
        Assert.Equal(0, state.GetFaction(0)!.Resources.Soldier);
    }

    /// <summary>边界：某城只剩一支打光的部队时，它能用兵源池重整残部。</summary>
    /// <remarks>
    /// 【v1.8 修复】原规则把 Soldiers == 0 一律当「已覆灭」排除，
    /// 结果是「打光 → 池里明明有兵 → 没人能领 → 池永久卡满 → 再也出不了兵」，
    /// 无头模拟第 5 年起三方全部无兵，世界直接死掉。城防军被打光应当能重整。
    /// </remarks>
    [Fact]
    public void 补给_独城残部可重整()
    {
        var state = CreateSupplyWorld(10, (10, 0));
        var (pipeline, _) = CreatePipeline();

        pipeline.RunMonth(state);

        Assert.Equal(10, state.GetArmy(10)!.Soldiers);
    }

    /// <summary>边界：超出兵力上限的部分留在池里，不硬塞。</summary>
    [Fact]
    public void 补给_超出兵力上限时余量留池()
    {
        // 4000 人口的城上限 8000；现役 7999，池里 500 → 只能补 1，余 499 留池。
        var state = CreateSupplyWorld(500, (10, 7999));
        var (pipeline, _) = CreatePipeline();

        pipeline.RunMonth(state);

        Assert.Equal(8000, state.GetArmy(10)!.Soldiers);
        Assert.Equal(499, state.GetFaction(0)!.Resources.Soldier);
    }

    /// <summary>边界：池为 0 时部队兵力不变，也不触发饥荒（有存活部队）。</summary>
    [Fact]
    public void 补给_池为0时部队兵力不变且无饥荒()
    {
        var state = CreateSupplyWorld(0, (10, 40), (20, 60));
        var (pipeline, _) = CreatePipeline();

        var result = pipeline.RunMonth(state);

        Assert.Equal(40, state.GetArmy(10)!.Soldiers);
        Assert.Equal(60, state.GetArmy(20)!.Soldiers);
        Assert.DoesNotContain(result.Events, e => e.EventKey == EventKeys.Famine);
        Assert.DoesNotContain(state.EventLog, e => e.EventKey == EventKeys.Famine);
    }

    /// <summary>饥荒：池为 0 且无任何存活部队 → 恰好一条 Famine 事件，归属该势力。</summary>
    [Fact]
    public void 饥荒_池为0且无存活部队触发()
    {
        // 势力 0 的唯一部队已覆灭（0 兵）且池为 0；势力 1/2 池未动（400/300），不触发。
        var state = CreateSupplyWorld(0, (10, 0));
        var (pipeline, _) = CreatePipeline();

        pipeline.RunMonth(state);

        // 计数落在权威日志 state.EventLog 上（第 11 步会把整份日志并入 result.Events）。
        var famines = state.EventLog
            .Where(e => e.EventKey == EventKeys.Famine)
            .ToList();
        Assert.Single(famines);
        Assert.Equal(0, famines[0].FactionId);
        Assert.Equal(EventCategory.Disaster, famines[0].Category);
    }

    // ========================================================================
    //  契约 v1.5：指令额度按势力分账
    // ========================================================================

    /// <summary>两方同月各成功执行 5 条，第 6 条各自被拒，计数互不影响。</summary>
    [Fact]
    public void 额度_两方各5条互不影响_第6条各自被拒()
    {
        var state = SampleData.CreateWorld();
        var (_, processor) = CreatePipeline();

        var playerOrders = Enumerable.Range(0, 6)
            .Select(_ => Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 1))
            .ToList();
        var aiOrders = Enumerable.Range(0, 6)
            .Select(_ => Order.CreateAiOrder(ActionType.Fortify, factionId: 1, target: 2, amount: 1))
            .ToList();

        var playerResults = processor.ProcessAll(state, playerOrders);
        var aiResults = processor.ProcessAll(state, aiOrders);

        Assert.Equal(Config.MaxOrdersPerFactionPerMonth, playerResults.Count(r => r.IsSuccess));
        Assert.Equal(ResolveResultCode.RejectedInsufficientCapacity, playerResults[5].Code);
        Assert.Equal(Config.MaxOrdersPerFactionPerMonth, aiResults.Count(r => r.IsSuccess));
        Assert.Equal(ResolveResultCode.RejectedInsufficientCapacity, aiResults[5].Code);

        // 计数按势力分账：双方各记各的 5 条，互不影响。
        Assert.Equal(Config.MaxOrdersPerFactionPerMonth, state.ActionsThisMonthByFaction[0]);
        Assert.Equal(Config.MaxOrdersPerFactionPerMonth, state.ActionsThisMonthByFaction[1]);
    }

    /// <summary>单势力第 6 条必拒；月初字典清零后可再次执行 5 条。</summary>
    [Fact]
    public void 额度_月初清零后额度恢复()
    {
        var state = SampleData.CreateWorld();
        var (_, processor) = CreatePipeline();

        var first = processor.ProcessAll(
            state,
            Enumerable.Range(0, 6)
                .Select(_ => Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 1))
                .ToList());

        Assert.Equal(Config.MaxOrdersPerFactionPerMonth, first.Count(r => r.IsSuccess));
        Assert.Equal(ResolveResultCode.RejectedInsufficientCapacity, first[5].Code);

        // 月初清零（GameClock.AdvanceMonth 的第 1 步行为）：字典整体重建。
        GameClock.AdvanceMonth(state);
        Assert.Empty(state.ActionsThisMonthByFaction);

        var second = processor.ProcessAll(
            state,
            Enumerable.Range(0, 6)
                .Select(_ => Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 1))
                .ToList());

        Assert.Equal(Config.MaxOrdersPerFactionPerMonth, second.Count(r => r.IsSuccess));
        Assert.Equal(ResolveResultCode.RejectedInsufficientCapacity, second[5].Code);
    }

    /// <summary>额度计数走 StateApplier 字典分支：执行一条后该势力计数 +1。</summary>
    [Fact]
    public void 额度_执行一条指令后该势力计数加一()
    {
        var state = SampleData.CreateWorld();
        var (_, processor) = CreatePipeline();

        var results = processor.ProcessAll(
            state,
            new List<Order>
            {
                Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 1),
            });

        Assert.True(results[0].IsSuccess);
        Assert.Equal(1, state.GetActionsThisMonth(0));

        // delta 必须表达为 Faction 目标 + 新字段路径，TargetId 即势力 Id。
        var counterDelta = results[0].Deltas.Single(d =>
            d.Field == DeltaField.FactionActionsThisMonth);
        Assert.Equal(DeltaTarget.Faction, counterDelta.TargetType);
        Assert.Equal(0, counterDelta.TargetId);
        Assert.Equal(0, counterDelta.OldValue);
        Assert.Equal(1, counterDelta.NewValue);
    }
}
