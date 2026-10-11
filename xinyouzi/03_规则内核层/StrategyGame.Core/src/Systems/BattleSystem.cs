// ============================================================================
//  战斗结算—— 回合制批量结算
//  来源：01_设计规格层/3_战斗系统设计.md 第 3.1-3.5 节
//
//  【纯函数铁律】本类所有方法都是纯函数：只读 state，产出 StateDelta 与
//  EventRecord，不修改任何字段。写回一律由 StateApplier 负责。
//
//  【全程整数】契约禁止浮点（浮点累加有误差），确定性铁律要求跨平台一致。
//  因此战损率与被歼率均用「万分比」定点表示，全程无浮点运算。
//
//  【v1.2 核心变更：攻守两条独立公式】
//  早期版本用同一个 lossRate 同时算攻方自损与守方伤亡，导致两个致命问题：
//    1. 数学不可达——「守方损失 = 守军 × lossRate × 0.6」要让守军归零需
//       lossRate ≥ 1.667，而击退阈值只有 0.70，攻陷分支永远不可能触发。
//    2. 逻辑倒错——共用一个系数后，强将降低 lossRate 会同时让守方更抗打，
//       推出「强将反而破城更慢」的荒谬结论。
//  现改为 lossRate（攻方自损）与 killRate（打掉守军）两条独立公式，
//  方向相反且都是必须的。详见 3_战斗系统设计.md 3.1 节的对照表。
//
//  【三种结果】击退 / 攻陷 / 未决，判定顺序固定为「先击退，后攻陷」。
//  必须有未决，否则 AI 会死磕同一座城直到打下来或全军覆没。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Systems;

/// <summary>战斗结果类型。</summary>
public enum BattleOutcome
{
    /// <summary>攻陷：战损率未过阈值且守城兵力被打光，城池易主。</summary>
    Capture = 0,

    /// <summary>击退：战损率达到阈值，攻方退回原城。</summary>
    Repelled = 1,

    /// <summary>未决：双方按正常公式结算损失，下月可再战。</summary>
    Stalemate = 2,
}

/// <summary>
/// 一次战斗的完整结算结果。
/// </summary>
/// <param name="Outcome">战斗结果类型。</param>
/// <param name="AttackerLoss">攻方损失兵力。</param>
/// <param name="DefenderLoss">守方损失兵力。恒≤ 守城兵力，不会为负。</param>
/// <param name="LossRateTenThousand">攻方战损率（万分比，便于测试与展示）。</param>
/// <param name="KillRateTenThousand">守方被歼率（万分比）。与 <paramref name="LossRateTenThousand"/> 是两个独立量。</param>
/// <param name="IsSiege">是否攻城战。false 为野战。</param>
public readonly record struct BattleResult(
    BattleOutcome Outcome,
    int AttackerLoss,
    int DefenderLoss,
    int LossRateTenThousand,
    int KillRateTenThousand,
    bool IsSiege);

/// <summary>
/// 战斗结算器。按 3_战斗系统设计.md 3.1-3.5 的公式计算战损与结果。
/// </summary>
/// <remarks>
/// 【系数来源】全部取自 <see cref="BalanceConfig"/>，代码中无硬编码玩法数值。
/// <c>DefenderLossFactor</c> 与 <see cref="BalanceConfig.StalemateLossRate"/>
/// 已于 v1.2 废弃，本类完全不读取——它们留在契约里只为兼容旧 config.json。
/// </remarks>
public sealed class BattleSystem
{
    private readonly BalanceConfig _config;

