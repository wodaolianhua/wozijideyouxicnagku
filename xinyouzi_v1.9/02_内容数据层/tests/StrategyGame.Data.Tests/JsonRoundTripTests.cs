// ============================================================================
//  JsonRoundTripTests —— 加载 → 校验 → 再序列化，往返后数据完全一致
// ============================================================================
//  【为什么必须测往返】
//  策划要改数值。如果加载和保存不是无损的，就会出现：
//  读进来 3000，存出去 3000，下次读进来变成 0 或 3000.0，
//  或者专长的 bonusPercent 被悄悄丢掉。这类 bug 在纯人工测试里极难发现，
//  因为数值看起来"差不多对"。
// ============================================================================

using System.Text.Json;
using StrategyGame.Contracts;
using StrategyGame.Data.Config;
using Xunit;

namespace StrategyGame.Data.Tests;

public class JsonRoundTripTests
{
    [Fact(DisplayName = "平衡参数往返后完全一致")]
    public void Balance_RoundTripsExactly()
    {
        var loader = Loader.Good();
        var original = loader.LoadBalance();

        var json = JsonConfigLoader.Serialize(original);
        var reloaded = JsonSerializer.Deserialize<BalanceConfig>(json, ConfigJson.Options);

        Assert.NotNull(reloaded);
        Assert.Equal(original.GrainPerPopPerMonth, reloaded!.GrainPerPopPerMonth);
        Assert.Equal(original.SoldierPerPopPerMonth, reloaded.SoldierPerPopPerMonth);
        Assert.Equal(original.MoraleDecayPerMonth, reloaded.MoraleDecayPerMonth);
        Assert.Equal(original.StabilityDecayPerMonth, reloaded.StabilityDecayPerMonth);
        Assert.Equal(original.BaseRecruitCostSilver, reloaded.BaseRecruitCostSilver);
        Assert.Equal(original.BaseRecruitCostGrain, reloaded.BaseRecruitCostGrain);
        Assert.Equal(original.BaseFortifyCostSilver, reloaded.BaseFortifyCostSilver);
        Assert.Equal(original.BaseDevelopCostSilver, reloaded.BaseDevelopCostSilver);
        Assert.Equal(original.LoyaltyThreshold, reloaded.LoyaltyThreshold);
        Assert.Equal(original.LoyaltyDecayPerMonth, reloaded.LoyaltyDecayPerMonth);
        Assert.Equal(original.PostTakeEffectMonths, reloaded.PostTakeEffectMonths);
        Assert.Equal(original.MaxOrdersPerFactionPerMonth, reloaded.MaxOrdersPerFactionPerMonth);

        // 专长加成表逐键比对
        Assert.Equal(original.SpecialtyBonus.Count, reloaded.SpecialtyBonus.Count);
        foreach (var kv in original.SpecialtyBonus)
        {
            Assert.True(reloaded.SpecialtyBonus.ContainsKey(kv.Key),
                $"往返后丢失了专长加成键：{kv.Key}");
            Assert.Equal(kv.Value, reloaded.SpecialtyBonus[kv.Key]);
        }
    }

    [Fact(DisplayName = "战斗系数往返后完全一致")]
    public void BattleCoefficients_RoundTripExactly()
    {
        var original = Loader.Good().LoadBalance();
        var reloaded = JsonSerializer.Deserialize<BalanceConfig>(
            JsonConfigLoader.Serialize(original), ConfigJson.Options)!;

        Assert.Equal(original.BaseLoss, reloaded.BaseLoss);
        Assert.Equal(original.DefenderLossFactor, reloaded.DefenderLossFactor);
        Assert.Equal(original.SiegeThresholdLoss, reloaded.SiegeThresholdLoss);
        Assert.Equal(original.FieldBattleLossFactor, reloaded.FieldBattleLossFactor);
        Assert.Equal(original.StalemateLossRate, reloaded.StalemateLossRate);
        Assert.Equal(original.ArmyMonthlyGrainPerSoldier, reloaded.ArmyMonthlyGrainPerSoldier);
        Assert.Equal(original.SiegeDefeatRoutRate, reloaded.SiegeDefeatRoutRate);
    }

