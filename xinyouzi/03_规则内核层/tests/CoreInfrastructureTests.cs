// ============================================================================
//  注册表 / 写回器 / 时钟 / 指令处理器 测试
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Config;
using StrategyGame.Core.Sample;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>
/// 结算器注册表测试。重点验证「未注册行动类型返回拒绝码而非抛异常」。
/// </summary>
public sealed class ResolverRegistryTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>默认注册表应注册全部六个原型行动。</summary>
    [Fact]
    public void CreateDefault_ShouldRegisterAllProtoActions()
    {
        var registry = ResolverRegistry.CreateDefault(Config);

        Assert.NotNull(registry.Get(ActionType.Recruit));
        Assert.NotNull(registry.Get(ActionType.Fortify));
        Assert.NotNull(registry.Get(ActionType.Develop));
        Assert.NotNull(registry.Get(ActionType.Dispatch));
        Assert.NotNull(registry.Get(ActionType.AssignPost));
        Assert.NotNull(registry.Get(ActionType.Rest));
    }

    /// <summary>每个结算器声明的 Action 必须与注册键一致。</summary>
    [Fact]
    public void Register_ShouldMatchResolverAction()
    {
        var registry = ResolverRegistry.CreateDefault(Config);

        foreach (ActionType action in new[]
        {
            ActionType.Recruit, ActionType.Fortify, ActionType.Develop,
            ActionType.Dispatch, ActionType.AssignPost, ActionType.Rest,
        })
        {
            Assert.Equal(action, registry.Get(action)!.Action);
        }
    }

    /// <summary>未注册的 ActionType 返回拒绝码，绝不抛异常。</summary>
    [Fact]
    public void Dispatch_UnregisteredAction_ShouldRejectWithoutThrowing()
    {
        var registry = ResolverRegistry.CreateDefault(Config);
        var state = SampleData.CreateWorld();

        // RecruitOfficer 是P1 保留位，骨架阶段不实现
        var order = Order.CreatePlayerOrder(ActionType.RecruitOfficer, target: 0);

        var result = registry.Dispatch(state, order);

        Assert.Equal(ResolveResultCode.RejectedNotImplemented, result.Code);
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Empty(result.Deltas);
    }

    /// <summary>未注册时Get 返回 null 而非抛异常。</summary>
    [Fact]
    public void Get_UnregisteredAction_ShouldReturnNull()
    {
        var registry = ResolverRegistry.CreateDefault(Config);
        Assert.Null(registry.Get(ActionType.Siege));
    }

    /// <summary>分发不得修改 state。</summary>
    [Fact]
    public void Dispatch_ShouldNotMutateState()
    {
        var registry = ResolverRegistry.CreateDefault(Config);
        var state = SampleData.CreateWorld();
        var pristine = state.Clone();

        registry.Dispatch(state, Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 100));

        Assert.Null(StateSnapshot.Diff(pristine, state));
    }
}

/// <summary>
/// 状态写回器测试。
/// </summary>
public sealed class StateApplierTests
{
    /// <summary>应能写回顶层 int 字段。</summary>
    [Fact]
    public void Apply_ShouldWriteCityField()
    {
        var state = SampleData.CreateWorld();
        var applier = new StateApplier();

        applier.Apply(state, new List<StateDelta>
        {
            new() { TargetType = DeltaTarget.City, TargetId = 0, Field = DeltaField.CityDefense, OldValue = 100, NewValue = 250 },
        });

        Assert.Equal(250, state.GetCity(0)!.Defense);
    }

    /// <summary>应能写回嵌套字段（Resources.Grain）。</summary>
    [Fact]
    public void Apply_ShouldWriteNestedResourceField()
    {
        var state = SampleData.CreateWorld();
        var applier = new StateApplier();

        applier.Apply(state, new List<StateDelta>
        {
            new() { TargetType = DeltaTarget.Faction, TargetId = 1, Field = DeltaField.FactionGrain, OldValue = 4000, NewValue = 1234 },
        });

        Assert.Equal(1234, state.GetFaction(1)!.Resources.Grain);
    }

