// ============================================================================
//  士气系统测试 —— 严格对照 4_数值框架.md 第 5.2 / 5.4 节
//
//  【11 个必测场景】测试名与任务书逐条对应：
//   1 初始 60 无惩罚      5 低士气可使战损超过击退阈值
//   2 士气 45 战损 ×1.5   6 击退后减 12
//   3 士气 20 战损 ×2.5   7 守方无论胜负都减 4
//   4 士气 5  战损 ×4.0   8 归零触发哗变一次且仅一次
//                         9 连续 6 月无战开始腐化
//                         10 参战后 MonthsWithoutBattle 归零
//                         11 Clone 保留两个新字段
//
//  【关键口径：低士气倍率必须能把战损推过击退阈值】
// 这条是士气系统「有没有存在意义」的判据。早期档位 ×1.2/1.5/2.0 在实测中
// 让士气从 60 掉到 10 战局结果一次都没变——只是多烧点兵，士气系统形同装饰。
// 因此实现里禁止对 lossRate 做 min(x, 1.0) 钳制。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Resolvers;
using StrategyGame.Core.Sample;
using StrategyGame.Core.Systems;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>
/// 士气系统测试。倍率档位来自契约 <c>Army.GetMoralePenalty()</c>。
/// </summary>
public sealed class MoraleSystemTests
{
    private static BalanceConfig Balance => SampleData.CreateBalance();

    private const int TenThousand = DesignConstants.TenThousandBase;

    private static MoraleSystem System => new(Balance);

    /// <summary>造一支可控的部队，避免依赖 SampleData 的具体数值。</summary>
    private static Army Army(
        int morale,
        int soldiers = 1000,
        int monthsWithoutBattle = 0,
        bool hasMutinied = false)
        => new()
        {
            Id = 0, OwnerFactionId = 0, OfficerId = -1,
            Soldiers = soldiers, Morale = morale, MoraleMax = 100,
            PositionCityId = 0, TargetCityId = -1,
            Stance = ArmyStance.Idle,
            MonthsWithoutBattle = monthsWithoutBattle,
            HasMutiniedThisMonth = hasMutinied,
        };

    private static WorldState WorldWith(params Army[] armies)
    {
        var state = SampleData.CreateWorld();
        state.Armies.Clear();
        state.Armies.AddRange(armies);
        return state;
    }

    private static void Apply(WorldState state, List<StateDelta> deltas)
        => new StateApplier().Apply(state, deltas);

    // ========================================================================
    //  场景 1-4：低士气惩罚档位
    // ========================================================================

    /// <summary>场景 1：士气 60 → 无惩罚，倍率 10000。</summary>
    [Fact]
    public void Morale_初始60_无惩罚()
    {
        Assert.Equal(TenThousand, Army(morale: 60).GetMoralePenalty());
        Assert.Equal(TenThousand, Army(morale: 100).GetMoralePenalty());
    }

    /// <summary>场景 2：士气 45（30-59 档）→ 战损 ×1.5。</summary>
    [Fact]
    public void Morale_45_战损加50()
    {
        Assert.Equal(15_000, Army(morale: 45).GetMoralePenalty());
        Assert.Equal(15_000, Army(morale: 30).GetMoralePenalty());
        Assert.Equal(15_000, Army(morale: 59).GetMoralePenalty());
    }

    /// <summary>场景 3：士气 20（10-29 档）→ 战损 ×2.5。</summary>
    [Fact]
    public void Morale_20_战损加150()
    {
        Assert.Equal(25_000, Army(morale: 20).GetMoralePenalty());
        Assert.Equal(25_000, Army(morale: 10).GetMoralePenalty());
        Assert.Equal(25_000, Army(morale: 29).GetMoralePenalty());
    }

    /// <summary>场景 4：士气 5（&lt;10 档）→ 战损 ×4.0。</summary>
    [Fact]
    public void Morale_5_战损加300()
    {
        Assert.Equal(40_000, Army(morale: 5).GetMoralePenalty());
        Assert.Equal(40_000, Army(morale: 9).GetMoralePenalty());
        Assert.Equal(40_000, Army(morale: 0).GetMoralePenalty());
    }

