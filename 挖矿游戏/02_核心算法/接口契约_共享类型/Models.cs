namespace MiningGame.Contracts;

// ===== 地下网格 =====
// 山头剖面：二维数组展平成 Cells，index = y * Width + x。
// (0,0) 为左上角；y 越大越深（越深矿物越值钱）。
public sealed class Cell
{
    public int X { get; set; }
    public int Y { get; set; }
    public TileType Tile { get; set; }
    public MineralType Mineral { get; set; }   // Tile=Ore 时有效：挖出什么矿
    public int Hardness { get; set; }           // 硬度：需要 Tool.Power >= 此值才能挖
    public int OreAmount { get; set; }          // 矿脉剩余产量（挖一次减 1，到 0 变 Air）
}

public sealed class MineGrid
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int SurfaceY { get; set; }           // 地表所在行（角色站这条线上算"在地面"可卖矿）
    public int ShaftX { get; set; }             // 初始井口 X（开局角色位置）
    public List<Cell> Cells { get; set; } = new();
}

// ===== 玩家 / 背包 =====
public sealed class BackpackItem
{
    public MineralType Mineral { get; set; }
    public int Amount { get; set; }
}

public sealed class Backpack
{
    public int Capacity { get; set; }           // 容量上限（能装多少单位矿）
    public int Used { get; set; }               // 已用容量
    public List<BackpackItem> Items { get; set; } = new();
}

