using MiningGame.Contracts;

namespace MiningGame.Core;

// 纯函数铁律：Advance 绝不改入参 state，所有推进都在深拷贝上做。
public static class GameStateCloner
{
    public static GameState Clone(GameState s)
    {
        return new GameState
        {
            Time = s.Time,
            Grid = new MineGrid
            {
                Width = s.Grid.Width,
                Height = s.Grid.Height,
                SurfaceY = s.Grid.SurfaceY,
                ShaftX = s.Grid.ShaftX,
                Cells = s.Grid.Cells.Select(c => new Cell
                {
                    X = c.X,
                    Y = c.Y,
                    Tile = c.Tile,
                    Mineral = c.Mineral,
                    Hardness = c.Hardness,
                    OreAmount = c.OreAmount,
                }).ToList(),
            },
            MinerX = s.MinerX,
            MinerY = s.MinerY,
            Stamina = s.Stamina,
            MaxStamina = s.MaxStamina,
            SurfaceStaminaRegen = s.SurfaceStaminaRegen,
            Money = s.Money,
            CurrentTool = s.CurrentTool,
            OwnedTools = s.OwnedTools.ToList(),
            Backpack = new Backpack
            {
                Capacity = s.Backpack.Capacity,
                Used = s.Backpack.Used,
                Items = s.Backpack.Items.Select(i => new BackpackItem { Mineral = i.Mineral, Amount = i.Amount }).ToList(),
            },
            Workers = s.Workers.Select(w => new Worker
            {
                Id = w.Id,
                Kind = w.Kind,
                X = w.X,
                Y = w.Y,
                Power = w.Power,
                Interval = w.Interval,
                Wage = w.Wage,
            }).ToList(),
            ToolShop = s.ToolShop.Select(t => new ToolSpec { Kind = t.Kind, Name = t.Name, Power = t.Power, Price = t.Price }).ToList(),
            WorkerShop = s.WorkerShop.Select(w => new WorkerSpec
            {
                Kind = w.Kind,
                Name = w.Name,
                Power = w.Power,
                Interval = w.Interval,
                Wage = w.Wage,
                Price = w.Price,
            }).ToList(),
            Prices = new Dictionary<int, int>(s.Prices),
            PolishedGoods = s.PolishedGoods.Select(p => new PolishedItem { Mineral = p.Mineral, Grade = p.Grade, Amount = p.Amount }).ToList(),
            PolishSettings = new PolishConfig
            {
                GradeWeights = s.PolishSettings.GradeWeights.ToList(),
                GradePricePercent = s.PolishSettings.GradePricePercent.ToList(),
                GradeBonusPerTier = s.PolishSettings.GradeBonusPerTier,
                UnitsPerAction = s.PolishSettings.UnitsPerAction,
                StaminaPerUnit = s.PolishSettings.StaminaPerUnit,
            },
        };
    }
}
