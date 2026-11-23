using StrategyGame.Contracts;
using StrategyGame.Core.Internal;

namespace StrategyGame.Core;

/// <summary>
/// 行军推进器：按时间步沿 PlannedPath 前进，产出新状态、行军轨迹与遭遇事件。
/// 纯函数：不修改入参 state；不读系统时钟；随机数只透传 seed。
/// </summary>
public sealed class MarchSimulator : IMarchSimulator
{
    private readonly IPathFinder _pathFinder;
    private readonly ILossResolver _lossResolver;
    private readonly double _engagementDistance;

    public MarchSimulator(
        IPathFinder? pathFinder = null,
        ILossResolver? lossResolver = null,
        double engagementDistance = MarchRules.DefaultEngagementDistance)
    {
        _pathFinder = pathFinder ?? new PathFinder();
        _lossResolver = lossResolver ?? new LossResolver();
        _engagementDistance = Math.Max(0.0, engagementDistance);
    }

    public AdvanceResult Advance(WorldState state, IReadOnlyList<Order> orders, int steps, int seed)
        => Run(state, orders, Math.Max(0, steps), seed, stopOnEncounter: false);

    public AdvanceResult FastForward(WorldState state, IReadOnlyList<Order> orders, int maxSteps, int seed)
        => Run(state, orders, Math.Max(0, maxSteps), seed, stopOnEncounter: true);

    private AdvanceResult Run(
        WorldState state,
        IReadOnlyList<Order>? orders,
        int steps,
        int seed,
        bool stopOnEncounter)
    {
        ArgumentNullException.ThrowIfNull(state);

        var working = StateCloner.Clone(state);

        var trailByArmy = new Dictionary<string, MarchTrail>(StringComparer.Ordinal);
        var trailList = new List<MarchTrail>();
        foreach (var army in working.Armies)
        {
            var trail = new MarchTrail { ArmyId = army.Id };
            var (x, y) = MarchMath.PositionOf(working.Map, army);
            trail.Points.Add(new TrailPoint { X = x, Y = y, Time = working.Time });
            trailByArmy[army.Id] = trail;
            trailList.Add(trail);
        }

        var code = ResolveResultCode.Ok;
        if (orders is { Count: > 0 })
        {
            code = ApplyOrders(working, orders);
        }

        var encounters = new List<EncounterEvent>();

        for (var step = 0; step < steps; step++)
        {
            var tracks = StepArmies(working, trailByArmy);
            working.Time += 1;

            var stepEncounters = DetectStepEncounters(working, tracks, seed);
            if (stepEncounters.Count > 0)
            {
                ApplyLosses(working, stepEncounters);
                encounters.AddRange(stepEncounters);

                if (stopOnEncounter)
                {
                    break;
                }
            }

            if (AllArmiesIdle(working))
            {
                break; // 全部抵达/待命，提前结束（无意义空转不再推时间）
            }
        }

        return new AdvanceResult
        {
            State = working,
            Trails = trailList,
            Encounters = encounters,
            Code = code,
        };
    }

    /// <summary>
    /// 把本步新命令翻译成 PlannedPath / PathIndex，并处理 Hold。
    /// 路网缺口（无路/目标不存在）只返回结果码，不抛异常。
    /// </summary>
    private ResolveResultCode ApplyOrders(WorldState state, IReadOnlyList<Order> orders)
    {
        var code = ResolveResultCode.Ok;
        var hasCode = false;

        void Record(ResolveResultCode candidate)
        {
            if (!hasCode)
            {
                code = candidate;
                hasCode = true;
            }
        }

        foreach (var order in orders)
        {
            if (order is null)
            {
                continue;
            }

            var army = MarchMath.FindArmy(state, order.ArmyId);
            if (army is null)
            {
                continue; // 未知部队 Id：忽略，不影响其他部队
            }

            if (order.Kind == OrderKind.Hold)
            {
                ClearPath(army);
                continue;
            }

            var waypoints = order.Waypoints ?? new List<string>();
            if (waypoints.Count == 0)
            {
                Record(ResolveResultCode.InvalidTarget);
                continue;
            }

            var invalid = waypoints.Any(w => MarchMath.FindNode(state.Map, w) is null);
            if (invalid)
            {
                Record(ResolveResultCode.InvalidTarget);
                continue;
            }

            var start = string.IsNullOrEmpty(army.NodeId) ? waypoints[0] : army.NodeId;
            List<string> path;
            if (order.Kind == OrderKind.MoveVia)
            {
                path = _pathFinder.FindPathVia(state.Map, start, waypoints);
            }
            else
            {
                path = _pathFinder.FindPath(state.Map, start, waypoints[waypoints.Count - 1]);
            }

            if (path.Count == 0)
            {
                Record(ResolveResultCode.NoPath);
                continue;
            }

            army.PlannedPath = path;
            army.PathIndex = 0;
            army.EdgeProgress = 0;
            army.NodeId = path[0];

            if (path.Count == 1)
            {
                Record(ResolveResultCode.AlreadyArrived);
            }
        }

        return code;
    }

    private static void ClearPath(Army army)
    {
        army.PlannedPath = new List<string>();
        army.PathIndex = 0;
        army.EdgeProgress = 0;
    }

