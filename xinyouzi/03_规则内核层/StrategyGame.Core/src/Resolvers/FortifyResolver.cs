// ============================================================================
//  修城防结算器
// ============================================================================
//  指令语义：TargetId = 城池 Id，Amount = 投入银量
//
//  【纯函数】只读 state，状态变化只通过 StateDelta 表达。
//  【数值来源】系数取自 BalanceConfig.BaseFortifyCostSilver，转换率见占位常量。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 修城防结算器：投入银两提升城池防御值与城防等级。
/// </summary>
/// <remarks>
/// 校验顺序：
///   1. 执行者合法 → Rejected_InvalidActor
///   2. 城池存在且属于该势力 → Rejected_InvalidTarget
///   3. 投入量为正 → Rejected_InvalidTarget
///   4. 本月人力未超上限 → Rejected_InsufficientCapacity
///   5. 银充足 → Rejected_NoResources
/// </remarks>
public sealed class FortifyResolver : ResolverBase
{
    /// <summary>创建修城防结算器。</summary>
    /// <param name="config">数值配置。所有系数从此读取。</param>
    /// <param name="random">可播种随机源。当前不涉及随机，保留参数以免后续改签名。</param>
    public FortifyResolver(BalanceConfig config, IRandom? random = null)
        : base(config, random)
    {
    }

    /// <summary>固定为 <see cref="ActionType.Fortify"/>。</summary>
    public override ActionType Action => ActionType.Fortify;

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
            state, order, factionId, CalculateSilverCost(state, factionId, order.Amount), 0);
    }

    /// <summary>执行修城防结算。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为城池 Id，Amount 为投入银量。</param>
    /// <returns>
    /// 成功时产出银扣减、城防提升、城防等级提升的 Delta 与一条事件；
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

        int silverCost = CalculateSilverCost(state, factionId, order.Amount);
        int defenseGain = CalculateDefenseGain(order.Amount);
        int newDefense = Math.Min(city.MaxDefense, city.Defense + defenseGain);
        int actualGain = newDefense - city.Defense;

        // TODO(设计): 城防等级提升阈值待数值框架定稿。
        // 当前占位规则：每累计投入满 100 银提升 1 级城防等级。
        int newFortifyLevel = city.FortifyLevel + ComputeFortifyLevelGain(order.Amount);

        var result = ResolveResult.Ok()
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionSilver,
                faction.Resources.Silver, faction.Resources.Silver - silverCost)
            .WithDelta(
                DeltaTarget.City, city.Id, DeltaField.CityDefense,
                city.Defense, newDefense);

        if (newFortifyLevel != city.FortifyLevel)
        {
            result.WithDelta(
                DeltaTarget.City, city.Id, DeltaField.CityFortifyLevel,
                city.FortifyLevel, newFortifyLevel);
        }

        result.WithDelta(
                DeltaTarget.WorldState, 0, DeltaField.WorldActionsThisMonth,
                state.ActionsThisMonth, state.ActionsThisMonth + 1)
            .WithEvent(
                EventCategory.Economy,
                EventKeys.Fortify,
                $"{faction.Name}修筑{city.Name}城防，投入银 {silverCost}，" +
                $"城防 +{actualGain}。",
                factionId,
                state.Year,
                state.Month);

        return result;
    }

    /// <summary>
    /// 计算修城防的银消耗。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <param name="amount">投入银量。</param>
    /// <returns>银消耗量。</returns>
    private int CalculateSilverCost(WorldState state, int factionId, int amount)
    {
        int baseCost = (int)((long)Config.BaseFortifyCostSilver * amount / PlaceholderNumbers.PercentBase);
        return Math.Max(0, ScaleInteger(baseCost, GetGovernanceModifier(state, factionId)));
    }

    /// <summary>
    /// 计算城防提升值。
    /// </summary>
    /// <param name="amount">投入银量。</param>
    /// <returns>城防提升点数。</returns>
    /// <remarks>
    /// TODO(设计): 待数值框架定稿后替换为 BalanceConfig 中的转化率。
    /// </remarks>
    private static int CalculateDefenseGain(int amount)
        => amount * PlaceholderNumbers.DefensePerSilver;

    /// <summary>
    /// 计算城防等级提升值。
    /// </summary>
    /// <param name="amount">投入银量。</param>
    /// <returns>城防等级提升点数。</returns>
    /// <remarks>
    /// TODO(设计): 待基建系统定稿后替换。
    /// </remarks>
    private static int ComputeFortifyLevelGain(int amount)
        => amount / PlaceholderNumbers.PercentBase;

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
                    ? $"无权修筑敌方城池「{city.Name}」的城防。"
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

                int silverCost = CalculateSilverCost(state, factionId, order.Amount);
                return RejectNoResources(
                    silverCost, 0, faction.Resources.Silver, faction.Resources.Grain).Message
                    ?? "资源不足。";
            }

            default:
                return "修城防指令被拒绝。";
        }
    }
}
