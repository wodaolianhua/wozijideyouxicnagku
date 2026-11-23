// ============================================================================
//  孤军撤回 —— 断粮道的部队得回家，不能永久滞留在敌城
// ============================================================================
//  【问题从哪来】未决（僵持）会把攻方部队留在敌城城下，姿态 Besieging。
//  如果这期间攻方前线的那座己方城池被第三方夺走，这支部队就既：
//    · 不在己方城里 → 吃不到每月新兵补给（第 8 步的补给口只在己方城）；
//    · 与任何己方城都不相邻 → 无法再对该城下出兵令（出兵的出发城必须是己方城）。
//  结果它在敌城里「永生」：既打不了，也补不上，兵力数字还挂在界面上骗人。
//  无头模拟里赤壤的一支部队就这样带着 2869 人卡了两年，兵源池同时积压两万八。
//
//  【规则】处置很简单，也是最符合直觉的那条：撤回最近的己方城池，就地整补。
//  确定性：多个候选时取 Id 最小的己方城（禁止用「最近」这种几何概念，
//  城池图没有坐标，距离没有定义）。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Systems;

/// <summary>
/// 孤军撤回系统。
/// </summary>
/// <remarks>
/// 纯函数：只读 state，产出 Delta 与事件，不修改任何字段。
/// </remarks>
public static class StrandedArmySystem
{
    /// <summary>
    /// 找出所有「滞留敌方城池且无法再出兵」的部队，把它们撤回己方城池。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="result">本回合结果，事件将累加其中。</param>
    /// <returns>状态增量列表。</returns>
    public static List<StateDelta> Resolve(WorldState state, TurnResult result)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(result);

        var deltas = new List<StateDelta>();

        // 按 Id 升序处理，保证确定性。
        var armies = new List<Army>(state.Armies);
        armies.Sort((a, b) => a.Id.CompareTo(b.Id));

        foreach (var army in armies)
        {
            // 【第 8 轮】行军中的部队跳过：它的位置只是路过（每月一跳的瞬态），
            // 孤军撤回会把它从行军路线上拽回己方城，直接破坏长途行军。
            // 行军到达后的滞留/围城部队不再有行军标记，照常接受孤军判定。
            if (army.MarchTargetCityId >= 0)
            {
                continue;
            }

            var here = state.GetCity(army.PositionCityId);
            if (here == null || here.OwnerFactionId == army.OwnerFactionId)
            {
                continue; // 在自己家里，无需处置
            }

            // 还有没有「挨着这座敌城的己方城池」？有就还能继续打，不算孤军。
            bool canStillAct = state.Cities.Any(c =>
                c.OwnerFactionId == army.OwnerFactionId
                && c.AdjacentCityIds.Contains(here.Id));

            if (canStillAct)
            {
                continue;
            }

            // 撤回「守备最空的己方城池」；兵力相同时取 Id 最小的城。
            // 【为什么不取 Id 最小】一开始就是取 Id 最小，结果所有孤军都往同一座城挤，
            // 其余城池变成零守备的空城，一打就掉——撤回反而成了资敌。
            // 往空城退既符合直觉（补空档），也能让 AI 的城防自然铺开。
            City? refuge = null;
            int refugeGarrison = int.MaxValue;

            foreach (var city in state.Cities)
            {
                if (city.OwnerFactionId != army.OwnerFactionId)
                {
                    continue;
                }

                int garrison = 0;
                foreach (var other in state.Armies)
                {
                    if (other.Id != army.Id && other.PositionCityId == city.Id
                        && other.OwnerFactionId == army.OwnerFactionId)
                    {
                        garrison += other.Soldiers;
                    }
                }

                if (garrison < refugeGarrison
                    || (garrison == refugeGarrison && refuge != null && city.Id < refuge.Id))
                {
                    refuge = city;
                    refugeGarrison = garrison;
                }
            }

            if (refuge == null)
            {
                continue;
            }

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Army,
                TargetId = army.Id,
                Field = DeltaField.ArmyPositionCityId,
                OldValue = army.PositionCityId,
                NewValue = refuge.Id,
            });

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Army,
                TargetId = army.Id,
                Field = DeltaField.ArmyTargetCityId,
                OldValue = army.TargetCityId,
                NewValue = -1,
            });

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Army,
                TargetId = army.Id,
                Field = DeltaField.ArmyStance,
                OldValue = (int)army.Stance,
                NewValue = (int)ArmyStance.Idle,
            });

            var faction = state.GetFaction(army.OwnerFactionId);
            var evt = new EventRecord
            {
                Year = state.Year,
                Month = state.Month,
                Category = EventCategory.Military,
                EventKey = EventKeys.Dispatch,
                Message = $"{faction?.Name ?? "某势力"}孤军粮道被断，{army.Soldiers} 人自{here.Name}撤回{refuge.Name}。",
                FactionId = army.OwnerFactionId,
            };

            result.Events.Add(evt);
        }

        return deltas;
    }
}
