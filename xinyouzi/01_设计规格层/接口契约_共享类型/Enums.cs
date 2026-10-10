// ============================================================================
//  StrategyGame.Contracts —— 唯一的接口真相来源
// ============================================================================
//  【这个文件是什么】
//  它是所有包共享的类型定义的真实 .cs 源码。任何人都不需要手抄——
//  直接在本工程里编译，或者通过 ProjectReference 引用本工程。
//
//  【规则】
//  1. 本文件只允许新增，不允许修改或删除已有定义。
//  2. 任何字段名的修改都是破坏性变更，会让所有下游包编译失败。
//  3. 契约变更必须走 00_项目管理 的变更流程。
//
//  【谁依赖这个工程】
//    03 规则内核层  → 引用并实现
//    04 智能体层    → 引用并实现（IFactionPlanner / IPersonnelPolicy）
//    06 工程验证层  → 引用并调用
//    02 内容数据层  → 引用配置类型
//    05A 表现层     → 不直接引用，按 JSON 线格式对齐（见 线格式规范.md）
//    05B 表现层     → 延后
// ============================================================================

namespace StrategyGame.Contracts;

/// <summary>行动类型。骨架阶段必须实现标注 [Proto] 的六种。</summary>
public enum ActionType
{
    // ===== [Proto] 六个必做行动 =====
    /// <summary>[Proto] 征兵：在目标城池补充兵源。</summary>
    Recruit = 0,
    /// <summary>[Proto] 修城防：投入资源提升城池防御。</summary>
    Fortify = 1,
    /// <summary>[Proto] 屯田：投入资源提升城池农业产出。</summary>
    Develop = 2,
    /// <summary>[Proto] 出兵：将领率军移动并可能攻城。</summary>
    Dispatch = 3,
    /// <summary>[Proto] 任命人才：把人才放到对应岗位。</summary>
    AssignPost = 4,
    /// <summary>[Proto] 休整：恢复部队士气。</summary>
    Rest = 5,

    // ===== 人才获取（P1，骨架阶段不实现，保留位）=====
    /// <summary>招揽人才：从市场上招揽在野人才。</summary>
    RecruitOfficer = 100,
    /// <summary>赏赐：消耗资源提升人才忠诚。</summary>
    Reward = 101,

    // ===== 外交与贸易（P2，保留位）=====
    Build = 200,
    Trade = 201,
    FormAlliance = 202,
    Siege = 203,
}

/// <summary>指令执行者类型。</summary>
public enum ActorType
{
    /// <summary>玩家亲自执行。ActorId 恒为 0。</summary>
    Player = 0,
    /// <summary>人才代管。ActorId 为 OfficerId。</summary>
    Officer = 1,
    /// <summary>AI 势力。ActorId 为 FactionId。</summary>
    AiFaction = 2,
}

/// <summary>结算结果码。失败必须显式返回对应码，禁止静默失败。</summary>
public enum ResolveResultCode
{
    Success = 0,

    //===== 拒绝类 =====
    /// <summary>执行者不存在、已阵亡、或已叛离。</summary>
    RejectedInvalidActor = 1,
    /// <summary>资源不足。Message 中给出所需与现有数量。</summary>
    RejectedNoResources = 2,
    /// <summary>目标 Id 不存在。</summary>
    RejectedInvalidTarget = 3,
    /// <summary>目标已被其他部队占据或正在被围。</summary>
    RejectedTargetOccupied = 4,
    /// <summary>该操作要求先任命人才到岗。</summary>
    RejectedNoPost = 5,
    /// <summary>目标不可达（城池不相邻）。</summary>
    RejectedNotAdjacent = 6,
    /// <summary>该指令本月已执行过，需等待冷却。</summary>
    RejectedCooldown = 7,
    /// <summary>该 ActionType 尚未实现。</summary>
    RejectedNotImplemented = 8,
    /// <summary>人力不足：超出本势力本月可执行的指令总数上限。</summary>
    RejectedInsufficientCapacity = 9,

    // ===== 系统异常类 =====
    /// <summary>内部错误。属于Bug，必须记录日志。</summary>
    InternalError = 99,
}

/// <summary>部队状态。</summary>
public enum ArmyStance
{
    Idle = 0,
    Moving = 1,
    Besieging = 2,
    Retreating = 3,
}

/// <summary>岗位类型。人才被任命到某个岗位后，其特质开始生效。</summary>
public enum PostType
{
    None = 0,
    /// <summary>城守：统御城池，产生综合加成。</summary>
    CityGovernor = 1,
    /// <summary>军师佐政：提升政务与城防效率。</summary>
    FortifyOverseer = 2,
    /// <summary>屯田使：提升农业产出。</summary>
    FarmOverseer = 3,
    /// <summary>将军：率领部队，降低战损。</summary>
    General = 4,
    /// <summary>斥候：提升情报与侦察范围。</summary>
    Scoutmaster = 5,
}

/// <summary>人才专长。专长与岗位的匹配决定加成强度。</summary>
public enum OfficerSpecialty
{
    None = 0,
    /// <summary>农业：提升粮产。</summary>
    Agriculture = 1,
    /// <summary>攻城：提升攻城伤害。</summary>
    Siege = 2,
    /// <summary>骑兵：提升部队机动与突袭。</summary>
    Cavalry = 3,
    /// <summary>后勤：降低消耗，提升士气恢复。</summary>
    Logistics = 4,
    /// <summary>外交：提升结盟概率，降低外交成本。</summary>
    Diplomacy = 5,
}

/// <summary>事件类别。</summary>
public enum EventCategory
{
    Economy = 0,
    Military = 1,
    Personnel = 2,
    Disaster = 3,
    Diplomacy = 4,
    Internal = 5,
}

/// <summary>难度档位。</summary>
/// <remarks>
/// 【铁律】难度只影响开局加载的 BalanceConfig，运行中不得改变。
/// 严禁让 AI "看玩家弱就放水"——那会破坏确定性，使无头模拟与回放失效。
/// 难度等级必须存入 WorldState，否则存档与回放无法重现同一难度。
/// </remarks>
public enum DifficultyLevel
{
    /// <summary>简单：AI 资源 ×1.3，决策保守，不主动进攻。</summary>
    Easy = 0,
    /// <summary>普通：基准数值，正常评估威胁。目标玩家胜率 25%-45%。</summary>
    Normal = 1,
    /// <summary>困难：AI 资源 ×0.85，主动扩张，针对玩家弱点。</summary>
    Hard = 2,
}

/// <summary>城市场景标记。影响产出与战损系数，由数值表配置。</summary>
public enum TerrainType
{
    Plain = 0,
    Hill = 1,
    Forest = 2,
    Mountain = 3,
    River = 4,
    Desert = 5,
}