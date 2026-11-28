using MiningGame.Contracts;

namespace MiningGame.Core;

internal static class GridUtil
{
    public static Cell? CellAt(MineGrid grid, int x, int y)
    {
        if (x < 0 || y < 0 || x >= grid.Width || y >= grid.Height) return null;
        int index = y * grid.Width + x;
        if (index < 0 || index >= grid.Cells.Count) return null;
        return grid.Cells[index];
    }

    public static bool InBounds(MineGrid grid, int x, int y) =>
        x >= 0 && y >= 0 && x < grid.Width && y < grid.Height;
}
