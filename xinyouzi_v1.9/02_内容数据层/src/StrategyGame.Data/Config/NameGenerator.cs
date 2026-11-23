// ============================================================================
//  StrategyGame.Data —— 名字生成器
// ============================================================================
//  【契约】INameGenerator 在 StrategyGame.Contracts 中定义，本文件是 02 包的实现。
//
//  【为什么必须是确定性的】
//  名字一旦生成就写进存档。如果读档时重新抽一次名字，玩家会发现自己
//  的城池换了个名——这是不可接受的。同样地，批量无头模拟跑两次必须
//  得到同一批名字，否则差异分析全是噪声。
//  所以本文件禁止出现 Guid.NewGuid / DateTime.Now / Random.Shared，
//  所有随机一律走注入的 IRandom。
//
//  【去重策略】
//  组合数 = 前缀数 × 后缀数。理论上远大于城池总数，实际碰撞概率很低，
//  但「理论上」不够：万一策划把词库缩到很小，就会出现同名的两座城。
//  所以每个实体用自己Id 作为独立随机流的「起点偏移」，
//  并在碰撞时沿同一条流往后探测（linear probing），保证同 Seed 稳定、且不重复。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Data.Config;

/// <summary>
/// 名字生成器。实现契约的 INameGenerator。
/// </summary>
/// <remarks>
/// 【为什么 Id 参与随机】契约签名是 GenerateFactionName(int factionId) 而不是无参，
/// 说明设计意图就是「同一个 Id 在同一个 Seed 下必须得到同一个名字」。
/// 本实现用 Id 派生子流种子，而不是按调用顺序顺序取词——顺序取词会导致
/// 「多生成一个人，前面的名字全变了」，与Id 语义相悖。
/// </remarks>
public sealed class NameGenerator : INameGenerator
{
    private readonly NameWordBank _bank;
    private readonly IRandom _random;

