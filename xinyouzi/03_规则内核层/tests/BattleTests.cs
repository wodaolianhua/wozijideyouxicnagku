// ============================================================================
//  战斗系统测试 —— 严格对照 3_战斗系统设计.md 第七节「验收标准」
//
//  【v1.2 覆盖的 8 个场景】测试名与设计文档一一对应，可直接对照：
//   均势 / 强将 / 弱将 / 兵力不足 / 兵力悬殊 / 死攻坚城 / 强将死攻 / 守军坚城
//
//  【两条独立公式】lossRate（攻方自损）与 killRate（打掉守军）方向相反：
//     城防↑  → lossRate↑（更亏）且 killRate↓（更难打）
//     统帅强↑ → lossRate↓（更省）且 killRate↑（更快打）
//  两式共用一个系数会产生「强将反而破城更慢」的逻辑倒错，这是 v1.2 的核心修正。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Resolvers;
using StrategyGame.Core.Sample;
using StrategyGame.Core.Systems;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>
/// 战斗结算器测试。覆盖设计文档第七节的全部验收标准。
/// </summary>
public sealed class BattleSystemTests
{
    private static BalanceConfig Balance => SampleData.CreateBalance();

    private const int TenThousand = DesignConstants.TenThousandBase;

    /// <summary>
    /// 跑一场标准攻城战，不带专长、不带士气惩罚。
    /// </summary>
    private static BattleResult Siege(
        int attackerSoldiers,
        int defenderSoldiers,
        int cityDefense,
        int attackerCommand,
        int defenderCommand)
        => new BattleSystem(Balance).Resolve(
            attackerSoldiers,
            defenderSoldiers,
            cityDefense,
            attackerCommand,
            defenderCommand,
            isSiege: true,
            attackerHasSiegeSpecialty: false,
            attackerHasCavalrySpecialty: false);

    // ========================================================================
    //  8 个场景 —— 与设计文档取值校验表逐行对应
    // ========================================================================

    /// <summary>
    /// 场景 1：均势攻城 3000 攻 1000 守，城防 500，统帅 50/50 → 攻陷。
    /// </summary>
    /// <remarks>
    /// lossRate 0.350、killRate 0.405。3000 × 0.405 = 1215 ≥ 1000，守军被打光。
    /// 这确立了核心门槛：均势下需约 3 倍于守军的兵力才能攻陷。
    /// </remarks>
    [Fact]
    public void Battle_均势3000攻1000守_应攻陷()
    {
        var result = Siege(3000, 1000, 500, 50, 50);

        Assert.Equal(0.350, result.LossRateTenThousand / (double)TenThousand, 3);
        Assert.Equal(0.405, result.KillRateTenThousand / (double)TenThousand, 3);
        Assert.Equal(1050, result.AttackerLoss);
        Assert.Equal(BattleOutcome.Capture, result.Outcome);
    }

    /// <summary>
    /// 场景 2：强将攻城（75/ 50）→ 攻陷，且攻方更省、守方更惨。
    /// </summary>
    /// <remarks>
    /// 强将的双重收益必须同时体现：lossRate 从 0.350 降到 0.280（攻方少损 210），
    /// killRate 从 0.405 升到 0.486（守方被打得更快）。
    /// 只测一个方向会漏掉「共用一个 lossRate」的历史 bug。
    /// </remarks>
    [Fact]
    public void Battle_强将3000攻1000守_应攻陷()
    {
        var even = Siege(3000, 1000, 500, 50, 50);
        var strong = Siege(3000, 1000, 500, 75, 50);

        Assert.Equal(0.280, strong.LossRateTenThousand / (double)TenThousand, 3);
        Assert.Equal(0.486, strong.KillRateTenThousand / (double)TenThousand, 3);
        Assert.Equal(840, strong.AttackerLoss);
        Assert.Equal(BattleOutcome.Capture, strong.Outcome);

        // 强将的价值必须双向成立
        Assert.True(strong.LossRateTenThousand < even.LossRateTenThousand, "强将应降低攻方战损");
        Assert.True(strong.KillRateTenThousand > even.KillRateTenThousand, "强将应提高守方被歼率");
    }

