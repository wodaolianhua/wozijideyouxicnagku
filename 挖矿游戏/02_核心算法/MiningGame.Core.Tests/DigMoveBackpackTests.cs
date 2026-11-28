using MiningGame.Contracts;
using Xunit;

namespace MiningGame.Core.Tests;

public class DigMoveBackpackTests
{
    [Fact]
    public void Move_IntoAir_Ok()
    {
        var state = TestWorld.State();
        var code = new MovementResolver().CanMove(state, Direction.Up);
        Assert.Equal(MoveResultCode.Ok, code);
    }

    [Fact]
    public void Move_IntoSolid_Blocked()
    {
        var state = TestWorld.State();
        Assert.Equal(MoveResultCode.Blocked, new MovementResolver().CanMove(state, Direction.Down));
        Assert.Equal(MoveResultCode.Blocked, new MovementResolver().CanMove(state, Direction.Right));
    }

    [Fact]
    public void Move_OutOfBounds()
    {
        var state = TestWorld.State();
        state.MinerX = 0;
        state.MinerY = 0;
        Assert.Equal(MoveResultCode.OutOfBounds, new MovementResolver().CanMove(state, Direction.Left));
    }

    [Fact]
    public void Dig_Bedrock_NotDiggable()
    {
        var state = TestWorld.State();
        var result = new DigResolver().Dig(state, 7, 3, 1);
        Assert.Equal(DigResultCode.NotDiggable, result.Code);
    }

    [Fact]
    public void Dig_Air_Empty()
    {
        var state = TestWorld.State();
        Assert.Equal(DigResultCode.Empty, new DigResolver().Dig(state, 3, 2, 1).Code);
    }

    [Fact]
    public void Dig_OutOfBounds()
    {
        var state = TestWorld.State();
        Assert.Equal(DigResultCode.OutOfBounds, new DigResolver().Dig(state, -1, 0, 1).Code);
        Assert.Equal(DigResultCode.OutOfBounds, new DigResolver().Dig(state, 99, 99, 1).Code);
    }

    [Fact]
    public void Dig_WeakTool_ToolTooWeak()
    {
        var state = TestWorld.State();
        // 徒手 power=1，铁矿 hardness=3
        Assert.Equal(DigResultCode.ToolTooWeak, new DigResolver().Dig(state, 3, 3, 1).Code);
    }

    [Fact]
    public void Dig_BackpackFull()
    {
        var state = TestWorld.State(capacity: 1);
        state.CurrentTool = ToolKind.Pickaxe;
        state.Backpack.Items.Add(new BackpackItem { Mineral = MineralType.Coal, Amount = 1 });
        state.Backpack.Used = 1;

        Assert.Equal(DigResultCode.BackpackFull, new DigResolver().Dig(state, 3, 3, 1).Code);
    }

    [Fact]
    public void Dig_Ore_GetsMineral_And_DecrementsToAir()
    {
        var state = TestWorld.State();
        state.CurrentTool = ToolKind.Pickaxe;
        var sim = new MiningSimulator();

        var dig = new GameAction { Kind = ActionKind.Dig, Direction = Direction.Down };
        var result = sim.Advance(state, new[] { dig, dig, dig }, 3, 42);

        Assert.Equal(1, result.State.Backpack.Items.Count);
        Assert.Equal(MineralType.Iron, result.State.Backpack.Items[0].Mineral);
        Assert.Equal(3, result.State.Backpack.Items[0].Amount);

        var oreCell = result.State.Grid.Cells[3 * TestWorld.Width + 3];
        Assert.Equal(TileType.Air, oreCell.Tile);
    }

    [Fact]
    public void Dig_Dirt_OpensPath_WithoutBackpack()
    {
        var state = TestWorld.State();
        var sim = new MiningSimulator();

        var result = sim.Advance(state, new[] { new GameAction { Kind = ActionKind.Dig, Direction = Direction.Left } }, 1, 42);

        Assert.Empty(result.State.Backpack.Items);
        var dirtCell = result.State.Grid.Cells[2 * TestWorld.Width + 2];
        Assert.Equal(TileType.Air, dirtCell.Tile);
    }

    [Fact]
    public void Backpack_CanAdd_And_Valuation()
    {
        var policy = new BackpackPolicy();
        var backpack = new Backpack
        {
            Capacity = 5,
            Used = 3,
            Items = new List<BackpackItem>
            {
                new() { Mineral = MineralType.Iron, Amount = 2 },
                new() { Mineral = MineralType.Coal, Amount = 1 },
            },
        };

        Assert.True(policy.CanAdd(backpack, 2));
        Assert.False(policy.CanAdd(backpack, 3));

        var prices = TestWorld.Config().Prices;
        Assert.Equal(39, policy.Valuation(backpack, prices)); // 16*2 + 7
    }

    [Fact]
    public void Advance_DoesNotMutate_InputState()
    {
        var state = TestWorld.State();
        state.CurrentTool = ToolKind.Pickaxe;
        var sim = new MiningSimulator();

        sim.Advance(state, new[] { new GameAction { Kind = ActionKind.Dig, Direction = Direction.Down } }, 1, 42);

        Assert.Equal(0, state.Time);
        Assert.Equal(0, state.Backpack.Used);
        Assert.Equal(TileType.Ore, state.Grid.Cells[3 * TestWorld.Width + 3].Tile);
    }
}
