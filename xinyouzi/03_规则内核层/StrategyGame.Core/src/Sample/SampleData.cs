// ============================================================================
//  最小示例数据
// ============================================================================
//  交付要求：3 势力 / 6 城 / 5 人才，供测试使用。
//  同时提供一份占位 BalanceConfig，让 03 包能独立编译独立测试。
//
//  【重要】这里的数值是「能跑通流程的占位值」，不是设计数值。
//  TODO(设计): 待02 内容数据层交付后，本文件仅保留结构，数值全部走 IConfigLoader。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Sample;

/// <summary>
/// 最小示例数据集。3 势力 / 6 城 / 5 人才。
/// </summary>
/// <remarks>
/// 城池布局为 2×3 网格，相邻关系严格对称：
///   0-1-2
///   3-4-5
/// 相邻对：0-1, 0-3, 1-2, 1-4, 2-5, 3-4, 4-5
/// </remarks>
public static class SampleData
{
    /// <summary>创建示例平衡配置。取 3_战斗系统设计.md 第八节与4_数值框架.md 5.2 的基准值。</summary>
    /// <returns>一份可直接驱动结算的平衡配置。</returns>
    /// <remarks>
    /// 【v1.2 刻度】所有比率类字段一律万分比（10000 = 100%）。
    /// 【重要】真正的数值表由 02 包写入 <c>assets/data/config.json</c>，
    /// 本方法仅保证 03 包能独立编译独立测试。
    /// </remarks>
    public static BalanceConfig CreateBalance()
        => new()
        {
            // ===== 经济（万分比）=====
            // GrainPerPopPerMonth = 800 → 3000 人口城月产 240 粮。
            GrainPerPopPerMonth = 800,
            // SoldierPerPopPerMonth = 5000 → 3000 人口、Stability 60 = 90 兵/月。
            SoldierPerPopPerMonth = 5000,
            // ArmyMonthlyGrainPerSoldier = 200 → 每人每月 0.02 粮。
            ArmyMonthlyGrainPerSoldier = 200,

            // ===== 士气（绝对点数，非比率）=====
            MoraleDecayPerMonth = 3,
            MoraleDecayNoBattleThreshold = 6,
            MoraleDecayNoBattlePerMonth = 2,
            MoraleGainOnCapture = 10,
            MoraleLossOnRepel = 12,
            MoraleLossOnStalemate = 3,
            MoraleLossOnDefend = 4,
            MutinyRoutRate = 3000,

            StabilityDecayPerMonth = 1,

            // ===== 绝对数量（银/粮/点）=====
            BaseRecruitCostSilver = 50,
            BaseRecruitCostGrain = 30,
            BaseFortifyCostSilver = 40,
            BaseDevelopCostSilver = 35,
            LoyaltyThreshold = 30,
            LoyaltyDecayPerMonth = 1,
            PostTakeEffectMonths = 1,
            MaxOrdersPerFactionPerMonth = 5,

            // ===== 战斗系统系数（万分比）=====
            // 来源：3_战斗系统设计.md 第八节「系数清单」。
            BaseLoss = 3500,
            BaseKill = 2700,
            SiegeThresholdLoss = 7000,
            FieldBattleLossFactor = 6000,
            SiegeDefeatRoutRate = 0,

            // [已废弃 v1.2] 以下两项不参与任何计算。
            // 此处显式置 0，避免 ConfigValidator 报「字段已失效」告警。
            DefenderLossFactor = 0,
            StalemateLossRate = 0,

            SpecialtyBonus = new Dictionary<string, int>
            {
                { "Agriculture:FarmOverseer", 20 },
                { "Siege:FortifyOverseer", 15 },
                { "Cavalry:General", 15 },
                { "Logistics:Any", 5 },
                { "Diplomacy:Scoutmaster", 10 },
            },
        };

    /// <summary>创建示例世界状态。</summary>
    /// <param name="seed">随机种子。相同种子产生完全一致的初始状态。</param>
    /// <returns>3 势力 / 6 城 / 5 人才 / 2 支部队的完整世界状态。</returns>
    public static WorldState CreateWorld(int seed = 12345)
    {
        return new WorldState
        {
            Year = 1,
            Month = 1,
            Seed = seed,
            Difficulty = DifficultyLevel.Normal,
            ActionsThisMonth = 0,
            Player = new PlayerProfile
            {
                Id = Order.PlayerId,
                Name = "玩家",
                Command = 60,
                IsPersonallyCommanding = false,
                CommandingArmyId = -1,
            },
            Factions = CreateFactions(),
            Cities = CreateCities(),
            Armies = CreateArmies(),
            Officers = CreateOfficers(),
            PendingOrders = new List<PendingOrder>(),
            EventLog = new List<EventRecord>(),
        };
    }

