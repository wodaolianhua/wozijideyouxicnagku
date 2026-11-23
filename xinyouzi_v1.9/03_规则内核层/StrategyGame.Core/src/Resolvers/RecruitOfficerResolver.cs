// ============================================================================
//  招贤结算器
// ============================================================================
//  指令语义：TargetId = 城池 Id，Amount = 花费银（调用方传入，低于 150 直接拒绝）
//
//  【为什么不是「随机生成一个人」而是「从在野池里挑一个」】
//  结算器是纯函数，只能产出 StateDelta；而 StateDelta 的通道只有 int，
//  装不下一个新人的四维、姓名、专长。若在结算器里直接 new Officer 塞进列表，
//  就走通了「Resolver 直接改 state」这条被明令禁止的路，回放与千局模拟会一起失效。
//  改为从数据里预置的在野人才池挑人：招募 = 把某个在野人才的归属改成自己，
//  这是一次普通字段写入，Delta 完全表达得了，随机只体现在「挑中谁」。
//
//  副作用是池子有限（数据里 20 人）。P0 够用：一个势力上限 10 人，
//  池子空了会明确拒绝，而不是静默生成一个空属性的人。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 招贤结算器：花银从在野人才池招一人入幕。
/// </summary>
/// <remarks>
/// 校验顺序：执行者 → 城池存在且属于本势力 → 花费合法 → 人才数未满
/// → 本月未招过 → 在野池非空 → 银够 → 行动力够。
/// </remarks>
public sealed class RecruitOfficerResolver : ResolverBase
{
    /// <summary>创建招贤结算器。</summary>
    /// <param name="config">数值配置。</param>
    /// <param name="random">随机源。用于决定在野池里挑中谁，必须可播种。</param>
    public RecruitOfficerResolver(BalanceConfig config, IRandom? random = null)
        : base(config, random)
    {
    }

    /// <summary>固定为 <see cref="ActionType.RecruitOfficer"/>。</summary>
    public override ActionType Action => ActionType.RecruitOfficer;

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

        if (order.Amount < MinimumCost)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        if (state.GetOfficersOfFaction(factionId).Count >= PlaceholderNumbers.MaxOfficersPerFaction)
        {
            return ResolveResultCode.RejectedInsufficientCapacity;
        }

        int faction = factionId;
        int now = CurrentMonth(state);
        var factionEntity = state.GetFaction(factionId);
        if (factionEntity != null && factionEntity.LastRecruitMonth == now && now != 0)
        {
            return ResolveResultCode.RejectedCooldown;
        }

        if (GetWildPool(state).Count == 0)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        var capacity = ValidateCapacity(state, faction);
        if (capacity != ResolveResultCode.Success)
        {
            return capacity;
        }

        return ValidateResources(state, order, factionId, order.Amount, 0);
    }

    /// <summary>执行结算。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <returns>成功时扣银、把一名在野人才划归本势力、记录招募月份。</returns>
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

        var pool = GetWildPool(state);
        int index = Random == null ? 0 : Random.Next(pool.Count);
        var recruit = pool[index];

        int cost = order.Amount;
        int now = CurrentMonth(state);

        return ResolveResult.Ok()
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionSilver,
                faction.Resources.Silver, faction.Resources.Silver - cost)
            .WithDelta(
                DeltaTarget.Officer, recruit.Id, nameof(Officer.OwnerFactionId),
                recruit.OwnerFactionId, factionId)
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionLastRecruitMonth,
                faction.LastRecruitMonth, now)
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionActionsThisMonth,
                state.GetActionsThisMonth(factionId), state.GetActionsThisMonth(factionId) + 1)
            .WithEvent(
                EventCategory.Personnel,
                EventKeys.RecruitOfficer,
                $"{faction.Name}在{city.Name}招得{recruit.Name}（统率 {recruit.Command}、" +
                $"治理 {recruit.Governance}、智谋 {recruit.Intellect}、魅力 {recruit.Charisma}），" +
                $"耗银 {cost}。",
                factionId,
                state.Year,
                state.Month);
    }

    /// <summary>招贤的最低花费。低于此值视为非法指令，而不是资源不足。</summary>
    public const int MinimumCost = 150;

    /// <summary>取当前可招的在野人才（按 Id 升序，保证同种子挑中同一人）。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>在野人才列表。</returns>
    private static List<Officer> GetWildPool(WorldState state)
        => state.Officers
            .Where(o => o.OwnerFactionId == -1 && o.IsAlive)
            .OrderBy(o => o.Id)
            .ToList();

    /// <summary>把年月折算成绝对月序号，与 <c>TimeUtil.TotalMonths</c> 同口径。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>绝对月序号。</returns>
    private static int CurrentMonth(WorldState state)
        => TimeUtil.TotalMonths(state.Year, state.Month) + 1;

    private string BuildRejectionMessage(WorldState state, Order order, ResolveResultCode code)
    {
        int factionId = state.ResolveActorFactionId(order);
        var faction = state.GetFaction(factionId);
        var city = state.GetCity(order.TargetId);

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
                    return $"只能在自己的城池招贤，「{city.Name}」不是你的。";
                }

                if (order.Amount < MinimumCost)
                {
                    return $"招贤花费至少 {MinimumCost} 银，指令给出的是 {order.Amount}。";
                }

                return "在野已无可用之人。";
            }

            case ResolveResultCode.RejectedInsufficientCapacity:
                return $"帐下已满（{PlaceholderNumbers.MaxOfficersPerFaction} 人，解雇功能后续开放）。";

            case ResolveResultCode.RejectedCooldown:
                return "本月已招过一人，下月再来。";

            case ResolveResultCode.RejectedNoResources:
                return faction == null
                    ? "银不足。"
                    : $"银不足：需要 {order.Amount}，现有 {faction.Resources.Silver}。";

            default:
                return "招贤指令被拒绝。";
        }
    }
}
