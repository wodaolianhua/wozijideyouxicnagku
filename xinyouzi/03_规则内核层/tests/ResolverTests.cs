// ============================================================================
//  六个结算器：成功路径 + 拒绝路径
// ============================================================================
//  验收标准：每个结算器至少一条成功路径 + 至少一条拒绝路径。
//  拒绝路径要断言「具体的结果码」而不只是「失败了」——
//  返回错误的码会让 UI 显示错误的提示，这比不拒绝更糟。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Resolvers;
using StrategyGame.Core.Sample;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>
/// 征兵结算器测试。
/// </summary>
public sealed class RecruitResolverTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>成功路径：产出资源扣减与兵源增加的 Delta，且至少一条事件。</summary>
    [Fact]
    public void Resolve_SufficientResources_ShouldSucceed()
    {
        var state = SampleData.CreateWorld();
        var resolver = new RecruitResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Recruit, target: 0, amount: 100);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.Success, result.Code);
        Assert.True(result.IsSuccess);

        // 银、粮扣减 + 兵源增加 + 人力计数，共 4 条
        Assert.Equal(4, result.Deltas.Count);
        Assert.NotEmpty(result.Events);

        StateDelta soldierDelta = result.Deltas.Single(
            d => d.Field == DeltaField.FactionSoldier);
        Assert.Equal(500, soldierDelta.OldValue);
        Assert.Equal(600, soldierDelta.NewValue);
    }

    /// <summary>拒绝路径：银不足 → RejectedNoResources。</summary>
    [Fact]
    public void Resolve_InsufficientSilver_ShouldRejectWithNoResources()
    {
        var state = SampleData.CreateWorld();
        state.GetFaction(0)!.Resources.Silver = 1;

        var resolver = new RecruitResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Recruit, target: 0, amount: 1000);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedNoResources, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Empty(result.Deltas);
    }

    /// <summary>拒绝路径：目标城池不存在 → RejectedInvalidTarget。</summary>
    [Fact]
    public void Resolve_NonExistentCity_ShouldRejectWithInvalidTarget()
    {
        var state = SampleData.CreateWorld();
        var resolver = new RecruitResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Recruit, target: 999, amount: 10);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInvalidTarget, result.Code);
        Assert.Empty(result.Deltas);
    }

    /// <summary>拒绝路径：在敌方城池征兵 → RejectedInvalidTarget。</summary>
    [Fact]
    public void Resolve_EnemyCity_ShouldRejectWithInvalidTarget()
    {
        var state = SampleData.CreateWorld();
        var resolver = new RecruitResolver(Config);

        // 城 1 属于魏（势力 1）
        var order = Order.CreatePlayerOrder(ActionType.Recruit, target: 1, amount: 10);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInvalidTarget, result.Code);
    }

    /// <summary>拒绝路径：本月人力耗尽 → RejectedInsufficientCapacity。</summary>
    [Fact]
    public void Resolve_CapacityExhausted_ShouldRejectWithInsufficientCapacity()
    {
        var state = SampleData.CreateWorld();
        state.ActionsThisMonth = Config.MaxOrdersPerFactionPerMonth;

        var resolver = new RecruitResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Recruit, target: 0, amount: 10);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInsufficientCapacity, result.Code);
    }

    /// <summary>拒绝路径：执行者不存在 → RejectedInvalidActor。</summary>
    [Fact]
    public void Resolve_NonExistentOfficerActor_ShouldRejectWithInvalidActor()
    {
        var state = SampleData.CreateWorld();
        var resolver = new RecruitResolver(Config);
        var order = Order.CreateOfficerOrder(ActionType.Recruit, officerId: 999, target: 0, amount: 10);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInvalidActor, result.Code);
    }
}

/// <summary>
/// 修城防结算器测试。
/// </summary>
public sealed class FortifyResolverTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>成功路径：城防提升并产出扣银 Delta。</summary>
    [Fact]
    public void Resolve_ValidOrder_ShouldIncreaseDefense()
    {
        var state = SampleData.CreateWorld();
        var resolver = new FortifyResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 100);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.Success, result.Code);
        Assert.NotEmpty(result.Events);

        StateDelta defenseDelta = result.Deltas.Single(d => d.Field == DeltaField.CityDefense);
        Assert.Equal(100, defenseDelta.OldValue);
        Assert.True(defenseDelta.NewValue > defenseDelta.OldValue);
    }

    /// <summary>城防不得超过上限。</summary>
    [Fact]
    public void Resolve_ShouldNotExceedMaxDefense()
    {
        var state = SampleData.CreateWorld();
        state.GetCity(0)!.Defense = 299;

        var resolver = new FortifyResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 100);

        var result = resolver.Resolve(state, order);

        StateDelta defenseDelta = result.Deltas.Single(d => d.Field == DeltaField.CityDefense);
        Assert.Equal(state.GetCity(0)!.MaxDefense, defenseDelta.NewValue);
    }

    /// <summary>拒绝路径：银不足 → RejectedNoResources。</summary>
    [Fact]
    public void Resolve_InsufficientSilver_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        state.GetFaction(0)!.Resources.Silver = 0;

        var resolver = new FortifyResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 1000);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedNoResources, result.Code);
    }

    /// <summary>拒绝路径：投入量为 0 → RejectedInvalidTarget。</summary>
    [Fact]
    public void Resolve_ZeroAmount_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        var resolver = new FortifyResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInvalidTarget, result.Code);
    }
}

