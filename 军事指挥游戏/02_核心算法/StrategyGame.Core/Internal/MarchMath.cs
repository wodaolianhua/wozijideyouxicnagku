using StrategyGame.Contracts;

namespace StrategyGame.Core.Internal;

/// <summary>一段线性运动轨迹（单步内），T0/T1 为步内归一化时间 0..1。</summary>
internal readonly record struct TrackPiece(
    double T0,
    double T1,
    double X0,
    double Y0,
    double X1,
    double Y1)
{
    public (double X, double Y) At(double t)
    {
        var span = T1 - T0;
        if (span <= 1e-12)
        {
            return (X1, Y1);
        }

        var k = Math.Clamp((t - T0) / span, 0.0, 1.0);
        return (X0 + (X1 - X0) * k, Y0 + (Y1 - Y0) * k);
    }

    public bool IsMoving => Math.Abs(X1 - X0) > 1e-9 || Math.Abs(Y1 - Y0) > 1e-9;
}

/// <summary>地图查询 / 位置插值 / 单步轨迹求交等共用的纯几何工具。</summary>
internal static class MarchMath
{
    public const double Epsilon = 1e-9;

    public static bool SameId(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

    public static MapNode? FindNode(WorldMap? map, string? id)
    {
        if (map is null || string.IsNullOrEmpty(id))
        {
            return null;
        }

        foreach (var node in map.Nodes)
        {
            if (SameId(node.Id, id))
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>查无向边：重复边取 Cost 最小的一条，保证确定性。</summary>
    public static MapEdge? FindEdge(WorldMap? map, string from, string to)
    {
        if (map is null)
        {
            return null;
        }

        MapEdge? best = null;
        foreach (var edge in map.Edges)
        {
            var forward = SameId(edge.From, from) && SameId(edge.To, to);
            var backward = SameId(edge.From, to) && SameId(edge.To, from);
            if (!forward && !backward)
            {
                continue;
            }

            if (!double.IsFinite(edge.Cost) || edge.Cost < 0)
            {
                continue;
            }

            if (best is null || edge.Cost < best.Cost)
            {
                best = edge;
            }
        }

        return best;
    }

    /// <summary>穿越一条边所需的时间步数（含边类型与目标节点地形修正，按步兵基准速度 1.0）。</summary>
    public static double TraversalCost(WorldMap map, string from, string to)
    {
        var edge = FindEdge(map, from, to);
        if (edge is null)
        {
            return double.PositiveInfinity;
        }

        var toNode = FindNode(map, to);
        var terrain = toNode?.Terrain ?? TerrainType.Plain;
        return Math.Max(
            0.01,
            edge.Cost * MarchRules.EdgeCostFactor(edge.Type) * MarchRules.TerrainCostFactor(terrain));
    }

    public static (double X, double Y) NodePosition(WorldMap map, string? nodeId)
    {
        var node = FindNode(map, nodeId);
        return node is null ? (0, 0) : (node.X, node.Y);
    }

    /// <summary>部队当前世界坐标：在边上的按 EdgeProgress 线性插值。</summary>
    public static (double X, double Y) PositionOf(WorldMap map, Army army)
    {
        var path = army.PlannedPath;
        if (path is null || path.Count == 0)
        {
            return NodePosition(map, army.NodeId);
        }

        var index = Math.Clamp(army.PathIndex, 0, path.Count - 1);
        if (index + 1 >= path.Count)
        {
            return NodePosition(map, path[index]);
        }

        var progress = Math.Clamp(army.EdgeProgress, 0.0, 1.0);
        var (x0, y0) = NodePosition(map, path[index]);
        var (x1, y1) = NodePosition(map, path[index + 1]);
        return (x0 + (x1 - x0) * progress, y0 + (y1 - y0) * progress);
    }

    /// <summary>离给定坐标最近的节点 Id；距离相同按 Id 字典序取小，保证确定性。</summary>
    public static string NearestNodeId(WorldMap map, double x, double y)
    {
        string bestId = "";
        var bestDistance = double.PositiveInfinity;

        foreach (var node in map.Nodes)
        {
            var dx = node.X - x;
            var dy = node.Y - y;
            var distance = dx * dx + dy * dy;
            var better = distance < bestDistance - 1e-12;
            var tie = Math.Abs(distance - bestDistance) <= 1e-12
                      && (bestId.Length == 0 || string.CompareOrdinal(node.Id, bestId) < 0);
            if (better || tie)
            {
                bestDistance = distance;
                bestId = node.Id;
            }
        }

        return bestId;
    }

    /// <summary>
    /// 单步内两组轨迹是否进入接战距离。返回首次接触的步内时间与接触点坐标。
    /// 按重叠时间段做“最近接近点”计算，因此迎面擦过（步末已错开）也能抓到。
    /// </summary>
    public static bool TryMeet(
        List<TrackPiece>? piecesA,
        List<TrackPiece>? piecesB,
        double threshold,
        out double time,
        out double x,
        out double y)
    {
        time = 0;
        x = 0;
        y = 0;
        if (piecesA is null || piecesB is null)
        {
            return false;
        }

        var found = false;
        var bestTime = double.PositiveInfinity;
        var bestX = 0.0;
        var bestY = 0.0;
        var tolerance = Math.Max(threshold, 0.0);
        var toleranceSquared = tolerance * tolerance + 1e-12;

        foreach (var pa in piecesA)
        {
            foreach (var pb in piecesB)
            {
                var t0 = Math.Max(pa.T0, pb.T0);
                var t1 = Math.Min(pa.T1, pb.T1);
                if (t1 < t0)
                {
                    continue;
                }

                var (ax0, ay0) = pa.At(t0);
                var (bx0, by0) = pb.At(t0);
                var (ax1, ay1) = pa.At(t1);
                var (bx1, by1) = pb.At(t1);

                var rx0 = ax0 - bx0;
                var ry0 = ay0 - by0;
                var rx1 = ax1 - bx1;
                var ry1 = ay1 - by1;
                var drx = rx1 - rx0;
                var dry = ry1 - ry0;
                var denominator = drx * drx + dry * dry;

                var s = denominator > 1e-18
                    ? -(rx0 * drx + ry0 * dry) / denominator
                    : 0.0;
                s = Math.Clamp(s, 0.0, 1.0);

                var dx = rx0 + drx * s;
                var dy = ry0 + dry * s;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared > toleranceSquared)
                {
                    continue;
                }

                var contactTime = t0 + (t1 - t0) * s;
                if (!found || contactTime < bestTime - 1e-12)
                {
                    var (mxA, myA) = pa.At(contactTime);
                    var (mxB, myB) = pb.At(contactTime);
                    found = true;
                    bestTime = contactTime;
                    bestX = (mxA + mxB) / 2.0;
                    bestY = (myA + myB) / 2.0;
                }
            }
        }

        if (found)
        {
            time = bestTime;
            x = bestX;
            y = bestY;
        }

        return found;
    }

    public static bool IsMoving(List<TrackPiece>? pieces)
    {
        if (pieces is null)
        {
            return false;
        }

        foreach (var piece in pieces)
        {
            if (piece.IsMoving)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>部队是否原地不动（没有剩余路段）。</summary>
    public static bool IsIdle(Army army)
    {
        var path = army.PlannedPath;
        return path is null || path.Count == 0 || army.PathIndex + 1 >= path.Count;
    }

    public static Army? FindArmy(WorldState state, string? armyId)
    {
        if (string.IsNullOrEmpty(armyId))
        {
            return null;
        }

        foreach (var army in state.Armies)
        {
            if (SameId(army.Id, armyId))
            {
                return army;
            }
        }

        return null;
    }
}

/// <summary>遭遇事件构造（负责统一算伏击标记）。</summary>
internal static class EncounterRules
{
    public static EncounterEvent Create(
        WorldMap map,
        int time,
        Army a,
        Army b,
        double x,
        double y,
        bool aMoving,
        bool bMoving)
    {
        var nodeId = MarchMath.NearestNodeId(map, x, y);
        var encounter = new EncounterEvent
        {
            Time = time,
            NodeId = nodeId,
            ArmyIds = new List<string> { a.Id, b.Id },
        };

        // 伏击：山地/森林提供隐蔽，且一方原地驻扎、另一方在动 → 驻扎方伏击
        var node = MarchMath.FindNode(map, nodeId);
        var hasCover = node is not null
                       && (node.Terrain == TerrainType.Mountain || node.Terrain == TerrainType.Forest);
        if (hasCover && aMoving != bMoving)
        {
            encounter.IsAmbush = true;
            encounter.AmbusherId = aMoving ? b.Id : a.Id;
        }

        return encounter;
    }
}

/// <summary>状态深拷贝：保证 Advance/FastForward 不修改入参 state。</summary>
internal static class StateCloner
{
    public static WorldState Clone(WorldState state)
    {
        var map = new WorldMap
        {
            Nodes = state.Map?.Nodes is null
                ? new List<MapNode>()
                : state.Map.Nodes.Select(n => new MapNode
                {
                    Id = n.Id,
                    Name = n.Name,
                    Terrain = n.Terrain,
                    X = n.X,
                    Y = n.Y,
                }).ToList(),
            Edges = state.Map?.Edges is null
                ? new List<MapEdge>()
                : state.Map.Edges.Select(e => new MapEdge
                {
                    From = e.From,
                    To = e.To,
                    Type = e.Type,
                    Cost = e.Cost,
                }).ToList(),
        };

        return new WorldState
        {
            Time = state.Time,
            Map = map,
            Armies = state.Armies is null ? new List<Army>() : state.Armies.Select(CloneArmy).ToList(),
        };
    }

    public static Army CloneArmy(Army army) => new()
    {
        Id = army.Id,
        Name = army.Name,
        Faction = army.Faction,
        UnitType = army.UnitType,
        Strength = army.Strength,
        NodeId = army.NodeId,
        EdgeProgress = army.EdgeProgress,
        PlannedPath = army.PlannedPath is null ? new List<string>() : new List<string>(army.PlannedPath),
        PathIndex = army.PathIndex,
    };
}
