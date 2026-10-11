// ============================================================================
//  规则内核层的内部类型与常量
// ============================================================================
//  【重要】本文件不重复声明任何契约类型。
//  ActionType / ActorType / ArmyStance / PostType / ResolveResultCode /
//  DifficultyLevel / TerrainType / OfficerSpecialty / EventCategory
//  全部来自 StrategyGame.Contracts，此处只做 using 与引用。
//
//  本文件只放「契约里没有、但规则层自己需要」的类型：
//    - TurnStep        ：回合 11 步的步骤标识
//    - DeltaTarget     ：StateDelta.TargetType 的合法取值
//    - DeltaField      ：StateDelta.Field 的路径约定
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core;

// ============================================================================
//  回合步骤
// ============================================================================

/// <summary>
/// 回合流水线的 11 个步骤标识。
/// </summary>
/// <remarks>
/// 【铁律】Tick 的步骤顺序固定，不得自行调整。顺序调整会引发连锁 bug。
/// 序号即<see cref="TurnPipeline"/> 中的执行顺序，禁止复用或插入新序号。
/// 若未来需要新增步骤，只能追加到 11 之后，并同步修改设计规格。
/// </remarks>
public enum TurnStep
{
    /// <summary>第 1 步：推进时间（月/年）。</summary>
    AdvanceTime = 1,

    /// <summary>第 2 步：经济结算（粮 = 人口 × 系数 × 人才修正）。</summary>
    Economy = 2,

    /// <summary>第 3 步：民心/稳定，按公式衰减或恢复。</summary>
    MoraleAndStability = 3,

    /// <summary>第 4 步：基建推进（城防等级、屯田度累积）。</summary>
    Infrastructure = 4,

    /// <summary>第 5 步：兵源结算（按人口 × 稳定度生成士兵）。</summary>
    SoldierIncome = 5,

    /// <summary>第 6 步：人物事件（按事件库概率抽取）。</summary>
    OfficerEvents = 6,

    /// <summary>第 7 步：执行本月到期的 PendingOrders。</summary>
    ExecutePendingOrders = 7,

    /// <summary>第 8 步：兵源消耗。Soldier 归零 → 触发饥荒事件。</summary>
    SoldierConsumption = 8,

    /// <summary>第 9 步：AI 决策（通过 IFactionPlanner 接口产出下月指令）。</summary>
    AiDecision = 9,

    /// <summary>第 10 步：胜负判定（玩家仅剩一城 / 全势力覆灭 → GameOver）。</summary>
    VictoryCheck = 10,

    /// <summary>第 11 步：交还决策权，构造并返回 TurnResult。</summary>
    Handoff = 11,
}

/// <summary>
/// 回合流水线的结构性常量。
/// </summary>
public static class TurnStepInfo
{
    /// <summary>
    /// 回合流水线允许的步骤总数。用于自检，防止枚举被误改。
    /// </summary>
    public const int StepCount = 11;
}

// ============================================================================
//  StateDelta 字段约定
// ============================================================================

/// <summary>
/// <see cref="StateDelta.TargetType"/> 的合法取值。
/// </summary>
/// <remarks>
/// 取值必须严格来自契约注释规定集合：Faction / City / Army / Officer / WorldState。
/// 使用常量而非裸字符串，避免拼写错误在运行时才暴露。
/// </remarks>
public static class DeltaTarget
{
    /// <summary>目标为势力。</summary>
    public const string Faction = "Faction";

    /// <summary>目标为城池。</summary>
    public const string City = "City";

    /// <summary>目标为部队。</summary>
    public const string Army = "Army";

    /// <summary>目标为人才。</summary>
    public const string Officer = "Officer";

    /// <summary>目标为世界状态本身。</summary>
    public const string WorldState = "WorldState";
}

/// <summary>
/// <see cref="StateDelta.Field"/> 的路径约定与常用字段名。
/// </summary>
/// <remarks>
/// 【嵌套字段】势力资源是嵌套对象（<c>Faction.Resources.Grain</c>），
/// 因此 Field 使用点号路径：<c>"Resources.Grain"</c>。
/// <see cref="StateApplier"/> 负责解析点号路径并写入真实对象。
/// 顶层字段（如 City 的 "Defense"）不含点号。
/// </remarks>
public static class DeltaField
{
    /// <summary>势力的粮。用法：TargetType=Faction, Field="Resources.Grain"。</summary>
    public const string FactionGrain = "Resources.Grain";

    /// <summary>势力的银。</summary>
    public const string FactionSilver = "Resources.Silver";

    /// <summary>势力的可用兵源。</summary>
    public const string FactionSoldier = "Resources.Soldier";

    /// <summary>势力的民心/士气。</summary>
    public const string FactionMorale = "Resources.Morale";

    /// <summary>势力的稳定度。</summary>
    public const string FactionStability = "Resources.Stability";

    /// <summary>城池的当前城防值。</summary>
    public const string CityDefense = "Defense";

