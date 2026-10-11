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

/// <summary>全局平衡参数。所有数值必须来自这里，禁止硬编码在规则层。
///
/// 【刻度铁律】所有"比率/系数"类字段一律是<b>万分比整数</b>（100% = 10000），
/// 禁止小数、禁止百分数（85 vs 8500）。理由：战斗系统全程定点，
/// 经济系统若用浮点会出现"两次结算差 1 粒粮"的漂移，破坏确定性铁律。
/// 消费方公式一律写成 `a * (X / 10000)`，并做整除截断（统一向下取整）。
/// 唯一的例外是 <see cref="ArmyMonthlyGrainPerSoldier"/>（/1000 口径，待裁决）。</summary>
public sealed class BalanceConfig
{
    /// <summary>每人口每月产粮，万分比。800 = 0.08。3000 人口城 = 240 粮/月。</summary>
    public int GrainPerPopPerMonth { get; set; }

    /// <summary>每人口每月产兵源，万分比。5000 = 0.5。3000 人口、Stability 60 = 90 兵/月。</summary>
    public int SoldierPerPopPerMonth { get; set; }

    /// <summary>【v1.2 修正】士气自然恢复量（绝对点数，非比率）。基准 3。
    /// <para><b>关键规则</b>：恢复<b>只在"本月未参战"时生效</b>；参战当月不恢复，
    /// 只结算战斗增减。理由：如果两者同月叠加，「每 2 月一战（未决 -3）+ 恢复 +3」
    /// 会让士气恒定 60 —— <b>连续作战完全无惩罚</b>，最该被惩罚的玩法变成最优解。
    /// 这比"腐化失效"更严重。</para></summary>
    public int MoraleDecayPerMonth { get; set; }

    /// <summary>触发"和平腐化"所需的连续未参战月数。基准 4。
    /// <para>为什么是 4 不是 6：一局只有 60-72 回合，阈值 6 意味着整整一年无战才见效，
    /// 而玩家本来就很少连续 6 月不动兵。阈值 4 让"放着不打"成为真实成本。</para></summary>
    public int MoraleDecayNoBattleThreshold { get; set; }

    /// <summary>连续无战后每月的"和平腐化"扣士气（绝对点数，非比率）。基准 6。
    /// <para><b>必须大于 <see cref="MoraleDecayPerMonth"/></b>，否则净变化 ≥ 0，
    /// 腐化永远不产生有效下滑——这是 v1.2 初稿的真实缺陷，由 03 包执行者代入实算发现。</para>
    /// <para>实算基准（恢复 +3 / 腐化 -6 / 阈值 4）：
    /// 连续 12 月无战，士气 60 → 24，跌破 30（战损 +150% 档），腐化形成真实压力；
    /// 而「每 2 月一战 + 战后轮休」稳定在 57-60，不会自己把自己打崩。</para></summary>
    public int MoraleDecayNoBattlePerMonth { get; set; }

    // ===== 士气战斗变动（绝对点数，非比率）=====
    // 来源：4_数值框架.md 5.4「战斗时士气如何流转」

    /// <summary>攻城攻陷后攻方士气增量。基准 10。</summary>
    public int MoraleGainOnCapture { get; set; }

    /// <summary>攻城/野战被击退后攻方士气减量。基准 12。</summary>
    public int MoraleLossOnRepel { get; set; }

    /// <summary>攻城未决后攻方士气减量。基准 3。</summary>
    public int MoraleLossOnStalemate { get; set; }

    /// <summary>守方无论战果的士气减量（防止"龟缩无损"成为零成本策略）。基准 4。</summary>
    public int MoraleLossOnDefend { get; set; }

    /// <summary>士气归零时哗变，部队解散的兵力比例（万分比）。基准 3000（30%）。</summary>
    public int MutinyRoutRate { get; set; }

    /// <summary>稳定度每月自然衰减，万分比（作用于 Stability 值本身的百分比）。</summary>
    public int StabilityDecayPerMonth { get; set; }

    // 以下为绝对数量（银/粮/点），不是比率，不受万分比刻度约束。
    public int BaseRecruitCostSilver { get; set; }
    public int BaseRecruitCostGrain { get; set; }
    public int BaseFortifyCostSilver { get; set; }
    public int BaseDevelopCostSilver { get; set; }

    /// <summary>忠诚度阈值（绝对点数，非比率）。低于此值人才可能叛离。</summary>
    public int LoyaltyThreshold { get; set; }

    /// <summary>忠诚每月衰减（绝对点数，非比率）。</summary>
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
    //
    //  【v1.2 重要变更】攻方损耗与守方伤亡改为两条独立公式：
    //      lossRate（攻方每投 1 兵自损多少）← BaseLoss
    //      killRate（攻方每投 1 兵能歼多少守军）← BaseKill
    //  原设计只有一个 BaseLoss，导致"守方损失 = 守军 × lossRate × 0.6"
    //  要守军归零需 lossRate ≥ 1.667，而击退阈值只有 0.70，攻陷分支永不可达。
    //  详见 3_战斗系统设计.md 第 3.1 / 3.2 节。
    // ================================================================

    /// <summary>攻城战攻方基础战损率（万分比）。基准 3500（35%）。</summary>
    public int BaseLoss { get; set; }

    /// <summary>攻城战守方基础被歼率（万分比）。攻方每投入 1 点兵力能歼灭的守军比例。基准 2700（27%）。
    /// 与 <see cref="BaseLoss"/> 是<b>两个独立量</b>：前者算攻方自损，后者算守方被歼。
    /// 配BaseLoss=3500 使「3000 攻 1000 守、城防 500、均势统帅」恰好攻陷。</summary>
    public int BaseKill { get; set; }

