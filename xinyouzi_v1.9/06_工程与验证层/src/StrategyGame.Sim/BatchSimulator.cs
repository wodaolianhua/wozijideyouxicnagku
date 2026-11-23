// ============================================================================
//  无头批量模拟器 —— 本包最有价值的工程资产
//  【职责】跑 N 局 × M 月的无头模拟，聚合统计，导出 CSV 供 Excel 分析。
//
//  【并行安全性铁律】
//    每局独立构造 config / rng / applier / registry / processor / planner /
//    pipeline / state，禁止共享随机源与可变对象。
//    聚合结果按 gameIndex 排序后再写 CSV，保证并行不改变输出顺序。
//
//  【确定性铁律】禁止 DateTime.Now / Guid.NewGuid / Random.Shared；
//    任何排序聚合前先 OrderBy(Id)，不许依赖 Dictionary 遍历顺序。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Sim;

/// <summary>
/// 批量模拟参数。
/// </summary>
public sealed class BatchOptions
{
    /// <summary>局数。</summary>
    public int Games { get; set; } = 1000;

    /// <summary>基准种子。第 i 局的种子 = baseSeed + i。</summary>
    public int BaseSeed { get; set; } = 12345;

    /// <summary>月数上限。</summary>
    public int MaxMonths { get; set; } = SimulationHost.DefaultMaxMonths;

    /// <summary>是否并行。</summary>
    public bool Parallel { get; set; } = true;

    /// <summary>
    /// 是否启用种子敏感化（按 seed 轮换三方初始资源档位）。
    /// </summary>
    /// <remarks>
    /// 【为什么默认 true】
    /// 上游 03 的 <c>SampleData.CreateWorld(seed)</c> 只把 seed 存进状态，
    /// <b>不消费 seed 生成世界内容</b>；全链路唯一的随机消费点是 04 的
    /// AI 选才并列打破，而样例数据里五个人才的 Governance 互不相同、永远不并列。
    /// 结果：不启用时 1000 局跑出<b>完全相同的一局</b>，
    /// 胜率 / 僵持率等所有平衡指标都退化为常数，批量模拟器失去意义。
    /// 启用后按 seed 轮换上游样例中已有的三档初始资源（不新增任何数值），
    /// 让三方初始条件对称化，批量统计才有意义。
    /// 严格照抄上游的行为可用 <c>--strict-world</c> 复现。
    /// 详见 <see cref="WorldVariator"/>。
    /// </remarks>
    public bool ApplySeedVariance { get; set; } = true;

    /// <summary>进度回调（已完成局数）。可为 null。</summary>
    public Action<int>? Progress { get; set; }
}