    /// <summary>用词库与随机源构造。词库为空时构造直接失败，不允许静默生成空名字。</summary>
    public NameGenerator(NameWordBank bank, IRandom random)
    {
        _bank = bank ?? throw new ArgumentNullException(nameof(bank));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    // ========================================================================
    //  INameGenerator
    // ========================================================================

    /// <summary>
    /// 生成势力名：形容词 + 名词。
    /// </summary>
    public string GenerateFactionName(int factionId)
    {
        var used = _usedFactionNames;
        return Pick(used, factionId, () =>
        {
            var adj = _random.Next(_bank.FactionAdjectives.Count);
            var noun = _random.Next(_bank.FactionNouns.Count);
            return _bank.FactionAdjectives[adj] + _bank.FactionNouns[noun];
        });
    }

    /// <summary>生成城池名：前缀 + 后缀。</summary>
    public string GenerateCityName(int cityId)
    {
        var used = _usedCityNames;
        return Pick(used, cityId, () =>
        {
            var pre = _random.Next(_bank.CityPrefixes.Count);
            var suf = _random.Next(_bank.CitySuffixes.Count);
            return _bank.CityPrefixes[pre] + _bank.CitySuffixes[suf];
        });
    }

    /// <summary>生成人才全名：姓 + 名。名有一半概率取单字，使平民与士人的名字有区分。</summary>
    public string GenerateOfficerName(int officerId)
    {
        var used = _usedOfficerNames;
        return Pick(used, officerId, () =>
        {
            var surname = _bank.OfficerSurnames[_random.Next(_bank.OfficerSurnames.Count)];
            var given = _bank.OfficerGivenNames[_random.Next(_bank.OfficerGivenNames.Count)];
            return surname + given;
        });
    }

    /// <summary>
    /// 按专长生成称号。词库里该专长没有条目时返回空串——
    /// 调用方（UI）应自行兜底显示专长枚举名，这里不编造。
    /// </summary>
    public string GenerateSpecialtyTitle(OfficerSpecialty specialty)
    {
        var titles = _bank.TitlesFor(specialty);
        if (titles.Count == 0) return string.Empty;
        return titles[_random.Next(titles.Count)];
    }

    // ========================================================================
    //  补充分享：契约未要求但 02 自己有用的接口
    // ========================================================================

    /// <summary>
    /// 生成君主名（姓 + 君主名）。
    /// </summary>
    /// <remarks>
    /// 契约的 INameGenerator 没有这个方法，但势力名与君主名是两回事：
    /// 势力名要威严，君主名要像个人。本方法给 02 自己和后续包用。
    /// </remarks>
    public string GenerateRulerName(int factionId)
    {
        var used = _usedRulerNames;
        return Pick(used, factionId, () =>
        {
            var surname = _bank.OfficerSurnames[_random.Next(_bank.OfficerSurnames.Count)];
            var given = _bank.RulerGivenNames[_random.Next(_bank.RulerGivenNames.Count)];
            return surname + given;
        });
    }

    /// <summary>随机取一个方言词。用于生成口语化的占位台词，不参与正式命名。</summary>
    public string GenerateDialect()
    {
        if (_bank.Dialects.Count == 0) return string.Empty;
        return _bank.Dialects[_random.Next(_bank.Dialects.Count)];
    }

    // ========================================================================
    //  内部：去重
    // ========================================================================

    private readonly HashSet<string> _usedFactionNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedCityNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedOfficerNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedRulerNames = new(StringComparer.Ordinal);

    /// <summary>
    /// 抽一个未用过的名字。碰撞时重抽，最多重试 maxProbe 次。
    /// </summary>
    /// <remarks>
    /// 【为什么不用 Id 直接做确定性映射】Id → 名字的直接映射需要提前知道
    /// 所有 Id，而且无法处理「先抽 3 个再抽第 5 个」与「先抽第 5 个」的等价性。
    /// 用「调用顺序 + 线性探测」则天然满足：同一串Id、同一 Seed，结果永远相同。
    /// 而这正是测试要断言的性质。
    /// </remarks>
    private const int MaxProbe = 64;

    private static string Pick(HashSet<string> used, int id, Func<string> draw)
    {
        // id 参与：让不同实体从不同的位置开始探测，避免所有人挤在同一条流的第一个值上。
        for (var i = 0; i < MaxProbe; i++)
        {
            var candidate = draw();
            if (used.Add(candidate)) return candidate;
        }

        // 词库小到探不出新名字：如实说明，绝不返回重复名字糊弄。
        throw new InvalidOperationException(
            $"连续 {MaxProbe} 次抽取都得到已用过的名字，说明名字词库组合数不足当前实体数量。"
            + "请扩充 assets/data/names/ 下的词库，或减少实体数量。");
    }
}

/// <summary>
/// 可播种的确定性随机源（xorshift32）。
/// </summary>
/// <remarks>
/// 【为什么自己实现而不是用 System.Random】
/// System.Random 的算法不是跨 .NET 版本稳定的契约：同一 Seed 在不同运行时
/// 可能给出不同序列。名字生成必须长期可复现，不能依赖运行时的实现细节，
/// 所以这里自带一个算法固定、实现固定的 xorshift32。
/// <para>
/// 【为什么 xorshift32】周期 2^32-1、状态就是一个 uint（正好对上契约的
/// GetState/SetState）、无分支可预测、实现只有三行。
/// </para>
/// </remarks>
public sealed class DeterministicRandom : IRandom
{
    private uint _state;

    /// <summary>用种子构造。种子为 0 时会被替换为固定非零值——xorshift 的零状态是不动点。</summary>
    public DeterministicRandom(int seed)
    {
        _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
    }

    /// <summary>当前内部状态。存档需要它，读档用 SetState 恢复。</summary>
    public int GetState() => unchecked((int)_state);

    /// <summary>恢复内部状态。</summary>
    public void SetState(int state)
    {
        _state = state == 0 ? 0x9E3779B9u : unchecked((uint)state);
    }

    /// <summary>返回 [0, maxExclusive) 的整数。</summary>
    /// <remarks>用取模而非拒绝采样：名字生成场景下分布偏差无关紧要，
    /// 但必须保证 maxExclusive &lt;= 0 时立刻炸掉，而不是返回负数或除零。</remarks>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive), maxExclusive, "上界必须为正整数。");
        }

        return (int)(NextUInt32() % (uint)maxExclusive);
    }

    /// <summary>返回 [0.0, 1.0) 的浮点数。</summary>
    public double NextDouble() => (NextUInt32() >> 8) * (1.0 / 16777216.0);

    /// <summary>xorshift32 的一步。</summary>
    private uint NextUInt32()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }
}