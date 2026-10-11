// ============================================================================
//  StrategyGame.Data —— 配置校验器
// ============================================================================
//  【设计立场】校验器是这套数据系统的护栏。它的唯一职责是：数据非法就一定
//  被抓到，并把问题说成人能读懂的话。
//
//  【三条铁律】
//  1. 不抛异常。收集全部问题后返回，由调用方决定是否中断启动。
//  2. 不静默兜底。任何"看起来能用但其实是猜的"行为都不允许。
//  3. 不猜。校验项全部显式写出，出错时给出文件+字段+实际值。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Data.Config;

/// <summary>问题分类码。测试与调用方据此断言，避免依赖中文字符串。</summary>
public static class ValidationCode
{
    // ===== 数值 =====
    public const string NegativeValue = "NEGATIVE_VALUE";
    public const string ValueOutOfRange = "VALUE_OUT_OF_RANGE";
    public const string ZeroValue = "ZERO_VALUE";
    public const string FloatUsed = "FLOAT_USED";
    public const string StringId = "STRING_ID";
    public const string MissingComment = "MISSING_COMMENT";

    // ===== 标识 =====
    public const string DuplicateId = "DUPLICATE_ID";

    // ===== 城池 =====
    public const string CityAdjMissing = "CITY_ADJ_MISSING";
    public const string CityAdjAsymmetric = "CITY_ADJ_ASYMMETRIC";
    public const string CityAdjSelf = "CITY_ADJ_SELF";
    public const string CityAdjDuplicate = "CITY_ADJ_DUPLICATE";
    public const string CityFactionUnknown = "CITY_FACTION_UNKNOWN";
    public const string CityCapitalCount = "CITY_CAPITAL_COUNT";
    public const string CityCapitalMismatch = "CITY_CAPITAL_MISMATCH";
    public const string CityOrphanState = "CITY_ORPHAN_STATE";

    // ===== 势力 =====
    public const string FactionCapitalUnknown = "FACTION_CAPITAL_UNKNOWN";
    public const string FactionNoCity = "FACTION_NO_CITY";
    public const string FactionPlayerCount = "FACTION_PLAYER_COUNT";
    /// <summary>玩家势力城池数与配置期望值不符。</summary>
    public const string FactionPlayerCityCount = "FACTION_PLAYER_CITY_COUNT";

    /// <summary>玩家的两座城不相邻。</summary>
    public const string PlayerCitiesNotAdjacent = "PLAYER_CITIES_NOT_ADJACENT";

    // ===== 人才 =====
    public const string OfficerAttrRange = "OFFICER_ATTR_RANGE";
    public const string OfficerAgeRange = "OFFICER_AGE_RANGE";
    public const string OfficerFactionUnknown = "OFFICER_FACTION_UNKNOWN";
    public const string OfficerSpecialtyUnknown = "OFFICER_SPECIALTY_UNKNOWN";

    // ===== 事件 =====
    public const string EventProbabilityRange = "EVENT_PROB_RANGE";
    public const string EventCategorySum = "EVENT_CATEGORY_SUM";
    public const string EventEmptyMessage = "EVENT_EMPTY_MESSAGE";

    // ===== 契约 v1.2 新增 =====
    /// <summary>契约 v1.2 已废弃但仍 > 0 的字段。</summary>
    public const string DeprecatedField = "DEPRECATED_FIELD";

    /// <summary>Ratio类系数超出万分比区间。</summary>
    public const string RatioOutOfRange = "RATIO_OUT_OF_RANGE";

    // ===== 词库 =====
    public const string NameBankMissing = "NAME_BANK_MISSING";
    public const string NameBankTooSmall = "NAME_BANK_TOO_SMALL";
    public const string NameBankDuplicate = "NAME_BANK_DUPLICATE";

    // ===== 特性 =====
    public const string SpecialtyBonusKey = "SPECIALTY_BONUS_KEY";
    public const string SpecialtyBonusRange = "SPECIALTY_BONUS_RANGE";

    // ===== 难度 =====
    public const string DifficultyMissing = "DIFFICULTY_MISSING";
    public const string DifficultyMultiplier = "DIFFICULTY_MULTIPLIER";

    // ===== 占位标记 =====
    public const string DeclaredTodo = "DECLARED_TODO";
}

/// <summary>
/// 配置校验器。
/// </summary>
/// <remarks>
/// 【为什么是静态类】校验过程无状态，不持有任何成员。每次调用都是纯函数，
/// 便于在测试里对同一份数据反复校验不同规则子集。
/// </remarks>
public static class ConfigValidator
{
    /// <summary>
    /// 万分比刻度的基准：100% = 10000。
    /// </summary>
    /// <remarks>
    /// 【契约 v1.2 的刻度铁律】所有比率/系数一律万分比整数。
    /// 旧版允许 35 表示 35%，v1.2 起一律 3500。这条常量就是全项目唯一的刻度锚点。
    /// </remarks>
    public const int RatioScale = 10000;

    /// <summary>同类别事件概率之和上限（万分比）。6000 = 60%。超过则每回合触发过于频繁。</summary>
    public const int MaxCategoryProbabilitySum = 6000;

    /// <summary>比率类系数允许的上界。刻意宽于 10000：少数系数（如需叠加两档惩罚）允许超过 100%。</summary>
    public const int MaxRatio = 100000;

    /// <summary>
    /// 比率类系数一旦超过这个值，几乎必然是万分比写成了百分数（或反之）。
    /// </summary>
    public const int RatioPlausibleCeiling = 10000;

    /// <summary>人才属性上下限。超出 0-100 即视为非法。</summary>
    public const int OfficerAttributeMin = 0;
    public const int OfficerAttributeMax = 100;

    /// <summary>人才年龄合理区间。低于 16 无法任职，高于 80 视为已故。</summary>
    public const int OfficerAgeMin = 16;
    public const int OfficerAgeMax = 80;

    /// <summary>玩家势力 Id。固定为 0，与 ActorType.Player 的 ActorId 对应。</summary>
    public const int PlayerFactionId = 0;

    /// <summary>
    /// 玩家初始人才的属性上限。玩家是"没落贵族"，开局人才必须明显偏弱，
    /// 否则"用人的那个人"这个核心玩法没有意义。
    /// </summary>
    public const int PlayerOfficerAttributeCeiling = 55;

    /// <summary>
    /// 玩家初始人才的属性下限。低于此值说明连老部下都不如，属于配置错误。
    /// </summary>
    public const int PlayerOfficerAttributeFloor = 40;

    // ========================================================================
    //  入口
    // ========================================================================

    /// <summary>
    /// 校验全部数据。
    /// </summary>
    /// <param name="data">已加载的数据集。为 null 时直接返回带一条错误的结果。</param>
    /// <returns>包含全部问题的结果，不抛异常。</returns>
    public static ValidationResult Validate(GameDataSet? data)
    {
        var result = new ValidationResult();
        if (data is null)
        {
            result.Error("DATA_NULL", "GameDataSet", "数据集为空，无法校验。加载可能失败。");
            return result;
        }

        // 顺序即报告顺序：从全局到局部，从个体到关系。
        ValidateBalance(data, result);
        ValidateCities(data, result);
        ValidateFactions(data, result);
        ValidateOfficers(data, result);
        ValidateEvents(data, result);
        ValidateSpecialties(data, result);
        ValidateDifficulties(data, result);
        ValidateNameBank(data, result);
        ValidateTextLevelRules(data, result);

        return result;
    }

