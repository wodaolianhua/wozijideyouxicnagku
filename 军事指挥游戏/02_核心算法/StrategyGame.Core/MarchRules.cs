using StrategyGame.Contracts;

namespace StrategyGame.Core;

/// <summary>
/// 行军/遭遇规则的数值设定集中在这里，纯常量与纯查表，方便调参与单测。
/// </summary>
public static class MarchRules
{
    /// <summary>默认接战距离（世界坐标单位）。两军距离小于等于该值视为进入接战。</summary>
    public const double DefaultEngagementDistance = 0.5;

    /// <summary>损失结算基础比例。</summary>
    public const double BaseLossRate = 0.12;

    /// <summary>伏击方损失系数。</summary>
    public const double AmbusherLossFactor = 0.5;

    /// <summary>被伏击方损失系数。</summary>
    public const double AmbushedLossFactor = 1.6;

    /// <summary>每时间步各兵种能推进的“路径分量”（越大越快）。</summary>
    public static double UnitSpeed(UnitType type) => type switch
    {
        UnitType.Infantry => 1.0,
        UnitType.Cavalry => 1.8,
        UnitType.Archer => 1.1,
        UnitType.Siege => 0.6,
        _ => 1.0,
    };

    /// <summary>边类型对行军耗时的倍率（乘在 MapEdge.Cost 上）。</summary>
    public static double EdgeCostFactor(EdgeType type) => type switch
    {
        EdgeType.Road => 1.0,
        EdgeType.MountainPath => 1.6,
        _ => 1.0,
    };

    /// <summary>目标节点地形对行军耗时的倍率。</summary>
    public static double TerrainCostFactor(TerrainType terrain) => terrain switch
    {
        TerrainType.Plain => 1.0,
        TerrainType.Hill => 1.25,
        TerrainType.Mountain => 1.6,
        TerrainType.Forest => 1.15,
        TerrainType.River => 1.4,
        _ => 1.0,
    };

    /// <summary>地形防御加成（损失结算时作为除数，越大越抗打）。</summary>
    public static double TerrainDefenseFactor(TerrainType terrain) => terrain switch
    {
        TerrainType.Plain => 1.0,
        TerrainType.Hill => 1.2,
        TerrainType.Mountain => 1.5,
        TerrainType.Forest => 1.3,
        TerrainType.River => 1.1,
        _ => 1.0,
    };

    /// <summary>兵种攻击系数（损失结算时乘在兵力上）。</summary>
    public static double UnitAttackFactor(UnitType type) => type switch
    {
        UnitType.Infantry => 1.0,
        UnitType.Cavalry => 1.2,
        UnitType.Archer => 0.9,
        UnitType.Siege => 0.6,
        _ => 1.0,
    };

    /// <summary>是否敌对：当前只把玩家与敌军视为交战方，中立不参战。</summary>
    public static bool AreHostile(Faction a, Faction b)
        => (a == Faction.Player && b == Faction.Enemy)
           || (a == Faction.Enemy && b == Faction.Player);
}
