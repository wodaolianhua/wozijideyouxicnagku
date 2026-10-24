// ============================================================================
//  征兵结算器
// ============================================================================
//  指令语义：TargetId = 城池 Id，Amount = 征兵数量
//
//  【纯函数】只读 state，状态变化只通过 StateDelta 表达。
//  【数值来源】全部系数取自 BalanceConfig，不硬编码。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 征兵结算器：在目标城池补充兵源，消耗势力银与粮。
/// </summary>
/// <remarks>
/// 校验顺序（任一失败即拒绝，返回最先命中的码）：
///   1. 执行者合法 → Rejected_InvalidActor
///   2. 城池存在→ Rejected_InvalidTarget
///   3. 城池属于该势力 → Rejected_InvalidTarget
///   4. 征兵数量为正 → Rejected_InvalidTarget
///   5. 本月人力未超上限 → Rejected_InsufficientCapacity
///   6. 银与粮充足 → Rejected_NoResources
/// </remarks>
public sealed class RecruitResolver : ResolverBase
{
    /// <summary>创建征兵结算器。</summary>
    /// <param name="config">数值配置。所有系数从此读取。</param>
    /// <param name="random">
    /// 可播种随机源。征兵当前不涉及随机，但保留参数以便后续战斗/事件
    /// 定稿后接入而不改签名。
    /// </param>
    public RecruitResolver(BalanceConfig config, IRandom? random = null)
        : base(config, random)
    {
    }

    /// <summary>固定为 <see cref="ActionType.Recruit"/>。</summary>
    public override ActionType Action => ActionType.Recruit;

    /// <summary>执行前置校验。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为城池 Id，Amount 为征兵数。</param>
    /// <returns>校验结果码。</returns>
    public override ResolveResultCode Validate(WorldState state, Order order)
    {
        var baseResult = base.Validate(state, order);
        if (baseResult != ResolveResultCode.Success)
        {
            return baseResult;
        }

        var city = state.GetCity(order.TargetId);
        if (city == null)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        int factionId = state.ResolveActorFactionId(order);
        if (city.OwnerFactionId != factionId)
        {
            // 不能在他国城池征兵。这属于目标非法，而非资源问题。
            return ResolveResultCode.RejectedInvalidTarget;
        }

        if (order.Amount <= 0)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        var capacity = ValidateCapacity(state);
        if (capacity != ResolveResultCode.Success)
        {
            return capacity;
        }

        return ValidateResources(
            state,
            order,
            factionId,
            CalculateSilverCost(order.Amount),
            CalculateGrainCost(order.Amount));
    }

    /// <summary>执行征兵结算。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为城池 Id，Amount 为征兵数。</param>
    /// <returns>
    /// 成功时产出势力银/粮扣减与兵源增加的 Delta，以及一条 Economy 类事件；
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

        int silverCost = CalculateSilverCost(order.Amount);
        int grainCost = CalculateGrainCost(order.Amount);
        int recruitCount = order.Amount;

        var result = ResolveResult.Ok()
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionSilver,
                faction.Resources.Silver, faction.Resources.Silver - silverCost)
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionGrain,
                faction.Resources.Grain, faction.Resources.Grain - grainCost)
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionSoldier,
                faction.Resources.Soldier, faction.Resources.Soldier + recruitCount)
            .WithDelta(
                DeltaTarget.WorldState, 0, DeltaField.WorldActionsThisMonth,
                state.ActionsThisMonth, state.ActionsThisMonth + 1)
            .WithEvent(
                EventCategory.Economy,
                EventKeys.Recruit,
                $"{faction.Name}在{state.GetCity(order.TargetId)!.Name}征兵 {recruitCount} 人，" +
                $"耗银 {silverCost}、粮 {grainCost}。",
                factionId,
                state.Year,
                state.Month);

        return result;
    }

    /// <summary>
    /// 计算征兵的银消耗。
    /// </summary>
    /// <param name="amount">征兵数量。</param>
    /// <returns>银消耗量。</returns>
    /// <remarks>
    /// 公式：<c>BaseRecruitCostSilver × 人数 / 100</c>，无政务修正。
    /// 征兵在新岗位模型中无对应政务岗，城守「综合加成」扩展按裁决留给 06，本轮不做。
    /// </remarks>
    private int CalculateSilverCost(int amount)
        => (int)((long)Config.BaseRecruitCostSilver * amount / PlaceholderNumbers.PercentBase);

    /// <summary>
    /// 计算征兵的粮消耗。
    /// </summary>
    /// <param name="amount">征兵数量。</param>
    /// <returns>粮消耗量。</returns>
    /// <remarks>
    /// 公式：BaseRecruitCostGrain × 人数 / 100 + 每兵固定粮耗。
    /// </remarks>
    private int CalculateGrainCost(int amount)
    {
        long baseCost = (long)Config.BaseRecruitCostGrain * amount / PlaceholderNumbers.PercentBase;
        long total = baseCost + (long)PlaceholderNumbers.GrainPerRecruitedSoldier * amount;
        return (int)Math.Min(total, int.MaxValue);
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
        var faction = state.GetFaction(factionId);

        switch (code)
        {
            case ResolveResultCode.RejectedInvalidActor:
                return "执行者不存在、已阵亡或已叛离，无法下令。";

            case ResolveResultCode.RejectedInvalidTarget:
            {
                var city = state.GetCity(order.TargetId);
                if (city == null)
                {
                    return $"目标城池不存在（Id={order.TargetId}）。";
                }

                return city.OwnerFactionId != factionId
                    ? $"无权在敌方城池「{city.Name}」征兵。"
                    : "征兵数量必须大于 0。";
            }

            case ResolveResultCode.RejectedInsufficientCapacity:
                return $"本月指令数已达上限（{Config.MaxOrdersPerFactionPerMonth} 条）。";

            case ResolveResultCode.RejectedNoResources:
            {
                int silverCost = CalculateSilverCost(order.Amount);
                int grainCost = CalculateGrainCost(order.Amount);
                return faction == null
                    ? "资源不足。"
                    : RejectNoResources(
                        silverCost,
                        grainCost,
                        faction.Resources.Silver,
                        faction.Resources.Grain).Message ?? "资源不足。";
            }

            default:
                return "征兵指令被拒绝。";
        }
    }
}
