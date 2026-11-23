// ============================================================================
//  士气系统 —— 部队士气（Army.Morale）的月度结算与哗变处理
//  来源：01_设计规格层/4_数值框架.md 第 5.2 / 5.4 节
//
//  【命名陷阱 · 必读】
//  契约里有两个字段都叫 Morale，但完全不是一回事：
//    Army.Morale      = 部队士气，每支部队一个值，影响战损倍率与哗变 ← 本文件负责
//    Resources.Morale = 势力民心，全势力一个值，影响 Stability 与兵源 ← EconomySystem 负责
//  实现时本文件一律用 army / morale 命名，并在关键处加注释提醒后来人。
//
//  【纯函数】所有方法只读 state，产出 StateDelta 与 EventRecord，不修改任何字段。
//
//  【为什么士气必须有真实压力】
//  低士气惩罚档位（×1.5 / ×2.5 / ×4.0）是代入实算定出来的，不是拍脑袋：
//  初稿 ×1.2/1.5/2.0 在实测中「士气从60 掉到 10，战局结果一次都没变」
//  ——只是多烧点兵，士气系统形同装饰。现档能让士气 20 时均势攻城
//  从攻陷翻成击退，这才让「该休整还是该趁胜追击」成为真实的战略选择。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Systems;

/// <summary>
/// 部队士气月度结算。处理自然恢复、和平腐化与士气归零哗变。
/// </summary>
/// <remarks>
/// 【倍率的施加位置是硬约束】<see cref="Army.GetMoralePenalty"/> 返回的倍率
/// 必须由调用方乘在 lossRate 上、且<b>在与击退阈值比较之前</b>，
/// 并且<b>不得</b>对 lossRate 做 min(x, 1.0) 钳制。
/// 一旦钳制，低士气永远无法把战损推过阈值，整个士气系统等于没做。
/// 见 <see cref="BattleSystem.Resolve"/> 的说明。
/// </remarks>
public sealed class MoraleSystem
{
    private readonly BalanceConfig _config;

    /// <summary>创建士气系统。</summary>
    /// <param name="config">数值配置。士气系数全部从此读取。</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> 为 null 时抛出。</exception>
    public MoraleSystem(BalanceConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// 月度士气结算：参战周期判定、自然恢复 / 和平腐化（互斥）、哗变检查。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>状态增量列表。含月初标记翻转、士气、连续未参战月数、参战标记消费与哗变兵力。</returns>
    /// <remarks>
    /// 【流程，对每支部队（按序）】
    /// <code>
    /// 0. 月度标记月初翻转（第 3 轮补丁·修复二）：HasRestedThisMonth /
    ///    HasMutiniedThisMonth 语义都是「同一月内防重复」，月度结算开头先翻转
    ///    （仅当为 true 时产出 1→0 delta），再走四分支。
    ///    【先清后判】翻转只产出 delta、不改 state（纯函数约束），哗变判定
    ///    使用翻转后的局部值：上月哗变、本月士气仍 ≤ 0 的部队允许再次哗变。
    /// 1. 若 HasEngagedThisMonth（本结算周期内参过战，v1.4 参战信号）：
    ///    不恢复、不腐化——战斗增减已在战斗结算时由 DispatchResolver 按 5.4 结过；
    ///    MonthsWithoutBattle 保持不变（战斗时已清零，不 +1）；
    ///    参战标记消费即清（1 → 0）。
    /// 2. 否则若本月已休整（第 6 轮补丁）：挂起恢复/腐化——Rest 已给 +3（冷却防刷），
    ///    此处士气不增不减、不产出士气 delta；MonthsWithoutBattle 仍 +1（休整不是战斗），
    ///    但腐化暂不生效。停止休整后 −6/月腐化恢复。
    /// 3. 未参战、未休整，且 MonthsWithoutBattle + 1 &lt; 阈值 → morale += MoraleDecayPerMonth（恢复）
    /// 4. 未参战、未休整，且 MonthsWithoutBattle + 1 ≥ 阈值 → morale -= MoraleDecayNoBattlePerMonth（腐化）
    /// 5. morale 夹到 [0, MoraleMax]
    /// 6. morale ≤ 0 且未哗变过（按翻转后的标记）→ 解散 MutinyRoutRate 比例兵力并标记（各分支都判）
    /// </code>
    ///
    /// 【为什么先读标记再 +1】参战信号（v1.4）解决「计数为 0 无法区分新部队与刚参战」
    /// 的信号碰撞：新部队标记默认 false，开局第一次结算走恢复分支；
    /// 刚参战的部队标记为 true，当月结算不恢复不腐化。
    ///
    /// 【为什么先累加再判定】阈值 4 的语义是「连续 4 个月没打仗就开始腐化」，
    /// 因此本月是第 4 个无战月时当月就应扣，而不是等到第 5 月。
    ///
    /// 【恢复与腐化绝对不得在同一个月同时生效】这是第 2 轮补丁的核心。
    /// 早期实现无脑先 +3 再看是否叠加扣减，导致「无脑连续攻城」成为最优解。
    /// </remarks>
    public List<StateDelta> ResolveMonthlyMorale(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var deltas = new List<StateDelta>();

        foreach (var army in state.Armies)
        {
            if (army.Soldiers <= 0)
            {
                continue;
            }

            // ---- 步骤 0：月度标记月初翻转（第 3 轮补丁·修复二）----
            // HasRestedThisMonth 旧实现只在出兵时清零，纯休整部队一生只能休整一次；
            // HasMutiniedThisMonth 从不清零，哗变一生只有一次。两者语义都是
            // 「同一月内防重复」，因此在每次月度结算开头统一翻转。
            // 注意：翻转只产出 delta，不改 state（纯函数约束）；
            // RestResolver / DispatchResolver 对这两个字段的现有写法不动。
            //
            // 【第 6 轮补丁 · 休整分支】必须在翻转动作之前捕获旧值：
            // 休整分支要据此判断「本月是否执行过休整」，若先翻转再判会丢失信号。
            bool restedThisMonth = army.HasRestedThisMonth;
            if (army.HasRestedThisMonth)
            {
                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.Army,
                    TargetId = army.Id,
                    Field = DeltaField.ArmyHasRested,
                    OldValue = 1,
                    NewValue = 0,
                });
            }

            // 【先清后判】哗变判定用翻转后的局部值：上月哗变、本月士气仍崩溃
            // （如参战分支不恢复）→ 允许再次哗变。「哗变一生一次」就此消除。
            bool hasMutiniedThisCycle = army.HasMutiniedThisMonth;
            if (hasMutiniedThisCycle)
            {
                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.Army,
                    TargetId = army.Id,
                    Field = DeltaField.ArmyHasMutiniedThisMonth,
                    OldValue = 1,
                    NewValue = 0,
                });