    /// <summary>推进一个时间步，返回每支部队本步的运动轨迹（用于遭遇扫描）。</summary>
    private static Dictionary<string, List<TrackPiece>> StepArmies(
        WorldState state,
        Dictionary<string, MarchTrail> trailByArmy)
    {
        var tracks = new Dictionary<string, List<TrackPiece>>(StringComparer.Ordinal);
        var stepTime = state.Time + 1;

        foreach (var army in state.Armies)
        {
            var pieces = new List<TrackPiece>();
            tracks[army.Id] = pieces;

            army.PlannedPath ??= new List<string>();
            if (army.PathIndex < 0)
            {
                army.PathIndex = 0;
            }

            if (army.PlannedPath.Count > 0)
            {
                army.PathIndex = Math.Min(army.PathIndex, army.PlannedPath.Count - 1);
                army.NodeId = army.PlannedPath[army.PathIndex];
            }

            army.EdgeProgress = Math.Clamp(army.EdgeProgress, 0.0, 1.0);

            var speed = Math.Max(0.01, MarchRules.UnitSpeed(army.UnitType));
            var remaining = 1.0;
            var guard = 0;

            while (remaining > MarchMath.Epsilon
                   && army.PathIndex + 1 < army.PlannedPath.Count
                   && guard++ < 64)
            {
                var from = army.PlannedPath[army.PathIndex];
                var to = army.PlannedPath[army.PathIndex + 1];
                var cost = MarchMath.TraversalCost(state.Map, from, to);
                if (!double.IsFinite(cost))
                {
                    break; // 计划路径里出现了地图上不存在的边：停下，不崩溃
                }

                var (startX, startY) = MarchMath.PositionOf(state.Map, army);
                var timeToCross = (1.0 - army.EdgeProgress) * cost / speed;
                var pieceStart = 1.0 - remaining;

                if (timeToCross <= remaining + MarchMath.Epsilon)
                {
                    var pieceEnd = Math.Min(1.0, pieceStart + Math.Max(0.0, timeToCross));
                    var (endX, endY) = MarchMath.NodePosition(state.Map, to);
                    pieces.Add(new TrackPiece(pieceStart, pieceEnd, startX, startY, endX, endY));

                    remaining = Math.Max(0.0, remaining - Math.Max(0.0, timeToCross));
                    army.PathIndex += 1;
                    army.NodeId = to;
                    army.EdgeProgress = 0;

                    AddTrailPoint(trailByArmy, army.Id, endX, endY, stepTime);
                }
                else
                {
                    army.EdgeProgress = Math.Min(1.0, army.EdgeProgress + remaining * speed / cost);
                    var (endX, endY) = MarchMath.PositionOf(state.Map, army);
                    pieces.Add(new TrackPiece(pieceStart, 1.0, startX, startY, endX, endY));

                    AddTrailPoint(trailByArmy, army.Id, endX, endY, stepTime);
                    remaining = 0;
                }
            }

            if (remaining > MarchMath.Epsilon)
            {
                var (x, y) = MarchMath.PositionOf(state.Map, army);
                pieces.Add(new TrackPiece(1.0 - remaining, 1.0, x, y, x, y));
            }
        }

        return tracks;
    }

    private static void AddTrailPoint(
        Dictionary<string, MarchTrail> trailByArmy,
        string armyId,
        double x,
        double y,
        int time)
    {
        if (!trailByArmy.TryGetValue(armyId, out var trail))
        {
            return;
        }

        if (trail.Points.Count > 0)
        {
            var last = trail.Points[^1];
            if (Math.Abs(last.X - x) < 1e-9 && Math.Abs(last.Y - y) < 1e-9 && last.Time == time)
            {
                return; // 同一步内重复点不重复记
            }
        }

        trail.Points.Add(new TrailPoint { X = x, Y = y, Time = time });
    }

    private List<EncounterEvent> DetectStepEncounters(
        WorldState state,
        Dictionary<string, List<TrackPiece>> tracks,
        int seed)
    {
        var result = new List<EncounterEvent>();
        var armies = state.Armies;

        for (var i = 0; i < armies.Count; i++)
        {
            for (var j = i + 1; j < armies.Count; j++)
            {
                var a = armies[i];
                var b = armies[j];
                if (!MarchRules.AreHostile(a.Faction, b.Faction))
                {
                    continue;
                }

                if (!MarchMath.TryMeet(
                        tracks.TryGetValue(a.Id, out var ta) ? ta : null,
                        tracks.TryGetValue(b.Id, out var tb) ? tb : null,
                        _engagementDistance,
                        out var time,
                        out var x,
                        out var y))
                {
                    continue;
                }

                _ = time; // 事件时间以整步为准（Time 为 int），接触点坐标只用于定位节点

                var encounter = EncounterRules.Create(
                    state.Map,
                    state.Time,
                    a,
                    b,
                    x,
                    y,
                    MarchMath.IsMoving(tracks[a.Id]),
                    MarchMath.IsMoving(tracks[b.Id]));

                encounter.Loss = _lossResolver.Resolve(encounter, state, seed);
                result.Add(encounter);
            }
        }

        return result
            .OrderBy(e => e.Time)
            .ThenBy(e => e.NodeId, StringComparer.Ordinal)
            .ThenBy(e => string.Join("|", e.ArmyIds), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>把本步所有遭遇的损失扣到返回状态上（入参 state 不动）。</summary>
    private static void ApplyLosses(WorldState state, IReadOnlyList<EncounterEvent> encounters)
    {
        var total = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var encounter in encounters)
        {
            if (encounter.Loss?.LossByArmy is null)
            {
                continue;
            }

            foreach (var pair in encounter.Loss.LossByArmy)
            {
                total.TryGetValue(pair.Key, out var current);
                total[pair.Key] = current + pair.Value;
            }
        }

        foreach (var army in state.Armies)
        {
            if (total.TryGetValue(army.Id, out var loss) && loss > 0)
            {
                army.Strength = Math.Max(0, army.Strength - loss);
            }
        }
    }

    private static bool AllArmiesIdle(WorldState state)
    {
        foreach (var army in state.Armies)
        {
            if (!MarchMath.IsIdle(army))
            {
                return false;
            }
        }

        return true;
    }
}
