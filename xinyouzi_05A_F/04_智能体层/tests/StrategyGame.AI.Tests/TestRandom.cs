// 测试辅助：可播种随机源
using StrategyGame.Contracts;

namespace StrategyGame.AI.Tests;

public sealed class TestRandom : IRandom
{
    private Random _rng;

    public TestRandom(int seed = 42)
    {
        _rng = new Random(seed);
    }

    public int Next(int maxExclusive) => _rng.Next(maxExclusive);
    public double NextDouble() => _rng.NextDouble();
    public int GetState() => _rng.Next();
    public void SetState(int state) => _rng = new Random(state);
}
