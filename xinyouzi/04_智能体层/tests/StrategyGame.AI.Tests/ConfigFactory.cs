// 测试辅助：构造 BalanceConfig 基准值
using StrategyGame.Contracts;

namespace StrategyGame.AI.Tests;

public static class ConfigFactory
{
    public static BalanceConfig Create() => new()
    {
        GrainPerPopPerMonth = 800,
        SoldierPerPopPerMonth = 5000,
        MoraleDecayPerMonth = 3,
        MoraleDecayNoBattleThreshold = 4,
        MoraleDecayNoBattlePerMonth = 6,
        MoraleGainOnCapture = 10,
        MoraleLossOnRepel = 12,
        MoraleLossOnStalemate = 3,
        MoraleLossOnDefend = 4,
        MutinyRoutRate = 3000,
        StabilityDecayPerMonth = 500,
        BaseRecruitCostSilver = 100,
        BaseRecruitCostGrain = 50,
        BaseFortifyCostSilver = 200,
        BaseDevelopCostSilver = 150,
        LoyaltyThreshold = 30,
        LoyaltyDecayPerMonth = 2,
        PostTakeEffectMonths = 1,
        MaxOrdersPerFactionPerMonth = 5,
        BaseLoss = 3500,
        BaseKill = 2700,
        DefenderLossFactor = 0,
        SiegeThresholdLoss = 7000,
        FieldBattleLossFactor = 5000,
        StalemateLossRate = 0,
        ArmyMonthlyGrainPerSoldier = 200,
        SiegeDefeatRoutRate = 3000,
        SpecialtyBonus = new Dictionary<string, int>
        {
            ["Agriculture:FarmOverseer"] = 2500,
            ["Agriculture:Any"] = 1000,
            ["Siege:General"] = 2000,
            ["Cavalry:General"] = 2000,
            ["Logistics:Any"] = 1500,
            ["Diplomacy:Any"] = 1000,
        }
    };
}
