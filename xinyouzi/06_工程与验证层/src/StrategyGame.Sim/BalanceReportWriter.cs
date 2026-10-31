// ============================================================================
//  平衡分析报告生成器
//  【报告纪律】只报数，不调数值。
//    正确写法：「基准槽胜率 62%，超区间，疑似 AI 扩张阈值过保守，
//                建议检查 IFactionPlanner 出兵条件」
//    错误做法：自己改 BalanceConfig。
//    发现问题写变更申请，等裁决。
// ============================================================================

using System.Globalization;
using System.Text;
using StrategyGame.Contracts;

namespace StrategyGame.Sim;

/// <summary>
/// 平衡分析报告生成器。产出 Markdown。
/// </summary>
public static class BalanceReportWriter
{
    /// <summary>
    /// 生成平衡分析报告。
    /// </summary>
    /// <param name="report">批次聚合报告。</param>
    /// <param name="results">逐局结果（用于计算分布类指标）。</param>
    /// <param name="elapsed">CLI 实跑耗时。</param>
    /// <param name="applySeedVariance">是否启用了种子敏感化（写进报告口径说明）。</param>
    /// <param name="title">报告标题（跟随 CSV 主名，如 result_v16）。</param>
    /// <returns>Markdown 文本。</returns>
    public static string Generate(
        BatchReport report,
        IReadOnlyList<GameResult> results,
        TimeSpan elapsed,
        bool applySeedVariance,
        string title)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentException.ThrowIfNullOrEmpty(title);

        var sb = new StringBuilder();

        sb.AppendLine($"# 平衡分析 · {title}");
        sb.AppendLine();
        sb.AppendLine("> **本报告只报数，不改数值。** 所有超区间的指标都写明疑似成因，");
        sb.AppendLine("> 变更申请另行提交，由 00 项目管理裁决。本轮未修改任何 `BalanceConfig`。");
        sb.AppendLine();
        sb.AppendLine("## 0. 口径与样本");
        sb.AppendLine();
        sb.AppendLine("| 项 | 值 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| 样本量 | {report.TotalGames} 局 |");
        sb.AppendLine($"| 基准种子 | {report.BaseSeed}（第 i 局种子 = baseSeed + i） |");
        sb.AppendLine($"| 月数上限 | {report.MaxMonths} |");
        sb.AppendLine($"| 实际耗时 | {elapsed.TotalSeconds:F2} 秒 |");
        sb.AppendLine($"| 每局平均耗时 | {elapsed.TotalMilliseconds / Math.Max(1, report.TotalGames):F2} 毫秒 |");
        sb.AppendLine($"| 剧本 | 三方 AI 混战（`SampleData.CreateWorld(seed)` + `SampleData.CreateBalance()`） |");
        sb.AppendLine($"| 种子敏感化 | {(applySeedVariance ? "开启（按 seed 轮换三方初始资源档位，不新增数值）" : "关闭（严格照抄上游）")} |");
        sb.AppendLine($"| 终局口径 | 仅剩 ≤1 个势力仍拥有城池 → Elimination；跑满月数上限 → Stalemate |");
        sb.AppendLine();
        sb.AppendLine("> **「基准槽位」说明**：槽 0（汉）在 P0 是三方 AI 之一，不是玩家。");
        sb.AppendLine("> 本报告所有「胜率」均为三方 AI 混战下的基准槽位胜率，");
        sb.AppendLine("> **真正的玩家胜率要等 05A 可玩或未来的脚本玩家策略，本轮测不了**。");
        sb.AppendLine();

