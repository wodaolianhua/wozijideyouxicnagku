using System.Diagnostics;
using StrategyGame.Contracts;
using StrategyGame.AI.Personnel;
using Xunit;

namespace StrategyGame.AI.Tests;

public class AiPlannerTests
{
    private readonly BalanceConfig _config = ConfigFactory.Create();
    private readonly TestRandom _rng = new(42);

    // 六个 [Proto] 核心行动（0-5），禁止产出 Reward/RecruitOfficer 等保留位
    private static readonly ActionType[] CoreActions =
    {
        ActionType.Recruit, ActionType.Fortify, ActionType.Develop,
        ActionType.Dispatch, ActionType.AssignPost, ActionType.Rest
    };

    // =====================================================================
    //  岗位任命：缺岗城按 Governance 选才，Amount=-1
    // =====================================================================

    [Fact]
    public void Plan_VacantCity_AssignsHighestGovernanceOfficer_AmountMinusOne()
    {
        // 两人政务不等：农官 Gov70 / 凡人 Gov50
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = 1, IsPlayer = false, Name = "AI", Resources = new Resources { Silver = 9999, Grain = 9999, Soldier = 9999 } });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = 1, Name = "城1", Population = 3000, MaxPopulation = 5000, Defense = 100, MaxDefense = 500, FarmLevel = 0, FortifyLevel = 0, AdjacentCityIds = new List<int>() });
        state.Officers.Add(new Officer { Id = 10, Name = "农官", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Specialty = OfficerSpecialty.Agriculture, Governance = 70, Loyalty = 80 });
        state.Officers.Add(new Officer { Id = 11, Name = "凡人", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Specialty = OfficerSpecialty.None, Governance = 50, Loyalty = 80 });

        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        var assign = orders.FirstOrDefault(o => o.Action == ActionType.AssignPost && o.SecondaryTargetId == 1);
        Assert.NotNull(assign);
        Assert.Equal(10, assign!.TargetId);     // 政务最高者上岗
        Assert.Equal(-1, assign.Amount);        // Amount 恒为 -1
        Assert.Equal(1, assign.SecondaryTargetId); // SecondaryTargetId = 城
    }

    [Fact]
    public void Plan_OccupiedCity_SkipsAssignPost()
    {
        // 城里已有任意岗位在岗人才（CityGovernor），另有 1 名空闲人才 → 不产出指向该城的 AssignPost
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = 1, IsPlayer = false, Name = "AI", Resources = new Resources { Silver = 9999, Grain = 9999, Soldier = 9999 } });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = 1, Name = "城1", Population = 3000, AdjacentCityIds = new List<int>() });
        state.Officers.Add(new Officer { Id = 10, Name = "在岗", OwnerFactionId = 1, IsAlive = true, Post = PostType.CityGovernor, PostCityId = 1, Governance = 60 });
        state.Officers.Add(new Officer { Id = 11, Name = "闲人", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Governance = 50 });

        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        Assert.DoesNotContain(orders, o => o.Action == ActionType.AssignPost && o.SecondaryTargetId == 1);
    }

    [Fact]
    public void Plan_MultipleVacantCities_AssignsDistinctOfficers_OnceEach()
    {
        // 2 座缺岗城、2 名空闲人才 → 2 条 AssignPost，TargetId 互不相同、Amount 均 -1
        var state = BuildTwoCitiesTwoOfficers();
        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        var assigns = orders.Where(o => o.Action == ActionType.AssignPost).ToList();
        Assert.Equal(2, assigns.Count);
        Assert.Equal(2, assigns.Select(a => a.TargetId).Distinct().Count());
        Assert.All(assigns, a => Assert.Equal(-1, a.Amount));

        // 仅 1 名空闲人才时只有 1 条（同一人不得派两城）
        var state2 = BuildTwoCitiesTwoOfficers();
        state2.Officers.RemoveAll(o => o.Id == 11);
        var orders2 = planner.Plan(state2, 1);
        var assigns2 = orders2.Where(o => o.Action == ActionType.AssignPost).ToList();
        Assert.Single(assigns2);
    }

    private static WorldState BuildTwoCitiesTwoOfficers()
    {
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = 1, IsPlayer = false, Name = "AI", Resources = new Resources { Silver = 9999, Grain = 9999, Soldier = 9999 } });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = 1, Name = "大城", Population = 5000, AdjacentCityIds = new List<int>() });
        state.Cities.Add(new City { Id = 2, OwnerFactionId = 1, Name = "小城", Population = 2000, AdjacentCityIds = new List<int>() });
        state.Officers.Add(new Officer { Id = 10, Name = "甲", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Governance = 70 });
        state.Officers.Add(new Officer { Id = 11, Name = "乙", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Governance = 60 });
        return state;
    }

    // =====================================================================
    //  忠诚维护：低忠诚空闲人才被任命上岗，不产出 Reward
    // =====================================================================

    [Fact]
    public void Plan_LowLoyaltyIdleOfficer_GetsPostedNotRewarded()
    {
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = 1, IsPlayer = false, Name = "AI", Resources = new Resources { Silver = 5000, Grain = 5000, Soldier = 5000 } });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = 1, Name = "城1", Population = 3000, AdjacentCityIds = new List<int>() });
        state.Officers.Add(new Officer { Id = 10, Name = "贰臣", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Loyalty = 20, Governance = 60 });

        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        Assert.Contains(orders, o => o.Action == ActionType.AssignPost && o.TargetId == 10);
        Assert.DoesNotContain(orders, o => o.Action == ActionType.Reward);
    }

    // =====================================================================
    //  合法性：绝不产出保留位行动
    // =====================================================================

    [Fact]
    public void Plan_NeverEmitsReservedActions()
    {
        var state = BuildRichState(1);
        var planner = new AiFactionPlanner(_config, _rng);

        int totalOrders = 0;
        for (int turn = 0; turn < 100; turn++)
        {
            state.Month++;
            if (state.Month > 12) { state.Month = 1; state.Year++; }

            var orders = planner.Plan(state, 1);
            totalOrders += orders.Count;

            Assert.All(orders, o =>
            {
                Assert.InRange((int)o.Action, 0, 5);
                Assert.Contains(o.Action, CoreActions);
            });
        }

        Assert.True(totalOrders > 0, "100 个月指令总数应 > 0");
    }

    // =====================================================================
    //  兵源瓶颈：兵源不足时不扩张，改屯田
    // =====================================================================

    [Fact]
    public void Plan_SoldierStarved_NoDispatch_DoesDevelop()
    {
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = 1, IsPlayer = false, Name = "AI", Resources = new Resources { Silver = 5000, Grain = 5000, Soldier = 0 } });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = 1, Name = "城1", Population = 3000, MaxPopulation = 5000, Defense = 100, MaxDefense = 500, FarmLevel = 0, FortifyLevel = 0, AdjacentCityIds = new List<int> { 2 } });
        state.Cities.Add(new City { Id = 2, OwnerFactionId = 2, Name = "敌城", Population = 2000, AdjacentCityIds = new List<int> { 1 } });
        state.Armies.Add(new Army { Id = 1, OwnerFactionId = 1, OfficerId = -1, Soldiers = 300, Morale = 80, MoraleMax = 100, PositionCityId = 1, Stance = ArmyStance.Idle });
        state.Factions.Add(new Faction { Id = 2, IsPlayer = false, Name = "敌" });

        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        Assert.DoesNotContain(orders, o => o.Action == ActionType.Dispatch);
        Assert.Contains(orders, o => o.Action == ActionType.Develop);
    }

    [Fact]
    public void Plan_DevelopOrder_AmountIsSilverQuantity()
    {
        // 兵源瓶颈场景，断言 Develop Amount == 100
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = 1, IsPlayer = false, Name = "AI", Resources = new Resources { Silver = 5000, Grain = 5000, Soldier = 0 } });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = 1, Name = "城1", Population = 3000, FarmLevel = 0, AdjacentCityIds = new List<int> { 2 } });
        state.Cities.Add(new City { Id = 2, OwnerFactionId = 2, Name = "敌城", Population = 2000, AdjacentCityIds = new List<int> { 1 } });
        state.Armies.Add(new Army { Id = 1, OwnerFactionId = 1, OfficerId = -1, Soldiers = 300, Morale = 80, MoraleMax = 100, PositionCityId = 1, Stance = ArmyStance.Idle });
        state.Factions.Add(new Faction { Id = 2, IsPlayer = false, Name = "敌" });

        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        var develop = orders.FirstOrDefault(o => o.Action == ActionType.Develop);
        Assert.NotNull(develop);
        Assert.Equal(100, develop!.Amount);
    }

    [Fact]
    public void Plan_FortifyOrder_AmountIsSilverQuantity()
    {
        // 防守分支：有相邻敌城、总兵力 1000（500-3000）、银充足 → Fortify Amount == 100
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = 1, IsPlayer = false, Name = "AI", Resources = new Resources { Silver = 5000, Grain = 5000, Soldier = 5000 } });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = 1, Name = "城1", Population = 3000, Defense = 100, AdjacentCityIds = new List<int> { 2 } });
        state.Cities.Add(new City { Id = 2, OwnerFactionId = 2, Name = "敌城", Population = 2000, AdjacentCityIds = new List<int> { 1 } });
        state.Factions.Add(new Faction { Id = 2, IsPlayer = false, Name = "敌" });
        state.Armies.Add(new Army { Id = 1, OwnerFactionId = 1, OfficerId = -1, Soldiers = 1000, Morale = 80, MoraleMax = 100, PositionCityId = 1, Stance = ArmyStance.Idle });

        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        var fortify = orders.FirstOrDefault(o => o.Action == ActionType.Fortify);
        Assert.NotNull(fortify);
        Assert.Equal(100, fortify!.Amount);
    }

    // =====================================================================
    //  士气低时倾向 Rest 而非进攻
    // =====================================================================

    [Fact]
    public void Plan_LowMoraleArmy_RestsInsteadOfAttacking()
    {
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = 1, IsPlayer = false, Name = "AI", Resources = new Resources { Silver = 5000, Grain = 5000, Soldier = 5000 } });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = 1, Name = "城1", Population = 3000, AdjacentCityIds = new List<int> { 2 } });
        state.Cities.Add(new City { Id = 2, OwnerFactionId = 2, Name = "敌城", Population = 2000, AdjacentCityIds = new List<int> { 1 } });
        state.Factions.Add(new Faction { Id = 2, IsPlayer = false, Name = "敌" });
        state.Armies.Add(new Army { Id = 1, OwnerFactionId = 1, OfficerId = -1, Soldiers = 3000, Morale = 20, MoraleMax = 100, PositionCityId = 1, Stance = ArmyStance.Idle });

        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        Assert.Contains(orders, o => o.Action == ActionType.Rest);
        Assert.DoesNotContain(orders, o => o.Action == ActionType.Dispatch);
    }

    [Fact]
    public void Plan_HighMoraleArmy_AttacksWhenStrong()
    {
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = 1, IsPlayer = false, Name = "AI", Resources = new Resources { Silver = 5000, Grain = 5000, Soldier = 5000 } });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = 1, Name = "城1", Population = 3000, AdjacentCityIds = new List<int> { 2 } });
        state.Cities.Add(new City { Id = 2, OwnerFactionId = 2, Name = "敌城", Population = 2000, AdjacentCityIds = new List<int> { 1 } });
        state.Factions.Add(new Faction { Id = 2, IsPlayer = false, Name = "敌" });
        state.Armies.Add(new Army { Id = 1, OwnerFactionId = 1, OfficerId = -1, Soldiers = 3000, Morale = 80, MoraleMax = 100, PositionCityId = 1, Stance = ArmyStance.Idle });

        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        Assert.Contains(orders, o => o.Action == ActionType.Dispatch);
    }

    // =====================================================================
    //  指令数量上限 & 合法性
    // =====================================================================

    [Fact]
    public void Plan_OrdersCount_DoesNotExceedMax()
    {
        var state = BuildRichState(1);
        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        Assert.True(orders.Count <= _config.MaxOrdersPerFactionPerMonth,
            $"指令数 {orders.Count} 超过上限 {_config.MaxOrdersPerFactionPerMonth}");
    }

    [Fact]
    public void Plan_AllOrders_UseValidActionTypes()
    {
        var state = BuildRichState(1);
        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        Assert.All(orders, o => Assert.Contains(o.Action, CoreActions));
    }

    [Fact]
    public void Plan_AllOrders_AiFactionActor()
    {
        var state = BuildRichState(1);
        var planner = new AiFactionPlanner(_config, _rng);
        var orders = planner.Plan(state, 1);

        Assert.All(orders, o =>
        {
            Assert.Equal(ActorType.AiFaction, o.ActorType);
            Assert.Equal(1, o.ActorId);
        });
    }

    // =====================================================================
    //  性能：单次 Plan < 5ms
    // =====================================================================

    [Fact]
    public void Plan_Performance_UnderFiveMs()
    {
        var state = BuildRichState(1);
        for (int i = 2; i <= 10; i++)
        {
            state.Cities.Add(new City { Id = i, OwnerFactionId = 1, Name = $"城{i}", Population = 3000, AdjacentCityIds = new List<int> { 1 } });
        }
        for (int i = 1; i <= 20; i++)
        {
            state.Armies.Add(new Army { Id = i, OwnerFactionId = 1, OfficerId = -1, Soldiers = 1000, Morale = 70, MoraleMax = 100, PositionCityId = 1, Stance = ArmyStance.Idle });
        }
        for (int i = 1; i <= 30; i++)
        {
            state.Officers.Add(new Officer { Id = i, Name = $"人{i}", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Loyalty = 60 });
        }

        var planner = new AiFactionPlanner(_config, _rng);

        planner.Plan(state, 1); // 预热

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            planner.Plan(state, 1);
        }
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / 100;
        Assert.True(avgMs < 5.0, $"单次 Plan 平均耗时 {avgMs:F2}ms，超过 5ms 上限");
    }

    // =====================================================================
    //  100 回合模拟不崩溃
    // =====================================================================

    [Fact]
    public void Plan_HundredTurns_NoCrash()
    {
        var state = BuildRichState(1);
        state.Factions.Add(new Faction { Id = 2, IsPlayer = false, Name = "敌2" });
        var planner = new AiFactionPlanner(_config, _rng);

        int totalOrders = 0;
        for (int turn = 0; turn < 100; turn++)
        {
            state.Month++;
            if (state.Month > 12) { state.Month = 1; state.Year++; }

            var orders = planner.Plan(state, 1);
            totalOrders += orders.Count;

            Assert.All(orders, o => Assert.Contains(o.Action, CoreActions));
        }

        Assert.True(totalOrders > 0, "100 回合不应完全无指令");
    }

    [Fact]
    public void PlanAll_OnlyNonPlayerFactions()
    {
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = 0, IsPlayer = true, Name = "玩家" });
        state.Factions.Add(new Faction { Id = 1, IsPlayer = false, Name = "AI1" });
        state.Factions.Add(new Faction { Id = 2, IsPlayer = false, Name = "AI2" });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = 1, Population = 3000, AdjacentCityIds = new List<int>() });
        state.Cities.Add(new City { Id = 2, OwnerFactionId = 2, Population = 3000, AdjacentCityIds = new List<int>() });
        state.GetFaction(1)!.Resources = new Resources { Silver = 1000, Grain = 1000, Soldier = 1000 };
        state.GetFaction(2)!.Resources = new Resources { Silver = 1000, Grain = 1000, Soldier = 1000 };

        var planner = new AiFactionPlanner(_config, _rng);
        var all = planner.PlanAll(state);

        Assert.All(all, o => Assert.NotEqual(0, o.ActorId));
    }

    // =====================================================================
    //  辅助：构建一个富裕的单势力状态
    // =====================================================================

    private static WorldState BuildRichState(int factionId)
    {
        var state = new WorldState();
        state.Factions.Add(new Faction
        {
            Id = factionId,
            IsPlayer = false,
            Name = "AI",
            Resources = new Resources { Silver = 99999, Grain = 99999, Soldier = 9999, Stability = 80, Morale = 80 }
        });
        state.Cities.Add(new City
        {
            Id = 1, OwnerFactionId = factionId, Name = "主城",
            Population = 5000, MaxPopulation = 8000,
            Defense = 500, MaxDefense = 1000,
            FarmLevel = 3, FortifyLevel = 3,
            AdjacentCityIds = new List<int> { 2 }
        });
        state.Cities.Add(new City
        {
            Id = 2, OwnerFactionId = factionId + 1, Name = "邻敌",
            Population = 3000, AdjacentCityIds = new List<int> { 1 }
        });
        state.Factions.Add(new Faction { Id = factionId + 1, IsPlayer = false, Name = "敌" });
        state.Armies.Add(new Army
        {
            Id = 1, OwnerFactionId = factionId, OfficerId = -1,
            Soldiers = 4000, Morale = 80, MoraleMax = 100,
            PositionCityId = 1, Stance = ArmyStance.Idle
        });
        return state;
    }
}
