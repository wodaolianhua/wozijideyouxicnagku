// ============================================================================
//  存档测试 —— 读档续跑必须与不存档一致
//  【环境铁律】临时文件写本包 .tmp/，禁止使用 Path.GetTempPath()（会写 C 盘）。
// ============================================================================

using System.Text.Json;
using StrategyGame.Contracts;
using StrategyGame.Sim;
using Xunit;

namespace StrategyGame.Sim.Tests;

/// <summary>
/// 存档与读档验证。
/// </summary>
public class SaveSystemTests
{
    /// <summary>
    /// 第 6 月存档 → 读档 → 继续模拟，与不存档一口气跑到底的状态逐字段相等。
    /// </summary>
    [Fact]
    public void 第6月存档读档_继续模拟_与不存档一致()
    {
        const int seed = 606060;
        const int saveMonth = 6;

        // ===== 参照：一口气跑到底 =====
        var reference = new SimulationHost(seed, 120);
        reference.RunToEnd();

        // ===== 分支：跑到第 6 月存档，读档后续跑 =====
        var branch = new SimulationHost(seed, 120);
        for (int i = 0; i < saveMonth; i++)
        {
            branch.RunMonth();
        }

        Assert.Equal(saveMonth, branch.MonthsElapsed);

        var save = branch.BuildSave();
        string json = SaveSystem.Serialize(save);
        var restored = SaveSystem.Deserialize(json);

        var continued = SimulationHost.Restore(restored);
        continued.RunToEnd();

        string? diff = SimStateComparer.Diff(reference.State, continued.State);
        Assert.Null(diff);
        Assert.Equal(reference.MonthsElapsed, continued.MonthsElapsed);
        Assert.Equal(reference.WinnerFactionId, continued.WinnerFactionId);
    }

    /// <summary>
    /// 存档 JSON 必须含 Seed 与 Difficulty 字段（反序列化断言）。
    /// </summary>
    [Fact]
    public void 存档JSON_含Seed与Difficulty字段()
    {
        var host = new SimulationHost(70001, 30);
        for (int i = 0; i < 10; i++)
        {
            host.RunMonth();
        }

        var save = host.BuildSave();
        string json = SaveSystem.Serialize(save);

        // JSON 文本层面必须出现这两个字段名。
        Assert.Contains("\"Seed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Difficulty\"", json, StringComparison.Ordinal);

        // 反序列化层面必须能读回且值正确。
        var restored = SaveSystem.Deserialize(json);
        Assert.Equal(host.State.Seed, restored.State.Seed);
        Assert.Equal(host.State.Difficulty, restored.State.Difficulty);
        Assert.Equal(DifficultyLevel.Normal, restored.State.Difficulty);
        Assert.Equal(70001, restored.State.Seed);
    }

    /// <summary>
    /// 存档必须含已模拟月数、OrderId 计数器、格式版本号。
    /// </summary>
    [Fact]
    public void 存档_含月数与OrderId计数器与格式版本()
    {
        var host = new SimulationHost(70002, 30);
        for (int i = 0; i < 6; i++)
        {
            host.RunMonth();
        }

        var save = host.BuildSave();

        Assert.Equal(1, save.FormatVersion);
        // 新世界第 8 月即终局（03R6+04R4 后），采样点取终局前第 6 月。
        Assert.Equal(6, save.MonthsElapsed);
        Assert.Equal(host.NextOrderId, save.NextOrderId);
        Assert.True(save.NextOrderId >= 1);
    }

    /// <summary>
    /// 存档文件读写往返（临时文件写本包 .tmp/）。
    /// </summary>
    [Fact]
    public void 存档文件_读写往返一致()
    {
        string tempDir = PackagePaths.GetTempFolder();
        string path = Path.Combine(tempDir, $"save_test_{Guid.NewGuid():N}.json");

        try
        {
            var host = new SimulationHost(70003, 30);
            for (int i = 0; i < 12; i++)
            {
                host.RunMonth();
            }

            var save = host.BuildSave();
            SaveSystem.WriteToFile(save, path);

            Assert.True(File.Exists(path), "存档文件未生成。");

            var loaded = SaveSystem.ReadFromFile(path);
            Assert.Equal(save.State.Seed, loaded.State.Seed);
            Assert.Equal(save.MonthsElapsed, loaded.MonthsElapsed);
            Assert.Equal(save.NextOrderId, loaded.NextOrderId);
            Assert.Null(SimStateComparer.Diff(save.State, loaded.State));
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
    /// 存档的 WorldState 必须能被 System.Text.Json 直接往返（无接口类型字段）。
    /// </summary>
    [Fact]
    public void 存档_WorldState可JSON直接往返()
    {
        var host = new SimulationHost(70004, 30);
        host.RunToEnd();

        string json = JsonSerializer.Serialize(host.State);
        var restored = JsonSerializer.Deserialize<WorldState>(json);

        Assert.NotNull(restored);
        Assert.Null(SimStateComparer.Diff(host.State, restored));
    }
}
