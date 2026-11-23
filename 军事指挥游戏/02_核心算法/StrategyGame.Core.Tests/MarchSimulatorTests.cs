using StrategyGame.Contracts;
using Xunit;

namespace StrategyGame.Core.Tests;

public sealed class MarchSimulatorTests
{
    private readonly MarchSimulator _sim = new();

    [Fact]
    public void Advance_MoveTo_WritesPlannedPathAndInterpolatesPosition()
    {
        var state = TestWorld.BuildState(TestWorld.Army("A", Faction.Player, "N0"));
        var orders = new[] { new Order { ArmyId = "A", Kind = OrderKind.MoveTo, Waypoints = { "N1" } } };

        var result = _sim.Advance(state, orders, 1, seed: 1);

        var army = result.State.Armies.Single();
        Assert.Equal(ResolveResultCode.Ok, result.Code);
        Assert.Equal(new[] { "N0", "N1" }, army.PlannedPath);
        Assert.Equal(0, army.PathIndex);
        Assert.Equal("N0", army.NodeId);
        Assert.Equal(0.5, army.EdgeProgress, 6);

        var trail = result.Trails.Single();
        Assert.Equal(2, trail.Points.Count); // 起点 + 本步末位置
        Assert.Equal(0, trail.Points[0].Time);
        Assert.Equal(0, trail.Points[0].X, 6);
        Assert.Equal(5, trail.Points[1].X, 6);
        Assert.Equal(1, trail.Points[1].Time);
    }

    [Fact]
    public void Advance_ContinuesExistingPlannedPath_WithoutNewOrders()
    {
        var army = TestWorld.Army("A", Faction.Player, "N0").WithPath("N0", "N1", "N2");
        var state = TestWorld.BuildState(army);

        var result = _sim.Advance(state, Array.Empty<Order>(), 3, seed: 1);

        var moved = result.State.Armies.Single();
        Assert.Equal("N1", moved.NodeId);
        Assert.Equal(1, moved.PathIndex);
        Assert.Equal(0.25, moved.EdgeProgress, 6); // N1→N2 走了 25%，即 (12.5, 0)
    }

    [Fact]
    public void Advance_DoesNotMutateInputState()
    {
        var army = TestWorld.Army("A", Faction.Player, "N0");
        var state = TestWorld.BuildState(army);
        var orders = new[] { new Order { ArmyId = "A", Kind = OrderKind.MoveTo, Waypoints = { "N1" } } };

        var result = _sim.Advance(state, orders, 2, seed: 1);

        Assert.Equal(0, state.Time);
        Assert.Empty(state.Armies.Single().PlannedPath);
        Assert.Equal("N0", state.Armies.Single().NodeId);
        Assert.Equal(2, result.State.Time);
        Assert.Equal("N1", result.State.Armies.Single().NodeId);
    }

    [Fact]
    public void Advance_TerrainAndUnitTypeAffectProgress()
    {
        var infantry = TestWorld.Army("I", Faction.Player, "N2").WithPath("N2", "N3");
        var cavalry = TestWorld.Army("C", Faction.Player, "N2", unitType: UnitType.Cavalry).WithPath("N2", "N3");
        var state = TestWorld.BuildState(infantry, cavalry);

        var result = _sim.Advance(state, Array.Empty<Order>(), 1, seed: 1);

        // N2-N3 道路 6 × 丘陵 1.25 = 7.5 步；步兵 1 步走 0.1333，骑兵走 0.24
        Assert.Equal(1.0 / 7.5, result.State.Armies[0].EdgeProgress, 6);
        Assert.Equal(1.8 / 7.5, result.State.Armies[1].EdgeProgress, 6);
    }

