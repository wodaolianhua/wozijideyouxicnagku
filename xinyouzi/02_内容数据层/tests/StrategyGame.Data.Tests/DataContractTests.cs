// ============================================================================
//  DataContractTests —— 交付数据是否满足任务书与设计决策
// ============================================================================
//  【与 ConfigValidatorTests 的区别】
//  ConfigValidatorTests 测「校验器能不能抓坏数据」——工具本身对不对。
//  DataContractTests 测「我们交付的数据对不对」——内容本身符不符合要求。
//  两者都必要：工具再对，数据错了照样上线事故。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Data.Config;
using Xunit;

namespace StrategyGame.Data.Tests;

public class DataContractTests
{
    // ========================================================================
    //  设计决策D14 明确的硬要求
    // ========================================================================

    [Fact(DisplayName = "城池共 6 座，分属 3 个势力")]
    public void Cities_SixCities_ThreeFactions()
    {
        var data = Loader.Good().LoadAll();

        Assert.Equal(6, data.Cities.Count);
        Assert.Equal(3, data.Cities.Select(c => c.FactionId).Distinct().Count());
    }

    [Fact(DisplayName = "玩家势力恰好 2 座相邻小城（设计决策 D14）")]
    public void PlayerFaction_HasTwoAdjacentSmallCities()
    {
        var data = Loader.Good().LoadAll();

        var playerCities = data.Cities
            .Where(c => c.FactionId == ConfigValidator.PlayerFactionId)
            .OrderBy(c => c.Id)
            .ToList();

        // 期望值来自配置，不是写死的 2
        Assert.Equal(data.Extended.ExpectedPlayerCityCount, playerCities.Count);

        // 每座玩家城池都必须比中原霸主的城池小——玩家是弱势方，这一点逐城成立。
        // 注意：不能断言「玩家合计人口低于每一家 AI」。按数值框架，
        // 玩家 5800 / 中原 8700 / 边疆 4700，边疆人口其实比玩家还少；
        // 玩家的弱势体现在资源与人才上，不体现在人口总和上。
        var centralPlain = data.Cities.Where(c => c.FactionId == 1).Max(c => c.Population);
        foreach (var c in playerCities)
        {
            Assert.True(c.Population < centralPlain,
                $"玩家城池 {c.Id} 人口 {c.Population} 应低于中原霸主最大城池 {centralPlain}。");
        }

        // 两城必须相邻
        var a = playerCities[0];
        var b = playerCities[1];
        Assert.Contains(b.Id, a.AdjacentCityIds);
        Assert.Contains(a.Id, b.AdjacentCityIds);
    }

    [Fact(DisplayName = "玩家初始人才 Command/Governance 落在 40-55")]
    public void PlayerOfficers_AreDeliberatelyWeak()
    {
        var data = Loader.Good().LoadAll();

        var playerOfficerIds = data.OfficerStates
            .Where(s => s.OwnerFactionId == ConfigValidator.PlayerFactionId)
            .Select(s => s.OfficerId)
            .ToHashSet();

        Assert.NotEmpty(playerOfficerIds);

        foreach (var id in playerOfficerIds)
        {
            var o = data.Officers.First(x => x.Id == id);
            Assert.InRange(o.Command, 40, 55);
            Assert.InRange(o.Governance, 40, 55);
        }
    }

    [Fact(DisplayName = "玩家人才明显弱于 AI 势力人才")]
    public void PlayerOfficers_WeakerThanAi()
    {
        var data = Loader.Good().LoadAll();

        var playerOfficerIds = data.OfficerStates
            .Where(s => s.OwnerFactionId == ConfigValidator.PlayerFactionId)
            .Select(s => s.OfficerId)
            .ToHashSet();

        var playerMax = data.Officers.Where(o => playerOfficerIds.Contains(o.Id))
            .Max(o => Math.Max(o.Command, o.Governance));

        var aiMax = data.Officers.Where(o => !playerOfficerIds.Contains(o.Id))
            .Max(o => Math.Max(o.Command, o.Governance));

        Assert.True(playerMax < aiMax,
            $"玩家人才最强项 {playerMax} 应低于 AI 人才最强项 {aiMax}，否则「没落贵族」的设定不成立。");
    }

