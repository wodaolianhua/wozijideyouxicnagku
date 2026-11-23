// ============================================================================
//  占位数值
// ============================================================================
//  【本文件存在的唯一理由】
//  BalanceConfig 已定义的系数一律从配置读取，不在此重复。
//  但结构性的除数基数、暂时无法归入配置的比例常量，
//  集中放在这里，便于数值定稿后一次性替换。
//
//  【纪律】
//  1. 本文件不得出现任何「玩法平衡数值」（产量、价格、伤害等），
//     那些必须来自 BalanceConfig。
//  2. 每一条都必须带 TODO(设计) 标记，指明定稿后如何处理。
//  3. 新增条目必须同时说明数据来源，方便 01/02 包接手。
// ============================================================================

namespace StrategyGame.Core;

/// <summary>
/// 结构性的占位常量。全部为「换算基数」与「暂无法归入配置的修正」。
/// </summary>
/// <remarks>
/// 这不是数值表。真正的数值表在 <c>02_内容数据层</c>，由
/// <see cref="StrategyGame.Contracts.BalanceConfig"/> 承载。
/// </remarks>
public static class PlaceholderNumbers
{
    /// <summary>
    /// 百分比基数。所有百分比类计算的分母。
    /// </summary>
    /// <remarks>
    /// 【是否会变】不会。这是换算约定而非玩法数值，100 表示 100%。
    /// </remarks>
    public const int PercentBase = 100;

    /// <summary>
    /// 每点政务折算的加成（万分比）。基准 50 = 每点政务 +0.5%。
    /// </summary>
    /// <remarks>
    /// TODO(数值): 待 06 模拟调参后回收为正式值。数据来源：岗位取才裁决公式
    /// <c>G × officer.Governance</c> 中的系数 G，本轮按裁决基准 50 落地。
    /// </remarks>
    public const int GovernancePointToBonusPerMille = 50;

    /// <summary>
    /// 每级屯田等级提供的粮产加成（万分比）。基准 1500 = 每级 +15%。
    /// </summary>
    /// <remarks>
    /// TODO(数值): 待 06 模拟调参后回收为正式值。数据来源：4_数值框架.md「粮产」节
    /// <c>屯田等级加成 = 10000 + FarmLevel × 1500</c>。
    /// </remarks>
    public const int FarmLevelBonusPerMille = 1500;

    /// <summary>
    /// 征兵时每名士兵消耗的粮（占 BaseRecruitCostGrain 的比例基数之外的附加）。
    /// </summary>
    /// <remarks>
    /// TODO(设计): 当前征兵总消耗 = BaseRecruitCostGrain + 本值 × 数量。
    /// 定稿后应直接由 BalanceConfig.BaseRecruitCostGrain 表达总消耗，本常量删除。
    /// </remarks>
    public const int GrainPerRecruitedSoldier = 2;

    /// <summary>
    /// 修城防时，每投入 1 点银转化为多少点城防（城防/银）。
    /// </summary>
    /// <remarks>
    /// TODO(设计): 待数值框架定稿后替换为 BalanceConfig 中的转化率字段。
    /// </remarks>
    public const int DefensePerSilver = 1;

    /// <summary>
    /// 屯田时，每投入 1 点银转化为多少点屯田度（屯田度/银）。
    /// </summary>
    /// <remarks>
    /// TODO(设计): 待数值框架定稿后替换。
    /// </remarks>
    public const int FarmPerSilver = 1;

    /// <summary>
    /// 出兵时每名士兵的士气消耗（防止无限连续出兵）。
    /// </summary>
    /// <remarks>
    /// TODO(设计): 待战斗系统定稿后替换。
    /// </remarks>
    public const int MoraleCostPerDispatchedSoldier = 1;

    /// <summary>
    /// 稳定度过低时势力民心每月的额外流失（绝对点数）。
    /// </summary>
    /// <remarks>
    /// 现用途：低稳定度民心流失（EconomySystem.ResolveMoraleAndStability，TurnPipelineTests 已锁定）。
    /// 05B 实时战斗若需士气流失模型，<b>新增独立配置项，禁止挪用本项</b>。
    /// </remarks>
    public const int UnstableMoraleLoss = 2;

    /// <summary>
    /// 玩家势力仅剩此数量城池时判定为胜利。
    /// </summary>
    /// <remarks>
    /// 与胜负判定规则一致：玩家仅剩一城即达成胜利目标。
    /// </remarks>
    public const int VictoryCityThreshold = 1;

    /// <summary>
    /// 饥荒事件导致的民心损失。
    /// </summary>
    /// <remarks>
    /// TODO(设计): 待经济系统定稿后替换。当前为固定扣减值，
    /// 定稿后应按势力规模与稳定度动态计算。
    /// </remarks>
    public const int FamineMoraleLoss = 10;


    // ========================================================================
    //  建筑升级（v1.7 新增）
    //  TODO(设计): 待数值框架定稿后整体搬进 BalanceConfig。
    //  当前放这里的原因：BalanceConfig 是契约类型，新增字段需要走分发，
    //  而建筑成本还会随模拟调参频繁变动，先集中在规则层便于一次性回收。
    // ========================================================================

    /// <summary>升一级农田的银花费。</summary>
    public const int BuildCostFarm = 150;