    /// <summary>应能把枚举字段写为 int。</summary>
    [Fact]
    public void Apply_ShouldWriteEnumField()
    {
        var state = SampleData.CreateWorld();
        var applier = new StateApplier();

        applier.Apply(state, new List<StateDelta>
        {
            new() { TargetType = DeltaTarget.Army, TargetId = 0, Field = DeltaField.ArmyStance, OldValue = 0, NewValue = (int)ArmyStance.Besieging },
        });

        Assert.Equal(ArmyStance.Besieging, state.GetArmy(0)!.Stance);
    }

    /// <summary>应能把 bool 字段按 0/1 写回。</summary>
    [Fact]
    public void Apply_ShouldWriteBooleanFieldFromInt()
    {
        var state = SampleData.CreateWorld();
        var applier = new StateApplier();

        applier.Apply(state, new List<StateDelta>
        {
            new() { TargetType = DeltaTarget.Army, TargetId = 0, Field = DeltaField.ArmyHasRested, OldValue = 0, NewValue = 1 },
        });

        Assert.True(state.GetArmy(0)!.HasRestedThisMonth);
    }

    /// <summary>多条Delta 应按顺序生效，后者能看到前者。</summary>
    [Fact]
    public void Apply_ShouldProcessDeltasInOrder()
    {
        var state = SampleData.CreateWorld();
        var applier = new StateApplier();

        applier.Apply(state, new List<StateDelta>
        {
            new() { TargetType = DeltaTarget.City, TargetId = 0, Field = DeltaField.CityDefense, NewValue = 150 },
            new() { TargetType = DeltaTarget.City, TargetId = 0, Field = DeltaField.CityDefense, NewValue = 175 },
        });

        Assert.Equal(175, state.GetCity(0)!.Defense);
    }

    /// <summary>非法的 TargetType 必须抛异常，不得静默忽略。</summary>
    [Fact]
    public void Apply_InvalidTargetType_ShouldThrow()
    {
        var state = SampleData.CreateWorld();
        var applier = new StateApplier();

        Assert.Throws<InvalidOperationException>(() => applier.Apply(state, new List<StateDelta>
        {
            new() { TargetType = "Planet", TargetId = 0, Field = "Defense", NewValue = 1 },
        }));
    }

    /// <summary>目标 Id 不存在必须抛异常，不得静默跳过。</summary>
    [Fact]
    public void Apply_NonExistentTarget_ShouldThrow()
    {
        var state = SampleData.CreateWorld();
        var applier = new StateApplier();

        Assert.Throws<InvalidOperationException>(() => applier.Apply(state, new List<StateDelta>
        {
            new() { TargetType = DeltaTarget.City, TargetId = 999, Field = DeltaField.CityDefense, NewValue = 1 },
        }));
    }

    /// <summary>字段名不存在必须抛异常。</summary>
    [Fact]
    public void Apply_UnknownField_ShouldThrow()
    {
        var state = SampleData.CreateWorld();
        var applier = new StateApplier();

        Assert.Throws<InvalidOperationException>(() => applier.Apply(state, new List<StateDelta>
        {
            new() { TargetType = DeltaTarget.City, TargetId = 0, Field = "NoSuchField", NewValue = 1 },
        }));
    }
}

/// <summary>
/// 时间推进测试。
/// </summary>
public sealed class GameClockTests
{
    /// <summary>普通月份：只加月。</summary>
    [Fact]
    public void AdvanceMonth_OrdinaryMonth_ShouldIncrementMonthOnly()
    {
        var state = SampleData.CreateWorld();
        state.Year = 1;
        state.Month = 5;

        bool crossedYear = GameClock.AdvanceMonth(state);

        Assert.False(crossedYear);
        Assert.Equal(1, state.Year);
        Assert.Equal(6, state.Month);
    }

