namespace MiningGame.Contracts;

// 以下接口是 02 核心算法包必须实现的对外契约。
// 03 表现层只依赖这些接口，不依赖具体实现。

public interface IMiningSimulator
{
    // 按 actions 推进 steps 个时间步，产出新状态 + 每步的产出（挖掘/卖矿）。
    // 纯函数：不改入参 state；相同输入（含 seed）必得相同输出。
    // actions 语义见下方注释。卖矿收入、挖到什么都填进 Outcomes 返回前端显示。
    AdvanceResult Advance(GameState state, IReadOnlyList<GameAction> actions, int steps, int seed);

    // 快进：推进到"背包满 / 体力耗尽 / maxSteps 步"三者最先到者为止。
    // 用于"我懒得一步步挖，挖满就停"。actions 语义同 Advance。
    AdvanceResult FastForward(GameState state, IReadOnlyList<GameAction> actions, int maxSteps, int seed);
}

public interface IDigResolver
{
    // 挖掘判定：给定状态、目标格坐标，返回 DigResult（挖到什么/消耗多少体力/结果码）。
    // 纯函数，不改 state。工具等级不够、基岩、背包满、越界都要返回明确结果码，不崩溃。
    DigResult Dig(GameState state, int x, int y, int seed);
}

public interface IMovementResolver
{
    // 移动判定：朝 direction 走一格能否成功（空气可过、实心挡住、越界拒绝）。
    MoveResultCode CanMove(GameState state, Direction direction);
}

public interface IBackpackPolicy
{
    // 背包能否再装 amount 单位；装满返回 false。
    bool CanAdd(Backpack backpack, int amount);
    // 背包当前总价值（按 state.Prices 算），供"卖出预估"显示。
    int Valuation(Backpack backpack, IReadOnlyDictionary<int, int> prices);
}

public interface ISellResolver
{
    // 卖掉背包全部矿物：算收入、列明细。纯函数，不改 state（由 simulator 应用结果）。
    // 空背包返回 Income=0 的空结果。
    SellResult Sell(GameState state);
}

public interface IAutomationResolver
{
    // 自动单位（机器/雇工）在一步内的产出：返回本步所有自动 DigResult。
    // 按每个 Worker 的 Interval 决定这步挖不挖；工资从 Money 扣（由 simulator 应用）。
    List<DigResult> Produce(GameState state, int seed);
}

// ===== 动作语义（执行 AI 必读，避免做岔）=====
// GameAction 每步最多一条玩家主动作，simulator 按 Kind 分派：
//   Move       —— 朝 Direction 移动角色一格；目标须是 Air，否则 MoveResultCode.Blocked。
//   Dig        —— 挖角色朝 Direction 相邻的格；调 IDigResolver，成功则矿进背包、格子 OreAmount-1。
//   Sell       —— 必须角色在地表（MinerY <= SurfaceY），调 ISellResolver，收入进 Money、背包清空。
//   BuyTool    —— 从 ToolShop 找 Tool，钱够则扣钱、加入 OwnedTools 并装备。
//   HireWorker —— 从 WorkerShop 找 Worker，钱够则扣钱、加入 Workers。
//   GoSurface  —— 角色瞬移到井口 (ShaftX, SurfaceY)，恢复体力（地面休息）。
// 玩家不主动作时传空 actions：本步只结算自动单位产出 + 体力自然消耗。
// 状态字段都能存中间进度（位置/体力/钱/背包/工人），所以分步推进不需要重复下令。