    [Fact(DisplayName = "老部下忠诚度为 65")]
    public void PlayerOfficers_LoyaltyIs65()
    {
        var data = Loader.Good().LoadAll();

        var playerOfficerIds = data.OfficerStates
            .Where(s => s.OwnerFactionId == ConfigValidator.PlayerFactionId)
            .Select(s => s.OfficerId)
            .ToHashSet();

        foreach (var id in playerOfficerIds)
        {
            var o = data.Officers.First(x => x.Id == id);
            Assert.Equal(65, o.Loyalty);
        }
    }

    [Fact(DisplayName = "每个势力恰好一座首都")]
    public void EachFaction_HasExactlyOneCapital()
    {
        var data = Loader.Good().LoadAll();

        foreach (var group in data.Cities.GroupBy(c => c.FactionId))
        {
            Assert.Single(group, c => c.IsCapital);
        }
    }

    // ========================================================================
    //  任务书 R1 / R3 / R4 / R5 要求
    // ========================================================================

    [Fact(DisplayName = "7 个 JSON 文件齐全")]
    public void SevenJsonFiles_Exist()
    {
        Assert.Equal(7, JsonConfigLoader.AllFiles.Count);

        foreach (var f in JsonConfigLoader.AllFiles)
        {
            Assert.True(System.IO.File.Exists(Path.Combine(TestPaths.GoodDataDir, f)),
                $"数据文件 {f} 不存在。");
        }
    }

    [Fact(DisplayName = "config.json 包含全部 20 个系数且数值非负")]
    public void BalanceConfig_HasAllCoefficients()
    {
        var b = Loader.Good().LoadBalance();

        // 契约 BalanceConfig 的全部整数字段，逐个确认已配置
        var values = new Dictionary<string, int>
        {
            ["grainPerPopPerMonth"] = b.GrainPerPopPerMonth,
            ["soldierPerPopPerMonth"] = b.SoldierPerPopPerMonth,
            ["moraleDecayPerMonth"] = b.MoraleDecayPerMonth,
            ["stabilityDecayPerMonth"] = b.StabilityDecayPerMonth,
            ["baseRecruitCostSilver"] = b.BaseRecruitCostSilver,
            ["baseRecruitCostGrain"] = b.BaseRecruitCostGrain,
            ["baseFortifyCostSilver"] = b.BaseFortifyCostSilver,
            ["baseDevelopCostSilver"] = b.BaseDevelopCostSilver,
            ["loyaltyThreshold"] = b.LoyaltyThreshold,
            ["loyaltyDecayPerMonth"] = b.LoyaltyDecayPerMonth,
            ["postTakeEffectMonths"] = b.PostTakeEffectMonths,
            ["maxOrdersPerFactionPerMonth"] = b.MaxOrdersPerFactionPerMonth,
            ["baseLoss"] = b.BaseLoss,
            ["defenderLossFactor"] = b.DefenderLossFactor,
            ["siegeThresholdLoss"] = b.SiegeThresholdLoss,
            ["fieldBattleLossFactor"] = b.FieldBattleLossFactor,
            ["stalemateLossRate"] = b.StalemateLossRate,
            ["armyMonthlyGrainPerSoldier"] = b.ArmyMonthlyGrainPerSoldier,
            ["siegeDefeatRoutRate"] = b.SiegeDefeatRoutRate,
        };

        // 20 个系数 = 19 个整数字段 + 1 个专长加成表
        Assert.Equal(19, values.Count);
        foreach (var kv in values)
        {
            Assert.True(kv.Value >= 0, $"{kv.Key} = {kv.Value} 不应为负数。");
        }

        Assert.NotEmpty(b.SpecialtyBonus);
    }

