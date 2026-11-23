// ============================================================================
//  地图寻路 —— 长途行军的路径服务
// ============================================================================
//  【为什么需要】第 8 轮取消玩家出兵「只能打相邻城」的硬拒绝后，
//  行军时间与补给消耗都要按「出发城到目标城的路径距离」计算，
//  因此需要一个基于 cities.json 相邻关系图的最短路径服务。
//
//  【纯函数】只读 state，不修改任何字段，不消耗随机数。
//  【确定性】BFS 天然按层推进；同层邻居按城 Id 升序扩展，
//  保证相同图必然得到同一条最短路径（回放/存档依赖）。
//  【扩展点（第 8 轮备忘）】途中拦截首版不做。若未来做「途经城池遭遇战」，
//  只需在 MarchSystem 按本服务产出的路径逐跳推进时挂接拦截判定，
//  不需要改本服务的接口。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Pathfinding;

/// <summary>
/// 基于城池相邻关系图的最短路径服务。
/// </summary>
/// <remarks>
/// 城池相邻关系来自 02 的 cities.json，运行时不变（城池图是静态的，
/// 变化的只是城池归属），因此 BFS 结果在整局内稳定。
/// </remarks>
public static class MapPathfinder
{
    /// <summary>
    /// 一次寻路的结果。
    /// </summary>
    /// <param name="Path">完整路径（含出发城与目标城），找不到路径时为 null。</param>
    /// <param name="Hops">跳数 = 路径长度 − 1。找不到路径时为 -1。</param>
    public readonly record struct PathResult(List<int>? Path, int Hops)
    {
        /// <summary>路径是否可达（同城视为 0 跳可达）。</summary>
        public bool Found => Path != null && Path.Count >= 1;
    }

    /// <summary>
    /// 计算出发城到目标城的最短路径（BFS）。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="fromCityId">出发城 Id。</param>
    /// <param name="toCityId">目标城 Id。</param>
    /// <returns>路径结果。两城相同返回 0 跳；无连通路径返回未找到。</returns>
    /// <remarks>
    /// 【BFS 自己写，不引第三方】铁律 9。队列用 List + 下标模拟，
    /// 避免 Queue 的语义负担；同层邻居按城 Id 升序扩展保证确定性。
    /// </remarks>
    public static PathResult FindPath(WorldState state, int fromCityId, int toCityId)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (fromCityId == toCityId)
        {
            return state.GetCity(fromCityId) == null
                ? new PathResult(null, -1)
                : new PathResult(new List<int> { fromCityId }, 0);
        }

        if (state.GetCity(fromCityId) == null || state.GetCity(toCityId) == null)
        {
            return new PathResult(null, -1);
        }

        // prev[cityId] = 到达该城的前一站城 Id。BFS 完按此回溯路径。
        var prev = new Dictionary<int, int> { [fromCityId] = -1 };
        var queue = new List<int> { fromCityId };
        int head = 0;

        while (head < queue.Count)
        {
            int current = queue[head++];
            if (current == toCityId)
            {
                break;
            }

            var city = state.GetCity(current);
            if (city == null)
            {
                continue;
            }

            // 同层邻居按 Id 升序扩展：唯一的最短路径，保证确定性。
            var neighbors = new List<int>(city.AdjacentCityIds);
            neighbors.Sort();

            foreach (int next in neighbors)
            {
                if (prev.ContainsKey(next))
                {
                    continue;
                }

                prev[next] = current;
                queue.Add(next);
            }
        }

        if (!prev.ContainsKey(toCityId))
        {
            return new PathResult(null, -1);
        }

        // 回溯路径：target → ... → origin，再反转。
        var path = new List<int>();
        int cursor = toCityId;
        while (cursor >= 0)
        {
            path.Add(cursor);
            cursor = prev[cursor];
        }

        path.Reverse();
        return new PathResult(path, path.Count - 1);
    }
}
