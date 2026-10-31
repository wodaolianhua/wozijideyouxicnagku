// ============================================================================
//  06 工程与验证层 —— 数据模型
//  【定位】本文件只放 06 包自己的数据类型，不重复声明任何契约类型。
//  契约类型（WorldState / Order / ResolveResult / ActionType …）一律来自
//  StrategyGame.Contracts，经由 03 / 04 的 ProjectReference 传递。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Sim;

/// <summary>
/// 终局原因（本包自有的稳定标识符）。
/// </summary>
/// <remarks>
/// 【为什么不用上游的 GameOverReason 文本】
/// 上游 <c>VictoryChecker</c> 的胜负判定是**玩家口径**的：
/// 它用 <c>state.GetPlayerFaction()</c>（即 <c>Faction.IsPlayer == true</c> 的势力）
/// 作为判定基准。当三方全部由 AI 驱动时（三个势力 IsPlayer 皆 false），
/// 该基准势力不存在，上游判定会在第 1 个月直接给出「玩家全部城池易主，败局已定」。
/// 因此本包在宿主层按**三方混战口径**自行判定终局，
/// 上游的 turn.GameOver 仍被原样记录（见 <see cref="GameResult.UpstreamGameOverReason"/>），
/// 作为上游设计问题的证据上报，不参与终局控制。
/// </remarks>
public enum SimGameOverReason
{
    /// <summary>未终局。</summary>
    None = 0,

    /// <summary>终局：仅剩 0 或 1 个势力仍拥有城池（即有势力已被打灭）。</summary>
    Elimination = 1,

    /// <summary>终局：跑满月数上限仍未分出胜负（僵持）。</summary>
    Stalemate = 2,
}

/// <summary>
/// 终局时单个势力的快照。
/// </summary>
public sealed class FactionEndState
{
    /// <summary>势力 Id。</summary>
    public int FactionId { get; set; }

    /// <summary>势力名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>终局时拥有的城池数。</summary>
    public int Cities { get; set; }

    /// <summary>终局时全部部队的总兵力。</summary>
    public int Soldiers { get; set; }

    /// <summary>终局时势力资源中的粮。</summary>
    public int Grain { get; set; }

    /// <summary>终局时势力资源中的银。</summary>
    public int Silver { get; set; }

    /// <summary>终局时势力资源中的可用兵源。</summary>
    public int SoldierStock { get; set; }

    /// <summary>本局该势力的月均兵源存量（势力资源 Soldier 的月平均）。</summary>
    public int AverageSoldierStock { get; set; }
}

/// <summary>
/// 单局模拟结果。CSV 每行对应一个本对象。
/// </summary>
public sealed class GameResult
{
    /// <summary>批次内局序（0 基）。</summary>
    public int GameIndex { get; set; }

    /// <summary>本局随机种子。</summary>
    public int Seed { get; set; }

    /// <summary>胜方势力 Id；无胜方（僵持 / 全灭）为 -1。</summary>
    public int WinnerFactionId { get; set; } = -1;

    /// <summary>终局原因。</summary>
    public SimGameOverReason GameOverReason { get; set; }

    /// <summary>终局原因的稳定标识符（写 CSV 用，不含中文）。</summary>
    public string GameOverReasonKey
        => GameOverReason switch
        {
            SimGameOverReason.Elimination => "Elimination",
            SimGameOverReason.Stalemate => "Stalemate",
            _ => "None",
        };

    /// <summary>本局实际推进的月数（存活月数）。</summary>
    public int MonthsSurvived { get; set; }

    /// <summary>终局时三个势力的状态。</summary>
    public List<FactionEndState> Factions { get; set; } = new();

    /// <summary>各行动类型的发起数（含被拒绝的尝试）。</summary>
    public Dictionary<ActionType, int> OrdersByAction { get; set; } = new();

    /// <summary>AI 产出的指令总数。</summary>
    public int AiOrderTotal { get; set; }
    /// <summary>各类结算结果码的计数。</summary>
    public Dictionary<ResolveResultCode, int> ResultCodes { get; set; } = new();

    /// <summary>按行动类型分列的拒绝码计数。键为行动类型，值为「拒绝码 → 次数」。</summary>
    public Dictionary<ActionType, Dictionary<ResolveResultCode, int>> RejectsByAction { get; set; } = new();