    [Fact(DisplayName = "战斗系数符合任务书 R2 给出的设计值")]
    public void BattleCoefficients_MatchTaskBookValues()
    {
        var b = Loader.Good().LoadBalance();

        // 契约 v1.2：比率一律万分比，任务书表格中的百分数已按 ×100 换算
        Assert.Equal(3500, b.BaseLoss);
        Assert.Equal(2700, b.BaseKill);
        Assert.Equal(7000, b.SiegeThresholdLoss);
        Assert.Equal(6000, b.FieldBattleLossFactor);
        Assert.Equal(200, b.ArmyMonthlyGrainPerSoldier);
        Assert.Equal(10000, b.SiegeDefeatRoutRate);

        // 契约 v1.2 标记为废弃的字段必须置 0（> 0 会触发告警）
        Assert.Equal(0, b.DefenderLossFactor);
        Assert.Equal(0, b.StalemateLossRate);

        // 任务书 R2 第一张表
        Assert.Equal(3, b.BaseRecruitCostSilver);
        Assert.Equal(2, b.BaseRecruitCostGrain);
        Assert.Equal(200, b.BaseFortifyCostSilver);
        Assert.Equal(150, b.BaseDevelopCostSilver);
        Assert.Equal(25, b.LoyaltyThreshold);
        Assert.Equal(1, b.LoyaltyDecayPerMonth);
        Assert.Equal(5, b.MaxOrdersPerFactionPerMonth);

        // 契约 v1.2 把这两个系数统一为万分比
        Assert.Equal(800, b.GrainPerPopPerMonth);
        Assert.Equal(5000, b.SoldierPerPopPerMonth);
    }

    [Fact(DisplayName = "人才 5 名以上，专长覆盖至少 4 种")]
    public void Officers_AtLeastFive_CoverFourSpecialties()
    {
        var data = Loader.Good().LoadAll();

        Assert.True(data.Officers.Count >= 5,
            $"人才数 {data.Officers.Count} 不足 5 名。");

        var specialties = data.Officers
            .Select(o => o.Specialty)
            .Where(s => s != OfficerSpecialty.None)
            .Distinct()
            .ToList();

        Assert.True(specialties.Count >= 4,
            $"专长覆盖 {specialties.Count} 种（{string.Join(", ", specialties)}），不足 4 种。");
    }

    [Fact(DisplayName = "人才专长必须覆盖 Agriculture / Siege / Logistics")]
    public void Officers_CoverRequiredSpecialties()
    {
        var data = Loader.Good().LoadAll();
        var specialties = data.Officers.Select(o => o.Specialty).ToHashSet();

        Assert.Contains(OfficerSpecialty.Agriculture, specialties);
        Assert.Contains(OfficerSpecialty.Siege, specialties);
        Assert.Contains(OfficerSpecialty.Logistics, specialties);
    }

    [Fact(DisplayName = "存在一名高 Command 的猛将")]
    public void Officers_HaveHighCommandFighter()
    {
        var data = Loader.Good().LoadAll();

        Assert.Contains(data.Officers, o => o.Command >= 70);
    }

    [Fact(DisplayName = "事件至少 15 条，覆盖四个类别")]
    public void Events_AtLeast15_CoverFourCategories()
    {
        var data = Loader.Good().LoadAll();

        Assert.True(data.Events.Count >= 15,
            $"事件数 {data.Events.Count} 不足 15 条。");

        var categories = data.Events.Select(e => e.Category).Distinct().ToList();

        // 任务书 R5 要求覆盖：民生 / 军事 / 人才 / 灾害
        Assert.Contains(EventCategory.Economy, categories);
        Assert.Contains(EventCategory.Military, categories);
        Assert.Contains(EventCategory.Personnel, categories);
        Assert.Contains(EventCategory.Disaster, categories);
        Assert.True(categories.Count >= 4);
    }

    [Fact(DisplayName = "事件概率均在万分比区间，同类别之和不超过 6000")]
    public void Events_ProbabilitiesInRange_SumsUnder60()
    {
        var data = Loader.Good().LoadAll();

        foreach (var e in data.Events)
        {
            Assert.InRange(e.Probability, 0, ConfigValidator.RatioScale);
        }

        foreach (var group in data.Events.GroupBy(e => e.Category))
        {
            var sum = group.Sum(e => e.Probability);
            Assert.True(sum <= ConfigValidator.MaxCategoryProbabilitySum,
                $"类别 {group.Key} 的概率之和为 {sum}，超过 {ConfigValidator.MaxCategoryProbabilitySum}。");
        }
    }

