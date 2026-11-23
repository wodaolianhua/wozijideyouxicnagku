using StrategyGame.Contracts;

namespace StrategyGame.Core;

/// <summary>
/// 路网最短路径求解：按 <see cref="MapEdge.Cost"/> 加权的 Dijkstra。
/// 边按无向处理（两个节点间可双向行军）；不可达返回空列表，不抛异常。
/// 纯函数：不修改传入地图。
/// </summary>
public sealed class PathFinder : IPathFinder
{
    public List<string> FindPath(WorldMap map, string fromNodeId, string toNodeId)
    {
        var result = new List<string>();
        if (map is null || string.IsNullOrEmpty(fromNodeId) || string.IsNullOrEmpty(toNodeId))
        {
            return result;
        }

        var nodes = new Dictionary<string, MapNode>(StringComparer.Ordinal);
        foreach (var node in map.Nodes)
        {
            if (!string.IsNullOrEmpty(node.Id))
            {
                nodes[node.Id] = node;
            }
        }

        if (!nodes.ContainsKey(fromNodeId) || !nodes.ContainsKey(toNodeId))
        {
            return result;
        }

        if (string.Equals(fromNodeId, toNodeId, StringComparison.Ordinal))
        {
            result.Add(fromNodeId);
            return result;
        }

        var adjacency = BuildAdjacency(map, nodes);
        var best = new Dictionary<string, double>(StringComparer.Ordinal);
        var previous = new Dictionary<string, string>(StringComparer.Ordinal);
        var settled = new HashSet<string>(StringComparer.Ordinal);
        var queue = new PriorityQueue<string, (double Cost, long Seq)>();
        var seq = 0L;

        best[fromNodeId] = 0;
        queue.Enqueue(fromNodeId, (0, seq++));

        while (queue.TryDequeue(out var current, out var key))
        {
            if (!settled.Add(current))
            {
                continue;
            }

            if (string.Equals(current, toNodeId, StringComparison.Ordinal))
            {
                break;
            }

            if (!adjacency.TryGetValue(current, out var neighbors))
            {
                continue;
            }

            foreach (var (to, cost) in neighbors)
            {
                if (settled.Contains(to))
                {
                    continue;
                }

                var next = key.Cost + cost;
                if (!best.TryGetValue(to, out var old) || next < old - 1e-9)
                {
                    best[to] = next;
                    previous[to] = current;
                    // 优先级里带自增序号，距离相同时出队顺序固定，保证同输入同输出
                    queue.Enqueue(to, (next, seq++));
                }
            }
        }

        if (!settled.Contains(toNodeId))
        {
            return result; // 不可达
        }

        var cursor = toNodeId;
        while (true)
        {
            result.Add(cursor);
            if (string.Equals(cursor, fromNodeId, StringComparison.Ordinal))
            {
                break;
            }

            if (!previous.TryGetValue(cursor, out var prev))
            {
                return new List<string>(); // 理论上不会发生，保底不返回半截路径
            }

            cursor = prev;
        }

        result.Reverse();
        return result;
    }

    public List<string> FindPathVia(WorldMap map, string fromNodeId, IReadOnlyList<string> waypoints)
    {
        if (map is null || string.IsNullOrEmpty(fromNodeId))
        {
            return new List<string>();
        }

        if (waypoints is null || waypoints.Count == 0)
        {
            return FindPath(map, fromNodeId, fromNodeId);
        }

        var result = new List<string>();
        var current = fromNodeId;

        foreach (var waypoint in waypoints)
        {
            var segment = FindPath(map, current, waypoint);
            if (segment.Count == 0)
            {
                return new List<string>(); // 任一段不可达，整条多点路径失败
            }

            if (result.Count == 0)
            {
                result.AddRange(segment);
            }
            else
            {
                result.AddRange(segment.Skip(1)); // 去掉衔接点，避免重复
            }

            current = waypoint;
        }

        return result;
    }

    private static Dictionary<string, List<(string To, double Cost)>> BuildAdjacency(
        WorldMap map,
        IReadOnlyDictionary<string, MapNode> nodes)
    {
        var adjacency = new Dictionary<string, List<(string To, double Cost)>>(StringComparer.Ordinal);

        foreach (var edge in map.Edges)
        {
            if (edge is null
                || string.IsNullOrEmpty(edge.From)
                || string.IsNullOrEmpty(edge.To)
                || !nodes.ContainsKey(edge.From)
                || !nodes.ContainsKey(edge.To)
                || !double.IsFinite(edge.Cost)
                || edge.Cost < 0)
            {
                continue;
            }

            Add(adjacency, edge.From, edge.To, edge.Cost);
            Add(adjacency, edge.To, edge.From, edge.Cost);
        }

        // 邻接表按目标节点 Id 排序，进一步保证遍历顺序确定
        foreach (var list in adjacency.Values)
        {
            list.Sort(static (a, b) =>
            {
                var byId = string.CompareOrdinal(a.To, b.To);
                return byId != 0 ? byId : a.Cost.CompareTo(b.Cost);
            });
        }

        return adjacency;
    }

    private static void Add(
        Dictionary<string, List<(string To, double Cost)>> adjacency,
        string from,
        string to,
        double cost)
    {
        if (!adjacency.TryGetValue(from, out var list))
        {
            list = new List<(string To, double Cost)>();
            adjacency[from] = list;
        }

        list.Add((to, cost));
    }
}
