// ============================================================================
//  种子敏感化 —— 可选的初始资源档位轮换
//
//  【为什么存在】
//    上游 03 的 SampleData.CreateWorld(seed) 只把 seed 存进 WorldState.Seed，
//    **不消费 seed 生成世界内容**：三个势力的初始资源、城池、部队、人才全部硬编码。
//    而全链路唯一的随机消费点是 04 的 BestFitPolicy.SelectBestFor ——
//    「多个人才 Governance 并列时随机取一个」。样例数据里五个人才的
//    Governance 互不相同（90 / 95 / 60 / 100 / 50），永远不并列，
//    于是随机流虽在推进、决策结果却恒定 → **不同 seed 跑出完全相同的一局**。
//
//  【本类的定位：06 层临时替代方案，不是上游修复】
//    本类只做一件事：把上游样例中三个势力**已有的**三档初始资源
//    （粮/银/兵源 5000-4000-3000 / 5000-4000-3000 / 500-400-300）
//    按 seed 生成的置换重新分配给三个势力。
//    · 不新增任何数值：三个档位全部取自 SampleData.CreateWorld 的原值；
//    · 不改上游任何文件；
//    · 不改 BalanceConfig；
//    · 目的只有一个：让三方 AI 混战的初始条件对称化，
//      使「基准槽位胜率」测的是 AI 决策质量，而不是「谁开局就最强」。
//
//    上游一旦把世界生成改为真正消费 seed，本类应整体弃用。
//
//  【口径纪律】
//    默认【关闭】。基准口径严格照抄 SampleData.CreateWorld(seed)，
//    与上游逐字段一致；开启后的一切差异都必须在报告中显式标注。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Randomization;

namespace StrategyGame.Sim;

/// <summary>
/// 世界种子敏感化器。
/// </summary>
public static class WorldVariator
{
    /// <summary>
    /// 扰动用的独立随机源盐值。与主随机流分离，
    /// 保证「开/关扰动」两种口径下主随机流的位置完全一致，
    /// 差异只来自初始资源本身。
    /// </summary>
    private const int VarianceSalt = 0x5A5A5A5A;

    /// <summary>
    /// 按 seed 轮换三个势力的初始资源档位。
    /// </summary>
    /// <param name="state">世界状态。将被就地修改其势力初始资源。</param>
    /// <param name="seed">本局种子。</param>
    /// <remarks>
    /// 做法：读出三个势力当前的初始资源构成三档，用独立随机源打乱后
    /// 写回。资源数值本身一个都不新增、不修改。
    /// </remarks>
    public static void ApplyResourceRotation(WorldState state, int seed)
    {
        ArgumentNullException.ThrowIfNull(state);

        var factions = new List<Faction>(state.Factions);
        if (factions.Count < 2)
        {
            return;
        }

        // 取前三个势力的初始资源作为三档（顺序按 FactionId 升序，确定性）。
        factions.Sort((a, b) => a.Id.CompareTo(b.Id));
        int tierCount = Math.Min(3, factions.Count);

        var tiers = new List<(int Grain, int Silver, int Soldier)>(tierCount);
        for (int i = 0; i < tierCount; i++)
        {
            tiers.Add((
                factions[i].Resources.Grain,
                factions[i].Resources.Silver,
                factions[i].Resources.Soldier));
        }

        // 用独立随机源做 Fisher-Yates 置换，得到「哪个势力拿哪档」的分配。
        var rng = new DeterministicRandom(unchecked(seed ^ VarianceSalt));
        for (int i = tierCount - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (tiers[i], tiers[j]) = (tiers[j], tiers[i]);
        }

        for (int i = 0; i < tierCount; i++)
        {
            factions[i].Resources.Grain = tiers[i].Grain;
            factions[i].Resources.Silver = tiers[i].Silver;
            factions[i].Resources.Soldier = tiers[i].Soldier;
        }
    }
}