    /// <summary>场景 3：弱将攻城（40 / 50）→ 仍能攻陷，但攻方多损、守方更抗打。</summary>
    [Fact]
    public void Battle_弱将3000攻1000守_应攻陷()
    {
        var weak = Siege(3000, 1000, 500, 40, 50);

        Assert.Equal(0.389, weak.LossRateTenThousand / (double)TenThousand, 3);
        Assert.Equal(0.360, weak.KillRateTenThousand / (double)TenThousand, 3);
        Assert.True(weak.AttackerLoss > 1050, "弱将应比均势多损兵力");
        Assert.Equal(BattleOutcome.Capture, weak.Outcome);
    }

    /// <summary>
    /// 场景 4：兵力不足（1200 攻 1000 守）→ 未决，给「再攒一个月」的余地。
    /// </summary>
    /// <remarks>
    /// 守方损失 486 &lt; 1000 → 守军未被打光。
    /// 判未决而非击退是刻意的：避免玩家因一次计算失误被直接打退。
    /// </remarks>
    [Fact]
    public void Battle_兵力不足1200攻1000守_应未决()
    {
        var result = Siege(1200, 1000, 500, 50, 50);

        Assert.Equal(486, result.DefenderLoss);
        Assert.True(result.DefenderLoss < 1000);
        Assert.Equal(BattleOutcome.Stalemate, result.Outcome);
    }

    /// <summary>场景 5：兵力悬殊（800 攻 1000 守）→ 未决。</summary>
    [Fact]
    public void Battle_兵力悬殊800攻1000守_应未决()
    {
        var result = Siege(800, 1000, 500, 50, 50);

        Assert.Equal(324, result.DefenderLoss);
        Assert.True(result.DefenderLoss < 1000);
        Assert.Equal(BattleOutcome.Stalemate, result.Outcome);
    }

    /// <summary>
    /// 场景 6：死攻坚城（城防 1500，均势）→ 战损率恰压 0.70 阈值，判击退。
    /// </summary>
    /// <remarks>
    /// 这是「先屯田发展」的设计依据：城墙不是靠硬堆兵力能翻过去的。
    /// </remarks>
    [Fact]
    public void Battle_死攻城防1500_应击退()
    {
        var result = Siege(3000, 1000, 1500, 50, 50);

        Assert.Equal(0.700, result.LossRateTenThousand / (double)TenThousand, 3);
        Assert.True(result.LossRateTenThousand >= Balance.SiegeThresholdLoss);
        Assert.Equal(BattleOutcome.Repelled, result.Outcome);
    }

    /// <summary>
    /// 场景 7：强将死攻坚城 → lossRate 压到 0.560，未过阈值；killRate 仅 0.162，判未决。
    /// </summary>
    /// <remarks>
    /// 【用人可以把不可能变成可能，但不能凭空造优势】
    /// 换强将把「必败的击退」变成「僵持的未决」——这是全项目最核心的玩法表达。
    /// </remarks>
    [Fact]
    public void Battle_强将死攻城防1500_应未决()
    {
        var result = Siege(3000, 1000, 1500, 75, 50);

        Assert.Equal(0.560, result.LossRateTenThousand / (double)TenThousand, 3);
        Assert.Equal(0.162, result.KillRateTenThousand / (double)TenThousand, 3);
        Assert.True(result.LossRateTenThousand < Balance.SiegeThresholdLoss);
        Assert.Equal(486, result.DefenderLoss);
        Assert.Equal(BattleOutcome.Stalemate, result.Outcome);
    }

    /// <summary>场景 8：守军坚城（2000 攻 4000 守，城防 800）→ 未决。</summary>
    [Fact]
    public void Battle_守军坚城2000攻4000守_应未决()
    {
        var result = Siege(2000, 4000, 800, 75, 75);

        Assert.Equal(0.455, result.LossRateTenThousand / (double)TenThousand, 3);
        Assert.Equal(648, result.DefenderLoss);
        Assert.True(result.DefenderLoss < 4000);
        Assert.Equal(BattleOutcome.Stalemate, result.Outcome);
    }

