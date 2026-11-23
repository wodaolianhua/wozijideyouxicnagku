// ============================================================================
//  攻城结算 —— 相邻出兵与长途行军到达共用的唯一战斗逻辑
// ============================================================================
//  【为什么抽出来】第 8 轮长途行军要求「到达即触发现有的攻城结算代码，
//  重构为可被『到达事件』复用，不许复制一份战斗逻辑」。
//  原战斗分支内嵌在 DispatchResolver.Resolve 里，行军到达（MarchSystem）
//  无法调用。抽成静态服务后，两条通路共用同一份公式与 Delta 序列。
//
//  【纯函数】只读 state，状态变化只通过 ResolveResult.Deltas 表达。
//  【不负责】行动力计数、休整标记、亲征标记 —— 这些是「下令」的收尾
//  （DispatchResolver.FinalizeOrder），行军到达没有新指令，因此不在这里做。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Systems;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 攻城战结算服务：一场「攻方部队 vs 目标城守军」的完整结算。
/// </summary>
public static class SiegeSettlement
{
    /// <summary>
    /// 结算一场攻城战并产出完整 Delta 与事件。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="config">数值配置。所有战斗系数从此读取。</param>
    /// <param name="army">攻方部队。只读。</param>
    /// <param name="dispatched">本次投入的兵力（相邻出兵 = order.Amount；行军到达 = 全军）。</param>
    /// <param name="target">目标城。只读。</param>
    /// <param name="retreatCity">被击退时退回的城。只读。</param>
    /// <param name="factionId">攻方势力 Id。</param>
    /// <param name="attackerCommand">攻方统帅值（调用方已按亲征/统帅口径算好）。</param>
    /// <param name="personalCommand">本次进攻是否为玩家亲征（决定是否压制人才专长）。</param>
    /// <returns>战果（攻陷/击退/未决）对应的 Delta 与事件；不包含行动力计数。</returns>
    public static ResolveResult ResolveSiegeBattle(
        WorldState state,
        BalanceConfig config,
        Army army,
        int dispatched,
        City target,
        City retreatCity,
        int factionId,
        int attackerCommand,
        bool personalCommand)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(retreatCity);

        var battleSystem = new BattleSystem(config);
        var morale = new MoraleSystem(config);
        var faction = state.GetFaction(factionId)!;

        // 守方：目标城内的敌方部队。行军中（正路过此城）的部队不算守军。
        var defenders = state.GetArmiesAtCity(target.Id)
            .Where(a => a.OwnerFactionId != factionId && a.MarchTargetCityId < 0)
            .ToList();

        int defenderSoldiers = defenders.Sum(a => a.Soldiers);
        int defenderCommand = ResolveDefenderCommand(state, defenders);

        var battle = battleSystem.Resolve(
            dispatched,
            defenderSoldiers,
            target.Defense,
            attackerCommand,
            defenderCommand,
            isSiege: true,
            attackerHasSiegeSpecialty: HasSpecialty(state, army, OfficerSpecialty.Siege, personalCommand),
            attackerHasCavalrySpecialty: HasSpecialty(state, army, OfficerSpecialty.Cavalry, personalCommand),

            // 【硬约束】低士气倍率必须在与 SiegeThresholdLoss 比较之前乘上，
            // 且不得钳制 lossRate。缺了这一步，低士气只会多烧兵，永远不会改变战果。
            // GetMoralePenalty 的档位与阈值由 4_数值框架.md 5.2 定稿。
            attackerMoralePenaltyTenThousand: army.GetMoralePenalty());

        var result = ResolveResult.Ok();

        // ---- 攻方兵力变化 ----
        // 【重要】最终兵力 = 投入兵力 − 战斗损失。
        // 不能再额外扣一次「投入兵力」——那会让兵力凭空消失。
        int survivingAttackers = Math.Max(0, dispatched - battle.AttackerLoss);
        result.WithDelta(
            DeltaTarget.Army, army.Id, DeltaField.ArmySoldiers,
            army.Soldiers, survivingAttackers);