/// <summary>
/// 屯田结算器测试。
/// </summary>
public sealed class DevelopResolverTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>成功路径：屯田度提升。</summary>
    [Fact]
    public void Resolve_ValidOrder_ShouldIncreaseFarmLevel()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DevelopResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Develop, target: 0, amount: 100);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.Success, result.Code);
        Assert.NotEmpty(result.Events);

        StateDelta farmDelta = result.Deltas.Single(d => d.Field == DeltaField.CityFarmLevel);
        Assert.True(farmDelta.NewValue > farmDelta.OldValue);
    }

    /// <summary>拒绝路径：银不足 → RejectedNoResources。</summary>
    [Fact]
    public void Resolve_InsufficientSilver_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        state.GetFaction(0)!.Resources.Silver = 1;

        var resolver = new DevelopResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Develop, target: 0, amount: 500);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedNoResources, result.Code);
    }

    /// <summary>拒绝路径：在敌方城池屯田 → RejectedInvalidTarget。</summary>
    [Fact]
    public void Resolve_EnemyCity_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DevelopResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Develop, target: 2, amount: 50);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInvalidTarget, result.Code);
    }
}

/// <summary>
/// 出兵结算器测试。
/// </summary>
public sealed class DispatchResolverTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>成功路径：进攻敌方城池，触发战斗结算。</summary>
    /// <remarks>
    /// 【语义变更】战斗系统落地后，出兵不再只是「移动 + 标记 besieged」，
    /// 而是完整跑一遍攻城战。守军被大量弱攻时会被打光，城池直接易主，
    /// 因此本例改为断言城池易主而非进入围城状态。
    /// </remarks>
    [Fact]
    public void Resolve_ToEnemyCity_ShouldResolveBattle()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Config);

        // 洛阳（0 owned by 玩家）→ 长安（1 owned by 魏），0 与 1 相邻
        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 300, secondary: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.Success, result.Code);
        Assert.NotEmpty(result.Events);

        // 守军仅 250 人，300 人猛攻足以打光 → 攻陷
        Assert.Contains(result.Deltas, d =>
            d.TargetType == DeltaTarget.City
            && d.TargetId == 1
            && d.Field == DeltaField.CityOwnerFactionId);

        // 投入 300 人，战斗后存活 = 300 − 战损，不得凭空消失
        StateDelta soldierDelta = result.Deltas.Single(
            d => d.TargetType == DeltaTarget.Army
                 && d.TargetId == 0
                 && d.Field == DeltaField.ArmySoldiers);

        Assert.Equal(300, soldierDelta.OldValue);
        Assert.InRange(soldierDelta.NewValue, 1, 300);
    }

    /// <summary>成功路径：向自家城池调兵，进入移动状态。</summary>
    [Fact]
    public void Resolve_ToOwnCity_ShouldMove()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Config);

        // 洛阳（0）→ 宛城（3），两者同属玩家且相邻
        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 3, amount: 50, secondary: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.Success, result.Code);

        StateDelta stanceDelta = result.Deltas.Single(d => d.Field == DeltaField.ArmyStance);
        Assert.Equal((int)ArmyStance.Moving, stanceDelta.NewValue);
    }

    /// <summary>拒绝路径：目标不相邻 → RejectedNotAdjacent。</summary>
    [Fact]
    public void Resolve_NotAdjacent_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Config);

        // 洛阳（0）与邯郸（2）不相邻
        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 2, amount: 50, secondary: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedNotAdjacent, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    /// <summary>
    /// 拒绝路径：目标已被围困 → RejectedTargetSieged（契约 v1.3 新增独立结果码）。
    /// </summary>
    /// <remarks>
    /// 【语义变更】战斗系统落地后，「目标城内有敌军」不再是拒绝理由——
    /// 那正是攻城战的正常场景。真正的拒绝条件是「已被围困」，
    /// 即已有部队处于 Besieging 状态。故本例让敌军处于围城姿态。
    /// 围困是临时状态（等下月），区别于永久性的 RejectedTargetOccupied。
    /// </remarks>
    [Fact]
    public void Resolve_UnderSiege_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Config);

        // 宛城（3）里放一支正在围城的敌军，使其处于「已被围困」状态
        state.Armies.Add(new Army
        {
            Id = 9, OwnerFactionId = 1, OfficerId = 2,
            Soldiers = 100, Morale = 50, MoraleMax = 100,
            PositionCityId = 3, TargetCityId = 3, Stance = ArmyStance.Besieging,
        });

        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 3, amount: 50, secondary: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedTargetSieged, result.Code);
    }

    /// <summary>拒绝路径：兵力超过现有 → RejectedNoResources。</summary>
    [Fact]
    public void Resolve_ExceedsSoldiers_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 99999, secondary: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedNoResources, result.Code);
    }
}

