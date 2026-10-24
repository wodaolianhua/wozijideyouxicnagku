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

    /// <summary>
    /// 判定势力覆灭时，该势力拥有的城池数量上限（归零即覆灭）。
    /// </summary>
    public const int EliminatedCityThreshold = 0;
}
