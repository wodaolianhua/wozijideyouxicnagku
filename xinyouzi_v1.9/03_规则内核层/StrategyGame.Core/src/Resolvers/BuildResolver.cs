// ============================================================================
//  建筑升级结算器
// ============================================================================
//  指令语义：TargetId = 城池 Id，SecondaryTargetId = 建筑类型（0 农田 1 兵营 2 城墙 3 市集）
//
//  【为什么不立刻加等级】升级必须跨月。下令只是「开工」，
//  等级由下个月月初的 BuildingSystem 完工时才 +1。
//  这样玩家的资源投入有真实的等待成本，而不是点了就变强。
//
//  【纯函数】只读 state，状态变化只通过 StateDelta 表达。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Systems;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 建筑升级结算器：扣银、记录工程、开始倒计时。
/// </summary>
/// <remarks>
/// 校验顺序：执行者 → 城池存在 → 属于本势力 → 没有在施工 → 当前等级小于 5
/// → 银够 → 行动力够。任一失败返回最先命中的码。
/// </remarks>
public sealed class BuildResolver : ResolverBase
{
    /// <summary>创建建筑升级结算器。</summary>
    /// <param name="config">数值配置。</param>
    /// <param name="random">随机源。本结算器不使用随机。</param>
    public BuildResolver(BalanceConfig config, IRandom? random = null)
        : base(config, random)
    {
    }

    /// <summary>固定为 <see cref="ActionType.Build"/>。</summary>
    public override ActionType Action => ActionType.Build;

    /// <summary>执行前置校验。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
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
            return ResolveResultCode.RejectedInvalidTarget;
        }

        if (!IsValidBuilding(order.SecondaryTargetId))
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        // 一座城同时只能有一项工程，避免「同时升四样」绕过时间成本。
        if (city.UpgradeMonthsRemaining > 0)
        {
            return ResolveResultCode.RejectedCooldown;
        }

        if (BuildingSystem.GetLevel(city, order.SecondaryTargetId) >= BuildingSystem.MaxLevel)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        var capacity = ValidateCapacity(state, factionId);
        if (capacity != ResolveResultCode.Success)
        {
            return capacity;
        }

        return ValidateResources(
            state,
            order,
            factionId,
            GetSilverCost(order.SecondaryTargetId),
            0);
    }

    /// <summary>执行结算。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <returns>成功时扣银、写入工程状态与行动力；失败时返回中文原因。</returns>
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

        int building = order.SecondaryTargetId;
        int cost = GetSilverCost(building);
        int months = GetMonths(building);

        return ResolveResult.Ok()
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionSilver,
                faction.Resources.Silver, faction.Resources.Silver - cost)
            .WithDelta(
                DeltaTarget.City, city.Id, DeltaField.CityUpgradingBuilding,
                city.UpgradingBuilding, building)
            .WithDelta(
                DeltaTarget.City, city.Id, DeltaField.CityUpgradeMonthsRemaining,
                city.UpgradeMonthsRemaining, months)
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionActionsThisMonth,
                state.GetActionsThisMonth(factionId), state.GetActionsThisMonth(factionId) + 1)
            .WithEvent(
                EventCategory.Economy,
                EventKeys.Build,
                $"{faction.Name}在{city.Name}开工建造{BuildingName(building)}，" +
                $"耗银 {cost}，{months} 个月后完工。",
                factionId,
                state.Year,
                state.Month);
    }

    /// <summary>建筑类型编码是否合法。</summary>
    /// <param name="building">建筑编码。</param>
    /// <returns>0-3 返回 true。</returns>
    private static bool IsValidBuilding(int building)
        => building is >= BuildingSystem.Farm and <= BuildingSystem.Market;

    /// <summary>取建筑中文名。</summary>
    /// <param name="building">建筑编码。</param>
    /// <returns>中文名。</returns>
    private static string BuildingName(int building) => building switch
    {
        BuildingSystem.Farm => "农田",
        BuildingSystem.Barracks => "兵营",
        BuildingSystem.Fortify => "城墙",
        BuildingSystem.Market => "市集",
        _ => "未知建筑",
    };

    private static int GetSilverCost(int building) => building switch
    {
        BuildingSystem.Farm => PlaceholderNumbers.BuildCostFarm,
        BuildingSystem.Barracks => PlaceholderNumbers.BuildCostBarracks,
        BuildingSystem.Fortify => PlaceholderNumbers.BuildCostFortify,
        BuildingSystem.Market => PlaceholderNumbers.BuildCostMarket,
        _ => 0,
    };

    private static int GetMonths(int building) => building switch
    {
        BuildingSystem.Farm => PlaceholderNumbers.BuildMonthsFarm,
        BuildingSystem.Barracks => PlaceholderNumbers.BuildMonthsBarracks,
        BuildingSystem.Fortify => PlaceholderNumbers.BuildMonthsFortify,
        BuildingSystem.Market => PlaceholderNumbers.BuildMonthsMarket,
        _ => 0,
    };

    private string BuildRejectionMessage(WorldState state, Order order, ResolveResultCode code)
    {
        int factionId = state.ResolveActorFactionId(order);
        var faction = state.GetFaction(factionId);
        var city = state.GetCity(order.TargetId);
        int building = order.SecondaryTargetId;

        switch (code)
        {
            case ResolveResultCode.RejectedInvalidActor:
                return "执行者不存在、已阵亡或已叛离，无法下令。";

            case ResolveResultCode.RejectedInvalidTarget:
            {
                if (city == null)
                {
                    return $"目标城池不存在（Id={order.TargetId}）。";
                }

                if (city.OwnerFactionId != factionId)
                {
                    return $"不属于你的城池「{city.Name}」，不能在那里动工。";
                }

                if (!IsValidBuilding(building))
                {
                    return $"建筑类型无效（{building}）。合法值：0 农田、1 兵营、2 城墙、3 市集。";
                }

                return $"{BuildingName(building)}已经是最高等级（{BuildingSystem.MaxLevel} 级）。";
            }

            case ResolveResultCode.RejectedCooldown:
                return city == null
                    ? "该城正在施工，不能同时开第二项工程。"
                    : $"「{city.Name}」正在施工，还剩 {city.UpgradeMonthsRemaining} 个月，完工后才能开新工程。";

            case ResolveResultCode.RejectedInsufficientCapacity:
                return $"本月指令数已达上限（{Config.MaxOrdersPerFactionPerMonth} 条）。";

            case ResolveResultCode.RejectedNoResources:
            {
                int cost = GetSilverCost(building);
                return faction == null
                    ? "银不足。"
                    : $"银不足：需要 {cost}，现有 {faction.Resources.Silver}。";
            }

            default:
                return "建筑升级指令被拒绝。";
        }
    }
}
