using StrategyGame.Contracts;

namespace StrategyGame.Core.Tests;

/// <summary>单测共用的小地图与部队工厂。</summary>
/// <remarks>
/// 路网（无向）：
///   N0(0,0 平原) --道路2-- N1(10,0 平原) --道路4-- N2(20,0 平原) --道路6-- N3(30,0 丘陵)
///   N0 --山路20-- N2
///   N1 --山路10-- N4(10,10 森林) --山路4-- N5(20,10 山地) --山路8-- N2
///   N2 --道路1-- N7(25,-5 平原)
///   N6(40,40) 孤立无连边
/// </remarks>
internal static class TestWorld
{
    public static WorldMap BuildMap()
    {
        var map = new WorldMap();
        AddNode(map, "N0", 0, 0, TerrainType.Plain);
        AddNode(map, "N1", 10, 0, TerrainType.Plain);
        AddNode(map, "N2", 20, 0, TerrainType.Plain);
        AddNode(map, "N3", 30, 0, TerrainType.Hill);
        AddNode(map, "N4", 10, 10, TerrainType.Forest);
        AddNode(map, "N5", 20, 10, TerrainType.Mountain);
        AddNode(map, "N6", 40, 40, TerrainType.Plain);
        AddNode(map, "N7", 25, -5, TerrainType.Plain);

        AddEdge(map, "N0", "N1", EdgeType.Road, 2);
        AddEdge(map, "N1", "N2", EdgeType.Road, 4);
        AddEdge(map, "N2", "N3", EdgeType.Road, 6);
        AddEdge(map, "N0", "N2", EdgeType.MountainPath, 20);
        AddEdge(map, "N1", "N4", EdgeType.MountainPath, 10);
        AddEdge(map, "N4", "N5", EdgeType.MountainPath, 4);
        AddEdge(map, "N5", "N2", EdgeType.MountainPath, 8);
        AddEdge(map, "N2", "N7", EdgeType.Road, 1);
        return map;
    }

    public static WorldState BuildState(params Army[] armies) => new()
    {
        Time = 0,
        Map = BuildMap(),
        Armies = armies.ToList(),
    };

    public static Army Army(
        string id,
        Faction faction,
        string nodeId,
        int strength = 1000,
        UnitType unitType = UnitType.Infantry) => new()
    {
        Id = id,
        Name = id,
        Faction = faction,
        UnitType = unitType,
        Strength = strength,
        NodeId = nodeId,
    };

    /// <summary>给部队预置计划路径（模拟“已下令、正在行军”）。</summary>
    public static Army WithPath(this Army army, params string[] path)
    {
        army.PlannedPath = path.ToList();
        army.PathIndex = 0;
        army.NodeId = path[0];
        army.EdgeProgress = 0;
        return army;
    }

    private static void AddNode(WorldMap map, string id, double x, double y, TerrainType terrain)
        => map.Nodes.Add(new MapNode { Id = id, Name = id, X = x, Y = y, Terrain = terrain });

    private static void AddEdge(WorldMap map, string from, string to, EdgeType type, double cost)
        => map.Edges.Add(new MapEdge { From = from, To = to, Type = type, Cost = cost });
}
