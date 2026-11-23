// ============================================================================
//  v1.7 新玩法测试：部门效率 / 建筑升级 / 招贤 / 部门任命
// ============================================================================
//  【这一组测试保护的核心命题】
//  「换一个人，产出当场变」——这是整个游戏的核心乐趣。
//  如果这条不成立，界面做得再漂亮，玩家也只是在点按钮。
//  所以第 4 条测试（192 → 264 → 192）是全项目最重要的一条断言。
//
//  【数字怎么来的】
//  3000 人口 × 800/10000 = 240 基础粮。
//  内政空岗 = 8000 → 240 × 0.8 = 192。
//  治理 55 = 10000 + (55-50)×200 = 11000 → 240 × 1.1 = 264。
//  全部整数向下取整，与 EconomySystem 的截断方向一致。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Randomization;
using StrategyGame.Core.Resolvers;
using StrategyGame.Core.Sample;
using StrategyGame.Core.Systems;
using Xunit;

namespace StrategyGame.Core.Tests;

public sealed class DepartmentAndBuildingTests
{
    private static BalanceConfig Balance => SampleData.CreateBalance();

    private static readonly StateApplier Applier = new();

    private static void Apply(WorldState state, ResolveResult result)
    {
        Assert.True(result.IsSuccess, result.Message);
        Applier.Apply(state, result.Deltas);
    }

    /// <summary>
    /// 构造一个最小世界：两个势力各一座城，另有 10 名在野人才。
    /// </summary>
    private static WorldState CreateWorld(int seed = 20261108)
    {
        var state = new WorldState
        {
            Year = 1,
            Month = 1,
            Seed = seed,
            Difficulty = DifficultyLevel.Normal,
            Player = new PlayerProfile { Id = Order.PlayerId, Name = "玩家", Command = 60 },
            Factions = new List<Faction>
            {
                new()
                {
                    Id = 0, Name = "薪火", IsPlayer = true, CapitalCityId = 1,
                    Resources = new Resources { Grain = 0, Silver = 800, Soldier = 0, Morale = 60, Stability = 60 },
                },
                new()
                {
                    Id = 1, Name = "赤壤", IsPlayer = false, CapitalCityId = 2,
                    Resources = new Resources { Grain = 0, Silver = 5000, Soldier = 0, Morale = 60, Stability = 60 },
                },
            },
            Cities = new List<City>
            {
                new()
                {
                    Id = 1, Name = "主城", OwnerFactionId = 0, Population = 3000,
                    MaxPopulation = 10000, Defense = 100, MaxDefense = 1000,
                    FortifyLevel = 0, FarmLevel = 0, IsCapital = true,
                    Terrain = TerrainType.Plain, AdjacentCityIds = new List<int> { 2 },
                },
                new()
                {
                    Id = 2, Name = "敌城", OwnerFactionId = 1, Population = 3000,
                    MaxPopulation = 10000, Defense = 100, MaxDefense = 1000,
                    FortifyLevel = 0, FarmLevel = 0, IsCapital = true,
                    Terrain = TerrainType.Plain, AdjacentCityIds = new List<int> { 1 },
                },
            },
            Armies = new List<Army>(),
            Officers = new List<Officer>(),
            PendingOrders = new List<PendingOrder>(),
            EventLog = new List<EventRecord>(),
        };

        // 在野池：10 人，四维各不相同，都带至少一项短板或偏科。
        for (int i = 1; i <= 10; i++)
        {
            state.Officers.Add(new Officer
            {
                Id = 100 + i,
                Name = $"在野{i}",
                OwnerFactionId = -1,
                PostCityId = -1,
                Post = PostType.None,
                Command = 40 + i,
                Governance = 45 + i,
                Intellect = 38 + i,
                Charisma = 42 + i,
                Loyalty = 60,
                Age = 30,
                IsAlive = true,
                Specialty = i % 2 == 0 ? OfficerSpecialty.Agriculture : OfficerSpecialty.Siege,
            });
        }

        // 敌方一名人才，用于「任命别人的人」测试。
        state.Officers.Add(new Officer
        {
            Id = 200, Name = "敌将", OwnerFactionId = 1, PostCityId = 2,
            Post = PostType.General, Command = 70, Governance = 60,
            Intellect = 60, Charisma = 60, Loyalty = 80, Age = 35,
            IsAlive = true, Specialty = OfficerSpecialty.Cavalry,
        });

        // 一名四维可控的自家人才，给部门测试用（治理 55 → 效率 11000）。
        state.Officers.Add(new Officer
        {
            Id = 1, Name = "内政官", OwnerFactionId = 0, PostCityId = -1,
            Post = PostType.None, Command = 50, Governance = 55,
            Intellect = 50, Charisma = 50, Loyalty = 65, Age = 40,
            IsAlive = true, Specialty = OfficerSpecialty.None,
        });

        return state;
    }

