using StrategyGame.Contracts;
using Xunit;

namespace StrategyGame.Core.Tests;

public sealed class EncounterTests
{
    private readonly MarchSimulator _sim = new();
    private readonly EncounterDetector _detector = new();

    [Fact]
    public void Detect_SameNodeHostile_TriggersWithLoss()
    {
        var state = TestWorld.BuildState(
            TestWorld.Army("P", Faction.Player, "N1"),
            TestWorld.Army("E", Faction.Enemy, "N1"));

        var events = _detector.Detect(state, seed: 1);

        var encounter = Assert.Single(events);
        Assert.Equal(0, encounter.Time);
        Assert.Equal("N1", encounter.NodeId);
        Assert.Contains("P", encounter.ArmyIds);
        Assert.Contains("E", encounter.ArmyIds);
        Assert.Equal(2, encounter.Loss.LossByArmy.Count);
        Assert.All(encounter.Loss.LossByArmy.Values, v => Assert.True(v > 0));
        Assert.False(encounter.IsAmbush);
    }

    [Fact]
    public void Detect_FarApart_DoesNotTrigger()
    {
        var state = TestWorld.BuildState(
            TestWorld.Army("P", Faction.Player, "N1"),
            TestWorld.Army("E", Faction.Enemy, "N3"));

        Assert.Empty(_detector.Detect(state, seed: 1));
    }

    [Fact]
    public void Detect_SameFaction_DoesNotTrigger()
    {
        var state = TestWorld.BuildState(
            TestWorld.Army("P1", Faction.Player, "N1"),
            TestWorld.Army("P2", Faction.Player, "N1"));

        Assert.Empty(_detector.Detect(state, seed: 1));
    }

    [Fact]
    public void Advance_HeadOnOnSameEdge_TriggersDuringStep()
    {
        var player = TestWorld.Army("P", Faction.Player, "N0").WithPath("N0", "N1");
        var enemy = TestWorld.Army("E", Faction.Enemy, "N1").WithPath("N1", "N0");
        var state = TestWorld.BuildState(player, enemy);

        var result = _sim.Advance(state, Array.Empty<Order>(), 1, seed: 1);

        var encounter = Assert.Single(result.Encounters);
        Assert.Equal(1, encounter.Time);
        Assert.Contains("P", encounter.ArmyIds);
        Assert.Contains("E", encounter.ArmyIds);
        Assert.Equal(2, encounter.Loss.LossByArmy.Count);
        Assert.True(result.State.Armies.All(a => a.Strength < 1000));
    }

    [Fact]
    public void Advance_PassingSameNodeAtDifferentSteps_DoesNotTrigger()
    {
        // P 第 2 步经过 N1 后离开；E 第 16 步才抵达 N1 —— 同一节点、时间错开，不该误触
        var player = TestWorld.Army("P", Faction.Player, "N0").WithPath("N0", "N1", "N2", "N7");
        var enemy = TestWorld.Army("E", Faction.Enemy, "N4").WithPath("N4", "N1");
        var state = TestWorld.BuildState(player, enemy);

        var result = _sim.Advance(state, Array.Empty<Order>(), 20, seed: 1);

        Assert.Empty(result.Encounters);
        Assert.Equal("N7", result.State.Armies[0].NodeId);
        Assert.Equal("N1", result.State.Armies[1].NodeId);
    }

    [Fact]
    public void Advance_IdleEnemyOnMountain_FlagsAmbush()
    {
        var player = TestWorld.Army("P", Faction.Player, "N4").WithPath("N4", "N5");
        var enemy = TestWorld.Army("E", Faction.Enemy, "N5");
        var state = TestWorld.BuildState(player, enemy);

        var result = _sim.Advance(state, Array.Empty<Order>(), 10, seed: 1);

        var encounter = Assert.Single(result.Encounters);
        Assert.Equal(10, encounter.Time);
        Assert.Equal("N5", encounter.NodeId);
        Assert.True(encounter.IsAmbush);
        Assert.Equal("E", encounter.AmbusherId);
        Assert.Equal(148, encounter.Loss.LossByArmy["P"]); // 被伏击方吃 1.6 倍
        Assert.Equal(40, encounter.Loss.LossByArmy["E"]);  // 伏击方损失减半
    }

    [Fact]
    public void Advance_MeetingOnPlain_IsNotAmbush()
    {
        var player = TestWorld.Army("P", Faction.Player, "N0").WithPath("N0", "N1", "N2");
        var enemy = TestWorld.Army("E", Faction.Enemy, "N2");
        var state = TestWorld.BuildState(player, enemy);

        var result = _sim.Advance(state, Array.Empty<Order>(), 7, seed: 1);

        var encounter = Assert.Single(result.Encounters);
        Assert.Equal("N2", encounter.NodeId);
        Assert.False(encounter.IsAmbush);
        Assert.Equal(string.Empty, encounter.AmbusherId);
    }
}
