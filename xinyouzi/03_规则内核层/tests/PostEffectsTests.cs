// ============================================================================
//  岗位取才专项测试 —— 锁定 D12「换人可观测变化」核心链路
// ============================================================================
//  第 4 轮补丁把「用人 → 粮产」的占位假实现（数人头×5%）落成裁决公式：
//     城市人才加成(万分比) = G × Governance + 专长查表
//     每城月粮产 = 人口 × GrainPerPopPerMonth/10000 × 屯田等级加成 × 人才加成
//  本文件对粮产逐字段硬断言，并把 Develop / Fortify 岗位折扣、Recruit 无政务
//  修正一起锁住。期望值均已按裁决公式代算，是 D12 的锁，不得为凑实现而改。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Resolvers;
using StrategyGame.Core.Sample;
using StrategyGame.Core.Systems;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>岗位取才（粮产加成 / 岗位折扣）专项测试。</summary>
public sealed class PostEffectsTests
{
    private static BalanceConfig Balance => SampleData.CreateBalance();

    private static void Apply(WorldState state, List<StateDelta> deltas)
        => new StateApplier().Apply(state, deltas);

    /// <summary>构造 1 势力 + 1 城的极简世界，便于手工挂人才。</summary>
    private static WorldState CreateSingleCityWorld(int population = 3000, int farmLevel = 0)
    {
        return new WorldState
        {
            Year = 1,
            Month = 1,
            Seed = 12345,
            Difficulty = DifficultyLevel.Normal,
            ActionsThisMonth = 0,
            Player = new PlayerProfile { Id = Order.PlayerId, Name = "玩家", Command = 60 },
            Factions = new List<Faction>
            {
                new Faction
                {
                    Id = 0, Name = "汉", IsPlayer = true, CapitalCityId = 0,
                    Resources = new Resources
                    {
                        Grain = 5000, Silver = 5000, Soldier = 500,
                        Morale = 80, Stability = 80,
                    },
                },
            },
            Cities = new List<City>
            {
                new City
                {
                    Id = 0, Name = "测试城", OwnerFactionId = 0,
                    Population = population, MaxPopulation = 100_000,
                    Defense = 100, MaxDefense = 300,
                    FortifyLevel = 0, FarmLevel = farmLevel, IsCapital = true,
                    Terrain = TerrainType.Plain, AdjacentCityIds = new List<int>(),
                },
            },
            Armies = new List<Army>(),
            Officers = new List<Officer>(),
            PendingOrders = new List<PendingOrder>(),
            EventLog = new List<EventRecord>(),
        };
    }

    /// <summary>在城 0 手工挂一名已生效在岗人才。</summary>
    private static void AttachOfficer(
        WorldState state,
        int id,
        int governance,
        OfficerSpecialty specialty,
        PostType post,
        int postedSinceMonths)
    {
        state.Officers.Add(new Officer
        {
            Id = id, Name = $"人才{id}", OwnerFactionId = 0, PostCityId = 0,
            Post = post, PostedSinceMonths = postedSinceMonths,
            Command = 50, Governance = governance, Loyalty = 80, Age = 30,
            IsAlive = true, Specialty = specialty,
        });
    }

    /// <summary>结算粮产并返回势力 0 的粮产增量。</summary>
    private static int ResolveGrainIncrement(WorldState state)
    {
        int before = state.GetFaction(0)!.Resources.Grain;
        Apply(state, new EconomySystem(Balance).ResolveGrainIncome(state));
        return state.GetFaction(0)!.Resources.Grain - before;
    }

    // ======================= 粮产：人才加成 =======================

    [Fact]
    public void 粮产_空岗_无加成()
    {
        var state = CreateSingleCityWorld();
        Assert.Equal(240, ResolveGrainIncrement(state));
    }

    [Fact]
    public void 粮产_无专长高政务城守_仅吃政务加成()
    {
        var state = CreateSingleCityWorld();
        AttachOfficer(state, 0, 50, OfficerSpecialty.None, PostType.CityGovernor, 1);
        // 50 × 50 + 0 = 2500 → 240 × 12500/10000 = 300
        Assert.Equal(300, ResolveGrainIncrement(state));
    }

