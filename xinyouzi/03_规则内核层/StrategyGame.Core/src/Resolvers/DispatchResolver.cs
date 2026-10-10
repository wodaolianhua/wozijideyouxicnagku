// ============================================================================
//  出兵结算器
// ============================================================================
//  指令语义：TargetId = 目标城池 Id，SecondaryTargetId = 出发城 Id，Amount = 兵力
//
//  【纯函数】只读 state，状态变化只通过 StateDelta 表达。
//  【战斗说明】攻城伤害公式属于 01 包战斗系统设计范围，当前只结算「部队移动
//  与进入 besieged 状态」，伤害计算标注 TODO(设计) 待定稿后补入。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 出兵结算器：将领率军移动到相邻城池，敌方城池则进入围城状态。
/// </summary>
/// <remarks>
/// 校验顺序：
///   1. 执行者合法 → Rejected_InvalidActor
///   2. 出发城与目标城均存在 → Rejected_InvalidTarget
///   3. 两城相邻 → Rejected_NotAdjacent
///   4. 出发城属于该势力 → Rejected_InvalidTarget
///   5. 存在可用部队 → Rejected_InvalidTarget
///   6. 目标城池未被敌方占据 → Rejected_TargetOccupied
///   7. 兵力为正且不超过部队现有兵力 → Rejected_NoResources
/// </remarks>
public sealed class DispatchResolver : ResolverBase
{
    /// <summary>创建出兵结算器。</summary>
    /// <param name="config">数值配置。所有系数从此读取。</param>
    /// <param name="random">可播种随机源。攻城结算定稿后将用于战损随机。</param>
    public DispatchResolver(BalanceConfig config, IRandom? random = null)
        : base(config, random)
    {
    }

    /// <summary>固定为 <see cref="ActionType.Dispatch"/>。</summary>
    public override ActionType Action => ActionType.Dispatch;

    /// <summary>执行前置校验。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为目标城，SecondaryTargetId 为出发城，Amount 为兵力。</param>
    /// <returns>校验结果码。</returns>
    public override ResolveResultCode Validate(WorldState state, Order order)
    {
        var baseResult = base.Validate(state, order);
        if (baseResult != ResolveResultCode.Success)
        {
            return baseResult;
        }

        var origin = state.GetCity(order.SecondaryTargetId);
        var target = state.GetCity(order.TargetId);
        if (origin == null || target == null)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        if (!origin.AdjacentCityIds.Contains(order.TargetId))
        {
            // 相邻性以出发城的关系表为准。
            return ResolveResultCode.RejectedNotAdjacent;
        }

        int factionId = state.ResolveActorFactionId(order);
        if (origin.OwnerFactionId != factionId)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        var army = FindDispatchableArmy(state, order, factionId);
        if (army == null)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        // 目标若已有他方部队驻扎，则不可进入。
        bool occupiedByOther = state.GetArmiesAtCity(order.TargetId)
            .Any(a => a.OwnerFactionId != factionId);
        if (occupiedByOther)
        {
            return ResolveResultCode.RejectedTargetOccupied;
        }

        if (order.Amount <= 0 || order.Amount > army.Soldiers)
        {
            return ResolveResultCode.RejectedNoResources;
        }

        return ValidateCapacity(state);
    }

    /// <summary>执行出兵结算。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为目标城，SecondaryTargetId 为出发城，Amount 为兵力。</param>
    /// <returns>
    /// 成功时产出兵力扣减、位置变更、目标锁定与 stance 变更的 Delta 及一条事件；
    /// 失败时返回明确拒绝码与中文原因。
    /// </returns>
    public override ResolveResult Resolve(WorldState state, Order order)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(order);

        var validation = Validate(state, order);
        if (validation != ResolveResultCode.Success)
        {
            return Reject(validation, BuildRejectionMessage(state, order, validation));
        }

        int factionId = state.ResolveActorFactionId(order);
        var faction = state.GetFaction(factionId)!;
        var origin = state.GetCity(order.SecondaryTargetId)!;
        var target = state.GetCity(order.TargetId)!;
        var army = FindDispatchableArmy(state, order, factionId)!;

        int dispatched = order.Amount;
        int newSoldiers = army.Soldiers - dispatched;
        int newMorale = Math.Max(0, army.Morale - dispatched * PlaceholderNumbers.MoraleCostPerDispatchedSoldier);

