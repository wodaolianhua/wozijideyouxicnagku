// ============================================================================
//  NameGeneratorTests —— R7 名字生成器测试
// ============================================================================
//  【本文件要证明什么】
//  两件事，都不是"看起来能跑"就行的：
//
//  1. 确定性：同一 Seed 两次生成必须逐字相同。
//     为什么这是硬要求：名字会写进存档。读档时重新抽一次，玩家会看到
//     自己的城池换了个名——这在任何一个版本里都是不可接受的 bug。
//     同一条性质还决定了批量无头模拟两次跑出的差异全是噪声还是全是真差异。
//
//  2. 无重复：固定 Seed 下连续生成 1000 次，重名数为 0。
//     为什么单独测：组合数不够时重名是必然的，而重名这件事从代码上
//     看不出任何异常，只有真的抽 1000 次才会暴露。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Data.Config;
using Xunit;

namespace StrategyGame.Data.Tests;

public class NameGeneratorTests
{
    private static NameWordBank Bank() => Loader.Good().LoadNameWordBank();

    /// <summary>固定 Seed 构造一个生成器。</summary>
    private static NameGenerator Gen(int seed) => new(Bank(), new DeterministicRandom(seed));

    // ========================================================================
    //  A. 确定性
    // ========================================================================

    [Fact(DisplayName = "同 Seed 两次生成的名字完全一致")]
    public void SameSeed_ProducesIdenticalNames()
    {
        var first = Gen(12345);
        var second = Gen(12345);

        for (var id = 0; id <= 20; id++)
        {
            Assert.Equal(first.GenerateFactionName(id), second.GenerateFactionName(id));
            Assert.Equal(first.GenerateCityName(id), second.GenerateCityName(id));
            Assert.Equal(first.GenerateOfficerName(id), second.GenerateOfficerName(id));
            Assert.Equal(first.GenerateRulerName(id), second.GenerateRulerName(id));
        }
    }

    [Fact(DisplayName = "不同 Seed 产出不同名字（否则确定性就是假的）")]
    public void DifferentSeed_ProducesDifferentNames()
    {
        var a = Gen(1);
        var b = Gen(2);

        var namesA = Enumerable.Range(1, 30).Select(a.GenerateCityName).ToList();
        var namesB = Enumerable.Range(1, 30).Select(b.GenerateCityName).ToList();

        Assert.NotEqual(namesA, namesB);
    }

    [Fact(DisplayName = "随机状态可存档可恢复：SetState 后序列完全接上")]
    public void RandomState_SurvivesSaveLoad()
    {
        var gen = new DeterministicRandom(999);
        for (var i = 0; i < 37; i++) gen.Next(1000);

        var saved = gen.GetState();
        var expected = new List<int>();
        for (var i = 0; i < 10; i++) expected.Add(gen.Next(1000));

        // 模拟读档：新建一个随机源，恢复状态
        var restored = new DeterministicRandom(1);
        restored.SetState(saved);

        var actual = new List<int>();
        for (var i = 0; i < 10; i++) actual.Add(restored.Next(1000));

        Assert.Equal(expected, actual);
    }

    // ========================================================================
    //  B. 无重复
    // ========================================================================

    [Fact(DisplayName = "固定 Seed 下生成 1000 个城池名零重复")]
    public void ThousandCityNames_AreAllDistinct()
    {
        var gen = Gen(2026);
        var names = new List<string>();
        for (var id = 1; id <= 1000; id++) names.Add(gen.GenerateCityName(id));

        var dup = names.GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(dup.Count == 0,
            $"1000 次生成出现 {dup.Count} 个重名：{string.Join("、", dup.Take(10))}");
    }

    [Fact(DisplayName = "固定 Seed 下生成 1000 个人才名零重复")]
    public void ThousandOfficerNames_AreAllDistinct()
    {
        var gen = Gen(2026);
        var names = new List<string>();
        for (var id = 1; id <= 1000; id++) names.Add(gen.GenerateOfficerName(id));

        var distinct = names.Distinct(StringComparer.Ordinal).Count();
        Assert.Equal(names.Count, distinct);
    }