    [Fact]
    public void 粮产_农业专长屯田使_政务加专长最高()
    {
        var state = CreateSingleCityWorld();
        AttachOfficer(state, 0, 50, OfficerSpecialty.Agriculture, PostType.FarmOverseer, 1);
        // 2500 + 2500 = 5000 → 240 × 15000/10000 = 360
        Assert.Equal(360, ResolveGrainIncrement(state));
    }

    [Fact]
    public void 粮产_D12核心断言_农业屯田使大于无专长城守大于空岗()
    {
        var farmState = CreateSingleCityWorld();
        AttachOfficer(farmState, 0, 50, OfficerSpecialty.Agriculture, PostType.FarmOverseer, 1);

        var governorState = CreateSingleCityWorld();
        AttachOfficer(governorState, 0, 50, OfficerSpecialty.None, PostType.CityGovernor, 1);

        var emptyState = CreateSingleCityWorld();

        int farm = ResolveGrainIncrement(farmState);
        int governor = ResolveGrainIncrement(governorState);
        int empty = ResolveGrainIncrement(emptyState);

        // 这是「换人可观测变化」的锁定：农业屯田使 > 无专长城守 > 空岗，一个都不能少。
        Assert.Equal(360, farm);
        Assert.Equal(300, governor);
        Assert.Equal(240, empty);
        Assert.True(farm > governor);
        Assert.True(governor > empty);
    }

    [Fact]
    public void 粮产_到岗窗口未满_无人才加成()
    {
        var state = CreateSingleCityWorld();
        AttachOfficer(state, 0, 50, OfficerSpecialty.Agriculture, PostType.FarmOverseer, 0);
        // PostedSinceMonths=0 < PostTakeEffectMonths=1，人才加成按 0 处理。
        Assert.Equal(240, ResolveGrainIncrement(state));
    }

    [Fact]
    public void 粮产_屯田等级进公式()
    {
        var state = CreateSingleCityWorld(farmLevel: 1);
        // 空岗：240 × 11500/10000 = 276
        Assert.Equal(276, ResolveGrainIncrement(state));
    }

    [Fact]
    public void 粮产_屯田等级与人才叠加_截断顺序锁定()
    {
        var state = CreateSingleCityWorld(farmLevel: 2);
        AttachOfficer(state, 0, 50, OfficerSpecialty.Agriculture, PostType.FarmOverseer, 1);
        // 240 × 13000/10000 = 312，312 × 15000/10000 = 468
        Assert.Equal(468, ResolveGrainIncrement(state));
    }

    [Fact]
    public void 粮产_非粮产岗位不贡献()
    {
        var state = CreateSingleCityWorld();
        AttachOfficer(state, 0, 50, OfficerSpecialty.Siege, PostType.FortifyOverseer, 1);
        // 军师佐政不在粮产岗位白名单 → 0 贡献。
        Assert.Equal(240, ResolveGrainIncrement(state));
    }

    [Fact]
    public void 粮产_多城逐城截断后求和()
    {
        var state = CreateSingleCityWorld(population: 9999);

        // 追加第二座 9999 人口城，同属势力 0。
        state.Cities.Add(new City
        {
            Id = 1, Name = "测试城二", OwnerFactionId = 0,
            Population = 9999, MaxPopulation = 100_000,
            Defense = 100, MaxDefense = 300,
            FortifyLevel = 0, FarmLevel = 0, IsCapital = false,
            Terrain = TerrainType.Plain, AdjacentCityIds = new List<int>(),
        });

        // 单城 9999×800/10000=799.92→799，两城逐城截断后求和 = 1598。
        // （若退化为势力总人口一次算：19998×800/10000=1599，用这 1 粒粮之差锁死逐城截断。）
        Assert.Equal(1598, ResolveGrainIncrement(state));
    }

    [Fact]
    public void 粮产_换config值_结果跟随()
    {
        var config = Balance;
        config.SpecialtyBonus.Clear();

        var state = CreateSingleCityWorld();
        AttachOfficer(state, 0, 50, OfficerSpecialty.Agriculture, PostType.FarmOverseer, 1);

        int before = state.GetFaction(0)!.Resources.Grain;
        Apply(state, new EconomySystem(config).ResolveGrainIncome(state));
        int increment = state.GetFaction(0)!.Resources.Grain - before;

        // 专长表清空后，农业屯田使只剩政务 2500 → 300。
        Assert.Equal(300, increment);
    }

    // ======================= Develop / Fortify 岗位折扣 =======================