// ===== 自动化单位（机器/雇工）实例 =====
public sealed class Worker
{
    public string Id { get; set; } = "";
    public WorkerKind Kind { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Power { get; set; }              // 挖掘能力（对标 Cell.Hardness）
    public int Interval { get; set; }           // 每 N 个时间步挖一次
    public int Wage { get; set; }               // 雇工每步工资（Robot 可为 0）
}

// ===== 工具规格（商店里卖什么） =====
public sealed class ToolSpec
{
    public ToolKind Kind { get; set; }
    public string Name { get; set; } = "";
    public int Power { get; set; }              // 能挖硬度 <= Power 的格子
    public int Price { get; set; }
}

public sealed class WorkerSpec
{
    public WorkerKind Kind { get; set; }
    public string Name { get; set; } = "";
    public int Power { get; set; }
    public int Interval { get; set; }
    public int Wage { get; set; }
    public int Price { get; set; }
}

// ===== 动作（玩家每步下的命令） =====
public sealed class GameAction
{
    public ActionKind Kind { get; set; }
    public Direction Direction { get; set; }    // Move/Dig 用：朝哪个方向
    public ToolKind Tool { get; set; }          // BuyTool 用：买哪种
    public WorkerKind Worker { get; set; }      // HireWorker 用：雇哪种
}

// ===== 挖掘结果 =====
public sealed class DigResult
{
    public DigResultCode Code { get; set; }
    public MineralType Mineral { get; set; }    // 挖到的矿（Code=Ok 时有效）
    public int Amount { get; set; }             // 本次产量
    public int X { get; set; }
    public int Y { get; set; }
    public int StaminaCost { get; set; }        // 本次消耗体力
}

// ===== 成品 / 打磨 =====
// 打磨：背包里的原矿 -> 带成色的成品。成品单独放 PolishedGoods，不再占背包容量。
public sealed class PolishedItem
{
    public MineralType Mineral { get; set; }
    public QualityGrade Grade { get; set; }
    public int Amount { get; set; }
}

// 成色的价格与概率配置（来自 01 数据，放 GameState 里供纯函数读取，方便调数）
public sealed class PolishConfig
{
    // 下标 = QualityGrade 数值；基础权重，越大越容易出。长度须覆盖全部档位。
    public List<int> GradeWeights { get; set; } = new();
    // 下标 = QualityGrade 数值；成品单价 = 原矿单价 * 百分比 / 100。
    public List<int> GradePricePercent { get; set; } = new();
    // 矿物越深（MineralType 数值越大）越容易出好成色：高档权重 += 矿物等级 * 本值 * 档位。
    public int GradeBonusPerTier { get; set; }
    public int UnitsPerAction { get; set; } = 5;      // 一次打磨消耗多少单位原矿
    public int StaminaPerUnit { get; set; } = 1;      // 每单位原矿打磨消耗体力
}

public sealed class PolishResult
{
    public PolishResultCode Code { get; set; }
    public List<PolishedItem> Produced { get; set; } = new(); // 本次打磨出的成品
    public int ConsumedUnits { get; set; }                    // 消耗的原矿单位数
    public int StaminaCost { get; set; }
}

// ===== 卖矿结果（收入要传回前端显示） =====
public sealed class SellResult
{
    public int Income { get; set; }             // 本次卖出总收入（原矿 + 成品）
    public List<BackpackItem> Sold { get; set; } = new();  // 卖掉的原始矿、各多少
    public List<PolishedSaleLine> SoldProducts { get; set; } = new(); // 卖掉的成品（含成色/单价）
}

// 成品出售明细：前端按成色分组显示，证明"不同成色不同价"。
public sealed class PolishedSaleLine
{
    public MineralType Mineral { get; set; }
    public QualityGrade Grade { get; set; }
    public int Amount { get; set; }
    public int UnitPrice { get; set; }
    public int Subtotal { get; set; }
}

// ===== 世界状态（全部中间进度都存这里，分步推进不丢） =====
public sealed class GameState
{
    public int Time { get; set; }               // 时间步
    public MineGrid Grid { get; set; } = new();
    public int MinerX { get; set; }             // 角色当前位置
    public int MinerY { get; set; }
    public int Stamina { get; set; }            // 体力：挖矿消耗，地面休息恢复
    public int MaxStamina { get; set; }
    public int SurfaceStaminaRegen { get; set; }// 在地表每步恢复的体力
    public int Money { get; set; }              // 启动资金 + 卖矿收入
    public ToolKind CurrentTool { get; set; }   // 当前装备工具
    public List<ToolKind> OwnedTools { get; set; } = new();  // 已拥有工具
    public Backpack Backpack { get; set; } = new();
    public List<Worker> Workers { get; set; } = new();       // 已雇机器/雇工
    public List<ToolSpec> ToolShop { get; set; } = new();    // 商店工具表（来自 01 数据）
    public List<WorkerSpec> WorkerShop { get; set; } = new();// 商店雇工表（来自 01 数据）
    public Dictionary<int, int> Prices { get; set; } = new();// MineralType(数值) -> 原矿单价
    public List<PolishedItem> PolishedGoods { get; set; } = new(); // 已打磨成品（含成色）
    public PolishConfig PolishSettings { get; set; } = new();      // 成色权重/价格配置
}

// ===== 世界初始配置（01 数据 JSON 的顶层结构） =====
public sealed class WorldConfig
{
    public int StartingMoney { get; set; }
    public int BackpackCapacity { get; set; }
    public int MaxStamina { get; set; }
    public int SurfaceStaminaRegen { get; set; }             // 在地表每步恢复的体力
    public Dictionary<int, int> Prices { get; set; } = new(); // 原矿单价表
    public List<ToolSpec> ToolShop { get; set; } = new();
    public List<WorkerSpec> WorkerShop { get; set; } = new();
    public PolishConfig PolishSettings { get; set; } = new();
}

// ===== 每步推进的产出（前端据此渲染 + 显示事件） =====
public sealed class DigOutcome
{
    public List<DigResult> Digs { get; set; } = new();       // 本步所有挖掘（玩家 + 自动单位）
    public SellResult Sale { get; set; } = new();            // 本步卖矿结果（收入在这里）
    public PolishResult Polish { get; set; } = new();        // 本步打磨结果（ConsumedUnits=0 表示没打磨）
    public string Message { get; set; } = "";                // 给玩家看的提示文字
}

// ===== 总推进结果 =====
public sealed class AdvanceResult
{
    public GameState State { get; set; } = new();            // 推进后的新状态（纯函数：不改入参）
    public List<DigOutcome> Outcomes { get; set; } = new();  // 每步产出（含卖矿收入，前端显示）
    public AdvanceResultCode Code { get; set; }
}
