// ============================================================================
//  StrategyGame.Data —— 本包自定义的数据模板
// ============================================================================
//  【铁律】契约里已有的类型（BalanceConfig / OfficerTemplate / CityTemplate /
//  GameEventTemplate / Faction / Resources）本文件一律不重复声明，只引用。
//  本文件只定义契约尚未覆盖的模板。
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using StrategyGame.Contracts;

namespace StrategyGame.Data.Config;

// ============================================================================
//  1. 特质效果表
// ============================================================================

/// <summary>
/// 特质模板。对应 assets/data/specialties.json。
/// </summary>
/// <remarks>
/// 【为什么需要它】契约只定义了 OfficerSpecialty 枚举（专长"是什么"），
/// 但没有定义专长的"效果数值"（加成多少、适配哪些岗位）。这两件事必须分离：
/// 枚举是契约，改它要动所有包；数值是策划自由，调它不该编译。
/// </remarks>
public sealed class SpecialtyTemplate
{
    /// <summary>特质唯一整数 ID。禁止字符串 ID。</summary>
    public int Id { get; set; }

    /// <summary>对应的专长枚举。人才身上的 Specialty 必须能在这里找到定义。</summary>
    public OfficerSpecialty Specialty { get; set; }

    /// <summary>特质中文名，用于 UI 展示。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>特质说明，写清楚这个特质到底让什么变强。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>该特质最适合的岗位。人才任命到该岗位时加成最高。</summary>
    public PostType PreferredPost { get; set; }

    /// <summary>加成百分比。100 表示 +100%。禁止负数。</summary>
    public int BonusPercent { get; set; }

    /// <summary>该特质是否允许出现在人才身上。false 表示仅作展示或预留。</summary>
    public bool IsAssignable { get; set; } = true;
}

// ============================================================================
//  2. 难度档位表
// ============================================================================

/// <summary>
/// 难度档位预设。对应 assets/data/difficulty.json。
/// </summary>
/// <remarks>
/// 【为什么单独一个文件】契约的 BalanceConfig 不含难度系数（因为难度只影响开局
/// 加载，运行中不得改变）。把难度系数混进 config.json 会诱发"运行时改难度"的
/// 坏代码，所以物理隔离成独立文件，由开局流程一次性读取后写入 WorldState。
/// </remarks>
public sealed class DifficultyPreset
{
    /// <summary>难度枚举值。</summary>
    public DifficultyLevel Difficulty { get; set; }

    /// <summary>UI 显示名。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>AI 资源倍率（百分数）。130 表示 AI 资源 ×1.3。必须大于 0。</summary>
    public int AiResourceMultiplierPercent { get; set; } = 100;

    /// <summary>AI 扩张积极度（百分数）。数值越高 AI 越倾向于主动出兵。</summary>
    public int AiAggressionPercent { get; set; } = 50;

    /// <summary>AI 是否允许主动发起攻城。false 表示只防守与屯田。</summary>
    public bool AiCanInitiateSiege { get; set; }

    /// <summary>预设说明。</summary>
    public string Description { get; set; } = string.Empty;
}

// ============================================================================
//  3. 城池初始状态（契约 CityTemplate 未覆盖的字段）
// ============================================================================

/// <summary>
/// 城池开局状态。对应 cities.json 中的 cityStates 数组，按 cityId 与城池一一对应。
/// </summary>
/// <remarks>
/// 【为什么不塞进 CityTemplate】CityTemplate 是契约类型，字段名不可改、不可加。
/// 它只有"城池身份"，没有"城池当前状态"。状态属于内容数据层的职责，
/// 所以用旁挂数组表达，运行时由 03 包组装成契约的 City 对象。
/// </remarks>
public sealed class CityStateData
{
    /// <summary>对应 CityTemplate.Id。</summary>
    public int CityId { get; set; }

    /// <summary>人口上限。CityTemplate 只有当前人口 Population，上限属于开局状态，故旁挂。</summary>
    public int MaxPopulation { get; set; }

    /// <summary>当前城防值。必须 ≥ 0 且 ≤ MaxDefense。</summary>
    public int Defense { get; set; }

