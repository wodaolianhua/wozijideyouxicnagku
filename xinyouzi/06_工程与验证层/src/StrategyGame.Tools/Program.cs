// ============================================================================
//  批量模拟命令行入口
//  【用法】
//    dotnet run --project StrategyGame.Tools -- simulate --games 1000 --seed 12345 --out result.csv
//
//  【纪律】参数解析手写，禁止引命令行第三方包。
//  【环境】默认输出路径必须落在 E 盘本包内（out/result.csv）。
// ============================================================================

using System.Diagnostics;
using System.Globalization;
using StrategyGame.Sim;

namespace StrategyGame.Tools;

/// <summary>
/// CLI 入口。
/// </summary>
public static class Program
{
    /// <summary>
    /// 入口点。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。0 表示成功。</returns>
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[错误] {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 解析参数并执行。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。</returns>
    private static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        string command = args[0];

        switch (command)
        {
            case "simulate":
                return RunSimulate(ParseOptions(args));
            case "analyze":
                return RunAnalyze(args);
            case "replay":
                return RunReplay(args);
            case "--help":
            case "-h":
            case "help":
                PrintUsage();
                return 0;
            default:
                Console.Error.WriteLine($"[错误] 未知命令「{command}」。");
                PrintUsage();
                return 2;
        }
    }