    /// <summary>12 月推进：年+1、月归 1。</summary>
    [Fact]
    public void AdvanceMonth_December_ShouldRollOverToNextYear()
    {
        var state = SampleData.CreateWorld();
        state.Year = 1;
        state.Month = 12;

        bool crossedYear = GameClock.AdvanceMonth(state);

        Assert.True(crossedYear);
        Assert.Equal(2, state.Year);
        Assert.Equal(1, state.Month);
    }

    /// <summary>新的一月应清空本月指令计数（契约 v1.5：按势力分账字典整体重建）。</summary>
    [Fact]
    public void AdvanceMonth_ShouldResetMonthlyActionCounter()
    {
        var state = SampleData.CreateWorld();
        state.ActionsThisMonthByFaction[0] = 4;
        state.ActionsThisMonthByFaction[2] = 9;

        GameClock.AdvanceMonth(state);

        Assert.Empty(state.ActionsThisMonthByFaction);
    }

    /// <summary>年+月与单调整数的互转应可逆。</summary>
    [Fact]
    public void TotalMonths_RoundTrip_ShouldBeReversible()
    {
        foreach (int year in new[] { 1, 2, 10, 999 })
        {
            foreach (int month in new[] { 1, 6, 12 })
            {
                int total = GameClock.TotalMonths(year, month);
                var (y, m) = GameClock.FromTotalMonths(total);

                Assert.Equal(year, y);
                Assert.Equal(month, m);
            }
        }
    }

    /// <summary>只应取出本月到期的延迟指令。</summary>
    [Fact]
    public void GetDueOrders_ShouldOnlyReturnCurrentMonth()
    {
        var state = SampleData.CreateWorld();
        state.Year = 1;
        state.Month = 3;

        state.PendingOrders.Add(new PendingOrder
        {
            OrderId = 1, Action = ActionType.Rest, TargetId = 0,
            ExecuteYear = 1, ExecuteMonth = 3,
        });
        state.PendingOrders.Add(new PendingOrder
        {
            OrderId = 2, Action = ActionType.Rest, TargetId = 0,
            ExecuteYear = 1, ExecuteMonth = 4,
        });

        var due = GameClock.GetDueOrders(state);

        Assert.Single(due);
        Assert.Equal(1, due[0].OrderId);
    }

    /// <summary>取出到期指令后应能从队列移除。</summary>
    [Fact]
    public void RemoveDueOrders_ShouldRemoveOnlyDueOnes()
    {
        var state = SampleData.CreateWorld();
        state.Year = 1;
        state.Month = 3;

        state.PendingOrders.Add(new PendingOrder
        {
            OrderId = 1, Action = ActionType.Rest, ExecuteYear = 1, ExecuteMonth = 3,
        });
        state.PendingOrders.Add(new PendingOrder
        {
            OrderId = 2, Action = ActionType.Rest, ExecuteYear = 1, ExecuteMonth = 5,
        });

        int removed = GameClock.RemoveDueOrders(state);

        Assert.Equal(1, removed);
        Assert.Single(state.PendingOrders);
        Assert.Equal(2, state.PendingOrders[0].OrderId);
    }
}

/// <summary>
/// 指令处理器测试。
/// </summary>
public sealed class CommandProcessorTests
{
    private static readonly BalanceConfig Config = SampleData.CreateBalance();