    // ========================================================================
    //  边界必测项
    // ========================================================================

    /// <summary>
    /// 守方损失恒 ≤ 守城兵力：投入 1 兵打 10000 守军不得产生负兵力。
    /// </summary>
    /// <remarks>
    /// 【为什么这条必须存在】旧公式用「守军规模 × 系数」算守方损失，
    /// 投入 1 兵时若不截断就会出现「守方损失 > 守军」甚至负数兵力。
    /// 现公式按攻方投入计，截断由 min(投入 × killRate, 守军) 保证。
    /// </remarks>
    [Fact]
    public void Battle_投入1兵打10000守军_守方损失不得超过守军()
    {
        var result = Siege(1, 10_000, 500, 50, 50);

        Assert.InRange(result.DefenderLoss, 0, 10_000);
        Assert.Equal(0, result.DefenderLoss);
    }

    /// <summary>守方损失在各种极端配比下都不得为负，也不得超过守军规模。</summary>
    [Theory]
    [InlineData(1, 10_000)]
    [InlineData(1, 1)]
    [InlineData(10_000, 1)]
    [InlineData(999, 1000)]
    [InlineData(1000, 999)]
    public void Battle_守方损失恒在0到守军之间(int atk, int def)
    {
        var result = Siege(atk, def, 500, 50, 50);

        Assert.InRange(result.DefenderLoss, 0, def);
    }

    /// <summary>
    /// <c>commanderRatio</c> 分母为（攻 + 守），不含 +1——用极端值锁定。
    /// </summary>
    /// <remarks>
    /// 若误用 <c>攻/(攻+守+1)</c>，这两个比值会变成 4975 与 4976（有 +1 时
    /// 双方统帅悬殊会被拉向 0.5，强将/ 弱将的差异被抹掉）。
    /// 无 +1 口径下应为精确的 10000 与 0。
    /// </remarks>
    [Fact]
    public void Battle_commanderRatio分母不含加一()
    {
        Assert.Equal(TenThousand, BattleSystem.CalculateCommanderRatio(100, 0));
        Assert.Equal(0, BattleSystem.CalculateCommanderRatio(0, 100));

        // 悬殊值：无 +1 时必须精确落在整数边界上，不被拉向 0.5。
        Assert.Equal(9_950, BattleSystem.CalculateCommanderRatio(199, 1));
        Assert.Equal(50, BattleSystem.CalculateCommanderRatio(1, 199));
    }

    /// <summary>双方统帅皆0 时取中性值 0.5，而不是 0。</summary>
    /// <remarks>
    /// 若取 0，killRate 会归零、「无将守城」变成打不动，
    /// 且 lossRate 会翻倍成最强状态——比 +1 更严重的逻辑倒错。
    /// </remarks>
    [Fact]
    public void Battle_双方统帅皆0_取中性值()
    {
        Assert.Equal(DesignConstants.NeutralCommanderRatio,
            BattleSystem.CalculateCommanderRatio(0, 0));

        var result = Siege(3000, 1000, 500, 0, 0);

        Assert.Equal(0.350, result.LossRateTenThousand / (double)TenThousand, 3);
        Assert.Equal(0.405, result.KillRateTenThousand / (double)TenThousand, 3);
    }

    /// <summary>守方无统帅时，攻方打起来最省——守军无人统率是真实弱点。</summary>
    [Fact]
    public void Battle_守方无统帅_攻方战损最低()
    {
        var result = Siege(3000, 1000, 500, 50, 0);

        // 攻方统帅 50、守方 0 → commanderRatio = 10000 → (2−2r) = 0 → 攻方零战损。
        Assert.Equal(0, result.LossRateTenThousand);

        // 同一侧效应让 killRate 冲高：BaseKill 2700 × (2−0.5) × 2.0 = 8100。
        // killRate 不做上限钳制——守方损失另由 min(投入×killRate, 守军)兜底，
        // 钳到 10000 只会掩盖「极端统帅差」的诊断信息。
        Assert.Equal(8100, result.KillRateTenThousand);
        Assert.Equal(0, result.AttackerLoss);
        Assert.Equal(1000, result.DefenderLoss);
    }

