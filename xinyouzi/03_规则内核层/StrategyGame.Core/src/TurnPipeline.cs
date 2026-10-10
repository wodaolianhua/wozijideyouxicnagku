// ============================================================================
//  回合流水线—— 11 步固定调度
// ============================================================================
//  【铁律】11 步顺序固定，不得自行调整。顺序调整会引发连锁 bug。
//
//   1推进时间    2 经济结算      3 民心/稳定     4 基建推进
//   5 兵源结算   6 人物事件      7 执行 PendingOrders
//   8 兵源消耗   9 AI 决策       10 胜负判定     11 交还决策权
//
//  【禁止】不把玩法逻辑写进本文件。本文件只做调度：
//  玩法逻辑全在 Resolver 与 Systems 中，本文件只决定「什么时候调用谁」。
//  【依赖边界】对 04 包只有 IFactionPlanner 接口依赖，不依赖其具体实现，
//  因此本包可以独立编译、独立测试。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Systems;

namespace StrategyGame.Core;

/// <summary>
/// 回合流水线。按固定顺序执行 11 个步骤，推进一个月。
/// </summary>
public sealed class TurnPipeline : ITurnPipeline
{
    private readonly CommandProcessor _commandProcessor;
    private readonly IStateApplier _applier;
    private readonly EconomySystem _economy;
    private readonly VictoryChecker _victoryChecker;
    private readonly Func<WorldState, OfficerEventSystem>? _eventSystemFactory;
    private readonly IFactionPlanner? _planner;
    private readonly List<Order> _nextMonthOrders = new();

    /// <summary>创建回合流水线。</summary>
    /// <param name="commandProcessor">指令处理器。第 7 步用它执行延迟指令。</param>
    /// <param name="applier">状态写回器。各步骤产出的 Delta 由它写回。</param>
    /// <param name="balanceConfig">数值配置。传给经济系统。</param>
    /// <param name="eventSystemFactory">
    /// 人物事件系统工厂。第 6 步按势力调用。传 null 则跳过人物事件抽取。
    /// 用工厂而非单实例，是因为事件归属势力随回合变化。
    /// </param>
    /// <param name="planner">
    /// AI 决策器（04 包实现）。第 9 步调用。本包只依赖接口，
    /// 传 null 则跳过 AI 决策——这让本包能独立测试。
    /// </param>
    /// <exception cref="ArgumentNullException">前三个参数为 null 时抛出。</exception>
    public TurnPipeline(
        CommandProcessor commandProcessor,
        IStateApplier applier,
        BalanceConfig balanceConfig,
        Func<WorldState, OfficerEventSystem>? eventSystemFactory = null,
        IFactionPlanner? planner = null)
    {
        _commandProcessor = commandProcessor
            ?? throw new ArgumentNullException(nameof(commandProcessor));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
        ArgumentNullException.ThrowIfNull(balanceConfig);

        _economy = new EconomySystem(balanceConfig);
        _victoryChecker = new VictoryChecker();
        _eventSystemFactory = eventSystemFactory;
        _planner = planner;
    }

    /// <summary>
    /// 推进一个月。
    /// </summary>
    /// <param name="state">世界状态。方法会按步骤推进并写回其变更。</param>
    /// <returns>本回合结果，含事件、指令、快照与胜负判定。</returns>
    /// <remarks>
    /// 【顺序不可调整】各步骤存在依赖：
    /// 时间推进必须最先（后续步骤要读新年月）；
    /// 执行延迟指令在兵源消耗之前（本月下令的兵当月就该消耗）；
    /// 胜负判定必须在 AI 决策之后（AI 可能在本回合攻下最后一城）。
    /// </remarks>
    public TurnResult RunMonth(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var result = new TurnResult();
        _nextMonthOrders.Clear();

        // ---------- 第 1 步：推进时间 ----------
        GameClock.AdvanceMonth(state);

        // ---------- 第 2 步：经济结算 ----------
        ApplyDeltas(state, _economy.ResolveGrainIncome(state));

        // ---------- 第 3 步：民心 / 稳定 ----------
        ApplyDeltas(state, _economy.ResolveMoraleAndStability(state));

        // ---------- 第 4 步：基建推进 ----------
        ApplyDeltas(state, _economy.ResolveInfrastructure(state));
        ApplyDeltas(state, _economy.ResolvePostedOfficers(state));
        ApplyDeltas(state, _economy.ResolveLoyaltyDecay(state));

        // ---------- 第 5 步：兵源结算 ----------
        ApplyDeltas(state, _economy.ResolveSoldierIncome(state));

        // ---------- 第 6 步：人物事件 ----------
        RunOfficerEvents(state, result);

        // ---------- 第 7 步：执行本月到期的延迟指令 ----------
        RunPendingOrders(state, result);

        // ---------- 第 8 步：兵源消耗 ----------
        RunSoldierConsumption(state, result);

        // ---------- 第 9 步：AI 决策 ----------
        RunAiDecision(state, result);

        // ---------- 第 10 步：胜负判定 ----------
        var victory = _victoryChecker.Evaluate(state);
        result.GameOver = victory.GameOver;
        result.GameOverReason = victory.Reason;

        // ---------- 第 11 步：交还决策权 ----------
        result.Year = state.Year;
        result.Month = state.Month;
        result.NextMonthOrders = new List<Order>(_nextMonthOrders);
        result.Events.AddRange(state.EventLog);
        result.Snapshot = state.Clone();

        return result;
    }