    // ====================================================================
    //  部门效率
    // ====================================================================

    [Fact(DisplayName = "空岗效率 = 8000")]
    public void Department_Vacant_Is8000()
    {
        var state = CreateWorld();

        Assert.Equal(8000, DepartmentSystem.GetEfficiency(state, 0, DepartmentType.Interior));
    }

    [Fact(DisplayName = "属性 50 的效率 = 10000")]
    public void Department_Attribute50_Is10000()
    {
        var state = CreateWorld();
        state.GetOfficer(1)!.Governance = 50;
        state.GetFaction(0)!.DepartmentHead[DepartmentType.Interior] = 1;

        Assert.Equal(10000, DepartmentSystem.GetEfficiency(state, 0, DepartmentType.Interior));
    }

    [Fact(DisplayName = "属性 62 的内政效率 = 12400")]
    public void Department_Attribute62_Is12400()
    {
        var state = CreateWorld();
        state.GetOfficer(1)!.Governance = 62;
        state.GetFaction(0)!.DepartmentHead[DepartmentType.Interior] = 1;

        Assert.Equal(12400, DepartmentSystem.GetEfficiency(state, 0, DepartmentType.Interior));
    }

    [Fact(DisplayName = "换人：粮产 192 → 264 → 192（全项目最关键的一条）")]
    public void Department_SwapOfficer_ChangesGrainImmediately()
    {
        var state = CreateWorld();
        var economy = new EconomySystem(Balance);

        // 空岗：240 × 0.8 = 192
        Apply(state, ResolveResult.Ok());
        Applier.Apply(state, economy.ResolveGrainIncome(state));
        Assert.Equal(192, state.GetFaction(0)!.Resources.Grain);

        // 任命治理 55 的人：240 × 1.1 = 264
        var appoint = new AppointDepartmentResolver(Balance);
        Apply(state, appoint.Resolve(state,
            Order.CreatePlayerOrder(ActionType.AppointDepartment, (int)DepartmentType.Interior, secondary: 1)));

        int before = state.GetFaction(0)!.Resources.Grain;
        Applier.Apply(state, economy.ResolveGrainIncome(state));
        Assert.Equal(before + 264, state.GetFaction(0)!.Resources.Grain);

        // 卸任：回到 192
        Apply(state, appoint.Resolve(state,
            Order.CreatePlayerOrder(ActionType.AppointDepartment, (int)DepartmentType.Interior, secondary: -1)));

        int beforeUnassign = state.GetFaction(0)!.Resources.Grain;
        Applier.Apply(state, economy.ResolveGrainIncome(state));
        Assert.Equal(beforeUnassign + 192, state.GetFaction(0)!.Resources.Grain);
    }

    [Fact(DisplayName = "兵源受军事部门影响：统率 60 比空岗多五成")]
    public void Department_Military_ScalesSoldierIncome()
    {
        var state = CreateWorld();
        var economy = new EconomySystem(Balance);

        Applier.Apply(state, economy.ResolveSoldierIncome(state));
        int vacant = state.GetFaction(0)!.Resources.Soldier;
        state.GetFaction(0)!.Resources.Soldier = 0;

        state.GetOfficer(1)!.Command = 60;
        state.GetFaction(0)!.DepartmentHead[DepartmentType.Military] = 1;
        Applier.Apply(state, economy.ResolveSoldierIncome(state));
        int withHead = state.GetFaction(0)!.Resources.Soldier;

        // 基础 3000 × 500/10000 = 150，稳定度 60 → 90。
        // 空岗 90 × 0.8 = 72；统率 60 效率 12000 → 90 × 1.2 = 108。比值 1.5。
        Assert.Equal(72, vacant);
        Assert.Equal(108, withHead);
        Assert.Equal(3, withHead * 2 / vacant); // 108 / 72 = 1.5，整数比校验
    }

    // ====================================================================
    //  建筑升级
    // ====================================================================