    /// <summary>批量处理后，成功指令应已写回状态。</summary>
    [Fact]
    public void ProcessAll_ShouldApplySuccessfulOrders()
    {
        var state = SampleData.CreateWorld();
        var registry = ResolverRegistry.CreateDefault(Config);
        var processor = new CommandProcessor(registry, new StateApplier());

        int defenseBefore = state.GetCity(0)!.Defense;

        var results = processor.ProcessAll(state, new List<Order>
        {
            Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 100),
        });

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.True(state.GetCity(0)!.Defense > defenseBefore);
        Assert.Single(state.EventLog);
    }

    /// <summary>失败的指令不得写回任何状态。</summary>
    [Fact]
    public void ProcessAll_FailedOrder_ShouldNotMutateState()
    {
        var state = SampleData.CreateWorld();
        var registry = ResolverRegistry.CreateDefault(Config);
        var processor = new CommandProcessor(registry, new StateApplier());
        var pristine = state.Clone();

        var results = processor.ProcessAll(state, new List<Order>
        {
            Order.CreatePlayerOrder(ActionType.Recruit, target: 999, amount: 10),
        });

        Assert.False(results[0].IsSuccess);
        Assert.Null(StateSnapshot.Diff(pristine, state));
    }

    /// <summary>返回结果顺序必须与传入顺序一致。</summary>
    [Fact]
    public void ProcessAll_ShouldPreserveOrder()
    {
        var state = SampleData.CreateWorld();
        var registry = ResolverRegistry.CreateDefault(Config);
        var processor = new CommandProcessor(registry, new StateApplier());

        var orders = new List<Order>
        {
            Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 10),
            Order.CreatePlayerOrder(ActionType.Develop, target: 0, amount: 10),
            Order.CreatePlayerOrder(ActionType.Rest, target: 0),
        };

        var results = processor.ProcessAll(state, orders);

        Assert.Equal(3, results.Count);
        Assert.Equal(ActionType.Fortify, orders[0].Action);
        Assert.Equal(ActionType.Develop, orders[1].Action);
        Assert.Equal(ActionType.Rest, orders[2].Action);
    }

    /// <summary>人力上限应阻止第 6 条指令。</summary>
    [Fact]
    public void ProcessAll_ShouldEnforceMonthlyCapacity()
    {
        var state = SampleData.CreateWorld();
        var registry = ResolverRegistry.CreateDefault(Config);
        var processor = new CommandProcessor(registry, new StateApplier());

        var orders = Enumerable.Range(0, 8)
            .Select(_ => Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 1))
            .ToList();

        var results = processor.ProcessAll(state, orders);

        int successCount = results.Count(r => r.IsSuccess);
        Assert.Equal(Config.MaxOrdersPerFactionPerMonth, successCount);

        // 超出的部分必须明确拒绝，不能静默忽略。
        Assert.Contains(
            results,
            r => r.Code == ResolveResultCode.RejectedInsufficientCapacity);
    }
}

/// <summary>
/// 配置校验器测试。
/// </summary>
public sealed class ConfigValidatorTests
{
    /// <summary>示例数据本身必须通过校验。</summary>
    [Fact]
    public void Validate_SampleData_ShouldPass()
    {
        var issues = ConfigValidator.Validate(
            SampleData.CreateBalance(),
            new List<OfficerTemplate>(),
            new List<CityTemplate>(),
            SampleData.CreateEvents());

        Assert.Empty(issues);
    }

    /// <summary>负数系数必须报错。</summary>
    [Fact]
    public void Validate_NegativeValue_ShouldReportError()
    {
        var balance = SampleData.CreateBalance();
        balance.GrainPerPopPerMonth = -5;

        var issues = ConfigValidator.Validate(
            balance, new List<OfficerTemplate>(), new List<CityTemplate>(), new List<GameEventTemplate>());

        Assert.Contains(issues, i =>
            i.Location == "GrainPerPopPerMonth" && i.Severity == ConfigIssueSeverity.Error);
    }

    /// <summary>人才属性越界必须报错。</summary>
    [Fact]
    public void Validate_OfficerAttributeOutOfRange_ShouldReportError()
    {
        var officers = new List<OfficerTemplate>
        {
            new() { Id = 0, Name = "测试", Command = 150, Governance = 50, Loyalty = 50, Age = 30 },
        };

        var issues = ConfigValidator.Validate(
            SampleData.CreateBalance(), officers, new List<CityTemplate>(), new List<GameEventTemplate>());

        Assert.Contains(issues, i =>
            i.Location == "Officer[0].Command" && i.Severity == ConfigIssueSeverity.Error);
    }