    /// <summary>城防上限。升级城防的收益以此为封顶。</summary>
    public int MaxDefense { get; set; }

    /// <summary>城防等级。影响城防成长速度。</summary>
    public int FortifyLevel { get; set; }

    /// <summary>屯田等级。影响农业产出。</summary>
    public int FarmLevel { get; set; }
}

// ============================================================================
//  3b. 人才初始状态（契约 OfficerTemplate 未覆盖的字段）
// ============================================================================

/// <summary>
/// 人才开局状态。对应 officers.json 中的 officerStates 数组，按 officerId 与人才一一对应。
/// </summary>
/// <remarks>
/// 【为什么必须旁挂】契约 OfficerTemplate 只有 Id / Name / Command / Governance /
/// Loyalty / Age / Specialty —— 它描述的是"这个人有多强"，不包含"这个人属于谁"。
/// 但势力归属是内容数据的必要信息（否则无法校验玩家势力的人才强度），
/// 所以用旁挂数组表达归属与任职，运行时由 03 包组装成契约的 Officer 对象。
/// </remarks>
public sealed class OfficerStateData
{
    /// <summary>对应 OfficerTemplate.Id。</summary>
    public int OfficerId { get; set; }

    /// <summary>归属势力 Id。-1 表示在野人才（尚未被任何势力招揽）。</summary>
    public int OwnerFactionId { get; set; } = -1;

    /// <summary>任职城池 Id。-1 表示未任职。</summary>
    public int PostCityId { get; set; } = -1;

    /// <summary>当前岗位。未任职时为 PostType.None。</summary>
    public PostType Post { get; set; } = PostType.None;

    /// <summary>是否在世。开局为 false 的人不会进入可用人才列表。</summary>
    public bool IsAlive { get; set; } = true;
}

// ============================================================================
//  3c. 契约外的平衡细则（裁决：不扩契约）
// ============================================================================

/// <summary>
/// 02 包自己的平衡细则。对应 config.json 的 extendedBalance 段。
/// </summary>
/// <remarks>
/// 【为什么这些字段不进契约】
/// 返工裁决给的判断标准只有一条：03 包需要读它吗？不需要 → 不进契约。
/// 任务书 R2 表格里的 stabilityNaturalRecovery / moraleNaturalRecovery /
/// moraleDecayWhenPeace / rewardCostSilver / rewardLoyaltyGain 都属于
/// 「02 自己记下来的设计细则」——它们要么与契约已有字段同义（只是叫法不同），
/// 要么对应骨架阶段根本不实现的 ActionType.Reward。把它们塞进契约会让
/// 所有包都看见一批用不到的字段，那才是真正的契约污染。
/// <para>
/// expectedPlayerCityCount 放这里而不是常量，是同一逻辑的另一面：
/// 它是「设计期望值」，应当由策划在 JSON 里改，而不是由程序员在代码里改。
/// </para>
/// </remarks>
public sealed class ExtendedBalance
{
    /// <summary>稳定度自然恢复量（绝对点数/月）。与契约的 StabilityDecayPerMonth 语义相反，见 config.json 说明。</summary>
    public int StabilityNaturalRecovery { get; set; }

    /// <summary>士气自然恢复量（绝对点数/月）。</summary>
    public int MoraleNaturalRecovery { get; set; }

    /// <summary>和平状态下的士气衰减（绝对点数/月）。与契约 MoraleDecayPerMonth 同义，保留任务书原字段名以便追溯。</summary>
    public int MoraleDecayWhenPeace { get; set; }

    /// <summary>赏赐一次的银两消耗（绝对数量）。</summary>
    public int RewardCostSilver { get; set; }

    /// <summary>赏赐一次的忠诚度提升（绝对点数）。</summary>
    public int RewardLoyaltyGain { get; set; }

    /// <summary>
    /// 玩家势力应有的城池数量。校验器的硬校验依据。
    /// 不写死在代码里——设计改数值时只改这个 JSON 字段，校验器自动跟随。
    /// </summary>
    public int ExpectedPlayerCityCount { get; set; } = 2;
}

// ============================================================================
//  4. 全量数据集
// ============================================================================