    // ========================================================================
    //  野战与专长
    // ========================================================================

    /// <summary>野战没有城墙消耗，战损应低于同等条件的攻城。</summary>
    [Fact]
    public void Battle_野战战损低于攻城()
    {
        var battle = new BattleSystem(Balance);

        var siege = battle.Resolve(1000, 1000, 500, 50, 50, true, false, false);
        var field = battle.Resolve(1000, 1000, 500, 50, 50, false, false, false);

        Assert.True(field.LossRateTenThousand < siege.LossRateTenThousand);
    }

    /// <summary>野战均势：design 3.5 的 0.21 / 0.27。</summary>
    [Fact]
    public void Battle_野战均势_战损021()
    {
        var result = new BattleSystem(Balance)
            .Resolve(1000, 1000, 0, 50, 50, false, false, false);

        Assert.Equal(0.210, result.LossRateTenThousand / (double)TenThousand, 3);
    }

    /// <summary>Siege 专长应降低攻城战损。</summary>
    [Fact]
    public void Battle_Siege专长降低攻城战损()
    {
        var battle = new BattleSystem(Balance);

        var without = battle.Resolve(1000, 1000, 500, 50, 50, true, false, false);
        var with = battle.Resolve(1000, 1000, 500, 50, 50, true, true, false);

        Assert.True(with.LossRateTenThousand < without.LossRateTenThousand);
    }

    /// <summary>Cavalry 专长应降低野战战损。</summary>
    [Fact]
    public void Battle_Cavalry专长降低野战战损()
    {
        var battle = new BattleSystem(Balance);

        var without = battle.Resolve(1000, 1000, 0, 50, 50, false, false, false);
        var with = battle.Resolve(1000, 1000, 0, 50, 50, false, false, true);

        Assert.True(with.LossRateTenThousand < without.LossRateTenThousand);
    }

    // ========================================================================
    //  判定顺序：先击退，后攻陷
    // ========================================================================

    /// <summary>
    /// 判定顺序不可调换：lossRate 已过阈值时，即便守军被打光也必须判击退。
    /// </summary>
    /// <remarks>
    /// 若先判攻陷，「强将死攻坚城」这类高战损的强攻在守军恰好被打光时
    /// 会同时满足两个条件，顺序不同结果就不同。
    /// 「打不动就先撤」正是先判击退的设计目的。
    /// </remarks>
    [Fact]
    public void Battle_战损过阈值时优先判击退而非攻陷()
    {
        // 城防 1000、统帅 30/70：lossRate = 3500×1.5×1.4 = 7350 ≥ 7000，
        // 同时 killRate = 2700×1.0×0.6 = 1620，1000 投入可换掉 162 ≥ 100 守军。
        // 两个条件同时成立，结果完全取决于判定顺序。
        var battle = new BattleSystem(Balance);
        var result = battle.Resolve(
            1000, 100, 1000, 30, 70, true, false, false);

        Assert.True(result.LossRateTenThousand >= Balance.SiegeThresholdLoss);
        Assert.True(result.DefenderLoss >= 100, "守军应被打光");
        Assert.Equal(BattleOutcome.Repelled, result.Outcome);
    }

    // ========================================================================
    //  未决不重复扣兵
    // ========================================================================

    /// <summary>
    /// 未决时双方按正常公式结算，不得再额外扣一次 StalemateLossRate。
    /// </summary>
    /// <remarks>
    /// 旧实现里「守方损失」和「未决双方各损 10%」重复计算了一次，
    /// 导致实际扣了两次。该字段现已废弃，实现不得读取它。
    /// </remarks>
    [Fact]
    public void Battle_未决按正常公式结算不额外扣兵()
    {
        var balance = Balance;

        // 把已废弃字段设成极大值，若实现误读它，未决损失会离谱地放大。
        balance.StalemateLossRate = 9000;
        var battle = new BattleSystem(balance);

        var result = battle.Resolve(1200, 1000, 500, 50, 50, true, false, false);

        Assert.Equal(BattleOutcome.Stalemate, result.Outcome);
        Assert.Equal(1200 * 3500 / TenThousand, result.AttackerLoss);
        Assert.Equal(1200 * 4050 / TenThousand, result.DefenderLoss);
    }

