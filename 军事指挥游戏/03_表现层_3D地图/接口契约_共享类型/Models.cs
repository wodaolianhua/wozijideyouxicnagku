namespace StrategyGame.Contracts;

// ===== 地图 =====
public sealed class MapNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public TerrainType Terrain { get; set; }
    public double X { get; set; }   // 展示用坐标
    public double Y { get; set; }
}

public sealed class MapEdge
{
    public string From { get; set; } = "";   // 节点 Id
    public string To { get; set; } = "";     // 节点 Id
    public EdgeType Type { get; set; }
    public double Cost { get; set; }         // 行军基础代价，越大越慢
}

public sealed class WorldMap
{
    public List<MapNode> Nodes { get; set; } = new();
    public List<MapEdge> Edges { get; set; } = new();
}

// ===== 部队 =====
public sealed class Army
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Faction Faction { get; set; }
    public UnitType UnitType { get; set; }
    public int Strength { get; set; }          // 兵力
    public string NodeId { get; set; } = "";   // 当前所在节点（= PlannedPath[PathIndex]）
    public double EdgeProgress { get; set; }   // 从 PlannedPath[PathIndex] 走向下一点的进度 0..1

    // 当前执行的计划路径（下令后写入，分步推进时靠它续走，不需重复下令）
    public List<string> PlannedPath { get; set; } = new();  // 节点 Id 序列
    public int PathIndex { get; set; }                       // 当前走到第几段（指向 PlannedPath 中的当前节点）
}

// ===== 命令 =====
public sealed class Order
{
    public string ArmyId { get; set; } = "";
    public OrderKind Kind { get; set; }
    public List<string> Waypoints { get; set; } = new();  // MoveTo: 1 个终点；MoveVia: 途经点+终点（按顺序）
}

// ===== 行军轨迹（供前端画箭头） =====
public sealed class TrailPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public int Time { get; set; }              // 到达该点的时间步
}

public sealed class MarchTrail
{
    public string ArmyId { get; set; } = "";
    public List<TrailPoint> Points { get; set; } = new();
}

// ===== 遭遇 / 结算 =====
public sealed class EncounterEvent
{
    public int Time { get; set; }
    public string NodeId { get; set; } = "";
    public List<string> ArmyIds { get; set; } = new();
    public bool IsAmbush { get; set; }         // 是否有一方伏击（影响损失）
    public string AmbusherId { get; set; } = "";

    // 本次遭遇的损失结果（随事件一起传给前端，供"损失情报面板"展示）。
    // Advance/FastForward 内部调用损失结算后填入；前端直接读这个字段，不用自己算。
    public LossResult Loss { get; set; } = new();
}

public sealed class LossResult
{
    public Dictionary<string, int> LossByArmy { get; set; } = new();  // ArmyId -> 损失兵力
}

// ===== 世界状态 =====
public sealed class WorldState
{
    public int Time { get; set; }
    public WorldMap Map { get; set; } = new();
    public List<Army> Armies { get; set; } = new();
}

// ===== 推进结果 =====
public sealed class AdvanceResult
{
    public WorldState State { get; set; } = new();
    public List<MarchTrail> Trails { get; set; } = new();       // 本步产生的轨迹，供画箭头
    public List<EncounterEvent> Encounters { get; set; } = new(); // 本步触发的遭遇
    public ResolveResultCode Code { get; set; }
}