                // 先清：本结算周期内按翻转后的值（false）参与哗变判定。
                hasMutiniedThisCycle = false;
            }

            int newMorale;
            int monthsWithoutBattle;

            if (army.HasEngagedThisMonth)
            {
                // ---- 分支一：参战周期 ----
                // 本结算周期内发生过战斗：士气增减已由战斗结算（5.4）结过，
                // 月度结算既不恢复也不腐化；计数保持（战斗时已清零），标记消费即清。
                newMorale = army.Morale;
                monthsWithoutBattle = army.MonthsWithoutBattle;

                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.Army,
                    TargetId = army.Id,
                    Field = DeltaField.ArmyHasEngagedThisMonth,
                    OldValue = army.HasEngagedThisMonth ? 1 : 0,
                    NewValue = 0,
                });
            }
            else if (restedThisMonth)
            {
                // ---- 分支二：休整分支（第 6 轮补丁）----
                // 休整部队本月挂起和平腐化：Rest 结算已给 +3/月（冷却防刷），
                // 此处不再自然恢复；MonthsWithoutBattle 仍累加但腐化暂不生效。
                // 停止休整后 −6/月腐化恢复。
                //
                // 休整不是战斗：连续无战月计数照走（+1），越不越阈值都不影响
                // 本月（腐化挂起），因此士气既不 +MoraleDecayPerMonth 也不
                // −MoraleDecayNoBattlePerMonth，净变化 0（Rest 已给 +3）。
                newMorale = army.Morale;
                monthsWithoutBattle = army.MonthsWithoutBattle + 1;
            }
            else
            {
                // ---- 分支三 / 四：未参战月（恢复与腐化互斥，按序判定）----
                // 用结算前的值 +1 后比较：本月是第 4 个无战月时当月就扣。
                monthsWithoutBattle = army.MonthsWithoutBattle + 1;

                bool rotting = monthsWithoutBattle >= _config.MoraleDecayNoBattleThreshold;

                // 恢复与腐化绝对不得在同一个月同时生效（第 2 轮补丁核心）。
                newMorale = rotting
                    ? army.Morale - _config.MoraleDecayNoBattlePerMonth
                    : army.Morale + _config.MoraleDecayPerMonth;

                newMorale = Math.Clamp(newMorale, 0, Math.Max(0, army.MoraleMax));
            }

            if (newMorale != army.Morale)
            {
                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.Army,
                    TargetId = army.Id,
                    Field = DeltaField.ArmyMorale,
                    OldValue = army.Morale,
                    NewValue = newMorale,
                });
            }

            if (monthsWithoutBattle != army.MonthsWithoutBattle)
            {
                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.Army,
                    TargetId = army.Id,
                    Field = DeltaField.ArmyMonthsWithoutBattle,
                    OldValue = army.MonthsWithoutBattle,
                    NewValue = monthsWithoutBattle,
                });
            }

            // ---- 哗变：士气崩溃，解散部分兵力 ----
            //
            // 【判定时机 · 踩过的坑】早期实现只看结算后的 newMorale <= 0，
            // 结果哗变成了不可达分支：自然恢复 +3 恒大于和平腐化 −2，
            // 士气一旦归零，下月就被拉回 3，永远不会≤ 0。
            // 「士气崩溃的部队当月就散」才是设计语义——不给它一个月的恢复缓冲。
            // 因此判定取「结算前/后」两者的较小值。
            int moraleFloor = Math.Min(army.Morale, newMorale);

            if (moraleFloor <= 0 && !hasMutiniedThisCycle)
            {
                int rout = (int)((long)army.Soldiers * _config.MutinyRoutRate
                    / EconomySystem.RatioBase);

                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.Army,
                    TargetId = army.Id,
                    Field = DeltaField.ArmySoldiers,
                    OldValue = army.Soldiers,
                    NewValue = army.Soldiers - rout,
                });

                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.Army,
                    TargetId = army.Id,
                    Field = DeltaField.ArmyHasMutiniedThisMonth,
                    // OldValue 取翻转后的当前值：月初翻转 delta（1→0）已先于本条
                    // 在同一列表内产出，哗变置位时刻标记的当前值恒为 0。
                    OldValue = 0,
                    NewValue = 1,
                });
            }
        }

        return deltas;
    }

    /// <summary>
    /// 产出一支部队因士气归零而哗变的事件记录。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="armyId">哗变部队 Id。</param>
    /// <returns>事件记录；未触发哗变时返回 null。</returns>
    /// <remarks>
    /// 单独提供是因为 <see cref="ResolveMonthlyMorale"/> 返回的是裸 Delta 列表，
    /// 而「哗变」是需要让玩家看见的历史，必须单独产出 <see cref="EventRecord"/>。
    /// </remarks>
    public EventRecord? BuildMutinyEvent(WorldState state, int armyId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var army = state.GetArmy(armyId);
        if (army == null)
        {
            return null;
        }

        int rout = (int)((long)army.Soldiers * _config.MutinyRoutRate / EconomySystem.RatioBase);

        return new EventRecord
        {
            Year = state.Year,
            Month = state.Month,
            Category = EventCategory.Internal,
            EventKey = EventKeys.Mutiny,
            Message = $"部队士气崩溃，哗变逃散 {rout} 人。",
            FactionId = army.OwnerFactionId,
        };
    }

    /// <summary>
    /// 计算休整后的士气。
    /// </summary>
    /// <param name="army">部队。只读。</param>
    /// <returns>休整后士气，上限为 <see cref="Army.MoraleMax"/>。</returns>
    /// <remarks>
    /// 恢复量 = <c>MoraleDecayPerMonth</c>（基准 3 点），
    /// 即 4_数值框架.md 5.2「自然恢复 +3 / 月」的同一口径——
    /// 休整只是把这份自然恢复立即兑现，不额外给加成，否则休整会变成
    /// 「最快的士气恢复手段」而压过「靠时间慢慢养」的选择。
    /// </remarks>
    public int CalculateMoraleAfterRest(Army army)
    {
        ArgumentNullException.ThrowIfNull(army);

        int recovered = army.Morale + _config.MoraleDecayPerMonth;
        return Math.Min(Math.Max(0, army.MoraleMax), recovered);
    }

    /// <summary>
    /// 计算一次战斗后攻方部队的士气变化值。
    /// </summary>
    /// <param name="outcome">战斗结果。</param>
    /// <returns>士气增量（可为负）。</returns>
    /// <remarks>
    /// 严格对应 4_数值框架.md 5.4「战斗时士气如何流转」：
    /// 攻陷 +10 / 击退 −12 / 未决 −3。
    /// </remarks>
    public int CalculateAttackerMoraleDelta(BattleOutcome outcome) => outcome switch
    {
        BattleOutcome.Capture => _config.MoraleGainOnCapture,
        BattleOutcome.Repelled => -_config.MoraleLossOnRepel,
        _ => -_config.MoraleLossOnStalemate,
    };

    /// <summary>
    /// 守方部队无论战果的士气减量。
    /// </summary>
    /// <returns>士气增量（恒为负或零）。</returns>
    /// <remarks>
    /// 守方统一 −4 是为了避免「龟缩无损」成为零成本策略：
    /// 若守方击退时不掉士气，玩家会发现「死守」是免费的。
    /// </remarks>
    public int CalculateDefenderMoraleDelta() => -_config.MoraleLossOnDefend;
}