    // ========================================================================
    //  系数必须来自配置
    // ========================================================================

    /// <summary>更换 BaseLoss 配置应改变战损率，证明系数确实来自配置。</summary>
    [Fact]
    public void Battle_更换BaseLoss配置应改变战损()
    {
        var before = Siege(1000, 1000, 500, 50, 50);

        var modified = Balance;
        modified.BaseLoss = 7000;
        var after = new BattleSystem(modified)
            .Resolve(1000, 1000, 500, 50, 50, true, false, false);

        Assert.True(after.LossRateTenThousand > before.LossRateTenThousand);
    }

    /// <summary>更换 BaseKill 配置应改变守方损失，证明该系数来自配置。</summary>
    [Fact]
    public void Battle_更换BaseKill配置应改变守方损失()
    {
        var before = Siege(1200, 5000, 500, 50, 50);

        var modified = Balance;
        modified.BaseKill = 5400;
        var after = new BattleSystem(modified)
            .Resolve(1200, 5000, 500, 50, 50, true, false, false);

        Assert.True(after.DefenderLoss > before.DefenderLoss);
    }

    /// <summary>战斗结算必须是纯函数：重复调用结果一致。</summary>
    [Fact]
    public void Battle_重复调用结果一致()
    {
        var battle = new BattleSystem(Balance);

        var first = battle.Resolve(1000, 800, 400, 60, 55, true, true, false);
        var second = battle.Resolve(1000, 800, 400, 60, 55, true, true, false);

        Assert.Equal(first, second);
    }
}

/// <summary>
/// 出兵结算器的战斗集成测试。
/// </summary>
public sealed class DispatchBattleTests
{
    private static BalanceConfig Balance => SampleData.CreateBalance();

