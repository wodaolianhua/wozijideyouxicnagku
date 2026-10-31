// ============================================================================
//  确定性测试 —— 整个项目的信任基础
//  【铁律】同 Seed 跑两次，终局状态必须逐字段相等。
//  这是整个项目最核心的验收：它失败意味着规则层有隐式状态污染。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core;
using StrategyGame.Sim;
using Xunit;

namespace StrategyGame.Sim.Tests;

/// <summary>
/// 确定性验证。
/// </summary>
public class DeterminismTests
{
    /// <summary>
    /// 同 Seed 两次模拟，终局状态逐字段相等（跑满 120 月）。
    /// </summary>
    [Fact]
    public void 同Seed两次模拟_终局逐字段相等()
    {
        const int seed = 20261025;

        var first = new SimulationHost(seed, 120);
        first.RunToEnd();

        var second = new SimulationHost(seed, 120);
        second.RunToEnd();

        string? diff = SimStateComparer.Diff(first.State, second.State);
        Assert.Null(diff);
        Assert.Equal(first.MonthsElapsed, second.MonthsElapsed);
        Assert.Equal(first.WinnerFactionId, second.WinnerFactionId);
        Assert.Equal(first.GameOverReason, second.GameOverReason);
    }

    /// <summary>
    /// 启用种子敏感化后，不同 Seed 应当产生不同轨迹。
    /// </summary>
    /// <remarks>
    /// 【为什么这条测试重要】
    /// 上游 <c>SampleData.CreateWorld(seed)</c> 只把 seed 存进状态，不消费 seed 生成世界内容；
    /// 全链路唯一的随机消费点是 AI 选才并列打破，而样例数据里无人并列。
    /// 结果：不启用种子敏感化时，1000 局会跑出完全相同的一局，
    /// 批量模拟器的全部平衡指标退化为常数。
    /// 这条测试锁住「种子确实生效」这个前提。
    /// <para>2026-10-25（06R6）断言口径更新：03R6+04R4 后战局缩短为 8 月灭国，
    /// 粗粒度终局指标跨 seed 同构，断言改为「初始资源档位不同 + 逐月全字段至少一处差异」。
    /// 千局平衡指标缺乏方差是已裁决备案的平衡问题（蜀无军/兵力雪球/AI 不回防），不由本测试守门。</para>
    /// </remarks>
    [Fact]
    public void 启用种子敏感化_不同Seed轨迹应不同()
    {
        var a = new SimulationHost(1001, 120, applySeedVariance: true);
        var b = new SimulationHost(1002, 120, applySeedVariance: true);

        // 第一层断言：变异必须首先改变三方初始资源档位（Fisher-Yates 轮换确实落地）。
        Assert.NotEqual(ResourceSignature(a), ResourceSignature(b));

        // 第二层断言：逐月比较两局世界（Seed 字段豁免），至少一个月存在差异。
        // 【口径说明（00 裁决备案）】03R6+04R4 后两局粗粒度终局会同构（均第 8 月魏灭国），
        // 这是已接受的平衡现状，不是变异失效；变异的效果体现在逐月全字段（如兵力数值）上。
        int aSeed = a.State.Seed;
        int bSeed = b.State.Seed;
        a.State.Seed = 0;
        b.State.Seed = 0;

        bool diverged = false;
        int guard = 0;
        while ((!a.IsFinished || !b.IsFinished) && guard < 120)
        {
            a.RunMonth();
            b.RunMonth();
            guard++;
            diverged |= SimStateComparer.Diff(a.State, b.State) is not null;
        }

        a.State.Seed = aSeed;
        b.State.Seed = bSeed;

        Assert.True(diverged, "种子敏感化未对逐月世界状态产生任何差异。");

        static string ResourceSignature(SimulationHost host)
            => string.Join("|", host.State.Factions
                .OrderBy(f => f.Id)
                .Select(f => $"{f.Id}:{f.Resources.Grain},{f.Resources.Silver},{f.Resources.Soldier}"));
    }

