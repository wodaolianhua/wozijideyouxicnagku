// ============================================================================
//  配置校验器
// ============================================================================
//  【R5 要求】返回所有问题列表（不抛异常），由调用方决定是否中断。
//  【为什么重要】配置错误如果留到运行时才暴露，会表现为「数值不合理」
//  这种难以定位的现象。开局一次性校验并全部列出，能把问题挡在门外。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Config;

/// <summary>配置问题的严重级别。</summary>
public enum ConfigIssueSeverity
{
    /// <summary>警告：可以继续运行，但值得注意。</summary>
    Warning = 0,

    /// <summary>错误：应当阻止开局。</summary>
    Error = 1,
}

/// <summary>单条配置问题。</summary>
/// <param name="Severity">严重级别。</param>
/// <param name="Location">问题定位（字段路径或模板 Id）。</param>
/// <param name="Message">面向开发者的问题描述（中文）。</param>
public readonly record struct ConfigIssue(
    ConfigIssueSeverity Severity,
    string Location,
    string Message);

/// <summary>
/// 配置校验器。检查数值非负、事件概率范围、人才属性范围、城市引用存在性。
/// </summary>
public static class ConfigValidator
{
    /// <summary>人才属性的有效上限。</summary>
    public const int OfficerAttributeMax = 100;

    /// <summary>
    /// 专长加成键中代表「任意岗位」的通配值。
    /// </summary>
    /// <remarks>
    /// 契约规定 <c>SpecialtyBonus</c> 的键为 <c>"Specialty:Post"</c> 或
    /// <c>"Specialty:Any"</c>。后者不是任何 <see cref="PostType"/> 枚举值，
    /// 必须单独放行，否则合法的通配配置会被误判为错误。
    /// </remarks>
    public const string SpecialtyBonusAnyPost = "Any";

    /// <summary>城市人口的有效上限。</summary>
    public const int PopulationMax = 10_000_000;

    /// <summary>
    /// 校验整套配置，返回全部问题。
    /// </summary>
    /// <param name="balance">平衡配置。</param>
    /// <param name="officers">人才模板。</param>
    /// <param name="cities">城池模板。</param>
    /// <param name="events">事件模板。</param>
    /// <returns>问题列表。无问题时返回空列表，绝不抛异常。</returns>
    /// <remarks>
    /// 本方法刻意不抛异常：一次返回全部问题，
    /// 开发者可以一轮修完，而不是「修一个跑一次」。
    /// 是否中断开局由调用方根据是否存在 <see cref="ConfigIssueSeverity.Error"/> 决定。
    /// </remarks>
    public static List<ConfigIssue> Validate(
        BalanceConfig balance,
        List<OfficerTemplate> officers,
        List<CityTemplate> cities,
        List<GameEventTemplate> events)
    {
        var issues = new List<ConfigIssue>();

        ValidateBalance(balance, issues);
        ValidateOfficers(officers, issues);
        ValidateCities(cities, issues);
        ValidateEvents(events, issues);
        ValidateCrossReferences(cities, issues);

        return issues;
    }

    /// <summary>校验平衡配置：数值非负。</summary>
    /// <param name="balance">平衡配置。</param>
    /// <param name="issues">问题收集列表。</param>
    private static void ValidateBalance(BalanceConfig? balance, List<ConfigIssue> issues)
    {
        if (balance == null)
        {
            issues.Add(new ConfigIssue(
                ConfigIssueSeverity.Error, "BalanceConfig", "平衡配置缺失。"));
            return;
        }

        AddNonNegative(issues, "GrainPerPopPerMonth", balance.GrainPerPopPerMonth);
        AddNonNegative(issues, "SoldierPerPopPerMonth", balance.SoldierPerPopPerMonth);
        AddNonNegative(issues, "MoraleDecayPerMonth", balance.MoraleDecayPerMonth);
        AddNonNegative(issues, "StabilityDecayPerMonth", balance.StabilityDecayPerMonth);
        AddNonNegative(issues, "BaseRecruitCostSilver", balance.BaseRecruitCostSilver);
        AddNonNegative(issues, "BaseRecruitCostGrain", balance.BaseRecruitCostGrain);
        AddNonNegative(issues, "BaseFortifyCostSilver", balance.BaseFortifyCostSilver);
        AddNonNegative(issues, "BaseDevelopCostSilver", balance.BaseDevelopCostSilver);
        AddNonNegative(issues, "LoyaltyThreshold", balance.LoyaltyThreshold);
        AddNonNegative(issues, "LoyaltyDecayPerMonth", balance.LoyaltyDecayPerMonth);
        AddNonNegative(issues, "PostTakeEffectMonths", balance.PostTakeEffectMonths);
        AddNonNegative(issues, "MaxOrdersPerFactionPerMonth", balance.MaxOrdersPerFactionPerMonth);

        if (balance.LoyaltyThreshold > OfficerAttributeMax)
        {
            issues.Add(new ConfigIssue(
                ConfigIssueSeverity.Error,
                "LoyaltyThreshold",
                $"忠诚阈值应在 0-{OfficerAttributeMax} 之间，当前为 {balance.LoyaltyThreshold}。"));
        }

        if (balance.MaxOrdersPerFactionPerMonth <= 0)
        {
            issues.Add(new ConfigIssue(
                ConfigIssueSeverity.Error,
                "MaxOrdersPerFactionPerMonth",
                "每月指令上限必须大于 0，否则势力永远无法行动。"));
        }

        ValidateSpecialtyBonuses(balance, issues);
    }