    /// <summary>
    /// 成功路径：投入足量兵力攻陷敌方城池，城池归属变更。
    /// </summary>
    /// <remarks>
    /// 【兵力配比】按新的 killRate 公式，玩家投入需达到守军的约 3 倍
    /// 才能攻陷（3_战斗系统设计.md 3.1「均势攻城约需 3 倍兵力」）。
    /// </remarks>
    [Fact]
    public void Resolve_压倒性兵力应攻陷城池()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Balance);

        state.GetArmy(1)!.PositionCityId = 1;
        state.GetArmy(1)!.Soldiers = 80;

        // 先确认 150 人不足以攻陷（落入未决）：killRate ≈ 4074，150×0.4074 ≈ 61 < 80。
        var light = resolver.Resolve(
            state, Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 150, secondary: 0));

        Assert.Contains(light.Deltas, d =>
            d.TargetType == DeltaTarget.Army
            && d.TargetId == 0
            && d.Field == DeltaField.ArmyStance
            && d.NewValue == (int)ArmyStance.Besieging);

        // 再用足量兵力攻下同一座城
        var overwhelming = SampleData.CreateWorld();
        overwhelming.GetArmy(1)!.PositionCityId = 1;
        overwhelming.GetArmy(1)!.Soldiers = 80;
        overwhelming.GetArmy(0)!.Soldiers = 3000;

        var captureResult = resolver.Resolve(
            overwhelming,
            Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 3000, secondary: 0));

        Assert.True(captureResult.IsSuccess);
        Assert.Contains(captureResult.Deltas, d =>
            d.TargetType == DeltaTarget.City
            && d.TargetId == 1
            && d.Field == DeltaField.CityOwnerFactionId
            && d.NewValue == 0);

        new StateApplier().Apply(overwhelming, captureResult.Deltas);

        Assert.Equal(0, overwhelming.GetCity(1)!.OwnerFactionId);
        Assert.Equal(1, overwhelming.GetArmy(0)!.PositionCityId);
        Assert.InRange(overwhelming.GetArmy(0)!.Soldiers, 1, 3000);
    }

    /// <summary>成功路径：战损过高被击退，部队退回出发城。</summary>
    [Fact]
    public void Resolve_死攻应退回出发城()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Balance);

        // 守军必须在目标城内，且城防高到必然击退。
        state.GetArmy(1)!.PositionCityId = 1;
        state.GetCity(1)!.Defense = 2000;

        var result = resolver.Resolve(
            state, Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 300, secondary: 0));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Deltas, d =>
            d.Field == DeltaField.ArmyPositionCityId && d.NewValue == 0);

        new StateApplier().Apply(state, result.Deltas);

        Assert.Equal(0, state.GetArmy(0)!.PositionCityId);
        Assert.Equal(ArmyStance.Idle, state.GetArmy(0)!.Stance);
        Assert.Equal(1, state.GetCity(1)!.OwnerFactionId);
    }

    /// <summary>成功路径：未决时进入围城状态，下月不可再对该城出兵。</summary>
    [Fact]
    public void Resolve_未决应进入围城状态()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Balance);

        state.GetArmy(1)!.PositionCityId = 1;
        state.GetArmy(1)!.Soldiers = 5000;

        var result = resolver.Resolve(
            state, Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 100, secondary: 0));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Deltas, d =>
            d.Field == DeltaField.ArmyStance && d.NewValue == (int)ArmyStance.Besieging);
    }

    /// <summary>
    /// 被围困的城池下月不可再发起 Dispatch。
    /// </summary>
    /// <remarks>
    /// 【契约缺口】设计文档写 <c>RejectedTargetSieged</c>，
    /// 但契约 v1.2 的 <see cref="ResolveResultCode"/> 无该成员。
    /// 本实现返回 <see cref="ResolveResultCode.RejectedTargetOccupied"/>——
    /// 其契约注释已含「或正在被围」，语义一致。此缺口已上报。
    /// </remarks>
    [Fact]
    public void Resolve_被围困城池不得再次出兵()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Balance);

        state.Armies.Add(new Army
        {
            Id = 5, OwnerFactionId = 0, OfficerId = -1,
            Soldiers = 100, Morale = 50, MoraleMax = 100,
            PositionCityId = 1, TargetCityId = 1, Stance = ArmyStance.Besieging,
        });

        var result = resolver.Resolve(
            state, Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 50, secondary: 0));

        Assert.Equal(ResolveResultCode.RejectedTargetOccupied, result.Code);
    }

    /// <summary>验收：玩家亲征时不叠加 Officer 加成。</summary>
    [Fact]
    public void Resolve_亲征不叠加人才加成()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Balance);

        state.Player.Command = 30;
        state.GetArmy(0)!.OfficerId = 3;
        Assert.Equal(80, state.GetEffectiveCommand(0));

        var result = resolver.Resolve(
            state, Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 100, secondary: 0));

        Assert.True(result.IsSuccess);
        new StateApplier().Apply(state, result.Deltas);

        Assert.True(state.Player.IsPersonallyCommanding);
        Assert.Equal(0, state.Player.CommandingArmyId);
        Assert.Equal(-1, state.GetArmy(0)!.OfficerId);
        Assert.Equal(30, state.GetEffectiveCommand(0));
    }

    /// <summary>非玩家下达的 Dispatch 不得触发亲征。</summary>
    [Fact]
    public void Resolve_Ai出兵不触发亲征()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Balance);

        var result = resolver.Resolve(
            state, Order.CreateAiOrder(ActionType.Dispatch, factionId: 0, target: 1, amount: 50, secondary: 0));

        Assert.True(result.IsSuccess);
        new StateApplier().Apply(state, result.Deltas);

        Assert.False(state.Player.IsPersonallyCommanding);
    }

    /// <summary>调用战斗结算不得修改 state（纯净性）。</summary>
    [Fact]
    public void Resolve_战斗结算不得修改状态()
    {
        var state = SampleData.CreateWorld();
        var pristine = state.Clone();
        var resolver = new DispatchResolver(Balance);

        resolver.Resolve(
            state, Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 100, secondary: 0));

        Assert.Null(StateSnapshot.Diff(pristine, state));
    }

    /// <summary>战斗必须产出至少一条事件记录。</summary>
    [Fact]
    public void Resolve_必须产出事件()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Balance);

        var result = resolver.Resolve(
            state, Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: 100, secondary: 0));

        Assert.NotEmpty(result.Events);
        Assert.False(string.IsNullOrWhiteSpace(result.Events[0].Message));
    }

    /// <summary>
    /// 回归测试：投入的兵力不能凭空消失。
    /// </summary>
    /// <remarks>
    /// 【曾出现的 bug】实现里先写「原兵力 − 投入兵力」，
    /// 又在战斗分支按「投入兵力 − 损失」再写一次，同一字段被扣两次。
    /// 本测试锁定「投入 3000、损失 X、最终应为 3000 − X」这一不变式。
    /// </remarks>
    [Fact]
    public void Resolve_攻方兵力应等于投入减损失()
    {
        var state = SampleData.CreateWorld();
        var resolver = new DispatchResolver(Balance);

        state.GetArmy(1)!.PositionCityId = 1;
        state.GetArmy(1)!.Soldiers = 5000;
        state.GetArmy(0)!.Soldiers = 3000;

        const int Dispatched = 2000;
        var result = resolver.Resolve(
            state, Order.CreatePlayerOrder(ActionType.Dispatch, target: 1, amount: Dispatched, secondary: 0));

        new StateApplier().Apply(state, result.Deltas);

        var balance = Balance;
        var battle = new BattleSystem(balance);

        // 复算必须与Resolver 内部取的口径一致：
        //   攻方统帅 = 亲征玩家统帅 60；守方统帅 = 守城部队统帅（陈庆之 92）。
        // 统帅比 60/(60+92) = 3947，城防 90 → defenseFactor 900。
        // lossRate = 3500 × (0.5+0.09) × (2−2×0.3947) = 2499。
        int attackerCommand = state.Player.Command;
        int defenderCommand = 92;

        int expectedLoss = Dispatched * battle.CalculateLossRateTenThousand(
            state.GetCity(1)!.Defense, attackerCommand, defenderCommand, isSiege: true)
            / DesignConstants.TenThousandBase;

        Assert.Equal(Dispatched - expectedLoss, state.GetArmy(0)!.Soldiers);
    }
}

