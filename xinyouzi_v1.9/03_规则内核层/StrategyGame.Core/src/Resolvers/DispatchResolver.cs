// ============================================================================
//  出兵结算器（含战斗结算 + 长途行军开拔）
// ============================================================================
//  指令语义：TargetId = 目标城池 Id，SecondaryTargetId = 出发城 Id，Amount = 兵力
//
//  【纯函数】只读 state，状态变化只通过 StateDelta 表达。
//  【战斗来源】01_设计规格层/3_战斗系统设计.md，公式与系数全部走 BalanceConfig。
//  战斗结算本体已抽到 <see cref="SiegeSettlement"/>，供「相邻出兵」与
//  「长途行军到达」两条通路共用，本文件不再内嵌战斗公式。
//
//  【亲征】ActorType == Player 且玩家当前未亲征时，本指令为亲征：
//  玩家统帅位占据该部队，不叠加任何 Officer 加成（设计决策 D9）。
//
//  【第 8 轮·长途行军】
//    · 相邻拒绝只对非玩家（AI/人才代管）生效；玩家可对全图任意城出兵。
//    · 玩家对非相邻城出兵 = 「开拔」：按 BFS 路径跳数计算行军月数与粮草，
//      写入部队的行军字段（MarchTargetCityId / MarchArriveYear / MarchArriveMonth /
//      MarchPathCityIds），此后由 Systems.MarchSystem 在月 tick 中推进，到达即触发
//      与本文件共用的攻城结算。行军中的部队不可再下 Dispatch。
//    · 行军参数见 PlaceholderNumbers（MarchMonthsPerHop /
//      MarchGrainPerSoldierPerHopPerMille，TODO(设计) 待 02 落 config）。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Pathfinding;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 出兵结算器：将领率军移动到目标城池，敌方城池则进行攻城战结算。
/// </summary>
/// <remarks>
/// 校验顺序（严格按 5_指令字典.md，先查存在再查归属）：
///   1. 出发城与目标城都存在 → Rejected_InvalidTarget
///   2. 出发城归属本势力 → Rejected_InvalidTarget
///   3. 两城相邻 → Rejected_NotAdjacent（【第8轮】仅对非玩家保留；
///      玩家非相邻改为长途行军，无连通路径 → Rejected_NoPath）
///   4. 存在可调动部队且兵力充足 → Rejected_NoResources
///   5. 目标城未被围困 → Rejected_TargetSieged（同阵营围城部队豁免；
///      长途行军到达在数月之后，开拔时不做本检查）
///   6. 长途行军粮草充足 → Rejected_NoResources
///   7. 人力未满 → Rejected_InsufficientCapacity
///   8. 行军中的部队不可再下令 → Rejected_ArmyMarching（第8轮）
/// </remarks>
/// <remarks>
/// 【围困标记的载体】契约 v1.2 未给 City 增加「被围困」字段，
/// 因此本实现以「城内存在 <see cref="ArmyStance.Besieging"/> 的部队」为等价标记。
/// 这与未决结算时把攻方部队 stance 置为 Besieging 是同一件事的两面。
/// </remarks>
public sealed class DispatchResolver : ResolverBase
{
    /// <summary>表示「无统帅」的哨兵值。</summary>
    private const int NoOfficer = -1;

    /// <summary>创建出兵结算器。</summary>
    /// <param name="config">数值配置。所有系数从此读取。</param>
    /// <param name="random">可播种随机源。当前结算完全确定，不消耗随机数。</param>
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

        int factionId = state.ResolveActorFactionId(order);
        if (origin.OwnerFactionId != factionId)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        bool isAdjacent = origin.AdjacentCityIds.Contains(order.TargetId);

        // 【第 8 轮】相邻拒绝只对非玩家保留：AI 规划器仍依赖相邻规则，
        // 本轮不动 AI。玩家对非相邻城出兵改走长途行军。
        if (!isAdjacent && order.ActorType != ActorType.Player)
        {
            return ResolveResultCode.RejectedNotAdjacent;
        }

        Army? army;
        if (isAdjacent)
        {
            army = FindDispatchableArmy(state, order, factionId);
        }
        else
        {
            // 长途行军：路径必须连通（无路径 → RejectedNoPath）且至少 1 跳
            // （目标就是出发城 → RejectedInvalidTarget），
            // 且只能由出发城内的部队开拔（围城续攻的第二轮兜底语义只属于相邻战）。
            var marchPath = MapPathfinder.FindPath(state, origin.Id, order.TargetId);
            if (!marchPath.Found)
            {
                return ResolveResultCode.RejectedNoPath;
            }

            if (marchPath.Hops < 1)
            {
                return ResolveResultCode.RejectedInvalidTarget;
            }

            army = FindArmyAtOrigin(state, order, factionId);
        }

