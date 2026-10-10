// ============================================================================
//  StrategyGame.Contracts —— 接口与序列化契约
// ============================================================================
//  【本文件解决什么问题】
//  Markdown 里的代码块无法约束实现。这里用真实的接口定义，让编译器和
//  IDE 来强制保证跨包一致性：签名对不上，编译就失败，而不是等联调时才发现。
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace StrategyGame.Contracts;

// ============================================================================
//  规则层核心接口（由 03 包实现）
// ============================================================================

/// <summary>
/// 结算器接口。
///
/// 【硬性约束】实现必须是纯函数：
///   1. 只读传入的 state，不得修改它任何字段；
///   2. 状态变化只能通过返回 ResolveResult.Deltas 表达；
///   3. 禁止使用系统时间、网络、文件IO；
///   4. 若涉及随机，必须使用注入的 IRandom，不得用 Random.Shared。
///
/// 【为什么】无头模拟器要对比两次模拟的状态差异、回放系统要从日志重放、
/// 存档要能精确回滚——这���件事全部依赖纯函数。
/// </summary>
public interface IResolver
{
    /// <summary>本结算器处理的行动类型。注册表据此分发。</summary>
    ActionType Action { get; }

    /// <summary>执行前置校验。校验不通过时不应调用 Resolve。</summary>
    ResolveResultCode Validate(WorldState state, Order order);

    /// <summary>执行结算。必须是纯函数。</summary>
    ResolveResult Resolve(WorldState state, Order order);
}

/// <summary>可播种的随机源。规则层所有随机必须走这个接口。</summary>
public interface IRandom
{
    /// <summary>返回 [0, maxExclusive) 之间的整数。</summary>
    int Next(int maxExclusive);

    /// <summary>返回 [0.0, 1.0) 之间的浮点数。</summary>
    double NextDouble();

    /// <summary>当前内部状态，用于存档。</summary>
    int GetState();

    /// <summary>恢复内部状态，用于读档。</summary>
    void SetState(int state);
}

/// <summary>结算器注册表。</summary>
public interface IResolverRegistry
{
    void Register(ActionType action, IResolver resolver);

    /// <summary>查找结算器。未注册时返回 null，不要抛异常。</summary>
    IResolver? Get(ActionType action);

    /// <summary>分发指令。未注册时返回 RejectedNotImplemented。</summary>
    ResolveResult Dispatch(WorldState state, Order order);
}

/// <summary>状态写回器。把 StateDelta 统一应用到 WorldState。</summary>
public interface IStateApplier
{
    void Apply(WorldState state, List<StateDelta> deltas);
}

/// <summary>指令处理器。</summary>
public interface ICommandProcessor
{
    /// <summary>处理单条指令。不会修改 state（由 Applier 在外部统一写回）。</summary>
    ResolveResult Process(WorldState state, Order order);

    /// <summary>批量处理。返回所有结果，顺序与传入一致。</summary>
    List<ResolveResult> ProcessAll(WorldState state, List<Order> orders);
}

/// <summary>回合推进器。</summary>
public interface ITurnPipeline
{
    /// <summary>推进一个月并返回结果。</summary>
    TurnResult RunMonth(WorldState state);
}

/// <summary>回合结果。</summary>
public sealed class TurnResult
{
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>本月所有结算结果。</summary>
    public List<ResolveResult> Results { get; set; } = new();

    /// <summary>本月发生的事件。</summary>
    public List<EventRecord> Events { get; set; } = new();

    /// <summary>AI 本月产出的指令，供下回合第7 步执行。</summary>
    public List<Order> NextMonthOrders { get; set; } = new();

    public bool GameOver { get; set; }
    public string? GameOverReason { get; set; }

    /// <summary>回合结束后已应用变更的状态快照，直接给 UI 用。</summary>
    public WorldState Snapshot { get; set; } = new();
}

// ============================================================================
//  智能体层接口（由 04 包实现）
// ============================================================================

/// <summary>
/// 势力决策器。
///
/// 【硬性约束】铁律：只能产出 Order，禁止修改 WorldState。
/// 审查时看到本类中任何对 state 的写入调用，一律打回。
/// </summary>
public interface IFactionPlanner
{
    /// <summary>为指定势力规划下月指令。返回空列表表示本月无动作。</summary>
    List<Order> Plan(WorldState state, int factionId);