    /// <summary>
    /// 严格模式（不启用种子敏感化）下，各局<b>除 Seed 字段外</b>结果相同 —— 上游现状基线。
    /// </summary>
    /// <remarks>
    /// 【这条测试记录的是上游现状，不是期望行为】
    /// 它锁住「上游 SampleData.CreateWorld(seed) 不消费 seed 生成世界内容」这一事实。
    /// 唯一允许的差异是 <c>WorldState.Seed</c> 字段本身（它按契约记录本局种子），
    /// 其余全部字段必须完全一致。
    /// 上游一旦改为真正按 seed 生成世界，本测试会失败 —— 那时应当删除或改写本测试，
    /// 并同步移除 06 层的 <see cref="WorldVariator"/>。
    /// </remarks>
    [Fact]
    public void 严格模式_不同Seed除Seed字段外结果相同_上游现状基线()
    {
        var a = new SimulationHost(1001, 60, applySeedVariance: false);
        a.RunToEnd();
        var b = new SimulationHost(1002, 60, applySeedVariance: false);
        b.RunToEnd();

        // Seed 字段按契约记录本局种子，此处豁免。
        int aSeed = a.State.Seed;
        int bSeed = b.State.Seed;
        a.State.Seed = 0;
        b.State.Seed = 0;

        Assert.Null(SimStateComparer.Diff(a.State, b.State));

        a.State.Seed = aSeed;
        b.State.Seed = bSeed;
        Assert.NotEqual(aSeed, bSeed);
    }

    /// <summary>
    /// 启用种子敏感化后，同 Seed 两次模拟仍必须逐字段相等（随机化不得破坏确定性）。
    /// </summary>
    [Fact]
    public void 启用种子敏感化_同Seed两次模拟仍逐字段相等()
    {
        var first = new SimulationHost(4711, 120, applySeedVariance: true);
        first.RunToEnd();

        var second = new SimulationHost(4711, 120, applySeedVariance: true);
        second.RunToEnd();

        Assert.Null(SimStateComparer.Diff(first.State, second.State));
    }

    /// <summary>
    /// 同一批次的两个不同索引局，seed 必须按 baseSeed + index 递增。
    /// </summary>
    [Fact]
    public void 批次内局序_种子按基准种子递增()
    {
        var simulator = new BatchSimulator();
        var (results, _) = simulator.Run(new BatchOptions
        {
            Games = 5,
            BaseSeed = 4242,
            MaxMonths = 24,
            Parallel = false,
        });

        Assert.Equal(5, results.Count);
        for (int i = 0; i < results.Count; i++)
        {
            Assert.Equal(i, results[i].GameIndex);
            Assert.Equal(4242 + i, results[i].Seed);
        }
    }

    /// <summary>
    /// 终局判定口径：三方混战下胜方必须是仍拥有城池的势力。
    /// </summary>
    [Fact]
    public void 终局时胜方必须仍拥有城池()
    {
        var host = new SimulationHost(777, 120);
        host.RunToEnd();

        if (host.WinnerFactionId >= 0)
        {
            var faction = host.State.GetFaction(host.WinnerFactionId);
            Assert.NotNull(faction);

            int cities = host.State.Cities.Count(c => c.OwnerFactionId == host.WinnerFactionId);
            Assert.True(cities > 0, "胜方势力竟没有任何城池。");
        }
        else
        {
            Assert.Equal(SimGameOverReason.Stalemate, host.GameOverReason);
        }
    }

    /// <summary>
    /// 三方 AI 混战：三个势力都必须置 IsPlayer=false。
    /// </summary>
    [Fact]
    public void 初始世界_三方均为AI槽位()
    {
        var host = new SimulationHost(999, 12);
        host.RunToEnd();

        Assert.Equal(3, host.State.Factions.Count);
        Assert.All(host.State.Factions, f => Assert.False(f.IsPlayer));
    }
}
