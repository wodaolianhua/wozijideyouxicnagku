// ============================================================================
//  兵力上限 —— 让「集中兵力」成为唯一能破城的打法
// ============================================================================
//  【为什么单独成文件】上限既被「兵源产出」（EconomySystem）用到，又被
//  「兵源分发」（TurnPipeline 第 8 步）用到。放在任何一边都会让另一边反向依赖。
//
//  【纯函数】本类全部方法只读 state，不产生 Delta，不做任何写操作。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Systems;

/// <summary>
/// 兵力上限计算。
/// </summary>
/// <remarks>
/// 【上限口径】
/// · 单支部队：上限 = 所在城人口 × <see cref="PlaceholderNumbers.SoldierCapPerPopulation"/>。
///   行进中（所在城不是自己的城）的部队不设上限——它带的是已经征到的兵，不会凭空变多。
/// · 势力兵源池：上限 = 所辖各城上限之和。池满即不再产出，溢出无处安放。
///
/// 【为什么按人口而不是固定值】固定值会让小城和大城一样能屯兵，
/// 「打大城」失去收益。按人口则吞并人口大城立刻扩军，扩张有回报。
/// </remarks>
public static class ArmyCapacity
{
    /// <summary>单城可容纳的驻军上限。</summary>
    /// <param name="city">城池。只读。</param>
    /// <returns>兵力上限。人口非正时返回 0。</returns>
    public static int GetCityCap(City city)
    {
        ArgumentNullException.ThrowIfNull(city);

        if (city.Population <= 0)
        {
            return 0;
        }

        return city.Population * PlaceholderNumbers.SoldierCapPerPopulation;
    }

    /// <summary>某支部队的兵力上限。驻在自己城里才有上限，行军/围城途中不设限。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="army">部队。只读。</param>
    /// <returns>上限；无上限时返回 <see cref="int.MaxValue"/>。</returns>
    public static int GetArmyCap(WorldState state, Army army)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(army);

        var city = state.GetCity(army.PositionCityId);
        if (city == null || city.OwnerFactionId != army.OwnerFactionId)
        {
            return int.MaxValue;
        }

        return GetCityCap(city);
    }

    /// <summary>势力兵源池的上限（所辖各城上限之和）。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <returns>上限；无城时为 0。</returns>
    public static int GetFactionPoolCap(WorldState state, int factionId)
    {
        ArgumentNullException.ThrowIfNull(state);

        long total = 0;
        foreach (var city in state.Cities)
        {
            if (city.OwnerFactionId == factionId)
            {
                total += GetCityCap(city);
            }
        }

        return (int)Math.Min(total, int.MaxValue);
    }
}
