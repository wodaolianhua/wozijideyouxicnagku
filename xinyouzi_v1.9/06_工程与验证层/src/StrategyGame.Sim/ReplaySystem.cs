// ============================================================================
//  回放系统 —— 记录每月实际入队的指令，逐条重放
//  【铁律】回放不接 planner（AI），pipeline 的 planner 传 null。
//  回放终局状态必须与原模拟终局逐字段相等。
//
//  【为什么回放必须逐月恢复随机源状态】
//    一局里所有随机消费共用同一个 DeterministicRandom(seed)。
//    AI 选才在并列时会消耗随机数（BestFitPolicy.SelectBestFor）。
//    回放不接 AI，若不逐月恢复随机源内部状态，
//    两边的随机流位置就会错位。因此原模拟记录「每月开始时的随机源状态」，
//    回放时逐月 SetState 还原，保证随机流位置一一对应。
//
//  【为什么回放要逐条记录 OrderId 与执行年月】
//    PendingOrder 会被写进世界状态快照，逐字段比较时包含 OrderId 与
//    ExecuteYear/ExecuteMonth。回放若不还原这两项，状态比较必然不等。
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using StrategyGame.Contracts;
using StrategyGame.Core;

namespace StrategyGame.Sim;

/// <summary>
/// 回放日志中的一条指令记录。字段与 <see cref="PendingOrder"/> 一一对应。
/// </summary>
public sealed class ReplayEntry
{
    /// <summary>本局自增的指令 Id。</summary>
    public int OrderId { get; set; }

    /// <summary>执行者类型。</summary>
    public ActorType ActorType { get; set; }

    /// <summary>执行者 Id。</summary>
    public int ActorId { get; set; }

    /// <summary>行动类型。</summary>
    public ActionType Action { get; set; }

    /// <summary>目标 Id。</summary>
    public int TargetId { get; set; }

    /// <summary>次目标 Id。</summary>
    public int SecondaryTargetId { get; set; } = -1;

    /// <summary>数量。</summary>
    public int Amount { get; set; }

    /// <summary>执行年。</summary>
    public int ExecuteYear { get; set; }

    /// <summary>执行月。</summary>
    public int ExecuteMonth { get; set; }

    /// <summary>产出该指令的月份序号（1 基）。</summary>
    public int ProducedInMonth { get; set; }

    /// <summary>
    /// 从入队前的 <see cref="Order"/> 构造记录。
    /// </summary>
    /// <param name="order">指令。</param>
    /// <param name="month">产出月份序号。</param>
    /// <returns>记录对象。</returns>
    public static ReplayEntry FromOrder(Order order, int month)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new ReplayEntry
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
            ProducedInMonth = month,
        };
    }

    /// <summary>
    /// 还原为可入队的 <see cref="PendingOrder"/>。
    /// </summary>
    /// <returns>待执行指令。</returns>
    public PendingOrder ToPendingOrder()
        => new()
        {
            OrderId = OrderId,
            ActorType = ActorType,
            ActorId = ActorId,
            Action = Action,
            TargetId = TargetId,
            SecondaryTargetId = SecondaryTargetId,
            Amount = Amount,
            ExecuteYear = ExecuteYear,
            ExecuteMonth = ExecuteMonth,
        };
}

    /// <summary>
    /// 回放日志。
    /// </summary>
    public sealed class ReplayLog
    {
        /// <summary>本局的随机种子。</summary>
        public int Seed { get; set; }

        /// <summary>本局的月数上限。</summary>
        public int MaxMonths { get; set; } = SimulationHost.DefaultMaxMonths;

        /// <summary>实际推进的月数。</summary>
        public int MonthsSimulated { get; set; }

        /// <summary>
        /// 是否启用了种子敏感化。回放必须复现同一口径的初始世界。
        /// </summary>
        public bool ApplySeedVariance { get; set; }

        /// <summary>每月开始时的随机源状态。</summary>
        public List<int> MonthRandomStates { get; set; } = new();

        /// <summary>每月实际入队的指令。</summary>
        public List<ReplayEntry> Entries { get; set; } = new();
    }

/// <summary>
/// 回放结果。
/// </summary>
public sealed class ReplayResult
{
    /// <summary>回放终局的世界状态。</summary>
    public WorldState FinalState { get; set; } = new();

    /// <summary>回放推进的月数。</summary>
    public int MonthsSimulated { get; set; }

    /// <summary>回放终局的胜方势力 Id；无胜方为 -1。</summary>
    public int WinnerFactionId { get; set; } = -1;

    /// <summary>回放终局原因。</summary>
    public SimGameOverReason GameOverReason { get; set; }
}