    /// <summary>事件概率越界必须报错。</summary>
    [Fact]
    public void Validate_EventProbabilityOutOfRange_ShouldReportError()
    {
        var events = new List<GameEventTemplate>
        {
            new() { Id = 1, Category = EventCategory.Economy, Probability = 15_000, Message = "越界" },
        };

        var issues = ConfigValidator.Validate(
            SampleData.CreateBalance(), new List<OfficerTemplate>(), new List<CityTemplate>(), events);

        Assert.Contains(issues, i => i.Location == "Event[1]" && i.Severity == ConfigIssueSeverity.Error);
    }

    /// <summary>同类事件概率之和不得超过 60。</summary>
    [Fact]
    public void Validate_CategoryProbabilitySumExceeded_ShouldReportError()
    {
        var events = new List<GameEventTemplate>
        {
            new() { Id = 1, Category = EventCategory.Economy, Probability = 4000, Message = "a" },
            new() { Id = 2, Category = EventCategory.Economy, Probability = 3000, Message = "b" },
        };

        var issues = ConfigValidator.Validate(
            SampleData.CreateBalance(), new List<OfficerTemplate>(), new List<CityTemplate>(), events);

        Assert.Contains(issues, i =>
            i.Location.Contains("EventCategory") && i.Severity == ConfigIssueSeverity.Error);
    }

    /// <summary>相邻关系不对称必须报错。</summary>
    [Fact]
    public void Validate_AsymmetricAdjacency_ShouldReportError()
    {
        var cities = new List<CityTemplate>
        {
            new() { Id = 0, Name = "甲", FactionId = 0, Population = 100, AdjacentCityIds = new List<int> { 1 } },
            new() { Id = 1, Name = "乙", FactionId = 1, Population = 100, AdjacentCityIds = new List<int>() },
        };

        var issues = ConfigValidator.Validate(
            SampleData.CreateBalance(), new List<OfficerTemplate>(), cities, new List<GameEventTemplate>());

        Assert.Contains(issues, i =>
            i.Location == "City[0]" && i.Severity == ConfigIssueSeverity.Error);
    }

    /// <summary>引用不存在的城池必须报错。</summary>
    [Fact]
    public void Validate_DanglingCityReference_ShouldReportError()
    {
        var cities = new List<CityTemplate>
        {
            new() { Id = 0, Name = "甲", FactionId = 0, Population = 100, AdjacentCityIds = new List<int> { 77 } },
        };

        var issues = ConfigValidator.Validate(
            SampleData.CreateBalance(), new List<OfficerTemplate>(), cities, new List<GameEventTemplate>());

        Assert.Contains(issues, i => i.Location == "City[0]" && i.Severity == ConfigIssueSeverity.Error);
    }

    /// <summary>校验器不得抛异常——必须一次性返回全部问题。</summary>
    [Fact]
    public void Validate_ShouldNeverThrow()
    {
        var balance = SampleData.CreateBalance();
        balance.BaseRecruitCostSilver = -1;
        balance.MaxOrdersPerFactionPerMonth = 0;

        var officers = new List<OfficerTemplate>
        {
            new() { Id = -1, Name = "", Command = 999, Governance = -5, Loyalty = 0, Age = 0 },
        };

        var cities = new List<CityTemplate>
        {
            new() { Id = -1, Name = "", FactionId = -1, Population = -10, AdjacentCityIds = new List<int> { 5 } },
        };

        var events = new List<GameEventTemplate>
        {
            new() { Id = 0, Category = EventCategory.Economy, Probability = -1, Message = "" },
        };

        var issues = ConfigValidator.Validate(balance, officers, cities, events);

        // 应返回多条问题，而不是在第一处就抛异常。
        Assert.True(issues.Count >= 8, $"应至少报出 8 个问题，实际 {issues.Count} 个。");
    }

    // ========================================================================
    //  v1.2 新增的 3 条校验规则
    // ========================================================================

