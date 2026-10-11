// ============================================================================
//  休整结算器
// ============================================================================
//  指令语义：TargetId = 部队 Id
//
//  【纯函数】只读 state，状态变化只通过 StateDelta 表达。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Systems;

namespace StrategyGame.Core.Resolvers;

/// <summary>
    /// 休整结算器：恢复部队士气，标记本月已休整以防反复刷士气。
    /// </summary>
    /// <remarks>
    /// 校验顺序：
    ///   1. 执行者合法 → Rejected_InvalidActor
    ///   2. 部队存在且属于该势力 → Rejected_InvalidTarget
    ///   3. 本月尚未休整过 → Rejected_Cooldown
    ///   4. 本月人力未超上限 → Rejected_InsufficientCapacity
    /// </remarks>
    public sealed class RestResolver : ResolverBase
{
    private readonly MoraleSystem _morale;

    /// <summary>创建休整结算器。</summary>
    /// <param name="config">数值配置。</param>
    /// <param name="random">可播种随机源。当前不涉及随机，保留参数以免后续改签名。</param>
    public RestResolver(BalanceConfig config, IRandom? random = null)
        : base(config, random)
    {
        _morale = new MoraleSystem(config);
    }

    /// <summary>固定为 <see cref="ActionType.Rest"/>。</summary>
    public override ActionType Action => ActionType.Rest;

    /// <summary>执行前置校验。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为部队 Id。</param>
    /// <returns>校验结果码。</returns>
    public override ResolveResultCode Validate(WorldState state, Order order)
    {
        var baseResult = base.Validate(state, order);
        if (baseResult != ResolveResultCode.Success)
        {
            return baseResult;
        }

        var army = state.GetArmy(order.TargetId);
        if (army == null)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        int factionId = state.ResolveActorFactionId(order);
        if (army.OwnerFactionId != factionId)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        // 冷却：防止本月反复休整刷士气，这是本结算器最重要的拒绝分支。
        if (army.HasRestedThisMonth)
        {
            return ResolveResultCode.RejectedCooldown;
        }

        return ValidateCapacity(state);
    }

    /// <summary>执行休整结算。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为部队 Id。</param>
    /// <returns>
    /// 成功时产出品气恢复与本月已休整标记的 Delta 及一条事件；
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
        var army = state.GetArmy(order.TargetId)!;

        int newMorale = _morale.CalculateMoraleAfterRest(army);

        var result = ResolveResult.Ok()
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyMorale,
                army.Morale, newMorale)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyHasRested,
                army.HasRestedThisMonth ? 1 : 0, 1)
            .WithDelta(
                DeltaTarget.WorldState, 0, DeltaField.WorldActionsThisMonth,
                state.ActionsThisMonth, state.ActionsThisMonth + 1)
            .WithEvent(
                EventCategory.Military,
                EventKeys.Rest,
                $"{faction.Name}部队于{(state.GetCity(army.PositionCityId)?.Name ?? "战场")}休整，" +
                $"士气 {army.Morale} → {newMorale}。",
                factionId,
                state.Year,
                state.Month);

        return result;
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
        switch (code)
        {
            case ResolveResultCode.RejectedInvalidActor:
                return "执行者不存在、已阵亡或已叛离，无法下令。";

            case ResolveResultCode.RejectedInvalidTarget:
            {
                var army = state.GetArmy(order.TargetId);
                if (army == null)
                {
                    return $"目标部队不存在（Id={order.TargetId}）。";
                }

                return "无权指挥他势力的部队。";
            }

            case ResolveResultCode.RejectedCooldown:
                return "该部队本月已休整过，需等待下月。";

            case ResolveResultCode.RejectedInsufficientCapacity:
                return $"本月指令数已达上限（{Config.MaxOrdersPerFactionPerMonth} 条）。";

            default:
                return "休整指令被拒绝。";
        }
    }
}