    [Fact]
    public void Develop_屯田使已生效_享折扣()
    {
        var withOfficer = CreateSingleCityWorld();
        AttachOfficer(withOfficer, 0, 50, OfficerSpecialty.Agriculture, PostType.FarmOverseer, 1);

        var resolver = new DevelopResolver(Balance);
        var order = Order.CreatePlayerOrder(ActionType.Develop, target: 0, amount: 100);

        var result = resolver.Resolve(withOfficer, order);
        Assert.Equal(ResolveResultCode.Success, result.Code);

        // baseCost = 35×100/100 = 35，折扣 5000 → 实付 35×5000/10000 = 17。
        StateDelta silver = result.Deltas.Single(d => d.Field == DeltaField.FactionSilver);
        Assert.Equal(5000, silver.OldValue);
        Assert.Equal(5000 - 17, silver.NewValue);

        // 拆掉人才后无折扣 → 实付 35。
        var withoutOfficer = withOfficer.Clone();
        withoutOfficer.Officers.Clear();

        var noOfficerResult = resolver.Resolve(withoutOfficer, order);
        StateDelta noOfficerSilver = noOfficerResult.Deltas.Single(d => d.Field == DeltaField.FactionSilver);
        Assert.Equal(5000 - 35, noOfficerSilver.NewValue);
    }

    [Fact]
    public void Fortify_军师佐政已生效_享折扣_岗位不对口不享()
    {
        var matching = CreateSingleCityWorld();
        AttachOfficer(matching, 0, 50, OfficerSpecialty.Siege, PostType.FortifyOverseer, 1);

        var resolver = new FortifyResolver(Balance);
        var order = Order.CreatePlayerOrder(ActionType.Fortify, target: 0, amount: 100);

        // baseCost = 40，折扣 = 政务 2500（专长查表 Siege:FortifyOverseer / Siege:Any 均落空为 0）
        // → 实付 40×7500/10000 = 30。
        var result = resolver.Resolve(matching, order);
        StateDelta silver = result.Deltas.Single(d => d.Field == DeltaField.FactionSilver);
        Assert.Equal(5000 - 30, silver.NewValue);

        // 换成 FarmOverseer 岗位（不对口）→ 折扣 0，实付 40。
        var mismatched = CreateSingleCityWorld();
        AttachOfficer(mismatched, 0, 50, OfficerSpecialty.Siege, PostType.FarmOverseer, 1);

        var mismatchedResult = resolver.Resolve(mismatched, order);
        StateDelta mismatchedSilver = mismatchedResult.Deltas.Single(d => d.Field == DeltaField.FactionSilver);
        Assert.Equal(5000 - 40, mismatchedSilver.NewValue);
    }

    // ======================= Recruit 无政务修正 =======================

    [Fact]
    public void Recruit_政务岗不影响征兵银耗()
    {
        var resolver = new RecruitResolver(Balance);
        var order = Order.CreatePlayerOrder(ActionType.Recruit, target: 0, amount: 100);

        // 有在岗人才的世界。
        var withOfficer = CreateSingleCityWorld();
        AttachOfficer(withOfficer, 0, 50, OfficerSpecialty.Agriculture, PostType.FarmOverseer, 1);

        var result = resolver.Resolve(withOfficer, order);
        Assert.Equal(ResolveResultCode.Success, result.Code);

        // 银耗恒为 50×100/100 = 50；粮耗 = 30×100/100 + 2×100 = 230。
        StateDelta silver = result.Deltas.Single(d => d.Field == DeltaField.FactionSilver);
        StateDelta grain = result.Deltas.Single(d => d.Field == DeltaField.FactionGrain);
        Assert.Equal(5000 - 50, silver.NewValue);
        Assert.Equal(5000 - 230, grain.NewValue);

        // 无在岗人才的世界，银耗与粮耗不变。
        var empty = CreateSingleCityWorld();
        var emptyResult = resolver.Resolve(empty, order);
        StateDelta emptySilver = emptyResult.Deltas.Single(d => d.Field == DeltaField.FactionSilver);
        StateDelta emptyGrain = emptyResult.Deltas.Single(d => d.Field == DeltaField.FactionGrain);
        Assert.Equal(5000 - 50, emptySilver.NewValue);
        Assert.Equal(5000 - 230, emptyGrain.NewValue);
    }
}