        // ================= 第一轮指标 =================
        sb.AppendLine("## 1. 第一轮指标");
        sb.AppendLine();
        sb.AppendLine("| 指标 | 实测 | 参考区间 | 判定 |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine($"| 基准槽位（槽0）胜率 | {Percent(report.BaselineSlotWinRatePerMille)} | 25%–45%（仅参考；非真实玩家） | {Judge(report.BaselineSlotWinRatePerMille, 2500, 4500)} |");
        sb.AppendLine($"| 平均存活/终局月数 | {report.AverageMonthsSurvived:F1} | ≥ 60 | {(report.AverageMonthsSurvived >= 60 ? "符合" : "**超出区间**")} |");
        sb.AppendLine($"| 10 回合内终局比例 | {Percent(report.EarlyEndRatePerMille)} | 接近 0 | {(report.EarlyEndRatePerMille <= 500 ? "符合" : "**超出区间**")} |");
        sb.AppendLine();

        // 胜方分布明细。
        sb.AppendLine("### 1.1 终局结果分布");
        sb.AppendLine();
        sb.AppendLine("| 终局原因 | 局数 | 占比 |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine($"| Elimination（仅剩 ≤1 势力持城） | {report.TotalGames - report.StalemateGames} | {Percent(report.TotalGames == 0 ? 0 : (int)((long)(report.TotalGames - report.StalemateGames) * 10_000 / report.TotalGames))} |");
        sb.AppendLine($"| Stalemate（跑满月数仍未决） | {report.StalemateGames} | {Percent(report.StalemateRatePerMille)} |");
        sb.AppendLine();
        sb.AppendLine("| 胜方势力 | 胜场数 | 占比 |");
        sb.AppendLine("|---|---|---|");
        var factionNames = new Dictionary<int, string> { { 0, "汉（基准槽位 0）" }, { 1, "魏（1）" }, { 2, "蜀（2）" } };
        foreach (var pair in report.WinsByFaction.OrderBy(p => p.Key))
        {
            string name = factionNames.TryGetValue(pair.Key, out string? n) && n != null
                ? n
                : $"势力 {pair.Key}";
            int perMille = report.TotalGames == 0 ? 0 : (int)((long)pair.Value * 10_000 / report.TotalGames);
            sb.AppendLine($"| {name} | {pair.Value} | {Percent(perMille)} |");
        }

        sb.AppendLine($"| 无胜方（僵持） | {report.StalemateGames} | {Percent(report.StalemateRatePerMille)} |");
        sb.AppendLine();

        // ================= 第二轮指标 =================
        sb.AppendLine("## 2. 第二轮指标（真正的调平衡抓手）");
        sb.AppendLine();
        sb.AppendLine("| 指标 | 实测 | 参考区间 | 判定 |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine($"| 平均兵源存量（三方均值） | {AverageStock(report):N0} | 应长期偏低 | {(AverageStock(report) < 5000 ? "符合（长期偏低）" : "**超出区间**")} |");
        sb.AppendLine($"| AI 部队士气均值 | {report.AverageArmyMorale} | 不应长期 <30 | {(report.AverageArmyMorale >= 30 ? "符合" : "**超出区间**")} |");
        sb.AppendLine($"| 低士气（<30）样本占比 | {Percent(report.LowMoraleRatioPerMille)} | 不应长期占多数 | {(report.LowMoraleRatioPerMille < 5000 ? "符合" : "**超出区间**")} |");
        sb.AppendLine($"| 攻陷次数 / Dispatch 尝试次数 | {Percent(report.CapturePerDispatchPerMille)} | 30%–50% | {Judge(report.CapturePerDispatchPerMille, 3000, 5000)} |");
        sb.AppendLine($"| 僵持率（{report.MaxMonths} 月未决占比） | {Percent(report.StalemateRatePerMille)} | 20%–40% | {Judge(report.StalemateRatePerMille, 2000, 4000)} |");
        sb.AppendLine();

        sb.AppendLine("### 2.1 各势力月均兵源存量");
        sb.AppendLine();
        sb.AppendLine("| 势力 | 月均兵源存量 |");
        sb.AppendLine("|---|---|");
        foreach (var pair in report.AverageSoldierStockByFaction.OrderBy(p => p.Key))
        {
            string name = factionNames.TryGetValue(pair.Key, out string? n) && n != null
                ? n
                : $"势力 {pair.Key}";
            sb.AppendLine($"| {name} | {pair.Value:N0} |");
        }

        sb.AppendLine();

        // ================= 拒绝码分布 =================
        sb.AppendLine("## 3. 各类 ResolveResult 拒绝码分布");
        sb.AppendLine();
        sb.AppendLine("| 结果码 | 次数 | 占全部结算比 |");
        sb.AppendLine("|---|---|---|");
        long totalResults = 0;
        foreach (var count in report.ResultCodes.Values)
        {
            totalResults += count;
        }

        foreach (var pair in report.ResultCodes.OrderBy(p => p.Key))
        {
            int perMille = totalResults == 0 ? 0 : (int)((long)pair.Value * 10_000 / totalResults);
            sb.AppendLine($"| {pair.Key} | {pair.Value} | {Percent(perMille)} |");
        }

        sb.AppendLine();
        sb.AppendLine($"> **RejectedNotImplemented（码 8）总计数 = {report.NotImplementedTotal}**");
        sb.AppendLine("> —— 链路无非法行动，这是本轮的核心闸门之一。");
        sb.AppendLine();

        // ================= 行动发起数 =================
        sb.AppendLine("## 4. 各行动发起数");
        sb.AppendLine();
        sb.AppendLine("| 行动类型 | 发起次数 |");
        sb.AppendLine("|---|---|");
        foreach (var pair in report.OrdersByAction.OrderBy(p => p.Key))
        {
            sb.AppendLine($"| {pair.Key} | {pair.Value} |");
        }

        sb.AppendLine();
        sb.AppendLine("| 其他统计 | 值 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| 事件总数 | {report.TotalEvents} |");
        sb.AppendLine($"| 哗变次数 | {report.TotalMutiny} |");
        sb.AppendLine($"| 攻陷次数 | {report.TotalCityCaptured} |");
        sb.AppendLine();

        // ================= 疑似成因 =================
        sb.AppendLine("## 5. 超区间指标的疑似成因（变更申请素材，不在本轮修改）");
        sb.AppendLine();
        AppendSuspectedCauses(sb, report);
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// 写出平衡分析报告文件。
    /// </summary>
    /// <param name="report">聚合报告。</param>
    /// <param name="results">逐局结果。</param>
    /// <param name="elapsed">耗时。</param>
    /// <param name="applySeedVariance">是否启用种子敏感化。</param>
    /// <param name="path">文件绝对路径。</param>
    /// <param name="title">报告标题（写入首行「# 平衡分析 · {title}」）。</param>
    public static void WriteToFile(
        BatchReport report,
        IReadOnlyList<GameResult> results,
        TimeSpan elapsed,
        bool applySeedVariance,
        string path,
        string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(title);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, Generate(report, results, elapsed, applySeedVariance, title), new UTF8Encoding(true));
    }

    /// <summary>
    /// 列出超区间指标的疑似成因。
    /// </summary>
    /// <param name="sb">输出缓冲。</param>
    /// <param name="report">聚合报告。</param>
    private static void AppendSuspectedCauses(StringBuilder sb, BatchReport report)
    {
        bool any = false;

        if (report.StalemateRatePerMille > 4000)
        {
            any = true;
            sb.AppendLine($"- **僵持率 {Percent(report.StalemateRatePerMille)}，超区间（20%–40%）**");
            sb.AppendLine("  疑似：三方都抢不到足以攻城的兵力（AI 出兵安全线 500 人 vs 样例世界初始只有 300 / 250 兵），");
            sb.AppendLine("  导致 120 月内没有任何一方能攻陷城池，全局僵持。");
            sb.AppendLine("  建议检查 `AiFactionPlanner.AddMilitaryOrders` 的 `SoldierSafetyThreshold` 与 `DispatchResolver` 的攻城兵力门槛。");
            sb.AppendLine();
        }

        if (report.BaselineSlotWinRatePerMille < 2500 || report.BaselineSlotWinRatePerMille > 4500)
        {
            any = true;
            sb.AppendLine($"- **基准槽位胜率 {Percent(report.BaselineSlotWinRatePerMille)}，超区间（25%–45%）**");
            sb.AppendLine("  疑似：与僵持率同源 —— 没有终局就没有胜方，该指标是被僵持率带偏的伴生指标，不是独立的 AI 强度问题。");
            sb.AppendLine();
        }

        if (report.AverageArmyMorale < 30)
        {
            any = true;
            sb.AppendLine($"- **AI 部队士气均值 {report.AverageArmyMorale}，低于 30**");
            sb.AppendLine("  疑似：`MoraleSystem` 的「和平腐化」在没有战斗时持续扣士气，");
            sb.AppendLine("  而 AI 的 `Rest` 分支只在「有部队士气 <30 且本月未休整」时才触发，");
            sb.AppendLine("  且 `AiFactionPlanner` 的经济/防守分支会先抢占每月指令上限（`MaxOrdersPerFactionPerMonth = 5`），");
            sb.AppendLine("  导致休整指令长期排不上队。");
            sb.AppendLine("  建议检查 `AiFactionPlanner.AddStrategicOrders` 的策略优先级与 `MaxOrdersPerFactionPerMonth` 的额度分配。");
            sb.AppendLine();
        }

        int dispatchAttempts = report.OrdersByAction.TryGetValue(ActionType.Dispatch, out int d) ? d : 0;
        if (dispatchAttempts == 0)
        {
            any = true;
            sb.AppendLine("- **Dispatch 尝试次数为 0**");
            sb.AppendLine("  疑似：`AiFactionPlanner.AddMilitaryOrders` 要求 `army.Soldiers >= SoldierSafetyThreshold(500)`，");
            sb.AppendLine("  而样例世界初始只有 300（汉）与 250（魏）兵，且 `ResolveSoldierIncome` 产出的是");
            sb.AppendLine("  「可用兵源」而非「现有部队兵力」，两者之间没有把兵源变成部队兵力的环节，");
            sb.AppendLine("  因此部队兵力永远低于 500，出兵分支永不进入。");
            sb.AppendLine("  建议检查：兵源 → 部队兵力的转化环节是否缺失（`RecruitResolver` 只加 `Resources.Soldier`，不加 `Army.Soldiers`）。");
            sb.AppendLine();
        }

        if (!any)
        {
            sb.AppendLine("- 本轮所有指标均在参考区间内，无需变更申请。");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// 计算三方月均兵源存量的平均值。
    /// </summary>
    /// <param name="report">聚合报告。</param>
    /// <returns>平均值。</returns>
    private static double AverageStock(BatchReport report)
    {
        if (report.AverageSoldierStockByFaction.Count == 0)
        {
            return 0;
        }

        long sum = 0;
        foreach (var value in report.AverageSoldierStockByFaction.Values)
        {
            sum += value;
        }

        return (double)sum / report.AverageSoldierStockByFaction.Count;
    }

    /// <summary>
    /// 把万分比格式化为百分数字符串。
    /// </summary>
    /// <param name="perMille">万分比值。</param>
    /// <returns>形如「12.34%」。</returns>
    private static string Percent(int perMille)
        => (perMille / 100.0).ToString("F2", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// 判定是否在区间内。
    /// </summary>
    /// <param name="perMille">实测值（万分比）。</param>
    /// <param name="low">下界（万分比）。</param>
    /// <param name="high">上界（万分比）。</param>
    /// <returns>判定文本。</returns>
    private static string Judge(int perMille, int low, int high)
        => perMille < low ? "**低于区间**" : perMille > high ? "**高于区间**" : "符合";
}