    /// <summary>RejectedNotImplemented（码 8）的计数。必须恒为 0。</summary>
    public int NotImplementedCount
        => ResultCodes.TryGetValue(ResolveResultCode.RejectedNotImplemented, out int v) ? v : 0;

    /// <summary>本局事件总数。</summary>
    public int EventCount { get; set; }

    /// <summary>哗变事件次数（EventKeys.Mutiny）。</summary>
    public int MutinyCount { get; set; }

    /// <summary>攻陷事件次数（EventKeys.CityCaptured）。</summary>
    public int CityCapturedCount { get; set; }

    /// <summary>本局全部部队士气样本的平均值（整数除法，向下取整）。</summary>
    public int AverageArmyMorale { get; set; }

    /// <summary>本局全部部队士气样本中低于 30 的占比（万分比）。</summary>
    public int LowMoraleRatioPerMille { get; set; }

    /// <summary>上游 TurnPipeline 判定为终局的月份；-1 表示上游从未判终局。</summary>
    public int UpstreamGameOverMonth { get; set; } = -1;

    /// <summary>上游 TurnPipeline 给出的终局原因原文（中文）。</summary>
    public string? UpstreamGameOverReason { get; set; }
}

/// <summary>
/// 批次聚合报告。
/// </summary>
public sealed class BatchReport
{
    /// <summary>总局数。</summary>
    public int TotalGames { get; set; }

    /// <summary>基准种子。</summary>
    public int BaseSeed { get; set; }

    /// <summary>月数上限。</summary>
    public int MaxMonths { get; set; }

    /// <summary>各势力的胜场数。</summary>
    public Dictionary<int, int> WinsByFaction { get; set; } = new();

    /// <summary>僵持（未分胜负）局数。</summary>
    public int StalemateGames { get; set; }

    /// <summary>10 回合内终局的局数。</summary>
    public int EarlyEndGames { get; set; }

    /// <summary>存活月数总和。</summary>
    public long TotalMonths { get; set; }

    /// <summary>各行动类型的发起总数。</summary>
    public Dictionary<ActionType, int> OrdersByAction { get; set; } = new();

    /// <summary>各类结算结果码的总计数。</summary>
    public Dictionary<ResolveResultCode, int> ResultCodes { get; set; } = new();

    /// <summary>RejectedNotImplemented 总数。必须为 0。</summary>
    public int NotImplementedTotal { get; set; }

    /// <summary>事件总数。</summary>
    public int TotalEvents { get; set; }

    /// <summary>哗变总次数。</summary>
    public int TotalMutiny { get; set; }

    /// <summary>攻陷总次数。</summary>
    public int TotalCityCaptured { get; set; }

    /// <summary>全部部队士气样本的平均值。</summary>
    public int AverageArmyMorale { get; set; }

    /// <summary>低士气（&lt;30）样本占比（万分比）。</summary>
    public int LowMoraleRatioPerMille { get; set; }

    /// <summary>各势力月均兵源存量的批次平均。</summary>
    public Dictionary<int, int> AverageSoldierStockByFaction { get; set; } = new();

    /// <summary>平均存活月数。</summary>
    public double AverageMonthsSurvived
        => TotalGames == 0 ? 0 : (double)TotalMonths / TotalGames;

    /// <summary>基准槽位（槽 0）胜率（万分比）。三方 AI 混战口径，非真实玩家胜率。</summary>
    public int BaselineSlotWinRatePerMille
        => TotalGames == 0
            ? 0
            : (int)((long)(WinsByFaction.TryGetValue(0, out int w) ? w : 0) * 10_000 / TotalGames);

    /// <summary>僵持率（万分比）。</summary>
    public int StalemateRatePerMille
        => TotalGames == 0 ? 0 : (int)((long)StalemateGames * 10_000 / TotalGames);

    /// <summary>10 回合内终局比例（万分比）。</summary>
    public int EarlyEndRatePerMille
        => TotalGames == 0 ? 0 : (int)((long)EarlyEndGames * 10_000 / TotalGames);

    /// <summary>攻陷次数 / Dispatch 尝试次数（万分比）。</summary>
    public int CapturePerDispatchPerMille
    {
        get
        {
            int dispatch = OrdersByAction.TryGetValue(ActionType.Dispatch, out int d) ? d : 0;
            if (dispatch == 0)
            {
                return 0;
            }

            return (int)((long)TotalCityCaptured * 10_000 / dispatch);
        }
    }
}