        // 自家城池 → Moving；敌方城池 → Besieging。攻城伤害待战斗公式定稿。
        var newStance = target.OwnerFactionId == factionId ? ArmyStance.Moving : ArmyStance.Besieging;

        var result = ResolveResult.Ok()
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmySoldiers,
                army.Soldiers, newSoldiers)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyMorale,
                army.Morale, newMorale)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyPositionCityId,
                army.PositionCityId, target.Id)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyTargetCityId,
                army.TargetCityId, target.Id)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyStance,
                (int)army.Stance, (int)newStance)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyHasRested,
                army.HasRestedThisMonth ? 1 : 0, 0)
            .WithDelta(
                DeltaTarget.WorldState, 0, DeltaField.WorldActionsThisMonth,
                state.ActionsThisMonth, state.ActionsThisMonth + 1)
            .WithEvent(
                EventCategory.Military,
                EventKeys.Dispatch,
                newStance == ArmyStance.Besieging
                    ? $"{faction.Name}自{origin.Name}出兵 {dispatched} 人，围困{target.Name}。"
                    : $"{faction.Name}自{origin.Name}调兵 {dispatched} 人，进驻{target.Name}。",
                factionId,
                state.Year,
                state.Month);

        return result;
    }

    /// <summary>
    /// 找出可执行本次出兵的部队。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <returns>符合条件的部队；无则返回 null。</returns>
    /// <remarks>
    /// 优先选择统帅能力最高者，以体现「派名将上」的策略价值。
    /// 排序键为 (统帅值, 部队 Id)，Id 作为次级键保证结果确定——
    /// 遍历顺序不定会让无头模拟产生不可复现的结果。
    /// </remarks>
    private static Army? FindDispatchableArmy(WorldState state, Order order, int factionId)
    {
        Army? best = null;
        int bestCommand = int.MinValue;

        foreach (var army in state.Armies)
        {
            if (army.OwnerFactionId != factionId || army.PositionCityId != order.SecondaryTargetId)
            {
                continue;
            }

            if (army.Soldiers <= 0)
            {
                continue;
            }

            int command = state.GetEffectiveCommand(army.Id);
            if (command > bestCommand)
            {
                bestCommand = command;
                best = army;
            }
        }

        return best;
    }

    /// <summary>
    /// 构造拒绝原因的中文说明。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <param name="code">拒绝码。</param>
    /// <returns>面向玩家的中文说明。</returns>
    private string BuildRejectionMessage(WorldState state, Order order, ResolveResultCode code)
    {
        int factionId = state.ResolveActorFactionId(order);

        switch (code)
        {
            case ResolveResultCode.RejectedInvalidActor:
                return "执行者不存在、已阵亡或已叛离，无法下令。";

            case ResolveResultCode.RejectedInvalidTarget:
            {
                var origin = state.GetCity(order.SecondaryTargetId);
                var target = state.GetCity(order.TargetId);
                if (origin == null || target == null)
                {
                    return "出发城或目标城不存在。";
                }

                if (origin.OwnerFactionId != factionId)
                {
                    return $"无权从敌方城池「{origin.Name}」出兵。";
                }

                return $"{origin.Name}没有可调动的部队。";
            }

            case ResolveResultCode.RejectedNotAdjacent:
            {
                var origin = state.GetCity(order.SecondaryTargetId);
                var target = state.GetCity(order.TargetId);
                return $"「{origin?.Name}」与「{target?.Name}」不相邻，无法出兵。";
            }

            case ResolveResultCode.RejectedTargetOccupied:
            {
                var target = state.GetCity(order.TargetId);
                return $"「{target?.Name}」已被敌方部队占据，无法进入。";
            }

            case ResolveResultCode.RejectedNoResources:
            {
                var army = FindDispatchableArmy(state, order, factionId);
                return army == null
                    ? "无可调动的兵力。"
                    : $"出兵兵力不足（现有 {army.Soldiers}，请求 {order.Amount}）。";
            }

            case ResolveResultCode.RejectedInsufficientCapacity:
                return $"本月指令数已达上限（{Config.MaxOrdersPerFactionPerMonth} 条）。";

            default:
                return "出兵指令被拒绝。";
        }
    }
}