    /// <summary>创建 3 个势力。</summary>
    /// <returns>势力列表。</returns>
    private static List<Faction> CreateFactions()
        => new()
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
            new Faction
            {
                Id = 1, Name = "魏", IsPlayer = false, CapitalCityId = 2,
                Resources = new Resources
                {
                    Grain = 4000, Silver = 4000, Soldier = 400,
                    Morale = 75, Stability = 75,
                },
            },
            new Faction
            {
                Id = 2, Name = "蜀", IsPlayer = false, CapitalCityId = 5,
                Resources = new Resources
                {
                    Grain = 3000, Silver = 3000, Soldier = 300,
                    Morale = 70, Stability = 70,
                },
            },
        };

    /// <summary>创建 6 座城池，2×3 网格布局。</summary>
    /// <returns>城池列表。</returns>
    private static List<City> CreateCities()
        => new()
        {
            new City
            {
                Id = 0, Name = "洛阳", OwnerFactionId = 0,
                Population = 10000, MaxPopulation = 20000,
                Defense = 100, MaxDefense = 300,
                FortifyLevel = 1, FarmLevel = 1,
                IsCapital = true, Terrain = TerrainType.Plain,
                AdjacentCityIds = new List<int> { 1, 3 },
            },
            new City
            {
                Id = 1, Name = "长安", OwnerFactionId = 1,
                Population = 9000, MaxPopulation = 18000,
                Defense = 90, MaxDefense = 300,
                FortifyLevel = 1, FarmLevel = 1,
                IsCapital = false, Terrain = TerrainType.Hill,
                AdjacentCityIds = new List<int> { 0, 2, 4 },
            },
            new City
            {
                Id = 2, Name = "邯郸", OwnerFactionId = 1,
                Population = 8000, MaxPopulation = 16000,
                Defense = 95, MaxDefense = 300,
                FortifyLevel = 2, FarmLevel = 1,
                IsCapital = true, Terrain = TerrainType.Plain,
                AdjacentCityIds = new List<int> { 1, 5 },
            },
            new City
            {
                Id = 3, Name = "宛城", OwnerFactionId = 0,
                Population = 7000, MaxPopulation = 15000,
                Defense = 80, MaxDefense = 300,
                FortifyLevel = 1, FarmLevel = 2,
                IsCapital = false, Terrain = TerrainType.Plain,
                AdjacentCityIds = new List<int> { 0, 4 },
            },
            new City
            {
                Id = 4, Name = "江陵", OwnerFactionId = 2,
                Population = 7500, MaxPopulation = 15000,
                Defense = 85, MaxDefense = 300,
                FortifyLevel = 1, FarmLevel = 1,
                IsCapital = false, Terrain = TerrainType.River,
                AdjacentCityIds = new List<int> { 1, 3, 5 },
            },
            new City
            {
                Id = 5, Name = "成都", OwnerFactionId = 2,
                Population = 9500, MaxPopulation = 19000,
                Defense = 110, MaxDefense = 300,
                FortifyLevel = 2, FarmLevel = 2,
                IsCapital = true, Terrain = TerrainType.Plain,
                AdjacentCityIds = new List<int> { 2, 4 },
            },
        };

    /// <summary>创建 2 支部队。</summary>
    /// <returns>部队列表。</returns>
    private static List<Army> CreateArmies()
        => new()
        {
            new Army
            {
                Id = 0, OwnerFactionId = 0, OfficerId = 3,
                Soldiers = 300, Morale = 80, MoraleMax = 100,
                PositionCityId = 0, TargetCityId = -1,
                Stance = ArmyStance.Idle, HasRestedThisMonth = false,
            },
            new Army
            {
                Id = 1, OwnerFactionId = 1, OfficerId = 4,
                Soldiers = 250, Morale = 75, MoraleMax = 100,
                PositionCityId = 2, TargetCityId = -1,
                Stance = ArmyStance.Idle, HasRestedThisMonth = false,
            },
        };

    /// <summary>创建 5 名人才。</summary>
    /// <returns>人才列表。</returns>
    private static List<Officer> CreateOfficers()
        => new()
        {
            new Officer
            {
                Id = 0, Name = "张良", OwnerFactionId = 0, PostCityId = -1,
                Post = PostType.None, PostedSinceMonths = 0,
                Command = 70, Governance = 90, Loyalty = 90, Age = 45,
                IsAlive = true, Specialty = OfficerSpecialty.Diplomacy,
            },
            new Officer
            {
                Id = 1, Name = "萧何", OwnerFactionId = 0, PostCityId = -1,
                Post = PostType.None, PostedSinceMonths = 0,
                Command = 40, Governance = 95, Loyalty = 95, Age = 50,
                IsAlive = true, Specialty = OfficerSpecialty.Agriculture,
            },
            new Officer
            {
                Id = 2, Name = "韩信", OwnerFactionId = 1, PostCityId = -1,
                Post = PostType.None, PostedSinceMonths = 0,
                Command = 98, Governance = 60, Loyalty = 70, Age = 35,
                IsAlive = true, Specialty = OfficerSpecialty.Cavalry,
            },
            new Officer
            {
                Id = 3, Name = "诸葛亮", OwnerFactionId = 2, PostCityId = -1,
                Post = PostType.None, PostedSinceMonths = 0,
                Command = 80, Governance = 100, Loyalty = 100, Age = 30,
                IsAlive = true, Specialty = OfficerSpecialty.Logistics,
            },
            new Officer
            {
                Id = 4, Name = "陈庆之", OwnerFactionId = -1, PostCityId = -1,
                Post = PostType.None, PostedSinceMonths = 0,
                Command = 92, Governance = 50, Loyalty = 60, Age = 28,
                IsAlive = true, Specialty = OfficerSpecialty.Siege,
            },
        };

    /// <summary>创建示例事件库。</summary>
    /// <returns>事件模板列表。</returns>
    /// <remarks>
    /// 【v1.2】<c>GameEventTemplate.Id</c> 已从 string 改为 int（跨语言 JSON 兼容）。
    /// 概率为万分比，10000 = 100%。同 Category 概率之和不得超过 6000。
    /// </remarks>
    public static List<GameEventTemplate> CreateEvents()
        => new()
        {
            new GameEventTemplate
            {
                Id = 1001,
                Category = EventCategory.Personnel,
                Probability = 1000,
                Message = "有贤士途经此地，求访无果。",
            },
            new GameEventTemplate
            {
                Id = 1002,
                Category = EventCategory.Economy,
                Probability = 1500,
                Message = "风调雨顺，粮产喜人。",
            },
            new GameEventTemplate
            {
                Id = 1003,
                Category = EventCategory.Disaster,
                Probability = 500,
                Message = "疫病流行，人心惶惶。",
            },
        };
}