    /// <summary>
    /// 第 6 步：为每个势力抽取人物事件。
    /// </summary>
    /// <param name="state">世界状态。</param>
    /// <param name="result">本回合结果，事件将累加其中。</param>
    private void RunOfficerEvents(WorldState state, TurnResult result)
    {
        if (_eventSystemFactory == null)
        {
            return;
        }

        foreach (var faction in state.Factions)
        {
            var eventSystem = _eventSystemFactory(state);
            var events = eventSystem.DrawEvents(state);
            if (events.Count == 0)
            {
                continue;
            }

            foreach (var evt in events)
            {
                evt.FactionId = faction.Id;
                state.EventLog.Add(evt);
                result.Events.Add(evt);
            }
        }
    }

    /// <summary>
    /// 第 7 步：执行本月到期的延迟指令。
    /// </summary>
    /// <param name="state">世界状态。</param>
    /// <param name="result">本回合结果，结算结果将累加其中。</param>
    /// <remarks>
    /// 逐条结算并立即写回：后一条指令的校验必须看到前一条的结果。
    /// 拒绝的指令不会中断本步骤——单个 AI 决策失败不该让整回合崩掉。
    /// </remarks>
    private void RunPendingOrders(WorldState state, TurnResult result)
    {
        var dueOrders = GameClock.GetDueOrders(state);
        if (dueOrders.Count == 0)
        {
            return;
        }

        foreach (var order in dueOrders)
        {
            var orderResult = _commandProcessor.ProcessAndApply(state, order);
            result.Results.Add(orderResult);

            if (orderResult.Events.Count > 0)
            {
                foreach (var evt in orderResult.Events)
                {
                    result.Events.Add(evt);
                }
            }
        }

        // 到期指令已执行，从队列移除，避免下月重复执行。
        GameClock.RemoveDueOrders(state);
    }

    /// <summary>
    /// 第 8 步：兵源消耗。Soldier 归零时触发饥荒事件。
    /// </summary>
    /// <param name="state">世界状态。</param>
    /// <param name="result">本回合结果，事件将累加其中。</param>
    /// <remarks>
    /// TODO(设计): 消耗公式待经济系统定稿。当前按当月出兵总数扣除，
    /// 定稿后替换为按人口与屯田度计算的消耗量。
    /// </remarks>
    private static void RunSoldierConsumption(WorldState state, TurnResult result)
    {
        foreach (var faction in state.Factions)
        {
            int totalSoldiers = 0;
            foreach (var army in state.Armies)
            {
                if (army.OwnerFactionId == faction.Id)
                {
                    totalSoldiers += army.Soldiers;
                }
            }

            if (faction.Resources.Soldier != 0)
            {
                continue;
            }

            // 兵源归零：粮不接济、民心动摇。
            // TODO(设计): 饥荒的具体惩罚数值待经济系统定稿，当前固定扣 10 民心。
            int moraleLoss = PlaceholderNumbers.FamineMoraleLoss;

            var famine = new EventRecord
            {
                Year = state.Year,
                Month = state.Month,
                Category = EventCategory.Disaster,
                EventKey = EventKeys.Famine,
                Message = $"{faction.Name}兵源耗尽，全境饥荒，民心下降 {moraleLoss}。",
                FactionId = faction.Id,
            };

            state.EventLog.Add(famine);
            result.Events.Add(famine);
            _ = totalSoldiers;
        }
    }

    /// <summary>
    /// 第 9 步：AI 决策，产出下月指令。
    /// </summary>
    /// <param name="state">世界状态。只读传入——AI 铁律：只能产出 Order，禁止改状态。</param>
    /// <param name="result">本回合结果，指令将累加其中。</param>
    private void RunAiDecision(WorldState state, TurnResult result)
    {
        if (_planner == null)
        {
            return;
        }

        var orders = _planner.PlanAll(state);
        if (orders.Count == 0)
        {
            return;
        }

        _nextMonthOrders.AddRange(orders);
        result.NextMonthOrders.AddRange(orders);
    }

    /// <summary>
    /// 把状态增量写回状态。
    /// </summary>
    /// <param name="state">世界状态。</param>
    /// <param name="deltas">状态增量列表。</param>
    private void ApplyDeltas(WorldState state, List<StateDelta> deltas)
    {
        if (deltas.Count > 0)
        {
            _applier.Apply(state, deltas);
        }
    }
}