    /// <summary>[已废弃 v1.2] 守方损失保护系数。守方损失不再按守军规模计算，不再需要此系数。
    /// 城墙保护改由 killRate 公式中的 (2 - defenseFactor) 项承担。
    /// <b>保留字段仅为兼容旧 config.json，执行者应读但不得参与计算；
    /// 若在加载到的配置中 &gt; 0，应输出一次告警提示该字段已失效。</b></summary>
    public int DefenderLossFactor { get; set; }

    /// <summary>攻陷/击退阈值（万分比）。lossRate ≥ 此值判为击退，且必须<b>先判击退再判攻陷</b>。基准 7000（70%）。</summary>
    public int SiegeThresholdLoss { get; set; }

    /// <summary>野战战损乘数（万分比）。lossRate = BaseLoss × 本值/10000 × (2-2×commanderRatio)。基准 5000（0.5）。
    /// <para>野战无城防，defenseFactor = 0，所以 lossRate = 0.35 × 0.5 × (2-2×cr) = 0.175（均势），
    /// 正好是攻城 0.35 的一半——这就是"野战比攻城安全"的来源。</para>
    /// <para>注意野战 killRate 的实算是 0.27 × (2-0) × 2×cr = <b>0.540</b>（均势），
    /// 不是 0.27，因为野战 `(2 - defenseFactor)` 从 1.5 变成 2.0。
    /// 因此野战全歼对方需<b>约 1.85 倍</b>兵力，攻城需约 2.5 倍——野战确实更划算。</para></summary>
    public int FieldBattleLossFactor { get; set; }

    /// <summary>[已废弃 v1.2] 未决状态额外扣兵比例。未决改为按 lossRate / killRate 正常结算，
    /// 不再额外扣一次（原先的"双方各损 10%"是重复计算）。
    /// <b>保留字段仅为兼容旧 config.json，执行者应读但不得参与计算；
    /// 若加载到的配置中 &gt; 0，应输出一次告警提示该字段已失效。</b></summary>
    public int StalemateLossRate { get; set; }

    /// <summary>每兵每月耗粮（万分比）。耗粮 = 兵力 × 本值 / 10000。基准 200= 0.02。
    ///
    /// 【v1.2 变更】原为 /1000 口径，是BalanceConfig 里唯一的例外，现已统一为万分比。
    /// 原文档"5000 兵月耗 100 粮"的说法本身就是错的（0.02 × 5000 = 100没错，
    /// 但据此推出的"348 粮养 3.4 万人"是算术错误，实际 1.74 万人），保留这个错误会误导实现。
    ///
    /// 【重要】耗粮不是主要瓶颈，<b>兵源才是</b>：3000 人口城每月只产 90 兵源
    /// （= 3000 × 5000/10000 × Stability/100），要攒到 5000 兵需约 4 年。
    /// 所以"越打越穷"的压迫感主要来自<b>兵源不足</b>与<b>失城后产出一腰斩</b>，
    /// 而非粮草见底。这一点直接影响 EconomySystem 的实现取舍。</summary>
    public int ArmyMonthlyGrainPerSoldier { get; set; }

    /// <summary>攻陷后守军逃散比例（万分比）。攻陷不等于干净拿下，残余守军逃回邻近城。基准待02 定。</summary>
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

/// <summary>事件模板。
/// 【v1.2 变更】Id 从 string 改为 int。全项目所有实体的 Id 一律为正整数，
/// 理由：① JSON 跨语言（TS 侧）时 string/number 混合类型容易产生隐式转换歧义；
/// ② WireFormat 与 ApiContract 都以整数 ID 为前提；③ 事件表是纯配置，无字符串需求。</summary>
public sealed class GameEventTemplate
{
    /// <summary>事件唯一整数 ID。全局唯一，必须为正整数。</summary>
    public int Id { get; set; }

    public EventCategory Category { get; set; }

    /// <summary>触发概率，万分比（10000 = 100%）。同一 Category 的概率之和不得超过 6000。</summary>
    public int Probability { get; set; }

    /// <summary>面向玩家的中文文本。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>效果参数，键值对。由 03 包的 Resolver 消费。
    /// 键为效果名（如 "grain" / "stability" / "morale"），值为该效果的<b>绝对数量</b>，
    /// 正数增加、负数减少。如果效果含义是比率，统一走万分比并在键名后缀 "_rate"。</summary>
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

        /// <summary>【v1.2 新增】目标城池正被围困，等下个月再试。
        /// 与 <see cref="TargetOccupied"/> 的区别：围困是<b>临时</b>状态，
        /// UI 应显示"该城正在被围，下月可再攻"，而不是"目标不可用"。</summary>
        public const string TargetSieged = "TARGET_SIEGED";

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
    /// <summary>owner = 00项目管理，任何人只读。</summary>
    public const string ManagementDir = "00_项目管理/";

    /// <summary>owner = 00项目管理，各包执行 AI 只读。
    /// 【为什么提示词进包目录】用户不该每次从我这里复制粘贴一段话，
    /// 他会搞不清哪个提示词是给哪个包的。把提示词放进包目录后，
    /// 开对话只需一句话"读 提示词_第N轮.md"，杜绝张冠李戴。
    /// 命名约定：提示词_第N轮返工.md（已开工）/ 提示词_待启动.md（未到时机）。
    /// <para>执行 AI 只能读它，禁止修改——提示词是上游指令，不是自己的产物。</para></summary>
    public const string PromptFilePattern = "提示词_*.md";

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