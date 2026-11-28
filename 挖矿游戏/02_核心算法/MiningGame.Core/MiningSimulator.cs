using MiningGame.Contracts;

namespace MiningGame.Core;

public sealed class MiningSimulator : IMiningSimulator
{
    private readonly BackpackPolicy _backpack;
    private readonly IDigResolver _dig;
    private readonly IMovementResolver _move;
    private readonly ISellResolver _sell;
    private readonly IPolishResolver _polish;
    private readonly IAutomationResolver _automation;

    public MiningSimulator(
        BackpackPolicy? backpack = null,
        IDigResolver? dig = null,
        IMovementResolver? move = null,
        ISellResolver? sell = null,
        IPolishResolver? polish = null,
        IAutomationResolver? automation = null)
    {
        _backpack = backpack ?? new BackpackPolicy();
        _dig = dig ?? new DigResolver(_backpack);
        _move = move ?? new MovementResolver();
        _sell = sell ?? new SellResolver();
        _polish = polish ?? new PolishResolver();
        _automation = automation ?? new AutomationResolver(_backpack);
    }

    public AdvanceResult Advance(GameState state, IReadOnlyList<GameAction> actions, int steps, int seed)
    {
        var result = new AdvanceResult();
        var s = GameStateCloner.Clone(state);

        int count = Math.Max(0, steps);
        for (int i = 0; i < count; i++)
        {
            GameAction? action = actions != null && i < actions.Count ? actions[i] : null;
            var outcome = Step(s, action, seed, out var code);
            if (code != AdvanceResultCode.Ok) result.Code = code;
            result.Outcomes.Add(outcome);
        }

        result.State = s;
        return result;
    }

    public AdvanceResult FastForward(GameState state, IReadOnlyList<GameAction> actions, int maxSteps, int seed)
    {
        var result = new AdvanceResult();
        var s = GameStateCloner.Clone(state);

        int limit = Math.Max(1, maxSteps);
        for (int i = 0; i < limit; i++)
        {
            if (s.Backpack.Used >= s.Backpack.Capacity || s.Stamina <= 0) break;

            GameAction? action = actions != null && actions.Count > 0 ? actions[i % actions.Count] : null;
            var outcome = Step(s, action, seed, out var code);
            if (code != AdvanceResultCode.Ok) result.Code = code;
            result.Outcomes.Add(outcome);
        }

        result.State = s;
        return result;
    }

    private DigOutcome Step(GameState s, GameAction? action, int seed, out AdvanceResultCode code)
    {
        code = AdvanceResultCode.Ok;
        var outcome = new DigOutcome();
        var messages = new List<string>();

        if (action != null) ApplyPlayerAction(s, action, seed, outcome, messages, ref code);

        ApplyAutomation(s, seed, outcome, messages);
        ApplyWages(s, messages);

        // 在地表每步恢复体力（本步没动作才算休息，避免边打磨边无限回体力）
        if (action == null && s.MinerY <= s.Grid.SurfaceY && s.SurfaceStaminaRegen > 0)
            s.Stamina = Math.Min(s.MaxStamina, s.Stamina + s.SurfaceStaminaRegen);

        s.Time += 1;
        outcome.Message = string.Join("；", messages);
        return outcome;
    }

    private void ApplyPlayerAction(
        GameState s,
        GameAction action,
        int seed,
        DigOutcome outcome,
        List<string> messages,
        ref AdvanceResultCode code)
    {
        switch (action.Kind)
        {
            case ActionKind.Move:
                DoMove(s, action.Direction, messages);
                break;
            case ActionKind.Dig:
                DoDig(s, action.Direction, seed, outcome, messages);
                break;
            case ActionKind.Sell:
                DoSell(s, outcome, messages, ref code);
                break;
            case ActionKind.BuyTool:
                DoBuyTool(s, action.Tool, messages, ref code);
                break;
            case ActionKind.HireWorker:
                DoHireWorker(s, action.Worker, messages, ref code);
                break;
            case ActionKind.GoSurface:
                DoGoSurface(s, messages);
                break;
            case ActionKind.Polish:
                DoPolish(s, seed, outcome, messages, ref code);
                break;
        }
    }

    private void DoMove(GameState s, Direction direction, List<string> messages)
    {
        var (dx, dy) = MovementResolver.DirectionDelta(direction);
        var code = _move.CanMove(s, direction);

        if (code == MoveResultCode.Ok)
        {
            s.MinerX += dx;
            s.MinerY += dy;
        }
        else if (code == MoveResultCode.Blocked)
        {
            messages.Add("前方被挡住，先挖开");
        }
        else
        {
            messages.Add("到边界了");
        }
    }