    /// <summary>校验专长加成字典。</summary>
    /// <param name="balance">平衡配置。</param>
    /// <param name="issues">问题收集列表。</param>
    private static void ValidateSpecialtyBonuses(BalanceConfig balance, List<ConfigIssue> issues)
    {
        if (balance.SpecialtyBonus == null)
        {
            return;
        }

        foreach (var (key, value) in balance.SpecialtyBonus)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, "SpecialtyBonus", "专长加成存在空键。"));
                continue;
            }

            // 约定格式 "Specialty:Post" 或 "Specialty:Any"。
            // 契约明确允许 "Any" 作为通配岗位，因此不能按 PostType 解析它。
            string[] parts = key.Split(':');
            if (parts.Length != 2)
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error,
                    $"SpecialtyBonus[{key}]",
                    "专长加成键应为 \"Specialty:Post\" 或 \"Specialty:Any\" 格式。"));
                continue;
            }

            if (!Enum.TryParse(parts[0], ignoreCase: true, out OfficerSpecialty _))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error,
                    $"SpecialtyBonus[{key}]",
                    $"未知的专长 \"{parts[0]}\"。"));
            }

            if (!Enum.TryParse(parts[1], ignoreCase: true, out PostType _)
                && !string.Equals(parts[1], SpecialtyBonusAnyPost, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error,
                    $"SpecialtyBonus[{key}]",
                    $"未知的岗位 \"{parts[1]}\"，应为合法的 PostType 值或 \"Any\"。"));
            }

            if (value < 0)
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error,
                    $"SpecialtyBonus[{key}]",
                    $"加成百分比不可为负，当前为 {value}。"));
            }
        }
    }

    /// <summary>校验人才模板：属性在有效范围内。</summary>
    /// <param name="officers">人才模板列表。</param>
    /// <param name="issues">问题收集列表。</param>
    private static void ValidateOfficers(List<OfficerTemplate>? officers, List<ConfigIssue> issues)
    {
        if (officers == null)
        {
            issues.Add(new ConfigIssue(
                ConfigIssueSeverity.Error, "Officers", "人才模板缺失。"));
            return;
        }

        var seen = new HashSet<int>();
        foreach (var officer in officers)
        {
            string location = $"Officer[{officer.Id}]";

            if (officer.Id < 0)
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location, "人才 Id 不可为负。"));
            }

            if (!seen.Add(officer.Id))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location, "人才 Id 重复。"));
            }

            if (string.IsNullOrWhiteSpace(officer.Name))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location, "人才姓名为空。"));
            }

            AddInRange(issues, location, "Command", officer.Command, 0, OfficerAttributeMax);
            AddInRange(issues, location, "Governance", officer.Governance, 0, OfficerAttributeMax);
            AddInRange(issues, location, "Loyalty", officer.Loyalty, 0, OfficerAttributeMax);

            if (officer.Age <= 0)
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location, $"年龄必须大于 0，当前为 {officer.Age}。"));
            }
        }
    }

    /// <summary>校验城池模板：数值非负、相邻关系对称。</summary>
    /// <param name="cities">城池模板列表。</param>
    /// <param name="issues">问题收集列表。</param>
    private static void ValidateCities(List<CityTemplate>? cities, List<ConfigIssue> issues)
    {
        if (cities == null)
        {
            issues.Add(new ConfigIssue(
                ConfigIssueSeverity.Error, "Cities", "城池模板缺失。"));
            return;
        }

        var seen = new HashSet<int>();
        foreach (var city in cities)
        {
            string location = $"City[{city.Id}]";

            if (city.Id < 0)
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location, "城池 Id 不可为负。"));
            }

            if (!seen.Add(city.Id))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location, "城池 Id 重复。"));
            }

            if (string.IsNullOrWhiteSpace(city.Name))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location, "城池名称为空。"));
            }

            if (city.Population < 0)
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location,
                    $"人口不可为负，当前为 {city.Population}。"));
            }

            if (city.Population > PopulationMax)
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Warning, location,
                    $"人口 {city.Population} 超过上限 {PopulationMax}，请确认数值表是否填错。"));
            }

            if (city.FactionId < 0)
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location,
                    $"势力 Id 不可为负，当前为 {city.FactionId}。"));
            }
        }
    }

    /// <summary>校验事件模板：概率在 0-100 之间，同类别概率和不超过 60。</summary>
    /// <param name="events">事件模板列表。</param>
    /// <param name="issues">问题收集列表。</param>
    private static void ValidateEvents(List<GameEventTemplate>? events, List<ConfigIssue> issues)
    {
        if (events == null)
        {
            issues.Add(new ConfigIssue(
                ConfigIssueSeverity.Error, "Events", "事件模板缺失。"));
            return;
        }

        var categorySums = new Dictionary<EventCategory, int>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var evt in events)
        {
            string location = $"Event[{evt.Id}]";

            if (string.IsNullOrWhiteSpace(evt.Id))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location, "事件 Id 为空。"));
            }
            else if (!seen.Add(evt.Id))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location, "事件 Id 重复。"));
            }

            if (evt.Probability < 0 || evt.Probability > 100)
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location,
                    $"触发概率应在 0-100 之间，当前为 {evt.Probability}。"));
            }

            if (string.IsNullOrWhiteSpace(evt.Message))
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error, location, "事件文本为空。"));
            }

            categorySums.TryGetValue(evt.Category, out int sum);
            categorySums[evt.Category] = sum + evt.Probability;
        }

        foreach (var (category, sum) in categorySums)
        {
            // 契约规定同一 Category 的概率之和不得超过 60。
            if (sum > 60)
            {
                issues.Add(new ConfigIssue(
                    ConfigIssueSeverity.Error,
                    $"EventCategory[{category}]",
                    $"该类别事件概率之和为 {sum}，超过上限 60。"));
            }
        }
    }

    /// <summary>校验城池之间的交叉引用：相邻城池必须存在且关系对称。</summary>
    /// <param name="cities">城池模板列表。</param>
    /// <param name="issues">问题收集列表。</param>
    /// <remarks>
    /// 【为什么必须对称】出击校验依赖出发城的 AdjacentCityIds。
    /// 若A→B 但 B→A 不存在，从B 出兵就无法打到 A，回放与模拟都会得出
    /// 依赖于数据书写顺序的荒谬结果。
    /// </remarks>
    private static void ValidateCrossReferences(List<CityTemplate>? cities, List<ConfigIssue> issues)
    {
        if (cities == null)
        {
            return;
        }

        var adjacency = new Dictionary<int, HashSet<int>>();
        foreach (var city in cities)
        {
            adjacency[city.Id] = new HashSet<int>(city.AdjacentCityIds);
        }

        foreach (var city in cities)
        {
            foreach (int neighborId in city.AdjacentCityIds)
            {
                if (!adjacency.ContainsKey(neighborId))
                {
                    issues.Add(new ConfigIssue(
                        ConfigIssueSeverity.Error,
                        $"City[{city.Id}]",
                        $"相邻城池 {neighborId} 不存在。"));
                    continue;
                }

                if (neighborId == city.Id)
                {
                    issues.Add(new ConfigIssue(
                        ConfigIssueSeverity.Error,
                        $"City[{city.Id}]",
                        "城池不能与自身相邻。"));
                    continue;
                }

                if (!adjacency[neighborId].Contains(city.Id))
                {
                    issues.Add(new ConfigIssue(
                        ConfigIssueSeverity.Error,
                        $"City[{city.Id}]",
                        $"与城池 {neighborId} 的相邻关系不对称，需在 {neighborId} 中补回 {city.Id}。"));
                }
            }
        }
    }

    /// <summary>记录一条「不可为负」的问题。</summary>
    /// <param name="issues">问题收集列表。</param>
    /// <param name="name">字段名。</param>
    /// <param name="value">字段值。</param>
    private static void AddNonNegative(List<ConfigIssue> issues, string name, int value)
    {
        if (value < 0)
        {
            issues.Add(new ConfigIssue(
                ConfigIssueSeverity.Error, name, $"数值不可为负，当前为 {value}。"));
        }
    }

    /// <summary>记录一条「超出范围」的问题。</summary>
    /// <param name="issues">问题收集列表。</param>
    /// <param name="location">定位前缀。</param>
    /// <param name="name">字段名。</param>
    /// <param name="value">字段值。</param>
    /// <param name="min">下界（含）。</param>
    /// <param name="max">上界（含）。</param>
    private static void AddInRange(
        List<ConfigIssue> issues,
        string location,
        string name,
        int value,
        int min,
        int max)
    {
        if (value < min || value > max)
        {
            issues.Add(new ConfigIssue(
                ConfigIssueSeverity.Error,
                $"{location}.{name}",
                $"应在 {min}-{max} 之间，当前为 {value}。"));
        }
    }
}
