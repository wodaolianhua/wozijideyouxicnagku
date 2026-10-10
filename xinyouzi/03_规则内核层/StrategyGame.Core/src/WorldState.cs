// ============================================================================
//  WorldState 的查询扩展
// ============================================================================
//  【不重新声明任何契约类型】
//  WorldState 及其 GetFaction/GetCity/GetOfficer 均定义在
//  StrategyGame.Contracts。本文件只提供规则层内部高频使用的只读查询方法。
//
//  【纯读原则】本文件所有方法都是只读查询，绝不修改传入的 state。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core;

/// <summary>
/// <see cref="WorldState"/> 的只读查询扩展方法。
/// </summary>
/// <remarks>
/// 【为什么不放进契约】契约是跨包冻结的真相来源，任何改动都要走变更流程。
/// 这些查询只服务于规则层内部，放在本包可避免无谓的契约变更。
/// </remarks>
public static class WorldStateExtensions
{
    /// <summary>取玩家势力（<see cref="Faction.IsPlayer"/> 为 true）。没有则返回 null。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>玩家势力；不存在时返回 null。</returns>
    public static Faction? GetPlayerFaction(this WorldState state)
        => state.Factions.Find(f => f.IsPlayer);

    /// <summary>取某势力拥有的全部城池 Id。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <returns>该势力拥有的城池 Id 列表；势力不存在时返回空列表。</returns>
    public static List<int> GetCityIdsOfFaction(this WorldState state, int factionId)
    {
        var result = new List<int>();
        foreach (var city in state.Cities)
        {
            if (city.OwnerFactionId == factionId)
            {
                result.Add(city.Id);
            }
        }

        return result;
    }

    /// <summary>取驻扎在某城池的部队列表。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="cityId">城池 Id。</param>
    /// <returns>该城池内的部队；城池不存在时返回空列表。</returns>
    public static List<Army> GetArmiesAtCity(this WorldState state, int cityId)
    {
        var result = new List<Army>();
        foreach (var army in state.Armies)
        {
            if (army.PositionCityId == cityId)
            {
                result.Add(army);
            }
        }

        return result;
    }

    /// <summary>取某势力拥有的全部人才。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <returns>该势力的人才列表；势力不存在时返回空列表。</returns>
    public static List<Officer> GetOfficersOfFaction(this WorldState state, int factionId)
    {
        var result = new List<Officer>();
        foreach (var officer in state.Officers)
        {
            if (officer.OwnerFactionId == factionId)
            {
                result.Add(officer);
            }
        }

        return result;
    }

    /// <summary>取势力总人口。只读，不产生副作用。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <returns>该势力所有城池人口之和。</returns>
    public static int GetTotalPopulation(this WorldState state, int factionId)
    {
        int total = 0;
        foreach (var city in state.Cities)
        {
            if (city.OwnerFactionId == factionId)
            {
                total += city.Population;
            }
        }

        return total;
    }

    /// <summary>
    /// 解析 <see cref="Order"/> 的执行者所属势力 Id。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="order">指令。</param>
    /// <returns>执行者所属势力 Id；无法解析时返回 -1。</returns>
    /// <remarks>
    /// Player 恒解析为其所属玩家势力；Officer 解析为其 OwnerFactionId；
    /// AiFaction 恒为 ActorId 本身。这是「AI 与玩家同路」的关键：
    /// 无论哪种来源，最终都归约到同一个 FactionId，资源校验逻辑得以复用。
    /// </remarks>
    public static int ResolveActorFactionId(this WorldState state, Order order)
    {
        switch (order.ActorType)
        {
            case ActorType.Player:
            {
                var playerFaction = state.GetPlayerFaction();
                return playerFaction?.Id ?? -1;
            }

            case ActorType.Officer:
            {
                var officer = state.GetOfficer(order.ActorId);
                if (officer == null || officer.OwnerFactionId < 0)
                {
                    return -1;
                }

                return officer.OwnerFactionId;
            }

            case ActorType.AiFaction:
            {
                return state.GetFaction(order.ActorId)?.Id ?? -1;
            }

            default:
                return -1;
        }
    }
}