    private void DoDig(GameState s, Direction direction, int seed, DigOutcome outcome, List<string> messages)
    {
        var (dx, dy) = MovementResolver.DirectionDelta(direction);
        int tx = s.MinerX + dx;
        int ty = s.MinerY + dy;
        var res = _dig.Dig(s, tx, ty, seed);

        switch (res.Code)
        {
            case DigResultCode.Ok:
                if (s.Stamina < res.StaminaCost)
                {
                    messages.Add("体力不足，回地面休息");
                    return;
                }

                var cell = GridUtil.CellAt(s.Grid, tx, ty);
                if (cell == null) return;

                string tileName = cell.Tile == TileType.Ore ? MineralName(cell.Mineral) + "矿脉" : CellName(cell.Tile);

                s.Stamina -= res.StaminaCost;
                if (res.Amount > 0) _backpack.Add(s.Backpack, res.Mineral, res.Amount);

                if (cell.Tile == TileType.Ore)
                {
                    cell.OreAmount = Math.Max(0, cell.OreAmount - 1);
                    if (cell.OreAmount <= 0)
                    {
                        cell.Tile = TileType.Air;
                        cell.Hardness = 0;
                    }
                }
                else
                {
                    // 土/石头一镐就通，变成空气
                    cell.Tile = TileType.Air;
                    cell.Hardness = 0;
                    cell.OreAmount = 0;
                }

                outcome.Digs.Add(res);
                messages.Add(res.Amount > 0
                    ? $"挖到 {MineralName(res.Mineral)} ×{res.Amount}"
                    : $"挖开{tileName}");
                break;

            case DigResultCode.Empty:
                messages.Add("这里是空的");
                break;
            case DigResultCode.NotDiggable:
                messages.Add("基岩，挖不动");
                break;
            case DigResultCode.ToolTooWeak:
                messages.Add("工具太弱，挖不动（去商店升级）");
                break;
            case DigResultCode.BackpackFull:
                messages.Add("背包满了，回地面卖矿");
                break;
            case DigResultCode.OutOfBounds:
                messages.Add("越界了");
                break;
        }
    }

    private void DoSell(GameState s, DigOutcome outcome, List<string> messages, ref AdvanceResultCode code)
    {
        if (s.MinerY > s.Grid.SurfaceY)
        {
            messages.Add("要回到地面才能卖矿");
            code = AdvanceResultCode.InvalidTarget;
            return;
        }

        var sale = _sell.Sell(s);
        outcome.Sale = sale;

        if (sale.Income == 0 && sale.Sold.Count == 0 && sale.SoldProducts.Count == 0)
        {
            messages.Add("背包和成品都是空的");
            return;
        }

        s.Money += sale.Income;
        s.Backpack.Items.Clear();
        s.Backpack.Used = 0;
        s.PolishedGoods.Clear();

        messages.Add($"卖出收入 ¥{sale.Income}");
    }

    private void DoBuyTool(GameState s, ToolKind kind, List<string> messages, ref AdvanceResultCode code)
    {
        var spec = s.ToolShop.FirstOrDefault(t => t.Kind == kind);
        if (spec == null)
        {
            messages.Add("商店没有这种工具");
            code = AdvanceResultCode.InvalidTarget;
            return;
        }

        if (s.OwnedTools.Contains(kind))
        {
            messages.Add($"已拥有 {spec.Name}");
            return;
        }

        if (s.Money < spec.Price)
        {
            messages.Add($"钱不够买 {spec.Name}（需 ¥{spec.Price}）");
            code = AdvanceResultCode.NotEnoughMoney;
            return;
        }

        s.Money -= spec.Price;
        s.OwnedTools.Add(kind);
        s.CurrentTool = kind;
        messages.Add($"买下 {spec.Name}，已装备");
    }

    private void DoHireWorker(GameState s, WorkerKind kind, List<string> messages, ref AdvanceResultCode code)
    {
        var spec = s.WorkerShop.FirstOrDefault(w => w.Kind == kind);
        if (spec == null)
        {
            messages.Add("商店没有这种雇工");
            code = AdvanceResultCode.InvalidTarget;
            return;
        }

        if (s.Money < spec.Price)
        {
            messages.Add($"钱不够雇 {spec.Name}（需 ¥{spec.Price}）");
            code = AdvanceResultCode.NotEnoughMoney;
            return;
        }

        s.Money -= spec.Price;
        s.Workers.Add(new Worker
        {
            Id = $"W{s.Workers.Count + 1}",
            Kind = kind,
            X = s.MinerX,
            Y = s.MinerY,
            Power = spec.Power,
            Interval = spec.Interval,
            Wage = spec.Wage,
        });
        messages.Add($"雇了 {spec.Name}，会跟着你挖相邻矿");
    }

    private void DoGoSurface(GameState s, List<string> messages)
    {
        s.MinerX = s.Grid.ShaftX;
        s.MinerY = s.Grid.SurfaceY;
        s.Stamina = s.MaxStamina;
        messages.Add("回到地面，体力已恢复");
    }