    [Fact]
    public void Advance_CavalryArrivesBeforeInfantry()
    {
        var infantry = TestWorld.Army("I", Faction.Player, "N2").WithPath("N2", "N3");
        var cavalry = TestWorld.Army("C", Faction.Player, "N2", unitType: UnitType.Cavalry).WithPath("N2", "N3");
        var state = TestWorld.BuildState(infantry, cavalry);

        var result = _sim.Advance(state, Array.Empty<Order>(), 5, seed: 1);

        Assert.Equal("N3", result.State.Armies[1].NodeId);
        Assert.Equal("N2", result.State.Armies[0].NodeId);
        Assert.True(result.State.Armies[0].EdgeProgress < 1.0);
    }

    [Fact]
    public void Advance_Hold_StopsArmyAtNode()
    {
        var army = TestWorld.Army("A", Faction.Player, "N1");
        var state = TestWorld.BuildState(army);
        var orders = new[] { new Order { ArmyId = "A", Kind = OrderKind.Hold } };

        var result = _sim.Advance(state, orders, 3, seed: 1);

        var moved = result.State.Armies.Single();
        Assert.Empty(moved.PlannedPath);
        Assert.Equal("N1", moved.NodeId);
        Assert.Equal(0, moved.EdgeProgress, 6);
        Assert.Equal(1, result.State.Time); // 全军待命：只过 1 步就提前收手，不空转
    }

    [Fact]
    public void Advance_MoveToUnknownTarget_ReturnsInvalidTarget()
    {
        var state = TestWorld.BuildState(TestWorld.Army("A", Faction.Player, "N0"));
        var orders = new[] { new Order { ArmyId = "A", Kind = OrderKind.MoveTo, Waypoints = { "NOPE" } } };

        var result = _sim.Advance(state, orders, 1, seed: 1);

        Assert.Equal(ResolveResultCode.InvalidTarget, result.Code);
        Assert.Equal("N0", result.State.Armies.Single().NodeId);
    }

    [Fact]
    public void Advance_MoveToUnreachable_ReturnsNoPath()
    {
        var state = TestWorld.BuildState(TestWorld.Army("A", Faction.Player, "N0"));
        var orders = new[] { new Order { ArmyId = "A", Kind = OrderKind.MoveTo, Waypoints = { "N6" } } };

        var result = _sim.Advance(state, orders, 1, seed: 1);

        Assert.Equal(ResolveResultCode.NoPath, result.Code);
        Assert.Empty(result.State.Armies.Single().PlannedPath);
    }

    [Fact]
    public void Advance_MoveToCurrentNode_ReturnsAlreadyArrived()
    {
        var state = TestWorld.BuildState(TestWorld.Army("A", Faction.Player, "N1"));
        var orders = new[] { new Order { ArmyId = "A", Kind = OrderKind.MoveTo, Waypoints = { "N1" } } };

        var result = _sim.Advance(state, orders, 1, seed: 1);

        Assert.Equal(ResolveResultCode.AlreadyArrived, result.Code);
        Assert.Equal(new[] { "N1" }, result.State.Armies.Single().PlannedPath);
    }

    [Fact]
    public void Advance_MoveVia_VisitsWaypointsInOrder()
    {
        var state = TestWorld.BuildState(TestWorld.Army("A", Faction.Player, "N0"));
        var orders = new[]
        {
            new Order { ArmyId = "A", Kind = OrderKind.MoveVia, Waypoints = { "N2", "N4" } },
        };

        var result = _sim.Advance(state, orders, 1, seed: 1);

        Assert.Equal(new[] { "N0", "N1", "N2", "N5", "N4" }, result.State.Armies.Single().PlannedPath);
    }

    [Fact]
    public void Advance_IsDeterministicForSameInput()
    {
        var state = TestWorld.BuildState(TestWorld.Army("A", Faction.Player, "N0").WithPath("N0", "N1", "N2"));

        var first = _sim.Advance(state, Array.Empty<Order>(), 4, seed: 7);
        var second = _sim.Advance(state, Array.Empty<Order>(), 4, seed: 7);

        Assert.Equal(first.State.Time, second.State.Time);
        Assert.Equal(first.State.Armies[0].EdgeProgress, second.State.Armies[0].EdgeProgress, 9);
        Assert.Equal(first.Trails[0].Points.Count, second.Trails[0].Points.Count);
    }
}
