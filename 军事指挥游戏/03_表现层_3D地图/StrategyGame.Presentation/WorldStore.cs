using System.Text.Json;
using StrategyGame.Contracts;
using StrategyGame.Core;

namespace StrategyGame.Presentation;

/// <summary>
/// 内存中的世界状态宿主：加载 01 的地图 JSON，持有当前 WorldState，
/// 对外只暴露 02 库的 Advance / FastForward / PathFinder 接口。
/// </summary>
public sealed class WorldStore
{
    private const int Seed = 1;
    private const int MaxAdvanceSteps = 50;
    private const int MaxFastForwardSteps = 2000;

    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string _mapPath;
    private readonly IMarchSimulator _simulator = new MarchSimulator();
    private readonly IPathFinder _pathFinder = new PathFinder();
    private WorldState _state;

    public WorldStore(string mapPath)
    {
        _mapPath = mapPath;
        _state = LoadWorld(mapPath);
    }

    public WorldState Snapshot()
    {
        lock (_gate)
        {
            return _state;
        }
    }

    public WorldState Reset()
    {
        lock (_gate)
        {
            _state = LoadWorld(_mapPath);
            return _state;
        }
    }

    public AdvanceResult Advance(IReadOnlyList<Order>? orders, int steps)
    {
        lock (_gate)
        {
            var result = _simulator.Advance(
                _state,
                orders ?? Array.Empty<Order>(),
                Math.Clamp(steps, 1, MaxAdvanceSteps),
                Seed);
            _state = result.State;
            return result;
        }
    }

    public AdvanceResult FastForward(IReadOnlyList<Order>? orders, int maxSteps)
    {
        lock (_gate)
        {
            var result = _simulator.FastForward(
                _state,
                orders ?? Array.Empty<Order>(),
                Math.Clamp(maxSteps, 1, MaxFastForwardSteps),
                Seed);
            _state = result.State;
            return result;
        }
    }

    /// <summary>只读预览：不下令、不改状态，供前端画虚线路径。</summary>
    public PreviewResult Preview(string armyId, IReadOnlyList<string>? waypoints)
    {
        lock (_gate)
        {
            var army = _state.Armies.FirstOrDefault(a => a.Id == armyId);
            if (army is null)
            {
                return new PreviewResult { Code = ResolveResultCode.InvalidTarget };
            }

            var points = (waypoints ?? Array.Empty<string>())
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .ToList();
            if (points.Count == 0 || points.Any(p => _state.Map.Nodes.All(n => n.Id != p)))
            {
                return new PreviewResult { Code = ResolveResultCode.InvalidTarget };
            }

            var start = string.IsNullOrEmpty(army.NodeId) ? points[0] : army.NodeId;
            var path = points.Count > 1
                ? _pathFinder.FindPathVia(_state.Map, start, points)
                : _pathFinder.FindPath(_state.Map, start, points[0]);

            return new PreviewResult
            {
                Path = path,
                Code = path.Count == 0 ? ResolveResultCode.NoPath : ResolveResultCode.Ok,
            };
        }
    }

    private static WorldState LoadWorld(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"找不到地图数据文件：{path}");
        }

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<WorldState>(json, LoadOptions);
            if (state?.Map?.Nodes is null || state.Map.Nodes.Count == 0 || state.Armies is null)
            {
                throw new InvalidDataException($"地图数据为空或字段缺失：{path}");
            }

            return state;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"地图数据 JSON 解析失败：{path}（{ex.Message}）", ex);
        }
    }
}

public sealed class AdvanceRequest
{
    public List<Order> Orders { get; set; } = new();
    public int Steps { get; set; } = 1;
}

public sealed class FastForwardRequest
{
    public List<Order> Orders { get; set; } = new();
    public int MaxSteps { get; set; } = 200;
}

public sealed class PreviewRequest
{
    public string ArmyId { get; set; } = "";
    public List<string> Waypoints { get; set; } = new();
}

public sealed class PreviewResult
{
    public List<string> Path { get; set; } = new();
    public ResolveResultCode Code { get; set; }
}