/// <summary>
/// 回放系统。
/// </summary>
public static class ReplaySystem
{
    /// <summary>回放日志的 JSON 序列化选项。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// 从宿主抽取回放日志。
    /// </summary>
    /// <param name="host">宿主。</param>
    /// <returns>回放日志。</returns>
    public static ReplayLog Record(SimulationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new ReplayLog
        {
            Seed = host.State.Seed,
            MaxMonths = host.MaxMonths,
            MonthsSimulated = host.MonthsElapsed,
            ApplySeedVariance = host.ApplySeedVariance,
            MonthRandomStates = new List<int>(host.MonthRandomStates),
            Entries = new List<ReplayEntry>(host.ReplayEntries),
        };
    }

    /// <summary>
    /// 重放一份日志。
    /// </summary>
    /// <param name="log">回放日志。</param>
    /// <returns>回放结果。</returns>
    /// <remarks>
    /// 【关键】planner 传 null —— 回放期间不调用任何 AI。
    /// </remarks>
    public static ReplayResult Replay(ReplayLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        // 与原模拟完全相同的初始世界与构造顺序。
        var config = StrategyGame.Core.Sample.SampleData.CreateBalance();
        var rng = new StrategyGame.Core.Randomization.DeterministicRandom(log.Seed);
        var applier = new StateApplier();
        var registry = ResolverRegistry.CreateDefault(config, rng);
        var processor = new CommandProcessor(registry, applier);

        // 【回放不接 planner】AI 一律不参与。
        var pipeline = new TurnPipeline(
            processor,
            applier,
            config,
            eventSystemFactory: null,
            planner: null);

        var state = SimulationHost.CreateInitialWorld(log.Seed);

        // 复现与原模拟同一口径的初始世界（含种子敏感化）。
        // 少了这一步，回放的初始资源与原模拟不同，后续状态必然分叉。
        if (log.ApplySeedVariance)
        {
            WorldVariator.ApplyResourceRotation(state, log.Seed);
        }

        // 按月分组：第 k 月产出的指令，在第 k+1 月的流水线第 7 步被执行。
        var entriesByMonth = new Dictionary<int, List<ReplayEntry>>();
        foreach (var entry in log.Entries)
        {
            if (!entriesByMonth.TryGetValue(entry.ProducedInMonth, out var list))
            {
                list = new List<ReplayEntry>();
                entriesByMonth[entry.ProducedInMonth] = list;
            }

            list.Add(entry);
        }

        int months = 0;
        int winner = -1;
        var reason = SimGameOverReason.None;

        for (int month = 1; month <= log.MonthsSimulated; month++)
        {
            // 逐月恢复随机源状态，保证随机流位置与原模拟一致。
            int stateIndex = month - 1;
            if (stateIndex < log.MonthRandomStates.Count)
            {
                rng.SetState(log.MonthRandomStates[stateIndex]);
            }

            pipeline.RunMonth(state);
            months++;

            // 本月产出的指令入队，执行月沿用日志记录（与原模拟一致）。
            if (entriesByMonth.TryGetValue(month, out var produced))
            {
                foreach (var entry in produced)
                {
                    state.PendingOrders.Add(entry.ToPendingOrder());
                }
            }

            var outcome = EvaluateOutcome(state);
            if (outcome.HasValue)
            {
                winner = outcome.Value;
                reason = SimGameOverReason.Elimination;
                break;
            }
        }

        if (months >= log.MonthsSimulated && reason == SimGameOverReason.None)
        {
            reason = SimGameOverReason.Stalemate;
        }

        return new ReplayResult
        {
            FinalState = state,
            MonthsSimulated = months,
            WinnerFactionId = winner,
            GameOverReason = reason,
        };
    }

    /// <summary>
    /// 判定混战口径的终局。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>未终局返回 null；终局返回胜方 Id。</returns>
    /// <remarks>
    /// 与 <see cref="SimulationHost"/> 的判定口径完全一致，否则回放与原模拟
    /// 的月数会在同一处分叉。
    /// </remarks>
    private static int? EvaluateOutcome(WorldState state)
    {
        var survivors = new List<int>();
        foreach (var faction in state.Factions)
        {
            foreach (var city in state.Cities)
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
            return survivors.Count == 1 ? survivors[0] : -1;
        }

        return null;
    }
    /// <summary>
    /// 序列化回放日志为 JSON。
    /// </summary>
    /// <param name="log">回放日志。</param>
    /// <returns>JSON 文本。</returns>
    public static string Serialize(ReplayLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return JsonSerializer.Serialize(log, Options);
    }

    /// <summary>
    /// 从 JSON 文本反序列化回放日志。
    /// </summary>
    /// <param name="json">JSON 文本。</param>
    /// <returns>回放日志。</returns>
    public static ReplayLog Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        return JsonSerializer.Deserialize<ReplayLog>(json, Options)
               ?? throw new InvalidDataException("回放日志 JSON 反序列化结果为 null。");
    }
}
