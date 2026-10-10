// ============================================================================
//  胜负判定
// ============================================================================
//  规则：玩家仅剩一城 → 胜利；全势力覆灭 → 结束。
//  纯函数：只读 state，返回判定结论，不修改任何字段。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Systems;

/// <summary>
/// 胜负判定器。对应流水线的第 10 步。
/// </summary>
/// <remarks>
/// 【设计意图】胜利条件刻意设为「仅剩一城」而非「攻下所有城」，
/// 让战争有明确的收敛终点，避免无头模拟跑不出结果。
/// </remarks>
public sealed class VictoryChecker
{
    /// <summary>胜利判定结论。</summary>
    public readonly record struct VictoryResult(bool GameOver, string? Reason);

    /// <summary>
    /// 判定本回合是否分出胜负。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>判定结论。未分出胜负时 GameOver 为 false、Reason 为 null。</returns>
    public VictoryResult Evaluate(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // 规则一：所有势力都不再拥有城池 → 世界归于沉寂，结束。
        bool allFactionsEliminated = state.Factions.All(f =>
            state.GetCityIdsOfFaction(f.Id).Count <= PlaceholderNumbers.EliminatedCityThreshold);

        if (allFactionsEliminated)
        {
            return new VictoryResult(true, "所有势力均已覆灭，游戏结束。");
        }

        // 规则二：玩家仅剩一城 → 达成胜利目标。
        var playerFaction = state.GetPlayerFaction();
        if (playerFaction != null)
        {
            int playerCityCount = state.GetCityIdsOfFaction(playerFaction.Id).Count;
            if (playerCityCount <= PlaceholderNumbers.VictoryCityThreshold)
            {
                return new VictoryResult(true, $"玩家仅剩 {playerCityCount} 城，达成胜利条件。");
            }
        }

        return new VictoryResult(false, null);
    }
}
