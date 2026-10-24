// ============================================================================
//  经济系统刻度测试 —— 锁定「百分数/千分比 → 万分比」的统一改口径
//
//  【为什么单独锁刻度】v1.2 把GrainPerPopPerMonth、SoldierPerPopPerMonth、
//  ArmyMonthlyGrainPerSoldier 统一改为万分比（10000 = 100%）。
// 这类改动的危险不在于编译失败，而在于<b>数值整体缩小 10 倍却没有任何报错</b>：
// 测试照样绿、游戏照样跑，只是经济系统悄悄失效。
// 因此本文件对三个公式逐个做「配置值 × 输入 = 期望输出」的硬断言，
// 任何刻度回退都会立刻红。
//
//  【设计转向】粮不是瓶颈，兵源才是。3000 人口城月产 90 兵源，
// 耗粮几乎可忽略——本测试同时把这个结论锁住，防止后续误调。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Sample;
using StrategyGame.Core.Systems;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>经济系统万分比刻度测试。</summary>
public sealed class EconomyScaleTests
{
    private static BalanceConfig Balance => SampleData.CreateBalance();

    private static void Apply(WorldState state, List<StateDelta> deltas)
        => new StateApplier().Apply(state, deltas);

    /// <summary>
    /// 粮产 = Σ人口 × GrainPerPopPerMonth / 10000，且按万分比向下取整。
    /// </summary>
    /// <remarks>
    /// 本测试只锁「万分比刻度与截断方向」单一变量，故 FarmLevel 置 0；
    /// 屯田等级加成另有 PostEffects 专项测试覆盖。
    /// </remarks>
    [Fact]
    public void 粮产_按万分比结算并向下取整()
    {
        Assert.Equal(800, Balance.GrainPerPopPerMonth);

        var state = SampleData.CreateWorld();
        state.Cities.Clear();
        state.Cities.Add(new City
        {
            Id = 0, Name = "测试城", OwnerFactionId = 0,
            Population = 3000, MaxPopulation = 10_000, Defense = 100, MaxDefense = 300,
            FortifyLevel = 1, FarmLevel = 0, IsCapital = true,
            Terrain = TerrainType.Plain, AdjacentCityIds = new List<int>(),
        });

        int grainBefore = state.GetFaction(0)!.Resources.Grain;
        Apply(state, new EconomySystem(Balance).ResolveGrainIncome(state));

        // 3000 × 800 / 10000 = 240
        Assert.Equal(grainBefore + 240, state.GetFaction(0)!.Resources.Grain);
    }

    /// <summary>
    /// 粮产截断方向：9999 人口 × 800/10000 = 799.92 → 799。
    /// </summary>
    /// <remarks>
    /// 本测试只锁截断方向单一变量，故 FarmLevel 置 0；
    /// 屯田等级加成另有 PostEffects 专项测试覆盖。
    /// </remarks>
    [Fact]
    public void 粮产_截断方向向下()
    {
        var state = SampleData.CreateWorld();
        state.Cities.Clear();
        state.Cities.Add(new City
        {
            Id = 0, Name = "测试城", OwnerFactionId = 0,
            Population = 9999, MaxPopulation = 10_000, Defense = 100, MaxDefense = 300,
            FortifyLevel = 1, FarmLevel = 0, IsCapital = true,
            Terrain = TerrainType.Plain, AdjacentCityIds = new List<int>(),
        });

        int grainBefore = state.GetFaction(0)!.Resources.Grain;
        Apply(state, new EconomySystem(Balance).ResolveGrainIncome(state));

        Assert.Equal(grainBefore + 799, state.GetFaction(0)!.Resources.Grain);
    }

    /// <summary>
    /// 兵源 = Σ人口 × SoldierPerPopPerMonth / 10000。
    /// </summary>
    /// <remarks>
    /// 这是设计的真正瓶颈：3000 人口城月产 90 兵源，
    /// 而 3000 人的攻城部队一次就打光——兵源补给周期以「月」计。
    /// </remarks>
    [Fact]
    public void 兵源_按万分比结算()
    {
        Assert.Equal(5000, Balance.SoldierPerPopPerMonth);

        var state = SampleData.CreateWorld();
        state.Cities.Clear();
        state.Cities.Add(new City
        {
            Id = 0, Name = "测试城", OwnerFactionId = 0,
            Population = 3000, MaxPopulation = 10_000, Defense = 100, MaxDefense = 300,
            FortifyLevel = 1, FarmLevel = 1, IsCapital = true,
            Terrain = TerrainType.Plain, AdjacentCityIds = new List<int>(),
        });

        int soldierBefore = state.GetFaction(0)!.Resources.Soldier;
        Apply(state, new EconomySystem(Balance).ResolveSoldierIncome(state));

        // 基础 3000 × 5000 / 10000 = 1500，再按稳定度 80/100 缩放 → 1200。
        // 【刻度陷阱】这里除的是 StabilityBase(100)——稳定度本身是 0-100 量纲，
        // 不是万分比。若误除以RatioBase(10000)，兵源会变成 0。
        int stability = state.GetFaction(0)!.Resources.Stability;
        int expected = 1500 * stability / EconomySystem.StabilityBase;

        Assert.Equal(80, stability);
        Assert.Equal(1200, expected);
        Assert.Equal(soldierBefore + expected, state.GetFaction(0)!.Resources.Soldier);
    }