    [Fact(DisplayName = "每个事件都有非空中文文案与效果")]
    public void Events_HaveMessageAndEffects()
    {
        var data = Loader.Good().LoadAll();

        foreach (var e in data.Events)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Message), $"事件 {e.Id} 缺少文案。");
            Assert.NotEmpty(e.Effects);
        }
    }

    [Fact(DisplayName = "所有 ID 唯一")]
    public void AllIds_AreUnique()
    {
        var data = Loader.Good().LoadAll();

        Assert.Equal(data.Cities.Count, data.Cities.Select(c => c.Id).Distinct().Count());
        Assert.Equal(data.Officers.Count, data.Officers.Select(o => o.Id).Distinct().Count());
        Assert.Equal(data.Factions.Count, data.Factions.Select(f => f.Id).Distinct().Count());
        Assert.Equal(data.Specialties.Count, data.Specialties.Select(s => s.Id).Distinct().Count());
        Assert.Equal(data.Events.Count, data.Events.Select(e => e.Id).Distinct().Count());
    }

    [Fact(DisplayName = "所有数据文件都有 _comment 说明")]
    public void AllFiles_HaveComment()
    {
        var data = Loader.Good().LoadAll();

        foreach (var file in JsonConfigLoader.AllFiles)
        {
            Assert.True(data.FileComments.ContainsKey(file),
                $"{file} 缺少 _comment 字段。");
            Assert.False(string.IsNullOrWhiteSpace(data.FileComments[file]),
                $"{file} 的 _comment 为空。");
        }
    }

    [Fact(DisplayName = "占位数值全部带 TODO 标记")]
    public void PlaceholderValues_AreMarkedTodo()
    {
        var data = Loader.Good().LoadAll();

        Assert.NotEmpty(data.DeclaredTodos);

        // 每个数据文件都应至少声明一条待办，说明其数值尚未定稿
        foreach (var file in JsonConfigLoader.AllFiles)
        {
            Assert.Contains(data.DeclaredTodos, t => t.StartsWith(file, StringComparison.Ordinal));
        }
    }

    [Fact(DisplayName = "难易三档系数符合契约注释")]
    public void DifficultyMultipliers_MatchContractRemarks()
    {
        var data = Loader.Good().LoadAll();

        var easy = data.Difficulties.First(d => d.Difficulty == DifficultyLevel.Easy);
        var normal = data.Difficulties.First(d => d.Difficulty == DifficultyLevel.Normal);
        var hard = data.Difficulties.First(d => d.Difficulty == DifficultyLevel.Hard);

        // 契约 DifficultyLevel 的注释：Easy ×1.3，Hard ×0.85
        Assert.Equal(130, easy.AiResourceMultiplierPercent);
        Assert.Equal(100, normal.AiResourceMultiplierPercent);
        Assert.Equal(85, hard.AiResourceMultiplierPercent);
    }

    [Fact(DisplayName = "数据文件为 UTF-8 编码")]
    public void Files_AreUtf8()
    {
        foreach (var file in JsonConfigLoader.AllFiles)
        {
            var path = Path.Combine(TestPaths.GoodDataDir, file);
            var bytes = System.IO.File.ReadAllBytes(path);

            // UTF-8 BOM 以 EF BB BF 开头；本项目要求无 BOM 的纯 UTF-8
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Assert.False(hasBom, $"{file} 不应包含 UTF-8 BOM。");

            // 能被严格 UTF-8 解码即证明编码合法
            var strict = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true);
            var noException = Record.Exception(() => strict.GetString(bytes));
            Assert.Null(noException);
        }
    }

    [Fact(DisplayName = "数据文件缩进为 2 空格")]
    public void Files_UseTwoSpaceIndent()
    {
        foreach (var file in JsonConfigLoader.AllFiles)
        {
            var lines = System.IO.File.ReadAllLines(Path.Combine(TestPaths.GoodDataDir, file));

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var indent = line.Length - line.TrimStart(' ').Length;
                Assert.Equal(0, indent % 2);
            }
        }
    }
}
