// ============================================================================
//  胜负判定
// ============================================================================
//  来源：01_设计规格层/3_战斗系统设计.md 第五节
//
//  【重要更正】原实现把「玩家仅剩一城」判为【胜利】，方向反了。
//  设计第五节明确：
//    - 玩家仅剩 1 座城        → 失败
//    - 玩家全部城池易主       → 立即失败
//    - 玩家拥有大陆 80% 以上 → 立即胜利
//    - 存活满 72 个月         → 按城池数排名结算
//  原型目标是攻城，把城池打光才是赢；只剩一座城是败局。
//
//  【纯函数】只读 state，返回判定结论，不修改任何字段。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Systems;

/// <summary>对局结局类型。</summary>
public enum GameOutcome
{
    /// <summary>未分出胜负，继续。</summary>
    Ongoing = 0,

    /// <summary>玩家胜利。</summary>
    Victory = 1,

    /// <summary>玩家失败。</summary>
    Defeat = 2,

    /// <summary>超过硬上限月数，按城池数排名结算。</summary>
    TimeoutSettled = 3,
}

/// <summary>
/// 胜负判定器。对应流水线的第 10 步。
/// </summary>
public sealed class VictoryChecker
{
    /// <summary>胜负判定结论。</summary>
    /// <param name="Outcome">结局类型。</param>
    /// <param name="GameOver">是否已分出结局（应交还控制权）。</param>
    /// <param name="Reason">面向玩家的中文说明。未结束时为 null。</param>
    public readonly record struct VictoryResult(
        GameOutcome Outcome,
        bool GameOver,
        string? Reason);

    /// <summary>
    /// 判定本回合是否分出胜负。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>判定结论。未分出胜负时 GameOver 为 false、Reason 为 null。</returns>
    /// <remarks>
    /// 判定顺序：先判失败（城池 ≤1），再判胜利（占比 ≥80%），
    /// 最后判超时（≥72 个月）。顺序不能颠倒——
    /// 若先判胜利，玩家在最后一城时若恰好不满足失败条件会得到错误结论。
    /// </remarks>
    public VictoryResult Evaluate(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var playerFaction = state.GetPlayerFaction();
        int playerCityCount = playerFaction?.Id is int factionId
            ? state.GetCityIdsOfFaction(factionId).Count
            : 0;

        int totalCities = state.Cities.Count;

        // ---- 规则一：玩家仅剩一城或全城易主 → 失败 ----
        if (playerCityCount <= DesignConstants.DefeatCityThreshold)
        {
            return new VictoryResult(
                GameOutcome.Defeat,
                true,
                playerCityCount == 0
                    ? "玩家全部城池易主，败局已定。"
                    : $"玩家仅剩 {playerCityCount} 城，败局已定。");
        }

        // ---- 规则二：玩家拥有大陆 80% 以上城池 → 立即胜利 ----
        // 【单位一致性】城市占比按「万分比」计算（3333 = 33.33%），
        // 因此阈值也必须是万分比，80% = 8000。
        // 【曾经的 bug】阈值误写为 800（千分比），导致 2/6 城（3333）
        // 就被判为胜利，中局直接结束游戏。单位必须与被比较量一致。
        if (totalCities > 0)
        {
            int playerPerMille = playerCityCount * DesignConstants.TenThousandBase / totalCities;
            if (playerPerMille >= DesignConstants.VictoryCityPerMille)
            {
                return new VictoryResult(
                    GameOutcome.Victory,
                    true,
                    $"玩家已控制全境 {playerCityCount}/{totalCities} 城，达成胜利条件。");
            }
        }

        // ---- 规则三：所有势力都不再拥有城池 → 世界归于沉寂 ----
        bool allFactionsEliminated = state.Factions.All(f =>
            state.GetCityIdsOfFaction(f.Id).Count <= PlaceholderNumbers.EliminatedCityThreshold);

        if (allFactionsEliminated)
        {
            return new VictoryResult(
                GameOutcome.Defeat,
                true,
                "所有势力均已覆灭，游戏结束。");
        }

        // ---- 规则四：存活满 72 个月 → 按城池数强制结算 ----
        int elapsedMonths = TimeUtil.TotalMonths(state.Year, state.Month);
        if (elapsedMonths >= DesignConstants.MaxGameMonths)
        {
            return new VictoryResult(
                GameOutcome.TimeoutSettled,
                true,
                $"已满 {elapsedMonths} 个月（上限 {DesignConstants.MaxGameMonths} 个月），" +
                $"按城池数结算：玩家 {playerCityCount} 城。");
        }

        return new VictoryResult(GameOutcome.Ongoing, false, null);
    }

    /// <summary>
    /// 计算一局已进行的月数。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>自开局（1 年 1 月）起经过的月数。</returns>
    public static int GetElapsedMonths(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return TimeUtil.TotalMonths(state.Year, state.Month);
    }
}