    /// <summary>
    /// 新增规则 1：<c>BaseKill</c> 必须存在且 &gt; 0。
    /// </summary>
    /// <remarks>
    /// 【为什么是错误而不是警告】BaseKill ≤ 0 时 killRate 恒为 0，
    /// 守军永远打不光，「攻陷」成为<b>数学上不可达</b>的分支——
    /// 游戏会退化成「永远打不下任何城」。这类配置必须阻断开局。
    /// </remarks>
    [Fact]
    public void Validate_BaseKillMissing_ShouldReportError()
    {
        foreach (var bad in new[] { 0, -1 })
        {
            var balance = SampleData.CreateBalance();
            balance.BaseKill = bad;

            var issues = ConfigValidator.Validate(
                balance,
                new List<OfficerTemplate>(),
                new List<CityTemplate>(),
                new List<GameEventTemplate>());

            Assert.Contains(issues, i =>
                i.Location == "BaseKill" && i.Severity == ConfigIssueSeverity.Error);
        }
    }

    /// <summary><c>BaseKill</c> 超过万分比上限（&gt;100%）也必须报错。</summary>
    [Fact]
    public void Validate_BaseKillAboveScale_ShouldReportError()
    {
        var balance = SampleData.CreateBalance();
        balance.BaseKill = ConfigValidator.ProbabilityScale + 1;

        var issues = ConfigValidator.Validate(
            balance, new List<OfficerTemplate>(), new List<CityTemplate>(), new List<GameEventTemplate>());

        Assert.Contains(issues, i =>
            i.Location == "BaseKill" && i.Severity == ConfigIssueSeverity.Error);
    }

    /// <summary>
    /// 新增规则 2：废弃字段 <c>DefenderLossFactor</c> / <c>StalemateLossRate</c>
    /// 只告警，不阻断。
    /// </summary>
    /// <remarks>
    /// 【为什么不阻断】这两个字段留在契约里只为兼容旧 config.json。
    /// 它们已不参与计算，值残留不会让结算出错，只会让开发者困惑；
    /// 若升级就报错，旧存档直接无法加载，代价远大于收益。
    /// 因此必须<b>恰好是 Warning</b>，且不能出现 Error。
    /// </remarks>
    [Fact]
    public void Validate_DeprecatedFields_ShouldWarnOnly()
    {
        var balance = SampleData.CreateBalance();
        balance.DefenderLossFactor = 60;
        balance.StalemateLossRate = 10;

        var issues = ConfigValidator.Validate(
            balance, new List<OfficerTemplate>(), new List<CityTemplate>(), new List<GameEventTemplate>());

        Assert.Contains(issues, i =>
            i.Location == "DefenderLossFactor" && i.Severity == ConfigIssueSeverity.Warning);

        Assert.Contains(issues, i =>
            i.Location == "StalemateLossRate" && i.Severity == ConfigIssueSeverity.Warning);

        // 关键：不能同时报 Error，否则旧配置会被阻断。
        Assert.DoesNotContain(issues, i =>
            (i.Location == "DefenderLossFactor" || i.Location == "StalemateLossRate")
            && i.Severity == ConfigIssueSeverity.Error);
    }

    /// <summary>废弃字段为 0 时不应产生噪音告警。</summary>
    [Fact]
    public void Validate_DeprecatedFieldsZero_ShouldNotWarn()
    {
        var issues = ConfigValidator.Validate(
            SampleData.CreateBalance(),
            new List<OfficerTemplate>(),
            new List<CityTemplate>(),
            new List<GameEventTemplate>());

        Assert.DoesNotContain(issues, i =>
            i.Location is "DefenderLossFactor" or "StalemateLossRate");
    }

