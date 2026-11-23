using StrategyGame.Contracts;

namespace StrategyGame.Core;

/// <summary>
/// 第一阶段可见性策略：敌军行军全程可见。
/// 战争迷雾阶段到来时另写一个 IVisibilityPolicy 实现替换，不动调用方。
/// </summary>
public sealed class AlwaysVisiblePolicy : IVisibilityPolicy
{
    public bool IsEnemyVisible(Army enemyArmy, Army observer, WorldState state) => true;
}
