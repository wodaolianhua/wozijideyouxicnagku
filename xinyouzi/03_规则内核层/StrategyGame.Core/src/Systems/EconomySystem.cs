// ============================================================================
//  经济 / 民心 / 基建 / 兵源 —— 回合结算的玩法逻辑
// ============================================================================
//  【为什么单独成文件】任务书禁止把玩法逻辑写进 TurnPipeline：
//  流水线只做调度，具体逻辑在 Resolver 里。本文件承载第 2-5、8 步的玩法，
//  TurnPipeline 只负责按顺序调用。
//
//  【纯函数】每个方法都是纯函数：只读 state，产出 StateDelta 列表。
//  与 Resolver 遵守同一纪律——这是无头模拟与回放的前提。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Systems;

/// <summary>
/// 月度经济与民生结算。覆盖流水线的第 2、3、4、5、8 步。
/// </summary>
/// <remarks>
/// 所有方法均为纯函数：只读 state，产出 Delta 与事件，不修改任何字段。
/// 调用方负责把 Delta 交给 <see cref="IStateApplier"/> 写回。
/// </remarks>
public sealed class EconomySystem
{
    private readonly BalanceConfig _config;

    /// <summary>创建经济系统。</summary>
    /// <param name="config">数值配置。所有系数从此读取，禁止硬编码。</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> 为 null 时抛出。</exception>
    public EconomySystem(BalanceConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// 第 2 步：经济结算。粮 = 人口 × 系数 × 人才修正。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>状态增量列表。</returns>
    /// <remarks>
    /// TODO(设计): 当前只结算粮产。银的收入公式属经济系统设计范围，
    /// 待 01 包定稿后补入，届时新增 FactionSilver 的 Delta。
    /// </remarks>
    public List<StateDelta> ResolveGrainIncome(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var deltas = new List<StateDelta>();

        foreach (var faction in state.Factions)
        {
            int totalPopulation = state.GetTotalPopulation(faction.Id);
            if (totalPopulation <= 0)
            {
                continue;
            }

            // 全部整数运算，杜绝浮点误差，保证逐字段可复现。
            int grain = (int)((long)totalPopulation
                * _config.GrainPerPopPerMonth
                / PlaceholderNumbers.PercentBase);

            int modifier = CalculateTalentModifier(state, faction.Id);

            // TODO(设计): 人才修正的具体系数待人事制度定稿后替换。
            long adjusted = (long)grain * (PlaceholderNumbers.PercentBase + modifier)
                / PlaceholderNumbers.PercentBase;

            int finalGrain = (int)Math.Min(adjusted, int.MaxValue);
            if (finalGrain <= 0)
            {
                continue;
            }

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Faction,
                TargetId = faction.Id,
                Field = DeltaField.FactionGrain,
                OldValue = faction.Resources.Grain,
                NewValue = faction.Resources.Grain + finalGrain,
            });
        }