    /// <summary>
    /// 场景 5（本轮最关键的一条）：低士气必须能把战损推过击退阈值。
    /// </summary>
    /// <remarks>
    /// 均势攻城 3000 攻 1000 守，基础 lossRate = 0.350。
    /// 士气 60 时 → 攻陷；士气 20（×2.5）→ 0.875 ≥ 0.70 → 击退。
    /// 若实现里做了 <c>min(x, 1.0)</c> 钳制，这里会得到 1.0 仍判击退——
    /// 看似通过；但士气 20 ×2.5 与士气 60 就会完全等价，士气系统等于没做。
    /// 因此本测试同时锁定「阈值翻转」和「倍率确实生效」两件事。
    /// </remarks>
    [Fact]
    public void Morale_低士气可使战损超过击退阈值()
    {
        var battle = new BattleSystem(Balance);

        var healthy = battle.Resolve(
            3000, 1000, 500, 50, 50, true, false, false,
            Army(morale: 60).GetMoralePenalty());

        var shaky = battle.Resolve(
            3000, 1000, 500, 50, 50, true, false, false,
            Army(morale: 20).GetMoralePenalty());

        Assert.Equal(BattleOutcome.Capture, healthy.Outcome);
        Assert.True(healthy.LossRateTenThousand < Balance.SiegeThresholdLoss);

        Assert.Equal(BattleOutcome.Repelled, shaky.Outcome);
        Assert.True(shaky.LossRateTenThousand >= Balance.SiegeThresholdLoss);

        // 倍率确实是 ×2.5，而不是被钳制成同一个值。
        Assert.Equal(25_000, Army(morale: 20).GetMoralePenalty());
        Assert.Equal(8750, shaky.LossRateTenThousand);
        Assert.Equal(3500, healthy.LossRateTenThousand);
    }

    // ========================================================================
    //  场景 6-7：战斗士气流转
    // ========================================================================

    /// <summary>场景 6：被击退后士气 −12。</summary>
    [Fact]
    public void Morale_击退后减12()
    {
        Assert.Equal(-12, System.CalculateAttackerMoraleDelta(BattleOutcome.Repelled));
        Assert.Equal(12, Balance.MoraleLossOnRepel);
    }

    /// <summary>场景 7：守方无论胜负都 −4。</summary>
    /// <remarks>
    /// 守方减益只取决于「参与了防守」这一事实，与攻陷/击退/未决无关——
    /// 统一走 <see cref="MoraleSystem.CalculateDefenderMoraleDelta"/>，
    /// 不接受按战果分叉的写法。
    /// </remarks>
    [Fact]
    public void Morale_守方无论胜负都减4()
    {
        var system = System;

        Assert.Equal(-4, system.CalculateDefenderMoraleDelta());

        foreach (var outcome in new[]
                 {
                     BattleOutcome.Capture, BattleOutcome.Repelled, BattleOutcome.Stalemate,
                 })
        {
            // 守方减益与战果无关：三种战果下都是 −4。
            Assert.Equal(-4, system.CalculateDefenderMoraleDelta());
            _ = outcome;
        }
    }

    /// <summary>攻方士气流转：攻陷 +10 / 击退 −12 / 未决 −3。</summary>
    [Fact]
    public void Morale_攻方士气流转按战果分档()
    {
        var system = System;

        Assert.Equal(10, system.CalculateAttackerMoraleDelta(BattleOutcome.Capture));
        Assert.Equal(-12, system.CalculateAttackerMoraleDelta(BattleOutcome.Repelled));
        Assert.Equal(-3, system.CalculateAttackerMoraleDelta(BattleOutcome.Stalemate));
    }

