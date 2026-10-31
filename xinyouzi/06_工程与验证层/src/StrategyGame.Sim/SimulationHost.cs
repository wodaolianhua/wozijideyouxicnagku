// ============================================================================
//  月循环宿主 —— 03 流水线缺的最后一块接线
//  【职责】
//    1. 每局独立构造 config / rng / applier / registry / processor / planner / pipeline；
//    2. 跑月循环，把 AI 产出的下月指令入队（执行月 = 下一月）；
//    3. 按「三方 AI 混战」口径判定终局；
//    4. 逐月采样统计，产出可写入 CSV 的结果。
//
//  【上游接缝】03 的 TurnPipeline.RunMonth 不会把 AI 产出的下月指令自动入队，
//  这是 03 有意留给宿主的接缝，由本类闭环。
//
//  【确定性红线】
//    · 禁止 DateTime.Now / Guid.NewGuid / Random.Shared；
//    · 一局内所有随机消费共用同一个 DeterministicRandom(seed)；
//    · 每局 seed = baseSeed + gameIndex，构造顺序固定；
//    · 不使用 Dictionary 的遍历顺序做任何决策（只用键查找）。
// ============================================================================

using StrategyGame.AI;
using StrategyGame.Contracts;
using StrategyGame.Core;
using StrategyGame.Core.Randomization;
using StrategyGame.Core.Sample;

namespace StrategyGame.Sim;

/// <summary>
/// 月循环宿主。承载一局（或多局）的无头模拟。
/// </summary>
/// <remarks>
/// 【每局独立实例】本类持有 config / rng / pipeline / state 等全部可变对象，
/// <b>禁止跨局共享</b>。批量模拟并行时每局各自 new 一个实例，
/// 因此不存在共享随机源与共享可变对象的问题。
/// </remarks>
public sealed class SimulationHost
{
    /// <summary>默认月数上限。</summary>
    public const int DefaultMaxMonths = 120;

    private readonly BalanceConfig _config;
    private readonly DeterministicRandom _rng;
    private readonly TurnPipeline _pipeline;

    /// <summary>回放日志（每月实际入队的指令全字段）。</summary>
    private readonly List<ReplayEntry> _replayEntries = new();

    /// <summary>每月开始时的随机源内部状态（回放时按此逐月恢复，精确复现随机流）。</summary>
    private readonly List<int> _monthRandomStates = new();

    /// <summary>上批入队指令的行动类型序列，用于把结算结果映射回行动类型。</summary>
    private readonly List<ActionType> _pendingBatchActions = new();

    /// <summary>本局自增的 OrderId 计数器（从 1 开始）。</summary>
    private int _nextOrderId = 1;

    /// <summary>上一次记录事件时 EventLog 的长度。</summary>
    private int _lastEventLogLength;

    private int _monthsElapsed;
    private bool _finished;
    private int _winnerFactionId = -1;
    private SimGameOverReason _gameOverReason = SimGameOverReason.None;
    private int _upstreamGameOverMonth = -1;
    private string? _upstreamGameOverReason;

    /// <summary>最近一次 RunMonth 的完整回合结果（含逐条 ResolveResult）。首次 RunMonth 前为 null。</summary>
    /// <remarks>【05A 授权改动 2026-10-31】RunMonth 原为 void，逐条结算结果（code/message/deltas）
    /// 无任何只读通道，05A 表现层按提示词 §3（tick 返回逐条 results）无法实现。
    /// 经用户裁决豁免「禁改 06」后新增本只读属性：仅记录引用，不改变任何既有行为、随机流与确定性。
    /// 改动共两处：本属性 + RunMonth 内一行赋值。</remarks>
    public TurnResult? LastTurn { get; private set; }
    /// <summary>创建一局宿主。</summary>
    /// <param name="seed">本局随机种子。</param>
    /// <param name="maxMonths">月数上限。到达上限仍未分胜负则记为僵持。</param>
    /// <param name="applySeedVariance">
    /// 是否启用种子敏感化（按 seed 轮换三方初始资源档位）。
    /// 默认 true —— 上游 <c>SampleData.CreateWorld(seed)</c> 不消费 seed 生成世界内容，
    /// 不启用则不同 seed 跑出完全相同的一局，批量统计失去意义。
    /// 严格照抄上游的行为传 false 复现。
    /// 开启原因与口径见 <see cref="WorldVariator"/>。
    /// </param>
    public SimulationHost(int seed, int maxMonths = DefaultMaxMonths, bool applySeedVariance = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seed);

