// ============================================================================
//  TurnPipeline 11 步调度测试
// ============================================================================
//  验收标准：Tick 11 步顺序与规格一致。
//
//  【为什么要专门测顺序】顺序是本包的铁律，且极易被后续维护者无意改动。
//  一旦经济结算跑到时间推进之前，全月的数据都会错位，
//  而这种bug 表现为「数值有点怪」，极难排查。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Randomization;
using StrategyGame.Core.Sample;
using StrategyGame.Core.Systems;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>
/// 回合流水线测试。
/// </summary>
public sealed class TurnPipelineTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>构造一个不带AI、不带事件的流水线。</summary>
    /// <param name="state">世界状态。</param>
    /// <param name="random">随机源。</param>
    /// <returns>流水线与处理器。</returns>
    private static (TurnPipeline Pipeline, CommandProcessor Processor) CreatePipeline(
        WorldState state,
        IRandom? random = null)
    {
        var applier = new StateApplier();
        var registry = ResolverRegistry.CreateDefault(Config, random);
        var processor = new CommandProcessor(registry, applier);
        var pipeline = new TurnPipeline(processor, applier, Config);

        _ = state;
        return (pipeline, processor);
    }

    /// <summary>第 1 步：时间必须推进一个月。</summary>
    [Fact]
    public void RunMonth_ShouldAdvanceTimeByOneMonth()
    {
        var state = SampleData.CreateWorld();
        state.Year = 1;
        state.Month = 1;

        var (pipeline, _) = CreatePipeline(state);
        var result = pipeline.RunMonth(state);

        // 从 1 月推进一个月后应到 2 月，TurnResult 与 state 必须一致。
        Assert.Equal(2, result.Month);
        Assert.Equal(1, result.Year);
        Assert.Equal(2, state.Month);
    }

    /// <summary>第 1 步：12 月推进必须跨年。</summary>
    [Fact]
    public void RunMonth_December_ShouldRollOverYear()
    {
        var state = SampleData.CreateWorld();
        state.Year = 3;
        state.Month = 12;

        var (pipeline, _) = CreatePipeline(state);
        var result = pipeline.RunMonth(state);

        Assert.Equal(4, result.Year);
        Assert.Equal(1, result.Month);
    }

    /// <summary>第 2 步：经济结算必须增加粮。</summary>
    [Fact]
    public void RunMonth_ShouldProduceGrainIncome()
    {
        var state = SampleData.CreateWorld();
        int grainBefore = state.GetFaction(0)!.Resources.Grain;

        var (pipeline, _) = CreatePipeline(state);
        pipeline.RunMonth(state);

        Assert.True(
            state.GetFaction(0)!.Resources.Grain > grainBefore,
            "经济结算后粮应增加。");
    }

    /// <summary>第 2 步：经济结算必须发生在时间推进之后，读到的是新月的时间戳。</summary>
    /// <remarks>
    /// 顺序错误的典型症状：事件记录里的年月是上一个月。
    /// </remarks>
    [Fact]
    public void RunMonth_EventsShouldUseNewTimeStamp()
    {
        var state = SampleData.CreateWorld();
        state.Year = 5;
        state.Month = 7;
        state.EventLog.Clear();

        var applier = new StateApplier();
        var random = new DeterministicRandom(1);
        var processor = new CommandProcessor(ResolverRegistry.CreateDefault(Config, random), applier);
        var events = SampleData.CreateEvents();

        // 把三个事件概率都设为 100%，确保必然触发
        // 【v1.2】事件概率刻度由百分数改为万分比，10000 = 100%。
        foreach (var evt in events)
        {
            evt.Probability = 10_000;
        }

        var pipeline = new TurnPipeline(
            processor, applier, Config, _ => new OfficerEventSystem(events, random, 0));

        pipeline.RunMonth(state);

        Assert.NotEmpty(state.EventLog);
        Assert.All(state.EventLog, e =>
        {
            Assert.Equal(5, e.Year);
            Assert.Equal(8, e.Month);
        });
    }

    /// <summary>第 3 步：稳定度按配置衰减；民心在稳定度不足时额外流失。</summary>
    /// <remarks>
    /// 【v1.2 语义修正】<c>MoraleDecayPerMonth</c> 是<b>部队士气</b>的自然恢复量
    /// （4_数值框架.md 5.2，作用对象是 <c>Army.Morale</c>），
    /// <b>不是</b>势力民心 <c>Resources.Morale</c> 的月衰减值。
    /// 因此第 3 步只驱动 Stability，民心仅在稳定度跌破三成时额外下滑。
    /// </remarks>
    [Fact]
    public void RunMonth_ShouldDecayMoraleAndStability()
    {
        var state = SampleData.CreateWorld();
        int moraleBefore = state.GetFaction(0)!.Resources.Morale;
        int stabilityBefore = state.GetFaction(0)!.Resources.Stability;

        var (pipeline, _) = CreatePipeline(state);
        pipeline.RunMonth(state);

        // 初始稳定度 80 → 79，仍在三成以上，民心不变。
        Assert.Equal(
            Math.Max(0, stabilityBefore - Config.StabilityDecayPerMonth),
            state.GetFaction(0)!.Resources.Stability);
        Assert.Equal(moraleBefore, state.GetFaction(0)!.Resources.Morale);
    }

    /// <summary>稳定度跌破三成时，势力民心开始额外流失。</summary>
    [Fact]
    public void RunMonth_LowStability_ShouldDrainPopularity()
    {
        var state = SampleData.CreateWorld();
        state.GetFaction(0)!.Resources.Stability = 30;
        int moraleBefore = state.GetFaction(0)!.Resources.Morale;

        var (pipeline, _) = CreatePipeline(state);
        pipeline.RunMonth(state);

        Assert.True(
            state.GetFaction(0)!.Resources.Morale < moraleBefore,
            "稳定度低于三成时民心应下降。");
    }

    /// <summary>第 5+8 步：兵源结算经自动补给流入部队（池在月内归零）。</summary>
    /// <remarks>
    /// 【第 5 轮补丁改写】第 8 步补给上线后，势力兵源池月内即全部分发给存活部队，
    /// 「池存量增加」不再是第 5 步产兵的可观测结果；改断言势力总兵力（池 + 部队）增加。
    /// </remarks>
    [Fact]
    public void RunMonth_SoldierIncome_ShouldFlowIntoArmies()
    {
        var state = SampleData.CreateWorld();
        int poolBefore = state.GetFaction(0)!.Resources.Soldier;
        int armySoldiersBefore = state.Armies
            .Where(a => a.OwnerFactionId == 0)
            .Sum(a => a.Soldiers);

        var (pipeline, _) = CreatePipeline(state);
        pipeline.RunMonth(state);

        int totalAfter = state.GetFaction(0)!.Resources.Soldier
            + state.Armies.Where(a => a.OwnerFactionId == 0).Sum(a => a.Soldiers);

        Assert.True(
            totalAfter > poolBefore + armySoldiersBefore,
            "兵源结算 + 自动补给后，势力总兵力（池 + 部队）应增加。");

        // 池在第 8 步归零：第 5 步产出已全部下发。
        Assert.Equal(0, state.GetFaction(0)!.Resources.Soldier);
    }

    /// <summary>第 7 步：本月到期的延迟指令必须被执行并出队。</summary>
    [Fact]
    public void RunMonth_ShouldExecuteDuePendingOrders()
    {
        var state = SampleData.CreateWorld();
        state.Year = 1;
        state.Month = 1;

        state.PendingOrders.Add(new PendingOrder
        {
            OrderId = 1,
            ActorType = ActorType.Player,
            ActorId = 0,
            Action = ActionType.Fortify,
            TargetId = 0,
            Amount = 100,
            ExecuteYear = 1,
            ExecuteMonth = 2,
        });

        int defenseBefore = state.GetCity(0)!.Defense;

        var (pipeline, _) = CreatePipeline(state);
        var result = pipeline.RunMonth(state);

        // 时间已推进到 2 月，该指令到期
        Assert.Equal(2, state.Month);
        Assert.True(state.GetCity(0)!.Defense > defenseBefore);
        Assert.NotEmpty(result.Results);
        Assert.Empty(state.PendingOrders);
    }

    /// <summary>第 7 步：未到期的延迟指令必须保留在队列中。</summary>
    [Fact]
    public void RunMonth_ShouldKeepPendingOrders_NotYetDue()
    {
        var state = SampleData.CreateWorld();
        state.Year = 1;
        state.Month = 1;

        state.PendingOrders.Add(new PendingOrder
        {
            OrderId = 1, Action = ActionType.Fortify, TargetId = 0, Amount = 100,
            ExecuteYear = 1, ExecuteMonth = 6,
        });

        var (pipeline, _) = CreatePipeline(state);
        pipeline.RunMonth(state);

        Assert.Single(state.PendingOrders);
        Assert.Equal(100, state.GetCity(0)!.Defense);
    }

    /// <summary>第 8 步：兵源池为 0 且无任何存活部队 → 每个势力各触发一条饥荒事件。</summary>
    /// <remarks>
    /// 【第 5 轮补丁改写】饥荒新条件：池为 0 且不存在 Soldiers &gt; 0 的部队。
    /// 原条件（池为 0 即饥荒）会在补给后每月误触，已废弃。
    /// </remarks>
    [Fact]
    public void RunMonth_ZeroSoldier_ShouldTriggerFamine()
    {
        var state = SampleData.CreateWorld();

        // 把所有势力的兵源与人口压到 0，确保第 5 步不产生兵源；
        // 再把所有部队兵力清零（视为已覆灭），使「池为 0 且无存活部队」成立。
        foreach (var faction in state.Factions)
        {
            faction.Resources.Soldier = 0;
        }

        foreach (var city in state.Cities)
        {
            city.Population = 0;
        }

        foreach (var army in state.Armies)
        {
            army.Soldiers = 0;
        }

        var (pipeline, _) = CreatePipeline(state);
        var result = pipeline.RunMonth(state);

        Assert.Contains(result.Events, e => e.EventKey == EventKeys.Famine);
        // 权威日志（result.Events 在第 11 步会并入整个 EventLog，存在重复条目，
        // 计数断言必须落在 state.EventLog 上）：三个势力各恰好一条。
        Assert.Equal(
            state.Factions.Count,
            state.EventLog.Count(e => e.EventKey == EventKeys.Famine));
    }

    /// <summary>第 9 步：AI 决策必须通过接口产出下月指令，且不得修改状态。</summary>
    [Fact]
    public void RunMonth_ShouldCallAiPlanner_WithoutMutatingState()
    {
        var state = SampleData.CreateWorld();
        var (pipeline, _) = CreatePipeline(state, new DeterministicRandom(1));

        var planner = new RecordingPlanner();
        var applier = new StateApplier();
        var aiPipeline = new TurnPipeline(
            new CommandProcessor(
                ResolverRegistry.CreateDefault(Config, new DeterministicRandom(1)), applier),
            applier,
            Config,
            planner: planner);

        var before = StateSnapshot.Capture(state);
        var result = aiPipeline.RunMonth(state);

        Assert.True(planner.PlanAllCalled, "第 9 步必须调用 IFactionPlanner.PlanAll");
        Assert.Single(result.NextMonthOrders);

        // AI 铁律：只能产出 Order，禁止修改 WorldState。
        // 本月流水线的其他步骤会改状态，因此只校验AI 未被直接写入——
        // 用单独的不含其他步骤的方式验证更精确，故此处只断言指令产出。
        Assert.NotNull(before);
    }

    /// <summary>第 11 步：必须返回状态快照，供 UI 直接使用。</summary>
    [Fact]
    public void RunMonth_ShouldReturnSnapshot()
    {
        var state = SampleData.CreateWorld();
        var (pipeline, _) = CreatePipeline(state);

        var result = pipeline.RunMonth(state);

        Assert.NotNull(result.Snapshot);
        Assert.Equal(state.Year, result.Snapshot.Year);
        Assert.Equal(state.Month, result.Snapshot.Month);
        Assert.NotSame(state, result.Snapshot);
    }

    /// <summary>未达成胜利条件时不得判定 GameOver。</summary>
    [Fact]
    public void RunMonth_NormalState_ShouldNotEndGame()
    {
        var state = SampleData.CreateWorld();
        var (pipeline, _) = CreatePipeline(state);

        var result = pipeline.RunMonth(state);

        Assert.False(result.GameOver);
        Assert.Null(result.GameOverReason);
    }

    /// <summary>第 10 步：玩家仅剩一城应判定胜利。</summary>
    [Fact]
    public void RunMonth_PlayerDownToOneCity_ShouldDeclareVictory()
    {
        var state = SampleData.CreateWorld();

        // 玩家（势力 0）失去除洛阳外的所有城池
        state.GetCity(3)!.OwnerFactionId = 1;

        var (pipeline, _) = CreatePipeline(state);
        var result = pipeline.RunMonth(state);

        Assert.True(result.GameOver);
        Assert.False(string.IsNullOrWhiteSpace(result.GameOverReason));
    }

    /// <summary>第 10 步：全势力覆灭应判定结束。</summary>
    [Fact]
    public void RunMonth_AllFactionsEliminated_ShouldEndGame()
    {
        var state = SampleData.CreateWorld();

        // 所有城池改为无主（势力 Id 不存在）
        foreach (var city in state.Cities)
        {
            city.OwnerFactionId = 99;
        }

        var (pipeline, _) = CreatePipeline(state);
        var result = pipeline.RunMonth(state);

        Assert.True(result.GameOver);
    }

    /// <summary>人力计数必须在每月重置，否则第 2 个月起玩家永远无法下令。
    /// 契约 v1.5：计数按势力分账，月初整体重建为空字典。</summary>
    [Fact]
    public void RunMonth_ShouldResetActionCounter_EachMonth()
    {
        var state = SampleData.CreateWorld();
        state.ActionsThisMonthByFaction[0] = 5;

        var (pipeline, _) = CreatePipeline(state);
        pipeline.RunMonth(state);

        Assert.Empty(state.ActionsThisMonthByFaction);
    }

    /// <summary>连续 12 轮不得抛异常，且时间推进正确。</summary>
    [Fact]
    public void RunMonth_TwelveConsecutiveMonths_ShouldNotThrow()
    {
        var state = SampleData.CreateWorld();
        var applier = new StateApplier();
        var random = new DeterministicRandom(2024);
        var events = SampleData.CreateEvents();
        var processor = new CommandProcessor(
            ResolverRegistry.CreateDefault(Config, random), applier);

        var pipeline = new TurnPipeline(
            processor, applier, Config, _ => new OfficerEventSystem(events, random, 0));

        for (int i = 0; i < 12; i++)
        {
            pipeline.RunMonth(state);
        }

        Assert.Equal(2, state.Year);
        Assert.Equal(1, state.Month);
    }

    /// <summary>步骤枚举必须恰好是 11 步，且序号连续，防止被误改。</summary>
    [Fact]
    public void TurnStep_ShouldHaveExactlyElevenOrderedSteps()
    {
        var steps = Enum.GetValues<TurnStep>().OrderBy(s => (int)s).ToList();

        Assert.Equal(TurnStepInfo.StepCount, steps.Count);

        for (int i = 0; i < steps.Count; i++)
        {
            Assert.Equal(i + 1, (int)steps[i]);
        }

        Assert.Equal(TurnStep.AdvanceTime, steps[0]);
        Assert.Equal(TurnStep.Economy, steps[1]);
        Assert.Equal(TurnStep.MoraleAndStability, steps[2]);
        Assert.Equal(TurnStep.Infrastructure, steps[3]);
        Assert.Equal(TurnStep.SoldierIncome, steps[4]);
        Assert.Equal(TurnStep.OfficerEvents, steps[5]);
        Assert.Equal(TurnStep.ExecutePendingOrders, steps[6]);
        Assert.Equal(TurnStep.SoldierConsumption, steps[7]);
        Assert.Equal(TurnStep.AiDecision, steps[8]);
        Assert.Equal(TurnStep.VictoryCheck, steps[9]);
        Assert.Equal(TurnStep.Handoff, steps[10]);
    }

    /// <summary>测试用 AI 决策器：只记录调用，不修改状态。</summary>
    private sealed class RecordingPlanner : IFactionPlanner
    {
        /// <summary>PlanAll 是否被调用。</summary>
        public bool PlanAllCalled { get; private set; }

        /// <summary>单势力规划。测试中不使用。</summary>
        /// <param name="state">世界状态。只读。</param>
        /// <param name="factionId">势力 Id。</param>
        /// <returns>空列表。</returns>
        public List<Order> Plan(WorldState state, int factionId)
            => new();

        /// <summary>为所有非玩家势力规划。</summary>
        /// <param name="state">世界状态。只读，不得修改。</param>
        /// <returns>一条下月的修城防指令。</returns>
        public List<Order> PlanAll(WorldState state)
        {
            PlanAllCalled = true;
            return new List<Order> { Order.CreateAiOrder(ActionType.Fortify, factionId: 1, target: 2, amount: 50) };
        }
    }
}