        // ---- 守方兵力变化 ----
        foreach (var defender in defenders)
        {
            // 守军损失按总损失与守军规模的比例分摊到各部队。
            int defenderLossShare = defenderSoldiers > 0
                ? battle.DefenderLoss * defender.Soldiers / defenderSoldiers
                : 0;

            int newDefenderSoldiers = Math.Max(0, defender.Soldiers - defenderLossShare);

            result.WithDelta(
                DeltaTarget.Army, defender.Id, DeltaField.ArmySoldiers,
                defender.Soldiers, newDefenderSoldiers);

            // ---- 守军参战信号（第 3 轮补丁·修复三，与攻方对称）----
            result.WithDelta(
                DeltaTarget.Army, defender.Id, DeltaField.ArmyHasEngagedThisMonth,
                defender.HasEngagedThisMonth ? 1 : 0, 1);

            result.WithDelta(
                DeltaTarget.Army, defender.Id, DeltaField.ArmyMonthsWithoutBattle,
                defender.MonthsWithoutBattle, 0);
        }

        // ---- 攻方参战信号（契约 v1.4）----
        result.WithDelta(
            DeltaTarget.Army, army.Id, DeltaField.ArmyMonthsWithoutBattle,
            army.MonthsWithoutBattle, 0);

        result.WithDelta(
            DeltaTarget.Army, army.Id, DeltaField.ArmyHasEngagedThisMonth,
            army.HasEngagedThisMonth ? 1 : 0, 1);

