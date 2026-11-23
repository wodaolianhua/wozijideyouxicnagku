using StrategyGame.Contracts;
using Xunit;

namespace StrategyGame.Core.Tests;

public sealed class LossResolverTests
{
    private readonly LossResolver _resolver = new();

    [Fact]
    public void Resolve_EqualStrengthOnPlain_EqualLosses()
    {
        var state = TestWorld.BuildState(
            TestWorld.Army("P", Faction.Player, "N2"),
            TestWorld.Army("E", Faction.Enemy, "N2"));
        var encounter = new EncounterEvent
        {
            Time = 0,
            NodeId = "N2",
            ArmyIds = { "P", "E" },
        };

        var loss = _resolver.Resolve(encounter, state, seed: 1);

        Assert.Equal(120, loss.LossByArmy["P"]);
        Assert.Equal(120, loss.LossByArmy["E"]);
    }

    [Fact]
    public void Resolve_AmbushedSideLosesMore()
    {
        var player = TestWorld.Army("P", Faction.Player, "N4").WithPath("N4", "N5");
        player.EdgeProgress = 0.5;
        var enemy = TestWorld.Army("E", Faction.Enemy, "N5");
        var state = TestWorld.BuildState(player, enemy);
        var encounter = new EncounterEvent
        {
            Time = 1,
            NodeId = "N5",
            ArmyIds = { "P", "E" },
            IsAmbush = true,
            AmbusherId = "E",
        };

        var loss = _resolver.Resolve(encounter, state, seed: 1);

        Assert.True(loss.LossByArmy["P"] > loss.LossByArmy["E"]);
        Assert.True(loss.LossByArmy["E"] > 0);
    }

    [Fact]
    public void Resolve_LossIsClampedToOwnStrength()
    {
        var state = TestWorld.BuildState(
            TestWorld.Army("P", Faction.Player, "N2", strength: 10),
            TestWorld.Army("E", Faction.Enemy, "N2", strength: 1000));
        var encounter = new EncounterEvent
        {
            Time = 0,
            NodeId = "N2",
            ArmyIds = { "P", "E" },
        };

        var loss = _resolver.Resolve(encounter, state, seed: 1);

        Assert.Equal(10, loss.LossByArmy["P"]);
        Assert.True(loss.LossByArmy["E"] >= 0);
    }

    [Fact]
    public void Resolve_IsDeterministic()
    {
        var state = TestWorld.BuildState(
            TestWorld.Army("P", Faction.Player, "N5"),
            TestWorld.Army("E", Faction.Enemy, "N5", unitType: UnitType.Cavalry));
        var encounter = new EncounterEvent
        {
            Time = 3,
            NodeId = "N5",
            ArmyIds = { "P", "E" },
        };

        var first = _resolver.Resolve(encounter, state, seed: 42);
        var second = _resolver.Resolve(encounter, state, seed: 42);

        Assert.Equal(first.LossByArmy["P"], second.LossByArmy["P"]);
        Assert.Equal(first.LossByArmy["E"], second.LossByArmy["E"]);
    }
}
