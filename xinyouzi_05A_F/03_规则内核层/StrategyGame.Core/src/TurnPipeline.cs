// ============================================================================
//  回合流水线—— 11 步固定调度
// ============================================================================
//  【铁律】11 步顺序固定，不得自行调整。顺序调整会引发连锁 bug。
//
//   1推进时间    2 经济结算      3 民心/稳定     4 基建推进
//   5 兵源结算   6 人物事件      7 执行 PendingOrders
//   8 兵源补给   9 AI 决策       10 胜负判定     11 交还决策权
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
    private readonly MoraleSystem _morale;
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
        _morale = new MoraleSystem(balanceConfig);
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

        // ---------- 第 3 步：民心 / 稳定 / 部队士气 ----------
        ApplyDeltas(state, _economy.ResolveMoraleAndStability(state));

        // 部队士气（Army.Morale）月度结算：自然恢复 + 和平腐化 + 哗变。
        // 【注意】它与上一行的势力民心（Resources.Morale）是两个不同的东西，
        // 契约里两者同名。详见 MoraleSystem 文件头的命名陷阱说明。
        RunArmyMorale(state, result);

        // ---------- 第 4 步：基建推进 ----------
        ApplyDeltas(state, _economy.ResolveInfrastructure(state));
        ApplyDeltas(state, _economy.ResolvePostedOfficers(state));
        ApplyDeltas(state, _economy.ResolveLoyaltyDecay(state));

        // ---------- 第 5 步：兵源结算 ----------
        ApplyDeltas(state, _economy.ResolveSoldierIncome(state));

        // 驻军粮耗与兵源同属本月结算（5_指令字典.md Dispatch 条目）。
        ApplyDeltas(state, _economy.ResolveArmyGrainUpkeep(state));

        // ---------- 第 6 步：人物事件 ----------
        RunOfficerEvents(state, result);

        // ---------- 第 7 步：执行本月到期的延迟指令 ----------
        RunPendingOrders(state, result);

        // ---------- 第 8 步：兵源自动补给 + 饥荒判定 ----------
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
    /// 部队士气月度结算，并补齐哗变事件。
    /// </summary>
    /// <param name="state">世界状态。</param>
    /// <param name="result">本回合结果，事件将累加其中。</param>
    /// <remarks>
    /// Delta 与事件分两步产出：Delta 是状态变化，事件是玩家要看见的历史。
    /// 先算出 Delta，再据其内容判断哪些部队哗变了，避免读改写 state。
    /// </remarks>
    private void RunArmyMorale(WorldState state, TurnResult result)
    {
        var deltas = _morale.ResolveMonthlyMorale(state);
        if (deltas.Count == 0)
        {
            return;
        }

        ApplyDeltas(state, deltas);

        // 哗变事件：HasMutiniedThisMonth 由 0 变 1 的部队即为本月哗变者。
        // 【第 3 轮补丁·修复二配套】月初翻转（1→0）也走该字段，但那不是哗变：
        // 只有真正置位（NewValue = 1）的 delta 才对应本月经历的哗变；
        // 若不过滤，上月哗变过的部队每月初都会被翻转 delta 刷出一条假哗变事件。
        foreach (var delta in deltas)
        {
            if (delta.TargetType != DeltaTarget.Army
                || delta.Field != DeltaField.ArmyHasMutiniedThisMonth
                || delta.NewValue != 1)
            {
                continue;
            }

            var army = state.GetArmy(delta.TargetId);
            if (army == null)
            {
                continue;
            }

            var evt = _morale.BuildMutinyEvent(state, army.Id);
            if (evt == null)
            {
                continue;
            }

            state.EventLog.Add(evt);
            result.Events.Add(evt);
        }
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
    /// 第 8 步：兵源自动补给 + 饥荒判定（裁决单 D3）。
    /// </summary>
    /// <param name="state">世界状态。</param>
    /// <param name="result">本回合结果，事件将累加其中。</param>
    /// <remarks>
    /// 【P0 最简链路】兵源池（Resources.Soldier）是部队兵力的唯一来源：
    /// 第 5 步产入池，本步一次性下发给存活部队（Army.Soldiers &gt; 0，按 Army.Id 升序），
    /// 每队得 floor(P/n)，余数 P % n 给 Id 最小的前若干队每队 +1，随后池归零。
    /// 未来「手动编组/部署」「编制上限」接入后替换本分配规则（裁决单已备案）。
    /// 饥荒：兵源池为 0 且该势力不存在任何存活部队才触发（补给后不会每月误触）。
    /// 确定性：分配只依赖 Id 排序与整数除法，禁止引入随机数。
    /// </remarks>
    private void RunSoldierConsumption(WorldState state, TurnResult result)
    {
        foreach (var faction in state.Factions)
        {
            // 存活部队：Soldiers &gt; 0（== 0 视为已覆灭，不补），按 Id 升序保证确定性。
            List<Army> aliveArmies = state.Armies
                .Where(a => a.OwnerFactionId == faction.Id && a.Soldiers > 0)
                .OrderBy(a => a.Id)
                .ToList();

            int pool = faction.Resources.Soldier;

            // ---- 自动补给：n > 0 且 P > 0 时把池一次性分光 ----
            if (aliveArmies.Count > 0 && pool > 0)
            {
                var deltas = new List<StateDelta>(aliveArmies.Count + 1);
                int baseShare = pool / aliveArmies.Count;
                int remainder = pool % aliveArmies.Count;

                for (int i = 0; i < aliveArmies.Count; i++)
                {
                    int gain = baseShare + (i < remainder ? 1 : 0);
                    if (gain <= 0)
                    {
                        continue;
                    }

                    var army = aliveArmies[i];
                    deltas.Add(new StateDelta
                    {
                        TargetType = DeltaTarget.Army,
                        TargetId = army.Id,
                        Field = DeltaField.ArmySoldiers,
                        OldValue = army.Soldiers,
                        NewValue = army.Soldiers + gain,
                    });
                }

                // 兵源池归零（字段路径与 RecruitResolver 产兵 delta 同款）。
                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.Faction,
                    TargetId = faction.Id,
                    Field = DeltaField.FactionSoldier,
                    OldValue = pool,
                    NewValue = 0,
                });

                ApplyDeltas(state, deltas);
            }

            // ---- 饥荒判定：池为 0 且无任何存活部队 ----
            if (pool == 0 && aliveArmies.Count == 0)
            {
                // 兵源归零且无部队：粮不接济、民心动摇。
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
            }
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