    /// <summary>为所有非玩家势力规划。</summary>
    List<Order> PlanAll(WorldState state);
}

/// <summary>人才政策——决定"用谁"。这是本项目最核心的特色接口。</summary>
public interface IPersonnelPolicy
{
    /// <summary>为指定岗位选出最佳人才。找不到返回 null。</summary>
    Officer? SelectBestFor(WorldState state, int factionId, PostType post);

    /// <summary>选出最佳将领。</summary>
    Officer? SelectBestGeneralFor(WorldState state, int factionId);

    /// <summary>检查忠诚度，返回叛逃等事件。不得直接修改 state。</summary>
    List<EventRecord> CheckLoyalty(WorldState state, int factionId);
}

// ============================================================================
//  配置接口（由 02 包实现）
// ============================================================================

/// <summary>全局平衡参数。所有数值必须来自这里，禁止硬编码在规则层。</summary>
public sealed class BalanceConfig
{
    public int GrainPerPopPerMonth { get; set; }
    public int SoldierPerPopPerMonth { get; set; }
    public int MoraleDecayPerMonth { get; set; }
    public int StabilityDecayPerMonth { get; set; }
    public int BaseRecruitCostSilver { get; set; }
    public int BaseRecruitCostGrain { get; set; }
    public int BaseFortifyCostSilver { get; set; }
    public int BaseDevelopCostSilver { get; set; }
    public int LoyaltyThreshold { get; set; }
    public int LoyaltyDecayPerMonth { get; set; }

    /// <summary>人才到岗需要的月数。默认 1。</summary>
    public int PostTakeEffectMonths { get; set; } = 1;

    /// <summary>单一势力每月可执行的指令总数上限。</summary>
    public int MaxOrdersPerFactionPerMonth { get; set; } = 5;

    /// <summary>专长加成。键为 "Specialty:Post" 或 "Specialty:Any"，值为百分比。</summary>
    public Dictionary<string, int> SpecialtyBonus { get; set; } = new();

    // ================================================================
    //  战斗系统系数 —— 来源：01_设计规格层/3_战斗系统设计.md
    //  必须走配置，禁止在代码中硬编码。缺任何一项都视为配置校验失败。
    // ================================================================

    /// <summary>攻城战基础战损率。基准 0.35（百分数 35）。</summary>
    public int BaseLoss { get; set; }

    /// <summary>守方损失系数。守方有城墙保护，损失低于攻方。基准 0.6。</summary>
    public int DefenderLossFactor { get; set; }

    /// <summary>攻陷阈值。lossRate 超过此值判为击退。基准 70（百分数）。</summary>
    public int SiegeThresholdLoss { get; set; }

    /// <summary>野战损失系数。野战无城墙消耗，损失低于攻城。基准 0.6。</summary>
    public int FieldBattleLossFactor { get; set; }

    /// <summary>未决状态双方各自损失比例。基准 10（百分数）。</summary>
    public int StalemateLossRate { get; set; }

    /// <summary>装备兵每月耗粮。基准 20（耗粮 = 兵力 × 本值 / 1000）。</summary>
    public int ArmyMonthlyGrainPerSoldier { get; set; }

    /// <summary>战败时城池易主，守方兵力清零比例。基准 100（百分数）。</summary>
    public int SiegeDefeatRoutRate { get; set; }
}

/// <summary>人才初始模板。</summary>
public sealed class OfficerTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Command { get; set; }
    public int Governance { get; set; }
    public int Loyalty { get; set; }
    public int Age { get; set; }
    public OfficerSpecialty Specialty { get; set; }
}

/// <summary>城池初始模板。</summary>
public sealed class CityTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int FactionId { get; set; }
    public int Population { get; set; }
    public TerrainType Terrain { get; set; }
    public bool IsCapital { get; set; }
    public List<int> AdjacentCityIds { get; set; } = new();
}

/// <summary>事件模板。</summary>
public sealed class GameEventTemplate
{
    public string Id { get; set; } = string.Empty;
    public EventCategory Category { get; set; }
    /// <summary>触发概率 0-100。同一 Category 的概率之和不得超过 60。</summary>
    public int Probability { get; set; }
    /// <summary>面向玩家的中文文本。</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>效果参数，键值对。由 03 包的 Resolver 消费。</summary>
    public Dictionary<string, int> Effects { get; set; } = new();
}

