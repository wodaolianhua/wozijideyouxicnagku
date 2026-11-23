namespace StrategyGame.Contracts;

// ===== 地图 / 地形 =====
public enum TerrainType
{
    Plain,      // 平原：行军正常
    Hill,       // 丘陵：略慢
    Mountain,   // 山地：慢，易伏击
    Forest,     // 森林：略慢，隐蔽
    River,      // 河流：需渡口，沿路才可过
}

public enum EdgeType
{
    Road,       // 道路：速度快
    MountainPath, // 山路：速度慢、有伏击风险
}

// ===== 势力 / 部队 =====
public enum Faction
{
    Player,
    Enemy,
    Neutral,
}

public enum UnitType
{
    Infantry,   // 步兵
    Cavalry,    // 骑兵：行军快
    Archer,     // 弓兵
    Siege,      // 攻城/器械：慢
}

// ===== 命令 =====
public enum OrderKind
{
    MoveTo,     // 单点终点：自动寻路
    MoveVia,    // 多点路径：逐段途经点
    Hold,       // 原地待命
}

// ===== 事件 / 结果 =====
public enum EventKind
{
    Arrival,    // 抵达终点
    Encounter,  // 遭遇敌军
}

public enum ResolveResultCode
{
    Ok,
    NoPath,           // 路网不可达
    InvalidTarget,    // 目标节点不存在
    AlreadyArrived,   // 已在终点
}