    /// <summary>创建战斗结算器。</summary>
    /// <param name="config">数值配置。战斗系数必须已由ConfigValidator 校验通过。</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> 为 null 时抛出。</exception>
    public BattleSystem(BalanceConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// 计算统帅对比比值（万分比）。
    /// </summary>
    /// <param name="attackerCommand">攻方统帅值。</param>
    /// <param name="defenderCommand">守方统帅值，未设则为 0。</param>
    /// <returns>比值，万分比。攻方越强则值越大（0-10000）。</returns>
    /// <remarks>
    /// 【口径】全项目统一为 <c>攻方 / (攻方 + 守方)</c>，<b>不带 +1</b>。
    /// 指令字典旧写法 <c>攻/(攻+守+1)</c> 已废弃：+1 会在双方统帅悬殊时把比值
    /// 强行拉向 0.5，恰好抹掉「强将 / 弱将」的差异，是错的。
    ///
    /// 【两边皆0】按设计 3.1 取中性值 0.5（5000）——此时不存在统帅优势，
    /// 攻防都按均势算。若取 0 会让「双方都无将」变成「攻方占尽优势」，
    /// 产生比 +1 更严重的逻辑倒错。
    /// </remarks>
    public static int CalculateCommanderRatio(int attackerCommand, int defenderCommand)
    {
        int attacker = Math.Max(0, attackerCommand);
        int defender = Math.Max(0, defenderCommand);
        int denominator = attacker + defender;

        // 双方皆0：不存在对比优势，取设计规定的中性值 0.5。
        if (denominator <= 0)
        {
            return DesignConstants.TenThousandBase / 2;
        }

        return attacker * DesignConstants.TenThousandBase / denominator;
    }

    /// <summary>
    /// 计算攻方战损率（万分比）：攻方每投入 1 点兵力，自己损失多少。
    /// </summary>
    /// <param name="cityDefense">目标城当前城防值。</param>
    /// <param name="attackerCommand">攻方统帅值。</param>
    /// <param name="defenderCommand">守方统帅值。</param>
    /// <param name="isSiege">true 为攻城战，false 为野战。</param>
    /// <returns>战损率，万分比。3500 表示 35%。</returns>
    /// <remarks>
    /// 公式（3.1 / 3.5，全程整数）：
    /// <code>
    /// 攻城：BaseLoss × (0.5 + defenseFactor) × (2 - 2 × commanderRatio)
    /// 野战：BaseLoss × FieldBattleLossFactor/10000 × (2 - 2 × commanderRatio)
    /// </code>
    /// 野战不乘城防项——没有城墙消耗，这正是「避开坚城、诱敌野战」
    /// 成为可行策略的机制来源。
    /// </remarks>
    public int CalculateLossRateTenThousand(
        int cityDefense,
        int attackerCommand,
        int defenderCommand,
        bool isSiege)
    {
        int commanderRatio = CalculateCommanderRatio(attackerCommand, defenderCommand);

        // (2 - 2 × ratio) = 2 × (1 - ratio)，全程整数。
        int commanderTerm = 2 * (DesignConstants.TenThousandBase - commanderRatio);

        if (!isSiege)
        {
            // 野战：BaseLoss × FieldBattleLossFactor × commanderTerm
            return (int)((long)_config.BaseLoss * _config.FieldBattleLossFactor
                * commanderTerm
                / ((long)DesignConstants.TenThousandBase * DesignConstants.TenThousandBase));
        }

        // defenseFactor = 城防值 / 1000，用万分比表示。
        int defenseFactor = cityDefense * DesignConstants.TenThousandBase
            / DesignConstants.DefenseFactorDivisor;

        // (0.5 + defenseFactor) = 5000 + defenseFactor
        int defenseTerm = (DesignConstants.TenThousandBase / 2) + defenseFactor;

        return (int)((long)_config.BaseLoss * defenseTerm * commanderTerm
            / ((long)DesignConstants.TenThousandBase * DesignConstants.TenThousandBase));
    }

    /// <summary>
    /// 计算守方被歼率（万分比）：攻方每投入 1 点兵力，能打掉多少守军。
    /// </summary>
    /// <param name="cityDefense">目标城当前城防值。</param>
    /// <param name="attackerCommand">攻方统帅值。</param>
    /// <param name="defenderCommand">守方统帅值。</param>
    /// <param name="isSiege">true 为攻城战，false 为野战。</param>
    /// <returns>被歼率，万分比。4012 表示 40.12%。</returns>
    /// <remarks>
    /// 公式（3.1 / 3.5）：
    /// <code>
    /// killRate = BaseKill × (2 - defenseFactor) × (2 × commanderRatio)
    /// </code>
    /// 野战 <c>defenseFactor = 0</c>，故 <c>(2 - defenseFactor) = 2</c>，无需另开分支。
    ///
    /// 【城墙保护在这里】守方保护不再由单独的 0.6 系数承担，
    /// 而由 <c>(2 - defenseFactor)</c> 承担：城防越高，攻方每点兵力换掉的
    /// 守军越少。同时城防还抬高 lossRate。城墙的价值是双向的。
    ///
    /// 【为什么守方损失按攻方投入计】守军规模没丢——它是攻陷判定的右端
    /// （投入 × killRate ≥ 守军）。守军规模决定门槛，守方伤亡决定每次换掉多少，
    /// 两者是同一道除法的分母与分子，<b>不该重复相乘</b>。
    /// </remarks>
    public int CalculateKillRateTenThousand(
        int cityDefense,
        int attackerCommand,
        int defenderCommand,
        bool isSiege)
    {
        int commanderRatio = CalculateCommanderRatio(attackerCommand, defenderCommand);

        // defenseFactor：野战无城防，直接取 0。
        int defenseFactor = isSiege
            ? cityDefense * DesignConstants.TenThousandBase
                / DesignConstants.DefenseFactorDivisor
            : 0;

        // (2 - defenseFactor)，全程整数。
        int defenseTerm = 2 * DesignConstants.TenThousandBase - defenseFactor;

        // (2 × commanderRatio)
        int commanderTerm = 2 * commanderRatio;

        return (int)((long)_config.BaseKill * defenseTerm * commanderTerm
            / ((long)DesignConstants.TenThousandBase * DesignConstants.TenThousandBase));
    }

    /// <summary>
    /// 结算一场战斗。
    /// </summary>
    /// <param name="attackerSoldiers">攻方投入兵力。</param>
    /// <param name="defenderSoldiers">守城兵力。</param>
    /// <param name="cityDefense">目标城城防值。</param>
    /// <param name="attackerCommand">攻方统帅值（亲征时取 Player.Command）。</param>
    /// <param name="defenderCommand">守方统帅值。</param>
    /// <param name="isSiege">true 为攻城战。</param>
    /// <param name="attackerHasSiegeSpecialty">
    /// 攻方统帅是否具备 Siege 专长（攻城战战损减伤）。
    /// </param>
    /// <param name="attackerHasCavalrySpecialty">
    /// 攻方统帅是否具备 Cavalry 专长（野战战损减伤）。
    /// </param>
    /// <param name="attackerMoralePenaltyTenThousand">
    /// 攻方低士气惩罚倍率（万分比），由 <see cref="Army.GetMoralePenalty"/> 提供。
    /// 10000 = 无惩罚，40000 = ×4.0。
    /// </param>
    /// <returns>战斗结算结果。</returns>
    /// <remarks>
    /// 【低士气惩罚的施加位置】必须在与 <c>SiegeThresholdLoss</c> 比较<b>之前</b>
    /// 乘上 lossRate，且<b>不得</b>做 min(x, 1.0) 钳制。一旦钳制，士气倍率最多
    /// 把 lossRate 推到 1.0，永远不会改变「是否 ≥ 0.70」的判定，低士气等于没惩罚。
    /// 见 4_数值框架.md 5.2「关键平衡约束」。
    ///
    /// 【判定顺序不可调换】先击退（lossRate ≥ 阈值），再攻陷（守方损失 ≥ 守军）。
    /// 若先判攻陷，lossRate 已 ≥ 0.70 的强攻在守军恰好被打光时会同时满足
    /// 两条件，顺序不同结果就不同——「打不动就先撤」正是先判击退的目的。
    ///
    /// 【未决】按正常公式结算，<b>不再额外扣兵</b>。
    /// 原实现里「守方损失」与「未决双方各损 10%」重复计算了一次。
    /// </remarks>
    public BattleResult Resolve(
        int attackerSoldiers,
        int defenderSoldiers,
        int cityDefense,
        int attackerCommand,
        int defenderCommand,
        bool isSiege,
        bool attackerHasSiegeSpecialty,
        bool attackerHasCavalrySpecialty,
        int attackerMoralePenaltyTenThousand = DesignConstants.TenThousandBase)
    {
        int lossRate = CalculateLossRateTenThousand(
            cityDefense, attackerCommand, defenderCommand, isSiege);

        int killRate = CalculateKillRateTenThousand(
            cityDefense, attackerCommand, defenderCommand, isSiege);

        // 专长减伤 ×0.85：Siege 用于攻城，Cavalry 用于野战。
        if (isSiege && attackerHasSiegeSpecialty)
        {
            lossRate = ApplySpecialtyReduction(lossRate);
        }
        else if (!isSiege && attackerHasCavalrySpecialty)
        {
            lossRate = ApplySpecialtyReduction(lossRate);
        }

        // ---- 低士气惩罚：乘在阈值比较之前，且不钳制 ----
        lossRate = (int)((long)lossRate * attackerMoralePenaltyTenThousand
            / DesignConstants.TenThousandBase);

        int attackerLoss = ScaleByRate(attackerSoldiers, lossRate, allowOverLoss: true);

        // ---- 守方损失：min(投入 × killRate, 守城兵力) ----
        int defenderLoss = Math.Min(
            ScaleByRate(attackerSoldiers, killRate, allowOverLoss: false),
            defenderSoldiers);

        // ---- 第 1 步：击退判定 ----
        if (lossRate >= _config.SiegeThresholdLoss)
        {
            return new BattleResult(
                BattleOutcome.Repelled, attackerLoss, defenderLoss, lossRate, killRate, isSiege);
        }

        // ---- 第 2 步：攻陷判定 ----
        if (defenderSoldiers <= 0 || defenderLoss >= defenderSoldiers)
        {
            return new BattleResult(
                BattleOutcome.Capture, attackerLoss, defenderLoss, lossRate, killRate, isSiege);
        }

        // ---- 第 3 步：未决，按正常公式结算，不额外扣兵 ----
        return new BattleResult(
            BattleOutcome.Stalemate, attackerLoss, defenderLoss, lossRate, killRate, isSiege);
    }

    /// <summary>
    /// 应用专长减伤。
    /// </summary>
    /// <param name="lossRate">原战损率（万分比）。</param>
    /// <returns>减伤后的战损率（万分比）。</returns>
    /// <remarks>
    /// 来源：3_战斗系统设计.md 3.3——Siege 专长攻城战 ×0.85，
    /// Cavalry 专长野战 ×0.85。系数存于
    /// <see cref="DesignConstants.SpecialtyLossReductionPerMille"/>。
    /// </remarks>
    private static int ApplySpecialtyReduction(int lossRate)
        => (int)((long)lossRate * DesignConstants.SpecialtyLossReductionPerMille
            / DesignConstants.TenThousandBase);

    /// <summary>
    /// 按比率缩放兵力。全程整数，整除截断统一向下。
    /// </summary>
    /// <param name="soldiers">基数兵力。</param>
    /// <param name="rateTenThousand">比率（万分比）。</param>
    /// <param name="allowOverLoss">
    /// true 时允许结果超过基数（用于攻方战损：低士气惩罚可把lossRate 推到 1.0 以上，
    /// 此时攻击者按公式损失全部兵力，而不是被静默截断——截断会让士气惩罚形同虚设）。
    /// false 时结果不超过基数（用于守方被歼，另由调用方按守军规模上限截断）。
    /// </param>
    /// <returns>按比率缩放后的兵力值，最小为 0。</returns>
    private static int ScaleByRate(int soldiers, int rateTenThousand, bool allowOverLoss)
    {
        if (soldiers <= 0)
        {
            return 0;
        }

        long scaled = (long)soldiers * rateTenThousand / DesignConstants.TenThousandBase;

        return allowOverLoss
            ? (int)Math.Clamp(scaled, 0, int.MaxValue)
            : (int)Math.Clamp(scaled, 0, soldiers);
    }
}