public interface IConfigLoader
{
    BalanceConfig LoadBalance();
    List<OfficerTemplate> LoadOfficers();
    List<CityTemplate> LoadCities();
    List<GameEventTemplate> LoadEvents();
}

// ============================================================================
//  命名系统契约
// ============================================================================
//  【为什么进契约】名字必须可复现：同一 Seed 生成同一批名字，否则存档读档后
//  名字会变，批量模拟也无法复现。这和规则层的确定性要求是同一件事。
// ============================================================================

/// <summary>
/// 名字生成器。必须注入 IRandom，禁止使用 Guid /系统时间 / Random.Shared。
/// </summary>
public interface INameGenerator
{
    /// <summary>生成势力名。</summary>
    string GenerateFactionName(int factionId);

    /// <summary>生成城池名。</summary>
    string GenerateCityName(int cityId);

    /// <summary>生成人才全名。</summary>
    string GenerateOfficerName(int officerId);

    /// <summary>按专长生成称号，如「农圣」「守城者」。用于 UI 展示人才特质。</summary>
    string GenerateSpecialtyTitle(OfficerSpecialty specialty);
}

/// <summary>
/// 玩家命名校验。命名规则见 01_设计规格层/命名系统/0_命名系统设计.md。
/// </summary>
public static class NamingRules
{
    public const int MinLength = 2;
    public const int MaxLength = 8;

    /// <summary>校验玩家输入的名字。返回错误原因；合法时返回 null。</summary>
    public static string? ValidatePlayerName(string? name, string? existingNames = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "名字不能为空";

        if (name.Length < MinLength || name.Length > MaxLength)
            return $"名字长度需在 {MinLength}-{MaxLength} 个字符之间";

        if (!name.All(IsAllowedChar))
            return "名字只能包含中文汉字";

        if (existingNames != null && existingNames.Contains(name))
            return "这个名字已被使用";

        return null;
    }

    /// <summary>允许的字符：中文字符与常见汉字范围。</summary>
    private static bool IsAllowedChar(char c) => c >= 0x4E00 && c <= 0x9FFF;
}

/// <summary>玩家可命名的对象范围。城池与 AI 势力不可命名，保持世界可信度。</summary>
public static class NamingScope
{
    /// <summary>玩家可命名自己的势力。</summary>
    public const bool PlayerFaction = true;

    /// <summary>玩家可命名自己的君主角色。</summary>
    public const bool PlayerCharacter = true;

    /// <summary>玩家可命名自己招揽到的人才。</summary>
    public const bool RecruitedOfficers = true;

    /// <summary>城池不可命名——城池是地理实体，不该由玩家随意起名。</summary>
    public const bool Cities = false;

    /// <summary>AI 势力不可命名。</summary>
    public const bool AiFactions = false;
}

// ============================================================================
//  线格式规范 —— 前后端通信的唯一标准
// ============================================================================
//  05A（TypeScript）不引用本工程，而是按本规范对齐 JSON。
//  任何字段的新增/改名/删除，必须先改这里再改代码。
// ============================================================================

/// <summary>JSON 序列化配置。所有跨进程传输必须使用此配置。</summary>
public static class WireFormat
{
    /// <summary>统一的序列化选项。命名策略、枚举处理都已固定。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        // 字段名用 camelCase：C# 的 Defense → JSON 的 defense
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // 枚举序列化为字符串而非数字。
        // 理由：数字枚举一旦顺序调整，跨语言端会静默读到错误的值，且难以排查。
        // 字符串枚举即使顺序变了也不会读错。
        Converters = { new JsonStringEnumConverter() },

        // 遇到未声明的字段时忽略，而不是报错。
        // 理由：后端新增字段不应导致旧版前端崩溃。
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,

        WriteIndented = false,
    };

    /// <summary>文件名约定。05A 侧解析这些文件名。</summary>
    public static class FileNames
    {
        public const string Balance = "config.json";
        public const string Officers = "officers.json";
        public const string Cities = "cities.json";
        public const string Events = "events.json";
    }
}