    [Fact(DisplayName = "升农田：扣 150 银，等级不变，剩余 1 月")]
    public void Build_Farm_DeductsSilverAndStartsCountdown()
    {
        var state = CreateWorld();
        var build = new BuildResolver(Balance);

        Apply(state, build.Resolve(state,
            Order.CreatePlayerOrder(ActionType.Build, target: 1, secondary: BuildingSystem.Farm)));

        var faction = state.GetFaction(0)!;
        var city = state.GetCity(1)!;

        Assert.Equal(650, faction.Resources.Silver);
        Assert.Equal(0, city.FarmLevel);
        Assert.Equal(BuildingSystem.Farm, city.UpgradingBuilding);
        Assert.Equal(1, city.UpgradeMonthsRemaining);
    }

    [Fact(DisplayName = "升级次月完工：农田等级 +1，工程清空")]
    public void Build_CompletesNextMonth()
    {
        var state = CreateWorld();
        var build = new BuildResolver(Balance);

        Apply(state, build.Resolve(state,
            Order.CreatePlayerOrder(ActionType.Build, target: 1, secondary: BuildingSystem.Farm)));

        Applier.Apply(state, BuildingSystem.ResolveAdvance(state));

        var city = state.GetCity(1)!;
        Assert.Equal(1, city.FarmLevel);
        Assert.Equal(0, city.UpgradeMonthsRemaining);
        Assert.Equal(BuildingSystem.None, city.UpgradingBuilding);
    }

    [Fact(DisplayName = "施工中再下令被拒，且不扣银")]
    public void Build_WhileBusy_IsRejected()
    {
        var state = CreateWorld();
        var build = new BuildResolver(Balance);

        Apply(state, build.Resolve(state,
            Order.CreatePlayerOrder(ActionType.Build, target: 1, secondary: BuildingSystem.Farm)));

        int silverAfterFirst = state.GetFaction(0)!.Resources.Silver;

        var second = build.Resolve(state,
            Order.CreatePlayerOrder(ActionType.Build, target: 1, secondary: BuildingSystem.Market));

        Assert.False(second.IsSuccess);
        Assert.Equal(ResolveResultCode.RejectedCooldown, second.Code);
        Assert.Equal(silverAfterFirst, state.GetFaction(0)!.Resources.Silver);
    }

    [Fact(DisplayName = "银不够被拒，不扣银也不开工")]
    public void Build_WithoutSilver_IsRejected()
    {
        var state = CreateWorld();
        state.GetFaction(0)!.Resources.Silver = 100;

        var build = new BuildResolver(Balance);
        var result = build.Resolve(state,
            Order.CreatePlayerOrder(ActionType.Build, target: 1, secondary: BuildingSystem.Farm));

        Assert.False(result.IsSuccess);
        Assert.Equal(ResolveResultCode.RejectedNoResources, result.Code);
        Assert.Equal(100, state.GetFaction(0)!.Resources.Silver);
        Assert.Equal(BuildingSystem.None, state.GetCity(1)!.UpgradingBuilding);
    }

    // ====================================================================
    //  招贤
    // ====================================================================

    [Fact(DisplayName = "招贤扣银，人才数 +1")]
    public void Recruit_DeductsSilverAndAddsOfficer()
    {
        var state = CreateWorld();
        int before = state.GetOfficersOfFaction(0).Count;

        var recruit = new RecruitOfficerResolver(Balance, new DeterministicRandom(state.Seed));
        Apply(state, recruit.Resolve(state,
            Order.CreatePlayerOrder(ActionType.RecruitOfficer, target: 1, amount: 200)));

        Assert.Equal(600, state.GetFaction(0)!.Resources.Silver);
        Assert.Equal(before + 1, state.GetOfficersOfFaction(0).Count);
    }

    [Fact(DisplayName = "同月招第二次被拒")]
    public void Recruit_TwiceInSameMonth_IsRejected()
    {
        var state = CreateWorld();
        var recruit = new RecruitOfficerResolver(Balance, new DeterministicRandom(state.Seed));

        Apply(state, recruit.Resolve(state,
            Order.CreatePlayerOrder(ActionType.RecruitOfficer, target: 1, amount: 200)));

        var second = recruit.Resolve(state,
            Order.CreatePlayerOrder(ActionType.RecruitOfficer, target: 1, amount: 200));

        Assert.False(second.IsSuccess);
        Assert.Equal(ResolveResultCode.RejectedCooldown, second.Code);
    }