/// <summary>
/// 一次加载得到的全部内容数据。
/// </summary>
public sealed class GameDataSet
{
    /// <summary>全局平衡参数，来自 config.json。</summary>
    public BalanceConfig Balance { get; set; } = new();

    /// <summary>契约外的平衡细则，来自 config.json 的 extendedBalance 段。</summary>
    public ExtendedBalance Extended { get; set; } = new();

    /// <summary>人才初始表，来自 officers.json。</summary>
    public List<OfficerTemplate> Officers { get; set; } = new();

    /// <summary>城池初始表，来自 cities.json。</summary>
    public List<CityTemplate> Cities { get; set; } = new();

    /// <summary>城池开局状态，来自 cities.json 的 cityStates 数组。</summary>
    public List<CityStateData> CityStates { get; set; } = new();

    /// <summary>人才开局状态（归属势力与任职），来自 officers.json 的 officerStates 数组。</summary>
    public List<OfficerStateData> OfficerStates { get; set; } = new();

    /// <summary>事件库，来自 events.json。</summary>
    public List<GameEventTemplate> Events { get; set; } = new();

    /// <summary>特质效果表，来自 specialties.json。</summary>
    public List<SpecialtyTemplate> Specialties { get; set; } = new();

    /// <summary>势力初始表，来自 factions.json。</summary>
    public List<Faction> Factions { get; set; } = new();

    /// <summary>难度档位表，来自 difficulty.json。</summary>
    public List<DifficultyPreset> Difficulties { get; set; } = new();

    /// <summary>
    /// 各数据文件的原始 JSON 文本，key 为文件名。
    /// 校验器需要它做"文本层"检查（浮点数字、_comment 覆盖度），这些信息在反序列化后已经丢失。
    /// </summary>
    public Dictionary<string, string> RawJson { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 数据文件里声明的 TODO（_todos 数组）。
    /// 这些是"尚未定稿、当前为占位值"的显式记录，必须原样上报，不能吞掉。
    /// </summary>
    public List<string> DeclaredTodos { get; set; } = new();

    /// <summary>数据文件里的 _comment 说明（文件名 → 说明）。</summary>
    public Dictionary<string, string> FileComments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>名字生成器词库，来自 assets/data/names/ 下的 6 个文件。</summary>
    public NameWordBank Names { get; set; } = new();
}

// ============================================================================
//  6. 名字生成器词库
// ============================================================================

/// <summary>
/// R7 名字生成器词库。6 个文件的合并结果。
/// </summary>
/// <remarks>
/// 【为什么词库是数据而不是代码里的数组】
/// 名字是可调内容，策划要能自己加词。放代码里就得重新编译，与本包
/// 「数值从代码里搬出去」的初衷相反。
/// <para>
/// 【禁止真实历史地名与人物名】
/// 世界观是架空的（D13）。混入真实名字会让玩家分不清这是虚构世界还是
/// 真实历史，也会带来不必要的政治敏感风险。因此词库全部为自造词，
/// 并且刻意回避高辨识度的历史人物姓氏与称号（不写「武圣」，写「鏖兵手」）。
/// </para>
/// </remarks>
public sealed class NameWordBank
{
    /// <summary>势力名形容词段。不少于 60 条。</summary>
    public List<string> FactionAdjectives { get; set; } = new();

    /// <summary>势力名名词段。不少于 60 条。</summary>
    public List<string> FactionNouns { get; set; } = new();

    /// <summary>城名前缀。不少于 40 条。</summary>
    public List<string> CityPrefixes { get; set; } = new();

    /// <summary>城池名后缀。不少于 25 条。</summary>
    public List<string> CitySuffixes { get; set; } = new();

    /// <summary>人才姓氏。不少于 80 条。</summary>
    public List<string> OfficerSurnames { get; set; } = new();

    /// <summary>人才名。不少于 80 条。</summary>
    public List<string> OfficerGivenNames { get; set; } = new();

    /// <summary>君主名（不含姓）。不少于 80 条。</summary>
    public List<string> RulerGivenNames { get; set; } = new();

    /// <summary>方言词。不少于 30 条。</summary>
    public List<string> Dialects { get; set; } = new();

