// ============================================================================
//  长途行军推进 —— 月 tick 中把行军部队沿路径逐跳推进，到达即结算
// ============================================================================
//  【第 8 轮】玩家对非相邻城出兵不再被相邻校验拒绝，而是「开拔」：
//  DispatchResolver 在开拔时写入部队的行军字段（目标城、到达年月、路径快照），
//  本系统在每个月的流水线中推进这些部队，到达目标城时触发与相邻出兵
//  共用的攻城结算（SiegeSettlement）——同一份战斗逻辑，不复制。
//
//  【推进模型（首版）】
//    · 行军时间 = 跳数 × 每跳月数（PlaceholderNumbers.MarchMonthsPerHop，默认 1）；
//    · 下单月部队仍在出发城（整备），次月起每月前进一跳，到达月触发结算；
//    · 路径快照 = [出发城, 途经城…, 目标城]，当前位置按「已走月数 / 每跳月数」
//      从路径下标推导，不另存进度字段。
//
//  【扩展点（第 8 轮备忘）】途中拦截首版不做：部队路过敌方城不发生遭遇战。
//  未来做拦截时，在本类「中途推进」分支按路径下一跳的城池归属挂接判定即可，
//  不需要改 DispatchResolver 与路径服务。
//
//  【纯函数】只读 state，状态变化只通过 StateDelta 表达；事件追加到
//  state.EventLog（与 TurnPipeline.RunArmyMorale 的哗变事件同口径：
//  第 11 步会把 EventLog 追加进 TurnResult.Events，供表现层读取）。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Resolvers;

namespace StrategyGame.Core.Systems;

/// <summary>
/// 长途行军月度推进系统。
/// </summary>
public static class MarchSystem
{
    /// <summary>
    /// 推进所有行军中的部队，产出位置/战斗/清空等 Delta 与事件。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="config">数值配置。到达战斗的系数从此读取。</param>
    /// <returns>状态增量列表（按部队 Id 升序处理，保证确定性）。</returns>
    public static List<StateDelta> Resolve(WorldState state, BalanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(config);

        var deltas = new List<StateDelta>();

        // 按 Id 升序处理：确定性铁律（遍历顺序不定会让无头模拟不可复现）。
        var marching = state.Armies
            .Where(a => a.MarchTargetCityId >= 0)
            .OrderBy(a => a.Id)
            .ToList();

        foreach (var army in marching)
        {
            if (army.Soldiers <= 0)
            {
                // 部队已在行军中溃散（防御分支：正常流程到不了这里）。
                AddMarchClear(deltas, army);
                state.EventLog.Add(new EventRecord
                {
                    Year = state.Year,
                    Month = state.Month,
                    Category = EventCategory.Military,
                    EventKey = EventKeys.Dispatch,
                    Message = "一支行军部队已经溃散，行军终止。",
                    FactionId = army.OwnerFactionId,
                });
                continue;
            }

            int monthsPerHop = Math.Max(1, PlaceholderNumbers.MarchMonthsPerHop);
            int currentTotal = TimeUtil.TotalMonths(state.Year, state.Month);
            int arriveTotal = TimeUtil.TotalMonths(army.MarchArriveYear, army.MarchArriveMonth);

            var path = army.MarchPathCityIds;
            int hopsTotal = path.Count >= 2 ? path.Count - 1 : 0;

            if (currentTotal < arriveTotal && hopsTotal > 0)
            {
                // ---- 中途推进：位置前进一跳 ----
                // 到达月 = 开拔月 + 跳数 × 每跳月数，据此反推开拔月与已走跳数。
                int dispatchTotal = arriveTotal - hopsTotal * monthsPerHop;
                int monthsElapsed = Math.Max(0, currentTotal - dispatchTotal);
                int hopsAdvanced = monthsElapsed / monthsPerHop;
                int nextIndex = Math.Clamp(hopsAdvanced, 1, path.Count - 1);

                int nextCityId = path[nextIndex];
                if (nextCityId != army.PositionCityId)
                {
                    deltas.Add(new StateDelta
                    {
                        TargetType = DeltaTarget.Army,
                        TargetId = army.Id,
                        Field = DeltaField.ArmyPositionCityId,
                        OldValue = army.PositionCityId,
                        NewValue = nextCityId,
                    });
                }

                continue;
            }

            // ---- 到达（currentTotal ≥ arriveTotal；路径损坏时也走这里兜底）----
            ResolveArrival(state, config, army, path, deltas);
        }

        return deltas;
    }