    [Fact(DisplayName = "人才表往返后完全一致")]
    public void Officers_RoundTripExactly()
    {
        var loader = Loader.Good();
        var original = loader.LoadOfficers();

        var json = JsonConfigLoader.Serialize(original);
        var reloaded = JsonSerializer.Deserialize<List<OfficerTemplate>>(json, ConfigJson.Options)!;

        Assert.Equal(original.Count, reloaded.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Id, reloaded[i].Id);
            Assert.Equal(original[i].Name, reloaded[i].Name);
            Assert.Equal(original[i].Command, reloaded[i].Command);
            Assert.Equal(original[i].Governance, reloaded[i].Governance);
            Assert.Equal(original[i].Loyalty, reloaded[i].Loyalty);
            Assert.Equal(original[i].Age, reloaded[i].Age);
            Assert.Equal(original[i].Specialty, reloaded[i].Specialty);
        }
    }

    [Fact(DisplayName = "城池表往返后完全一致，含相邻列表")]
    public void Cities_RoundTripExactly()
    {
        var loader = Loader.Good();
        var original = loader.LoadCities();

        var json = JsonConfigLoader.Serialize(original);
        var reloaded = JsonSerializer.Deserialize<List<CityTemplate>>(json, ConfigJson.Options)!;

        Assert.Equal(original.Count, reloaded.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Id, reloaded[i].Id);
            Assert.Equal(original[i].Name, reloaded[i].Name);
            Assert.Equal(original[i].FactionId, reloaded[i].FactionId);
            Assert.Equal(original[i].Population, reloaded[i].Population);
            Assert.Equal(original[i].Terrain, reloaded[i].Terrain);
            Assert.Equal(original[i].IsCapital, reloaded[i].IsCapital);

            // 相邻列表顺序与内容都要一致
            Assert.Equal(original[i].AdjacentCityIds, reloaded[i].AdjacentCityIds);
        }
    }

    [Fact(DisplayName = "事件表往返后完全一致，含效果键值对")]
    public void Events_RoundTripExactly()
    {
        var loader = Loader.Good();
        var original = loader.LoadEvents();

        var json = JsonConfigLoader.Serialize(original);
        var reloaded = JsonSerializer.Deserialize<List<GameEventTemplate>>(json, ConfigJson.Options)!;

        Assert.Equal(original.Count, reloaded.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Id, reloaded[i].Id);
            Assert.Equal(original[i].Category, reloaded[i].Category);
            Assert.Equal(original[i].Probability, reloaded[i].Probability);
            Assert.Equal(original[i].Message, reloaded[i].Message);

            Assert.Equal(original[i].Effects.Count, reloaded[i].Effects.Count);
            foreach (var kv in original[i].Effects)
            {
                Assert.True(reloaded[i].Effects.ContainsKey(kv.Key),
                    $"往返后丢失了事件效果键：{original[i].Id}/{kv.Key}");
                Assert.Equal(kv.Value, reloaded[i].Effects[kv.Key]);
            }
        }
    }

    [Fact(DisplayName = "特质表往返后完全一致")]
    public void Specialties_RoundTripExactly()
    {
        var loader = Loader.Good();
        var original = loader.LoadSpecialties();

        var json = JsonConfigLoader.Serialize(original);
        var reloaded = JsonSerializer.Deserialize<List<SpecialtyTemplate>>(json, ConfigJson.Options)!;

        Assert.Equal(original.Count, reloaded.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Id, reloaded[i].Id);
            Assert.Equal(original[i].Specialty, reloaded[i].Specialty);
            Assert.Equal(original[i].Name, reloaded[i].Name);
            Assert.Equal(original[i].Description, reloaded[i].Description);
            Assert.Equal(original[i].PreferredPost, reloaded[i].PreferredPost);
            Assert.Equal(original[i].BonusPercent, reloaded[i].BonusPercent);
            Assert.Equal(original[i].IsAssignable, reloaded[i].IsAssignable);
        }
    }

    [Fact(DisplayName = "势力表往返后完全一致")]
    public void Factions_RoundTripExactly()
    {
        var loader = Loader.Good();
        var original = loader.LoadFactions();

        var json = JsonConfigLoader.Serialize(original);
        var reloaded = JsonSerializer.Deserialize<List<Faction>>(json, ConfigJson.Options)!;

        Assert.Equal(original.Count, reloaded.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Id, reloaded[i].Id);
            Assert.Equal(original[i].Name, reloaded[i].Name);
            Assert.Equal(original[i].IsPlayer, reloaded[i].IsPlayer);
            Assert.Equal(original[i].CapitalCityId, reloaded[i].CapitalCityId);

            Assert.NotNull(original[i].Resources);
            Assert.NotNull(reloaded[i].Resources);
            Assert.Equal(original[i].Resources!.Grain, reloaded[i].Resources!.Grain);
            Assert.Equal(original[i].Resources.Silver, reloaded[i].Resources.Silver);
            Assert.Equal(original[i].Resources.Soldier, reloaded[i].Resources.Soldier);
            Assert.Equal(original[i].Resources.Morale, reloaded[i].Resources.Morale);
            Assert.Equal(original[i].Resources.Stability, reloaded[i].Resources.Stability);
        }
    }

    [Fact(DisplayName = "难度表往返后完全一致")]
    public void Difficulties_RoundTripExactly()
    {
        var loader = Loader.Good();
        var original = loader.LoadDifficulties();

        var json = JsonConfigLoader.Serialize(original);
        var reloaded = JsonSerializer.Deserialize<List<DifficultyPreset>>(json, ConfigJson.Options)!;

        Assert.Equal(original.Count, reloaded.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Difficulty, reloaded[i].Difficulty);
            Assert.Equal(original[i].DisplayName, reloaded[i].DisplayName);
            Assert.Equal(original[i].AiResourceMultiplierPercent, reloaded[i].AiResourceMultiplierPercent);
            Assert.Equal(original[i].AiAggressionPercent, reloaded[i].AiAggressionPercent);
            Assert.Equal(original[i].AiCanInitiateSiege, reloaded[i].AiCanInitiateSiege);
        }
    }

    [Fact(DisplayName = "整体数据集往返：连续两次往返结果稳定")]
    public void FullDataset_TwoRoundTrips_AreStable()
    {
        var first = Loader.Good().LoadAll();

        // 第一次往返：数据 → JSON → 数据
        var json1 = JsonConfigLoader.Serialize(first.Cities);
        var cities1 = JsonSerializer.Deserialize<List<CityTemplate>>(json1, ConfigJson.Options)!;

        // 第二次往返：再序列化一次，结果应与第一次逐字节相同（稳定性）
        var json2 = JsonConfigLoader.Serialize(cities1);
        var cities2 = JsonSerializer.Deserialize<List<CityTemplate>>(json2, ConfigJson.Options)!;

        Assert.Equal(cities1.Count, cities2.Count);
        for (var i = 0; i < cities1.Count; i++)
        {
            Assert.Equal(cities1[i].Id, cities2[i].Id);
            Assert.Equal(cities1[i].Population, cities2[i].Population);
            Assert.Equal(cities1[i].AdjacentCityIds, cities2[i].AdjacentCityIds);
        }

        Assert.Equal(json1, json2);
    }

    [Fact(DisplayName = "序列化输出不含浮点数")]
    public void SerializedOutput_HasNoFloatNumbers()
    {
        var data = Loader.Good().LoadAll();

        foreach (var (file, json) in data.RawJson)
        {
            var skeleton = RemoveStringLiterals(json);
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(skeleton, FloatPattern),
                $"{file} 中出现了浮点数字面量。");
        }
    }

    [Fact(DisplayName = "枚举序列化为字符串而非数字")]
    public void Enums_AreSerializedAsStrings()
    {
        var cities = Loader.Good().LoadCities();
        var json = JsonConfigLoader.Serialize(cities);

        Assert.Contains("\"Plain\"", json);
        Assert.DoesNotContain("\"terrain\":0", json, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "字段名为 camelCase，与线格式契约一致")]
    public void Fields_UseCamelCase()
    {
        var cities = Loader.Good().LoadCities();
        var json = JsonConfigLoader.Serialize(cities);

        Assert.Contains("\"isCapital\"", json);
        Assert.Contains("\"adjacentCityIds\"", json);
        Assert.DoesNotContain("\"IsCapital\"", json, StringComparison.Ordinal);
    }

    private static string RemoveStringLiterals(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, StringLiteralPattern, "\"\"");

    /// <summary>
    /// 浮点数字面量的匹配模式。
    /// </summary>
    /// <remarks>
    /// 用原始字符串字面量（"""..."""）而不是逐字符转义——后者极易漏掉一个反斜杠，
    /// 而且可读性极差。负号前面加否定前瞻，是为了排除数组下标与标识符里的数字。
    /// </remarks>
    private const string FloatPattern = """(?<![\w"])-?\d+\.\d+""";

    /// <summary>JSON 字符串字面量的匹配模式（含转义处理）。</summary>
    private const string StringLiteralPattern = """"(?:\\.|[^"\\])*"""";
}