        if (army == null)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        // 【第 8 轮】行军中的部队不可再下 Dispatch（同一月开拔后部队仍停在出发城，
        // 没有这个拦截会被连点两次出兵）。
        if (army.MarchTargetCityId >= 0)
        {
            return ResolveResultCode.RejectedArmyMarching;
        }

        if (order.Amount <= 0 || order.Amount > army.Soldiers)
        {
            return ResolveResultCode.RejectedNoResources;
        }

        if (isAdjacent)
        {
            // 目标若被敌对阵营围困（城内有敌方 Besieging 部队），本月不可再对该城Dispatch。
            // 【第 3 轮补丁·修复一】同阵营的围城部队豁免——只有敌对阵营的围城部队
            // 才算围困。否则僵持后攻方滞留敌城、姿态 Besieging，会被自家围城标记
            // 拦死在下月之外，与僵持事件「下月可再战」直接矛盾。
            bool underSiege = state.Armies.Any(a =>
                a.PositionCityId == order.TargetId
                && a.Stance == ArmyStance.Besieging
                && a.OwnerFactionId != factionId);

            if (underSiege)
            {
                return ResolveResultCode.RejectedTargetSieged;
            }

            return ValidateCapacity(state, factionId);
        }

        // ---- 长途行军：开拔时校验粮草（跳数 × 每兵每跳消耗）----
        // 【为什么开拔时不查围困】行军到达在数月之后，此刻的围困状态到
        // 达时早已解除或易主，查了反而是错误拦截。
        int hops = MapPathfinder.FindPath(state, origin.Id, order.TargetId).Hops;
        var grainCheck = ValidateResources(
            state, order, factionId, 0, CalculateMarchGrainCost(order.Amount, hops));
        if (grainCheck != ResolveResultCode.Success)
        {
            return grainCheck;
        }

