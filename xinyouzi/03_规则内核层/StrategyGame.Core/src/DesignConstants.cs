// ============================================================================
//  设计常量—— 01 包已定稿但契约未提供配置字段的数值
// ============================================================================
//  【与PlaceholderNumbers 的区别】
//  PlaceholderNumbers = 待定稿的占位值，等 01 包补数值框架。
//  本文件            = 01 包【已经定稿】的数值，但 BalanceConfig 里没有对应字段。
//
//  【为什么不能硬编码在业务代码里】
//  任务书要求「所有数值系数从 BalanceConfig 读取，代码中无硬编码魔法数字」。
//  但胜负阈值、月份上限这类系数契约里确实没有字段，而我又不能改契约
//  （契约是00 包的只读真相来源）。因此集中放在本文件，
//  每条注明来源文档与行号，便于日后 02 包填config.json 时一并迁移。
//
//  【定稿后应做】
//  02 包把这些值写进 config.json、00 包给 BalanceConfig 加字段后，
//  删除本文件并改读配置。届时只需改本文件对应的读取点。
// ============================================================================

namespace StrategyGame.Core;

/// <summary>
/// 已定稿但契约暂无配置字段的设计常量。
/// </summary>
/// <remarks>
/// 来源：<c>01_设计规格层/3_战斗系统设计.md</c> 第五节「胜负判定」、
/// <c>01_设计规格层/0_设计决策记录.md</c> D15。
/// </remarks>
public static class DesignConstants
{
    /// <summary>
    /// 玩家城池占比达到此比例即立即胜利（万分比）。
    /// </summary>
    /// <remarks>
    /// 来源：3_战斗系统设计.md 第五节「玩家拥有大陆 80% 以上城池 → 立即胜利」。
    ///
    /// 【单位必须是万分比】8000 = 80.0%。判定时城市占比也按万分比计算
    /// （<c>playerCityCount * 10000 / totalCities</c>），
    /// 两者单位必须一致。早期版本误用千分比 800，
    /// 会让 33% 的城池占比被判为胜利。
    /// </remarks>
    public const int VictoryCityPerMille = 8000;

    /// <summary>
    /// 玩家城池数≤ 此值时判定失败。
    /// </summary>
    /// <remarks>
    /// 来源：3_战斗系统设计.md 第五节「玩家仅剩 1 座城 → 失败」。
    /// 注意方向：仅剩一城是【失败】而非胜利。原型目标是攻城，
    /// 把城池打光才是赢。
    /// </remarks>
    public const int DefeatCityThreshold = 1;

    /// <summary>
    /// 一局硬上限月数。超过则按城池数排名强制结算。
    /// </summary>
    /// <remarks>
    /// 来源：D15「72 个月为硬上限，超时按城池数结算」。
    /// 及格线为 60 个月（见 <see cref="ParMonths"/>）。
    /// </remarks>
    public const int MaxGameMonths = 72;

    /// <summary>
    /// 及格线月数。仅用于事件提示与统计，不改变结算结果。
    /// </summary>
    /// <remarks>
    /// 来源：4_数值框架.md 第一节「60-72 回合」；
    /// D15「60 回合为及格线」。
    /// </remarks>
    public const int ParMonths = 60;

    /// <summary>
    /// 百分比/千分数换算基数。
    /// </summary>
    /// <remarks>把配置里的百分数（35表示 0.35）换算成整数运算的基数。</remarks>
    public const int PercentBase = 100;

    /// <summary>
    /// 战损率定点运算基数（万分比）。
    /// </summary>
    /// <remarks>
    /// 【为什么用万分之而不是浮点】
    /// 契约要求资源全程整数（浮点累加有误差），且确定性铁律要求
    /// 「相同输入=完全相同结果」。浮点在跨平台上的舍入行为无法保证一致，
    /// 因此战损率用万分比整数表示，全程无浮点。
    /// </remarks>
    public const int TenThousandBase = 10_000;

    /// <summary>
    /// 城防值换算防御因子的除数。
    /// </summary>
    /// <remarks>来源：3_战斗系统设计.md 3.1「defenseFactor = 城防值 / 1000.0」。</remarks>
    public const int DefenseFactorDivisor = 1000;

    /// <summary>
    /// 专长战损减伤系数（万分比）。
    /// </summary>
    /// <remarks>
    /// 来源：3_战斗系统设计.md 3.3——
    /// Siege 专长攻城战 lossRate ×0.85，Cavalry 专长野战 lossRate ×0.85。
    /// 8500 = 85.0%。设计意图：专长要有实际价值但不夸张。
    /// </remarks>
    public const int SpecialtyLossReductionPerMille = 8500;

    /// <summary>
    /// 统帅比值的中性值（万分比）。双方统帅皆0 时使用。
    /// </summary>
    /// <remarks>
    /// 【口径已统一】3_战斗系统设计.md 3.1 与本实现一致采用
    /// <c>commanderRatio = 攻 /(攻 + 守)</c>，<b>不带+1</b>。
    /// 指令字典旧写法 <c>攻/(攻+守+1)</c> 已由 01 包裁决为错误并废弃：
    /// +1 会在双方统帅悬殊时把比值强行拉向 0.5，恰好抹掉强将/弱将的差异。
    ///
    /// 【为什么留这个常量】早期实现用「+1 防除零」的口径，
    /// 现在除零由 <c>BattleSystem.CalculateCommanderRatio</c> 按设计
    /// 取 0.5 中性值处理（见方法说明）。本常量保留作为该中性值的唯一出处，
    /// 避免 0.5 被写死成裸数字。
    /// </remarks>
    public const int NeutralCommanderRatio = 5_000;
}
