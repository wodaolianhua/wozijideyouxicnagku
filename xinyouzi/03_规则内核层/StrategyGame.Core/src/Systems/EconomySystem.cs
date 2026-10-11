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
    ///
    /// 【刻度铁律 v1.2】所有比率一律<b>万分比整数</b>（10000 = 100%）。
    /// 早期实现把粮产 / 兵源除以 100（百分数），契约已统一为万分比，
    /// 若不改，用 800 的系数除以 100 会算出 8 倍粮产。
    ///
    /// 【整除截断】所有除法对非负操作数执行，即<b>统一向下取整</b>。
    /// 浮点取整在不同语言里结果可能不同，这是跨语言协作的隐患。
    /// </remarks>
    public sealed class EconomySystem
    {
    /// <summary>
    /// 比率刻度基数。契约 v1.2：所有比率类字段为万分比，10000 = 100%。
    /// </summary>
    public const int RatioBase = 10_000;

    /// <summary>
    /// 稳定度刻度基数。稳定度本身是 0-100 的量纲，与万分比不同，不可混用。
    /// </summary>
    /// <remarks>
    /// 兵源公式 <c>人口 × SoldierPerPopPerMonth × (Stability / 100)</c> 中，
    /// 前一项是万分比、后一项是百分数。两个刻度必须分开，否则兵源会差100 倍。
    /// </remarks>
    public const int StabilityBase = 100;

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
    /// 【公式】<c>grain = 人口 × GrainPerPopPerMonth / 10000</c>，再乘人才修正。
    /// 3000 人口、系数 800 → 240 粮/月。
    ///
    /// 【重要设计转向】<b>粮不是真正的瓶颈，兵源才是。</b>
    /// 3000 人口城每月产粮 240，可养 1.2 万兵（240 ÷ 0.02）——相对宽松；
    /// 而同城每月只产 90 兵源，攒5000 兵要 4 年。
    /// 因此本系统<b>不</b>采用「粮不够就崩」的模型：
    /// 压迫感来自「人不够」与「打下地也守不住」。
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
            int grain = (int)((long)totalPopulation * _config.GrainPerPopPerMonth / RatioBase);

            int modifier = CalculateTalentModifier(state, faction.Id);

            // 人才修正以「百分点」表达，按万分比缩放。
            long adjusted = (long)grain * (RatioBase + modifier) / RatioBase;

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
    /// 第 3 步：势力民心衰减。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>状态增量列表。</returns>
    /// <remarks>
    /// 【命名警告】<c>Resources.Morale</c> 是<b>势力民心</b>（全势力一个值），
    /// 影响 Stability 与兵源产出；<c>Army.Morale</c> 是<b>部队士气</b>（每部队一个值），
    /// 影响战损倍率与哗变。两者<b>同名但不是一回事</b>，本方法只处理民心。
    /// 部队士气由 <see cref="MoraleSystem"/> 负责。
    ///
    /// 【重要】民心的月度变化由 <c>StabilityDecayPerMonth</c> 驱动，
    /// <b>不</b>使用 <c>MoraleDecayPerMonth</c>——后者是<b>部队士气</b>的
    /// 自然恢复量（4_数值框架.md 5.2），作用对象是 Army.Morale。
    /// </remarks>
    public List<StateDelta> ResolveMoraleAndStability(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var deltas = new List<StateDelta>();

        foreach (var faction in state.Factions)
        {
            // 势力民心：稳定度不足时额外下滑，民心与稳定度互为因果。
            int morale = faction.Resources.Morale;
            int stability = faction.Resources.Stability;

            int newStability = Math.Max(0, stability - _config.StabilityDecayPerMonth);

            // 稳定度低于三成 → 民心额外流失，形成「失稳则离心」的负反馈。
            int moraleDelta = newStability >= 30 ? 0 : -PlaceholderNumbers.UnstableMoraleLoss;

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Faction,
                TargetId = faction.Id,
                Field = DeltaField.FactionMorale,
                OldValue = faction.Resources.Morale,
                NewValue = Math.Max(0, morale + moraleDelta),
            });

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Faction,
                TargetId = faction.Id,
                Field = DeltaField.FactionStability,
                OldValue = faction.Resources.Stability,
                NewValue = newStability,
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
    /// <b>公式（3_战斗系统设计.md 第四节）：</b>
    /// <code>
    /// 本月兵源产出 = 人口 × (SoldierPerPopPerMonth / 10000) × (Stability / 100)
    /// </code>
    /// 3000 人口、系数 5000、Stability 60 → 3000 × 0.5 × 0.6 = <b>90 兵/月</b>。
    ///
    /// 【这是本项目真正的瓶颈】粮产 3000 人口城月产 240（可养 1.2 万兵），
    /// 而兵源只有 90/月，攒到 5000 兵需要约 4 年。
    /// 所以 <c>Stability</c> 的重要性高于粮储——它直接乘在兵源产出上。
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
                / RatioBase;

            // 稳定度越低，产出越少。注意这里除的是 StabilityBase(100)，
            // 不是 RatioBase——稳定度本身是 0-100 量纲。
            int stability = Math.Max(0, faction.Resources.Stability);
            long soldiers = baseSoldiers * stability / StabilityBase;

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
    /// 第 5 步的一部分：驻军每月耗粮。耗粮 = 兵力 × (ArmyMonthlyGrainPerSoldier / 10000)。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>状态增量列表。</returns>
    /// <remarks>
    /// 【刻度】v1.2 起 <c>ArmyMonthlyGrainPerSoldier</c> 由 /1000 口径统一为万分比，
    /// 基准值由 20 变为 200（均为每人每月 0.02 粮）。
    /// 若沿用旧除数 1000，300 人的部队耗粮会变成 600 倍，粮耗直接压垮经济。
    ///
    /// 【粮耗不是主要瓶颈】5000 人月耗 100 粮，而一座 3000 人口城月产 240 粮——
    /// 相对宽松。真正的瓶颈是兵源（90/月）。所以本方法<b>不做</b>
    /// 「粮不够就崩」的模型：粮不足时最多扣到 0，不触发连锁崩溃。
    /// </remarks>
    public List<StateDelta> ResolveArmyGrainUpkeep(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var deltas = new List<StateDelta>();

        foreach (var faction in state.Factions)
        {
            int totalSoldiers = 0;
            foreach (var army in state.Armies)
            {
                if (army.OwnerFactionId == faction.Id)
                {
                    totalSoldiers += army.Soldiers;
                }
            }

            if (totalSoldiers <= 0)
            {
                continue;
            }

            long upkeep = (long)totalSoldiers * _config.ArmyMonthlyGrainPerSoldier / RatioBase;

            if (upkeep <= 0)
            {
                continue;
            }

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.Faction,
                TargetId = faction.Id,
                Field = DeltaField.FactionGrain,
                OldValue = faction.Resources.Grain,
                // 粮不足时最多扣到 0，不产生负数。
                NewValue = (int)Math.Max(0, faction.Resources.Grain - upkeep),
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
