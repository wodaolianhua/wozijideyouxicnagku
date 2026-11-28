namespace MiningGame.Contracts;

// ===== 矿物（按深度/价值递增）=====
// 数值即深度层级：0 最浅最便宜，5 最深最贵。算法与数据层共用。
public enum MineralType
{
    Soil,        // 泥土（废料，可能不值钱或少量）
    Coal,        // 煤
    Iron,        // 铁
    Copper,      // 铜
    Silver,      // 银
    Gold,        // 金
    RareCrystal, // 稀有晶体（最深）
}

// ===== 地下格子的材质（影响能否挖、挖掘耗时）=====
public enum TileType
{
    Air,         // 空气/已挖空，可通行
    Dirt,        // 土：软，好挖
    Stone,       // 石头：硬，慢
    Bedrock,     // 基岩：不可挖（边界）
    Ore,         // 矿脉：挖了产出矿物（具体矿物看 Cell.Mineral）
}

// ===== 工具/设备（挖矿能力递增）=====
public enum ToolKind
{
    BareHands,   // 徒手：只能挖土
    Pickaxe,     // 镐：能挖石头、浅矿
    Drill,       // 电钻：能挖硬岩、深矿
    Excavator,   // 挖掘机：全自动大范围（后期）
}

// ===== 自动化单位（后期买机器 / 雇人自动挖）=====
public enum WorkerKind
{
    Robot,       // 机器人：定时自动挖相邻格
    HiredMiner,  // 雇工：定时自动挖，效率更高但要工资
}

// ===== 玩家操作 =====
public enum ActionKind
{
    Move,        // 上/下/左/右移动一格
    Dig,         // 挖当前位置相邻的格（方向参数）
    Sell,        // 在地面卖掉背包里的矿 + 已打磨成品
    BuyTool,     // 买工具
    HireWorker,  // 买机器/雇人
    GoSurface,   // 回到地面（瞬移到地表井口）
    Polish,      // 在地面把背包原矿打磨成带成色的成品（追加，不改动前面数值）
}

// ===== 成品成色（品质档位：数值越高越贵）=====
// 打磨后每单位原矿产出一件成品，成色由 PolishConfig 的权重随机决定（走 seed，确定性）。
public enum QualityGrade
{
    Common,      // 普通
    Good,        // 良好
    Fine,        // 精良
    Excellent,   // 卓越
    Flawless,    // 完美
}

// ===== 方向 =====
public enum Direction
{
    Up, Down, Left, Right,
}

// ===== 挖掘结果码 =====
public enum DigResultCode
{
    Ok,
    Empty,          // 挖的是空气
    NotDiggable,    // 基岩，挖不动
    ToolTooWeak,    // 工具等级不够
    BackpackFull,   // 背包满
    OutOfBounds,    // 越界
}

// ===== 移动结果码 =====
public enum MoveResultCode
{
    Ok,
    Blocked,        // 目标格是实心（要先挖）
    OutOfBounds,
}

// ===== 总推进结果码 =====
public enum AdvanceResultCode
{
    Ok,
    NotEnoughMoney, // 买工具/雇人钱不够
    InvalidTarget,  // 目标非法
    BackpackFull,
}

// ===== 打磨结果码 =====
public enum PolishResultCode
{
    Ok,
    NotAtSurface,   // 不在表面，不能打磨
    NothingToPolish,// 背包没有原矿
    OutOfStamina,   // 体力不足以打磨任何单位
}
