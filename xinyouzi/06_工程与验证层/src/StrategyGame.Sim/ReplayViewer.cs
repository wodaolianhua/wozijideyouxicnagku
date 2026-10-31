// ============================================================================
//  回放可视化输出
//  【为什么需要】回放日志是一串指令，不看指令本身没法判断「回放对不对」。
//  本类把回放日志渲染成人类可读的逐月流水，供人工核对与排查。
//
//  【纪律】只输出回放日志本身的内容，不引入任何新数据。
// ============================================================================

using System.Text;
using StrategyGame.Contracts;

namespace StrategyGame.Sim;

/// <summary>
/// 回放可视化输出。
/// </summary>
public static class ReplayViewer
{
    /// <summary>
    /// 把回放日志渲染成人类可读的逐月流水。
    /// </summary>
    /// <param name="log">回放日志。</param>
    /// <param name="maxMonths">最多输出多少个月。≤0 表示全部。</param>
    /// <returns>可读文本。</returns>
    public static string Render(ReplayLog log, int maxMonths = 0)
    {
        ArgumentNullException.ThrowIfNull(log);

        var sb = new StringBuilder();
        sb.AppendLine("=== 回放日志 ===");
        sb.AppendLine($"种子        : {log.Seed}");
        sb.AppendLine($"月数上限    : {log.MaxMonths}");
        sb.AppendLine($"实际月数    : {log.MonthsSimulated}");
        sb.AppendLine($"种子敏感化  : {(log.ApplySeedVariance ? "开" : "关")}");
        sb.AppendLine($"指令总数    : {log.Entries.Count}");
        sb.AppendLine();

        var byMonth = new Dictionary<int, List<ReplayEntry>>();
        foreach (var entry in log.Entries)
        {
            if (!byMonth.TryGetValue(entry.ProducedInMonth, out var list))
            {
                list = new List<ReplayEntry>();
                byMonth[entry.ProducedInMonth] = list;
            }

            list.Add(entry);
        }

        var months = byMonth.Keys.OrderBy(x => x);
        int limit = maxMonths <= 0 ? int.MaxValue : maxMonths;
        int printed = 0;

        foreach (int month in months)
        {
            if (printed >= limit)
            {
                sb.AppendLine($"... （省略剩余 {byMonth.Count - printed} 个月）");
                break;
            }

            var entries = byMonth[month];
            sb.AppendLine($"--- 第 {month} 月产出 {entries.Count} 条指令 ---");

            foreach (var entry in entries)
            {
                sb.AppendLine(
                    $"  #{entry.OrderId,-4} {entry.Action,-11} actor={entry.ActorType}/{entry.ActorId,-3} " +
                    $"target={entry.TargetId,-3} secondary={entry.SecondaryTargetId,-3} " +
                    $"amount={entry.Amount,-6} 执行于 {entry.ExecuteYear}-{entry.ExecuteMonth}");
            }

            printed++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 把回放日志渲染成按行动类型汇总的统计。
    /// </summary>
    /// <param name="log">回放日志。</param>
    /// <returns>可读文本。</returns>
    public static string RenderSummary(ReplayLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        var counts = new Dictionary<ActionType, int>();
        foreach (var entry in log.Entries)
        {
            counts.TryGetValue(entry.Action, out int v);
            counts[entry.Action] = v + 1;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== 回放日志 · 行动类型分布 ===");
        foreach (var pair in counts.OrderBy(p => p.Key))
        {
            sb.AppendLine($"  {pair.Key,-12} {pair.Value}");
        }

        sb.AppendLine();
        sb.AppendLine("=== 回放日志 · 拒绝码说明 ===");
        sb.AppendLine("  回放日志只记录「实际入队的指令」，不含结算结果；");
        sb.AppendLine("  结算码分布请看批量模拟 CSV 的 reject_* 列。");

        return sb.ToString();
    }
}
