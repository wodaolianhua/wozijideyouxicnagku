// ============================================================================
//  ConfigValidatorTests —— 校验器测试
// ============================================================================
//  【本文件的核心命题】
//  任务书验收标准要求：「故意写入一个非法数据，校验器能检出并给出可读错误信息」。
//  所以这里的测试主体不是"好数据能通过"，而是"坏数据必须被抓到，
//  而且必须报出人能看懂的中文错误"。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Data.Config;
using Xunit;

namespace StrategyGame.Data.Tests;

public class ConfigValidatorTests
{
    // ========================================================================
    //  A. 交付数据本身必须干净
    // ========================================================================

    [Fact(DisplayName = "交付的正式数据必须零错误")]
    public void GoodData_HasNoErrors()
    {
        var (_, result) = Loader.GoodValidated();

        Assert.True(result.Errors.Count() == 0,
            "交付数据不应存在错误级问题，实际报告：\n" + result.ToReport());
    }

    [Fact(DisplayName = "交付数据能被正常加载，且各表非空")]
    public void GoodData_LoadsAllTables()
    {
        var data = Loader.Good().LoadAll();

        Assert.NotEmpty(data.Cities);
        Assert.NotEmpty(data.Officers);
        Assert.NotEmpty(data.Events);
        Assert.NotEmpty(data.Specialties);
        Assert.NotEmpty(data.Factions);
        Assert.NotEmpty(data.Difficulties);
        Assert.NotEmpty(data.CityStates);
        Assert.NotEmpty(data.OfficerStates);
        Assert.Equal(JsonConfigLoader.AllFiles.Count, data.RawJson.Count);
    }

    // ========================================================================
    //  B. 验收要求：故意写坏数据必须被检出
    // ========================================================================

    [Fact(DisplayName = "坏数据①：负数资源必须被检出")]
    public void BadData_NegativeResource_IsCaught()
    {
        var data = Loader.FromFixture("bad_negative").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.NegativeValue);