/// <summary>
/// 胜负判定测试。方向必须与设计第五节一致。
/// </summary>
public sealed class VictoryCheckerTests
{
    /// <summary>玩家仅剩一城 → 失败（不是胜利）。</summary>
    [Fact]
    public void Evaluate_玩家仅剩一城_应判失败()
    {
        var state = SampleData.CreateWorld();
        state.GetCity(3)!.OwnerFactionId = 1;

        var result = new VictoryChecker().Evaluate(state);

        Assert.Equal(GameOutcome.Defeat, result.Outcome);
        Assert.True(result.GameOver);
    }

    /// <summary>玩家全部城池易主 → 失败。</summary>
    [Fact]
    public void Evaluate_玩家失去所有城池_应判失败()
    {
        var state = SampleData.CreateWorld();
        state.GetCity(0)!.OwnerFactionId = 1;
        state.GetCity(3)!.OwnerFactionId = 1;

        var result = new VictoryChecker().Evaluate(state);

        Assert.Equal(GameOutcome.Defeat, result.Outcome);
    }

    /// <summary>玩家拥有 80% 以上城池 → 胜利。</summary>
    [Fact]
    public void Evaluate_玩家占领多数城池_应判胜利()
    {
        var state = SampleData.CreateWorld();
        state.GetCity(1)!.OwnerFactionId = 0;
        state.GetCity(2)!.OwnerFactionId = 0;
        state.GetCity(4)!.OwnerFactionId = 0;
        state.GetCity(5)!.OwnerFactionId = 0;

        var result = new VictoryChecker().Evaluate(state);

        Assert.Equal(GameOutcome.Victory, result.Outcome);
        Assert.True(result.GameOver);
    }

    /// <summary>正常中局不应分出胜负。</summary>
    [Fact]
    public void Evaluate_正常中局应继续()
    {
        var state = SampleData.CreateWorld();
        var result = new VictoryChecker().Evaluate(state);

        Assert.Equal(GameOutcome.Ongoing, result.Outcome);
        Assert.False(result.GameOver);
    }