        return deltas;
    }

    /// <summary>
    /// 第 3 步：民心与稳定度衰减。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>状态增量列表。</returns>
    /// <remarks>
    /// TODO(设计): 衰减公式待数值框架定稿。当前为固定月衰减值，
    /// 稳定度低于阈值时追加惩罚，定稿后替换为完整公式。
    /// </remarks>
    public List<StateDelta> ResolveMoraleAndStability(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var deltas = new List<StateDelta>();

        foreach (var faction in state.Factions)
        {
            int morale = faction.Resources.Morale - _config.MoraleDecayPerMonth;
            int stability = faction.Resources.Stability - _config.StabilityDecayPerMonth;

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Faction,
                TargetId = faction.Id,
                Field = DeltaField.FactionMorale,
                OldValue = faction.Resources.Morale,
                NewValue = Math.Max(0, morale),
            });

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Faction,
                TargetId = faction.Id,
                Field = DeltaField.FactionStability,
                OldValue = faction.Resources.Stability,
                NewValue = Math.Max(0, stability),
            });
        }

        return deltas;
    }

    /// <summary>
    /// 第 5 步：兵源结算。按人口 × 稳定度生成可用士兵。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>状态增量列表。</returns>
    /// <remarks>
    /// 稳定度修正 = 稳定度百分比（0-100 映射到 0-100%）。
    /// TODO(设计): 完整兵源公式待数值框架定稿。
    /// </remarks>
    public List<StateDelta> ResolveSoldierIncome(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var deltas = new List<StateDelta>();

        foreach (var faction in state.Factions)
        {
            int totalPopulation = state.GetTotalPopulation(faction.Id);
            if (totalPopulation <= 0)
            {
                continue;
            }

            long baseSoldiers = (long)totalPopulation
                * _config.SoldierPerPopPerMonth
                / PlaceholderNumbers.PercentBase;

            // 稳定度越低，产出越少；乘以 100% 基准做定点缩放。
            int stability = Math.Max(0, faction.Resources.Stability);
            long soldiers = baseSoldiers * stability / PlaceholderNumbers.PercentBase;

            int finalSoldiers = (int)Math.Min(soldiers, int.MaxValue);
            if (finalSoldiers <= 0)
            {
                continue;
            }

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Faction,
                TargetId = faction.Id,
                Field = DeltaField.FactionSoldier,
                OldValue = faction.Resources.Soldier,
                NewValue = faction.Resources.Soldier + finalSoldiers,
            });
        }

        return deltas;
    }

    /// <summary>
    /// 第 4 步：基建推进。在岗人才推动屯田度累积。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>状态增量列表。</returns>
    /// <remarks>
    /// TODO(设计): 基建累积速率待数值框架定稿。
    /// 当前为「有屯田使则每月 +1 屯田度」的占位规则。
    /// </remarks>
    public List<StateDelta> ResolveInfrastructure(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var deltas = new List<StateDelta>();

        foreach (var city in state.Cities)
        {
            bool hasFarmOverseer = false;
            foreach (var officer in state.Officers)
            {
                if (officer.PostCityId == city.Id
                    && officer.Post == PostType.FarmOverseer
                    && officer.IsAlive
                    && officer.OwnerFactionId == city.OwnerFactionId)
                {
                    hasFarmOverseer = true;
                    break;
                }
            }

            if (!hasFarmOverseer)
            {
                continue;
            }

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.City,
                TargetId = city.Id,
                Field = DeltaField.CityFarmLevel,
                OldValue = city.FarmLevel,
                NewValue = city.FarmLevel + 1,
            });
        }

        return deltas;
    }

    /// <summary>
    /// 人才到岗计数推进。PostTakeEffectMonths 决定换任后需等待多久生效。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>状态增量列表。</returns>
    public List<StateDelta> ResolvePostedOfficers(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var deltas = new List<StateDelta>();

        foreach (var officer in state.Officers)
        {
            if (officer.Post == PostType.None)
            {
                continue;
            }

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Officer,
                TargetId = officer.Id,
                Field = DeltaField.OfficerPostedSinceMonths,
                OldValue = officer.PostedSinceMonths,
                NewValue = officer.PostedSinceMonths + 1,
            });
        }

        return deltas;
    }

    /// <summary>
    /// 人才忠诚度衰减。低于阈值时可能叛离。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>状态增量列表。</returns>
    /// <remarks>
    /// TODO(设计): 叛离的具体判定与后果属人事制度设计范围，
    /// 当前仅做忠诚度衰减，叛离留给 01 包定稿。
    /// </remarks>
    public List<StateDelta> ResolveLoyaltyDecay(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var deltas = new List<StateDelta>();

        foreach (var officer in state.Officers)
        {
            if (officer.OwnerFactionId < 0 || !officer.IsAlive)
            {
                continue;
            }

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Officer,
                TargetId = officer.Id,
                Field = DeltaField.OfficerLoyalty,
                OldValue = officer.Loyalty,
                NewValue = Math.Max(0, officer.Loyalty - _config.LoyaltyDecayPerMonth),
            });
        }

        return deltas;
    }

    /// <summary>
    /// 计算势力的人才修正百分比。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <returns>修正百分点数。</returns>
    /// <remarks>
    /// TODO(设计): 待人事制度定稿后替换。当前按在岗人才数量线性给修正。
    /// </remarks>
    private static int CalculateTalentModifier(WorldState state, int factionId)
    {
        int postedCount = 0;
        foreach (var officer in state.GetOfficersOfFaction(factionId))
        {
            if (officer.Post != PostType.None)
            {
                postedCount++;
            }
        }

        return postedCount * PlaceholderNumbers.GovernanceBonusPerPostedOfficer;
    }
}