        // ---- 战果分支 ----
        switch (battle.Outcome)
        {
            case BattleOutcome.Capture:
            {
                // 攻陷：城池易主，攻方部队进驻。
                result.WithDelta(
                    DeltaTarget.City, target.Id, DeltaField.CityOwnerFactionId,
                    target.OwnerFactionId, factionId);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyPositionCityId,
                    army.PositionCityId, target.Id);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyTargetCityId,
                    army.TargetCityId, -1);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyStance,
                    (int)army.Stance, (int)ArmyStance.Idle);

                // 大胜提振士气（MoraleGainOnCapture）。
                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyMorale,
                    army.Morale, ApplyMoraleDelta(army, morale.CalculateAttackerMoraleDelta(battle.Outcome)));

                // 【v1.8 失城冲击】失城方稳定度 −8、民心 −5。
                int loserId = target.OwnerFactionId;
                var loser = state.GetFaction(loserId);
                if (loser != null)
                {
                    result.WithDelta(
                        DeltaTarget.Faction, loserId, DeltaField.FactionStability,
                        loser.Resources.Stability,
                        Math.Max(0, loser.Resources.Stability - PlaceholderNumbers.CityLossStabilityShock));

                    result.WithDelta(
                        DeltaTarget.Faction, loserId, DeltaField.FactionMorale,
                        loser.Resources.Morale,
                        Math.Max(0, loser.Resources.Morale - PlaceholderNumbers.CityLossMoraleShock));
                }

                result.WithEvent(
                    EventCategory.Military,
                    EventKeys.CityCaptured,
                    $"{faction.Name}攻陷{target.Name}！战损 {battle.AttackerLoss}，" +
                    $"守军损失 {battle.DefenderLoss}。",
                    factionId,
                    state.Year,
                    state.Month);
                break;
            }

            case BattleOutcome.Repelled:
            {
                // 击退：攻方退回出发城（长途行军到达被击退时，退回路径上最近的己方城）。
                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyPositionCityId,
                    army.PositionCityId, retreatCity.Id);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyTargetCityId,
                    army.TargetCityId, -1);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyStance,
                    (int)army.Stance, (int)ArmyStance.Idle);

                // 被击退后士气重挫（MoraleLossOnRepel）。
                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyMorale,
                    army.Morale, ApplyMoraleDelta(army, morale.CalculateAttackerMoraleDelta(battle.Outcome)));

                result.WithEvent(
                    EventCategory.Military,
                    EventKeys.SiegeRepelled,
                    $"{faction.Name}攻{target.Name}受挫，部队退回{retreatCity.Name}，" +
                    $"损失 {battle.AttackerLoss} 人。",
                    factionId,
                    state.Year,
                    state.Month);
                break;
            }

            default:
            {
                // 未决：按正常公式结算双方损失（不再额外扣兵），围城继续，下月可再战。
                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyPositionCityId,
                    army.PositionCityId, target.Id);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyTargetCityId,
                    army.TargetCityId, target.Id);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyStance,
                    (int)army.Stance, (int)ArmyStance.Besieging);

                // 拉锯消耗耐心（MoraleLossOnStalemate）。
                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyMorale,
                    army.Morale, ApplyMoraleDelta(army, morale.CalculateAttackerMoraleDelta(battle.Outcome)));

                result.WithEvent(
                    EventCategory.Military,
                    EventKeys.SiegeStalemate,
                    $"{faction.Name}围攻{target.Name}未决，双方各有损失，下月可再战。",
                    factionId,
                    state.Year,
                    state.Month);
                break;
            }
        }

        // ---- 守方无论战果都减士气（MoraleLossOnDefend）----
        // 目的是防止「龟缩无损」成为零成本策略：即使守住，部队也会受伤。
        foreach (var defender in defenders)
        {
            result.WithDelta(
                DeltaTarget.Army, defender.Id, DeltaField.ArmyMorale,
                defender.Morale,
                ApplyMoraleDelta(defender, morale.CalculateDefenderMoraleDelta()));
        }

        return result;
    }

    /// <summary>
    /// 取守方统帅值。取守军中统帅最高者，无守军则为 0。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="defenders">守城部队列表。只读。</param>
    /// <returns>统帅值 0-100。</returns>
    private static int ResolveDefenderCommand(WorldState state, List<Army> defenders)
    {
        int best = 0;
        foreach (var defender in defenders)
        {
            int command = state.GetEffectiveCommand(defender.Id);
            if (command > best)
            {
                best = command;
            }
        }

        return best;
    }

    /// <summary>
    /// 判断部队统帅是否具备指定专长。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="army">部队。只读。</param>
    /// <param name="specialty">待查专长。</param>
    /// <param name="personalCommand">本次进攻是否为玩家亲征。</param>
    /// <returns>具备该专长返回 true。</returns>
    /// <remarks>
    /// 玩家亲征时统帅位被玩家占据，不叠加任何 Officer 加成（D9），
    /// 因此亲征一律返回 false——即使该部队原本的统帅恰有该专长。
    /// </remarks>
    private static bool HasSpecialty(
        WorldState state,
        Army army,
        OfficerSpecialty specialty,
        bool personalCommand)
    {
        if (personalCommand)
        {
            return false;
        }

        if (state.Player.IsPersonallyCommanding && state.Player.CommandingArmyId == army.Id)
        {
            return false;
        }

        var officer = state.GetOfficer(army.OfficerId);
        return officer != null
               && officer.IsAlive
               && officer.Specialty == specialty;
    }

    /// <summary>
    /// 在原士气上应用增减并夹到合法区间。
    /// </summary>
    /// <param name="army">部队。只读。</param>
    /// <param name="delta">士气增量，可为负。</param>
    /// <returns>调整后的士气，落在 [0, MoraleMax] 内。</returns>
    /// <remarks>
    /// 夹取是必须的：士气归零会触发哗变，若允许负值则「士气 −12」可能
    /// 直接把一支 60 士气的部队打成 −12，哗变判定与倍率判定都会失真。
    /// </remarks>
    private static int ApplyMoraleDelta(Army army, int delta)
        => Math.Clamp(army.Morale + delta, 0, Math.Max(0, army.MoraleMax));
}