    /// <summary>升一级兵营的银花费。</summary>
    public const int BuildCostBarracks = 180;

    /// <summary>升一级城墙的银花费。</summary>
    public const int BuildCostFortify = 200;

    /// <summary>升一级市集的银花费。</summary>
    public const int BuildCostMarket = 160;

    /// <summary>升一级农田所需月数。</summary>
    public const int BuildMonthsFarm = 1;

    /// <summary>升一级兵营所需月数。</summary>
    public const int BuildMonthsBarracks = 1;

    /// <summary>升一级城墙所需月数。城墙更费工，两个月。</summary>
    public const int BuildMonthsFortify = 2;

    /// <summary>升一级市集所需月数。</summary>
    public const int BuildMonthsMarket = 1;

    /// <summary>招募一名人才的基础银花费。</summary>
    public const int RecruitOfficerCost = 200;

    /// <summary>一个势力的人才上限。P0 不做解雇，放宽到 10 避免招满卡死。</summary>
    public const int MaxOfficersPerFaction = 10;

    /// <summary>市集每级提供的银收入加成（万分比）。1500 = 每级 +15%。</summary>
    public const int MarketLevelBonusPerMille = 1500;

    /// <summary>兵营每级提供的兵源加成（万分比）。1500 = 每级 +15%。</summary>
    public const int BarracksLevelBonusPerMille = 1500;

    // ========================================================================
    //  兵力上限与稳定度回弹（v1.8 新增）
    //  TODO(设计): 数值定稿后搬进 BalanceConfig。
    // ========================================================================

    /// <summary>
    /// 每点人口可养驻军的上限（倍数）。2 = 3000 人口的城最多屯 6000 兵。
    /// </summary>
    /// <remarks>
    /// 【为什么必须有上限】v1.7 的无头模拟里，60 个月后单支部队涨到 48 万，
    /// 界面上的兵力数字失去意义，而且「集中兵力」这个唯一正确的打法变得毫无价值
    /// ——反正每支部队都吃不完。上限让「把两座城的兵汇到一处再攻」成为真需求。
    /// </remarks>
    public const int SoldierCapPerPopulation = 2;

    /// <summary>
    /// 稳定度回弹基准线。低于此值每月回升，高于此值每月按
    /// <c>StabilityDecayPerMonth</c> 比例下滑。
    /// </summary>
    /// <remarks>
    /// 【v1.8 修复·全局倒计时】原实现只有衰减没有回升，稳定度按每月 −5% 一路归零，
    /// 第 45 个月起全势力兵源产出为 0，沙盒必然卡死。
    /// 稳定度应当是一个「有拉力的弹簧」，不是一个单向倒计时。
    /// </remarks>
    public const int StabilityBaseline = 60;

    /// <summary>稳定度低于基准线时的每月回升量（绝对点数）。</summary>
    public const int StabilityRecoveryPerMonth = 2;

    /// <summary>丢失一座城池时，失城方稳定度的冲击（绝对点数）。</summary>
    public const int CityLossStabilityShock = 8;

    /// <summary>丢失一座城池时，失城方民心的冲击（绝对点数）。</summary>
    public const int CityLossMoraleShock = 5;

    /// <summary>
    /// 判定势力覆灭时，该势力拥有的城池数量上限（归零即覆灭）。
    /// </summary>
    public const int EliminatedCityThreshold = 0;

    // ========================================================================
    //  长途行军（v1.9 新增）
    //  TODO(设计): 待 02 在 config.json/BalanceConfig 落地下列键后，
    //  本组常量整体删除并改为从配置读取（铁律 5）。
    //  待 02 补的键清单：
    //    march.monthsPerHop              —— 每跳行军月数，落地后替换 MarchMonthsPerHop
    //    march.grainPerSoldierPerHopPerMille —— 每兵每跳粮草消耗（万分比），
    //                                           落地后替换 MarchGrainPerSoldierPerHopPerMille
    // ========================================================================

    /// <summary>
    /// 每跳行军的月数。首版 = 1：一条相邻边走一个月。
    /// </summary>
    /// <remarks>
    /// TODO(设计): 待 02 落地 config 键 march.monthsPerHop。
    /// 数据来源：第 8 轮提示词「行军时间首版 = 跳数 × 每跳月数（每跳月数默认 1）」。
    /// </remarks>
    public const int MarchMonthsPerHop = 1;

    /// <summary>
    /// 长途行军每兵每跳的粮草消耗（万分比）。200 = 0.02 粮/兵/跳，
    /// 与 ArmyMonthlyGrainPerSoldier 基准对齐（行军月耗预付）。
    /// </summary>
    /// <remarks>
    /// TODO(设计): 待 02 落地 config 键 march.grainPerSoldierPerHopPerMille。
    /// 数据来源：第 8 轮提示词「补给消耗随距离递增（首版公式：基础消耗 × 跳数）」，
    /// 基础消耗 = 兵力 × 本系数 / 10000。
    /// 【第8轮口径】仅长途行军（跳数 ≥ 2）收取；相邻出兵维持「行军不损耗」现状，
    /// 避免改动 AI 与既有相邻出兵的行为。
    /// </remarks>
    public const int MarchGrainPerSoldierPerHopPerMille = 200;
}