    /// <summary>
    /// 新增规则 3：8 个士气字段必须在合理范围。
    /// </summary>
    /// <remarks>
    /// 【刻度陷阱】7 个是<b>绝对点数</b>（0-100），只有
    /// <c>MutinyRoutRate</c> 是<b>万分比</b>。若把万分比项也按 0-100 校验，
    /// 3000（30%）会被误判越界；反之把点数项按万分比校验则完全失效。
    /// </remarks>
    [Fact]
    public void Validate_MoralePointsOutOfRange_ShouldReportError()
    {
        var pointFields = new Action<BalanceConfig>[]
        {
            b => b.MoraleDecayPerMonth = 0,
            b => b.MoraleDecayNoBattleThreshold = 0,
            b => b.MoraleDecayNoBattlePerMonth = 0,
            b => b.MoraleGainOnCapture = 0,
            b => b.MoraleLossOnRepel = 0,
            b => b.MoraleLossOnStalemate = 0,
            b => b.MoraleLossOnDefend = 0,
        };

        var names = new[]
        {
            "MoraleDecayPerMonth", "MoraleDecayNoBattleThreshold", "MoraleDecayNoBattlePerMonth",
            "MoraleGainOnCapture", "MoraleLossOnRepel", "MoraleLossOnStalemate", "MoraleLossOnDefend",
        };

        for (int i = 0; i < pointFields.Length; i++)
        {
            var balance = SampleData.CreateBalance();
            pointFields[i](balance);

            var issues = ConfigValidator.Validate(
                balance,
                new List<OfficerTemplate>(),
                new List<CityTemplate>(),
                new List<GameEventTemplate>());

            // Location 统一带 "BalanceConfig." 前缀，便于人工定位所属配置块。
            Assert.Contains(issues, issue =>
                issue.Location.EndsWith(names[i], StringComparison.Ordinal)
                && issue.Severity == ConfigIssueSeverity.Error);
        }
    }

    /// <summary>士气点数超过 100（士气量纲上限）必须报错。</summary>
    [Fact]
    public void Validate_MoralePointsAboveMax_ShouldReportError()
    {
        var balance = SampleData.CreateBalance();
        balance.MoraleGainOnCapture = ConfigValidator.MoralePointMax + 1;

        var issues = ConfigValidator.Validate(
            balance, new List<OfficerTemplate>(), new List<CityTemplate>(), new List<GameEventTemplate>());

        Assert.Contains(issues, i =>
            i.Location.EndsWith("MoraleGainOnCapture", StringComparison.Ordinal)
            && i.Severity == ConfigIssueSeverity.Error);
    }

    /// <summary>
    /// <c>MutinyRoutRate</c> 是唯一的万分比项：3000合法（30%），
    /// 超过 10000 才是错误。把它当 0-100 的点数项校验是错的。
    /// </summary>
    [Fact]
    public void Validate_MutinyRoutRate_ShouldUseTenThousandScale()
    {
        var ok = SampleData.CreateBalance();
        ok.MutinyRoutRate = 3000;

        var okIssues = ConfigValidator.Validate(
            ok, new List<OfficerTemplate>(), new List<CityTemplate>(), new List<GameEventTemplate>());

        Assert.DoesNotContain(okIssues, i => i.Location == "MutinyRoutRate");

        var bad = SampleData.CreateBalance();
        bad.MutinyRoutRate = ConfigValidator.ProbabilityScale + 1;

        var badIssues = ConfigValidator.Validate(
            bad, new List<OfficerTemplate>(), new List<CityTemplate>(), new List<GameEventTemplate>());

        Assert.Contains(badIssues, i =>
            i.Location == "MutinyRoutRate" && i.Severity == ConfigIssueSeverity.Error);
    }

    /// <summary>
    /// 击退阈值必须高于基础战损，否则任何攻城都会被判击退。
    /// </summary>
    [Fact]
    public void Validate_ThresholdNotAboveBaseLoss_ShouldReportError()
    {
        var balance = SampleData.CreateBalance();
        balance.SiegeThresholdLoss = balance.BaseLoss;

        var issues = ConfigValidator.Validate(
            balance, new List<OfficerTemplate>(), new List<CityTemplate>(), new List<GameEventTemplate>());

        Assert.Contains(issues, i =>
            i.Location == "SiegeThresholdLoss" && i.Severity == ConfigIssueSeverity.Error);
    }
}

/// <summary>
/// 内存配置加载器测试。
/// </summary>
public sealed class ConfigLoaderTests
{
    /// <summary>内存加载器应原样返回配置。</summary>
    [Fact]
    public void InMemoryConfigLoader_ShouldReturnProvidedValues()
    {
        var balance = SampleData.CreateBalance();
        var loader = new InMemoryConfigLoader(
            balance, SampleOfficers(), SampleCities(), SampleData.CreateEvents());

        Assert.Same(balance, loader.LoadBalance());
        Assert.Equal(2, loader.LoadOfficers().Count);
        Assert.Equal(2, loader.LoadCities().Count);
        Assert.NotEmpty(loader.LoadEvents());
    }

