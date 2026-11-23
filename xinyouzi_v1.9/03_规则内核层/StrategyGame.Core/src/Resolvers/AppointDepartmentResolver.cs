// ============================================================================
//  部门任命结算器
// ============================================================================
//  指令语义：TargetId = 部门枚举值（0-4），SecondaryTargetId = 人才 Id（-1 表示卸任）
//
//  【为什么不消耗行动力】
//  换人是这个游戏的核心操作。如果任命要花行动力，玩家会为了省额度而
//  一次任命到底、再也不调岗，「用人」就退化成了「开局点一下」。
//  所以任命免费、随时可换，代价体现在「换上去的人不一定更好」。
//
//  【一人只管一个部门】把 A 从内政调到军事，内政自动空出来（回到 80% 效率）。
//  这逼玩家在「谁更适合哪个位置」之间做取舍，而不是把强人复制到每个部门。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Resolvers;

/// <summary>
/// 部门任命结算器：把一名人才放到某个部门，或把某部门空出来。
/// </summary>
/// <remarks>
/// 校验顺序：执行者 → 部门合法 → 人才属于本势力且在世 → 该部门当前主官存在（卸任时）。
/// 不校验行动力，不校验资源。
/// </remarks>
public sealed class AppointDepartmentResolver : ResolverBase
{
    /// <summary>创建部门任命结算器。</summary>
    /// <param name="config">数值配置。</param>
    /// <param name="random">随机源。本结算器不使用随机。</param>
    public AppointDepartmentResolver(BalanceConfig config, IRandom? random = null)
        : base(config, random)
    {
    }

    /// <summary>固定为 <see cref="ActionType.AppointDepartment"/>。</summary>
    public override ActionType Action => ActionType.AppointDepartment;

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

        if (!Enum.IsDefined(typeof(DepartmentType), order.TargetId))
        {
            return ResolveResultCode.RejectedInvalidTarget;
        }

        // 卸任：SecondaryTargetId = -1，只要部门合法即可。
        if (order.SecondaryTargetId == -1)
        {
            return ResolveResultCode.Success;
        }

        var officer = state.GetOfficer(order.SecondaryTargetId);
        if (officer == null || !officer.IsAlive)
        {
            return ResolveResultCode.RejectedInvalidActor;
        }

        int factionId = state.ResolveActorFactionId(order);
        if (officer.OwnerFactionId != factionId)
        {
            return ResolveResultCode.RejectedInvalidActor;
        }

        return ResolveResultCode.Success;
    }

    /// <summary>执行结算。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <returns>成功时写入部门主官表；失败时返回中文原因。</returns>
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
        var department = (DepartmentType)order.TargetId;
        int officerId = order.SecondaryTargetId;

        int previousHead = faction.DepartmentHead != null
            && faction.DepartmentHead.TryGetValue(department, out int current)
                ? current
                : -1;

        var result = ResolveResult.Ok()
            .WithDelta(
                DeltaTarget.Faction, factionId,
                DeltaField.FactionDepartmentHeadPrefix + (int)department,
                previousHead, officerId);

        // 一人只管一个部门：先清掉他原来管的那个部门。
        if (officerId >= 0 && faction.DepartmentHead != null)
        {
            foreach (var pair in faction.DepartmentHead)
            {
                if (pair.Key != department && pair.Value == officerId)
                {
                    result.WithDelta(
                        DeltaTarget.Faction, factionId,
                        DeltaField.FactionDepartmentHeadPrefix + (int)pair.Key,
                        pair.Value, -1);
                }
            }
        }

        string message;
        if (officerId < 0)
        {
            message = $"{faction.Name}的{DepartmentName(department)}主官卸任。";
        }
        else
        {
            var officer = state.GetOfficer(officerId)!;
            int attr = Systems.DepartmentSystem.GetKeyAttribute(officer, department);
            int efficiency = Systems.DepartmentSystem.GetEfficiency(
                Simulate(state, factionId, department, officerId), factionId, department);

            message = $"{faction.Name}任命{officer.Name}为{DepartmentName(department)}主官，" +
                      $"关键属性 {attr}，效率 {efficiency / 100}%。";
        }

        result.WithEvent(
            EventCategory.Personnel,
            EventKeys.AppointDepartment,
            message,
            factionId,
            state.Year,
            state.Month);

        return result;
    }

    /// <summary>
    /// 造一份「任命已生效」的临时状态，用于在只读前提下算出任命后的效率。
    /// </summary>
    /// <param name="state">原状态。只读。</param>
    /// <param name="factionId">势力。</param>
    /// <param name="department">部门。</param>
    /// <param name="officerId">新人选。</param>
    /// <returns>克隆后的状态，仅供读效率，不会被写回。</returns>
    /// <remarks>
    /// 事件文案要写「任命后效率」，但此时 Delta 还没写回。为了让文案与真实结果一致，
    /// 克隆一份状态先算一次。克隆是一次性的，不进入任何状态通道。
    /// </remarks>
    private static WorldState Simulate(WorldState state, int factionId, DepartmentType department, int officerId)
    {
        var clone = state.Clone();
        var faction = clone.GetFaction(factionId);
        if (faction != null)
        {
            faction.DepartmentHead ??= new Dictionary<DepartmentType, int>();
            faction.DepartmentHead[department] = officerId;
        }

        return clone;
    }

    private static string DepartmentName(DepartmentType department) => department switch
    {
        DepartmentType.Military => "军事",
        DepartmentType.Interior => "内政",
        DepartmentType.Economy => "经济",
        DepartmentType.Science => "科技",
        DepartmentType.Diplomacy => "外交",
        _ => "未知部门",
    };

    private string BuildRejectionMessage(WorldState state, Order order, ResolveResultCode code)
    {
        switch (code)
        {
            case ResolveResultCode.RejectedInvalidActor:
            {
                var officer = state.GetOfficer(order.SecondaryTargetId);
                if (officer == null || !officer.IsAlive)
                {
                    return $"人才不存在或已不在世（Id={order.SecondaryTargetId}）。";
                }

                return $"「{officer.Name}」不是你的人，不能任命。";
            }

            case ResolveResultCode.RejectedInvalidTarget:
                return $"部门无效（{order.TargetId}）。合法值：0 军事、1 内政、2 经济、3 科技、4 外交。";

            default:
                return "任命指令被拒绝。";
        }
    }
}
