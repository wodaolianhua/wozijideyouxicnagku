// ============================================================================
//  确定性测试
// ============================================================================
//  验收标准：同 Seed 同指令序列，两次模拟的最终状态逐字段相等。
//
//  【为什么这是硬约束】
//  无头模拟器要靠「跑两次、比对差异」来定位 bug；
//  回放系统要从日志重放出同一状态；
//  存档要能精确复现。这些全部要求随机与调度完全可复现。
//  任何一处Random.Shared、HashSet 遍历顺序或时间依赖都会让测试红掉。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Randomization;
using StrategyGame.Core.Sample;
using StrategyGame.Core.Systems;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>
/// 同种子可复现性验证。
/// </summary>
public sealed class DeterminismTests
{
    /// <summary>用固定种子与固定指令序列跑 N 个月，返回最终状态。</summary>
    /// <param name="seed">随机种子。</param>
    /// <param name="months">推进月数。</param>
    /// <returns>推进后的世界状态。</returns>
    private static WorldState Simulate(int seed, int months)
    {
        var state = SampleData.CreateWorld(seed);
        var config = SampleData.CreateBalance();
        var random = new DeterministicRandom(seed);
        var events = SampleData.CreateEvents();

        var registry = ResolverRegistry.CreateDefault(config, random);
        var applier = new StateApplier();
        var processor = new CommandProcessor(registry, applier);

        var pipeline = new TurnPipeline(
            processor,
            applier,
            config,
            eventSystemFactory: _ => new OfficerEventSystem(events, random, factionId: 0));

        for (int month = 0; month < months; month++)
        {
            // 每月固定下达同一组指令，确保指令序列也完全一致。
            var orders = new List<Order>
            {
                Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 50),
                Order.CreatePlayerOrder(ActionType.Recruit, target: 0, amount: 20),
            };

            orders[0].OrderId = month * 2 + 1;
            orders[1].OrderId = month * 2 + 2;

            processor.ProcessAll(state, orders);
            pipeline.RunMonth(state);
        }

        return state;
    }

    /// <summary>同种子跑两次，最终状态必须逐字段相等。</summary>
    [Fact]
    public void SameSeed_TwoRuns_ShouldProduceIdenticalFinalState()
    {
        const int Seed = 20240617;
        const int Months = 12;

        WorldState first = Simulate(Seed, Months);
        WorldState second = Simulate(Seed, Months);

        string? diff = StateSnapshot.Diff(first, second);
        Assert.True(diff == null, $"同种子两次模拟结果不一致：\n{diff}");
    }

    /// <summary>不同种子应产生不同事件序列，证明随机源确实在起作用。</summary>
    /// <remarks>
    /// 反向验证：如果换了种子结果仍然相同，
    /// 说明事件抽取根本没走随机源，测试就成了假阳性。
    /// </remarks>
    [Fact]
    public void DifferentSeeds_ShouldProduceDifferentEventStreams()
    {
        WorldState a = Simulate(1, 24);
        WorldState b = Simulate(999, 24);

        string logA = StateSnapshot.Capture(a);
        string logB = StateSnapshot.Capture(b);

        Assert.NotEqual(logA, logB);
    }

    /// <summary>DeterministicRandom 本身必须可复现。</summary>
    [Fact]
    public void DeterministicRandom_SameSeed_ShouldProduceSameSequence()
    {
        var rngA = new DeterministicRandom(4242);
        var rngB = new DeterministicRandom(4242);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(rngA.Next(1000), rngB.Next(1000));
            Assert.Equal(rngA.NextDouble(), rngB.NextDouble());
        }
    }

    /// <summary>随机源状态存取必须能精确恢复序列（存档读档的基础）。</summary>
    [Fact]
    public void DeterministicRandom_StateSaveRestore_ShouldResumeSequence()
    {
        var rng = new DeterministicRandom(777);
        for (int i = 0; i < 10; i++)
        {
            rng.Next(1000);
        }

        int savedState = rng.GetState();
        int[] expected = Enumerable.Range(0, 10).Select(_ => rng.Next(1000)).ToArray();

        var restored = new DeterministicRandom(1);
        restored.SetState(savedState);
        int[] actual = Enumerable.Range(0, 10).Select(_ => restored.Next(1000)).ToArray();

        Assert.Equal(expected, actual);
    }

    /// <summary>随机源的 Next 必须落在合法区间内。</summary>
    [Fact]
    public void DeterministicRandom_Next_ShouldStayInRange()
    {
        var rng = new DeterministicRandom(31337);

        for (int i = 0; i < 1000; i++)
        {
            int value = rng.Next(37);
            Assert.InRange(value, 0, 36);
        }

        for (int i = 0; i < 1000; i++)
        {
            double value = rng.NextDouble();
            Assert.InRange(value, 0.0, 0.9999999);
        }
    }

    /// <summary>随机源不得使用 Random.Shared——种子 0 也必须产出非恒定序列。</summary>
    [Fact]
    public void DeterministicRandom_ZeroSeed_ShouldNotBeDegenerate()
    {
        var rng = new DeterministicRandom(0);
        var values = Enumerable.Range(0, 50).Select(_ => rng.Next(1000)).ToHashSet();

        // 若种子 0 导致序列退化，这里会只剩极少几个不同值。
        Assert.True(values.Count > 40, $"序列过于集中，去重后仅 {values.Count} 个值。");
    }

    /// <summary>回合推进本身必须确定：同起点跑 12 个月，结果一致。</summary>
    [Fact]
    public void TurnPipeline_SameSeed_TwelveMonths_ShouldMatch()
    {
        WorldState a = SampleData.CreateWorld(555);
        WorldState b = SampleData.CreateWorld(555);

        var config = SampleData.CreateBalance();
        var applier = new StateApplier();
        var randomA = new DeterministicRandom(555);
        var randomB = new DeterministicRandom(555);
        var events = SampleData.CreateEvents();

        var pipelineA = new TurnPipeline(
            new CommandProcessor(ResolverRegistry.CreateDefault(config, randomA), applier),
            applier,
            config,
            _ => new OfficerEventSystem(events, randomA, 0));

        var pipelineB = new TurnPipeline(
            new CommandProcessor(ResolverRegistry.CreateDefault(config, randomB), applier),
            applier,
            config,
            _ => new OfficerEventSystem(events, randomB, 0));

        for (int i = 0; i < 12; i++)
        {
            pipelineA.RunMonth(a);
            pipelineB.RunMonth(b);
        }

        string? diff = StateSnapshot.Diff(a, b);
        Assert.True(diff == null, $"12 个月后两次推进结果不一致：\n{diff}");

        // 年月应恰好推进了 12 个月。
        Assert.Equal(2, a.Year);
        Assert.Equal(1, a.Month);
    }
}
