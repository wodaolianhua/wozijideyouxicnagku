using StrategyGame.Contracts;
using Xunit;

namespace StrategyGame.Core.Tests;

public sealed class PathFinderTests
{
    private readonly PathFinder _finder = new();

    [Fact]
    public void FindPath_PicksShortestByWeightedCost()
    {
        var path = _finder.FindPath(TestWorld.BuildMap(), "N0", "N3");

        Assert.Equal(new[] { "N0", "N1", "N2", "N3" }, path);
    }

    [Fact]
    public void FindPath_PrefersCheapDetourOverExpensiveShortcut()
    {
        // 直连 N0-N2 是山路、代价 20；绕 N1 只花 6
        var path = _finder.FindPath(TestWorld.BuildMap(), "N0", "N2");

        Assert.Equal(new[] { "N0", "N1", "N2" }, path);
    }

    [Fact]
    public void FindPath_Unreachable_ReturnsEmpty()
    {
        var path = _finder.FindPath(TestWorld.BuildMap(), "N0", "N6");

        Assert.Empty(path);
    }

    [Fact]
    public void FindPath_UnknownNode_ReturnsEmpty()
    {
        Assert.Empty(_finder.FindPath(TestWorld.BuildMap(), "N0", "NOPE"));
        Assert.Empty(_finder.FindPath(TestWorld.BuildMap(), "NOPE", "N0"));
    }

    [Fact]
    public void FindPath_FromEqualsTo_ReturnsSingleNode()
    {
        Assert.Equal(new[] { "N0" }, _finder.FindPath(TestWorld.BuildMap(), "N0", "N0"));
    }

    [Fact]
    public void FindPathVia_SplicesSegmentsWithoutDuplicatingJunctions()
    {
        var path = _finder.FindPathVia(TestWorld.BuildMap(), "N0", new[] { "N2", "N4" });

        Assert.Equal(new[] { "N0", "N1", "N2", "N5", "N4" }, path);
    }

    [Fact]
    public void FindPathVia_UnreachableWaypoint_ReturnsEmpty()
    {
        var path = _finder.FindPathVia(TestWorld.BuildMap(), "N0", new[] { "N2", "N6" });

        Assert.Empty(path);
    }

    [Fact]
    public void FindPathVia_NoWaypoints_ReturnsStartOnly()
    {
        Assert.Equal(new[] { "N0" }, _finder.FindPathVia(TestWorld.BuildMap(), "N0", Array.Empty<string>()));
    }
}