/// <summary>
/// 无头批量模拟器。
/// </summary>
public sealed class BatchSimulator
{
    /// <summary>
    /// 跑一批模拟。
    /// </summary>
    /// <param name="options">参数。</param>
    /// <returns>逐局结果与聚合报告。</returns>
    public (List<GameResult> Results, BatchReport Report) Run(BatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Games <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Games), options.Games, "局数必须为正数。");
        }

        if (options.BaseSeed <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.BaseSeed), options.BaseSeed, "基准种子必须为正数（0 会被随机源替换为默认种子）。");
        }

        var results = new GameResult[options.Games];
        int done = 0;

        if (options.Parallel)
        {
            // 每局完全独立：各自 new 宿主，不共享任何可变对象。
            Parallel.For(0, options.Games, index =>
            {
                results[index] = SimulateOne(options.BaseSeed + index, options.MaxMonths, index, options.ApplySeedVariance);
                ReportProgress(options, ref done);
            });
        }
        else
        {
            for (int index = 0; index < options.Games; index++)
            {
                results[index] = SimulateOne(options.BaseSeed + index, options.MaxMonths, index, options.ApplySeedVariance);
                ReportProgress(options, ref done);
            }
        }

        // 按 gameIndex 排序：并行与串行产出完全一致的顺序。
        var ordered = new List<GameResult>(results);
        ordered.Sort((a, b) => a.GameIndex.CompareTo(b.GameIndex));

        return (ordered, Aggregate(ordered, options));
    }

    /// <summary>
    /// 跑一局。
    /// </summary>
    /// <param name="seed">本局种子。</param>
    /// <param name="maxMonths">月数上限。</param>
    /// <param name="gameIndex">局序。</param>
    /// <param name="applySeedVariance">是否启用种子敏感化。</param>
    /// <returns>本局结果。</returns>
    public static GameResult SimulateOne(int seed, int maxMonths, int gameIndex, bool applySeedVariance = true)
    {
        var host = new SimulationHost(seed, maxMonths, applySeedVariance);
        host.RunToEnd();
        return host.BuildResult(gameIndex, seed);
    }

    /// <summary>
    /// 聚合逐局结果。
    /// </summary>
    /// <param name="results">逐局结果。</param>
    /// <param name="options">参数。</param>
    /// <returns>聚合报告。</returns>
    private static BatchReport Aggregate(List<GameResult> results, BatchOptions options)
    {
        var report = new BatchReport
        {
            TotalGames = results.Count,
            BaseSeed = options.BaseSeed,
            MaxMonths = options.MaxMonths,
        };

        long moraleSum = 0;
        long moraleSamples = 0;
        long lowMoraleSamples = 0;

        // 各势力月均兵源存量：先按势力累加各局的月均值，再取批次均值。
        var stockSumByFaction = new Dictionary<int, long>();
        var stockCountByFaction = new Dictionary<int, int>();

        foreach (var result in results)
        {
            if (result.WinnerFactionId >= 0)
            {
                report.WinsByFaction.TryGetValue(result.WinnerFactionId, out int wins);
                report.WinsByFaction[result.WinnerFactionId] = wins + 1;
            }
            else
            {
                report.StalemateGames++;
            }

            if (result.MonthsSurvived <= 10)
            {
                report.EarlyEndGames++;
            }

            report.TotalMonths += result.MonthsSurvived;
            report.TotalEvents += result.EventCount;
            report.TotalMutiny += result.MutinyCount;
            report.TotalCityCaptured += result.CityCapturedCount;

            foreach (var (action, count) in result.OrdersByAction)
            {
                report.OrdersByAction.TryGetValue(action, out int v);
                report.OrdersByAction[action] = v + count;
            }

            foreach (var (code, count) in result.ResultCodes)
            {
                report.ResultCodes.TryGetValue(code, out int v);
                report.ResultCodes[code] = v + count;
            }

            foreach (var faction in result.Factions)
            {
                stockSumByFaction.TryGetValue(faction.FactionId, out long s);
                stockCountByFaction.TryGetValue(faction.FactionId, out int c);
                stockSumByFaction[faction.FactionId] = s + faction.AverageSoldierStock;
                stockCountByFaction[faction.FactionId] = c + 1;
            }

            moraleSum += (long)result.AverageArmyMorale;
            moraleSamples++;
            lowMoraleSamples += (long)result.LowMoraleRatioPerMille;
        }

        foreach (var (factionId, sum) in stockSumByFaction)
        {
            int count = stockCountByFaction[factionId];
            report.AverageSoldierStockByFaction[factionId] = count == 0 ? 0 : (int)(sum / count);
        }

        report.AverageArmyMorale = moraleSamples == 0 ? 0 : (int)(moraleSum / moraleSamples);
        report.LowMoraleRatioPerMille = moraleSamples == 0
            ? 0
            : (int)(lowMoraleSamples / moraleSamples);

        report.NotImplementedTotal = report.ResultCodes.TryGetValue(
            ResolveResultCode.RejectedNotImplemented, out int notImpl) ? notImpl : 0;

        return report;
    }

    /// <summary>
    /// 上报进度。
    /// </summary>
    /// <param name="options">参数。</param>
    /// <param name="done">已完成计数（引用传递）。</param>
    private static void ReportProgress(BatchOptions options, ref int done)
    {
        if (options.Progress == null)
        {
            return;
        }

        int current = Interlocked.Increment(ref done);
        options.Progress(current);
    }
}