/// <summary>
/// API 通信契约（03 供 05A 调用）。
/// 05A 侧按此定义 TypeScript 类型。签名与路径都是契约。
/// </summary>
public static class ApiContract
{
    // ===== 请求/响应封装 =====
    // 成功：{ "ok": true, "data": ... }
    // 失败：{ "ok": false, "error": { "code": "...", "message": "..." } }

    /// <summary>基础路径前缀。</summary>
    public const string BasePath = "/api";

    public static class Endpoints
    {
        /// <summary>GET 获取完整世界状态快照。</summary>
        public const string GetState = BasePath + "/state";

        /// <summary>POST 下达指令。请求体为 Order 的 JSON（camelCase）。</summary>
        public const string SubmitOrder = BasePath + "/order";

        /// <summary>POST 推进一个月。请求体为空。返回 TurnResult。</summary>
        public const string AdvanceMonth = BasePath + "/turn/advance";

        /// <summary>POST 创建新游戏。请求体为 { "seed": number }。</summary>
        public const string NewGame = BasePath + "/game/new";

        /// <summary>POST 保存。请求体为空，返回存档 JSON。</summary>
        public const string Save = BasePath + "/game/save";

        /// <summary>POST 读档。请求体为存档 JSON。</summary>
        public const string Load = BasePath + "/game/load";
    }

    /// <summary>标准错误码。05A 必须按此展示用户可读提示，不得自行翻译码值。</summary>
    public static class ErrorCodes
    {
        public const string InvalidActor = "INVALID_ACTOR";
        public const string NoResources = "NO_RESOURCES";
        public const string InvalidTarget = "INVALID_TARGET";
        public const string TargetOccupied = "TARGET_OCCUPIED";
        public const string NoPost = "NO_POST";
        public const string NotAdjacent = "NOT_ADJACENT";
        public const string Cooldown = "COOLDOWN";
        public const string NotImplemented = "NOT_IMPLEMENTED";
        public const string InsufficientCapacity = "INSUFFICIENT_CAPACITY";

        /// <summary>前端未预期的错误。发生时属于 Bug。</summary>
        public const string Internal = "INTERNAL_ERROR";

        /// <summary>网络/服务不可用。05A 必须实现重试与降级提示。</summary>
        public const string NetworkUnavailable = "NETWORK_UNAVAILABLE";
    }
}

/// <summary>API 错误响应体。</summary>
public sealed class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>补充信息，如"需要银 500，现有 300"。可为空。</summary>
    public string? Detail { get; set; }
}

/// <summary>标准响应封装。</summary>
public sealed class ApiResponse
{
    public bool Ok { get; set; }
    public System.Text.Json.Nodes.JsonNode? Data { get; set; }
    public ApiError? Error { get; set; }
}

// ============================================================================
//  文件归属表 —— 解决冲突
// ============================================================================

/// <summary>
/// 每个文件有唯一 owner 包。其他包只读不改。
/// 违反此表导致的合并冲突，一律由 owner 包裁决。
/// </summary>
public static class FileOwnership
{
    /// <summary>契约定义文件。owner = 00项目管理，任何人只读。</summary>
    public const string ContractsDir = "StrategyGame.Contracts/";

    /// <summary>owner = 03 规则内核，其他包只读。</summary>
    public const string WorldStateFile = "StrategyGame.Core/src/WorldState.cs";

    public const string OrderFile = "StrategyGame.Core/src/Order.cs";
    public const string ResolversDir = "StrategyGame.Core/src/Resolvers/";
    public const string TurnPipelineFile = "StrategyGame.Core/src/TurnPipeline.cs";
    public const string ConfigDir = "StrategyGame.Core/src/Config/";

    /// <summary>owner = 04 智能体，其他包只读。</summary>
    public const string AiDir = "StrategyGame.AI/src/";

    /// <summary>owner = 02 内容数据，其他包只读。</summary>
    public const string DataDir = "assets/data/";

    /// <summary>owner = 06 工程验证，其他包只读。</summary>
    public const string ToolsDir = "StrategyGame.Tools/";

    /// <summary>owner = 05A 表现层，其他包只读。</summary>
    public const string UiDir = "StrategyGame.UI/";
}