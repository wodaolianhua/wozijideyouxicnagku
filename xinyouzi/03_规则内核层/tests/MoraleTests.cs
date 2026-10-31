// ============================================================================
//  士气系统测试 —— 严格对照 4_数值框架.md 第 5.2 / 5.4 节
//
//  【11 个必测场景】测试名与任务书逐条对应：
//   1 初始 60 无惩罚      5 低士气可使战损超过击退阈值
//   2 士气 45 战损 ×1.5   6 击退后减 12
//   3 士气 20 战损 ×2.5   7 守方无论胜负都减 4
//   4 士气 5  战损 ×4.0   8 归零触发哗变一次且仅一次
//                         9 连续 4 月无战开始腐化（v1.3 阈值 4）
//                         10 参战后 MonthsWithoutBattle 归零
//                         11 Clone 保留两个新字段
//
//  【第 2 轮补丁 · 契约 v1.4 参战信号】恢复只在未参战月生效，四个新场景：
//   TestMorale_参战月不恢复 / 连续未参战4月触发腐化 /
//   每2月一战不会恒定在60 / 恢复与腐化不叠加。
//
//  【第 3 轮补丁】月初标记翻转（修复二）/ 守军参战信号（修复三），
//   新增 TestMorale_月度标记月初翻转 / TestMorale_守军参战不回血不腐化；
//   T1-B 的手动复位 workaround 已改为真实续攻路径（修复一）。
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

    /// <summary>
    /// 【第 3 轮补丁改锁】哗变标记月初翻转后，士气仍崩溃的部队可再次哗变。
    /// </summary>
    /// <remarks>
    /// 旧规则「HasMutiniedThisMonth 从不清零 → 哗变一生一次」是月度标记无翻转的
    /// 老 bug（第 2 轮汇报遗留第 3 条）。修复后标记在月度结算开头翻转（先清后判）：
    /// 上月哗变、本月士气仍 ≤ 0 → 再次解散兵力，是否再哗变只看当月士气。
    /// 本例未参战：恢复分支把结算后士气拉到 3，但判定取结算前后较小值 0 → 仍触发。
    /// </remarks>
    [Fact]
    public void Morale_哗变标记翻转后士气仍崩溃可再次哗变()
    {
        var state = WorldWith(Army(morale: 0, soldiers: 1000, hasMutinied: true));

        var deltas = System.ResolveMonthlyMorale(state);

        // 月初翻转照常产出：旧标记 1→0。
        Assert.Contains(deltas, d =>
            d.Field == DeltaField.ArmyHasMutiniedThisMonth
            && d.OldValue == 1 && d.NewValue == 0);

        // 先清后判：士气仍崩溃 → 再次哗变扣兵（一生一次 bug 消除）。
        Assert.Contains(deltas, d => d.Field == DeltaField.ArmySoldiers);
    }

    // ========================================================================
    //  场景 9：和平腐化
    // ========================================================================

    /// <summary>
    /// 场景 9：连续无战达到阈值后开始腐化，阈值当月即生效（契约 v1.3 基准：阈值 4 / 腐化 6）。
    /// </summary>
    /// <remarks>
    /// 阈值 4 的语义是「连续 4 个月没打仗就开始腐化」，
    /// 用结算前的值 +1 后比较，所以第 4 个无战月当月就扣，而不是等第 5 月。
    /// </remarks>
    [Fact]
    public void Morale_连续4月无战开始腐化()
    {
        var system = System;
        Assert.Equal(4, Balance.MoraleDecayNoBattleThreshold);
        Assert.Equal(6, Balance.MoraleDecayNoBattlePerMonth);
        Assert.Equal(3, Balance.MoraleDecayPerMonth);

        // ---- 前 3 个月：未达阈值，只自然恢复 ----
        var state = WorldWith(Army(morale: 50, monthsWithoutBattle: 0));

        for (int month = 1; month <= 3; month++)
        {
            Apply(state, system.ResolveMonthlyMorale(state));
            Assert.Equal(50 + 3 * month, state.GetArmy(0)!.Morale);
            Assert.Equal(month, state.GetArmy(0)!.MonthsWithoutBattle);
        }

        // ---- 第 4 个月：m = 3+1 = 4 达阈值，当月只扣 6（不叠加 +3）----
        int before = state.GetArmy(0)!.Morale;
        Apply(state, system.ResolveMonthlyMorale(state));
        Assert.Equal(4, state.GetArmy(0)!.MonthsWithoutBattle);
        Assert.Equal(before - 6, state.GetArmy(0)!.Morale);
    }

    /// <summary>
    /// 长期无战的士气演化（契约 v1.3 定稿）：前 3 月恢复 +3，第 4 月起每月只扣 6，
    /// 士气单调下滑——和平腐化构成真实压力。
    /// </summary>
    /// <remarks>
    /// 上一轮曾把「净变化 = +3 −2 = +1、腐化拉不垮部队」作为疑问上报；
    /// 契约 v1.3 已按实算改定 +3 / −6 / 阈值 4，本测试随之改锁新行为：
    /// 12 个月纯和平 60 → 15，任何一个月不触发哗变、不触零。
    /// </remarks>
    [Fact]
    public void Morale_长期无战腐化构成真实压力()
    {
        var state = WorldWith(Army(morale: 60, monthsWithoutBattle: 0));
        var system = System;

        int previous = state.GetArmy(0)!.Morale;

        for (int month = 1; month <= 12; month++)
        {
            Apply(state, system.ResolveMonthlyMorale(state));

            int current = state.GetArmy(0)!.Morale;
            int expected = month <= 3 ? previous + 3 : previous - 6;

            Assert.Equal(expected, current);
            Assert.InRange(current, 0, 100);

            previous = current;
        }

        // 60 + 3×3（第 1-3 月）− 6×9（第 4-12 月）= 15。
        Assert.Equal(15, state.GetArmy(0)!.Morale);
        Assert.False(state.GetArmy(0)!.HasMutiniedThisMonth);
        Assert.Equal(1000, state.GetArmy(0)!.Soldiers);
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
    //  第 2 轮补丁 · 第 1 节：恢复只在未参战月生效（契约 v1.4 参战信号）
    // ========================================================================

    /// <summary>
    /// 补丁 T1：参战月不恢复——参战周期的月度结算净变化为 0。
    /// </summary>
    /// <remarks>
    /// A 段直接结算：连续 3 个参战周期（模拟第 7 步战斗后的标记/计数状态），
    /// 士气 50 纹丝不动、标记消费即清、计数保持 0。
    /// B 段端到端：经 DispatchResolver 攻城未决（僵持 −3）+ 月度结算 ×3 = 41，
    /// 锁定「无脑连续攻城每月净掉 3 点」的补丁目标。
    /// </remarks>
    [Fact]
    public void TestMorale_参战月不恢复()
    {
        var system = System;

        // ---- A 段：直接结算 ----
        var state = WorldWith(Army(morale: 50));
        for (int month = 1; month <= 3; month++)
        {
            // 模拟本月第 7 步战斗已发生：计数清零、参战标记置位。
            var army = state.GetArmy(0)!;
            army.MonthsWithoutBattle = 0;
            army.HasEngagedThisMonth = true;

            var deltas = system.ResolveMonthlyMorale(state);
            Apply(state, deltas);

            Assert.Equal(50, state.GetArmy(0)!.Morale);
            Assert.Equal(0, state.GetArmy(0)!.MonthsWithoutBattle);
            Assert.False(state.GetArmy(0)!.HasEngagedThisMonth);
            Assert.DoesNotContain(deltas, d => d.Field == DeltaField.ArmyMorale);
        }

        // ---- B 段：端到端（DispatchResolver 僵持 + 月度结算）----
        var world = SampleData.CreateWorld();
        world.Player.IsPersonallyCommanding = true;  // 固定普通 Dispatch 路径，避免首击被判亲征挬走统帅
        world.GetArmy(0)!.OfficerId = 0;             // 张良（势力 0，统帅 70）
        world.GetArmy(0)!.Morale = 50;
        world.GetArmy(0)!.Soldiers = 3000;

        var resolver = new DispatchResolver(Balance);

        for (int month = 1; month <= 3; month++)
        {
            // 【第 3 轮补丁·修复一后改为真实续攻路径】僵持后攻方滞留敌城、姿态 Besieging：
            // 同阵营围城豁免 + FindDispatchableArmy 第二轮兑底让城下部队就地再战，
            // 不再需要上一版手动复位姿态的 workaround。
            // SecondaryTargetId 仍传相邻出发城（语义为补给方向）；
            // 出兵量上限 150，次月起把城下剩余兵力全部投入。
            var defender = world.GetArmy(1)!;
            defender.PositionCityId = 1;
            defender.Soldiers = 80;   // 守军每月回填，维持「攻未决」的僵持战果

            var besieger = world.GetArmy(0)!;

            var result = resolver.Resolve(
                world,
                Order.CreatePlayerOrder(
                    ActionType.Dispatch,
                    target: 1,
                    amount: Math.Min(150, besieger.Soldiers),
                    secondary: 0));

            Assert.True(result.IsSuccess);
            var battleMorale = result.Deltas.Single(
                d => d.TargetId == 0 && d.Field == DeltaField.ArmyMorale);
            Assert.Equal(-Balance.MoraleLossOnStalemate, battleMorale.NewValue - battleMorale.OldValue);

            Apply(world, result.Deltas);
            Assert.Equal(50 - 3 * month, world.GetArmy(0)!.Morale);

            // 月度结算：参战周期 → 不恢复
            Apply(world, system.ResolveMonthlyMorale(world));
            Assert.Equal(50 - 3 * month, world.GetArmy(0)!.Morale);
            Assert.Equal(0, world.GetArmy(0)!.MonthsWithoutBattle);
            Assert.False(world.GetArmy(0)!.HasEngagedThisMonth);
        }

        Assert.Equal(41, world.GetArmy(0)!.Morale);
    }

    /// <summary>
    /// 补丁 T2：连续未参战 4 月触发腐化——前 3 月各 +3，第 4 月只 −6，末值 63。
    /// </summary>
    /// <remarks>
    /// 【勘误】补丁提示词写「60+3+3+3−6=57」为算术错误，60+9−6=63（00 号裁决第三节）。
    /// </remarks>
    [Fact]
    public void TestMorale_连续未参战4月触发腐化()
    {
        var state = WorldWith(Army(morale: 60, monthsWithoutBattle: 0));
        var system = System;

        int[] expected = { 63, 66, 69, 63 };

        for (int month = 1; month <= 4; month++)
        {
            var deltas = system.ResolveMonthlyMorale(state);
            Apply(state, deltas);

            Assert.Equal(expected[month - 1], state.GetArmy(0)!.Morale);
            Assert.Equal(month, state.GetArmy(0)!.MonthsWithoutBattle);
        }

        // 第 4 月必须只扣 6（净 −6），不是叠加出 −3。
        Assert.Equal(63, state.GetArmy(0)!.Morale);
    }

    /// <summary>
    /// 补丁 T3：每 2 月一战不会恒定在 60（逐月事实序列）。
    /// </summary>
    /// <remarks>
    /// 【与补丁提示词的差异，详见汇报】原断言「12 月末 &lt; 57」在 v1.4 规则下数学上不可能：
    /// 每次战斗都把计数清零，休整月 m 永远 = 1 &lt; 4，只会恢复不会腐化，
    /// 因此交替场景士气单调上升（60 → 78）。00 号裁决改为锁定逐月事实：
    /// 参战后结算月净变化 0、休整月结算 +3、计数器永不达到 4（永不腐化）。
    /// </remarks>
    [Fact]
    public void TestMorale_每2月一战不会恒定在60()
    {
        var state = WorldWith(Army(morale: 60, monthsWithoutBattle: 0));
        var system = System;

        int morale = state.GetArmy(0)!.Morale;

        for (int month = 1; month <= 12; month++)
        {
            bool battleMonth = month % 2 == 1;   // 战、休、战、休……
            var army = state.GetArmy(0)!;

            if (battleMonth)
            {
                // 模拟上一结算周期结束时的战斗后果：计数清零、参战标记置位。
                army.MonthsWithoutBattle = 0;
                army.HasEngagedThisMonth = true;
            }

            int before = army.Morale;
            var deltas = system.ResolveMonthlyMorale(state);
            Apply(state, deltas);

            int change = state.GetArmy(0)!.Morale - before;
            if (battleMonth)
            {
                Assert.Equal(0, change);                              // 参战后结算月净变化 0
                Assert.False(state.GetArmy(0)!.HasEngagedThisMonth);  // 标记已消费
            }
            else
            {
                Assert.Equal(Balance.MoraleDecayPerMonth, change);    // 休整月结算 +3
                Assert.Equal(1, state.GetArmy(0)!.MonthsWithoutBattle);
            }

            Assert.True(
                state.GetArmy(0)!.MonthsWithoutBattle < Balance.MoraleDecayNoBattleThreshold,
                "交替节奏下计数器永不达到阈值，永不腐化");

            morale = state.GetArmy(0)!.Morale;
        }

        // 12 月 = 6 个参战结算（0）+ 6 个休整月（+3），末值 78。
        Assert.Equal(60 + 3 * 6, morale);
    }

    /// <summary>
    /// 补丁 T4：恢复与腐化不叠加——任何一次结算的士气变化只能是 0 / +3 / −6 之一。
    /// </summary>
    /// <remarks>
    /// 「既 +3 又 −6」的叠加在同一次结算里只可能表现为净 −3（+3−6），
    /// 那是旧实现叠加 bug 的特征签名，必须绝迹。
    /// 扫描三类场景：跨阈值的 12 个月纯和平、参战周期结算、战/休交替。
    /// </remarks>
    [Fact]
    public void TestMorale_恢复与腐化不叠加()
    {
        var system = System;
        var allChanges = new List<int>();

        // 场景一：连续 12 个无战月（跨越阈值，叠加 bug 会在第 4 月暴露为净 −3）。
        var idle = WorldWith(Army(morale: 60, monthsWithoutBattle: 0));
        for (int month = 1; month <= 12; month++)
        {
            var deltas = system.ResolveMonthlyMorale(idle);
            allChanges.AddRange(MoraleChanges(deltas));
            Apply(idle, deltas);
        }

        Assert.Equal(15, idle.GetArmy(0)!.Morale);   // 60 + 9 − 54

        // 场景二：参战周期结算（净 0，不产出士气 delta）。
        var engaged = WorldWith(Army(morale: 50));
        engaged.GetArmy(0)!.HasEngagedThisMonth = true;
        var engagedDeltas = system.ResolveMonthlyMorale(engaged);
        allChanges.AddRange(MoraleChanges(engagedDeltas));
        Assert.DoesNotContain(engagedDeltas, d => d.Field == DeltaField.ArmyMorale);

        // 场景三：战/休交替 12 个月。
        var mixed = WorldWith(Army(morale: 60, monthsWithoutBattle: 0));
        for (int month = 1; month <= 12; month++)
        {
            if (month % 2 == 1)
            {
                mixed.GetArmy(0)!.MonthsWithoutBattle = 0;
                mixed.GetArmy(0)!.HasEngagedThisMonth = true;
            }

            var deltas = system.ResolveMonthlyMorale(mixed);
            allChanges.AddRange(MoraleChanges(deltas));
            Apply(mixed, deltas);
        }

        int stack = Balance.MoraleDecayPerMonth - Balance.MoraleDecayNoBattlePerMonth; // +3 + (−6) = −3

        Assert.All(allChanges, c => Assert.True(
            c == 0 || c == Balance.MoraleDecayPerMonth || c == -Balance.MoraleDecayNoBattlePerMonth,
            $"出现非法士气变化 {c}（只允许 0 / +3 / −6）"));
        Assert.DoesNotContain(stack, allChanges);
    }

    /// <summary>提取一次结算中全部部队的士气变化量（New − Old）。</summary>
    private static IEnumerable<int> MoraleChanges(List<StateDelta> deltas)
        => deltas.Where(d => d.Field == DeltaField.ArmyMorale)
                 .Select(d => d.NewValue - d.OldValue);

    // ========================================================================
    //  第 3 轮补丁：月初标记翻转（修复二）与守军参战信号（修复三）
    // ========================================================================

    /// <summary>
    /// 补丁 T7：月度标记月初翻转——休整 / 哗变标记在结算开头归零且产出对应 delta；
    /// 「先清后判」：上月哗变、本月士气仍 ≤ 0 的部队可再次哗变。
    /// </summary>
    /// <remarks>
    /// 老 bug：HasRestedThisMonth 只在出兵时清零（纯休整部队一生只能休整一次）、
    /// HasMutiniedThisMonth 从不清零（哗变一生一次）。修复二在月度结算开头统一翻转。
    /// </remarks>
    [Fact]
    public void TestMorale_月度标记月初翻转()
    {
        var system = System;

        // ---- 段一：两标记同时为 true，结算一次全部翻转 ----
        var state = WorldWith(Army(morale: 50, soldiers: 1000));
        state.GetArmy(0)!.HasRestedThisMonth = true;
        state.GetArmy(0)!.HasMutiniedThisMonth = true;

        var deltas = system.ResolveMonthlyMorale(state);

        var restedFlip = deltas.Single(d => d.Field == DeltaField.ArmyHasRested);
        Assert.Equal(1, restedFlip.OldValue);
        Assert.Equal(0, restedFlip.NewValue);

        var mutiniedFlip = deltas.Single(d => d.Field == DeltaField.ArmyHasMutiniedThisMonth);
        Assert.Equal(1, mutiniedFlip.OldValue);
        Assert.Equal(0, mutiniedFlip.NewValue);

        Apply(state, deltas);
        Assert.False(state.GetArmy(0)!.HasRestedThisMonth);
        Assert.False(state.GetArmy(0)!.HasMutiniedThisMonth);

        // 本月休整且未参战 → 走休整分支：挂起恢复/腐化，士气不增不减（50），无士气 delta。
        Assert.DoesNotContain(deltas, d => d.Field == DeltaField.ArmyMorale);
        Assert.Equal(50, state.GetArmy(0)!.Morale);

        // ---- 段二：先清后判——上月已哗变、本月参战且士气 ≤ 0 → 可再次哗变 ----
        var relapsed = WorldWith(Army(morale: 0, soldiers: 1000));
        relapsed.GetArmy(0)!.HasMutiniedThisMonth = true;   // 上月哗变的遗留标记
        relapsed.GetArmy(0)!.HasEngagedThisMonth = true;    // 本月参战：参战分支不恢复士气

        var again = system.ResolveMonthlyMorale(relapsed);

        // 月初翻转照常产出（1→0）。
        Assert.Contains(again, d =>
            d.Field == DeltaField.ArmyHasMutiniedThisMonth
            && d.OldValue == 1 && d.NewValue == 0);

        // 哗变兵力 delta 再次产出：一生一次 bug 消除（1000 → 700）。
        var rout = again.Single(d => d.Field == DeltaField.ArmySoldiers);
        Assert.Equal(1000, rout.OldValue);
        Assert.Equal(700, rout.NewValue);

        // 翻转之后哗变标记重新置位（0→1），本月的「防重复」语义保持。
        Assert.Contains(again, d =>
            d.Field == DeltaField.ArmyHasMutiniedThisMonth
            && d.OldValue == 0 && d.NewValue == 1);
    }

    /// <summary>
    /// 补丁 T8：守军参战不回血——被围城的守军走参战分支：
    /// 士气不恢复（不 +3）、不腐化（无 −6）、计数保持 0、标记消费清零。
    /// </summary>
    /// <remarks>
    /// 修复三前的症状：守军参战不置 HasEngagedThisMonth、不清零计数——
    /// 被围城的头 3 个月守军反而每月 +3 回血，第 4 月起又在打仗的同时被腐化 −6。
    /// 修复三让守方与攻方对称：参战信号由 DispatchResolver 在分摊战损的同一循环补发。
    /// </remarks>
    [Fact]
    public void TestMorale_守军参战不回血不腐化()
    {
        var world = SampleData.CreateWorld();
        world.Player.IsPersonallyCommanding = true;   // 固定普通 Dispatch 路径
        world.GetArmy(0)!.OfficerId = 0;              // 张良（势力 0，统帅 70）
        world.GetArmy(0)!.Morale = 50;
        world.GetArmy(1)!.PositionCityId = 1;
        world.GetArmy(1)!.Soldiers = 80;

        var resolver = new DispatchResolver(Balance);
        var system = System;

        for (int month = 1; month <= 4; month++)
        {
            world.GetArmy(1)!.Soldiers = 80;          // 守军每月回填，维持僵持战果

            var result = resolver.Resolve(
                world,
                Order.CreatePlayerOrder(
                    ActionType.Dispatch,
                    target: 1,
                    amount: Math.Min(150, world.GetArmy(0)!.Soldiers),
                    secondary: 0));

            Assert.True(result.IsSuccess);

            // 战斗当月：守军参战信号已置位、计数已清零（与攻方同组 delta）。
            var engaged = result.Deltas.Single(d =>
                d.TargetId == 1 && d.Field == DeltaField.ArmyHasEngagedThisMonth);
            Assert.Equal(1, engaged.NewValue);

            Assert.Contains(result.Deltas, d =>
                d.TargetId == 1
                && d.Field == DeltaField.ArmyMonthsWithoutBattle
                && d.NewValue == 0);

            Apply(world, result.Deltas);

            // 次月结算：守军走参战分支——士气不变（不 +3 也不 −6），标记消费清零。
            int defenderMorale = world.GetArmy(1)!.Morale;
            Apply(world, system.ResolveMonthlyMorale(world));

            Assert.Equal(defenderMorale, world.GetArmy(1)!.Morale);
            Assert.False(world.GetArmy(1)!.HasEngagedThisMonth);
            Assert.Equal(0, world.GetArmy(1)!.MonthsWithoutBattle);
            Assert.Equal(ArmyStance.Besieging, world.GetArmy(0)!.Stance);   // 连续围城中
            Assert.Equal(1, world.GetCity(1)!.OwnerFactionId);              // 城未易主
        }

        // 连续被围 4 个月：守军士气只被战斗 −4/月，从不 +3 回血、从不 −6 腐化。
        Assert.Equal(75 - Balance.MoraleLossOnDefend * 4, world.GetArmy(1)!.Morale);
        Assert.Equal(0, world.GetArmy(1)!.MonthsWithoutBattle);
        Assert.False(world.GetArmy(1)!.HasMutiniedThisMonth);
        Assert.True(world.GetArmy(1)!.Soldiers > 0);
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

    // ========================================================================
    //  第 6 轮补丁：休整分支 —— 休整部队本月挂起和平腐化
    // ========================================================================

    /// <summary>
    /// 第 6 轮补丁 · 测试 1（正向）：休整挂起腐化。
    /// MonthsWithoutBattle 已达腐化阈值、HasRestedThisMonth=true、Morale=40 时，
    /// 本月结算既不恢复也不腐化——士气保持 40、无士气 delta、计数 +1、
    /// HasRestedThisMonth 翻转 1→0 的 delta 在列。
    /// </summary>
    [Fact]
    public void 休整挂起腐化_本月士气不变()
    {
        var state = WorldWith(Army(morale: 40, soldiers: 1000,
            monthsWithoutBattle: Balance.MoraleDecayNoBattleThreshold - 1));
        state.GetArmy(0)!.HasRestedThisMonth = true;

        var deltas = System.ResolveMonthlyMorale(state);

        // 无士气 delta：既不 +3 恢复也不 −6 腐化。
        Assert.DoesNotContain(deltas, d => d.Field == DeltaField.ArmyMorale);

        // 计数 +1（休整不是战斗）。
        var count = deltas.Single(d => d.Field == DeltaField.ArmyMonthsWithoutBattle);
        Assert.Equal(Balance.MoraleDecayNoBattleThreshold - 1, count.OldValue);
        Assert.Equal(Balance.MoraleDecayNoBattleThreshold, count.NewValue);

        // 休整标记翻转 1→0 的 delta 在列。
        var rested = deltas.Single(d => d.Field == DeltaField.ArmyHasRested);
        Assert.Equal(1, rested.OldValue);
        Assert.Equal(0, rested.NewValue);

        Apply(state, deltas);
        Assert.Equal(40, state.GetArmy(0)!.Morale);
        Assert.False(state.GetArmy(0)!.HasRestedThisMonth);
        Assert.Equal(Balance.MoraleDecayNoBattleThreshold, state.GetArmy(0)!.MonthsWithoutBattle);
    }

    /// <summary>
    /// 第 6 轮补丁 · 测试 2（端到端小循环）：连续休整不哗变。
    /// 12 个周期，每周期先 RestResolver 成功结算（+3、置标记），再跑月度结算。
    /// 初始士气 20：12 周期后士气 > 0，且全程无散兵 delta、无哗变标记置位（NewValue==1）。
    /// </summary>
    [Fact]
    public void 连续休整_12周期不哗变()
    {
        var state = WorldWith(Army(morale: 20, soldiers: 1000));
        var system = System;
        var restResolver = new RestResolver(Balance);

        for (int month = 1; month <= 12; month++)
        {
            // 先休整：+3、置 HasRestedThisMonth 标记。
            var rest = restResolver.Resolve(
                state, Order.CreatePlayerOrder(ActionType.Rest, target: 0, amount: 0, secondary: 0));
            Assert.True(rest.IsSuccess);
            Apply(state, rest.Deltas);

            // 再月度结算：休整分支 → 挂起腐化，士气不变。
            var deltas = system.ResolveMonthlyMorale(state);

            // 全程无散兵 delta、无哗变标记置位。
            Assert.DoesNotContain(deltas, d => d.Field == DeltaField.ArmySoldiers);
            Assert.DoesNotContain(deltas, d =>
                d.Field == DeltaField.ArmyHasMutiniedThisMonth && d.NewValue == 1);

            Apply(state, deltas);

            // 推进到下月，重置势力指令额度与月初标记（下一轮 Rest 才能再次生效）。
            GameClock.AdvanceMonth(state);
        }

        Assert.True(state.GetArmy(0)!.Morale > 0);
        Assert.False(state.GetArmy(0)!.HasMutiniedThisMonth);
        Assert.Equal(1000, state.GetArmy(0)!.Soldiers);
    }

    /// <summary>
    /// 第 6 轮补丁 · 测试 3（反向）：停止休整后腐化恢复。
    /// 接续休整场景后停止 Rest，连续跑月度结算：腐化 −6/月重新生效、
    /// 士气最终降至 0 并触发哗变。
    /// </summary>
    [Fact]
    public void 停止休整后_腐化恢复并最终哗变()
    {
        var state = WorldWith(Army(morale: 20, soldiers: 1000,
            monthsWithoutBattle: Balance.MoraleDecayNoBattleThreshold - 1));
        var system = System;

        // 先休整一个周期，把计数推到阈值、并让休整标记本轮生效。
        var restResolver = new RestResolver(Balance);
        var rest = restResolver.Resolve(
            state, Order.CreatePlayerOrder(ActionType.Rest, target: 0, amount: 0, secondary: 0));
        Assert.True(rest.IsSuccess);
        Apply(state, rest.Deltas);
        Apply(state, system.ResolveMonthlyMorale(state));

        // 此刻计数已 ≥ 阈值（休整月 +1 后落腐化区），停止休整后腐化应 −6/月。
        Assert.True(state.GetArmy(0)!.MonthsWithoutBattle >= Balance.MoraleDecayNoBattleThreshold);

        bool mutinied = false;
        for (int month = 1; month <= 20 && !mutinied; month++)
        {
            Apply(state, system.ResolveMonthlyMorale(state));
            if (state.GetArmy(0)!.HasMutiniedThisMonth)
            {
                mutinied = true;
            }

            GameClock.AdvanceMonth(state);
        }

        // 腐化 −6/月必然把士气磨到 0，并触发哗变（散兵 + 标记置位）。
        Assert.Equal(0, state.GetArmy(0)!.Morale);
        Assert.True(mutinied);
        Assert.True(state.GetArmy(0)!.Soldiers < 1000);
    }

    /// <summary>
    /// 第 6 轮补丁 · 测试 4：休整与参战同月 → 参战优先。
    /// HasEngagedThisMonth 与 HasRestedThisMonth 同为 true：走参战分支
    /// （士气不额外增减、MonthsWithoutBattle 保持），休整标记仍 1→0 翻转。
    /// </summary>
    [Fact]
    public void 休整与参战同月_走参战分支()
    {
        var state = WorldWith(Army(morale: 50, soldiers: 1000,
            monthsWithoutBattle: 0));
        state.GetArmy(0)!.HasEngagedThisMonth = true;
        state.GetArmy(0)!.HasRestedThisMonth = true;

        var deltas = System.ResolveMonthlyMorale(state);

        // 参战分支：无士气 delta（不额外增减）。
        Assert.DoesNotContain(deltas, d => d.Field == DeltaField.ArmyMorale);

        // MonthsWithoutBattle 保持（战斗已清零，不 +1）。
        Assert.DoesNotContain(deltas, d => d.Field == DeltaField.ArmyMonthsWithoutBattle);

        // 休整标记仍 1→0 翻转。
        var rested = deltas.Single(d => d.Field == DeltaField.ArmyHasRested);
        Assert.Equal(1, rested.OldValue);
        Assert.Equal(0, rested.NewValue);

        // 参战标记消费即清。
        var engaged = deltas.Single(d => d.Field == DeltaField.ArmyHasEngagedThisMonth);
        Assert.Equal(1, engaged.OldValue);
        Assert.Equal(0, engaged.NewValue);

        Apply(state, deltas);
        Assert.Equal(50, state.GetArmy(0)!.Morale);
        Assert.Equal(0, state.GetArmy(0)!.MonthsWithoutBattle);
        Assert.False(state.GetArmy(0)!.HasRestedThisMonth);
        Assert.False(state.GetArmy(0)!.HasEngagedThisMonth);
    }
}
