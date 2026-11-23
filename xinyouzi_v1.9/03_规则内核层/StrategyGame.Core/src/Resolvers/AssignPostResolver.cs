// ============================================================================
//  任命人才结算器
// ============================================================================
//  指令语义：TargetId = 人才 Id，SecondaryTargetId = 岗位所在城池 Id
//
//  【纯函数】只读 state，状态变化只通过 StateDelta 表达。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 任命人才结算器：把人才放到目标城池的岗位上，其特质随即生效。
/// </summary>
/// <remarks>
/// 校验顺序：
///   1. 执行者合法 → Rejected_InvalidActor
///   2. 人才存在且存活 → Rejected_InvalidTarget / Rejected_InvalidActor
///   3. 人才属于该势力 → Rejected_InvalidTarget
///   4. 目标城池存在且属于该势力 → Rejected_InvalidTarget
///   5. 该城池已有在岗人才 → Rejected_TargetOccupied
///   6. 本月人力未超上限 → Rejected_InsufficientCapacity
/// </remarks>
public sealed class AssignPostResolver : ResolverBase
{
    /// <summary>创建任命人才结算器。</summary>
    /// <param name="config">数值配置。忠诚度阈值与到岗月数从此读取。</param>
    /// <param name="random">可播种随机源。当前不涉及随机，保留参数以免后续改签名。</param>
    public AssignPostResolver(BalanceConfig config, IRandom? random = null)
        : base(config, random)
    {
    }

    /// <summary>固定为 <see cref="ActionType.AssignPost"/>。</summary>
    public override ActionType Action => ActionType.AssignPost;

    /// <summary>执行前置校验。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为人才 Id，SecondaryTargetId 为城池 Id。</param>
    /// <returns>校验结果码。</returns>
    public override ResolveResultCode Validate(WorldState state, Order order)
    {
        var baseResult = base.Validate(state, order);
        if (baseResult != ResolveResultCode.Success)
        {
            return baseResult;
        }

        var officer = state.GetOfficer(order.TargetId);
        if (officer == null)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        if (!officer.IsAlive)
        {
            // 人才已阵亡，属于执行者不可用。
            return ResolveResultCode.RejectedInvalidActor;
        }

        var city = state.GetCity(order.SecondaryTargetId);
        if (city == null)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        int factionId = state.ResolveActorFactionId(order);
        if (officer.OwnerFactionId != factionId || city.OwnerFactionId != factionId)
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        // 一城一岗：已有在岗人才则拒绝。
        bool occupied = state.Officers.Any(o =>
            o.PostCityId == city.Id &&
            o.Post != PostType.None &&
            o.IsAlive &&
            o.Id != officer.Id);

        if (occupied)
        {
            return ResolveResultCode.RejectedTargetOccupied;
        }

        return ValidateCapacity(state, factionId);
    }

    /// <summary>执行任命结算。纯函数，不修改 state。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令：TargetId 为人才 Id，SecondaryTargetId 为城池 Id。</param>
    /// <returns>
    /// 成功时产出人才岗位、岗位城池、到岗月数归零的 Delta 与一条 Personnel 类事件；
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
        var officer = state.GetOfficer(order.TargetId)!;
        var city = state.GetCity(order.SecondaryTargetId)!;

        // 岗位类型由人才专长推导：这是「用人」而非「摆棋子」的核心体现。
        var post = DeterminePost(officer);

        var result = ResolveResult.Ok()
            .WithDelta(
                DeltaTarget.Officer, officer.Id, DeltaField.OfficerPost,
                (int)officer.Post, (int)post)
            .WithDelta(
                DeltaTarget.Officer, officer.Id, DeltaField.OfficerPostCityId,
                officer.PostCityId, city.Id)
            .WithDelta(
                DeltaTarget.Officer, officer.Id, DeltaField.OfficerPostedSinceMonths,
                officer.PostedSinceMonths, 0)
            .WithDelta(
                DeltaTarget.Faction, factionId, DeltaField.FactionActionsThisMonth,
                state.GetActionsThisMonth(factionId), state.GetActionsThisMonth(factionId) + 1)
            .WithEvent(
                EventCategory.Personnel,
                EventKeys.AssignPost,
                $"{faction.Name}任命{officer.Name}为{city.Name}{PostTypeLabel(post)}，" +
                $"{Config.PostTakeEffectMonths} 个月后生效。",
                factionId,
                state.Year,
                state.Month);

        return result;
    }

    /// <summary>
    /// 根据人才专长推导应任岗位。
    /// </summary>
    /// <param name="officer">人才。只读。</param>
    /// <returns>岗位类型。</returns>
    /// <remarks>
    /// TODO(设计): 待人事制度定稿后替换。
    /// 当前映射为占位规则：专长与岗位的对应关系属01 包设计范围，
    /// 定稿后此处只改映射表，不改校验与写回逻辑。
    /// </remarks>
    private static PostType DeterminePost(Officer officer)
    {
        return officer.Specialty switch
        {
            OfficerSpecialty.Agriculture => PostType.FarmOverseer,
            OfficerSpecialty.Siege => PostType.FortifyOverseer,
            OfficerSpecialty.Cavalry => PostType.General,
            OfficerSpecialty.Diplomacy => PostType.Scoutmaster,
            _ => PostType.CityGovernor,
        };
    }

    /// <summary>
    /// 把岗位类型转为中文名，用于事件文本。
    /// </summary>
    /// <param name="post">岗位类型。</param>
    /// <returns>岗位中文名。</returns>
    private static string PostTypeLabel(PostType post)
    {
        return post switch
        {
            PostType.CityGovernor => "城守",
            PostType.FortifyOverseer => "军师佐政",
            PostType.FarmOverseer => "屯田使",
            PostType.General => "将军",
            PostType.Scoutmaster => "斥候",
            _ => "属官",
        };
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
            {
                var dead = state.GetOfficer(order.TargetId);
                return dead != null && !dead.IsAlive
                    ? $"{dead.Name}已阵亡，无法任命。"
                    : "执行者不存在、已阵亡或已叛离，无法下令。";
            }

            case ResolveResultCode.RejectedInvalidTarget:
            {
                var officer = state.GetOfficer(order.TargetId);
                var city = state.GetCity(order.SecondaryTargetId);

                if (officer == null)
                {
                    return $"目标人才不存在（Id={order.TargetId}）。";
                }

                if (city == null)
                {
                    return $"目标城池不存在（Id={order.SecondaryTargetId}）。";
                }

                if (officer.OwnerFactionId != factionId)
                {
                    return $"无权任命他势力的人才「{officer.Name}」。";
                }

                return $"无权在敌方城池「{city.Name}」任命属官。";
            }

            case ResolveResultCode.RejectedTargetOccupied:
            {
                var city = state.GetCity(order.SecondaryTargetId);
                return $"「{city?.Name}」已有在岗人才，一城一岗，不可兼任。";
            }

            case ResolveResultCode.RejectedInsufficientCapacity:
                return $"本月指令数已达上限（{Config.MaxOrdersPerFactionPerMonth} 条）。";

            default:
                return "任命指令被拒绝。";
        }
    }
}