    /// <summary>专长称号，按 OfficerSpecialty 分组。键名与契约枚举值一致。</summary>
    public Dictionary<string, List<string>> SpecialtyTitles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 各词库的条目数下限。低于即校验失败。
    /// </summary>
    /// <remarks>
    /// 词库太小会让名字明显重复（组合数不足）。这些下限取自任务书 R7 与返工清单，
    /// 写在这里而不是散落在测试里，是为了让「设计改词库规模」和「改校验规则」是两件事。
    /// </remarks>
    public static class Minimums
    {
        public const int FactionAdjectives = 60;
        public const int FactionNouns = 60;
        public const int CityPrefixes = 40;
        public const int CitySuffixes = 25;
        public const int OfficerSurnames = 80;
        public const int OfficerGivenNames = 80;
        public const int RulerGivenNames = 80;
        public const int Dialects = 30;
        public const int SpecialtyTitlesPerEnum = 3;
    }

    /// <summary>
    /// 取指定专长的称号列表。专长不在词库中时返回空列表（由校验器报错，不在这里兜底造词）。
    /// </summary>
    public List<string> TitlesFor(OfficerSpecialty specialty)
        => SpecialtyTitles.TryGetValue(specialty.ToString(), out var list) ? list : new List<string>();
}

// ============================================================================
//  7. 词库文件的反序列化形状
// ============================================================================

/// <summary>
/// 词库文件的内部形状，只在加载时用一次。
/// </summary>
/// <remarks>
/// 【为什么不直接用 Dictionary&lt;string, List&lt;string&gt;&gt; 统一读所有词库】
/// 词库文件里除了词条数组还有 _comment / _todos，键名各不相同
/// （adjectives / surnames / givenNames / prefixes…）。
/// 用具名类型把这些差异固定下来，策划改错键名时会得到一条指名道姓的错误，
/// 而不是「加载出来是空的，生成的名字全是空串」这种晚期症状。
/// </remarks>
public sealed class NameFactionWordsFile
{
    public List<string> Adjectives { get; set; } = new();
    public List<string> Nouns { get; set; } = new();
}

/// <summary>城池前后缀词库文件的形状。</summary>
public sealed class NameCityFile
{
    public List<string> Prefixes { get; set; } = new();
    public List<string> Suffixes { get; set; } = new();
}

/// <summary>专长称号词库文件的形状。titles 的键名与 OfficerSpecialty 枚举值一致。</summary>
public sealed class NameSpecialtyTitleFile
{
    public Dictionary<string, List<string>> Titles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Dialects { get; set; } = new();
}

// ============================================================================
//  5. 序列化选项
// ============================================================================

/// <summary>
/// 内容数据文件的序列化选项。
/// </summary>
/// <remarks>
/// 【与 WireFormat 的关系】字段命名策略与枚举处理完全复用契约的 WireFormat.Options，
/// 保证数据层与线格式不会漂移。唯一差别是 WriteIndented = true —— WireFormat 面向
/// 网络传输要紧凑，而数据文件面向策划阅读，必须缩进 2 空格（任务书 R1）。
/// </remarks>
public static class ConfigJson
{
    /// <summary>数据文件专用序列化选项。缩进 2 空格。</summary>
    public static readonly JsonSerializerOptions Options = Create(writeIndented: true);

    /// <summary>往返比对用的紧凑选项。字段与枚举处理与 Options 完全一致。</summary>
    public static readonly JsonSerializerOptions CompactOptions = Create(writeIndented: false);

    /// <summary>
    /// 构造选项。直接从契约的 WireFormat.Options 派生，避免两处配置各改各的。
    /// </summary>
    public static JsonSerializerOptions Create(bool writeIndented)
    {
        var options = new JsonSerializerOptions(WireFormat.Options)
        {
            WriteIndented = writeIndented,
            // 数据文件由策划手工编辑，宁可因多一个逗号直接报错，也不要默默吞掉整段配置。
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        return options;
    }

    /// <summary>
    /// 反序列化时统一要求：不能因为字段类型不符就悄悄取默认值。
    /// </summary>
    public static readonly JsonStringEnumConverter EnumConverter = new();
}