    /// <summary>超过 72 个月 → 强制结算。</summary>
    [Fact]
    public void Evaluate_超过月数上限_应按时结算()
    {
        var state = SampleData.CreateWorld();
        state.Year = 7;
        state.Month = 1;

        var result = new VictoryChecker().Evaluate(state);

        Assert.Equal(GameOutcome.TimeoutSettled, result.Outcome);
        Assert.True(result.GameOver);
    }

    /// <summary>未到 72 个月不应超时。</summary>
    [Fact]
    public void Evaluate_未到月数上限_不应超时()
    {
        var state = SampleData.CreateWorld();
        state.Year = 6;
        state.Month = 11;

        var result = new VictoryChecker().Evaluate(state);

        Assert.Equal(GameOutcome.Ongoing, result.Outcome);
    }
}

/// <summary>
/// 粮耗测试。刻度已在v1.2 统一为万分比。
/// </summary>
public sealed class ArmyGrainUpkeepTests
{
    /// <summary>驻军每月耗粮，兵力越多耗粮越多。</summary>
    [Fact]
    public void ResolveArmyGrainUpkeep_兵力越多耗粮越多()
    {
        var state = SampleData.CreateWorld();
        int grainBefore = state.GetFaction(0)!.Resources.Grain;

        var economy = new EconomySystem(SampleData.CreateBalance());
        var deltas = economy.ResolveArmyGrainUpkeep(state);

        StateDelta? delta = deltas.FirstOrDefault(d => d.TargetId == 0);
        Assert.NotNull(delta);
        Assert.True(delta!.NewValue < delta.OldValue);

        new StateApplier().Apply(state, deltas);
        Assert.True(state.GetFaction(0)!.Resources.Grain < grainBefore);
    }

    /// <summary>耗粮必须与兵力成正比。</summary>
    [Fact]
    public void ResolveArmyGrainUpkeep_与兵力成正比()
    {
        var small = SampleData.CreateWorld();
        var large = SampleData.CreateWorld();
        large.GetArmy(0)!.Soldiers *= 2;

        var economy = new EconomySystem(SampleData.CreateBalance());

        long smallCost = economy.ResolveArmyGrainUpkeep(small)
            .Where(d => d.TargetId == 0)
            .Sum(d => (long)d.OldValue - d.NewValue);

        long largeCost = economy.ResolveArmyGrainUpkeep(large)
            .Where(d => d.TargetId == 0)
            .Sum(d => (long)d.OldValue - d.NewValue);

        Assert.Equal(smallCost * 2, largeCost);
    }

    /// <summary>粮不得被扣成负数——经济系统不做「粮不够就崩」的模型。</summary>
    [Fact]
    public void ResolveArmyGrainUpkeep_粮不得为负()
    {
        var state = SampleData.CreateWorld();
        state.GetFaction(0)!.Resources.Grain = 1;

        var economy = new EconomySystem(SampleData.CreateBalance());
        var deltas = economy.ResolveArmyGrainUpkeep(state);

        new StateApplier().Apply(state, deltas);

        Assert.True(state.GetFaction(0)!.Resources.Grain >= 0);
    }

    /// <summary>耗粮公式：兵力 × ArmyMonthlyGrainPerSoldier / 10000（万分比）。</summary>
    [Fact]
    public void ResolveArmyGrainUpkeep_符合万分比公式()
    {
        var state = SampleData.CreateWorld();
        var balance = SampleData.CreateBalance();

        state.GetFaction(1)!.Resources.Grain = 100_000;
        int soldiers = state.Armies.Where(a => a.OwnerFactionId == 1).Sum(a => a.Soldiers);

        var economy = new EconomySystem(balance);
        var deltas = economy.ResolveArmyGrainUpkeep(state);

        StateDelta? delta = deltas.FirstOrDefault(d => d.TargetId == 1);
        Assert.NotNull(delta);

        long expected = (long)soldiers * balance.ArmyMonthlyGrainPerSoldier / 10_000;
        Assert.Equal(expected, (long)delta!.OldValue - delta.NewValue);
    }
}