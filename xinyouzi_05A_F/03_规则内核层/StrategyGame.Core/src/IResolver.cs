// ============================================================================
//  结算器基类—— 六个 Resolver 的公共骨架
// ============================================================================
//  【纯函数铁律的落地方式】
//  基类把「校验」与「产出 Delta」彻底分离：
//    Validate() 只读 state，返回结果码，绝不产出副作用
//    Resolve() 先跑 Validate，通过后才计算并产出 Delta
//  两个方法都不触碰 state 的任何字段。
//
//  【数值来源铁律】
//  所有系数一律从 BalanceConfig 读取。本类不出现任何硬编码玩法数值。
//  需要 BalanceConfig 尚未覆盖的占位系数时，统一放PlaceholderNumbers，
//  并标注 TODO(设计)，等数值框架定稿后替换。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core;

/// <summary>
/// 结算器基类。提供执行者校验、目标校验、资源校验等公共逻辑。
/// </summary>
/// <remarks>
/// 【继承约束】子类实现必须是纯函数：只读 state，状态变化只通过
/// <see cref="ResolveResult.Deltas"/> 表达。禁止使用系统时间、网络、文件 IO；
/// 涉及随机必须使用构造时注入的 <see cref="IRandom"/>，禁止 Random.Shared。
/// </remarks>
public abstract class ResolverBase : IResolver
{
    /// <summary>
    /// 创建结算器。
    /// </summary>
    /// <param name="config">数值配置。所有系数从此读取，禁止硬编码。</param>
    /// <param name="random">可播种随机源。若结算不涉及随机可传 null。</param>
    protected ResolverBase(BalanceConfig config, IRandom? random)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Random = random;
    }

    /// <summary>本结算器处理的行动类型。注册表据此分发。</summary>
    public abstract ActionType Action { get; }

    /// <summary>数值配置。所有系数从此读取。</summary>
    protected BalanceConfig Config { get; }

    /// <summary>可播种随机源。禁止 Random.Shared——那会破坏确定性。</summary>
    protected IRandom? Random { get; }

    /// <summary>
    /// 执行前置校验。
    /// </summary>
    /// <param name="state">世界状态。只读，绝不修改。</param>
    /// <param name="order">待校验指令。只读。</param>
    /// <returns>
    /// 校验通过返回 <see cref="ResolveResultCode.Success"/>；
    /// 否则返回具体的拒绝码。
    /// </returns>
    public virtual ResolveResultCode Validate(WorldState state, Order order)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(order);

        var actorCheck = ValidateActor(state, order);
        if (actorCheck != ResolveResultCode.Success)
        {
            return actorCheck;
        }

        return ResolveResultCode.Success;
    }

    /// <summary>
    /// 执行结算。必须是纯函数。
    /// </summary>
    /// <param name="state">世界状态。只读，绝不修改。</param>
    /// <param name="order">待结算指令。</param>
    /// <returns>
    /// 成功时携带 StateDelta 列表与至少一条 EventRecord；
    /// 失败时携带明确的结果码与中文 Message。
    /// </returns>
    public abstract ResolveResult Resolve(WorldState state, Order order);

    /// <summary>
    /// 校验执行者是否合法。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <returns>
    /// 合法返回 <see cref="ResolveResultCode.Success"/>；
    /// 执行者不存在/ 已阵亡 / 已叛离返回 <see cref="ResolveResultCode.RejectedInvalidActor"/>。
    /// </returns>
    /// <remarks>
    /// 三种来源统一归约到 FactionId：
    ///   Player     → 玩家势力（IsPlayer == true）
    ///   Officer    → 该人才所属势力（OwnerFactionId，-1 表示在野即叛离）
    ///   AiFaction  → ActorId 本身
    /// 归约后所有后续校验（资源、目标归属）逻辑完全复用，
    /// 这正是「AI 与玩家同路」的实现方式。
    /// </remarks>
    protected ResolveResultCode ValidateActor(WorldState state, Order order)
    {
        switch (order.ActorType)
        {
            case ActorType.Player:
            {
                // 玩家不是人才系统的一员，ActorId 恒为 0。
                if (order.ActorId != Order.PlayerId)
                {
                    return ResolveResultCode.RejectedInvalidActor;
                }

                var playerFaction = state.GetPlayerFaction();
                return playerFaction == null
                    ? ResolveResultCode.RejectedInvalidActor
                    : ResolveResultCode.Success;
            }

            case ActorType.Officer:
            {
                var officer = state.GetOfficer(order.ActorId);
                if (officer == null || !officer.IsAlive || officer.OwnerFactionId < 0)
                {
                    // 不存在 / 已阵亡 / 已叛离（OwnerFactionId == -1）
                    return ResolveResultCode.RejectedInvalidActor;
                }

                return state.GetFaction(officer.OwnerFactionId) == null
                    ? ResolveResultCode.RejectedInvalidActor
                    : ResolveResultCode.Success;
            }

            case ActorType.AiFaction:
            {
                return state.GetFaction(order.ActorId) == null
                    ? ResolveResultCode.RejectedInvalidActor
                    : ResolveResultCode.Success;
            }

            default:
                return ResolveResultCode.RejectedInvalidActor;
        }
    }

    /// <summary>
    /// 校验本月人力上限（单一势力每月可执行的指令总数，契约 v1.5 按势力分账）。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">指令发起势力 Id。</param>
    /// <returns>
    /// 已达上限返回 <see cref="ResolveResultCode.RejectedInsufficientCapacity"/>，
    /// 否则返回 Success。
    /// </returns>
    /// <remarks>
    /// 【契约 v1.5】额度记账在 <c>WorldState.ActionsThisMonthByFaction</c>，
    /// 以势力 Id 为 key，各势力互不挤占；key 取不到（该势力本月尚未执行过指令）按 0 计。
    /// </remarks>
    protected ResolveResultCode ValidateCapacity(WorldState state, int factionId)
    {
        return state.GetActionsThisMonth(factionId) >= Config.MaxOrdersPerFactionPerMonth
            ? ResolveResultCode.RejectedInsufficientCapacity
            : ResolveResultCode.Success;
    }

    /// <summary>
    /// 校验势力是否拥有足够资源。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <param name="factionId">执行者所属势力 Id。</param>
    /// <param name="silverCost">需要的银。</param>
    /// <param name="grainCost">需要的粮。</param>
    /// <returns>资源充足返回 Success，否则返回 <see cref="ResolveResultCode.RejectedNoResources"/>。</returns>
    protected ResolveResultCode ValidateResources(
        WorldState state,
        Order order,
        int factionId,
        int silverCost,
        int grainCost)
    {
        var faction = state.GetFaction(factionId);
        if (faction == null)
        {
            return ResolveResultCode.RejectedInvalidActor;
        }

        if (silverCost > faction.Resources.Silver)
        {
            return ResolveResultCode.RejectedNoResources;
        }

        if (grainCost > faction.Resources.Grain)
        {
            return ResolveResultCode.RejectedNoResources;
        }

        _ = order;
        return ResolveResultCode.Success;
    }

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="code">拒绝码。</param>
    /// <param name="message">面向玩家的中文原因说明。禁止留空。</param>
    /// <returns>携带拒绝码与说明的失败结果。</returns>
    /// <remarks>
    /// 【决策 2：失败必须显式拒绝】Message 中应给出所需与现有数量，
    /// 这样 UI 才能告诉玩家「差多少」，而不是「点了没反应」。
    /// </remarks>
    protected static ResolveResult Reject(ResolveResultCode code, string message)
        => ResolveResult.Fail(code, message);

    /// <summary>
    /// 构造资源不足的失败结果，自动附带所需与现有数量。
    /// </summary>
    /// <param name="silverCost">需要的银。</param>
    /// <param name="grainCost">需要的粮。</param>
    /// <param name="currentSilver">现有银。</param>
    /// <param name="currentGrain">现有粮。</param>
    /// <returns>携带 <see cref="ResolveResultCode.RejectedNoResources"/> 的失败结果。</returns>
    protected static ResolveResult RejectNoResources(
        int silverCost,
        int grainCost,
        int currentSilver,
        int currentGrain)
    {
        var missing = new List<string>(2);
        if (silverCost > currentSilver)
        {
            missing.Add($"银需 {silverCost}，现有 {currentSilver}");
        }

        if (grainCost > currentGrain)
        {
            missing.Add($"粮需 {grainCost}，现有 {currentGrain}");
        }

        string detail = missing.Count > 0 ? string.Join("；", missing) : "资源不足";
        return ResolveResult.Fail(
            ResolveResultCode.RejectedNoResources,
            $"资源不足（{detail}）。");
    }

    /// <summary>
    /// 取目标城「已生效」在岗人才并计算其岗位折扣（万分比）。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="cityId">目标城池 Id。</param>
    /// <param name="requiredPost">所需岗位。人才实际岗位不符则折扣为 0。</param>
    /// <param name="config">数值配置。</param>
    /// <returns>万分比折扣；城池不存在或岗位不对口时为 0。</returns>
    /// <remarks>
    /// 仅当该城已生效在岗人才的 <c>Post == requiredPost</c> 时返回其
    /// <see cref="PostEffects.GetBonus"/>（政务 + 专长，万分比），否则 0。
    /// 生效窗口由 <see cref="PostEffects.GetEffectiveCityOfficer"/> 保证。
    /// </remarks>
    protected static int GetCityPostDiscount(
        WorldState state,
        int cityId,
        PostType requiredPost,
        BalanceConfig config)
    {
        var city = state.GetCity(cityId);
        if (city == null)
        {
            return 0;
        }

        var officer = PostEffects.GetEffectiveCityOfficer(state, city, config);
        if (officer == null || officer.Post != requiredPost)
        {
            return 0;
        }

        return PostEffects.GetBonus(officer, requiredPost, config);
    }

    /// <summary>
    /// 按万分比折扣计算实付成本。
    /// </summary>
    /// <param name="baseCost">基础成本。</param>
    /// <param name="discountPerMille">折扣（万分比），负数按 0 处理。</param>
    /// <returns>实付银 = max(0, baseCost × (10000 − 折扣) / 10000)，向下取整。</returns>
    /// <remarks>
    /// 分母复用 <c>EconomySystem.RatioBase</c>（全程序唯一万分比基数，不另造常量）。
    /// </remarks>
    protected static int ApplyDiscount(int baseCost, int discountPerMille)
    {
        int effective = Math.Max(0, discountPerMille);
        return (int)Math.Max(
            0,
            (long)baseCost * Math.Max(0, StrategyGame.Core.Systems.EconomySystem.RatioBase - effective)
                / StrategyGame.Core.Systems.EconomySystem.RatioBase);
    }

    /// <summary>
    /// 把两个整数做安全乘法并按百分比缩放，全程整数运算。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <param name="perMille">每千分比增量。</param>
    /// <returns>缩放后的整数结果，最小为 0。</returns>
    /// <remarks>
    /// 【为什么全程整数】契约注释明确要求资源禁止浮点（浮点累加会产生误差），
    /// 且无头模拟要求逐字段可复现。因此所有数值计算走整数 + 定点缩放。
    /// </remarks>
    protected static int ScaleInteger(int value, int perMille)
        => value + (int)((long)value * perMille / PlaceholderNumbers.PercentBase);
}
