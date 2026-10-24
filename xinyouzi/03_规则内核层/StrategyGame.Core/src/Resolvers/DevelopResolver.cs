// ============================================================================
//  屯田结算器
// ============================================================================
//  指令语义：TargetId = 城池 Id，Amount = 投入银量
//
//  【纯函数】只读 state，状态变化只通过 StateDelta 表达。
//  【数值来源】系数取自 BalanceConfig.BaseDevelopCostSilver。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 屯田结算器：投入银两提升城池屯田等级，从而提高农业产出。
/// </summary>
/// <remarks>
/// 校验顺序：
///   1. 执行者合法 → Rejected_InvalidActor
///   2. 城池存在且属于该势力 → Rejected_InvalidTarget
///   3. 投入量为正 → Rejected_InvalidTarget
///   4. 本月人力未超上限 → Rejected_InsufficientCapacity
///   5. 银充足 → Rejected_NoResources
/// </remarks>
public sealed class DevelopResolver : ResolverBase
{
    /// <summary>创建屯田结算器。</summary>
    /// <param name="config">数值配置。所有系数从此读取。</param>
    /// <param name="random">可播种随机源。当前不涉及随机，保留参数以免后续改签名。</param>
    public DevelopResolver(BalanceConfig config, IRandom? random = null)
        : base(config, random)
    {
    }

    /// <summary>固定为 <see cref="ActionType.Develop"/>。</summary>
    public override ActionType Action => ActionType.Develop;

    /// <summary>执行前置校验。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为城池 Id，Amount 为投入银量。</param>
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
        if (city.OwnerFactionId != factionId || order.Amount <= 0)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        var capacity = ValidateCapacity(state);
        if (capacity != ResolveResultCode.Success)
        {
            return capacity;
        }

        return ValidateResources(
            state, order, factionId, CalculateSilverCost(state, order.TargetId, order.Amount), 0);
    }

    /// <summary>执行屯田结算。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为城池 Id，Amount 为投入银量。</param>
    /// <returns>
    /// 成功时产出银扣减与屯田等级提升的 Delta 与一条事件；
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
        var city = state.GetCity(order.TargetId)!;

        int silverCost = CalculateSilverCost(state, order.TargetId, order.Amount);
        int farmGain = CalculateFarmGain(order.Amount);
        int newFarmLevel = city.FarmLevel + farmGain;
        int actualGain = newFarmLevel - city.FarmLevel;

        var result = ResolveResult.Ok()
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionSilver,
                faction.Resources.Silver, faction.Resources.Silver - silverCost)
            .WithDelta(
                DeltaTarget.City, city.Id, DeltaField.CityFarmLevel,
                city.FarmLevel, newFarmLevel)
            .WithDelta(
                DeltaTarget.WorldState, 0, DeltaField.WorldActionsThisMonth,
                state.ActionsThisMonth, state.ActionsThisMonth + 1)
            .WithEvent(
                EventCategory.Economy,
                EventKeys.Develop,
                $"{faction.Name}在{city.Name}推进屯田，投入银 {silverCost}，" +
                $"屯田度 +{actualGain}。",
                factionId,
                state.Year,
                state.Month);

        return result;
    }

    /// <summary>
    /// 计算屯田的银消耗。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="cityId">目标城池 Id。</param>
    /// <param name="amount">投入银量。</param>
    /// <returns>银消耗量。</returns>
    /// <remarks>
    /// 目标城屯田使（<c>FarmOverseer</c>）已生效时，按其政务 + Agriculture 专长
    /// 折算万分比折扣：<c>实付银 = max(0, baseCost × (10000 − 折扣) / 10000)</c>。
    /// </remarks>
    private int CalculateSilverCost(WorldState state, int cityId, int amount)
    {
        int baseCost = (int)((long)Config.BaseDevelopCostSilver * amount / PlaceholderNumbers.PercentBase);
        int discount = GetCityPostDiscount(state, cityId, PostType.FarmOverseer, Config);
        return ApplyDiscount(baseCost, discount);
    }

    /// <summary>
    /// 计算屯田度提升值。
    /// </summary>
    /// <param name="amount">投入银量。</param>
    /// <returns>屯田度提升点数。</returns>
    /// <remarks>
    /// TODO(设计): 待数值框架定稿后替换为 BalanceConfig 中的转化率。
    /// </remarks>
    private static int CalculateFarmGain(int amount)
        => amount * PlaceholderNumbers.FarmPerSilver;

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
                    ? $"无权在敌方城池「{city.Name}」屯田。"
                    : "投入银量必须大于 0。";
            }

            case ResolveResultCode.RejectedInsufficientCapacity:
                return $"本月指令数已达上限（{Config.MaxOrdersPerFactionPerMonth} 条）。";

            case ResolveResultCode.RejectedNoResources:
            {
                if (faction == null)
                {
                    return "资源不足。";
                }

                int silverCost = CalculateSilverCost(state, order.TargetId, order.Amount);
                return RejectNoResources(
                    silverCost, 0, faction.Resources.Silver, faction.Resources.Grain).Message
                    ?? "资源不足。";
            }

            default:
                return "屯田指令被拒绝。";
        }
    }
}