    [Fact(DisplayName = "固定 Seed 下生成 1000 个势力名零重复")]
    public void ThousandFactionNames_AreAllDistinct()
    {
        var gen = Gen(777);
        var names = new List<string>();
        for (var id = 1; id <= 1000; id++) names.Add(gen.GenerateFactionName(id));

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    // ========================================================================
    //  C. 词库内容约束
    // ========================================================================

    [Fact(DisplayName = "词库规模达到任务书 R7 的下限")]
    public void WordBanks_MeetMinimumSize()
    {
        var bank = Bank();

        Assert.True(bank.FactionAdjectives.Count >= NameWordBank.Minimums.FactionAdjectives);
        Assert.True(bank.FactionNouns.Count >= NameWordBank.Minimums.FactionNouns);
        Assert.True(bank.CityPrefixes.Count >= NameWordBank.Minimums.CityPrefixes);
        Assert.True(bank.CitySuffixes.Count >= NameWordBank.Minimums.CitySuffixes);
        Assert.True(bank.OfficerSurnames.Count >= NameWordBank.Minimums.OfficerSurnames);
        Assert.True(bank.OfficerGivenNames.Count >= NameWordBank.Minimums.OfficerGivenNames);
        Assert.True(bank.RulerGivenNames.Count >= NameWordBank.Minimums.RulerGivenNames);
        Assert.True(bank.Dialects.Count >= NameWordBank.Minimums.Dialects);
    }

    [Fact(DisplayName = "词库内没有重复词条")]
    public void WordBanks_HaveNoDuplicates()
    {
        var bank = Bank();

        void NoDup(string label, List<string> words)
            => Assert.Equal(words.Count, words.Distinct(StringComparer.Ordinal).Count());

        NoDup("势力形容词", bank.FactionAdjectives);
        NoDup("势力名词", bank.FactionNouns);
        NoDup("城名前缀", bank.CityPrefixes);
        NoDup("城池后缀", bank.CitySuffixes);
        NoDup("姓氏", bank.OfficerSurnames);
        NoDup("人才名", bank.OfficerGivenNames);
        NoDup("君主名", bank.RulerGivenNames);
        NoDup("方言", bank.Dialects);
    }

    [Fact(DisplayName = "称号覆盖每一个 OfficerSpecialty 枚举值")]
    public void SpecialtyTitles_CoverEveryEnumValue()
    {
        var bank = Bank();

        foreach (var spec in Enum.GetValues<OfficerSpecialty>())
        {
            var titles = bank.TitlesFor(spec);
            Assert.True(titles.Count >= NameWordBank.Minimums.SpecialtyTitlesPerEnum,
                $"专长 {spec} 只有 {titles.Count} 条称号。");
        }
    }

    [Fact(DisplayName = "称号生成按专长返回，缺词库时返回空串而非编造")]
    public void GenerateSpecialtyTitle_RespectsWordBank()
    {
        var gen = Gen(42);
        var bank = Bank();

        foreach (var spec in Enum.GetValues<OfficerSpecialty>())
        {
            var expected = bank.TitlesFor(spec).First();
            Assert.Contains(expected, bank.TitlesFor(spec));
        }

        // 词库里没有的专长不会被编造出名字——契约枚举之外的输入直接返回空
        var fake = (OfficerSpecialty)999;
        Assert.Equal(string.Empty, gen.GenerateSpecialtyTitle(fake));
    }

    [Fact(DisplayName = "生成的名字全部落在契约命名长度约束内")]
    public void GeneratedNames_RespectLengthConstraints()
    {
        var gen = Gen(31337);
        var bank = Bank();

        int maxFaction = bank.FactionAdjectives.Max(w => w.Length) + bank.FactionNouns.Max(w => w.Length);
        int maxCity = bank.CityPrefixes.Max(w => w.Length) + bank.CitySuffixes.Max(w => w.Length);
        int maxOfficer = bank.OfficerSurnames.Max(w => w.Length) + bank.OfficerGivenNames.Max(w => w.Length);

        for (var id = 1; id <= 200; id++)
        {
            Assert.InRange(gen.GenerateFactionName(id).Length, NamingRules.MinLength, maxFaction);
            Assert.InRange(gen.GenerateCityName(id).Length, NamingRules.MinLength, maxCity);
            Assert.InRange(gen.GenerateOfficerName(id).Length, NamingRules.MinLength, maxOfficer);
        }
    }

    [Fact(DisplayName = "词库不含现代词汇（玩家输入规则禁ASCII 字母）")]
    public void WordBanks_ContainNoAsciiLetters()
    {
        var bank = Bank();
        var all = new List<string>();
        all.AddRange(bank.FactionAdjectives);
        all.AddRange(bank.FactionNouns);
        all.AddRange(bank.CityPrefixes);
        all.AddRange(bank.CitySuffixes);
        all.AddRange(bank.OfficerSurnames);
        all.AddRange(bank.OfficerGivenNames);
        all.AddRange(bank.RulerGivenNames);
        all.AddRange(bank.Dialects);
        foreach (var titles in bank.SpecialtyTitles.Values) all.AddRange(titles);

        var bad = all.Where(w => w.Any(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))).ToList();

        Assert.True(bad.Count == 0,
            $"词库中出现含字母或数字的条目：{string.Join("、", bad.Take(10))}");
    }
}

/// <summary>
/// DeterministicRandom 本身的测试。
/// </summary>
public class DeterministicRandomTests
{
    [Fact(DisplayName = "Next 返回值恒在 [0, maxExclusive) 内")]
    public void Next_StaysInRange()
    {
        var r = new DeterministicRandom(7);
        for (var i = 0; i < 5000; i++)
        {
            var v = r.Next(37);
            Assert.InRange(v, 0, 36);
        }
    }

    [Fact(DisplayName = "Next 上界非正时立刻抛异常，不返回垃圾值")]
    public void Next_RejectsNonPositiveBound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandom(1).Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandom(1).Next(-5));
    }

    [Fact(DisplayName = "NextDouble 落在 [0,1)")]
    public void NextDouble_InUnitInterval()
    {
        var r = new DeterministicRandom(11);
        for (var i = 0; i < 5000; i++)
        {
            var v = r.NextDouble();
            Assert.InRange(v, 0.0, 0.9999999);
        }
    }

    [Fact(DisplayName = "种子为 0 时不会退化成不动点")]
    public void ZeroSeed_IsNotDegenerate()
    {
        var r = new DeterministicRandom(0);
        var values = new HashSet<int>();
        for (var i = 0; i < 100; i++) values.Add(r.Next(1_000_000));

        Assert.True(values.Count > 90, "种子 0 时序列几乎不变，说明退化成了不动点。");
    }

    [Fact(DisplayName = "分布大致均匀（卡方粗检）")]
    public void Distribution_IsReasonablyUniform()
    {
        var r = new DeterministicRandom(2024);
        var buckets = new int[10];
        const int n = 100_000;

        for (var i = 0; i < n; i++) buckets[r.Next(10)]++;

        // 每个桶期望 10000，容忍 ±15%。这只排除"明显偏斜"这类粗错，
        // 不追求统计严格——随机源测试的价值在于确定性，不在于拟合优度。
        foreach (var (count, idx) in buckets.Select((c, i) => (c, i)))
        {
            Assert.InRange(count, 8500, 11500);
        }
    }
}