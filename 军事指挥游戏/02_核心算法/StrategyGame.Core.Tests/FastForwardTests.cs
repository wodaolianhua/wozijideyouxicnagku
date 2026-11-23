using StrategyGame.Contracts;
using Xunit;

namespace StrategyGame.Core.Tests;

public sealed class FastForwardTests
{
    private readonly MarchSimulator _sim = new();

    [Fact]
    public void FastForward_NoEncounter_StopsAtArrival()
    {
        var state = TestWorld.BuildState(TestWorld.Army("P", Faction.Player, "N0"));
        var orders = new[] { new Order { ArmyId = "P", Kind = OrderKind.MoveTo, Waypoints = { "N1" } } };

        var result = _sim.FastForward(state, orders, maxSteps: 10, seed: 1);

        Assert.Equal(ResolveResultCode.Ok, result.Code);
        Assert.Empty(result.Encounters);
        Assert.Equal(2, result.State.Time);
        Assert.Equal("N1", result.State.Armies.Single().NodeId);
        Assert.Equal(0, result.State.Armies.Single().EdgeProgress, 6);
    }

    [Fact]
    public void FastForward_Encounter_StopsAtContactStepWithLoss()
    {
        var state = TestWorld.BuildState(
            TestWorld.Army("P", Faction.Player, "N0"),
            TestWorld.Army("E", Faction.Enemy, "N1"));
        var orders = new[] { new Order { ArmyId = "P", Kind = OrderKind.MoveTo, Waypoints = { "N2" } } };

        var result = _sim.FastForward(state, orders, maxSteps: 10, seed: 1);

        var encounter = Assert.Single(result.Encounters);
        Assert.Equal(2, result.State.Time);
        Assert.Equal("N1", encounter.NodeId);
        Assert.Equal(2, encounter.Loss.LossByArmy.Count);
        Assert.True(result.State.Armies.All(a => a.Strength < 1000));
    }

    [Fact]
    public void FastForward_NoNewOrders_ContinuesPlannedPathToEnd()
    {
        var state = TestWorld.BuildState(
            TestWorld.Army("P", Faction.Player, "N0").WithPath("N0", "N1", "N2"));

        var result = _sim.FastForward(state, Array.Empty<Order>(), maxSteps: 10, seed: 1);

        Assert.Empty(result.Encounters);
        Assert.Equal(6, result.State.Time);
        Assert.Equal("N2", result.State.Armies.Single().NodeId);
    }

    [Fact]
    public void FastForward_StopsAtMaxStepsWhenStillMarching()
    {
        var state = TestWorld.BuildState(
            TestWorld.Army("P", Faction.Player, "N0"),
            TestWorld.Army("E", Faction.Enemy, "N4"));
        var orders = new[] { new Order { ArmyId = "P", Kind = OrderKind.MoveTo, Waypoints = { "N2" } } };

        var result = _sim.FastForward(state, orders, maxSteps: 3, seed: 1);

        Assert.Empty(result.Encounters);
        Assert.Equal(3, result.State.Time);
        Assert.Equal(0.25, result.State.Armies[0].EdgeProgress, 6);
    }
}
