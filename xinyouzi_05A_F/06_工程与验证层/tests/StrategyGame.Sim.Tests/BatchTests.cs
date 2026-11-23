// ============================================================================
//  批量模拟测试 —— 并行无害 + 无非法 Order + CSV 格式
// ============================================================================

using System.Text;
using StrategyGame.Contracts;
using StrategyGame.Sim;
using Xunit;

namespace StrategyGame.Sim.Tests;

/// <summary>
/// 批量模拟验证。
/// </summary>
public class BatchTests
{
    /// <summary>
    /// 串行 64 局与并行 64 局产出的 CSV 内容完全一致。
    /// </summary>
    [Fact]
    public void 串行64局与并行64局_CSV内容一致()
    {
        var simulator = new BatchSimulator();

        var (serial, _) = simulator.Run(new BatchOptions
        {
            Games = 64,
            BaseSeed = 20260101,
            MaxMonths = 120,
            Parallel = false,
        });

        var (parallel, _) = simulator.Run(new BatchOptions
        {
            Games = 64,
            BaseSeed = 20260101,
            MaxMonths = 120,
            Parallel = true,
        });

        string serialCsv = CsvExporter.ToCsv(serial);
        string parallelCsv = CsvExporter.ToCsv(parallel);

        Assert.Equal(serialCsv, parallelCsv);
        Assert.Equal(64, serial.Count);
        Assert.Equal(64, parallel.Count);
    }

    /// <summary>
    /// 百局模拟，零 NotImplemented 拒绝。
    /// </summary>
    [Fact]
    public void 百局模拟_零NotImplemented拒绝()
    {
        var simulator = new BatchSimulator();
        var (results, report) = simulator.Run(new BatchOptions
        {
            Games = 100,
            BaseSeed = 20260202,
            MaxMonths = 120,
            Parallel = true,
        });

        Assert.Equal(100, results.Count);

        foreach (var result in results)
        {
            Assert.Equal(0, result.NotImplementedCount);
        }

        Assert.Equal(0, report.NotImplementedTotal);
    }

    /// <summary>
    /// CSV 带 BOM、表头齐全、行数等于局数。
    /// </summary>
    [Fact]
    public void CSV_带BOM_表头齐全_行数等于局数()
    {
        var simulator = new BatchSimulator();
        var (results, _) = simulator.Run(new BatchOptions
        {
            Games = 12,
            BaseSeed = 20260303,
            MaxMonths = 60,
            Parallel = false,
        });

        string tempDir = PackagePaths.GetTempFolder();
        string path = Path.Combine(tempDir, $"csv_test_{Guid.NewGuid():N}.csv");

        try
        {
            CsvExporter.WriteToFile(results, path);

            byte[] bytes = File.ReadAllBytes(path);

            // BOM 校验：EF BB BF。
            Assert.True(bytes.Length >= 3, "CSV 文件为空。");
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);

            string text = Encoding.UTF8.GetString(bytes);
            string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 表头 + 12 行数据。
            Assert.Equal(results.Count + 1, lines.Length);

            string header = lines[0];
            foreach (var column in CsvExporter.Header)
            {
                Assert.Contains(column, header, StringComparison.Ordinal);
            }

            // 每行的列数必须与表头一致。
            int expectedColumns = CsvExporter.Header.Length;
            for (int i = 1; i < lines.Length; i++)
            {
                Assert.Equal(expectedColumns, lines[i].Split(',').Length);
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// 冒烟：10 局 × 120 月能在合理时间跑完。
    /// </summary>
    [Fact]
    public void 冒烟_10局120月_合理时间跑完()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var simulator = new BatchSimulator();
        var (results, _) = simulator.Run(new BatchOptions
        {
            Games = 10,
            BaseSeed = 20260404,
            MaxMonths = 120,
            Parallel = true,
        });

        stopwatch.Stop();

        Assert.Equal(10, results.Count);
        Assert.All(results, r => Assert.True(r.MonthsSurvived > 0));

        // 10 局 × 120 月应远快于 1000 局的 5 分钟预算，这里给 120 秒的宽松上限。
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(120),
            $"10 局 120 月耗时 {stopwatch.Elapsed}，超出预期。");
    }

    /// <summary>
    /// CSV 转义：含逗号 / 引号的字段必须被正确转义。
    /// </summary>
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say\"hi\"", "\"say\"\"hi\"\"\"")]
    public void CSV_字段转义正确(string input, string expected)
    {
        Assert.Equal(expected, CsvExporter.Escape(input));
    }

    /// <summary>
    /// game_over_reason 必须是稳定标识符（枚举名），不得含中文长句。
    /// </summary>
    [Fact]
    public void 终局原因_使用稳定标识符()
    {
        var simulator = new BatchSimulator();
        var (results, _) = simulator.Run(new BatchOptions
        {
            Games = 8,
            BaseSeed = 20260505,
            MaxMonths = 120,
            Parallel = false,
        });

        var allowed = new[] { "Elimination", "Stalemate", "None" };

        foreach (var result in results)
        {
            Assert.Contains(result.GameOverReasonKey, allowed);
        }
    }
}
