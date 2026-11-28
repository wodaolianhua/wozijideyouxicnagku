using MiningGame.Contracts;

namespace MiningGame.Core;

public sealed class MovementResolver : IMovementResolver
{
    public MoveResultCode CanMove(GameState state, Direction direction)
    {
        var (dx, dy) = DirectionDelta(direction);
        int tx = state.MinerX + dx;
        int ty = state.MinerY + dy;

        if (!GridUtil.InBounds(state.Grid, tx, ty)) return MoveResultCode.OutOfBounds;

        var cell = GridUtil.CellAt(state.Grid, tx, ty);
        if (cell == null) return MoveResultCode.OutOfBounds;

        return cell.Tile == TileType.Air ? MoveResultCode.Ok : MoveResultCode.Blocked;
    }

    public static (int dx, int dy) DirectionDelta(Direction direction) => direction switch
    {
        Direction.Up => (0, -1),
        Direction.Down => (0, 1),
        Direction.Left => (-1, 0),
        Direction.Right => (1, 0),
        _ => (0, 0),
    };
}
