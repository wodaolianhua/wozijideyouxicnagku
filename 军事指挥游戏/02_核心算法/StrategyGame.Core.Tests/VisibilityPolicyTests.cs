using StrategyGame.Contracts;
using Xunit;

namespace StrategyGame.Core.Tests;

public sealed class VisibilityPolicyTests
{
    [Fact]
    public void IsEnemyVisible_FirstPhase_AlwaysTrue()
    {
        var state = TestWorld.BuildState(
            TestWorld.Army("P", Faction.Player, "N0"),
            TestWorld.Army("E", Faction.Enemy, "N6"));
        var policy = new AlwaysVisiblePolicy();

        Assert.True(policy.IsEnemyVisible(state.Armies[1], state.Armies[0], state));
        Assert.True(policy.IsEnemyVisible(state.Armies[0], state.Armies[1], state));
    }
}