    /// <summary>示例城池模板必须自洽（对称、Id 唯一）。</summary>
    [Fact]
    public void SampleCityTemplates_ShouldBeSymmetric()
    {
        var cities = SampleCities();
        var ids = cities.Select(c => c.Id).ToHashSet();

        Assert.Equal(cities.Count, ids.Count);

        foreach (var city in cities)
        {
            foreach (int neighbor in city.AdjacentCityIds)
            {
                Assert.Contains(neighbor, ids);
                CityTemplate back = cities.Single(c => c.Id == neighbor);
                Assert.Contains(city.Id, back.AdjacentCityIds);
            }
        }
    }

    /// <summary>示例人才模板必须自洽。</summary>
    [Fact]
    public void SampleOfficerTemplates_ShouldHaveValidAttributes()
    {
        var officers = SampleOfficers();
        var ids = officers.Select(o => o.Id).ToHashSet();

        Assert.Equal(officers.Count, ids.Count);

        foreach (var officer in officers)
        {
            Assert.InRange(officer.Command, 0, 100);
            Assert.InRange(officer.Governance, 0, 100);
            Assert.InRange(officer.Loyalty, 0, 100);
            Assert.True(officer.Age > 0);
        }
    }

    private static List<OfficerTemplate> SampleOfficers()
        => new()
        {
            new OfficerTemplate { Id = 0, Name = "甲", Command = 70, Governance = 80, Loyalty = 90, Age = 40, Specialty = OfficerSpecialty.Agriculture },
            new OfficerTemplate { Id = 1, Name = "乙", Command = 60, Governance = 70, Loyalty = 80, Age = 35, Specialty = OfficerSpecialty.Siege },
        };

    private static List<CityTemplate> SampleCities()
        => new()
        {
            new CityTemplate { Id = 0, Name = "甲城", FactionId = 0, Population = 1000, Terrain = TerrainType.Plain, AdjacentCityIds = new List<int> { 1 } },
            new CityTemplate { Id = 1, Name = "乙城", FactionId = 1, Population = 900, Terrain = TerrainType.Hill, AdjacentCityIds = new List<int> { 0 } },
        };
}

/// <summary>
/// 样本数据自检测试。
/// </summary>
public sealed class SampleDataTests
{
    /// <summary>交付要求：3 势力 / 6 城 / 5 人才。</summary>
    [Fact]
    public void CreateWorld_ShouldProvideThreeFactionsSixCitiesFiveOfficers()
    {
        var state = SampleData.CreateWorld();

        Assert.Equal(3, state.Factions.Count);
        Assert.Equal(6, state.Cities.Count);
        Assert.Equal(5, state.Officers.Count);
        Assert.Single(state.Factions, f => f.IsPlayer);
        Assert.Equal(Order.PlayerId, state.Player.Id);
    }

    /// <summary>相邻关系必须对称，否则出击校验会依赖于数据书写顺序。</summary>
    [Fact]
    public void CreateWorld_AdjacencyMustBeSymmetric()
    {
        var state = SampleData.CreateWorld();

        foreach (var city in state.Cities)
        {
            foreach (int neighborId in city.AdjacentCityIds)
            {
                City neighbor = state.GetCity(neighborId)!;
                Assert.NotNull(neighbor);
                Assert.Contains(city.Id, neighbor.AdjacentCityIds);
            }
        }
    }

    /// <summary>只应有一个玩家势力，且玩家档案 Id 恒为 0。</summary>
    [Fact]
    public void CreateWorld_ShouldHaveExactlyOnePlayerFaction()
    {
        var state = SampleData.CreateWorld();
        Assert.Single(state.Factions, f => f.IsPlayer);
        Assert.Equal(0, state.Player.Id);
    }
}
