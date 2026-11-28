using MiningGame.Contracts;

namespace MiningGame.Core;

// 把 01 包的数据（网格 + 世界配置）装配成开局状态。纯函数：不读文件、不读时钟。
public static class GameStateFactory
{
    public static GameState Create(MineGrid grid, WorldConfig config)
    {
        var state = new GameState
        {
            Time = 0,
            Grid = grid,
            MinerX = grid.ShaftX,
            MinerY = grid.SurfaceY,
            Stamina = config.MaxStamina,
            MaxStamina = config.MaxStamina,
            SurfaceStaminaRegen = config.SurfaceStaminaRegen,
            Money = config.StartingMoney,
            CurrentTool = ToolKind.BareHands,
            OwnedTools = new List<ToolKind> { ToolKind.BareHands },
            Backpack = new Backpack
            {
                Capacity = config.BackpackCapacity,
                Used = 0,
                Items = new List<BackpackItem>(),
            },
            ToolShop = config.ToolShop.ToList(),
            WorkerShop = config.WorkerShop.ToList(),
            Prices = new Dictionary<int, int>(config.Prices),
            PolishedGoods = new List<PolishedItem>(),
            PolishSettings = config.PolishSettings,
        };

        // 保证开局站的位置是空气，避免数据把井口放实了导致无法移动。
        var start = GridUtil.CellAt(grid, state.MinerX, state.MinerY);
        if (start != null)
        {
            start.Tile = TileType.Air;
            start.Hardness = 0;
            start.OreAmount = 0;
        }

        return state;
    }
}