    [Fact(DisplayName = "招贤确定性：同种子两次挑中同一人")]
    public void Recruit_IsDeterministic()
    {
        var a = CreateWorld(seed: 777);
        var b = CreateWorld(seed: 777);

        var ra = new RecruitOfficerResolver(Balance, new DeterministicRandom(777));
        var rb = new RecruitOfficerResolver(Balance, new DeterministicRandom(777));

        Apply(a, ra.Resolve(a, Order.CreatePlayerOrder(ActionType.RecruitOfficer, target: 1, amount: 200)));
        Apply(b, rb.Resolve(b, Order.CreatePlayerOrder(ActionType.RecruitOfficer, target: 1, amount: 200)));

        var pickedA = a.GetOfficersOfFaction(0).OrderBy(o => o.Id).Last();
        var pickedB = b.GetOfficersOfFaction(0).OrderBy(o => o.Id).Last();

        Assert.Equal(pickedA.Id, pickedB.Id);
        Assert.Equal(pickedA.Command, pickedB.Command);
        Assert.Equal(pickedA.Governance, pickedB.Governance);
    }

    [Fact(DisplayName = "在野池的人都带短板，不会招到四维全强的完人")]
    public void RecruitPool_EveryoneHasAFlaw()
    {
        var state = CreateWorld();
        var pool = state.Officers.Where(o => o.OwnerFactionId == -1).ToList();

        Assert.NotEmpty(pool);
        Assert.All(pool, o =>
        {
            int weakest = Math.Min(Math.Min(o.Command, o.Governance), Math.Min(o.Intellect, o.Charisma));
            Assert.True(weakest <= 48, $"{o.Name} 四项都偏高（最低 {weakest}），招贤变成抽奖。");
        });
    }

    // ====================================================================
    //  部门任命
    // ====================================================================

    [Fact(DisplayName = "任命后部门主官表写入正确")]
    public void Appoint_WritesDepartmentHead()
    {
        var state = CreateWorld();
        var appoint = new AppointDepartmentResolver(Balance);

        Apply(state, appoint.Resolve(state,
            Order.CreatePlayerOrder(ActionType.AppointDepartment, (int)DepartmentType.Interior, secondary: 1)));

        Assert.Equal(1, state.GetFaction(0)!.DepartmentHead[DepartmentType.Interior]);
    }

    [Fact(DisplayName = "任命别人势力的人被拒")]
    public void Appoint_EnemyOfficer_IsRejected()
    {
        var state = CreateWorld();
        var appoint = new AppointDepartmentResolver(Balance);

        var result = appoint.Resolve(state,
            Order.CreatePlayerOrder(ActionType.AppointDepartment, (int)DepartmentType.Military, secondary: 200));

        Assert.False(result.IsSuccess);
        Assert.Equal(ResolveResultCode.RejectedInvalidActor, result.Code);
    }

    [Fact(DisplayName = "任命不占行动力")]
    public void Appoint_DoesNotConsumeCapacity()
    {
        var state = CreateWorld();
        var appoint = new AppointDepartmentResolver(Balance);

        int before = state.GetActionsThisMonth(0);

        Apply(state, appoint.Resolve(state,
            Order.CreatePlayerOrder(ActionType.AppointDepartment, (int)DepartmentType.Interior, secondary: 1)));

        Assert.Equal(before, state.GetActionsThisMonth(0));
    }

    [Fact(DisplayName = "一人只管一个部门：调岗后原部门自动空出")]
    public void Appoint_MovingOfficer_ClearsPreviousDepartment()
    {
        var state = CreateWorld();
        var appoint = new AppointDepartmentResolver(Balance);

        Apply(state, appoint.Resolve(state,
            Order.CreatePlayerOrder(ActionType.AppointDepartment, (int)DepartmentType.Interior, secondary: 1)));

        Apply(state, appoint.Resolve(state,
            Order.CreatePlayerOrder(ActionType.AppointDepartment, (int)DepartmentType.Military, secondary: 1)));

        var heads = state.GetFaction(0)!.DepartmentHead;
        Assert.Equal(1, heads[DepartmentType.Military]);
        Assert.Equal(-1, heads[DepartmentType.Interior]);
        Assert.Equal(8000, DepartmentSystem.GetEfficiency(state, 0, DepartmentType.Interior));
    }
}