/// <summary>
/// 任命人才结算器测试。
/// </summary>
public sealed class AssignPostResolverTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>成功路径：人才获得岗位。</summary>
    [Fact]
    public void Resolve_ValidOfficerAndCity_ShouldAssignPost()
    {
        var state = SampleData.CreateWorld();
        var resolver = new AssignPostResolver(Config);

        // 张良（0）属玩家，洛阳（0）属玩家
        var order = Order.CreatePlayerOrder(ActionType.AssignPost, target: 0, secondary: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.Success, result.Code);
        Assert.NotEmpty(result.Events);

        StateDelta postDelta = result.Deltas.Single(d => d.Field == DeltaField.OfficerPost);
        Assert.Equal((int)PostType.None, postDelta.OldValue);

        // 张良专长为外交 → 斥候
        Assert.Equal((int)PostType.Scoutmaster, postDelta.NewValue);
    }

    /// <summary>拒绝路径：人才不存在 → RejectedInvalidTarget。</summary>
    [Fact]
    public void Resolve_NonExistentOfficer_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        var resolver = new AssignPostResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.AssignPost, target: 999, secondary: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInvalidTarget, result.Code);
    }

    /// <summary>拒绝路径：任命他势力人才 → RejectedInvalidTarget。</summary>
    [Fact]
    public void Resolve_ForeignOfficer_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        var resolver = new AssignPostResolver(Config);

        // 韩信（2）属魏（势力 1）
        var order = Order.CreatePlayerOrder(ActionType.AssignPost, target: 2, secondary: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInvalidTarget, result.Code);
    }

    /// <summary>拒绝路径：岗位已被占（一城一岗）→ RejectedTargetOccupied。</summary>
    [Fact]
    public void Resolve_PostAlreadyOccupied_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        var resolver = new AssignPostResolver(Config);

        // 先让萧何（1）占住洛阳
        state.GetOfficer(1)!.Post = PostType.CityGovernor;
        state.GetOfficer(1)!.PostCityId = 0;

        var order = Order.CreatePlayerOrder(ActionType.AssignPost, target: 0, secondary: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedTargetOccupied, result.Code);
    }

    /// <summary>拒绝路径：人才已阵亡 → RejectedInvalidActor。</summary>
    [Fact]
    public void Resolve_DeadOfficer_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        state.GetOfficer(0)!.IsAlive = false;

        var resolver = new AssignPostResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.AssignPost, target: 0, secondary: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInvalidActor, result.Code);
    }
}

/// <summary>
/// 休整结算器测试。
/// </summary>
public sealed class RestResolverTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>成功路径：士气恢复并标记本月已休整。</summary>
    [Fact]
    public void Resolve_ValidArmy_ShouldRestoreMorale()
    {
        var state = SampleData.CreateWorld();
        var resolver = new RestResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Rest, target: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.Success, result.Code);
        Assert.NotEmpty(result.Events);

        StateDelta moraleDelta = result.Deltas.Single(d => d.Field == DeltaField.ArmyMorale);
        Assert.True(moraleDelta.NewValue > moraleDelta.OldValue);

        StateDelta restDelta = result.Deltas.Single(d => d.Field == DeltaField.ArmyHasRested);
        Assert.Equal(1, restDelta.NewValue);
    }

    /// <summary>士气不得超过上限。</summary>
    [Fact]
    public void Resolve_ShouldNotExceedMoraleMax()
    {
        var state = SampleData.CreateWorld();
        state.GetArmy(0)!.Morale = 99;

        var resolver = new RestResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Rest, target: 0);

        var result = resolver.Resolve(state, order);

        StateDelta moraleDelta = result.Deltas.Single(d => d.Field == DeltaField.ArmyMorale);
        Assert.Equal(state.GetArmy(0)!.MoraleMax, moraleDelta.NewValue);
    }

    /// <summary>拒绝路径：本月已休整过 → RejectedCooldown。</summary>
    [Fact]
    public void Resolve_AlreadyRestedThisMonth_ShouldRejectWithCooldown()
    {
        var state = SampleData.CreateWorld();
        state.GetArmy(0)!.HasRestedThisMonth = true;

        var resolver = new RestResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Rest, target: 0);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedCooldown, result.Code);
    }

    /// <summary>拒绝路径：部队不存在 → RejectedInvalidTarget。</summary>
    [Fact]
    public void Resolve_NonExistentArmy_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        var resolver = new RestResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Rest, target: 999);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInvalidTarget, result.Code);
    }

    /// <summary>拒绝路径：指挥他势力部队 → RejectedInvalidTarget。</summary>
    [Fact]
    public void Resolve_EnemyArmy_ShouldReject()
    {
        var state = SampleData.CreateWorld();
        var resolver = new RestResolver(Config);
        var order = Order.CreatePlayerOrder(ActionType.Rest, target: 1);

        var result = resolver.Resolve(state, order);

        Assert.Equal(ResolveResultCode.RejectedInvalidTarget, result.Code);
    }
}