    /// <summary>
    /// 行军到达结算：目标城易主则友好进驻/合兵，敌城则触发攻城战。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="config">数值配置。只读。</param>
    /// <param name="army">到达的部队。只读。</param>
    /// <param name="path">该部队的行军路径快照。只读。</param>
    /// <param name="deltas">累积的 Delta 列表。</param>
    private static void ResolveArrival(
        WorldState state,
        BalanceConfig config,
        Army army,
        List<int> path,
        List<StateDelta> deltas)
    {
        var target = state.GetCity(army.MarchTargetCityId);
        if (target == null)
        {
            // 防御分支：城池表静态，正常流程目标城永远存在。
            AddMarchClear(deltas, army);
            state.EventLog.Add(new EventRecord
            {
                Year = state.Year,
                Month = state.Month,
                Category = EventCategory.Military,
                EventKey = EventKeys.Dispatch,
                Message = "行军目标城已不存在，行军终止。",
                FactionId = army.OwnerFactionId,
            });
            return;
        }

        var faction = state.GetFaction(army.OwnerFactionId);
        string factionName = faction?.Name ?? "某势力";

        if (target.OwnerFactionId == army.OwnerFactionId)
        {
            // ---- 友好抵达：进驻或合兵（与相邻调兵同口径）----
            var resident = state.GetArmiesAtCity(target.Id)
                .Where(a => a.Id != army.Id
                    && a.OwnerFactionId == army.OwnerFactionId
                    && a.Soldiers > 0)
                .OrderBy(a => a.Id)
                .FirstOrDefault();

            if (resident != null)
            {
                int merged = resident.Soldiers + army.Soldiers;
                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.Army,
                    TargetId = resident.Id,
                    Field = DeltaField.ArmySoldiers,
                    OldValue = resident.Soldiers,
                    NewValue = merged,
                });

                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.Army,
                    TargetId = army.Id,
                    Field = DeltaField.ArmySoldiers,
                    OldValue = army.Soldiers,
                    NewValue = 0,
                });

                state.EventLog.Add(new EventRecord
                {
                    Year = state.Year,
                    Month = state.Month,
                    Category = EventCategory.Military,
                    EventKey = EventKeys.Dispatch,
                    Message = $"{factionName}远征军抵达{target.Name}，与驻军合兵，共 {merged} 人。",
                    FactionId = army.OwnerFactionId,
                });
            }
            else
            {
                state.EventLog.Add(new EventRecord
                {
                    Year = state.Year,
                    Month = state.Month,
                    Category = EventCategory.Military,
                    EventKey = EventKeys.Dispatch,
                    Message = $"{factionName}远征军抵达{target.Name}，就地驻防。",
                    FactionId = army.OwnerFactionId,
                });
            }

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Army,
                TargetId = army.Id,
                Field = DeltaField.ArmyPositionCityId,
                OldValue = army.PositionCityId,
                NewValue = target.Id,
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

            AddMarchClear(deltas, army);
            return;
        }

        // ---- 攻城：到达即触发与相邻出兵共用的攻城结算 ----
        var retreatCity = FindRetreatCity(state, army, path);

        var battle = SiegeSettlement.ResolveSiegeBattle(
            state,
            config,
            army,
            army.Soldiers,
            target,
            retreatCity,
            army.OwnerFactionId,
            state.GetEffectiveCommand(army.Id),
            personalCommand: state.Player.IsPersonallyCommanding
                            && state.Player.CommandingArmyId == army.Id);

        deltas.AddRange(battle.Deltas);
        state.EventLog.AddRange(battle.Events);
        AddMarchClear(deltas, army);
    }

    /// <summary>
    /// 击退时的撤退目标：沿来路找最近的己方城池；找不到就退回上一跳城。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="army">部队。只读。</param>
    /// <param name="path">行军路径快照。只读。</param>
    /// <returns>撤退目标城。</returns>
    /// <remarks>
    /// 路径快照含出发城（path[0]），而出发城在开拔时必为己方城，
    /// 因此「来路最近的己方城」一定存在；退而求其次的上一跳城也一定在图中。
    /// </remarks>
    private static City FindRetreatCity(WorldState state, Army army, List<int> path)
    {
        for (int i = path.Count - 2; i >= 0; i--)
        {
            var city = state.GetCity(path[i]);
            if (city != null && city.OwnerFactionId == army.OwnerFactionId)
            {
                return city;
            }
        }

        if (path.Count >= 2)
        {
            var previous = state.GetCity(path[path.Count - 2]);
            if (previous != null)
            {
                return previous;
            }
        }

        var here = state.GetCity(army.PositionCityId);
        return here ?? throw new InvalidOperationException(
            $"行军部队 {army.Id} 无法确定撤退目标（PositionCityId={army.PositionCityId} 不在图中）。");
    }

    /// <summary>
    /// 清空部队的全部行军字段（到达/终止后部队回到常规状态）。
    /// </summary>
    /// <param name="deltas">累积的 Delta 列表。</param>
    /// <param name="army">部队。只读。</param>
    private static void AddMarchClear(List<StateDelta> deltas, Army army)
    {
        deltas.Add(new StateDelta
        {
            TargetType = DeltaTarget.Army,
            TargetId = army.Id,
            Field = DeltaField.ArmyMarchTargetCityId,
            OldValue = army.MarchTargetCityId,
            NewValue = -1,
        });

        deltas.Add(new StateDelta
        {
            TargetType = DeltaTarget.Army,
            TargetId = army.Id,
            Field = DeltaField.ArmyMarchArriveYear,
            OldValue = army.MarchArriveYear,
            NewValue = 0,
        });

        deltas.Add(new StateDelta
        {
            TargetType = DeltaTarget.Army,
            TargetId = army.Id,
            Field = DeltaField.ArmyMarchArriveMonth,
            OldValue = army.MarchArriveMonth,
            NewValue = 0,
        });

        // NewValue < 0 = 清空路径快照（StateApplier 专用分支）。
        deltas.Add(new StateDelta
        {
            TargetType = DeltaTarget.Army,
            TargetId = army.Id,
            Field = DeltaField.ArmyMarchPathCityIds,
            OldValue = 0,
            NewValue = -1,
        });
    }
}