        // ===== 构造顺序固定（提示词第 3.1 节）=====
        // 顺序即确定性的一部分：rng 在 registry 之前创建，
        // 保证「结算器与 AI 共用同一随机流」且每次构造消耗序列一致。
        _config = SampleData.CreateBalance();
        _rng = new DeterministicRandom(seed);
        var applier = new StateApplier();
        var registry = ResolverRegistry.CreateDefault(_config, _rng);
        var processor = new CommandProcessor(registry, applier);
        var planner = new AiFactionPlanner(_config, _rng);
        _pipeline = new TurnPipeline(
            processor,
            applier,
            _config,
            eventSystemFactory: null,
            planner: planner);

        State = CreateInitialWorld(seed);
        MaxMonths = maxMonths;
        ApplySeedVariance = applySeedVariance;

        if (applySeedVariance)
        {
            WorldVariator.ApplyResourceRotation(State, seed);
        }
    }

    private SimulationHost(
        WorldState state,
        int randomState,
        int nextOrderId,
        int monthsElapsed,
        int maxMonths,
        List<int> monthRandomStates)
    {
        _config = SampleData.CreateBalance();
        _rng = new DeterministicRandom(state.Seed);
        _rng.SetState(randomState);

        var applier = new StateApplier();
        var registry = ResolverRegistry.CreateDefault(_config, _rng);
        var processor = new CommandProcessor(registry, applier);
        var planner = new AiFactionPlanner(_config, _rng);
        _pipeline = new TurnPipeline(
            processor,
            applier,
            _config,
            eventSystemFactory: null,
            planner: planner);

        State = state;
        MaxMonths = maxMonths;
        _nextOrderId = nextOrderId;
        _monthsElapsed = monthsElapsed;
        _lastEventLogLength = state.EventLog.Count;
        _monthRandomStates.AddRange(monthRandomStates);
    }

    /// <summary>
    /// 创建初始世界：样例数据 + 三方全部置 AI 化。
    /// </summary>
    /// <param name="seed">随机种子。</param>
    /// <returns>初始世界状态。</returns>
    /// <remarks>
    /// 【玩家槽处理】AiFactionPlanner.PlanAll 会跳过 IsPlayer 势力。
    /// 三方全部置 IsPlayer=false，让三个势力都走 AI 规划（三方 AI 混战）。
    /// 槽 0 在 CSV 与报告中称为「基准槽位」，不称「玩家」。
    /// </remarks>
    public static WorldState CreateInitialWorld(int seed)
    {
        var state = SampleData.CreateWorld(seed);
        foreach (var faction in state.Factions)
        {
            faction.IsPlayer = false;
        }

        return state;
    }

    /// <summary>当前世界状态。</summary>
    public WorldState State { get; }

    /// <summary>月数上限。</summary>
    public int MaxMonths { get; }

    /// <summary>本局是否启用了种子敏感化（回放必须复现同一口径）。</summary>
    public bool ApplySeedVariance { get; }

    /// <summary>已推进的月数。</summary>
    public int MonthsElapsed => _monthsElapsed;

    /// <summary>本局统计。</summary>
    public SimStats Stats { get; } = new();

    /// <summary>是否已终局。</summary>
    public bool IsFinished => _finished;

    /// <summary>胜方势力 Id；无胜方为 -1。</summary>
    public int WinnerFactionId => _winnerFactionId;

    /// <summary>终局原因。</summary>
    public SimGameOverReason GameOverReason => _gameOverReason;

    /// <summary>上游 TurnPipeline 判定终局的月份；-1 表示从未判定。</summary>
    public int UpstreamGameOverMonth => _upstreamGameOverMonth;

    /// <summary>上游 TurnPipeline 给出的终局原因原文。</summary>
    public string? UpstreamGameOverReason => _upstreamGameOverReason;

    /// <summary>回放日志（每月实际入队的指令全字段）。</summary>
    public IReadOnlyList<ReplayEntry> ReplayEntries => _replayEntries;

    /// <summary>每月开始时的随机源内部状态。</summary>
    public IReadOnlyList<int> MonthRandomStates => _monthRandomStates;

    /// <summary>本局自增的 OrderId 计数器当前值（存档需要）。</summary>
    public int NextOrderId => _nextOrderId;

    /// <summary>
    /// 推进一个月。
    /// </summary>
    /// <remarks>
    /// 【顺序铁律】RunMonth 内部第 1 步已先推进时间，第 9 步才产 AI 指令，
    /// 所以入队时执行月必须是 state 当前年月的<b>下一月</b>；早一个月或同月都会错。
    /// </remarks>
    public void RunMonth()
    {
        if (_finished)
        {
            return;
        }

        // 记录本月开始时的随机源状态：回放时按此逐月恢复，
        // 使「结算器消耗的随机序列」在原模拟与回放中完全一致。
        _monthRandomStates.Add(_rng.GetState());

        var turn = _pipeline.RunMonth(State);
        LastTurn = turn;
        _monthsElapsed++;

        RecordResults(turn.Results);
        RecordEvents();

        // ===== 把 AI 产出的下月指令入队 =====
        var (nextYear, nextMonth) = NextMonth(State.Year, State.Month);
        _pendingBatchActions.Clear();
        foreach (var order in turn.NextMonthOrders)
        {
            order.OrderId = _nextOrderId++;
            order.ExecuteYear = nextYear;
            order.ExecuteMonth = nextMonth;

            State.PendingOrders.Add(order.ToPendingOrder());

            _pendingBatchActions.Add(order.Action);
            Stats.AddOrder(order.Action);
            _replayEntries.Add(ReplayEntry.FromOrder(order, _monthsElapsed));
        }

        // 上游的胜负判定是「玩家口径」的（以 IsPlayer 势力为基准）。
        // 三方全部 AI 化后不存在基准势力，上游会在第 1 个月给出
        // 「玩家全部城池易主」的误判。此处原样记录，仅作诊断与上报，不参与终局控制。
        if (turn.GameOver && _upstreamGameOverMonth < 0)
        {
            _upstreamGameOverMonth = _monthsElapsed;
            _upstreamGameOverReason = turn.GameOverReason;
        }

        Stats.SampleMonth(State);

        EvaluateOutcome();
    }

    /// <summary>
    /// 推进到终局或月数上限。
    /// </summary>
    public void RunToEnd()
    {
        while (!_finished && _monthsElapsed < MaxMonths)
        {
            RunMonth();
        }

        if (!_finished)
        {
            _finished = true;
            _gameOverReason = SimGameOverReason.Stalemate;
            _winnerFactionId = -1;
        }
    }

    /// <summary>
    /// 构造本局的 CSV 结果对象。
    /// </summary>
    /// <param name="gameIndex">批次内局序。</param>
    /// <param name="seed">本局随机种子。</param>
    /// <returns>结果对象。</returns>
    public GameResult BuildResult(int gameIndex, int seed)
    {
        var result = new GameResult
        {
            GameIndex = gameIndex,
            Seed = seed,
            WinnerFactionId = _winnerFactionId,
            GameOverReason = _gameOverReason,
            MonthsSurvived = _monthsElapsed,
            EventCount = Stats.EventCount,
            MutinyCount = Stats.MutinyCount,
            CityCapturedCount = Stats.CityCapturedCount,
            AverageArmyMorale = Stats.AverageArmyMorale,
            LowMoraleRatioPerMille = Stats.LowMoraleRatioPerMille,
            UpstreamGameOverMonth = _upstreamGameOverMonth,
            UpstreamGameOverReason = _upstreamGameOverReason,
        };

        foreach (var (action, count) in Stats.OrdersByAction)
        {
            result.OrdersByAction[action] = count;
            result.AiOrderTotal += count;
        }

        foreach (var (code, count) in Stats.ResultCodes)
        {
            result.ResultCodes[code] = count;
        }

        foreach (var (action, map) in Stats.RejectsByAction)
        {
            var target = new Dictionary<ResolveResultCode, int>();
            foreach (var (code, count) in map)
            {
                target[code] = count;
            }

            result.RejectsByAction[action] = target;
        }

        foreach (var faction in State.Factions)
        {
            int cityCount = 0;
            int soldiers = 0;
            foreach (var city in State.Cities)
            {
                if (city.OwnerFactionId == faction.Id)
                {
                    cityCount++;
                }
            }

            foreach (var army in State.Armies)
            {
                if (army.OwnerFactionId == faction.Id)
                {
                    soldiers += army.Soldiers;
                }
            }

            result.Factions.Add(new FactionEndState
            {
                FactionId = faction.Id,
                Name = faction.Name,
                Cities = cityCount,
                Soldiers = soldiers,
                Grain = faction.Resources.Grain,
                Silver = faction.Resources.Silver,
                SoldierStock = faction.Resources.Soldier,
                AverageSoldierStock = Stats.AverageSoldierStock(faction.Id),
            });
        }

        return result;
    }

    /// <summary>
    /// 导出当前时刻的存档内容。
    /// </summary>
    /// <returns>可序列化的存档对象。</returns>
    public SaveFile BuildSave()
    {
        return new SaveFile
        {
            FormatVersion = SaveSystem.CurrentFormatVersion,
            State = State,
            MonthsElapsed = _monthsElapsed,
            NextOrderId = _nextOrderId,
            RandomState = _rng.GetState(),
            MaxMonths = MaxMonths,
            ApplySeedVariance = ApplySeedVariance,
            MonthRandomStates = new List<int>(_monthRandomStates),
        };
    }

    /// <summary>
    /// 从存档恢复一个可继续推进的宿主。
    /// </summary>
    /// <param name="save">存档对象。</param>
    /// <returns>恢复后的宿主。</returns>
    /// <remarks>
    /// 恢复的关键是随机源内部状态：AI 选才会在并列时消耗随机数，
    /// 不恢复则读档后的 AI 决策与不存档时不同，后续状态必然分叉。
    /// </remarks>
    public static SimulationHost Restore(SaveFile save)
    {
        ArgumentNullException.ThrowIfNull(save);

        return new SimulationHost(
            save.State,
            save.RandomState,
            save.NextOrderId,
            save.MonthsElapsed,
            save.MaxMonths,
            save.MonthRandomStates);
    }

    /// <summary>
    /// 从存档恢复并指定月数上限。
    /// </summary>
    /// <param name="save">存档对象。</param>
    /// <param name="maxMonths">新的月数上限。</param>
    /// <returns>恢复后的宿主。</returns>
    public static SimulationHost Restore(SaveFile save, int maxMonths)
    {
        ArgumentNullException.ThrowIfNull(save);

        return new SimulationHost(
            save.State,
            save.RandomState,
            save.NextOrderId,
            save.MonthsElapsed,
            maxMonths,
            save.MonthRandomStates);
    }

    /// <summary>
    /// 判定终局（三方 AI 混战口径）。
    /// </summary>
    /// <remarks>
    /// 【口径说明】上游 VictoryChecker 是玩家口径（基准势力 IsPlayer=true），
    /// 三方全 AI 时不可用：没有基准势力 → 上游立即判「玩家全部城池易主」。
    /// 此处按混战口径判定：
    ///   · 仅剩 0 或 1 个势力仍拥有城池 → 终局（Elimination），胜方为幸存者；
    ///   · 跑满月数上限仍有多方对峙 → 僵持（Stalemate），无胜方。
    /// 这是 06 宿主层的判定，不修改上游代码；差异写入汇报疑问条。
    /// </remarks>
    private void EvaluateOutcome()
    {
        var survivors = new List<int>();
        foreach (var faction in State.Factions)
        {
            foreach (var city in State.Cities)
            {
                if (city.OwnerFactionId == faction.Id)
                {
                    survivors.Add(faction.Id);
                    break;
                }
            }
        }

        if (survivors.Count <= 1)
        {
            _finished = true;
            _gameOverReason = SimGameOverReason.Elimination;
            _winnerFactionId = survivors.Count == 1 ? survivors[0] : -1;
        }
    }

    /// <summary>
    /// 记录本月结算结果，并把结果映射回行动类型。
    /// </summary>
    /// <param name="results">本月的结算结果。</param>
    /// <remarks>
    /// 【映射依据】流水线第 7 步按 PendingOrders 的队列顺序逐条结算，
    /// 每条产一个结果，顺序一一对应。因此第 i 个结果对应上批入队指令的第 i 条。
    /// </remarks>
    private void RecordResults(List<ResolveResult> results)
    {
        for (int i = 0; i < results.Count; i++)
        {
            var code = results[i].Code;
            Stats.AddResult(code);

            if (code == ResolveResultCode.Success || i >= _pendingBatchActions.Count)
            {
                continue;
            }

            Stats.AddReject(_pendingBatchActions[i], code);
        }
    }

    /// <summary>
    /// 记录本月新增事件（哗变、攻陷）。
    /// </summary>
    /// <remarks>
    /// 【为什么按 EventLog 增量统计】流水线的 TurnResult.Events 在第 11 步
    /// 会把<b>整个</b> EventLog 追加进去（累计量，不是本月增量），
    /// 直接用它统计会重复计数。因此按 EventLog 长度增量统计。
    /// </remarks>
    private void RecordEvents()
    {
        // EventLog 由流水线与结算器累积，此处只关心本月的增量。
        Stats.EventCount = State.EventLog.Count;

        int mutiny = 0;
        int captured = 0;
        for (int i = Math.Max(0, _lastEventLogLength); i < State.EventLog.Count; i++)
        {
            var evt = State.EventLog[i];
            if (evt.EventKey == EventKeys.Mutiny)
            {
                mutiny++;
            }
            else if (evt.EventKey == EventKeys.CityCaptured)
            {
                captured++;
            }
        }

        Stats.MutinyCount += mutiny;
        Stats.CityCapturedCount += captured;
        _lastEventLogLength = State.EventLog.Count;
    }

    /// <summary>
    /// 计算下一月的年月（12 月进位到次年 1 月）。
    /// </summary>
    /// <param name="year">当前年。</param>
    /// <param name="month">当前月。</param>
    /// <returns>下一月的年与月。</returns>
    public static (int Year, int Month) NextMonth(int year, int month)
        => month >= 12 ? (year + 1, 1) : (year, month + 1);
}