    /// <summary>
    /// 出兵结算实战：三种战果下攻方士气的净变化符合配置，
    /// 且守方无论战果都掉士气。
    /// </summary>
    /// <remarks>
    /// 三档兵力分别覆盖未决（150）、攻陷（3000）与击退（8000），
    /// 三条分支的士气增减必须都被真实走到——只测一条会漏掉分支漂移。
    /// </remarks>
    [Fact]
    public void Morale_实战三种战果守方均减士气()
    {
        // 投入兵力 / 目标城防 → 该战果下的攻方士气增量。
        // 击退档需要把城防拉到 2000（defenseFactor 2.0），否则 killRate 足以
        // 在任何兵力下把80 守军打光，击退分支永远走不到。
        var cases = new (int Dispatched, int Defense, int ExpectedDelta)[]
        {
            (150, 90, -Balance.MoraleLossOnStalemate),
            (3000, 90, Balance.MoraleGainOnCapture),
            (8000, 2000, -Balance.MoraleLossOnRepel),
        };

        foreach (var (dispatched, defense, expectedDelta) in cases)
        {
            var state = SampleData.CreateWorld();
            state.GetArmy(1)!.PositionCityId = 1;
            state.GetArmy(1)!.Soldiers = 80;
            state.GetArmy(0)!.Soldiers = 8000;
            state.GetArmy(0)!.Morale = 60;
            state.GetCity(1)!.Defense = defense;

            var before = state.GetArmy(0)!.Morale;
            var defenderBefore = state.GetArmy(1)!.Morale;

            var result = new DispatchResolver(Balance).Resolve(
                state,
                Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: dispatched, secondary: 0));

            Assert.True(result.IsSuccess);

            // 守方：不论战果，一律 −4。
            var defenderMorale = result.Deltas
                .Where(d => d.TargetId == 1 && d.Field == DeltaField.ArmyMorale)
                .Select(d => d.NewValue)
                .Single();
            Assert.Equal(Math.Max(0, defenderBefore - 4), defenderMorale);

            // 攻方：按战果精确分档，且夹在 [0, MoraleMax] 内。
            var attackerMorale = result.Deltas
                .Where(d => d.TargetId == 0 && d.Field == DeltaField.ArmyMorale)
                .Select(d => d.NewValue)
                .Single();
            Assert.Equal(Math.Clamp(before + expectedDelta, 0, 100), attackerMorale);
        }
    }

    // ========================================================================
    //  场景 8：哗变
    // ========================================================================

    /// <summary>
    /// 场景 8：士气归零触发哗变，且同一月只触发一次。
    /// </summary>
    /// <remarks>
    /// 「一次且仅一次」靠 <c>HasMutiniedThisMonth</c> 保证：
    /// 若漏了这个标记，一个士气为 0 的部队会在每次月度结算时重复扣 30% 兵力，
    /// 两个月就自己消失了。
    /// </remarks>
    [Fact]
    public void Morale_归零触发哗变一次且仅一次()
    {
        var state = WorldWith(Army(morale: 0, soldiers: 1000));
        var system = System;

        // ---- 第一次结算：触发哗变 ----
        var first = system.ResolveMonthlyMorale(state);
        Apply(state, first);

        Assert.Equal(1000, first
            .First(d => d.Field == DeltaField.ArmySoldiers).OldValue);

        int rout = 1000 * Balance.MutinyRoutRate / TenThousand;
        Assert.Equal(3000, Balance.MutinyRoutRate);
        Assert.Equal(700, state.GetArmy(0)!.Soldiers);
        Assert.True(state.GetArmy(0)!.HasMutiniedThisMonth);

        // ---- 第二次结算：不再重复哗变 ----
        var second = system.ResolveMonthlyMorale(state);
        Apply(state, second);

        Assert.DoesNotContain(second, d => d.Field == DeltaField.ArmySoldiers);
        Assert.Equal(700, state.GetArmy(0)!.Soldiers);

        // 事件记录也只应产出一条。
        var evt = system.BuildMutinyEvent(state, 0);
        Assert.NotNull(evt);
        Assert.Equal(EventKeys.Mutiny, evt!.EventKey);
    }

    /// <summary>已哗变过的部队当月不再触发哗变。</summary>
    [Fact]
    public void Morale_已标记哗变的部队不再重复扣兵()
    {
        var state = WorldWith(Army(morale: 0, soldiers: 1000, hasMutinied: true));

        var deltas = System.ResolveMonthlyMorale(state);

        Assert.DoesNotContain(deltas, d => d.Field == DeltaField.ArmySoldiers);
        Assert.DoesNotContain(deltas, d => d.Field == DeltaField.ArmyHasMutiniedThisMonth);
    }

    // ========================================================================
    //  场景 9：和平腐化
    // ========================================================================

    /// <summary>
    /// 场景 9：连续无战达到阈值后开始腐化，阈值当月即生效。
    /// </summary>
    /// <remarks>
    /// 阈值 6 的语义是「连续 6 个月没打仗就开始腐化」，
    /// 所以第 6 个无战月当月就该扣，而不是等第 7 月。
    /// </remarks>
    [Fact]
    public void Morale_连续6月无战开始腐化()
    {
        var system = System;
        Assert.Equal(6, Balance.MoraleDecayNoBattleThreshold);
        Assert.Equal(2, Balance.MoraleDecayNoBattlePerMonth);
        Assert.Equal(3, Balance.MoraleDecayPerMonth);

        // ---- 前5 个月：只自然恢复，不腐化 ----
        var state = WorldWith(Army(morale: 50, monthsWithoutBattle: 0));

        for (int month = 1; month <= 5; month++)
        {
            Apply(state, system.ResolveMonthlyMorale(state));
            Assert.Equal(50 + 3 * month, state.GetArmy(0)!.Morale);
        }

        Assert.Equal(5, state.GetArmy(0)!.MonthsWithoutBattle);

        // ---- 第 6 个月：达到阈值，净变化 = +3 −2 = +1 ----
        Apply(state, system.ResolveMonthlyMorale(state));
        Assert.Equal(6, state.GetArmy(0)!.MonthsWithoutBattle);
        Assert.Equal(50 + 3 * 5 + 1, state.GetArmy(0)!.Morale);
    }

    /// <summary>
    /// 长期无战的士气演化：净变化恒为 +1（恢复 3 − 腐化 2），士气单调回升。
    /// </summary>
    /// <remarks>
    /// 【本测试同时是一条待确认项】设计 4_数值框架 5.2 给的是
    /// 「自然恢复 +3/ 月」与「长期无战 −2 / 月」，两者叠加后净变化为 <b>+1</b>，
    /// 意味着和平腐化在数值上永远拉不垮一支部队——所谓「双刃剑」目前没有刃。
    /// 本测试锁定的是<b>当前实现的真实行为</b>（不做美化），
    /// 若设计侧希望腐化真正构成压力，需调大<code>MoraleDecayNoBattlePerMonth</code>
    /// 至大于 <code>MoraleDecayPerMonth</code>，届时本测试会失败并提示同步更新。
    /// 已作为疑问上报。
    /// </remarks>
    [Fact]
    public void Morale_长期无战净变化为恢复减腐化()
    {
        var state = WorldWith(Army(morale: 20, monthsWithoutBattle: 0));
        var system = System;

        int previous = state.GetArmy(0)!.Morale;

        for (int month = 1; month <= 24; month++)
        {
            Apply(state, system.ResolveMonthlyMorale(state));

            int current = state.GetArmy(0)!.Morale;
            int expected = month < 6
                ? previous + 3
                : Math.Min(100, previous + 1);

            Assert.Equal(expected, current);
            Assert.InRange(current, 0, 100);

            previous = current;
        }

        // 20 + 3×5（第1-5 月）+ 1×19（第 6-24 月）= 54。
        Assert.Equal(54, state.GetArmy(0)!.Morale);
    }

    /// <summary>
    /// 哗变只在士气真正崩溃时触发；靠自然恢复不会让一支健康部队哗变。
    /// </summary>
    [Fact]
    public void Morale_士气未崩溃不触发哗变()
    {
        var state = WorldWith(Army(morale: 50, soldiers: 1000));
        var system = System;

        for (int month = 1; month <= 12; month++)
        {
            Apply(state, system.ResolveMonthlyMorale(state));
        }

        Assert.False(state.GetArmy(0)!.HasMutiniedThisMonth);
        Assert.Equal(1000, state.GetArmy(0)!.Soldiers);
    }

    // ========================================================================
    //  场景 10：参战重置
    // ========================================================================

    /// <summary>场景 10：参战后 MonthsWithoutBattle 归零。</summary>
    [Fact]
    public void Morale_参战后MonthsWithoutBattle归零()
    {
        var state = SampleData.CreateWorld();
        state.GetArmy(1)!.PositionCityId = 1;
        state.GetArmy(1)!.Soldiers = 80;
        state.GetArmy(0)!.Soldiers = 3000;
        state.GetArmy(0)!.MonthsWithoutBattle = 5;

        var result = new DispatchResolver(Balance).Resolve(
            state,
            Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 150, secondary: 0));

        Assert.True(result.IsSuccess);

        var delta = result.Deltas.Single(
            d => d.TargetId == 0 && d.Field == DeltaField.ArmyMonthsWithoutBattle);

        Assert.Equal(5, delta.OldValue);
        Assert.Equal(0, delta.NewValue);

        // 独立于战果：攻陷 / 击退 / 未决都应重置。
        foreach (var dispatched in new[] { 150, 3000, 8000 })
        {
            var each = SampleData.CreateWorld();
            each.GetArmy(1)!.PositionCityId = 1;
            each.GetArmy(1)!.Soldiers = 80;
            each.GetArmy(0)!.Soldiers = 8000;
            each.GetArmy(0)!.MonthsWithoutBattle = 5;

            var r = new DispatchResolver(Balance).Resolve(
                each,
                Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: dispatched, secondary: 0));

            Assert.Contains(r.Deltas, d =>
                d.TargetId == 0 && d.Field == DeltaField.ArmyMonthsWithoutBattle && d.NewValue == 0);
        }
    }

    // ========================================================================
    //  场景 11：Clone 完整性
    // ========================================================================

    /// <summary>
    /// 场景 11：Army.Clone 必须保留 v1.2 新增的两个字段。
    /// </summary>
    /// <remarks>
    /// 漏掉任一字段都会造成静默bug：Clone 是快照/回滚的基准，
    /// 漏MonthsWithoutBattle 会让存档读回后部队莫名其妙开始腐化。
    /// </remarks>
    [Fact]
    public void Morale_Clone保留两个新字段()
    {
        var original = Army(morale: 42, soldiers: 777, monthsWithoutBattle: 9, hasMutinied: true);
        var copy = original.Clone();

        Assert.Equal(9, copy.MonthsWithoutBattle);
        Assert.True(copy.HasMutiniedThisMonth);

        // 其余字段不能被 Clone 破坏。
        Assert.Equal(original.Id, copy.Id);
        Assert.Equal(original.Soldiers, copy.Soldiers);
        Assert.Equal(original.Morale, copy.Morale);
        Assert.Equal(original.MoraleMax, copy.MoraleMax);
    }

    // ========================================================================
    //  休整恢复
    // ========================================================================

    /// <summary>休整按「自然恢复」口径兑现，且不超上限。</summary>
    [Fact]
    public void Morale_休整恢复3点且不超上限()
    {
        var system = System;

        Assert.Equal(53, system.CalculateMoraleAfterRest(Army(morale: 50)));
        Assert.Equal(100, system.CalculateMoraleAfterRest(Army(morale: 99)));

        // 【下限口径】休整是「兑现自然恢复」，不额外做下限保护：
        // 士气 0 的部队休整后即为 3，不留在 0。
        Assert.Equal(3, system.CalculateMoraleAfterRest(Army(morale: 0)));
    }

    /// <summary>休整指令真实生效，且同月不可重复休整。</summary>
    [Fact]
    public void Morale_休整指令提升士气并锁定同月重复()
    {
        var state = SampleData.CreateWorld();
        state.GetArmy(0)!.Morale = 50;
        state.GetArmy(0)!.HasRestedThisMonth = false;

        var resolver = new RestResolver(Balance);
        var order = Order.CreatePlayerOrder(ActionType.Rest, target: 0, amount: 0, secondary: 0);

        var first = resolver.Resolve(state, order);
        Assert.True(first.IsSuccess);

        Apply(state, first.Deltas);
        Assert.Equal(53, state.GetArmy(0)!.Morale);

        // 同月再次休整应被拒绝。
        var second = resolver.Resolve(state, order);
        Assert.False(second.IsSuccess);
    }

    // ========================================================================
    //  整除截断方向 —— 全项目统一向下取整
    // ========================================================================

    /// <summary>
    /// 截断方向必须向下：整数定点运算里方向是「多给」还是「少给」，
    /// 会直接影响玩家可复现的结果，必须锁死。
    /// </summary>
    [Fact]
    public void 截断_兵力换算一律向下取整()
    {
        // 999 × 3333 / 10000 = 332.66... → 332（向下），不是 333。
        var result = new BattleSystem(Balance).Resolve(
            999, 5000, 500, 50, 50, true, false, false);

        int rate = result.LossRateTenThousand;
        Assert.Equal(999 * rate / TenThousand, result.AttackerLoss);
        Assert.True(result.AttackerLoss <= (long)999 * rate / TenThousand);
    }

    /// <summary>守方损失同样向下取整，且永不超过守军规模。</summary>
    [Fact]
    public void 截断_守方损失向下且不超守军()
    {
        for (int attackers = 1; attackers <= 50; attackers++)
        {
            for (int defenders = 1; defenders <= 50; defenders += 7)
            {
                var r = new BattleSystem(Balance).Resolve(
                    attackers, defenders, 300, 40, 60, true, false, false);

                Assert.True(r.DefenderLoss <= defenders);
                Assert.True(r.DefenderLoss >= 0);
                Assert.True(r.AttackerLoss >= 0);
            }
        }
    }

    /// <summary>哗变解散兵力向下取整，绝不四舍五入多扣。</summary>
    [Fact]
    public void 截断_哗变解散人数向下取整()
    {
        var state = WorldWith(Army(morale: 0, soldiers: 999));
        var system = System;

        Apply(state, system.ResolveMonthlyMorale(state));

        // 999 × 3000 / 10000 = 299.7 → 299，剩 700。
        Assert.Equal(700, state.GetArmy(0)!.Soldiers);
    }
}
