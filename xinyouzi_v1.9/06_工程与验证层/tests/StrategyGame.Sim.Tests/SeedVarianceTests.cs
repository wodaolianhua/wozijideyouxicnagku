// ============================================================================
//  种子敏感化与 CSV 往返测试
//  【为什么需要】
//    上游 SampleData.CreateWorld(seed) 不消费 seed 生成世界内容，
//    全链路唯一随机消费点是 AI 选才并列打破，样例数据里无人并列。
//    06 层的 WorldVariator 用「轮换上游已有的三档初始资源」补上这一环，
//    这组测试锁住它的行为边界。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Sim;
using Xunit;

namespace StrategyGame.Sim.Tests;

/// <summary>
/// 种子敏感化验证。
/// </summary>
public class SeedVarianceTests
{
    /// <summary>
    /// 种子敏感化只重排既有档位，不新增任何数值。
    /// </summary>
    [Fact]
    public void 种子敏感化_只重排既有档位不新增数值()
    {
        var baseline = SimulationHost.CreateInitialWorld(12345);
        var baselineTiers = baseline.Factions
            .OrderBy(f => f.Id)
            .Select(f => (f.Resources.Grain, f.Resources.Silver, f.Resources.Soldier))
            .ToHashSet();

        for (int seed = 1; seed <= 50; seed++)
        {
            var world = SimulationHost.CreateInitialWorld(seed);
            WorldVariator.ApplyResourceRotation(world, seed);

            var rotatedTiers = world.Factions
                .OrderBy(f => f.Id)
                .Select(f => (f.Resources.Grain, f.Resources.Silver, f.Resources.Soldier))
                .ToHashSet();

            // 档位集合必须与基线完全一致 —— 只是重排，没有新数值。
            Assert.Equal(baselineTiers, rotatedTiers);
        }
    }

    /// <summary>
    /// 种子敏感化是确定性的：同 seed 两次施加结果必须一致。
    /// </summary>
    [Fact]
    public void 种子敏感化_同Seed结果一致()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var a = SimulationHost.CreateInitialWorld(seed);
            var b = SimulationHost.CreateInitialWorld(seed);

            WorldVariator.ApplyResourceRotation(a, seed);
            WorldVariator.ApplyResourceRotation(b, seed);

            Assert.Null(SimStateComparer.Diff(a, b));
        }
    }

    /// <summary>
    /// 种子敏感化必须真的产生差异 —— 否则等于没做。
    /// </summary>
    [Fact]
    public void 种子敏感化_不同Seed产生不同分配()
    {
        var seen = new HashSet<string>();

        for (int seed = 1; seed <= 50; seed++)
        {
            var world = SimulationHost.CreateInitialWorld(seed);
            WorldVariator.ApplyResourceRotation(world, seed);

            string key = string.Join(
                "|",
                world.Factions
                    .OrderBy(f => f.Id)
                    .Select(f => $"{f.Resources.Grain}/{f.Resources.Silver}/{f.Resources.Soldier}"));

            seen.Add(key);
        }

        // 三个势力、三档资源 → 最多 6 种排列，50 个 seed 应覆盖到多种。
        Assert.True(seen.Count > 1, "种子敏感化未产生任何差异。");
    }

    /// <summary>
    /// 种子敏感化不改变城池、部队、人才的归属 —— 只动资源。
    /// </summary>
    [Fact]
    public void 种子敏感化_不改变城池部队人才归属()
    {
        var baseline = SimulationHost.CreateInitialWorld(999);
        var world = SimulationHost.CreateInitialWorld(999);
        WorldVariator.ApplyResourceRotation(world, 999);

        Assert.Equal(
            baseline.Cities.Select(c => c.OwnerFactionId).OrderBy(x => x),
            world.Cities.Select(c => c.OwnerFactionId).OrderBy(x => x));

        Assert.Equal(
            baseline.Armies.Select(a => a.OwnerFactionId).OrderBy(x => x),
            world.Armies.Select(a => a.OwnerFactionId).OrderBy(x => x));

        Assert.Equal(
            baseline.Officers.Select(o => o.OwnerFactionId).OrderBy(x => x),
            world.Officers.Select(o => o.OwnerFactionId).OrderBy(x => x));
    }
}

/// <summary>
/// CSV 往返测试。
/// </summary>
public class CsvRoundTripTests
{
    /// <summary>
    /// 导出的 CSV 读回来后，关键指标必须完全一致。
    /// </summary>
    [Fact]
    public void CSV_导出后读回_关键指标一致()
    {
        var simulator = new BatchSimulator();
        var (results, report) = simulator.Run(new BatchOptions
        {
            Games = 16,
            BaseSeed = 777001,
            MaxMonths = 60,
            Parallel = false,
        });

        string tempDir = PackagePaths.GetTempFolder();
        string path = Path.Combine(tempDir, $"roundtrip_{Guid.NewGuid():N}.csv");

        try
        {
            CsvExporter.WriteToFile(results, path);
            var loaded = CsvReader.Read(path);

            Assert.Equal(results.Count, loaded.Count);

            for (int i = 0; i < results.Count; i++)
            {
                Assert.Equal(results[i].GameIndex, loaded[i].GameIndex);
                Assert.Equal(results[i].Seed, loaded[i].Seed);
                Assert.Equal(results[i].WinnerFactionId, loaded[i].WinnerFactionId);
                Assert.Equal(results[i].GameOverReason, loaded[i].GameOverReason);
                Assert.Equal(results[i].MonthsSurvived, loaded[i].MonthsSurvived);
                Assert.Equal(results[i].AiOrderTotal, loaded[i].AiOrderTotal);
                Assert.Equal(results[i].NotImplementedCount, loaded[i].NotImplementedCount);
                Assert.Equal(results[i].CityCapturedCount, loaded[i].CityCapturedCount);
                Assert.Equal(results[i].MutinyCount, loaded[i].MutinyCount);
            }

            // 聚合后关键总数也必须一致。
            var reloadedReport = CsvReader.Aggregate(loaded, report.BaseSeed, report.MaxMonths);
            Assert.Equal(report.TotalGames, reloadedReport.TotalGames);
            Assert.Equal(report.TotalMonths, reloadedReport.TotalMonths);
            Assert.Equal(report.NotImplementedTotal, reloadedReport.NotImplementedTotal);
            Assert.Equal(report.TotalCityCaptured, reloadedReport.TotalCityCaptured);
            Assert.Equal(report.TotalMutiny, reloadedReport.TotalMutiny);
            Assert.Equal(report.StalemateGames, reloadedReport.StalemateGames);
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
    /// CSV 行解析必须遵守标准引号转义规则。
    /// </summary>
    /// <remarks>
    /// 用例 3 是「字段值含双引号 + 后面还有一列」的标准形式：
    /// 原始行 `"say""hi""",x` 解析为两个字段 —— `say"hi"` 与 `x`。
    /// 用例 4 是同一字段值独占一行：原始行 `"say""hi"""` 解析为一个字段 `say"hi"`。
    /// </remarks>
    [Theory]
    [InlineData("a,b,c", 3)]
    [InlineData("\"a,b\",c", 2)]
    [InlineData("\"say\"\"hi\"\"\",x", 2)]
    [InlineData("\"say\"\"hi\"\"\"", 1)]
    public void CSV_行解析遵守引号转义(string line, int expectedFields)
    {
        Assert.Equal(expectedFields, CsvReader.SplitLine(line).Length);
    }
}