        return ValidateCapacity(state, factionId);
    }

    /// <summary>执行出兵结算。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为目标城，SecondaryTargetId 为出发城，Amount 为兵力。</param>
    /// <returns>
    /// 相邻出兵：成功时产出攻方兵力、守方兵力、城池归属、部队位置的 Delta 及事件；
    /// 长途行军：成功时产出部队行军字段与粮草扣减的 Delta 及开拔事件；
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

        bool isAdjacent = origin.AdjacentCityIds.Contains(order.TargetId);

        if (!isAdjacent)
        {
            return ResolveMarchStart(state, order, faction, origin, target, factionId);
        }

        var army = FindDispatchableArmy(state, order, factionId)!;
        int dispatched = order.Amount;
        bool isSiege = target.OwnerFactionId != factionId;
        bool personalCommand = TryResolvePersonalCommand(state, order, army);

        var result = ResolveResult.Ok();

        // =====================================================================
        // 分支一：调兵至自家城池 —— 不发生战斗，直接行军。
        // =====================================================================
        if (!isSiege)
        {
            // 【v1.8 合兵】目的地已有己方部队时，两军合并为一个番号。
            // 【为什么必须合】兵源池是「平均摊给每支部队」的，各城驻军因此永远同涨同跌，
            // 谁也攒不出两倍优势；而攻城判定要求「攻方打掉的守军 ≥ 守军总数」，
            // 均势永远打不穿。合兵是唯一能把兵力集中的动作——没有它，
            // 「集中兵力攻城」这句话在系统层面无法实现，界面上的「调兵」按钮也只是搬家。
            var resident = state.GetArmiesAtCity(target.Id)
                .Where(a => a.Id != army.Id
                    && a.OwnerFactionId == factionId
                    && a.Soldiers > 0)
                .OrderBy(a => a.Id)
                .FirstOrDefault();

            if (resident != null)
            {
                int merged = resident.Soldiers + dispatched;

                result.WithDelta(
                    DeltaTarget.Army, resident.Id, DeltaField.ArmySoldiers,
                    resident.Soldiers, merged);

                // 并入方兵力清零（番号并入驻军，等同于该部队已不存在）。
                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmySoldiers,
                    army.Soldiers, 0);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyPositionCityId,
                    army.PositionCityId, target.Id);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyTargetCityId,
                    army.TargetCityId, -1);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyStance,
                    (int)army.Stance, (int)ArmyStance.Idle);

                result.WithEvent(
                    EventCategory.Military,
                    EventKeys.Dispatch,
                    $"{faction.Name}自{origin.Name}调兵 {dispatched} 人至{target.Name}，" +
                    $"与驻军合兵，共 {merged} 人。",
                    factionId,
                    state.Year,
                    state.Month);

                return FinalizeOrder(result, state, order, army, personalCommand, factionId);
            }

            // 行军不损耗：投入的兵力原样抵达目标城。
            result.WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmySoldiers,
                army.Soldiers, dispatched);

            result.WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyPositionCityId,
                army.PositionCityId, target.Id);

            result.WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyTargetCityId,
                army.TargetCityId, target.Id);

            result.WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyStance,
                (int)army.Stance, (int)ArmyStance.Moving);

            result.WithEvent(
                EventCategory.Military,
                EventKeys.Dispatch,
                $"{faction.Name}自{origin.Name}调兵 {dispatched} 人，进驻{target.Name}。",
                factionId,
                state.Year,
                state.Month);

            return FinalizeOrder(result, state, order, army, personalCommand, factionId);
        }

        // =====================================================================
        // 分支二：进攻敌方城池 —— 战斗结算本体在 SiegeSettlement，
        // 与「长途行军到达」共用同一份公式与 Delta 序列。
        // =====================================================================
        int attackerCommand = personalCommand
            ? state.Player.Command
            : state.GetEffectiveCommand(army.Id);

        var battle = SiegeSettlement.ResolveSiegeBattle(
            state, Config, army, dispatched, target, origin, factionId, attackerCommand, personalCommand);

        result.Deltas.AddRange(battle.Deltas);
        result.Events.AddRange(battle.Events);

        return FinalizeOrder(result, state, order, army, personalCommand, factionId);
    }

    /// <summary>
    /// 长途行军开拔：写行军字段、扣粮草、部队姿态置为 Moving。
    /// 部队位置本月仍在出发城，由 <see cref="Systems.MarchSystem"/> 在后续月 tick 逐跳推进。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <param name="faction">攻方势力。只读。</param>
    /// <param name="origin">出发城。只读。</param>
    /// <param name="target">目标城。只读。</param>
    /// <param name="factionId">攻方势力 Id。</param>
    /// <returns>开拔结算结果。</returns>
    private ResolveResult ResolveMarchStart(
        WorldState state,
        Order order,
        Faction faction,
        City origin,
        City target,
        int factionId)
    {
        var path = MapPathfinder.FindPath(state, origin.Id, target.Id);
        if (!path.Found || path.Path == null)
        {
            // Validate 已拦过，这里是防御分支。
            return Reject(ResolveResultCode.RejectedNoPath, "目标不可达：没有连通路径。");
        }

        if (path.Hops < 1)
        {
            return Reject(ResolveResultCode.RejectedInvalidTarget, "目标就是出发城，无法行军。");
        }

        var army = FindArmyAtOrigin(state, order, factionId)!;

        int hops = path.Hops;
        int dispatched = order.Amount;
        int grainCost = CalculateMarchGrainCost(dispatched, hops);
        int monthsPerHop = Math.Max(1, PlaceholderNumbers.MarchMonthsPerHop);
        int marchMonths = hops * monthsPerHop;
        var (arriveYear, arriveMonth) = TimeUtil.FromTotalMonths(
            TimeUtil.TotalMonths(state.Year, state.Month) + marchMonths);

        var result = ResolveResult.Ok()
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionGrain,
                faction.Resources.Grain, faction.Resources.Grain - grainCost)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmySoldiers,
                army.Soldiers, dispatched)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyTargetCityId,
                army.TargetCityId, target.Id)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyStance,
                (int)army.Stance, (int)ArmyStance.Moving)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyMarchTargetCityId,
                army.MarchTargetCityId, target.Id)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyMarchArriveYear,
                army.MarchArriveYear, arriveYear)
            .WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyMarchArriveMonth,
                army.MarchArriveMonth, arriveMonth);

        // 路径快照：先清空，再按下标逐元素写入（StateApplier 专用分支）。
        result.WithDelta(
            DeltaTarget.Army, army.Id, DeltaField.ArmyMarchPathCityIds,
            0, -1);

        for (int i = 0; i < path.Path.Count; i++)
        {
            result.WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyMarchPathCityIds,
                i, path.Path[i]);
        }

        string via = string.Join("、", path.Path
            .Skip(1)
            .Take(path.Path.Count - 2)
            .Select(id => state.GetCity(id)?.Name ?? id.ToString()));

        string viaText = via.Length == 0 ? string.Empty : $"（途经 {via}）";

        result.WithEvent(
            EventCategory.Military,
            EventKeys.Dispatch,
            $"{faction.Name}自{origin.Name}发兵 {dispatched} 人，远征{target.Name}" +
            $"{viaText}，预计 {arriveYear} 年 {arriveMonth} 月抵达，耗粮 {grainCost}。",
            factionId,
            state.Year,
            state.Month);

        // 【第8轮】长途行军不触发亲征（亲征 = 统帅位被玩家占据、不叠加人才加成 D9）。
        // 行军动辄数月，若开拔即亲征会把玩家统帅位锁死整个行军期；
        // 到达时的攻城战按部队当前统帅结算（若玩家此前已在亲征该部队则自然沿用）。
        return FinalizeOrder(result, state, order, army, false, factionId);
    }

    /// <summary>
    /// 长途行军粮草消耗：兵力 × 跳数 × 每兵每跳消耗（万分比），向下取整。
    /// </summary>
    /// <param name="soldiers">投入兵力。</param>
    /// <param name="hops">路径跳数（≥ 2）。</param>
    /// <returns>粮草消耗量。</returns>
    /// <remarks>
    /// 【第8轮】首版公式 = 基础消耗 × 跳数，基础消耗 = 兵力 × 系数 / 10000。
    /// 系数 PlaceholderNumbers.MarchGrainPerSoldierPerHopPerMille（TODO(设计) 待 02 落 config）。
    /// </remarks>
    private static int CalculateMarchGrainCost(int soldiers, int hops)
        => (int)((long)soldiers * hops * PlaceholderNumbers.MarchGrainPerSoldierPerHopPerMille
            / DesignConstants.TenThousandBase);

    /// <summary>
    /// 收尾：重置休整标记、处理亲征状态、累加人力计数。
    /// </summary>
    /// <param name="result">已累积 Delta 的结算结果。</param>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <param name="army">出动的部队。只读。</param>
    /// <param name="personalCommand">是否亲征。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <returns>补齐公共 Delta 后的结算结果。</returns>
    /// <remarks>
    /// 调兵与战斗两条分支共用同一套收尾逻辑，
    /// 避免「调兵不设亲征、战斗设亲征」这类分支间行为漂移。
    /// </remarks>
    private static ResolveResult FinalizeOrder(
        ResolveResult result,
        WorldState state,
        Order order,
        Army army,
        bool personalCommand,
        int factionId)
    {
        // 出兵后重置休整标记。
        result.WithDelta(
            DeltaTarget.Army, army.Id, DeltaField.ArmyHasRested,
            army.HasRestedThisMonth ? 1 : 0, 0);

        // ---- 亲征：设置玩家统帅位（D9：不叠加任何 Officer 加成） ----
        if (personalCommand)
        {
            result.WithDelta(
                DeltaTarget.WorldState, 0, DeltaField.WorldPlayerIsCommanding,
                state.Player.IsPersonallyCommanding ? 1 : 0, 1);

            result.WithDelta(
                DeltaTarget.WorldState, 0, DeltaField.WorldPlayerCommandingArmyId,
                state.Player.CommandingArmyId, army.Id);

            // 统帅位被玩家占据，人才让位。
            result.WithDelta(
                DeltaTarget.Army, army.Id, DeltaField.ArmyOfficerId,
                army.OfficerId, NoOfficer);
        }

        _ = order;

        // ---- 人力计数（契约 v1.5：按指令发起势力分账，TargetId 即势力 Id）----
        result.WithDelta(
            DeltaTarget.Faction, factionId, DeltaField.FactionActionsThisMonth,
            state.GetActionsThisMonth(factionId), state.GetActionsThisMonth(factionId) + 1);

        return result;
    }

    /// <summary>
    /// 判断本次出兵是否为玩家亲征。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <param name="army">将要出动的部队。只读。</param>
    /// <returns>玩家亲自统率且当前未亲征时返回 true。</returns>
    /// <remarks>
    /// 亲征是一次「窗口」：玩家亲征期间统帅位被占据，不叠加人才加成（D9）。
    /// 已在亲征中则本条按普通调兵处理。
    /// </remarks>
    private static bool TryResolvePersonalCommand(WorldState state, Order order, Army army)
        => order.ActorType == ActorType.Player
           && !state.Player.IsPersonallyCommanding
           && order.ActorId == Order.PlayerId;

    /// <summary>
    /// 找出可执行本次出兵的部队。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <returns>符合条件的部队；无则返回 null。</returns>
    /// <remarks>
    /// 【两轮查找（第 3 轮补丁·修复一）】
    ///   第一轮（原规则）：从出发城（SecondaryTargetId）的己方部队中选；
    ///   第二轮兜底：第一轮找不到时，在「已滞留目标城下围城」的己方部队中选。
    ///   僵持（未决）后攻方驻在敌城、不在任何己方出发城，若无第二轮，
    ///   这支部队从此无法被再次调度，城也永远打不了——部队与城双死锁。
    ///   续攻指令的 SecondaryTargetId 仍传相邻城，语义为补给方向，
    ///   Validate 的相邻性校验维持不变。
    /// 两轮同样统帅优先、同统帅按部队 Id 升序打破平局，保证确定——
    /// 遍历顺序不定会让无头模拟产生不可复现的结果。
    /// </remarks>
    private static Army? FindDispatchableArmy(WorldState state, Order order, int factionId)
    {
        // 第一轮：出发城内的己方部队（原规则，优先级不变）。
        var army = FindArmyAtOrigin(state, order, factionId);

        if (army != null)
        {
            return army;
        }

        // 第二轮兜底：目标城下的己方围城部队就地再战。
        return PickBestArmy(
            state,
            factionId,
            a => a.PositionCityId == order.TargetId && a.Stance == ArmyStance.Besieging);
    }

    /// <summary>找出发城内的己方部队（长途行军只允许从这里开拔）。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <returns>符合条件的部队；无则返回 null。</returns>
    private static Army? FindArmyAtOrigin(WorldState state, Order order, int factionId)
        => PickBestArmy(
            state,
            factionId,
            a => a.PositionCityId == order.SecondaryTargetId);

    /// <summary>
    /// 在己方、有兵的部队中按条件挑选统帅最高者。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <param name="predicate">候选条件（位置/姿态）。</param>
    /// <returns>统帅最高的候选部队；无则返回 null。</returns>
    private static Army? PickBestArmy(WorldState state, int factionId, Func<Army, bool> predicate)
    {
        Army? best = null;
        int bestCommand = int.MinValue;

        foreach (var army in state.Armies)
        {
            if (army.OwnerFactionId != factionId || army.Soldiers <= 0 || !predicate(army))
            {
                continue;
            }

            int command = state.GetEffectiveCommand(army.Id);

            // 统帅相同时按 Id 升序，打破平局，保证确定性。
            if (best == null || command > bestCommand
                || (command == bestCommand && army.Id < best.Id))
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

                return $"{origin.Name}没有可出动的部队（长途行军只能由出发城内的部队开拔）。";
            }

            case ResolveResultCode.RejectedNotAdjacent:
            {
                var origin = state.GetCity(order.SecondaryTargetId);
                var target = state.GetCity(order.TargetId);
                return $"「{origin?.Name}」与「{target?.Name}」不相邻，无法出兵。";
            }

            case ResolveResultCode.RejectedNoPath:
            {
                var origin = state.GetCity(order.SecondaryTargetId);
                var target = state.GetCity(order.TargetId);
                return $"「{origin?.Name}」到「{target?.Name}」没有连通路径，无法行军。";
            }

            case ResolveResultCode.RejectedArmyMarching:
                return "该部队正在长途行军中，行军期间不能再对它下令，等它抵达后再行动。";

            case ResolveResultCode.RejectedTargetSieged:
            {
                var target = state.GetCity(order.TargetId);
                return $"「{target?.Name}」正被围困，本月无法再对该城出兵，下月再试。";
            }

            case ResolveResultCode.RejectedNoResources:
            {
                var army = FindDispatchableArmy(state, order, factionId);
                if (army == null)
                {
                    return "无可调动的兵力。";
                }

                if (order.Amount <= 0 || order.Amount > army.Soldiers)
                {
                    return $"出兵兵力不足（现有 {army.Soldiers}，请求 {order.Amount}）。";
                }

                var origin = state.GetCity(order.SecondaryTargetId);
                int hops = origin != null
                    ? MapPathfinder.FindPath(state, origin.Id, order.TargetId).Hops
                    : 0;
                int grainCost = CalculateMarchGrainCost(order.Amount, hops);
                var faction = state.GetFaction(factionId);
                return $"长途行军粮草不足：需要 {grainCost} 粮（{hops} 跳），" +
                       $"现有 {faction?.Resources.Grain ?? 0} 粮。";
            }

            case ResolveResultCode.RejectedInsufficientCapacity:
                return $"本月指令数已达上限（{Config.MaxOrdersPerFactionPerMonth} 条）。";

            default:
                return "出兵指令被拒绝。";
        }
    }
}
