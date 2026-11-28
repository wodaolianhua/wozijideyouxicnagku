using MiningGame.Contracts;

namespace MiningGame.Core;

public sealed class AutomationResolver : IAutomationResolver
{
    private readonly IBackpackPolicy _backpack;

    public AutomationResolver(IBackpackPolicy? backpack = null) => _backpack = backpack ?? new BackpackPolicy();

    public List<DigResult> Produce(GameState state, int seed)
    {
        var results = new List<DigResult>();
        if (state.Workers.Count == 0) return results;

        foreach (var worker in state.Workers)
        {
            if (worker.Interval <= 0) continue;
            if (state.Time % worker.Interval != 0) continue;

            var target = FindTarget(state, worker.X, worker.Y, worker.Power);
            if (target == null) continue;

            bool isOre = target.Tile == TileType.Ore;
            if (isOre && !_backpack.CanAdd(state.Backpack, 1)) continue;

            results.Add(new DigResult
            {
                Code = DigResultCode.Ok,
                Mineral = isOre ? target.Mineral : MineralType.Soil,
                Amount = isOre ? 1 : 0,
                X = target.X,
                Y = target.Y,
                StaminaCost = 0,   // 机器/雇工不消耗玩家体力
            });
        }

        return results;
    }

    // 优先挖相邻矿脉，其次挖相邻可挖的土/石开路。
    private static Cell? FindTarget(GameState state, int x, int y, int power)
    {
        Cell? fallback = null;

        foreach (var (dx, dy) in Dirs)
        {
            var cell = GridUtil.CellAt(state.Grid, x + dx, y + dy);
            if (cell == null) continue;
            if (cell.Tile == TileType.Air || cell.Tile == TileType.Bedrock) continue;
            if (cell.Hardness > power) continue;

            if (cell.Tile == TileType.Ore) return cell;
            fallback ??= cell;
        }

        return fallback;
    }

    private static readonly (int dx, int dy)[] Dirs =
    {
        (0, -1), (-1, 0), (1, 0), (0, 1),
    };
}