    private void DoPolish(GameState s, int seed, DigOutcome outcome, List<string> messages, ref AdvanceResultCode code)
    {
        var cfg = s.PolishSettings ?? new PolishConfig();
        var result = _polish.Polish(s, cfg.UnitsPerAction, seed);
        outcome.Polish = result;

        switch (result.Code)
        {
            case PolishResultCode.NotAtSurface:
                messages.Add("要回到地面才能打磨");
                code = AdvanceResultCode.InvalidTarget;
                return;
            case PolishResultCode.NothingToPolish:
                messages.Add("背包里没有原矿可打磨");
                return;
            case PolishResultCode.OutOfStamina:
                messages.Add("体力不足，先在地面休息");
                return;
        }

        ConsumeBackpack(s, result.ConsumedUnits);
        s.Stamina = Math.Max(0, s.Stamina - result.StaminaCost);

        foreach (var produced in result.Produced)
        {
            var same = s.PolishedGoods.FirstOrDefault(p => p.Mineral == produced.Mineral && p.Grade == produced.Grade);
            if (same == null)
            {
                same = new PolishedItem { Mineral = produced.Mineral, Grade = produced.Grade, Amount = 0 };
                s.PolishedGoods.Add(same);
            }
            same.Amount += produced.Amount;
        }

        messages.Add($"打磨 {result.ConsumedUnits} 单位原矿：{FormatProduced(result.Produced)}");
    }

    private static void ConsumeBackpack(GameState s, int units)
    {
        int left = units;
        var items = s.Backpack.Items;

        for (int i = 0; i < items.Count && left > 0; i++)
        {
            int take = Math.Min(items[i].Amount, left);
            items[i].Amount -= take;
            left -= take;
            if (items[i].Amount <= 0)
            {
                items.RemoveAt(i);
                i--;
            }
        }

        s.Backpack.Used = items.Sum(i => i.Amount);
    }

    private void ApplyAutomation(GameState s, int seed, DigOutcome outcome, List<string> messages)
    {
        if (s.Workers.Count == 0) return;

        var digs = _automation.Produce(s, seed);
        foreach (var dig in digs)
        {
            var cell = GridUtil.CellAt(s.Grid, dig.X, dig.Y);
            if (cell == null || cell.Tile == TileType.Air || cell.Tile == TileType.Bedrock) continue;
            if (dig.Amount > 0 && !_backpack.CanAdd(s.Backpack, dig.Amount)) continue;

            if (dig.Amount > 0) _backpack.Add(s.Backpack, dig.Mineral, dig.Amount);

            if (cell.Tile == TileType.Ore)
            {
                cell.OreAmount = Math.Max(0, cell.OreAmount - 1);
                if (cell.OreAmount <= 0)
                {
                    cell.Tile = TileType.Air;
                    cell.Hardness = 0;
                }
            }
            else
            {
                cell.Tile = TileType.Air;
                cell.Hardness = 0;
                cell.OreAmount = 0;
            }

            outcome.Digs.Add(dig);
            messages.Add(dig.Amount > 0
                ? $"自动单位挖到 {MineralName(dig.Mineral)} ×{dig.Amount}"
                : "自动单位挖开一格");
        }
    }

    private static void ApplyWages(GameState s, List<string> messages)
    {
        int wage = s.Workers.Sum(w => Math.Max(0, w.Wage));
        if (wage <= 0) return;

        if (s.Money >= wage)
        {
            s.Money -= wage;
        }
        else
        {
            s.Money = 0;
            messages.Add("钱不够付工资");
        }
    }

    public static string MineralName(MineralType mineral) => mineral switch
    {
        MineralType.Soil => "泥土",
        MineralType.Coal => "煤",
        MineralType.Iron => "铁",
        MineralType.Copper => "铜",
        MineralType.Silver => "银",
        MineralType.Gold => "金",
        MineralType.RareCrystal => "稀有晶体",
        _ => "未知矿物",
    };

    public static string GradeName(QualityGrade grade) => grade switch
    {
        QualityGrade.Common => "普通",
        QualityGrade.Good => "良好",
        QualityGrade.Fine => "精良",
        QualityGrade.Excellent => "卓越",
        QualityGrade.Flawless => "完美",
        _ => "普通",
    };

    private static string CellName(TileType tile) => tile switch
    {
        TileType.Dirt => "泥土",
        TileType.Stone => "岩石",
        TileType.Bedrock => "基岩",
        _ => "空格",
    };

    private static string FormatProduced(List<PolishedItem> produced) =>
        string.Join("、", produced.Select(p => $"{GradeName(p.Grade)}{MineralName(p.Mineral)}×{p.Amount}"));
}
