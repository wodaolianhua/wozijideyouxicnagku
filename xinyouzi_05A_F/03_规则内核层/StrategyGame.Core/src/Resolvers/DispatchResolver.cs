// ============================================================================
//  出兵结算器（含战斗结算）
// ============================================================================
//  指令语义：TargetId = 目标城池 Id，SecondaryTargetId = 出发城 Id，Amount = 兵力
//
//  【纯函数】只读 state，状态变化只通过 StateDelta 表达。
//  【战斗来源】01_设计规格层/3_战斗系统设计.md，公式与系数全部走 BalanceConfig。
//
//  【亲征】ActorType == Player 且玩家当前未亲征时，本指令为亲征：
//  玩家统帅位占据该部队，不叠加任何 Officer 加成（设计决策 D9）。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Systems;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 出兵结算器：将领率军移动到目标城池，敌方城池则进行攻城战结算。
/// </summary>
/// <remarks>
/// 校验顺序（严格按 5_指令字典.md，先查存在再查归属）：
///   1. 出发城与目标城都存在 → Rejected_InvalidTarget
///   2. 出发城归属本势力 → Rejected_InvalidTarget
///   3. 两城相邻 → Rejected_NotAdjacent
///   4. 存在可调动部队且兵力充足 → Rejected_NoResources
///   5. 目标城未被围困 → Rejected_TargetSieged（同阵营围城部队豁免）
    ///   6. 人力未满 → Rejected_InsufficientCapacity
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

    private readonly BattleSystem _battle;
    private readonly MoraleSystem _morale;

    /// <summary>创建出兵结算器。</summary>
    /// <param name="config">数值配置。所有系数从此读取。</param>
    /// <param name="random">可播种随机源。当前结算完全确定，不消耗随机数。</param>
    public DispatchResolver(BalanceConfig config, IRandom? random = null)
        : base(config, random)
    {
        _battle = new BattleSystem(config);
        _morale = new MoraleSystem(config);
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

        if (!origin.AdjacentCityIds.Contains(order.TargetId))
        {
            // 相邻性以出发城的关系表为准。
            return ResolveResultCode.RejectedNotAdjacent;
        }

        var army = FindDispatchableArmy(state, order, factionId);
        if (army == null)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        if (order.Amount <= 0 || order.Amount > army.Soldiers)
        {
            return ResolveResultCode.RejectedNoResources;
        }

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
            // 契约 v1.3 起提供独立结果码：围困是临时状态（下月可能解除，等一等），
            // 与永久性的 RejectedTargetOccupied（已被其他势力占领，换目标）区分。
            // v1.2 时代曾因枚举缺成员而暂用 RejectedTargetOccupied，现正名。
            return ResolveResultCode.RejectedTargetSieged;
        }

        return ValidateCapacity(state, factionId);
    }

    /// <summary>执行出兵与战斗结算。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为目标城，SecondaryTargetId 为出发城，Amount 为兵力。</param>
    /// <returns>
    /// 成功时产出攻方兵力、守方兵力、城池归属、部队位置的 Delta 及事件；
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
        bool isSiege = target.OwnerFactionId != factionId;
        bool personalCommand = TryResolvePersonalCommand(state, order, army);

        int attackerCommand = ResolveAttackerCommand(state, army, personalCommand);

        // 守方：目标城内的敌方部队。
        var defenders = state.GetArmiesAtCity(target.Id)
            .Where(a => a.OwnerFactionId != factionId)
            .ToList();

        int defenderSoldiers = defenders.Sum(a => a.Soldiers);
        int defenderCommand = ResolveDefenderCommand(state, defenders);

        var result = ResolveResult.Ok();

        // =====================================================================
        // 分支一：调兵至自家城池 —— 不发生战斗，直接行军。
        // =====================================================================
        if (!isSiege)
        {
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
        // 分支二：进攻敌方城池 —— 进行战斗结算。
        // =====================================================================
        var battle = _battle.Resolve(
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

        // ---- 攻方兵力变化 ----
        // 【重要】最终兵力 = 投入兵力 − 战斗损失。
        // 不能再额外扣一次「投入兵力」——那会让兵力凭空消失
        //（300 投入、损失 30，结果变成 300−300−30 = 0，少算了 270 人）。
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
            // 参战即重置「连续未参战月数」并置「本周期参战」标记（契约 v1.4）：
            // 缺了这两个 delta，被围城的守军头 3 个月照走恢复分支每月 +3 回血，
            // 第 4 月起又在「实际在打仗」的同时被腐化 −6。
            // 守方士气 −4 已由 CalculateDefenderMoraleDelta 在下方结过，
            // 次月结算读到标记为 true 即走参战分支（不恢复、不腐化）。
            // 没有参战的部队（不在 defenders 列表）不置位。
            result.WithDelta(
                DeltaTarget.Army, defender.Id, DeltaField.ArmyHasEngagedThisMonth,
                defender.HasEngagedThisMonth ? 1 : 0, 1);

            result.WithDelta(
                DeltaTarget.Army, defender.Id, DeltaField.ArmyMonthsWithoutBattle,
                defender.MonthsWithoutBattle, 0);
        }

        // ---- 参战即重置「连续未参战月数」，并置「本周期参战」标记（契约 v1.4）----
        // 和平腐化的判定依据就是计数与标记：打过仗的部队不该继续腐化，
        // 下月第 3 步士气结算读到标记为 true 即走「参战周期」分支（不恢复、不腐化），
        // 读后由 MoraleSystem 消费清零。攻守对称：守方的同组 delta 已在
        // 上方守军损失分摊循环内补发（第 3 轮补丁·修复三）。
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
                    army.Morale, ApplyMoraleDelta(army, _morale.CalculateAttackerMoraleDelta(battle.Outcome)));

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
                // 击退：攻方退回出发城。
                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyPositionCityId,
                    army.PositionCityId, origin.Id);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyTargetCityId,
                    army.TargetCityId, -1);

                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyStance,
                    (int)army.Stance, (int)ArmyStance.Idle);

                // 被击退后士气重挫（MoraleLossOnRepel）。
                result.WithDelta(
                    DeltaTarget.Army, army.Id, DeltaField.ArmyMorale,
                    army.Morale, ApplyMoraleDelta(army, _morale.CalculateAttackerMoraleDelta(battle.Outcome)));

                result.WithEvent(
                    EventCategory.Military,
                    EventKeys.SiegeRepelled,
                    $"{faction.Name}攻{target.Name}受挫，部队退回{origin.Name}，" +
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
                    army.Morale, ApplyMoraleDelta(army, _morale.CalculateAttackerMoraleDelta(battle.Outcome)));

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
                ApplyMoraleDelta(defender, _morale.CalculateDefenderMoraleDelta()));
        }

        return FinalizeOrder(result, state, order, army, personalCommand, factionId);
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
    /// 注意这里与 lossRate 的钳制规则<b>相反</b>：
    /// lossRate 不许钳制（否则低士气惩罚失效），士气必须钳制（否则状态非法）。
    /// </remarks>
    private static int ApplyMoraleDelta(Army army, int delta)
        => Math.Clamp(army.Morale + delta, 0, Math.Max(0, army.MoraleMax));

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
    /// 取攻方统帅值。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="army">部队。只读。</param>
    /// <param name="personalCommand">是否亲征。</param>
    /// <returns>统帅值 0-100。</returns>
    private static int ResolveAttackerCommand(
        WorldState state,
        Army army,
        bool personalCommand)
        => personalCommand
            ? state.Player.Command
            : state.GetEffectiveCommand(army.Id);

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
    /// <param name="personalCommand">本次出动是否为亲征。</param>
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
        var army = PickBestArmy(
            state,
            factionId,
            a => a.PositionCityId == order.SecondaryTargetId);

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

                return $"{origin.Name}没有可调动的部队。";
            }

            case ResolveResultCode.RejectedNotAdjacent:
            {
                var origin = state.GetCity(order.SecondaryTargetId);
                var target = state.GetCity(order.TargetId);
                return $"「{origin?.Name}」与「{target?.Name}」不相邻，无法出兵。";
            }

            case ResolveResultCode.RejectedTargetSieged:
            {
                var target = state.GetCity(order.TargetId);
                return $"「{target?.Name}」正被围困，本月无法再对该城出兵，下月再试。";
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
