using StrategyGame.Contracts;
using StrategyGame.AI.Personnel;
using Xunit;

namespace StrategyGame.AI.Tests;

public class PersonnelPolicyTests
{
    private readonly BalanceConfig _config = ConfigFactory.Create();
    private readonly TestRandom _rng = new(42);

    private static WorldState MakeState(int factionId, params Officer[] officers)
    {
        var state = new WorldState();
        state.Factions.Add(new Faction { Id = factionId, IsPlayer = false, Name = "AI" });
        state.Cities.Add(new City { Id = 1, OwnerFactionId = factionId, Name = "城1", Population = 3000 });
        foreach (var o in officers) state.Officers.Add(o);
        return state;
    }

    [Fact]
    public void SelectBestFor_GovernanceWins_RegardlessOfSpecialty()
    {
        // 政务唯一标准：None 专长 Gov90 击败 Agriculture Gov40（反向锁定上轮 +1000 规则）
        var state = MakeState(1,
            new Officer { Id = 10, Name = "农官", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Specialty = OfficerSpecialty.Agriculture, Governance = 40 },
            new Officer { Id = 11, Name = "俗吏", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Specialty = OfficerSpecialty.None, Governance = 90 }
        );

        var policy = new BestFitPolicy(_config, _rng);
        var best = policy.SelectBestFor(state, 1, PostType.CityGovernor);

        Assert.NotNull(best);
        Assert.Equal(11, best!.Id);
        Assert.Equal(OfficerSpecialty.None, best.Specialty);
    }

    [Fact]
    public void SelectBestFor_General_PrefersHighCommand()
    {
        // General 取 Command 最高，不看专长
        var state = MakeState(1,
            new Officer { Id = 10, Name = "猛将", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Specialty = OfficerSpecialty.Siege, Command = 85 },
            new Officer { Id = 11, Name = "庸将", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Specialty = OfficerSpecialty.Cavalry, Command = 50 }
        );

        var policy = new BestFitPolicy(_config, _rng);
        var best = policy.SelectBestFor(state, 1, PostType.General);

        Assert.NotNull(best);
        Assert.Equal(10, best!.Id);
    }

    [Fact]
    public void SelectBestFor_TieGovernance_RandomAmongTied()
    {
        // 两人同 Gov，多颗种子反复选，结果都在两人 Id 集合内、且两人都至少被选中 1 次
        var state = MakeState(1,
            new Officer { Id = 10, Name = "甲", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Governance = 60 },
            new Officer { Id = 11, Name = "乙", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Governance = 60 }
        );

        var seen = new HashSet<int>();
        for (int seed = 1; seed <= 30; seed++)
        {
            var policy = new BestFitPolicy(_config, new TestRandom(seed));
            var best = policy.SelectBestFor(state, 1, PostType.CityGovernor);
            Assert.NotNull(best);
            Assert.Contains(best!.Id, new[] { 10, 11 });
            seen.Add(best.Id);
        }

        Assert.Equal(new HashSet<int> { 10, 11 }, seen);
    }

    [Fact]
    public void SelectBestFor_NoFreeOfficer_ReturnsNull()
    {
        var state = MakeState(1,
            new Officer { Id = 10, Name = "A", OwnerFactionId = 1, IsAlive = true, Post = PostType.CityGovernor, PostCityId = 1 }
        );

        var policy = new BestFitPolicy(_config, _rng);
        Assert.Null(policy.SelectBestFor(state, 1, PostType.CityGovernor));
    }

    [Fact]
    public void SelectBestFor_DeadOfficer_Ignored()
    {
        var state = MakeState(1,
            new Officer { Id = 10, Name = "亡者", OwnerFactionId = 1, IsAlive = false, Post = PostType.None, Specialty = OfficerSpecialty.Agriculture }
        );

        var policy = new BestFitPolicy(_config, _rng);
        Assert.Null(policy.SelectBestFor(state, 1, PostType.CityGovernor));
    }

    [Fact]
    public void SelectBestFor_ExcludedIds_SkipsExcluded()
    {
        // excludedIds 重载：排除甲后应选乙
        var state = MakeState(1,
            new Officer { Id = 10, Name = "甲", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Governance = 90 },
            new Officer { Id = 11, Name = "乙", OwnerFactionId = 1, IsAlive = true, Post = PostType.None, Governance = 60 }
        );

        var policy = new BestFitPolicy(_config, _rng);
        var excluded = new HashSet<int> { 10 };
        var best = policy.SelectBestFor(state, 1, PostType.CityGovernor, excluded);

        Assert.NotNull(best);
        Assert.Equal(11, best!.Id);
    }

    // ===== 忠诚度系统：风险预警（不执行叛离） =====

    [Fact]
    public void CheckLoyalty_BelowThreshold_EmitsRiskEvent_NotDefection()
    {
        // 忠诚 0、阈值 30，概率 (30-0)/30 = 1.0，必然命中
        var state = MakeState(1,
            new Officer { Id = 10, Name = "贰臣", OwnerFactionId = 1, IsAlive = true, Loyalty = 0 }
        );

        var policy = new BestFitPolicy(_config, _rng);
        var events = policy.CheckLoyalty(state, 1);

        Assert.NotEmpty(events);
        Assert.All(events, e =>
        {
            Assert.Equal("officer_defection_risk", e.EventKey);
            Assert.Contains("风险", e.Message);
            Assert.DoesNotContain("叛离了", e.Message);
            Assert.DoesNotContain("已叛离", e.Message);
            Assert.DoesNotContain("叛逃", e.Message);
        });
    }

    [Fact]
    public void CheckLoyalty_AboveThreshold_NoEvent()
    {
        var state = MakeState(1,
            new Officer { Id = 10, Name = "忠臣", OwnerFactionId = 1, IsAlive = true, Loyalty = 80 }
        );

        var policy = new BestFitPolicy(_config, _rng);
        var events = policy.CheckLoyalty(state, 1);

        Assert.Empty(events);
    }

    [Fact]
    public void CheckLoyalty_DoesNotModifyState()
    {
        var state = MakeState(1,
            new Officer { Id = 10, Name = "贰臣", OwnerFactionId = 1, IsAlive = true, Loyalty = 0 }
        );
        int originalLoyalty = state.Officers[0].Loyalty;

        var policy = new BestFitPolicy(_config, _rng);
        policy.CheckLoyalty(state, 1);

        Assert.Equal(originalLoyalty, state.Officers[0].Loyalty);
    }
}
