// ============================================================================
//  CSV 回读 —— 把导出的 result.csv 读回为逐局结果
//  【为什么需要】平衡分析报告必须能基于「同一份 CSV」产出，
//  这样「报告里的数字」与「CSV 里的数字」必然一致，不存在两套口径。
//
//  【解析纪律】只认表头里有的列，缺列按 0 处理；
//  严格按引号转义规则解析（引号内逗号不算分隔符）。
// ============================================================================

using System.Globalization;
using System.Text;
using StrategyGame.Contracts;

namespace StrategyGame.Sim;

/// <summary>
/// CSV 回读器。
/// </summary>
public static class CsvReader
{
    /// <summary>
    /// 读取 CSV 为逐局结果。
    /// </summary>
    /// <param name="path">文件绝对路径。</param>
    /// <returns>逐局结果（按 game_index 升序）。</returns>
    public static List<GameResult> Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0)
        {
            return new List<GameResult>();
        }

        // 跳过 BOM。
        string header = lines[0].TrimStart('\uFEFF');
        string[] columns = SplitLine(header);

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < columns.Length; i++)
        {
            index[columns[i].Trim()] = i;
        }

        var results = new List<GameResult>(lines.Length - 1);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            string[] fields = SplitLine(lines[i]);
            var result = new GameResult
            {
                GameIndex = GetInt(fields, index, "game_index"),
                Seed = GetInt(fields, index, "seed"),
                WinnerFactionId = GetInt(fields, index, "winner_faction"),
                MonthsSurvived = GetInt(fields, index, "months_survived"),
                EventCount = GetInt(fields, index, "event_count"),
                MutinyCount = GetInt(fields, index, "mutiny_count"),
                CityCapturedCount = GetInt(fields, index, "city_captured_count"),
                AverageArmyMorale = GetInt(fields, index, "avg_army_morale"),
                LowMoraleRatioPerMille = GetInt(fields, index, "low_morale_ratio_permille"),
            };

            string reasonKey = GetString(fields, index, "game_over_reason");
            result.GameOverReason = reasonKey switch
            {
                "Elimination" => SimGameOverReason.Elimination,
                "Stalemate" => SimGameOverReason.Stalemate,
                _ => SimGameOverReason.None,
            };

            AddOrder(result, ActionType.Recruit, GetInt(fields, index, "orders_recruit"));
            AddOrder(result, ActionType.Fortify, GetInt(fields, index, "orders_fortify"));
            AddOrder(result, ActionType.Develop, GetInt(fields, index, "orders_develop"));
            AddOrder(result, ActionType.Dispatch, GetInt(fields, index, "orders_dispatch"));
            AddOrder(result, ActionType.AssignPost, GetInt(fields, index, "orders_assign_post"));
            AddOrder(result, ActionType.Rest, GetInt(fields, index, "orders_rest"));

            // orders_total 是权威列：按类型求和在「某类型计数为 0 时被跳过」
            // 的读回逻辑下可能与原始总数不一致，故直接读权威列。
            int totalOrders = GetInt(fields, index, "orders_total");
            if (totalOrders > 0)
            {
                result.AiOrderTotal = totalOrders;
            }
            else
            {
                foreach (var count in result.OrdersByAction.Values)
                {
                    result.AiOrderTotal += count;
                }
            }

            AddCode(result, ResolveResultCode.Success, GetInt(fields, index, "reject_success"));
            AddCode(result, ResolveResultCode.RejectedInvalidActor, GetInt(fields, index, "reject_invalid_actor"));
            AddCode(result, ResolveResultCode.RejectedNoResources, GetInt(fields, index, "reject_no_resources"));
            AddCode(result, ResolveResultCode.RejectedInvalidTarget, GetInt(fields, index, "reject_invalid_target"));
            AddCode(result, ResolveResultCode.RejectedTargetOccupied, GetInt(fields, index, "reject_target_occupied"));
            AddCode(result, ResolveResultCode.RejectedNoPost, GetInt(fields, index, "reject_no_post"));
            AddCode(result, ResolveResultCode.RejectedNotAdjacent, GetInt(fields, index, "reject_not_adjacent"));
            AddCode(result, ResolveResultCode.RejectedCooldown, GetInt(fields, index, "reject_cooldown"));
            AddCode(result, ResolveResultCode.RejectedNotImplemented, GetInt(fields, index, "reject_not_implemented"));
            AddCode(result, ResolveResultCode.RejectedInsufficientCapacity, GetInt(fields, index, "reject_insufficient_capacity"));
            AddCode(result, ResolveResultCode.RejectedTargetSieged, GetInt(fields, index, "reject_target_sieged"));
            AddCode(result, ResolveResultCode.InternalError, GetInt(fields, index, "internal_error"));

            for (int f = 0; f < 3; f++)
            {
                result.Factions.Add(new FactionEndState
                {
                    FactionId = f,
                    Cities = GetInt(fields, index, $"faction{f}_cities"),
                    Soldiers = GetInt(fields, index, $"faction{f}_soldiers"),
                    Grain = GetInt(fields, index, $"faction{f}_grain"),
                    Silver = GetInt(fields, index, $"faction{f}_silver"),
                    SoldierStock = GetInt(fields, index, $"faction{f}_soldier_stock"),
                    AverageSoldierStock = GetInt(fields, index, $"faction{f}_avg_soldier_stock"),
                });
            }

            results.Add(result);
        }

        results.Sort((a, b) => a.GameIndex.CompareTo(b.GameIndex));
        return results;
    }

    /// <summary>
    /// 聚合读回的逐局结果。
    /// </summary>
    /// <param name="results">逐局结果。</param>
    /// <param name="baseSeed">基准种子。</param>
    /// <param name="maxMonths">月数上限。</param>
    /// <returns>聚合报告。</returns>
    public static BatchReport Aggregate(IReadOnlyList<GameResult> results, int baseSeed, int maxMonths)
    {
        ArgumentNullException.ThrowIfNull(results);

        var report = new BatchReport
        {
            TotalGames = results.Count,
            BaseSeed = baseSeed,
            MaxMonths = maxMonths,
        };

        long moraleSum = 0;
        long lowMoraleSum = 0;
        var stockSum = new Dictionary<int, long>();
        var stockCount = new Dictionary<int, int>();

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
            moraleSum += result.AverageArmyMorale;
            lowMoraleSum += result.LowMoraleRatioPerMille;

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
                stockSum.TryGetValue(faction.FactionId, out long s);
                stockCount.TryGetValue(faction.FactionId, out int c);
                stockSum[faction.FactionId] = s + faction.AverageSoldierStock;
                stockCount[faction.FactionId] = c + 1;
            }
        }

        foreach (var (factionId, sum) in stockSum)
        {
            int count = stockCount[factionId];
            report.AverageSoldierStockByFaction[factionId] = count == 0 ? 0 : (int)(sum / count);
        }

        report.AverageArmyMorale = results.Count == 0 ? 0 : (int)(moraleSum / results.Count);
        report.LowMoraleRatioPerMille = results.Count == 0 ? 0 : (int)(lowMoraleSum / results.Count);
        report.NotImplementedTotal = report.ResultCodes.TryGetValue(
            ResolveResultCode.RejectedNotImplemented, out int notImpl) ? notImpl : 0;

        return report;
    }

    /// <summary>
    /// 拆一行 CSV 为字段（处理标准引号转义）。
    /// </summary>
    /// <param name="line">原始行。</param>
    /// <returns>字段数组。</returns>
    public static string[] SplitLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // 双写引号 → 字面引号。
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    /// <summary>
    /// 取字段的整数值。
    /// </summary>
    /// <param name="fields">字段数组。</param>
    /// <param name="index">列名 → 下标映射。</param>
    /// <param name="column">列名。</param>
    /// <returns>整数值；缺列或非法时返回 0。</returns>
    private static int GetInt(string[] fields, Dictionary<string, int> index, string column)
    {
        if (!index.TryGetValue(column, out int pos) || pos >= fields.Length)
        {
            return 0;
        }

        return int.TryParse(fields[pos], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
    }

    /// <summary>
    /// 取字段的字符串值。
    /// </summary>
    /// <param name="fields">字段数组。</param>
    /// <param name="index">列名 → 下标映射。</param>
    /// <param name="column">列名。</param>
    /// <returns>字符串值；缺列时返回空串。</returns>
    private static string GetString(string[] fields, Dictionary<string, int> index, string column)
    {
        if (!index.TryGetValue(column, out int pos) || pos >= fields.Length)
        {
            return string.Empty;
        }

        return fields[pos];
    }

    /// <summary>
    /// 写入行动发起数。
    /// </summary>
    /// <param name="result">结果对象。</param>
    /// <param name="action">行动类型。</param>
    /// <param name="count">次数。</param>
    private static void AddOrder(GameResult result, ActionType action, int count)
    {
        if (count > 0)
        {
            result.OrdersByAction[action] = count;
        }
    }

    /// <summary>
    /// 写入结算结果码计数。
    /// </summary>
    /// <param name="result">结果对象。</param>
    /// <param name="code">结果码。</param>
    /// <param name="count">次数。</param>
    private static void AddCode(GameResult result, ResolveResultCode code, int count)
    {
        if (count > 0)
        {
            result.ResultCodes[code] = count;
        }
    }
}