    /// <summary>城池的城防上限。</summary>
    public const string CityMaxDefense = "MaxDefense";

    /// <summary>城池的城防等级。</summary>
    public const string CityFortifyLevel = "FortifyLevel";

    /// <summary>城池的屯田等级。</summary>
    public const string CityFarmLevel = "FarmLevel";

    /// <summary>城池的当前人口。</summary>
    public const string CityPopulation = "Population";

    /// <summary>部队的现有兵力。</summary>
    public const string ArmySoldiers = "Soldiers";

    /// <summary>部队的当前士气。</summary>
    public const string ArmyMorale = "Morale";

    /// <summary>部队的所在城池 Id。</summary>
    public const string ArmyPositionCityId = "PositionCityId";

    /// <summary>部队的移动目的地城池 Id。</summary>
    public const string ArmyTargetCityId = "TargetCityId";

    /// <summary>部队的 stance（枚举值）。</summary>
    public const string ArmyStance = "Stance";

    /// <summary>部队本月是否已休整（0/1）。</summary>
    public const string ArmyHasRested = "HasRestedThisMonth";

    /// <summary>
    /// 部队连续未参战的月数。达到和平腐化阈值后每月额外扣士气。
    /// </summary>
    /// <remarks>
    /// 【必须进快照比对】该字段参与士气结算，漏检会让确定性测试出现假阴性。
    /// 参见 <c>MoraleSystem.ResolveMonthlyMorale</c>。
    /// </remarks>
    public const string ArmyMonthsWithoutBattle = "MonthsWithoutBattle";

    /// <summary>
    /// 部队本月是否已触发哗变（0/1）。防止同一个月内重复解散兵力。
    /// </summary>
    /// <remarks>见 <c>MoraleSystem.ResolveMonthlyMorale</c> 的哗变分支。</remarks>
    public const string ArmyHasMutiniedThisMonth = "HasMutiniedThisMonth";

    /// <summary>部队的统帅 Id，-1 表示无。</summary>
    public const string ArmyOfficerId = "OfficerId";

    /// <summary>城池的归属势力 Id。攻陷时由Dispatch 写入。</summary>
    public const string CityOwnerFactionId = "OwnerFactionId";

    /// <summary>人才的岗位所在城池 Id。</summary>
    public const string OfficerPostCityId = "PostCityId";

    /// <summary>人才的岗位类型（枚举值）。</summary>
    public const string OfficerPost = "Post";

    /// <summary>人才到岗至今的月数。</summary>
    public const string OfficerPostedSinceMonths = "PostedSinceMonths";

    /// <summary>人才的忠诚度。</summary>
    public const string OfficerLoyalty = "Loyalty";

    /// <summary>世界状态的当前年份。</summary>
    public const string WorldYear = "Year";

    /// <summary>世界状态的当前月份。</summary>
    public const string WorldMonth = "Month";

    /// <summary>世界状态的本月已执行指令数。</summary>
    public const string WorldActionsThisMonth = "ActionsThisMonth";

    /// <summary>玩家是否正在亲征（0/1）。</summary>
    public const string WorldPlayerIsCommanding = "Player.IsPersonallyCommanding";

    /// <summary>玩家亲征率领的部队 Id，-1 表示未亲征。</summary>
    public const string WorldPlayerCommandingArmyId = "Player.CommandingArmyId";
}

/// <summary>
/// 事件标识符常量。供 UI 决定图标与配色，必须稳定，不得随意改名。
/// </summary>
public static class EventKeys
{
    /// <summary>征兵完成。</summary>
    public const string Recruit = "ev.recruit";

    /// <summary>修筑城防完成。</summary>
    public const string Fortify = "ev.fortify";

    /// <summary>屯田推进完成。</summary>
    public const string Develop = "ev.develop";

    /// <summary>部队出征。</summary>
    public const string Dispatch = "ev.dispatch";

    /// <summary>人才任命。</summary>
    public const string AssignPost = "ev.assign_post";

    /// <summary>部队休整。</summary>
    public const string Rest = "ev.rest";

    /// <summary>攻城成功，城池易主。</summary>
    public const string CityCaptured = "ev.city_captured";

    /// <summary>攻城受挫，部队被击退。</summary>
    public const string SiegeRepelled = "ev.siege_repelled";

    /// <summary>攻城未决，双方各有损失。</summary>
    public const string SiegeStalemate = "ev.siege_stalemate";

    /// <summary>部队士气归零导致哗变逃散。</summary>
    public const string Mutiny = "ev.mutiny";

    /// <summary>经济结算（月度粮产入账）。</summary>
    public const string EconomyIncome = "ev.economy_income";

    /// <summary>兵源耗尽引发饥荒。</summary>
    public const string Famine = "ev.famine";

    /// <summary>势力被判定覆灭。</summary>
    public const string FactionEliminated = "ev.faction_eliminated";

    /// <summary>玩家达成胜利条件。</summary>
    public const string Victory = "ev.victory";
}
