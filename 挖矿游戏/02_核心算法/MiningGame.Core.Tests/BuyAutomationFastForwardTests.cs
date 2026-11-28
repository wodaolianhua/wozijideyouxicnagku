using MiningGame.Contracts;
using Xunit;

namespace MiningGame.Core.Tests;

public class BuyAutomationFastForwardTests
{
    [Fact]
    public void BuyTool_NotEnoughMoney_NoDeduction()
    {
        var state = TestWorld.State(money: 10);
        var result = new MiningSimulator().Advance(
            state, new[] { new GameAction { Kind = ActionKind.BuyTool, Tool = ToolKind.Pickaxe } }, 1, 1);

        Assert.Equal(AdvanceResultCode.NotEnoughMoney, result.Code);
        Assert.Equal(10, result.State.Money);
        Assert.DoesNotContain(ToolKind.Pickaxe, result.State.OwnedTools);
    }

    [Fact]
    public void BuyTool_EnoughMoney_DeductsAndEquips()
    {
        var state = TestWorld.State(money: 100);
        var result = new MiningSimulator().Advance(
            state, new[] { new GameAction { Kind = ActionKind.BuyTool, Tool = ToolKind.Pickaxe } }, 1, 1);

        Assert.Equal(70, result.State.Money);
        Assert.Equal(ToolKind.Pickaxe, result.State.CurrentTool);
        Assert.Contains(ToolKind.Pickaxe, result.State.OwnedTools);
    }

    [Fact]
    public void HireWorker_DeductsAndAddsWorker()
    {
        var state = TestWorld.State(money: 100);
        var result = new MiningSimulator().Advance(
            state, new[] { new GameAction { Kind = ActionKind.HireWorker, Worker = WorkerKind.Robot } }, 1, 1);

        Assert.Equal(50, result.State.Money);
        Assert.Single(result.State.Workers);
        Assert.Equal(3, result.State.Workers[0].Power);
    }

    [Fact]
    public void GoSurface_TeleportsAndRestoresStamina()
    {
        var state = TestWorld.State();
        state.Stamina = 3;
        state.MinerX = 2;
        state.MinerY = 4;

        var result = new MiningSimulator().Advance(
            state, new[] { new GameAction { Kind = ActionKind.GoSurface } }, 1, 1);

        Assert.Equal(TestWorld.Grid().ShaftX, result.State.MinerX);
        Assert.Equal(TestWorld.Grid().SurfaceY, result.State.MinerY);
        Assert.Equal(40, result.State.Stamina);
    }

    [Fact]
    public void Automation_ProducesOnInterval()
    {
        var state = TestWorld.State();
        state.Workers.Add(new Worker
        {
            Id = "W1", Kind = WorkerKind.Robot, X = 3, Y = 2,
            Power = 3, Interval = 1, Wage = 0,
        });

        var result = new MiningSimulator().Advance(state, Array.Empty<GameAction>(), 1, 5);

        Assert.Equal(1, result.State.Backpack.Used);
        Assert.Equal(MineralType.Iron, result.State.Backpack.Items[0].Mineral);
    }

    [Fact]
    public void Automation_WaitsForInterval()
    {
        var state = TestWorld.State();
        state.Workers.Add(new Worker
        {
            Id = "W1", Kind = WorkerKind.HiredMiner, X = 3, Y = 2,
            Power = 3, Interval = 3, Wage = 1,
        });

        // Time=0 时 0 % 3 == 0 会产出；进入 Time=1/2 后不应产出
        var first = new MiningSimulator().Advance(state, Array.Empty<GameAction>(), 3, 5);
        Assert.True(first.State.Time == 3);
        Assert.Equal(1, first.State.Backpack.Used); // 只有第 1 步产出一次
    }

    [Fact]
    public void FastForward_StopsWhenBackpackFull()
    {
        var state = TestWorld.State(capacity: 2);
        state.CurrentTool = ToolKind.Pickaxe;

        var result = new MiningSimulator().FastForward(
            state,
            new[] { new GameAction { Kind = ActionKind.Dig, Direction = Direction.Down } },
            maxSteps: 50, seed: 3);

        Assert.Equal(2, result.State.Backpack.Used);
        Assert.Equal(2, result.Outcomes.Count);
        Assert.True(result.State.Stamina < 40);
    }

    [Fact]
    public void FastForward_StopsWhenStaminaExhausted()
    {
        var state = TestWorld.State();
        state.CurrentTool = ToolKind.Pickaxe;
        state.Stamina = 3;

        var result = new MiningSimulator().FastForward(
            state,
            new[] { new GameAction { Kind = ActionKind.Dig, Direction = Direction.Down } },
            maxSteps: 50, seed: 3);

        Assert.Equal(1, result.State.Backpack.Used);
        Assert.Equal(0, result.State.Stamina);
        Assert.Single(result.Outcomes);
    }
}
