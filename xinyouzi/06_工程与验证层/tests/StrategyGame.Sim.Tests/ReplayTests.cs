// ============================================================================
//  回放测试 —— 检验 Resolver 纯函数铁律
//  【核心断言】回放（不接 AI）与原模拟终局状态逐字段相等。
//  若不相等，说明链路某处违反纯函数约束（有隐式状态污染）。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Sim;
using Xunit;

namespace StrategyGame.Sim.Tests;

/// <summary>
/// 回放验证。
/// </summary>
public class ReplayTests
{
    /// <summary>
    /// 回放（不调用 planner）与原模拟终局逐字段相等。
    /// </summary>
    [Fact]
    public void 回放_不调用Planner_与原模拟逐字段相等()
    {
        const int seed = 31337;

        var original = new SimulationHost(seed, 120);
        original.RunToEnd();

        var log = ReplaySystem.Record(original);
        var replayed = ReplaySystem.Replay(log);

        string? diff = SimStateComparer.Diff(original.State, replayed.FinalState);
        Assert.Null(diff);
        Assert.Equal(original.MonthsElapsed, replayed.MonthsSimulated);
        Assert.Equal(original.WinnerFactionId, replayed.WinnerFactionId);
        Assert.Equal(original.GameOverReason, replayed.GameOverReason);
    }

    /// <summary>
    /// 回放日志必须记录了实际入队的指令，且每条都带完整字段。
    /// </summary>
    [Fact]
    public void 回放日志_记录入队指令全字段()
    {
        var host = new SimulationHost(555, 60);
        host.RunToEnd();

        var log = ReplaySystem.Record(host);

        Assert.NotEmpty(log.Entries);
        Assert.Equal(host.State.Seed, log.Seed);

        // OrderId 必须从 1 开始严格递增（确定性）。
        for (int i = 0; i < log.Entries.Count; i++)
        {
            Assert.Equal(i + 1, log.Entries[i].OrderId);
        }

        // 每条指令都必须带有效的执行年月，且必须是「产出月 + 1」或跨年进位。
        foreach (var entry in log.Entries)
        {
            Assert.True(entry.ExecuteYear > 0, "执行年必须为正数。");
            Assert.True(entry.ExecuteMonth >= 1 && entry.ExecuteMonth <= 12, "执行月必须在 1-12。");
            Assert.NotEqual(ActionType.Reward, entry.Action);
            Assert.NotEqual(ActionType.RecruitOfficer, entry.Action);
        }
    }

    /// <summary>
    /// 回放日志可 JSON 往返，往返后重放结果不变。
    /// </summary>
    [Fact]
    public void 回放日志_JSON往返后重放结果一致()
    {
        var original = new SimulationHost(8080, 90);
        original.RunToEnd();

        var log = ReplaySystem.Record(original);
        string json = ReplaySystem.Serialize(log);
        var restored = ReplaySystem.Deserialize(json);

        var replayed = ReplaySystem.Replay(restored);
        string? diff = SimStateComparer.Diff(original.State, replayed.FinalState);
        Assert.Null(diff);
    }

    /// <summary>
    /// 多局回放都必须与原模拟一致。
    /// </summary>
    [Theory]
    [InlineData(11)]
    [InlineData(22)]
    [InlineData(33)]
    public void 多局回放_均与原模拟一致(int seed)
    {
        var original = new SimulationHost(seed, 120);
        original.RunToEnd();

        var replayed = ReplaySystem.Replay(ReplaySystem.Record(original));

        Assert.Null(SimStateComparer.Diff(original.State, replayed.FinalState));
    }
}