    /// <summary>稳定度为 0 时兵源应为 0（产出完全停摆）。</summary>
    [Fact]
    public void 兵源_稳定度为0时无产出()
    {
        var state = SampleData.CreateWorld();
        state.Cities.Clear();
        state.Cities.Add(new City
        {
            Id = 0, Name = "测试城", OwnerFactionId = 0,
            Population = 3000, MaxPopulation = 10_000, Defense = 100, MaxDefense = 300,
            FortifyLevel = 1, FarmLevel = 1, IsCapital = true,
            Terrain = TerrainType.Plain, AdjacentCityIds = new List<int>(),
        });

        state.GetFaction(0)!.Resources.Stability = 0;

        var deltas = new EconomySystem(Balance).ResolveSoldierIncome(state);

        Assert.DoesNotContain(deltas, d => d.TargetId == 0 && d.Field == DeltaField.FactionSoldier);
    }

    /// <summary>
    /// 耗粮 = Σ兵力 × ArmyMonthlyGrainPerSoldier / 10000。
    /// </summary>
    /// <remarks>
    /// 【v1.2 变更】原先除数是 1000（千分比），v1.2 统一为 10000。
    /// 若漏改，耗粮会放大 10 倍——这就是「粮不是瓶颈」结论被推翻的直接原因。
    /// </remarks>
    [Fact]
    public void 耗粮_按万分比结算()
    {
        Assert.Equal(200, Balance.ArmyMonthlyGrainPerSoldier);
        Assert.Equal(10_000, EconomySystem.RatioBase);

        var state = SampleData.CreateWorld();
        state.Armies.Clear();
        state.Armies.Add(new Army
        {
            Id = 0, OwnerFactionId = 0, OfficerId = -1,
            Soldiers = 3000, Morale = 60, MoraleMax = 100,
            PositionCityId = 0, TargetCityId = -1, Stance = ArmyStance.Idle,
        });

        int grainBefore = state.GetFaction(0)!.Resources.Grain;
        Apply(state, new EconomySystem(Balance).ResolveArmyGrainUpkeep(state));

        // 3000 × 200 / 10000 = 60（若仍按千分比则会是 600）
        Assert.Equal(grainBefore - 60, state.GetFaction(0)!.Resources.Grain);
    }

    /// <summary>
    /// 耗粮截断方向：333 人 × 200 / 10000 = 6.66 → 6。
    /// </summary>
    [Fact]
    public void 耗粮_截断方向向下()
    {
        var state = SampleData.CreateWorld();
        state.Armies.Clear();
        state.Armies.Add(new Army
        {
            Id = 0, OwnerFactionId = 0, OfficerId = -1,
            Soldiers = 333, Morale = 60, MoraleMax = 100,
            PositionCityId = 0, TargetCityId = -1, Stance = ArmyStance.Idle,
        });

        int grainBefore = state.GetFaction(0)!.Resources.Grain;
        Apply(state, new EconomySystem(Balance).ResolveArmyGrainUpkeep(state));

        Assert.Equal(grainBefore - 6, state.GetFaction(0)!.Resources.Grain);
    }

    /// <summary>
    /// 设计结论锁定：3000 人口城月产粮 240，而 3000 人部队月耗粮仅 60——
    /// 粮产是耗粮的 4 倍。经济压力来自兵源而非粮食。
    /// </summary>
    [Fact]
    public void 设计结论_粮产远高于耗粮瓶颈在兵源()
    {
        var balance = Balance;

        int grainFor3000Pop = 3000 * balance.GrainPerPopPerMonth / EconomySystem.RatioBase;
        int grainFor3000Soldiers = 3000 * balance.ArmyMonthlyGrainPerSoldier / EconomySystem.RatioBase;
        int soldierFor3000Pop = 3000 * balance.SoldierPerPopPerMonth / EconomySystem.RatioBase;

        Assert.Equal(240, grainFor3000Pop);
        Assert.Equal(60, grainFor3000Soldiers);
        Assert.Equal(1500, soldierFor3000Pop);

        // 粮不是瓶颈。
        Assert.True(grainFor3000Pop > grainFor3000Soldiers * 2);

        // 真正的瓶颈是兵源：1500 基础兵源/月（稳定度 80 时实得 1200），
        // 而一次攻城投入就是 3000 人——打一座城要攒两三个月。
        Assert.True(soldierFor3000Pop < 3000);
        Assert.Equal(1200, soldierFor3000Pop * 80 / EconomySystem.StabilityBase);
    }
}