    // ========================================================================
    //  1. 全局平衡参数
    // ========================================================================

    private static void ValidateBalance(GameDataSet data, ValidationResult r)
    {
        const string loc = "config.json";
        var b = data.Balance;

        // 每个整数字段都必须非负。集中列出，便于一次性改完。
        CheckNonNegative(r, loc, "grainPerPopPerMonth", b.GrainPerPopPerMonth);
        CheckNonNegative(r, loc, "soldierPerPopPerMonth", b.SoldierPerPopPerMonth);
        CheckNonNegative(r, loc, "stabilityDecayPerMonth", b.StabilityDecayPerMonth);
        CheckNonNegative(r, loc, "baseRecruitCostSilver", b.BaseRecruitCostSilver);
        CheckNonNegative(r, loc, "baseRecruitCostGrain", b.BaseRecruitCostGrain);
        CheckNonNegative(r, loc, "baseFortifyCostSilver", b.BaseFortifyCostSilver);
        CheckNonNegative(r, loc, "baseDevelopCostSilver", b.BaseDevelopCostSilver);
        CheckNonNegative(r, loc, "loyaltyDecayPerMonth", b.LoyaltyDecayPerMonth);
        CheckNonNegative(r, loc, "postTakeEffectMonths", b.PostTakeEffectMonths);
        CheckNonNegative(r, loc, "maxOrdersPerFactionPerMonth", b.MaxOrdersPerFactionPerMonth);

        // 士气流转的绝对点数（契约 v1.2 新增的一整组）
        CheckNonNegative(r, loc, "moraleDecayPerMonth", b.MoraleDecayPerMonth);
        CheckNonNegative(r, loc, "moraleDecayNoBattleThreshold", b.MoraleDecayNoBattleThreshold);
        CheckNonNegative(r, loc, "moraleDecayNoBattlePerMonth", b.MoraleDecayNoBattlePerMonth);
        CheckNonNegative(r, loc, "moraleGainOnCapture", b.MoraleGainOnCapture);
        CheckNonNegative(r, loc, "moraleLossOnRepel", b.MoraleLossOnRepel);
        CheckNonNegative(r, loc, "moraleLossOnStalemate", b.MoraleLossOnStalemate);
        CheckNonNegative(r, loc, "moraleLossOnDefend", b.MoraleLossOnDefend);

        // ---- 02 自己的平衡细则（不进契约，但同样要校验）----
        var e = data.Extended;
        CheckNonNegative(r, $"{loc}#extendedBalance.stabilityNaturalRecovery", e.StabilityNaturalRecovery);
        CheckNonNegative(r, $"{loc}#extendedBalance.moraleNaturalRecovery", e.MoraleNaturalRecovery);
        CheckNonNegative(r, $"{loc}#extendedBalance.moraleDecayWhenPeace", e.MoraleDecayWhenPeace);
        CheckNonNegative(r, $"{loc}#extendedBalance.rewardCostSilver", e.RewardCostSilver);
        CheckNonNegative(r, $"{loc}#extendedBalance.rewardLoyaltyGain", e.RewardLoyaltyGain);

        if (e.ExpectedPlayerCityCount <= 0)
        {
            r.Error(ValidationCode.ZeroValue, $"{loc}#extendedBalance.expectedPlayerCityCount",
                "玩家势力期望城池数为 " + e.ExpectedPlayerCityCount + "，必须为正整数。"
                + "校验器完全依赖这个值判断玩家城池数是否正确，它为 0 会让整条校验失效。");
        }

        // 忠诚阈值是百分比语义，必须落在 0-100。
        if (b.LoyaltyThreshold < 0 || b.LoyaltyThreshold > OfficerAttributeMax)
        {
            r.Error(ValidationCode.ValueOutOfRange, $"{loc}#loyaltyThreshold",
                $"忠诚阈值为 {b.LoyaltyThreshold}，合法区间是 0-100。");
        }

        // 每月指令上限为 0 会让势力完全无法行动，属于配置事故。
        if (b.MaxOrdersPerFactionPerMonth == 0)
        {
            r.Error(ValidationCode.ZeroValue, $"{loc}#maxOrdersPerFactionPerMonth",
                "单势力每月指令上限为 0，等于让所有势力无法行动。必须 ≥ 1。");
        }

        // 战斗系数全部是万分比：3500 表示 35%。
        CheckRatio(r, $"{loc}#baseLoss", "攻城基础战损率", b.BaseLoss);
        CheckRatio(r, $"{loc}#baseKill", "攻城守方被歼率", b.BaseKill);
        CheckRatio(r, $"{loc}#siegeThresholdLoss", "攻城击退阈值", b.SiegeThresholdLoss);
        CheckRatio(r, $"{loc}#fieldBattleLossFactor", "野战战损乘数", b.FieldBattleLossFactor);
        CheckRatio(r, $"{loc}#armyMonthlyGrainPerSoldier", "每兵每月耗粮", b.ArmyMonthlyGrainPerSoldier);
        CheckRatio(r, $"{loc}#siegeDefeatRoutRate", "攻陷后守军逃散比例", b.SiegeDefeatRoutRate);
        CheckRatio(r, $"{loc}#mutinyRoutRate", "哗变解散兵力比例", b.MutinyRoutRate);
        CheckRatio(r, $"{loc}#stabilityDecayPerMonth", "稳定度每月衰减", b.StabilityDecayPerMonth);
        CheckRatio(r, $"{loc}#grainPerPopPerMonth", "每人口每月产粮", b.GrainPerPopPerMonth);
        CheckRatio(r, $"{loc}#soldierPerPopPerMonth", "每人口每月产兵源", b.SoldierPerPopPerMonth);

        // baseKill 是v1.2 新增的关键字段。缺它（或填 0）会让「守军归零」这个分支
        // 永远不可达，攻城永远打不赢——属于会让整套战斗系统失效的配置事故，必须报错。
        if (b.BaseKill <= 0)
        {
            r.Error(ValidationCode.ZeroValue, $"{loc}#baseKill",
                "baseKill 为 " + b.BaseKill + "，必须大于 0。"
                + "契约 v1.2 已把攻城改为攻方损耗(baseLoss)与守方伤亡(baseKill)两条独立公式，"
                + "baseKill 为 0 时守军永远不会被歼灭，攻城分支不可达。");
        }

        // 契约 v1.2 标记为废弃的字段：读到 > 0 说明配置还没跟上契约变更。
        // 按契约要求「应输出一次告警提示该字段已失效」——是告警不是错误，
        // 因为旧配置仍然能加载，只是这两个数不该再被用。
        if (b.DefenderLossFactor > 0)
        {
            r.Warn(ValidationCode.DeprecatedField, $"{loc}#defenderLossFactor",
                "defenderLossFactor = " + b.DefenderLossFactor + "，但契约 v1.2 已将该字段标记为废弃："
                + "守方损失不再按守军规模计算，城墙保护由 killRate 公式中的 (2 - defenseFactor) 项承担。"
                + "请把该值置 0 以表明已确认失效。");
        }

        if (b.StalemateLossRate > 0)
        {
            r.Warn(ValidationCode.DeprecatedField, $"{loc}#stalemateLossRate",
                "stalemateLossRate = " + b.StalemateLossRate + "，但契约 v1.2 已将该字段标记为废弃："
                + "未决状态改为按 lossRate / killRate 正常结算，不再额外扣一次（原先是重复计算）。"
                + "请把该值置 0 以表明已确认失效。");
        }

        // 专长加成键格式：必须是 "Specialty:Post" 或 "Specialty:Any"。
        foreach (var (key, value) in b.SpecialtyBonus)
        {
            var loc2 = $"{loc}#specialtyBonus[\"{key}\"]";
            var parts = key.Split(':');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
                string.IsNullOrWhiteSpace(parts[1]))
            {
                r.Error(ValidationCode.SpecialtyBonusKey, loc2,
                    $"专长加成键 \"{key}\" 格式非法。必须是 \"专长:岗位\" 或 \"专长:Any\"，"
                    + "例如 \"Agriculture:FarmOverseer\" 或 \"Logistics:Any\"。");
                continue;
            }

            if (!Enum.TryParse<OfficerSpecialty>(parts[0], ignoreCase: true, out var spec))
            {
                r.Error(ValidationCode.SpecialtyBonusKey, loc2,
                    $"专长加成键 \"{key}\" 的前半段 \"{parts[0]}\" 不是合法的 OfficerSpecialty 枚举值。");
            }

            if (parts[1] != "Any" && !Enum.TryParse<PostType>(parts[1], ignoreCase: true, out _))
            {
                r.Error(ValidationCode.SpecialtyBonusKey, loc2,
                    $"专长加成键 \"{key}\" 的后半段 \"{parts[1]}\" 既不是 PostType 枚举值，也不是 \"Any\"。");
            }

            if (value < 0)
            {
                r.Error(ValidationCode.NegativeValue, loc2,
                    $"专长加成百分比为 {value}，不允许负数。减益请通过其他机制表达。");
            }
            else if (value > 10000)
            {
                r.Warn(ValidationCode.SpecialtyBonusRange, loc2,
                    $"专长加成为 {value}（万分比），超过 10000（=100%），请确认不是把百分数当成了万分比。");
            }
        }
    }

    // ========================================================================
    //  2. 城池
    // ========================================================================

    private static void ValidateCities(GameDataSet data, ValidationResult r)
    {
        var cities = data.Cities;

        // ---- ID 唯一 ----
        foreach (var group in cities.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            r.Error(ValidationCode.DuplicateId, $"cities.json#cities[id={group.Key}]",
                $"城池 Id {group.Key} 重复出现 {group.Count()} 次。Id 必须全局唯一。");
        }

        var known = new HashSet<int>(cities.Select(c => c.Id));
        var factionIds = new HashSet<int>(data.Factions.Select(f => f.Id));

        for (var i = 0; i < cities.Count; i++)
        {
            var c = cities[i];
            var loc = $"cities.json#cities[{i}](id={c.Id},{c.Name})";

            if (c.Id <= 0)
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{loc}.id",
                    $"城池 Id 为 {c.Id}，必须为正整数（0 保留给玩家）。");
            }

            if (string.IsNullOrWhiteSpace(c.Name))
            {
                r.Error(ValidationCode.CityAdjMissing, $"{loc}.name", "城池名称为空。");
            }

            CheckNonNegative(r, $"{loc}.population", c.Population);

            if (c.Population == 0)
            {
                r.Error(ValidationCode.ZeroValue, $"{loc}.population",
                    "人口为 0 的城池无法产出任何资源，属于无效配置。");
            }

            if (!factionIds.Contains(c.FactionId))
            {
                r.Error(ValidationCode.CityFactionUnknown, $"{loc}.factionId",
                    $"所属势力 {c.FactionId} 在 factions.json 中不存在。"
                    + $"现有势力：{(factionIds.Count == 0 ? "无" : string.Join(", ", factionIds.OrderBy(x => x)))}。");
            }

            if (!Enum.IsDefined(c.Terrain))
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{loc}.terrain",
                    $"地形值 \"{c.Terrain}\" 不是合法的 TerrainType 枚举值。");
            }

            // ---- 相邻关系：存在性 / 自引用 / 重复 / 对称性 ----
            ValidateAdjacency(c, cities, known, loc, r);
        }

        // ---- 每势力恰好一座首都 ----
        ValidateCapitalPerFaction(cities, r);

        // ---- 城池状态旁挂数组必须与城池一一对应 ----
        foreach (var state in data.CityStates)
        {
            if (!known.Contains(state.CityId))
            {
                r.Error(ValidationCode.CityOrphanState, $"cities.json#cityStates[cityId={state.CityId}]",
                    $"城池状态引用了不存在的城池 Id {state.CityId}。");
                continue;
            }

            var city = cities.First(x => x.Id == state.CityId);
            var sloc = $"cities.json#cityStates[cityId={state.CityId}]";
            CheckNonNegative(r, $"{sloc}.maxPopulation", state.MaxPopulation);
            CheckNonNegative(r, $"{sloc}.defense", state.Defense);
            CheckNonNegative(r, $"{sloc}.maxDefense", state.MaxDefense);
            CheckNonNegative(r, $"{sloc}.fortifyLevel", state.FortifyLevel);
            CheckNonNegative(r, $"{sloc}.farmLevel", state.FarmLevel);

            if (state.MaxPopulation > 0 && city.Population > state.MaxPopulation)
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{sloc}.maxPopulation",
                    $"城池 {city.Id}（{city.Name}）当前人口 {city.Population} 超过上限 {state.MaxPopulation}。");
            }

            if (state.MaxDefense > 0 && state.Defense > state.MaxDefense)
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{sloc}.defense",
                    $"当前城防 {state.Defense} 超过上限 {state.MaxDefense}。");
            }
        }

        var stateIds = data.CityStates.Select(s => s.CityId).ToHashSet();
        foreach (var c in cities.Where(c => !stateIds.Contains(c.Id)))
        {
            r.Error(ValidationCode.CityOrphanState, $"cities.json#cities[id={c.Id}]",
                $"城池 {c.Id}（{c.Name}）缺少对应的 cityStates 条目，开局状态无法组装。");
        }

        foreach (var dup in data.CityStates.GroupBy(s => s.CityId).Where(g => g.Count() > 1))
        {
            r.Error(ValidationCode.DuplicateId, $"cities.json#cityStates[cityId={dup.Key}]",
                $"城池状态 Id {dup.Key} 重复 {dup.Count()} 次。");
        }
    }

    /// <summary>
    /// 校验单座城池的相邻关系。
    /// </summary>
    /// <remarks>
    /// 【为什么对称性是最重要的校验项】
    /// AdjacentCityIds 决定 Dispatch 是否合法（契约 ResolveResultCode.RejectedNotAdjacent）。
    /// 如果只写了"A 邻接 B"却没写"B 邻接 A"，那么从 B 出发的部队永远打不到 A，
    /// 而从 A 出发却能打到 B。这种不对称在运行时表现为"能打别人，别人打不进来"，
    /// 无头模拟跑一万局也未必暴露，但玩家一上手就发现守不住。
    /// </remarks>
    private static void ValidateAdjacency(
        CityTemplate c, List<CityTemplate> all, HashSet<int> known, string loc, ValidationResult r)
    {
        var seen = new HashSet<int>();

        foreach (var adjId in c.AdjacentCityIds)
        {
            var aloc = $"{loc}.adjacentCityIds[{adjId}]";

            if (adjId == c.Id)
            {
                r.Error(ValidationCode.CityAdjSelf, aloc,
                    $"城池 {c.Id}（{c.Name}）把自己列为相邻城池。自环会让派兵逻辑陷入死循环。");
                continue;
            }

            if (!seen.Add(adjId))
            {
                r.Error(ValidationCode.CityAdjDuplicate, aloc,
                    $"城池 {c.Id}（{c.Name}）的相邻列表中 Id {adjId} 重复出现。");
                continue;
            }

            if (!known.Contains(adjId))
            {
                r.Error(ValidationCode.CityAdjMissing, aloc,
                    $"城池 {c.Id}（{c.Name}）的相邻列表指向不存在的城池 Id {adjId}。"
                    + $"当前已定义的城池 Id：{(known.Count == 0 ? "无" : string.Join(", ", known.OrderBy(x => x)))}。");
                continue;
            }

            // ---- 对称性：A 含 B，则 B 必须含 A ----
            var target = all.First(x => x.Id == adjId);
            if (!target.AdjacentCityIds.Contains(c.Id))
            {
                r.Error(ValidationCode.CityAdjAsymmetric, aloc,
                    $"相邻关系不对称：城池 {c.Id}（{c.Name}）把 {adjId}（{target.Name}）列为相邻，"
                    + $"但 {adjId}（{target.Name}）的 adjacentCityIds 里没有 {c.Id}。"
                    + $"A 含 B 则 B 必须含 A，否则从 {target.Name} 出发的部队永远无法抵达 {c.Name}。");
            }
        }
    }

    // ========================================================================
    //  3. 势力
    // ========================================================================

    private static void ValidateFactions(GameDataSet data, ValidationResult r)
    {
        var cities = data.Cities;
        var knownCities = new HashSet<int>(cities.Select(c => c.Id));

        foreach (var group in data.Factions.GroupBy(f => f.Id).Where(g => g.Count() > 1))
        {
            r.Error(ValidationCode.DuplicateId, $"factions.json#factions[id={group.Key}]",
                $"势力 Id {group.Key} 重复出现 {group.Count()} 次。");
        }

        var players = data.Factions.Where(f => f.IsPlayer).ToList();
        if (players.Count != 1)
        {
            r.Error(ValidationCode.FactionPlayerCount, "factions.json#factions",
                $"标记为玩家势力的数量为 {players.Count}，必须恰好为 1。"
                + "玩家势力数量不唯一会导致存档、回放、AI 规划全部失效。");
        }
        else
        {
            // ---- 玩家势力的城池数量必须等于配置里的期望值 ----
            // 【为什么不写死 2】设计要改玩家起始城池数时，只改 config.json 的
            // extendedBalance.expectedPlayerCityCount，不该要求程序员动校验器。
            // 【为什么这是硬校验】玩家城池数直接决定开局难度与「没落贵族」的定位，
            // 配错了会让整个第一年的平衡基线偏移，属于必须当场拦下的配置事故。
            var playerFaction = players[0];
            var expected = data.Extended.ExpectedPlayerCityCount;
            var playerCities = cities.Where(c => c.FactionId == playerFaction.Id)
                .OrderBy(c => c.Id).ToList();

            if (expected > 0 && playerCities.Count != expected)
            {
                var owned = playerCities.Select(c => $"{c.Id}({c.Name})");
                r.Error(ValidationCode.FactionPlayerCityCount, "cities.json#cities[factionId=0]",
                    $"玩家势力（{playerFaction.Name}）实际拥有 {playerCities.Count} 座城池，"
                    + $"但 config.json 的 extendedBalance.expectedPlayerCityCount = {expected}。"
                    + $"实际城池：{(owned.Any() ? string.Join(", ", owned) : "无")}。"
                    + "玩家起始城池数由设计决策直接决定（没落贵族 = 两座相邻小城），"
                    + "改它需要先改设计决策，再改这里的期望值，不能只改一边。");
            }

            // ---- 玩家的两座城必须相邻 ----
            // 任务书 R2.1 明确要求。理由不只是任务书：不相邻意味着玩家无法
            // 在两城之间互相支援，开局任一城被围另一个就是死城，
            // 而这与「没落贵族但仍有选择」的设计意图相反。
            if (playerCities.Count == 2)
            {
                var a = playerCities[0];
                var b = playerCities[1];

                if (!a.AdjacentCityIds.Contains(b.Id) || !b.AdjacentCityIds.Contains(a.Id))
                {
                    r.Error(ValidationCode.PlayerCitiesNotAdjacent,
                        "cities.json#cities[factionId=0]",
                        $"玩家的两座城池 {a.Id}（{a.Name}）与 {b.Id}（{b.Name}）不相邻"
                        + $"（{a.Id} 的相邻列表：{FormatList(a.AdjacentCityIds)}；"
                        + $"{b.Id} 的相邻列表：{FormatList(b.AdjacentCityIds)}）。"
                        + "不相邻会让玩家无法在两城间互相支援，开局任一城被围另一个即为死城。");
                }
            }
        }

        for (var i = 0; i < data.Factions.Count; i++)
        {
            var f = data.Factions[i];
            var loc = $"factions.json#factions[{i}](id={f.Id},{f.Name})";

            // 契约 v1.2：全项目实体 Id 一律正整数。
            if (f.Id < 0)
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{loc}.id",
                    $"势力 Id 为 {f.Id}，不允许为负数。0 固定为玩家势力，其余应为正整数。");
            }

            if (string.IsNullOrWhiteSpace(f.Name))
            {
                r.Error(ValidationCode.MissingComment, $"{loc}.name", $"势力 Id {f.Id} 的名称为空。");
            }

            if (!knownCities.Contains(f.CapitalCityId))
            {
                r.Error(ValidationCode.FactionCapitalUnknown, $"{loc}.capitalCityId",
                    $"势力 {f.Id}（{f.Name}）的首都 Id {f.CapitalCityId} 不存在。");
            }
            else
            {
                var capital = cities.First(c => c.Id == f.CapitalCityId);
                if (!capital.IsCapital)
                {
                    r.Error(ValidationCode.CityCapitalMismatch, $"{loc}.capitalCityId",
                        $"势力 {f.Id}（{f.Name}）指定城池 {capital.Id}（{capital.Name}）为首都，"
                        + "但该城在 cities.json 中的 isCapital 为 false。两处配置必须一致。");
                }

                if (capital.FactionId != f.Id)
                {
                    r.Error(ValidationCode.CityCapitalMismatch, $"{loc}.capitalCityId",
                        $"势力 {f.Id}（{f.Name}）的首都 {capital.Id}（{capital.Name}）"
                        + $"实际归属势力 {capital.FactionId}。首都必须属于自己。");
                }
            }

            if (!cities.Any(c => c.FactionId == f.Id))
            {
                r.Error(ValidationCode.FactionNoCity, loc,
                    $"势力 {f.Id}（{f.Name}）名下没有任何城池，开局即无法行动。");
            }

            var res = f.Resources;
            var rloc = $"{loc}.resources";
            CheckNonNegative(r, $"{rloc}.grain", res?.Grain ?? -1);
            CheckNonNegative(r, $"{rloc}.silver", res?.Silver ?? -1);
            CheckNonNegative(r, $"{rloc}.soldier", res?.Soldier ?? -1);
            CheckNonNegative(r, $"{rloc}.morale", res?.Morale ?? -1);
            CheckNonNegative(r, $"{rloc}.stability", res?.Stability ?? -1);

            if (res is not null && res.Grain == 0 && res.Silver == 0)
            {
                r.Warn(ValidationCode.ZeroValue, rloc,
                    $"势力 {f.Id}（{f.Name}）开局粮银均为 0，第一个月就会因资源不足而无法行动。");
            }
        }
    }

    // ========================================================================
    //  4. 人才
    // ========================================================================

    private static void ValidateOfficers(GameDataSet data, ValidationResult r)
    {
        var officers = data.Officers;
        var factionIds = new HashSet<int>(data.Factions.Select(f => f.Id));
        var cityIds = new HashSet<int>(data.Cities.Select(c => c.Id));
        var knownSpecialties = new HashSet<OfficerSpecialty>(
            data.Specialties.Where(s => s.IsAssignable).Select(s => s.Specialty));

        // 人才归属来自旁挂的 officerStates（契约 OfficerTemplate 不含归属字段）。
        var stateByOfficer = new Dictionary<int, OfficerStateData>();
        foreach (var group in data.OfficerStates.GroupBy(s => s.OfficerId).Where(g => g.Count() > 1))
        {
            r.Error(ValidationCode.DuplicateId, $"officers.json#officerStates[officerId={group.Key}]",
                $"人才状态 officerId {group.Key} 重复 {group.Count()} 次。");
        }
        foreach (var s in data.OfficerStates)
        {
            if (!officers.Any(o => o.Id == s.OfficerId))
            {
                r.Error(ValidationCode.OfficerFactionUnknown,
                    $"officers.json#officerStates[officerId={s.OfficerId}]",
                    $"人才状态引用了不存在的人才 Id {s.OfficerId}。");
                continue;
            }
            stateByOfficer[s.OfficerId] = s;

            var sloc = $"officers.json#officerStates[officerId={s.OfficerId}]";

            if (s.OwnerFactionId != -1 && !factionIds.Contains(s.OwnerFactionId))
            {
                r.Error(ValidationCode.OfficerFactionUnknown, $"{sloc}.ownerFactionId",
                    $"人才 {s.OfficerId} 归属势力 {s.OwnerFactionId} 不存在。"
                    + $"现有势力：{(factionIds.Count == 0 ? "无" : string.Join(", ", factionIds.OrderBy(x => x)))}。");
            }

            if (s.PostCityId != -1 && !cityIds.Contains(s.PostCityId))
            {
                r.Error(ValidationCode.OfficerFactionUnknown, $"{sloc}.postCityId",
                    $"人才 {s.OfficerId} 的任职城池 {s.PostCityId} 不存在。");
            }

            if (!Enum.IsDefined(s.Post))
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{sloc}.post",
                    $"人才 {s.OfficerId} 的岗位 \"{s.Post}\" 不是合法的 PostType 枚举值。");
            }
            else if (s.Post != PostType.None && s.PostCityId == -1)
            {
                r.Warn(ValidationCode.ValueOutOfRange, sloc,
                    $"人才 {s.OfficerId} 岗位为 {s.Post}，但未指定任职城池（postCityId = -1）。");
            }
        }

        foreach (var group in officers.GroupBy(o => o.Id).Where(g => g.Count() > 1))
        {
            r.Error(ValidationCode.DuplicateId, $"officers.json#officers[id={group.Key}]",
                $"人才 Id {group.Key} 重复出现 {group.Count()} 次。");
        }

        var playerOfficerCount = 0;

        for (var i = 0; i < officers.Count; i++)
        {
            var o = officers[i];
            var loc = $"officers.json#officers[{i}](id={o.Id},{o.Name})";

            if (o.Id <= 0)
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{loc}.id",
                    $"人才 Id 为 {o.Id}，必须为正整数。");
            }

            if (string.IsNullOrWhiteSpace(o.Name))
            {
                r.Error(ValidationCode.OfficerAttrRange, $"{loc}.name", "人才姓名为空。");
            }

            CheckOfficerAttribute(r, $"{loc}.command", "统率", o.Command);
            CheckOfficerAttribute(r, $"{loc}.governance", "政务", o.Governance);
            CheckOfficerAttribute(r, $"{loc}.loyalty", "忠诚", o.Loyalty);

            if (o.Age < OfficerAgeMin || o.Age > OfficerAgeMax)
            {
                r.Error(ValidationCode.OfficerAgeRange, $"{loc}.age",
                    $"年龄 {o.Age} 超出合理区间 {OfficerAgeMin}-{OfficerAgeMax}。");
            }

            if (!stateByOfficer.TryGetValue(o.Id, out var state))
            {
                r.Error(ValidationCode.OfficerFactionUnknown, loc,
                    $"人才 {o.Id}（{o.Name}）缺少对应的 officerStates 条目，归属势力未知，开局状态无法组装。");
                continue;
            }

            if (!Enum.IsDefined(o.Specialty))
            {
                r.Error(ValidationCode.OfficerSpecialtyUnknown, $"{loc}.specialty",
                    $"专长值 \"{o.Specialty}\" 不是合法的 OfficerSpecialty 枚举值。");
            }
            else if (o.Specialty != OfficerSpecialty.None && knownSpecialties.Count > 0
                     && !knownSpecialties.Contains(o.Specialty))
            {
                r.Error(ValidationCode.OfficerSpecialtyUnknown, $"{loc}.specialty",
                    $"人才 {o.Id}（{o.Name}）的专长 \"{o.Specialty}\" 在 specialties.json 中没有对应条目。");
            }

            // ---- 玩家人才必须明显偏弱（玩法设计要求，不是随手填）----
            if (state.OwnerFactionId == PlayerFactionId)
            {
                playerOfficerCount++;
                if (o.Command > PlayerOfficerAttributeCeiling || o.Governance > PlayerOfficerAttributeCeiling)
                {
                    r.Error(ValidationCode.OfficerAttrRange, loc,
                        $"玩家势力人才 {o.Id}（{o.Name}）Command={o.Command}、Governance={o.Governance}，"
                        + $"超出玩家人才上限 {PlayerOfficerAttributeCeiling}。"
                        + "玩家是『没落贵族』，开局人才必须明显偏弱，否则『用人』这个核心玩法失去意义。");
                }

                if (o.Command < PlayerOfficerAttributeFloor || o.Governance < PlayerOfficerAttributeFloor)
                {
                    r.Warn(ValidationCode.OfficerAttrRange, loc,
                        $"玩家势力人才 {o.Id}（{o.Name}）Command={o.Command}、Governance={o.Governance}，"
                        + $"低于建议下限 {PlayerOfficerAttributeFloor}。老部下再平庸也不该低于这个水平。");
                }
            }
        }

        if (playerOfficerCount == 0)
        {
            r.Warn(ValidationCode.OfficerFactionUnknown, "officers.json#officerStates",
                $"没有任何人才归属玩家势力（ownerFactionId = {PlayerFactionId}）。玩家将无人可用。");
        }
    }

    // ========================================================================
    //  5. 事件
    // ========================================================================

    private static void ValidateEvents(GameDataSet data, ValidationResult r)
    {
        var events = data.Events;

        foreach (var group in events.GroupBy(e => e.Id).Where(g => g.Count() > 1))
        {
            r.Error(ValidationCode.DuplicateId, $"events.json#events[id={group.Key}]",
                $"事件 Id {group.Key} 重复出现 {group.Count()} 次。");
        }

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var loc = $"events.json#events[{i}](id={e.Id})";

            // 契约 v1.2：Id 已是 int，全项目实体 Id 一律正整数。
            if (e.Id <= 0)
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{loc}.id",
                    $"事件 Id 为 {e.Id}，必须为正整数。"
                    + "契约 v1.2 已把 GameEventTemplate.Id 从 string 改为 int，不再使用 EVT_xxx 这类字符串 ID。");
            }

            if (!Enum.IsDefined(e.Category))
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{loc}.category",
                    $"事件类别 \"{e.Category}\" 不是合法的 EventCategory 枚举值。");
            }

            if (e.Probability < 0 || e.Probability > RatioScale)
            {
                r.Error(ValidationCode.EventProbabilityRange, $"{loc}.probability",
                    $"事件 {e.Id} 的触发概率为 {e.Probability}，合法区间是 0-{RatioScale}（万分比，"
                    + $"{RatioScale} = 100%）。");
            }

            if (string.IsNullOrWhiteSpace(e.Message))
            {
                r.Error(ValidationCode.EventEmptyMessage, $"{loc}.message",
                    $"事件 {e.Id} 的 message 为空。该字段会直接显示给玩家，不能为空。");
            }

            foreach (var (key, value) in e.Effects)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    r.Error(ValidationCode.StringId, $"{loc}.effects",
                        $"事件 {e.Id} 存在空的 effect 键。Resolver 无法消费。");
                }

                // 事件效果允许负数（减益），但幅度要有上限，避免手滑写成 -999999。
                if (value == 0)
                {
                    r.Warn(ValidationCode.ZeroValue, $"{loc}.effects[\"{key}\"]",
                        $"事件 {e.Id} 的效果 {key} = 0，等于什么都没发生。");
                }
                else if (Math.Abs((long)value) > 1000000)
                {
                    r.Error(ValidationCode.ValueOutOfRange, $"{loc}.effects[\"{key}\"]",
                        $"事件 {e.Id} 的效果 {key} = {value}，幅度超过 100 万，请确认没有打错。");
                }
            }
        }

        // ---- 同类别概率之和不得超过 6000（60%）----
        foreach (var group in events.GroupBy(e => e.Category))
        {
            var sum = group.Sum(e => e.Probability);
            if (sum > MaxCategoryProbabilitySum)
            {
                var detail = string.Join(", ", group.Select(e => $"{e.Id}={e.Probability}"));
                r.Error(ValidationCode.EventCategorySum, $"events.json#events[category={group.Key}]",
                    $"类别 {group.Key} 的事件概率之和为 {sum}，超过上限 {MaxCategoryProbabilitySum}。"
                    + $"明细：{detail}。每回合触发过多会让游戏变成抽奖。");
            }
        }
    }

    // ========================================================================
    //  6. 特质表
    // ========================================================================

    private static void ValidateSpecialties(GameDataSet data, ValidationResult r)
    {
        foreach (var group in data.Specialties.GroupBy(s => s.Id).Where(g => g.Count() > 1))
        {
            r.Error(ValidationCode.DuplicateId, $"specialties.json#specialties[id={group.Key}]",
                $"特质 Id {group.Key} 重复出现 {group.Count()} 次。");
        }

        for (var i = 0; i < data.Specialties.Count; i++)
        {
            var s = data.Specialties[i];
            var loc = $"specialties.json#specialties[{i}](id={s.Id},{s.Name})";

            if (s.Id <= 0)
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{loc}.id",
                    $"特质 Id 为 {s.Id}，必须为正整数。");
            }

            if (!Enum.IsDefined(s.Specialty))
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{loc}.specialty",
                    $"特质 {s.Id} 的 specialty \"{s.Specialty}\" 不是合法的 OfficerSpecialty 枚举值。");
            }

            if (!Enum.IsDefined(s.PreferredPost))
            {
                r.Error(ValidationCode.ValueOutOfRange, $"{loc}.preferredPost",
                    $"特质 {s.Id} 的 preferredPost \"{s.PreferredPost}\" 不是合法的 PostType 枚举值。");
            }

            if (s.BonusPercent < 0)
            {
                r.Error(ValidationCode.NegativeValue, $"{loc}.bonusPercent",
                    $"特质 {s.Id}（{s.Name}）的加成百分比为 {s.BonusPercent}，不允许负数。");
            }
            else if (s.BonusPercent > RatioScale)
            {
                r.Warn(ValidationCode.RatioOutOfRange, $"{loc}.bonusPercent",
                    $"特质 {s.Id}（{s.Name}）的加成为 {s.BonusPercent}，超过 {RatioScale}（=100%），"
                    + "请确认单位是万分比而不是百分数。");
            }

            if (string.IsNullOrWhiteSpace(s.Name))
            {
                r.Error(ValidationCode.MissingComment, $"{loc}.name", $"特质 Id {s.Id} 的名称为空。");
            }
        }
    }

    // ========================================================================
    //  7. 难度表
    // ========================================================================

    private static void ValidateDifficulties(GameDataSet data, ValidationResult r)
    {
        foreach (var level in Enum.GetValues<DifficultyLevel>())
        {
            if (!data.Difficulties.Any(d => d.Difficulty == level))
            {
                r.Error(ValidationCode.DifficultyMissing, $"difficulty.json#difficulties[{level}]",
                    $"难度档位 {level} 在 difficulty.json 中缺失。三档必须齐全，否则读档会拿不到系数。");
            }
        }

        foreach (var group in data.Difficulties.GroupBy(d => d.Difficulty).Where(g => g.Count() > 1))
        {
            r.Error(ValidationCode.DuplicateId, $"difficulty.json#difficulties[{group.Key}]",
                $"难度 {group.Key} 配置了 {group.Count()} 条，重复。");
        }

        foreach (var d in data.Difficulties)
        {
            var loc = $"difficulty.json#difficulties[{d.Difficulty}]";

            if (d.AiResourceMultiplierPercent <= 0)
            {
                r.Error(ValidationCode.DifficultyMultiplier, $"{loc}.aiResourceMultiplierPercent",
                    $"难度 {d.Difficulty} 的 AI 资源倍率为 {d.AiResourceMultiplierPercent}，必须为正数。");
            }

            CheckPercentRange(r, $"{loc}.aiAggressionPercent", $"难度 {d.Difficulty} 的 AI 积极度",
                d.AiAggressionPercent);
        }
    }

    // ========================================================================
    //  8. 名字词库
    // ========================================================================

    /// <summary>
    /// 校验名字生成器词库。
    /// </summary>
    /// <remarks>
    /// 【为什么词库规模是硬校验】名字生成靠随机组合，组合数 = 各段条目数的乘积。
    /// 词库太小会直接导致重名——玩家看到两座同名城池时的观感伤害，
    /// 比数据缺个字段严重得多。所以规模不够必须在启动前拦下。
    /// <para>
    /// 【为什么还查重复词条】词库里出现两个一模一样的词，等于凭空缩小了组合数，
    /// 而这个问题从文件大小上看不出来，只能逐条比。
    /// </para>
    /// </remarks>
    private static void ValidateNameBank(GameDataSet data, ValidationResult r)
    {
        var bank = data.Names;

        CheckWordList(r, "names/faction_words.json#adjectives", "势力名形容词", bank.FactionAdjectives, NameWordBank.Minimums.FactionAdjectives);
        CheckWordList(r, "names/faction_words.json#nouns", "势力名名词", bank.FactionNouns, NameWordBank.Minimums.FactionNouns);
        CheckWordList(r, "names/city_prefix.json#prefixes", "城名前缀", bank.CityPrefixes, NameWordBank.Minimums.CityPrefixes);
        CheckWordList(r, "names/city_prefix.json#suffixes", "城池名后缀", bank.CitySuffixes, NameWordBank.Minimums.CitySuffixes);
        CheckWordList(r, "names/officer_surname.json#surnames", "人才姓氏", bank.OfficerSurnames, NameWordBank.Minimums.OfficerSurnames);
        CheckWordList(r, "names/officer_given.json#givenNames", "人才名", bank.OfficerGivenNames, NameWordBank.Minimums.OfficerGivenNames);
        CheckWordList(r, "names/ruler_given.json#givenNames", "君主名", bank.RulerGivenNames, NameWordBank.Minimums.RulerGivenNames);
        CheckWordList(r, "names/specialty_title.json#dialects", "方言词", bank.Dialects, NameWordBank.Minimums.Dialects);

        // 称号必须覆盖每一个 OfficerSpecialty 枚举值。
        // 漏一个枚举意味着该专长的人才在 UI 上没有称号，属于功能缺失而不是内容缺失。
        foreach (var spec in Enum.GetValues<OfficerSpecialty>())
        {
            var titles = bank.TitlesFor(spec);
            var loc = $"names/specialty_title.json#titles[\"{spec}\"]";

            if (titles.Count == 0)
            {
                r.Error(ValidationCode.NameBankMissing, loc,
                    $"专长 {spec} 在称号词库中没有条目。INameGenerator.GenerateSpecialtyTitle 会返回空串。");
                continue;
            }

            if (titles.Count < NameWordBank.Minimums.SpecialtyTitlesPerEnum)
            {
                r.Error(ValidationCode.NameBankTooSmall, loc,
                    $"专长 {spec} 的称号只有 {titles.Count} 条，少于下限 {NameWordBank.Minimums.SpecialtyTitlesPerEnum} 条。");
            }

            CheckNoDuplicates(r, loc, titles);

            foreach (var t in titles.Where(string.IsNullOrWhiteSpace))
            {
                r.Error(ValidationCode.NameBankMissing, loc,
                    $"专长 {spec} 的称号列表中存在空白条目。");
                break;
            }
        }

        foreach (var key in bank.SpecialtyTitles.Keys)
        {
            if (!Enum.TryParse<OfficerSpecialty>(key, ignoreCase: true, out _))
            {
                r.Error(ValidationCode.NameBankMissing, $"names/specialty_title.json#titles[\"{key}\"]",
                    $"称号词库的键 \"{key}\" 不是合法的 OfficerSpecialty 枚举值，"
                    + "该分组永远不会被查询到。");
            }
        }
    }

    /// <summary>校验单个词条数组：规模下限 + 空白项 + 重复项。</summary>
    private static void CheckWordList(
        ValidationResult r, string loc, string label, List<string> words, int minimum)
    {
        if (words.Count == 0)
        {
            r.Error(ValidationCode.NameBankMissing, loc, $"{label}词库为空，无法生成名字。");
            return;
        }

        if (words.Count < minimum)
        {
            r.Error(ValidationCode.NameBankTooSmall, loc,
                $"{label}词库只有 {words.Count} 条，少于下限 {minimum} 条。"
                + "组合数不足会导致重名。补词即可，不需要改代码。");
        }

        CheckNoDuplicates(r, loc, words);

        var blank = words.Count(string.IsNullOrWhiteSpace);
        if (blank > 0)
        {
            r.Error(ValidationCode.NameBankMissing, loc, $"{label}词库中有 {blank} 条空白词条。");
        }
    }

    /// <summary>词库内重复检测。重复词会凭空缩小组合数，从文件大小上完全看不出来。</summary>
    private static void CheckNoDuplicates(ValidationResult r, string loc, List<string> words)
    {
        var dup = words
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .GroupBy(w => w, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (dup.Count > 0)
        {
            r.Error(ValidationCode.NameBankDuplicate, loc,
                $"词库中有 {dup.Count} 个重复词条：{string.Join("、", dup.Take(10))}"
                + (dup.Count > 10 ? " 等" : string.Empty)
                + "。重复条目等于凭空缩小组合数，会导致重名。");
        }
    }

    // ========================================================================
    //  9. 文本层规则（浮点数、_comment 覆盖、占位标记）
    // ========================================================================

    private static void ValidateTextLevelRules(GameDataSet data, ValidationResult r)
    {
        foreach (var (file, json) in data.RawJson)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                r.Error(ValidationCode.MissingComment, file, $"数据文件 {file} 内容为空。");
                continue;
            }

            CheckNoFloatLiterals(r, file, json);

            if (!json.Contains("\"_comment\"", StringComparison.Ordinal))
            {
                r.Error(ValidationCode.MissingComment, file,
                    $"数据文件 {file} 缺少 _comment 字段。半年后没人记得这个数为什么是这个数。");
            }
        }

        // 占位数值必须显式带 TODO 标记，这是"未定稿"的唯一诚实表达方式。
        foreach (var (file, json) in data.RawJson)
        {
            if (json.Contains("\"TODO", StringComparison.OrdinalIgnoreCase)) continue;
            if (!data.FileComments.TryGetValue(file, out var comment)) continue;
            if (!comment.Contains("TODO", StringComparison.OrdinalIgnoreCase)) continue;
            // _comment 里说明了 TODO，但文件内没有 TODO 标记，允许，仅作提示。
            r.Info(ValidationCode.DeclaredTodo, file,
                $"{file} 的说明里提到 TODO，建议在文件内加显式 TODO(占位) 标记以便全局检索。");
        }

        foreach (var todo in data.DeclaredTodos)
        {
            r.Info(ValidationCode.DeclaredTodo, "*.json#_todos", todo);
        }
    }

    /// <summary>
    /// 扫描原始 JSON 文本，禁止出现浮点数字面量。
    /// </summary>
    /// <remarks>
    /// 【为什么必须在文本层检查】资源数量在反序列化后都是 int，看不出原始写法是
    /// 3000 还是 3000.0。契约明确要求"禁止浮点数存资源"（粮草几千石的浮点累加
    /// 会积累误差），所以只能在 JSON 文本还没被解析的时候拦。
    /// 排除项：数组下标、行号注释等不含小数点，这里只匹配"数字.数字"形态。
    /// </remarks>
    private static void CheckNoFloatLiterals(ValidationResult r, string file, string json)
    {
        var span = json.AsSpan();
        var inString = false;
        var escaped = false;

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }

            if (ch == '"') { inString = true; continue; }

            // 定位一个数字的开头。
            if (!char.IsAsciiDigit(ch) && ch != '-') continue;
            if (ch == '-' && (i + 1 >= span.Length || !char.IsAsciiDigit(span[i + 1]))) continue;

            var start = i;
            var j = i + 1;
            while (j < span.Length && char.IsAsciiDigit(span[j])) j++;

            // 小数点：后面必须紧跟数字才算浮点，避免命中版本号之类的文本。
            if (j < span.Length && span[j] == '.' && j + 1 < span.Length && char.IsAsciiDigit(span[j + 1]))
            {
                var end = j;
                while (end < span.Length && (char.IsAsciiDigit(span[end]) || span[end] == '.')) end++;
                var token = span[start..end].ToString();
                r.Error(ValidationCode.FloatUsed, $"{file}#{token}",
                    $"数据文件 {file} 中出现浮点数字面量 \"{token}\"。"
                    + "契约要求资源数量一律用 int：浮点数累加会产生精度误差，粮银兵三项严禁使用小数。");
            }

            i = j - 1;
        }
    }

    // ========================================================================
    //  工具方法
    // ========================================================================

    /// <summary>检查数值非负。负数一律报错并给出实际值。</summary>
    private static void CheckNonNegative(ValidationResult r, string location, int value)
    {
        if (value < 0)
        {
            r.Error(ValidationCode.NegativeValue, location,
                $"数值为 {value}，不允许为负数。请检查是否把减益效果写成了负的资源存量。");
        }
    }

    /// <summary>检查数值非负，错误信息中带上字段中文名，便于策划定位。</summary>
    private static void CheckNonNegative(ValidationResult r, string location, string label, int value)
    {
        if (value < 0)
        {
            r.Error(ValidationCode.NegativeValue, location,
                $"{label}为 {value}，不允许为负数。请检查是否把减益效果写成了负的资源存量。");
        }
    }

    /// <summary>检查百分数语义字段落在 0-100。</summary>
    private static void CheckPercentRange(ValidationResult r, string location, string label, int value)
    {
        if (value < 0 || value > 100)
        {
            r.Error(ValidationCode.ValueOutOfRange, location,
                $"{label}为 {value}，百分数语义要求落在 0-100（100 表示 100%）。");
        }
    }

    /// <summary>
    /// 检查万分比语义字段。
    /// </summary>
    /// <remarks>
    /// 【为什么要单独一套，而不是复用 CheckPercentRange】
    /// 契约 v1.2 起全项目比率一律万分比。两套语义混在一份代码里，
    /// 迟早有人把 35 填进万分比字段——那会让所有产出缩小100 倍，
    /// 而且从数据上看不出任何异常。所以这里对「超过 RatioScale 却没有超出
    /// MaxRatio」的情况额外给一条专门的错误码，直接点名刻度写错。
    /// </remarks>
    private static void CheckRatio(ValidationResult r, string location, string label, int value)
    {
        if (value < 0)
        {
            r.Error(ValidationCode.NegativeValue, location,
                $"{label}为 {value}，比率不允许为负数。");
            return;
        }

        if (value > MaxRatio)
        {
            r.Error(ValidationCode.RatioOutOfRange, location,
                $"{label}为 {value}，超出万分比允许的上界 {MaxRatio}。请确认没有多打零。");
            return;
        }

        if (value > RatioPlausibleCeiling)
        {
            r.Error(ValidationCode.RatioOutOfRange, location,
                $"{label}为 {value}，超过 {RatioScale}（= 100%）。"
                + "契约 v1.2 起比率一律用万分比：35% 应写成 3500，而不是 35。"
                + "若确实需要大于 100% 的倍率，请确认这个数没写错。");
            return;
        }

        // 反向刻度错误：把万分比字段按百分数填（35 代替 3500）。
        // 这类错误从数据表面完全看不出异常——它是一个合法的正整数，
        // 只是让所有相关计算缩小 100 倍。要等到无头模拟跑出诡异结果才会被发现，
        // 那时候已经很难回溯是哪个字段。所以在这里用「小到不合理」作启发式提示。
        //
        // 之所以只是告警而不是错误：确实存在合法的极小比率
        // （例如万分比口径下的 0.35%），不能一棍子打死。
        // 真正的把关仍在评审与无头模拟，这里只负责把可疑值喊出来。
        if (value > 0 && value < 100)
        {
            r.Warn(ValidationCode.RatioOutOfRange, location,
                $"{label}为 {value}，看起来像是把百分数当成了万分比"
                + $"（契约 v1.2 口径下，35% = 3500）。若本字段确实是万分比，请确认这个数没漏两个零。");
        }
    }

    /// <summary>把整数列表格式化为可读文本，用于错误信息。</summary>
    private static string FormatList(List<int> values)
        => values.Count == 0 ? "（空）" : string.Join(", ", values);

    /// <summary>检查人才属性落在 0-100。</summary>
    private static void CheckOfficerAttribute(
        ValidationResult r, string location, string label, int value)
    {
        if (value < OfficerAttributeMin || value > OfficerAttributeMax)
        {
            r.Error(ValidationCode.OfficerAttrRange, location,
                $"{label}为 {value}，合法区间是 {OfficerAttributeMin}-{OfficerAttributeMax}。");
        }
    }

    /// <summary>
    /// 校验每势力恰好一座首都，且与 factions.json 的 capitalCityId 双向一致。
    /// </summary>
    private static void ValidateCapitalPerFaction(List<CityTemplate> cities, ValidationResult r)
    {
        var byFaction = cities.GroupBy(c => c.FactionId);
        foreach (var group in byFaction)
        {
            var capitals = group.Where(c => c.IsCapital).ToList();
            var loc = $"cities.json#cities[factionId={group.Key}]";
            var factionDesc = group.Select(c => $"{c.Id}({c.Name})");

            switch (capitals.Count)
            {
                case 1:
                    break;
                case 0:
                    r.Error(ValidationCode.CityCapitalCount, loc,
                        $"势力 {group.Key} 名下没有首都（isCapital = true）。"
                        + $"该势力的城池：{string.Join(", ", factionDesc)}。每个势力必须恰好有一座首都。");
                    break;
                default:
                    r.Error(ValidationCode.CityCapitalCount, loc,
                        $"势力 {group.Key} 名下有 {capitals.Count} 座首都："
                        + $"{string.Join(", ", capitals.Select(c => $"{c.Id}({c.Name})"))}。"
                        + $"该势力的城池：{string.Join(", ", factionDesc)}。每个势力必须恰好有一座首都。");
                    break;
            }
        }
    }
}