        // 位置说清文件与字段，说明里给出实际值与原因。
        Assert.Contains("factions.json", issue.Location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grain", issue.Location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-500", issue.Message);
        Assert.Contains("不允许为负数", issue.Message);
    }

    [Fact(DisplayName = "坏数据②：概率超出万分比上界必须被检出")]
    public void BadData_ProbabilityOverScale_IsCaught()
    {
        var data = Loader.FromFixture("bad_prob_over").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.EventProbabilityRange);

        Assert.Contains("1001", issue.Location);
        Assert.Contains("10100", issue.Message);
        Assert.Contains("10000", issue.Message);
    }

    [Fact(DisplayName = "坏数据③：相邻关系不对称必须被检出")]
    public void BadData_AsymmetricAdjacency_IsCaught()
    {
        var data = Loader.FromFixture("bad_adj_symmetric").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.CityAdjAsymmetric);

        // 错误信息必须同时指出两端，让策划知道该改哪一处。
        Assert.Contains("相邻关系不对称", issue.Message);
        Assert.Contains("城池 4（City_04）", issue.Message);
        Assert.Contains("5（City_05）", issue.Message);
    }

    [Fact(DisplayName = "坏数据④：指向不存在的城必须被检出")]
    public void BadData_DanglingCityReference_IsCaught()
    {
        var data = Loader.FromFixture("bad_adj_missing").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.CityAdjMissing);

        Assert.Contains("999", issue.Location);
        Assert.Contains("999", issue.Message);
        Assert.Contains("不存在的城池", issue.Message);
    }

    [Fact(DisplayName = "坏数据⑤：势力没有首都必须被检出")]
    public void BadData_NoCapital_IsCaught()
    {
        var data = Loader.FromFixture("bad_no_capital").LoadAll();
        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.CityCapitalCount);
    }

    [Fact(DisplayName = "坏数据⑥：一个势力两座首都必须被检出")]
    public void BadData_TwoCapitals_IsCaught()
    {
        var data = Loader.FromFixture("bad_two_capital").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.CityCapitalCount);
        Assert.Contains("2 座首都", issue.Message);
        Assert.Contains("势力 1", issue.Message);
    }

    [Fact(DisplayName = "坏数据⑦：玩家人才过强必须被检出")]
    public void BadData_StrongPlayerOfficer_IsCaught()
    {
        var data = Loader.FromFixture("bad_strong_player").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.OfficerAttrRange);
        Assert.Contains("88", issue.Message);
        Assert.Contains("没落贵族", issue.Message);
        Assert.Contains("Officer_01", issue.Location);
    }

    [Fact(DisplayName = "坏数据⑧：同类别概率和超过 6000 必须被检出")]
    public void BadData_CategoryProbabilitySum_IsCaught()
    {
        var data = Loader.FromFixture("bad_prob_sum").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.EventCategorySum);
        Assert.Contains("7700", issue.Message);
        Assert.Contains("6000", issue.Message);
        Assert.Contains("Economy", issue.Location);
    }

    // ========================================================================
    //  C. 校验器行为约定
    // ========================================================================

    [Fact(DisplayName = "校验器不抛异常，返回全部问题列表")]
    public void Validate_ReturnsAllIssuesInsteadOfThrowing()
    {
        // bad_two_capital 同时会触发：首都数错误 + factions 首都指向不一致。
        // 校验器必须把两条都报出来，而不是碰到第一条就抛异常。
        var data = Loader.FromFixture("bad_two_capital").LoadAll();

        ValidationResult result = null!;
        var noThrow = Record.Exception(() => result = ConfigValidator.Validate(data));

        Assert.Null(noThrow);
        Assert.True(result.Issues.Count >= 2,
            "一次校验应返回全部问题，而非只报第一条。实际：" + result.ToReport());
    }

    [Fact(DisplayName = "null 数据集返回错误而不是崩溃")]
    public void Validate_NullData_ReturnsError()
    {
        var result = ConfigValidator.Validate(null);

        Assert.True(result.HasErrors);
        Assert.Equal(1, result.CountByCode("DATA_NULL"));
    }

    [Fact(DisplayName = "错误报告可读，包含位置、问题与结论")]
    public void Report_IsHumanReadable()
    {
        var data = Loader.FromFixture("bad_negative").LoadAll();
        var report = ConfigValidator.Validate(data).ToReport();

        Assert.Contains("配置校验完成", report);
        Assert.Contains("factions.json", report);
        Assert.Contains("结论：数据非法，禁止启动。", report);
    }

    [Fact(DisplayName = "玩家人才在 40-55 区间时不报强度错")]
    public void PlayerOfficer_InWeakRange_Passes()
    {
        var data = Loader.Good().LoadAll();
        var result = ConfigValidator.Validate(data);

        // 正式数据里玩家的每一名人才都必须在 40-55
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

        Assert2.NoIssue(result, ValidationCode.OfficerAttrRange);
    }

    // ========================================================================
    //  D. 相邻对称性：正向 + 反向都验证
    // ========================================================================

    [Fact(DisplayName = "正式数据的相邻关系严格对称")]
    public void GoodData_AdjacencyIsSymmetric()
    {
        var data = Loader.Good().LoadAll();

        foreach (var c in data.Cities)
        {
            foreach (var adj in c.AdjacentCityIds)
            {
                var target = data.Cities.FirstOrDefault(x => x.Id == adj);
                Assert.True(target is not null, $"城池 {c.Id} 的相邻 {adj} 必须存在");

                Assert.True(target!.AdjacentCityIds.Contains(c.Id),
                    $"对称性被破坏：{c.Id} 含 {adj}，但 {adj} 不含 {c.Id}");
            }
        }
    }

    [Fact(DisplayName = "自引用必须被检出")]
    public void SelfAdjacency_IsCaught()
    {
        var data = Loader.Good().LoadAll();
        data.Cities.First(c => c.Id == 1).AdjacentCityIds.Add(1);

        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.CityAdjSelf);
    }

    [Fact(DisplayName = "相邻列表中重复的 ID 必须被检出")]
    public void DuplicateAdjacency_IsCaught()
    {
        var data = Loader.Good().LoadAll();
        data.Cities.First(c => c.Id == 1).AdjacentCityIds.Add(2);

        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.CityAdjDuplicate);
    }

    // ========================================================================
    //  E. 其他校验项
    // ========================================================================

    [Fact(DisplayName = "重复 ID 必须被检出")]
    public void DuplicateId_IsCaught()
    {
        var data = Loader.Good().LoadAll();

        // 契约 CityTemplate 没有 Clone()（那是 City 才有），这里手写一个等价副本
        var dup = data.Cities[0];
        data.Cities.Add(new CityTemplate
        {
            Id = dup.Id,
            Name = dup.Name,
            FactionId = dup.FactionId,
            Population = dup.Population,
            Terrain = dup.Terrain,
            IsCapital = dup.IsCapital,
            AdjacentCityIds = new List<int>(dup.AdjacentCityIds),
        });

        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.DuplicateId);
        Assert.Contains("重复", issue.Message);
        Assert.Contains("id=1", issue.Location);
    }

    [Fact(DisplayName = "人才属性越界必须被检出")]
    public void OfficerAttributeOutOfRange_IsCaught()
    {
        var data = Loader.Good().LoadAll();
        data.Officers.First(o => o.OwnerFactionIdSafe(data) != ConfigValidator.PlayerFactionId)
            .Command = 150;

        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.OfficerAttrRange);
    }

    [Fact(DisplayName = "浮点数资源必须在文本层被检出")]
    public void FloatLiteral_IsCaught()
    {
        // 场景：events.json 里混入一个浮点字面量 1200.5。
        // 注意契约把 Probability 声明为 int，强类型反序列化会先抛 JsonException——
        // 那属于「加载阶段」拦截，不是校验器的活。
        // 但任务书要求「禁止浮点数存资源」是一条独立规则，即使某天有人把字段改成
        // double 也必须仍被拦下，所以这里直接测文本层扫描逻辑本身。
        var good = Loader.ReadFixtureFile("bad_negative", "events.json");
        var broken = good.Replace("\"probability\": 1200,", "\"probability\": 1200.5,",
            StringComparison.Ordinal);
        Assert.NotEqual(good, broken);

        // 校验器的文本层规则直接作用在原始 JSON 上，与反序列化是否成功无关
        var fakeData = new GameDataSet();
        fakeData.RawJson["events.json"] = broken;

        var result = ConfigValidator.Validate(fakeData);

        var issue = Assert2.HasError(result, ValidationCode.FloatUsed);
        Assert.Contains("1200.5", issue.Message);
        Assert.Contains("int", issue.Message);
    }

    [Fact(DisplayName = "缺少 _comment 必须被检出")]
    public void MissingComment_IsCaught()
    {
        var good = Loader.ReadFixtureFile("bad_negative", "specialties.json");
        // 把 _comment 字段改名，模拟策划漏写说明
        var broken = good.Replace("\"_comment\":", "\"_xcomment\":", StringComparison.Ordinal);
        var tmp = Loader.CopyAndOverride("specialties.json", broken);

        var data = new JsonConfigLoader(tmp).LoadAll();
        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.MissingComment);
    }

    [Fact(DisplayName = "势力首都与城池标记不一致必须被检出")]
    public void CapitalMismatch_IsCaught()
    {
        var data = Loader.Good().LoadAll();
        // 把玩家势力的 capitalCityId 指向非首都城池
        data.Factions.First(f => f.IsPlayer).CapitalCityId = 2;

        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.CityCapitalMismatch);
    }

    [Fact(DisplayName = "玩家势力数量不唯一必须被检出")]
    public void PlayerFactionCount_IsCaught()
    {
        var data = Loader.Good().LoadAll();
        data.Factions[1].IsPlayer = true;

        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.FactionPlayerCount);
        Assert.Contains("恰好为 1", issue.Message);
        Assert.Contains("数量为 2", issue.Message);
    }

    [Fact(DisplayName = "城池缺少状态条目必须被检出")]
    public void MissingCityState_IsCaught()
    {
        var data = Loader.Good().LoadAll();
        data.CityStates.RemoveAt(0);

        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.CityOrphanState);
    }

    [Fact(DisplayName = "城防超过上限必须被检出")]
    public void DefenseExceedsMax_IsCaught()
    {
        var data = Loader.Good().LoadAll();
        data.CityStates.First(s => s.CityId == 1).Defense = 99999;

        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.ValueOutOfRange);
    }

    [Fact(DisplayName = "专长加成键格式非法必须被检出")]
    public void BadSpecialtyBonusKey_IsCaught()
    {
        var data = Loader.Good().LoadAll();
        data.Balance.SpecialtyBonus["格式错误的键"] = 10;

        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.SpecialtyBonusKey);
    }

    [Fact(DisplayName = "难度档位缺失必须被检出")]
    public void MissingDifficulty_IsCaught()
    {
        var data = Loader.Good().LoadAll();
        data.Difficulties.RemoveAll(d => d.Difficulty == DifficultyLevel.Hard);

        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.DifficultyMissing);
    }

    // ========================================================================
    //  F. 契约 v1.2 新增校验
    // ========================================================================

    [Fact(DisplayName = "坏数据⑨：玩家势力只有 1 座城必须被检出（本轮返工的核心回归）")]
    public void BadData_PlayerHasOneCity_IsCaught()
    {
        // 这一条对应上一轮的真实事故：把玩家从 2 城改成 1 城，
        // 而校验器当时完全放行。必须成为常驻回归样本。
        var data = Loader.FromFixture("bad_player_one_city").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.FactionPlayerCityCount);
        Assert.Contains("1 座城池", issue.Message);
        Assert.Contains("expectedPlayerCityCount", issue.Message);
    }

    [Fact(DisplayName = "玩家的两座城不相邻必须被检出")]
    public void PlayerCitiesNotAdjacent_IsCaught()
    {
        var data = Loader.FromFixture("bad_player_not_adjacent").LoadAll();
        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.PlayerCitiesNotAdjacent);
    }

    [Fact(DisplayName = "正式数据里玩家恰好 2 城且相邻")]
    public void GoodData_PlayerHasTwoAdjacentCities()
    {
        var (_, result) = Loader.GoodValidated();
        Assert2.NoIssue(result, ValidationCode.FactionPlayerCityCount);
        Assert2.NoIssue(result, ValidationCode.PlayerCitiesNotAdjacent);
    }

    [Fact(DisplayName = "期望城池数从配置读，改配置即改期望（不是写死 2）")]
    public void ExpectedCityCount_IsReadFromConfig()
    {
        var data = Loader.Good().LoadAll();

        // 改配置里的期望值，校验器立刻按新期望判定——证明没有写死
        data.Extended.ExpectedPlayerCityCount = 3;
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.FactionPlayerCityCount);
        Assert.Contains("= 3", issue.Message);

        // 改回2 即通过
        data.Extended.ExpectedPlayerCityCount = 2;
        Assert2.NoIssue(ConfigValidator.Validate(data), ValidationCode.FactionPlayerCityCount);
    }

    [Fact(DisplayName = "坏数据⑩：baseKill 为 0 必须被检出")]
    public void BadData_ZeroBaseKill_IsCaught()
    {
        var data = Loader.FromFixture("bad_base_kill").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.ZeroValue);
        Assert.Contains("baseKill", issue.Location);
    }

    [Fact(DisplayName = "坏数据⑪：把万分比当百分数填必须被告警")]
    public void BadData_PercentInsteadOfRatio_IsCaught()
    {
        // baseLoss 填 35 而不是 3500。这类错从数据表面看不出异常
        // （是一个合法正整数），但会让所有战损缩小 100 倍。
        // 校验器给的是告警而非错误：万分比口径下确实存在合法的极小比率，
        // 一棍子打死会误伤。真正的把关在评审与无头模拟。
        var data = Loader.FromFixture("bad_ratio_scale").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = result.Issues.FirstOrDefault(i =>
            i.Code == ValidationCode.RatioOutOfRange && i.Message.Contains("百分数当成了万分比"));

        Assert.True(issue is not null,
            "应报告「疑似把百分数当成万分比」。实际报告：\n" + result.ToReport());
        Assert.Equal(ValidationSeverity.Warning, issue!.Severity);
        Assert.False(result.HasErrors, "刻度可疑只是告警，不应阻止启动。");
    }

    [Fact(DisplayName = "已废弃字段未置 0 必须被告警（不是错误）")]
    public void DeprecatedFields_ProduceWarningNotError()
    {
        var data = Loader.FromFixture("bad_deprecated_field").LoadAll();
        var result = ConfigValidator.Validate(data);

        Assert.Equal(2, result.CountByCode(ValidationCode.DeprecatedField));
        Assert.False(result.HasErrors,
            "废弃字段只应告警，不应阻止启动——旧配置仍需能加载。");
    }

    [Fact(DisplayName = "坏数据⑫：事件 Id 为 0 必须被检出")]
    public void BadData_NonPositiveEventId_IsCaught()
    {
        var data = Loader.FromFixture("bad_event_id_zero").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.ValueOutOfRange);
        Assert.Contains("正整数", issue.Message);
    }

    [Fact(DisplayName = "坏数据⑬：词库规模不足必须被检出")]
    public void BadData_SmallWordBank_IsCaught()
    {
        var data = Loader.FromFixture("bad_name_bank_small").LoadAll();
        var result = ConfigValidator.Validate(data);

        var issue = Assert2.HasError(result, ValidationCode.NameBankTooSmall);
        Assert.Contains("姓氏", issue.Message);
    }

    [Fact(DisplayName = "坏数据⑭：词库重复词条必须被检出")]
    public void BadData_DuplicateWordBankEntry_IsCaught()
    {
        var data = Loader.FromFixture("bad_name_bank_dup").LoadAll();
        var result = ConfigValidator.Validate(data);

        Assert2.HasError(result, ValidationCode.NameBankDuplicate);
    }

    [Fact(DisplayName = "正式数据的比率字段全部落在万分比区间")]
    public void GoodData_AllRatiosInRange()
    {
        var data = Loader.Good().LoadAll();
        var b = data.Balance;

        var ratios = new Dictionary<string, int>
        {
            ["grainPerPopPerMonth"] = b.GrainPerPopPerMonth,
            ["soldierPerPopPerMonth"] = b.SoldierPerPopPerMonth,
            ["baseLoss"] = b.BaseLoss,
            ["baseKill"] = b.BaseKill,
            ["siegeThresholdLoss"] = b.SiegeThresholdLoss,
            ["fieldBattleLossFactor"] = b.FieldBattleLossFactor,
            ["armyMonthlyGrainPerSoldier"] = b.ArmyMonthlyGrainPerSoldier,
            ["siegeDefeatRoutRate"] = b.SiegeDefeatRoutRate,
            ["mutinyRoutRate"] = b.MutinyRoutRate,
            ["stabilityDecayPerMonth"] = b.StabilityDecayPerMonth,
        };

        foreach (var (name, value) in ratios)
        {
            Assert.InRange(value, 0, ConfigValidator.RatioScale);
        }

        Assert.True(b.BaseKill > 0, "baseKill 必须大于 0。");
    }

    [Fact(DisplayName = "所有事件 Id 都是正整数")]
    public void AllEventIds_ArePositiveIntegers()
    {
        var data = Loader.Good().LoadAll();

        foreach (var e in data.Events)
        {
            Assert.True(e.Id > 0, $"事件 Id {e.Id} 必须为正整数。");
        }
    }
}

/// <summary>测试辅助扩展方法。</summary>
internal static class OfficerTestExtensions
{
    /// <summary>取该人才归属的势力 ID（契约 OfficerTemplate 不含归属字段，需查旁挂表）。</summary>
    public static int OwnerFactionIdSafe(this OfficerTemplate officer, GameDataSet data)
    {
        foreach (var s in data.OfficerStates)
        {
            if (s.OfficerId == officer.Id) return s.OwnerFactionId;
        }
        return -1;
    }
}
