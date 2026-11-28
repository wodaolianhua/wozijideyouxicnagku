using MiningGame.Contracts;

namespace MiningGame.Core.Tests;

internal static class TestWorld
{
    public const int Width = 8;
    public const int Height = 7;

    public static WorldConfig Config(int capacity = 24, int money = 100) => new()
    {
        StartingMoney = money,
        BackpackCapacity = capacity,
        MaxStamina = 40,
        SurfaceStaminaRegen = 5,
        Prices = new Dictionary<int, int>
        {
            { 0, 1 }, { 1, 7 }, { 2, 16 }, { 3, 30 }, { 4, 62 }, { 5, 135 }, { 6, 330 },
        },
        ToolShop = new List<ToolSpec>
        {
            new() { Kind = ToolKind.BareHands, Name = "徒手", Power = 1, Price = 0 },
            new() { Kind = ToolKind.Pickaxe, Name = "镐", Power = 3, Price = 30 },
            new() { Kind = ToolKind.Drill, Name = "电钻", Power = 6, Price = 180 },
            new() { Kind = ToolKind.Excavator, Name = "挖掘机", Power = 8, Price = 900 },
        },
        WorkerShop = new List<WorkerSpec>
        {
            new() { Kind = WorkerKind.Robot, Name = "机器人", Power = 3, Interval = 1, Wage = 0, Price = 50 },
            new() { Kind = WorkerKind.HiredMiner, Name = "雇工", Power = 5, Interval = 2, Wage = 2, Price = 40 },
        },
        PolishSettings = new PolishConfig
        {
            GradeWeights = new List<int> { 60, 26, 10, 3, 1 },
            GradePricePercent = new List<int> { 100, 140, 190, 260, 360 },
            GradeBonusPerTier = 2,
            UnitsPerAction = 5,
            StaminaPerUnit = 1,
        },
    };

    public static MineGrid Grid()
    {
        var grid = new MineGrid { Width = Width, Height = Height, SurfaceY = 1, ShaftX = 3 };

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var cell = new Cell
                {
                    X = x,
                    Y = y,
                    Tile = TileType.Dirt,
                    Mineral = MineralType.Soil,
                    Hardness = 1,
                    OreAmount = 0,
                };

                if (y <= 1)
                {
                    cell.Tile = TileType.Air;
                    cell.Hardness = 0;
                }
                else if (y >= 5 || x == Width - 1)
                {
                    cell.Tile = TileType.Bedrock;
                    cell.Hardness = 99;
                }

                if (x == 3 && y == 2)
                {
                    cell.Tile = TileType.Air;
                    cell.Hardness = 0;
                }
                if (x == 3 && y == 3)
                {
                    cell.Tile = TileType.Ore;
                    cell.Mineral = MineralType.Iron;
                    cell.Hardness = 3;
                    cell.OreAmount = 3;
                }
                if (x == 4 && y == 3)
                {
                    cell.Tile = TileType.Ore;
                    cell.Mineral = MineralType.Gold;
                    cell.Hardness = 6;
                    cell.OreAmount = 2;
                }

                grid.Cells.Add(cell);
            }
        }

        return grid;
    }

    // 默认站位 (3,2)：脚下是空气竖井，正下方 (3,3) 是铁矿。
    public static GameState State(int capacity = 24, int money = 100)
    {
        var state = GameStateFactory.Create(Grid(), Config(capacity, money));
        state.MinerX = 3;
        state.MinerY = 2;
        return state;
    }

    public static GameState AtSurface(int capacity = 24, int money = 100)
    {
        var state = GameStateFactory.Create(Grid(), Config(capacity, money));
        state.MinerX = Grid().ShaftX;
        state.MinerY = Grid().SurfaceY;
        return state;
    }

    public static void FillBackpack(GameState state, MineralType mineral, int amount)
    {
        state.Backpack.Items.Add(new BackpackItem { Mineral = mineral, Amount = amount });
        state.Backpack.Used += amount;
    }
}
