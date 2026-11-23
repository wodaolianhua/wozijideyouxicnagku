// ============================================================================
//  建筑升级推进
// ============================================================================
//  【职责】每月月初把「还剩几个月」减一，减到 0 时把对应建筑等级 +1。
//  【为什么要单独一个月初步骤】升级必须跨月完成，玩家下令当月不能立刻见效。
//  这是「先投资、后收获」的节奏来源；如果当月生效，升级就变成无风险的即时加成。
//
//  【纯函数】只读 state，产出 Delta，由流水线统一写回。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Systems;

/// <summary>
/// 建筑升级的月初推进。
/// </summary>
/// <remarks>
/// 建筑类型编码与 <see cref="Contracts.ActionType.Build"/> 的 SecondaryTargetId 一致：
/// 0 农田（FarmLevel）、1 兵营（BarracksLevel）、2 城墙（FortifyLevel）、3 市集（MarketLevel）。
/// </remarks>
public static class BuildingSystem
{
    /// <summary>农田。</summary>
    public const int Farm = 0;

    /// <summary>兵营。</summary>
    public const int Barracks = 1;

    /// <summary>城墙。</summary>
    public const int Fortify = 2;

    /// <summary>市集。</summary>
    public const int Market = 3;

    /// <summary>无升级工程。</summary>
    public const int None = -1;

    /// <summary>建筑等级上限。</summary>
    public const int MaxLevel = 5;

    /// <summary>
    /// 推进所有城池的升级进度。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>状态增量列表；完工的城池会同时产出「等级 +1」与「工程清空」两条 Delta。</returns>
    /// <remarks>
    /// 【顺序】本方法必须在产出结算之前调用。
    /// 这样「本月完工」的城本月就吃到新等级，玩家按完[下个月]立刻看见数字变。
    /// </remarks>
    public static List<StateDelta> ResolveAdvance(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var deltas = new List<StateDelta>();

        foreach (var city in state.Cities)
        {
            if (city.UpgradeMonthsRemaining <= 0)
            {
                continue;
            }

            int remaining = city.UpgradeMonthsRemaining - 1;

            if (remaining > 0)
            {
                deltas.Add(new StateDelta
                {
                    TargetType = DeltaTarget.City,
                    TargetId = city.Id,
                    Field = DeltaField.CityUpgradeMonthsRemaining,
                    OldValue = city.UpgradeMonthsRemaining,
                    NewValue = remaining,
                });
                continue;
            }

            // 完工：等级 +1，工程清空。
            int building = city.UpgradingBuilding;

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.City,
                TargetId = city.Id,
                Field = DeltaField.CityUpgradeMonthsRemaining,
                OldValue = city.UpgradeMonthsRemaining,
                NewValue = 0,
            });

            deltas.Add(new StateDelta
            {
                TargetType = DeltaTarget.City,
                TargetId = city.Id,
                Field = DeltaField.CityUpgradingBuilding,
                OldValue = building,
                NewValue = None,
            });

            switch (building)
            {
                case Farm:
                    deltas.Add(LevelDelta(city.Id, DeltaField.CityFarmLevel, city.FarmLevel));
                    break;
                case Barracks:
                    deltas.Add(LevelDelta(city.Id, DeltaField.CityBarracksLevel, city.BarracksLevel));
                    break;
                case Fortify:
                    deltas.Add(LevelDelta(city.Id, DeltaField.CityFortifyLevel, city.FortifyLevel));
                    break;
                case Market:
                    deltas.Add(LevelDelta(city.Id, DeltaField.CityMarketLevel, city.MarketLevel));
                    break;
                default:
                    // 数据异常：有剩余月数却没有升级类型。清空工程，不加等级。
                    break;
            }
        }

        return deltas;
    }

    /// <summary>
    /// 取某城某建筑的当前等级。
    /// </summary>
    /// <param name="city">城池。只读。</param>
    /// <param name="building">建筑类型编码 0-3。</param>
    /// <returns>当前等级；非法类型返回 0。</returns>
    public static int GetLevel(City city, int building)
    {
        ArgumentNullException.ThrowIfNull(city);

        return building switch
        {
            Farm => city.FarmLevel,
            Barracks => city.BarracksLevel,
            Fortify => city.FortifyLevel,
            Market => city.MarketLevel,
            _ => 0,
        };
    }

    private static StateDelta LevelDelta(int cityId, string field, int oldLevel) => new()
    {
        TargetType = DeltaTarget.City,
        TargetId = cityId,
        Field = field,
        OldValue = oldLevel,
        NewValue = oldLevel + 1,
    };
}
