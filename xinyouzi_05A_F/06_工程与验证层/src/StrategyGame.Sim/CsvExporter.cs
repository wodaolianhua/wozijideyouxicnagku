// ============================================================================
//  CSV 导出 —— 一行一局，表头英文列名
//  【编码铁律】UTF-8 带 BOM（new UTF8Encoding(true)），
//  否则 Excel 打开中文列内容会乱码。
//  【转义铁律】文本字段含逗号 / 引号 / 换行时做标准 CSV 转义。
//  【口径铁律】game_over_reason 用稳定标识符（枚举名），不塞中文长句。
// ============================================================================

using System.Text;
using StrategyGame.Contracts;

namespace StrategyGame.Sim;

/// <summary>
/// CSV 导出器。
/// </summary>
public static class CsvExporter
{
    /// <summary>CSV 的 UTF-8 编码（带 BOM）。</summary>
    public static readonly UTF8Encoding Encoding = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>表头列名（英文，按此顺序输出）。</summary>
    public static readonly string[] Header =
    {
        "game_index",
        "seed",
        "winner_faction",
        "game_over_reason",
        "months_survived",
        "faction0_cities",
        "faction0_soldiers",
        "faction0_grain",
        "faction0_silver",
        "faction0_soldier_stock",
        "faction1_cities",
        "faction1_soldiers",
        "faction1_grain",
        "faction1_silver",
        "faction1_soldier_stock",
        "faction2_cities",
        "faction2_soldiers",
        "faction2_grain",
        "faction2_silver",
        "faction2_soldier_stock",
        "orders_recruit",
        "orders_fortify",
        "orders_develop",
        "orders_dispatch",
        "orders_assign_post",
        "orders_rest",
        "orders_total",
        "reject_success",
        "reject_invalid_actor",
        "reject_no_resources",
        "reject_invalid_target",
        "reject_target_occupied",
        "reject_no_post",
        "reject_not_adjacent",
        "reject_cooldown",
        "reject_not_implemented",
        "reject_insufficient_capacity",
        "reject_target_sieged",
        "internal_error",
        "mutiny_count",
        "city_captured_count",
        "event_count",
        "avg_army_morale",
        "low_morale_ratio_permille",
        "faction0_avg_soldier_stock",
        "faction1_avg_soldier_stock",
        "faction2_avg_soldier_stock",
    };

    /// <summary>
    /// 把逐局结果导出为 CSV 文本。
    /// </summary>
    /// <param name="results">逐局结果。调用方应保证已按 gameIndex 排序。</param>
    /// <returns>CSV 文本。</returns>
    public static string ToCsv(IEnumerable<GameResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Header));

        foreach (var result in results)
        {
            sb.AppendLine(string.Join(",", BuildRow(result)));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 写出 CSV 文件。
    /// </summary>
    /// <param name="results">逐局结果。</param>
    /// <param name="path">文件绝对路径。</param>
    public static void WriteToFile(IEnumerable<GameResult> results, string path)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentException.ThrowIfNullOrEmpty(path);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, ToCsv(results), Encoding);
    }

    /// <summary>
    /// 构造一行数据。
    /// </summary>
    /// <param name="result">一局的结果。</param>
    /// <returns>该行的各列文本。</returns>
    private static string[] BuildRow(GameResult result)
    {
        var columns = new List<string>
        {
            result.GameIndex.ToString(),
            result.Seed.ToString(),
            result.WinnerFactionId.ToString(),
            result.GameOverReasonKey,
            result.MonthsSurvived.ToString(),
        };

        // 三个势力的终局状态（按 FactionId 升序固定输出）。
        var factions = new List<FactionEndState>(result.Factions);
        factions.Sort((a, b) => a.FactionId.CompareTo(b.FactionId));

        for (int i = 0; i < 3; i++)
        {
            if (i < factions.Count)
            {
                var f = factions[i];
                columns.Add(f.Cities.ToString());
                columns.Add(f.Soldiers.ToString());
                columns.Add(f.Grain.ToString());
                columns.Add(f.Silver.ToString());
                columns.Add(f.SoldierStock.ToString());
            }
            else
            {
                columns.Add("0");
                columns.Add("0");
                columns.Add("0");
                columns.Add("0");
                columns.Add("0");
            }
        }

        // 各行动发起数（固定顺序，避免依赖 Dictionary 遍历顺序）。
        columns.Add(GetOrderCount(result, ActionType.Recruit).ToString());
        columns.Add(GetOrderCount(result, ActionType.Fortify).ToString());
        columns.Add(GetOrderCount(result, ActionType.Develop).ToString());
        columns.Add(GetOrderCount(result, ActionType.Dispatch).ToString());
        columns.Add(GetOrderCount(result, ActionType.AssignPost).ToString());
        columns.Add(GetOrderCount(result, ActionType.Rest).ToString());
        columns.Add(result.AiOrderTotal.ToString());

        // 各类结算结果码。
        columns.Add(GetCodeCount(result, ResolveResultCode.Success).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.RejectedInvalidActor).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.RejectedNoResources).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.RejectedInvalidTarget).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.RejectedTargetOccupied).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.RejectedNoPost).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.RejectedNotAdjacent).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.RejectedCooldown).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.RejectedNotImplemented).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.RejectedInsufficientCapacity).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.RejectedTargetSieged).ToString());
        columns.Add(GetCodeCount(result, ResolveResultCode.InternalError).ToString());

        columns.Add(result.MutinyCount.ToString());
        columns.Add(result.CityCapturedCount.ToString());
        columns.Add(result.EventCount.ToString());
        columns.Add(result.AverageArmyMorale.ToString());
        columns.Add(result.LowMoraleRatioPerMille.ToString());

        for (int i = 0; i < 3; i++)
        {
            columns.Add(i < factions.Count ? factions[i].AverageSoldierStock.ToString() : "0");
        }

        return columns.Select(Escape).ToArray();
    }

    /// <summary>
    /// 取某行动类型的发起数。
    /// </summary>
    /// <param name="result">结果。</param>
    /// <param name="action">行动类型。</param>
    /// <returns>次数。</returns>
    private static int GetOrderCount(GameResult result, ActionType action)
        => result.OrdersByAction.TryGetValue(action, out int v) ? v : 0;

    /// <summary>
    /// 取某结算结果码的计数。
    /// </summary>
    /// <param name="result">结果。</param>
    /// <param name="code">结果码。</param>
    /// <returns>次数。</returns>
    private static int GetCodeCount(GameResult result, ResolveResultCode code)
        => result.ResultCodes.TryGetValue(code, out int v) ? v : 0;

    /// <summary>
    /// 标准 CSV 字段转义：含逗号 / 引号 / 换行时用双引号包裹，内部引号翻倍。
    /// </summary>
    /// <param name="field">原始字段文本。</param>
    /// <returns>转义后的字段文本。</returns>
    public static string Escape(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        bool needsQuote = field.Contains(',', StringComparison.Ordinal)
                         || field.Contains('"', StringComparison.Ordinal)
                         || field.Contains('\n', StringComparison.Ordinal)
                         || field.Contains('\r', StringComparison.Ordinal);

        if (!needsQuote)
        {
            return field;
        }

        return "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
