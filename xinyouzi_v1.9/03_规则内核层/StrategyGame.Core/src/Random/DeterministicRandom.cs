// ============================================================================
//  确定性随机源
// ============================================================================
//  【为什么不用 System.Random】
//  1. System.Random 不暴露内部状态，无法实现 IRandom.GetState/SetState，
//     存档读档后随机序列会变，回放与无头模拟全部失效。
//  2. System.Random 的算法是实现细节，.NET 版本升级可能改变序列。
//
//  本实现只使用 32 位整数位运算，不含浮点、不含平台相关 API，
//  因此在任何 .NET 运行时上、任何 CPU 上都产生完全相同的序列。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Randomization;

/// <summary>
/// 可播种的确定性随机源（xorshift32）。
/// </summary>
/// <remarks>
/// 【确定性保证】本类所有方法仅使用 uint 整数运算。
/// 相同种子 + 相同调用序列 → 相同结果，跨进程、跨运行时、跨平台一致。
///
/// 【取模偏差】<see cref="Next(int)"/> 使用取模实现，对非2 的幂的上界存在
/// 极轻微偏差（不超过 1/(2^32)）。游戏内随机（事件抽取、暴击判定）不依赖
/// 密码学均匀性，此偏差可接受；如需无偏取模请改用拒绝采样。
/// </remarks>
public sealed class DeterministicRandom : IRandom
{
    /// <summary>xorshift 的内部状态。恒不为 0（全 0 会导致序列退化为常数）。</summary>
    private uint _state;

    /// <summary>零状态保护用的替代种子。xorshift 不允许状态为 0。</summary>
    private const uint NonZeroFallback = 0x9E3779B9u;

    /// <summary>用一个种子创建随机源。</summary>
    /// <param name="seed">任意整数。0 会被替换为 <see cref="NonZeroFallback"/>。</param>
    public DeterministicRandom(int seed)
    {
        _state = seed == 0 ? NonZeroFallback : unchecked((uint)seed);
    }

    /// <summary>当前内部状态。写入存档即可在读档后精确复现随机序列。</summary>
    public int GetState() => unchecked((int)_state);

    /// <summary>恢复内部状态。必须与 <see cref="GetState"/> 成对使用。</summary>
    /// <param name="state">存档中记录的内部状态。</param>
    public void SetState(int state)
    {
        uint converted = unchecked((uint)state);
        _state = converted == 0 ? NonZeroFallback : converted;
    }

    /// <summary>
    /// 生成 [0, maxExclusive) 区间的整数。
    /// </summary>
    /// <param name="maxExclusive">上界（不含）。必须为正数。</param>
    /// <returns>返回区间内的确定性伪随机整数。</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxExclusive"/> 小于或等于 0 时抛出。
    /// </exception>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive), maxExclusive, "上界必须为正数。");
        }

        // 先推进状态再取模，保证即使 maxExclusive==1 也会消耗一次随机数，
        // 从而让调用次数与序列位置一一对应（重放的前提）。
        uint value = NextUInt();
        return (int)(value % (uint)maxExclusive);
    }

    /// <summary>
    /// 生成 [0.0, 1.0) 区间的双精度浮点数。
    /// </summary>
    /// <returns>返回区间内的确定性伪随机数。</returns>
    public double NextDouble() => NextUInt() / (uint.MaxValue + 1.0);

    /// <summary>推进 xorshift32 状态并返回更新后的 32 位值。</summary>
    private uint NextUInt()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }
}