    /// <summary>
    /// replay 子命令：跑一局、记录回放日志、重放并校验逐字段相等。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。</returns>
    private static int RunReplay(string[] args)
    {
        int seed = 12345;
        int months = SimulationHost.DefaultMaxMonths;
        bool variance = true;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed":
                    seed = ParseInt(args, ref i, "--seed");
                    break;
                case "--months":
                    months = ParseInt(args, ref i, "--months");
                    break;
                case "--strict-world":
                    variance = false;
                    break;
                default:
                    Console.Error.WriteLine($"[错误] 未知参数「{args[i]}」。");
                    return 2;
            }
        }

        var host = new SimulationHost(seed, months, variance);
        host.RunToEnd();

        var log = ReplaySystem.Record(host);
        var replayed = ReplaySystem.Replay(log);

        Console.WriteLine(ReplayViewer.RenderSummary(log));
        Console.WriteLine();
        Console.WriteLine("=== 回放校验 ===");
        Console.WriteLine($"原模拟月数  : {host.MonthsElapsed}");
        Console.WriteLine($"回放月数    : {replayed.MonthsSimulated}");
        Console.WriteLine($"原模拟胜方  : {host.WinnerFactionId}");
        Console.WriteLine($"回放胜方    : {replayed.WinnerFactionId}");

        string? diff = SimStateComparer.Diff(host.State, replayed.FinalState);
        if (diff == null)
        {
            Console.WriteLine("终局状态    : 逐字段相等 ✓");
            return 0;
        }

        Console.Error.WriteLine("[失败] 回放终局与原模拟不一致：");
        Console.Error.WriteLine(diff);
        return 1;
    }

    /// <summary>
    /// analyze 子命令：读回 CSV 并产出平衡分析报告。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。</returns>
    private static int RunAnalyze(string[] args)
    {
        string? csvPath = null;
        string? reportPath = null;
        int games = 1000;
        int baseSeed = 12345;
        int months = SimulationHost.DefaultMaxMonths;
        bool variance = true;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--csv":
                    csvPath = ParseString(args, ref i, "--csv");
                    break;
                case "--out":
                    reportPath = ParseString(args, ref i, "--out");
                    break;
                case "--games":
                    games = ParseInt(args, ref i, "--games");
                    break;
                case "--seed":
                    baseSeed = ParseInt(args, ref i, "--seed");
                    break;
                case "--months":
                    months = ParseInt(args, ref i, "--months");
                    break;
                case "--strict-world":
                    variance = false;
                    break;
                default:
                    Console.Error.WriteLine($"[错误] 未知参数「{args[i]}」。");
                    return 2;
            }
        }

        csvPath ??= Path.Combine(PackagePaths.GetOutputFolder(), "result.csv");

        reportPath ??= DeriveReportPath(csvPath);

        if (!File.Exists(csvPath))
        {
            Console.Error.WriteLine($"[错误] CSV 不存在：{csvPath}");
            return 1;
        }

        var results = CsvReader.Read(csvPath);
        if (results.Count == 0)
        {
            Console.Error.WriteLine("[错误] CSV 无数据行。");
            return 1;
        }

        var report = CsvReader.Aggregate(results, baseSeed, months);
        BalanceReportWriter.WriteToFile(report, results, TimeSpan.Zero, variance, reportPath, Path.GetFileNameWithoutExtension(csvPath));

        Console.WriteLine($"已读入 {results.Count} 局，报告输出：{reportPath}");
        return 0;
    }

    /// <summary>
    /// 解析 simulate 子命令的参数。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>参数对象。</returns>
    private static BatchOptions ParseOptions(string[] args)
    {
        var options = new BatchOptions
        {
            Games = 1000,
            BaseSeed = 12345,
            MaxMonths = SimulationHost.DefaultMaxMonths,
            Parallel = true,
        };

        string? outPath = null;
        string? reportPathArg = null;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--games":
                    options.Games = ParseInt(args, ref i, "--games");
                    break;
                case "--seed":
                    options.BaseSeed = ParseInt(args, ref i, "--seed");
                    break;
                case "--months":
                    options.MaxMonths = ParseInt(args, ref i, "--months");
                    break;
                case "--out":
                    outPath = ParseString(args, ref i, "--out");
                    break;
                case "--report":
                    reportPathArg = ParseString(args, ref i, "--report");
                    break;
                case "--serial":
                    options.Parallel = false;
                    break;
                case "--strict-world":
                    // 严格照抄上游 SampleData.CreateWorld(seed)，不做种子敏感化。
                    // 用于复现「上游不消费 seed → 各局完全相同」的基线行为。
                    options.ApplySeedVariance = false;
                    break;
                default:
                    Console.Error.WriteLine($"[错误] 未知参数「{arg}」。");
                    throw new ArgumentException($"未知参数「{arg}」。");
            }
        }

        OutputPath = PackagePaths.ResolveOutputPath(outPath);
        ReportPath = reportPathArg;
        return options;
    }

    /// <summary>解析出的输出路径。</summary>
    private static string? OutputPath;

    /// <summary>解析出的分析报告路径（--report）；为空时按 CSV 主名推导（CA-1）。</summary>
    private static string? ReportPath;

    /// <summary>
    /// 执行批量模拟。
    /// </summary>
    /// <param name="options">参数。</param>
    /// <returns>进程退出码。</returns>
    private static int RunSimulate(BatchOptions options)
    {
        string outPath = OutputPath ?? PackagePaths.ResolveOutputPath(null);

        // 报告名跟随 CSV 主名（CA-1），不同批次 CSV 永不覆盖彼此报告。
        string reportPath = string.IsNullOrEmpty(ReportPath) ? DeriveReportPath(outPath) : ReportPath;

        Console.WriteLine("=== 06 工程与验证层 · 无头批量模拟 ===");
        Console.WriteLine($"局数        : {options.Games}");
        Console.WriteLine($"基准种子    : {options.BaseSeed}（第 i 局种子 = baseSeed + i）");
        Console.WriteLine($"月数上限    : {options.MaxMonths}");
        Console.WriteLine($"并行        : {(options.Parallel ? "是" : "否")}");
        Console.WriteLine($"种子敏感化  : {(options.ApplySeedVariance ? "开（按 seed 轮换三方初始资源档位）" : "关（严格照抄 SampleData.CreateWorld）")}");
        Console.WriteLine($"输出        : {outPath}");
        Console.WriteLine();

        var stopwatch = Stopwatch.StartNew();
        int lastReported = 0;

        var simulator = new BatchSimulator();
        var (results, report) = simulator.Run(new BatchOptions
        {
            Games = options.Games,
            BaseSeed = options.BaseSeed,
            MaxMonths = options.MaxMonths,
            Parallel = options.Parallel,
            ApplySeedVariance = options.ApplySeedVariance,
            Progress = done =>
            {
                int step = Math.Max(1, options.Games / 20);
                if (done == options.Games || done - lastReported >= step)
                {
                    lastReported = done;
                    double pct = options.Games == 0 ? 0 : done * 100.0 / options.Games;
                    Console.WriteLine($"  进度 {done}/{options.Games}（{pct:F1}%）");
                }
            },
        });

        stopwatch.Stop();

        CsvExporter.WriteToFile(results, outPath);

        BalanceReportWriter.WriteToFile(report, results, stopwatch.Elapsed, options.ApplySeedVariance, reportPath, Path.GetFileNameWithoutExtension(outPath));

        Console.WriteLine();
        Console.WriteLine("=== 结果 ===");
        Console.WriteLine($"总局数          : {report.TotalGames}");
        Console.WriteLine($"总耗时          : {stopwatch.Elapsed}");
        Console.WriteLine($"每局平均耗时    : {TimeSpan.FromTicks(stopwatch.Elapsed.Ticks / Math.Max(1, options.Games))}");
        Console.WriteLine($"CSV 输出        : {outPath}");
        Console.WriteLine($"分析报告        : {reportPath}");
        Console.WriteLine();
        Console.WriteLine($"基准槽位(槽0)胜率 : {report.BaselineSlotWinRatePerMille / 100.0:F2}%（三方 AI 混战口径，非真实玩家胜率）");
        Console.WriteLine($"平均存活/终局月数 : {report.AverageMonthsSurvived:F2}");
        Console.WriteLine($"10 回合内终局比例 : {report.EarlyEndRatePerMille / 100.0:F2}%");
        Console.WriteLine($"僵持率(120月未决) : {report.StalemateRatePerMille / 100.0:F2}%");
        Console.WriteLine($"NotImplemented 总数: {report.NotImplementedTotal}");

        if (report.NotImplementedTotal != 0)
        {
            Console.Error.WriteLine("[失败] RejectedNotImplemented 计数必须为 0，实际不为 0。链路存在非法行动。");
            return 1;
        }

        return 0;
    }

    /// <summary>报告路径默认跟随 CSV：同目录、同主文件名，命名「平衡分析_<CSV主名>.md」。
    /// 例：out/result_v16.csv → out/平衡分析_result_v16.md；out/result.csv → out/平衡分析_result.md。</summary>
    private static string DeriveReportPath(string csvPath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? PackagePaths.GetOutputFolder();
        return Path.Combine(dir, $"平衡分析_{Path.GetFileNameWithoutExtension(csvPath)}.md");
    }

    /// <summary>
    /// 解析整型参数。
    /// </summary>
    /// <param name="args">参数数组。</param>
    /// <param name="index">当前索引（引用传递，解析后指向值所在位置）。</param>
    /// <param name="name">参数名（报错用）。</param>
    /// <returns>整数值。</returns>
    private static int ParseInt(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"参数 {name} 缺少值。");
        }

        index++;
        if (!int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ArgumentException($"参数 {name} 的值「{args[index]}」不是合法整数。");
        }

        return value;
    }

    /// <summary>
    /// 解析字符串参数。
    /// </summary>
    /// <param name="args">参数数组。</param>
    /// <param name="index">当前索引（引用传递）。</param>
    /// <param name="name">参数名（报错用）。</param>
    /// <returns>字符串值。</returns>
    private static string ParseString(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"参数 {name} 缺少值。");
        }

        index++;
        return args[index];
    }

    /// <summary>
    /// 打印用法。
    /// </summary>
    private static void PrintUsage()
    {
        Console.WriteLine("用法：");
        Console.WriteLine("  dotnet run --project StrategyGame.Tools -- simulate [--games <N>] [--seed <S>] [--months <M>] [--out <csv路径>] [--report <报告路径>] [--serial]");
        Console.WriteLine();
        Console.WriteLine("参数：");
        Console.WriteLine("  --games   局数，默认 1000");
        Console.WriteLine("  --seed    基准种子，默认 12345（第 i 局种子 = seed + i）");
        Console.WriteLine("  --months  月数上限，默认 120");
        Console.WriteLine("  --out     CSV 输出路径，默认本包 out/result.csv");
        Console.WriteLine("  --report  分析报告输出路径，默认与 CSV 同目录，名为 平衡分析_<CSV主名>.md");
        Console.WriteLine("  --serial  串行执行（默认并行）");
        Console.WriteLine("  --strict-world  关闭种子敏感化，严格照抄上游 SampleData.CreateWorld(seed)");
    }
}
