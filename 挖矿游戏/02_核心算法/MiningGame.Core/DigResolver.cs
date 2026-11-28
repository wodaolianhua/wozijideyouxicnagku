using MiningGame.Contracts;

namespace MiningGame.Core;

public sealed class DigResolver : IDigResolver
{
    private readonly IBackpackPolicy _backpack;

    public DigResolver(IBackpackPolicy? backpack = null) => _backpack = backpack ?? new BackpackPolicy();

    public DigResult Dig(GameState state, int x, int y, int seed)
    {
        var result = new DigResult { X = x, Y = y };

        if (!GridUtil.InBounds(state.Grid, x, y))
        {
            result.Code = DigResultCode.OutOfBounds;
            return result;
        }

        var cell = GridUtil.CellAt(state.Grid, x, y);
        if (cell == null)
        {
            result.Code = DigResultCode.OutOfBounds;
            return result;
        }

        if (cell.Tile == TileType.Air)
        {
            result.Code = DigResultCode.Empty;
            return result;
        }

        if (cell.Tile == TileType.Bedrock || cell.Hardness >= 99)
        {
            result.Code = DigResultCode.NotDiggable;
            return result;
        }

        int power = ToolPower(state.ToolShop, state.CurrentTool);
        if (cell.Hardness > power)
        {
            result.Code = DigResultCode.ToolTooWeak;
            return result;
        }

        bool isOre = cell.Tile == TileType.Ore;
        if (isOre && !_backpack.CanAdd(state.Backpack, 1))
        {
            result.Code = DigResultCode.BackpackFull;
            return result;
        }

        result.Code = DigResultCode.Ok;
        result.Mineral = isOre ? cell.Mineral : MineralType.Soil;
        result.Amount = isOre ? 1 : 0;             // 土/石头只开路，不进背包
        result.StaminaCost = Math.Max(1, cell.Hardness);
        return result;
    }

    public static int ToolPower(IEnumerable<ToolSpec> shop, ToolKind kind)
    {
        var spec = shop.FirstOrDefault(t => t.Kind == kind);
        if (spec != null) return spec.Power;
        return kind switch
        {
            ToolKind.BareHands => 1,
            ToolKind.Pickaxe => 3,
            ToolKind.Drill => 6,
            ToolKind.Excavator => 8,
            _ => 1,
        };
    }
}
