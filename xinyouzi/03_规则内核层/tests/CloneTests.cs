// ============================================================================
//  Clone 深拷贝测试
// ============================================================================
//  验收标准：克隆后修改副本，原对象不受影响。
//  这是存档、回滚、"两次模拟对比" 的共同前提。
//  注意：契约的 Clone() 只保证字段级深拷贝，
//  因此测试必须覆盖「嵌套对象」与「集合元素」两层，而不只是顶层字段。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Core.Sample;
using Xunit;

namespace StrategyGame.Core.Tests;

/// <summary>
/// <see cref="WorldState.Clone"/> 深拷贝验证。
/// </summary>
public sealed class CloneTests
{
    /// <summary>克隆后的副本不得与原对象共享任何子对象引用。</summary>
    [Fact]
    public void Clone_ShouldNotShareReferences_WithOriginal()
    {
        var original = SampleData.CreateWorld();
        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.NotSame(original.Player, clone.Player);

        foreach (Faction f in original.Factions)
        {
            Faction clonedFaction = clone.GetFaction(f.Id)!;
            Assert.NotSame(f, clonedFaction);
            Assert.NotSame(f.Resources, clonedFaction.Resources);
        }

        foreach (City c in original.Cities)
        {
            Assert.NotSame(c, clone.GetCity(c.Id));
        }

        foreach (Army a in original.Armies)
        {
            Assert.NotSame(a, clone.GetArmy(a.Id));
        }

        foreach (Officer o in original.Officers)
        {
            Assert.NotSame(o, clone.GetOfficer(o.Id));
        }
    }

    /// <summary>修改副本的势力资源，原对象不受影响。</summary>
    [Fact]
    public void Clone_ModifyFactionResources_ShouldNotAffectOriginal()
    {
        var original = SampleData.CreateWorld();
        int originalGrain = original.GetFaction(0)!.Resources.Grain;

        var clone = original.Clone();
        clone.GetFaction(0)!.Resources.Grain = 999999;

        Assert.Equal(originalGrain, original.GetFaction(0)!.Resources.Grain);
    }

    /// <summary>修改副本的城池属性，原对象不受影响。</summary>
    [Fact]
    public void Clone_ModifyCity_ShouldNotAffectOriginal()
    {
        var original = SampleData.CreateWorld();
        int originalDefense = original.GetCity(0)!.Defense;

        var clone = original.Clone();
        clone.GetCity(0)!.Defense = 999;

        Assert.Equal(originalDefense, original.GetCity(0)!.Defense);
    }

    /// <summary>修改副本的相邻城池集合，原对象不受影响。</summary>
    /// <remarks>
    /// 这一条最容易漏：List&lt;int&gt; 是引用类型，
    /// 若 Clone 用浅拷贝，Add 会直接污染原对象的邻接表，出击校验随之出错。
    /// </remarks>
    [Fact]
    public void Clone_ModifyAdjacencyList_ShouldNotAffectOriginal()
    {
        var original = SampleData.CreateWorld();
        int originalAdjacencyCount = original.GetCity(0)!.AdjacentCityIds.Count;

        var clone = original.Clone();
        clone.GetCity(0)!.AdjacentCityIds.Add(99);
        clone.GetCity(0)!.AdjacentCityIds.Clear();

        Assert.Equal(originalAdjacencyCount, original.GetCity(0)!.AdjacentCityIds.Count);
        Assert.Contains(1, original.GetCity(0)!.AdjacentCityIds);
    }

    /// <summary>修改副本的部队属性，原对象不受影响。</summary>
    [Fact]
    public void Clone_ModifyArmy_ShouldNotAffectOriginal()
    {
        var original = SampleData.CreateWorld();
        int originalSoldiers = original.GetArmy(0)!.Soldiers;

        var clone = original.Clone();
        clone.GetArmy(0)!.Soldiers = 0;
        clone.GetArmy(0)!.Stance = ArmyStance.Besieging;

        Assert.Equal(originalSoldiers, original.GetArmy(0)!.Soldiers);
        Assert.Equal(ArmyStance.Idle, original.GetArmy(0)!.Stance);
    }

    /// <summary>修改副本的人才属性，原对象不受影响。</summary>
    [Fact]
    public void Clone_ModifyOfficer_ShouldNotAffectOriginal()
    {
        var original = SampleData.CreateWorld();
        int originalLoyalty = original.GetOfficer(0)!.Loyalty;

        var clone = original.Clone();
        clone.GetOfficer(0)!.Loyalty = 0;
        clone.GetOfficer(0)!.Post = PostType.General;

        Assert.Equal(originalLoyalty, original.GetOfficer(0)!.Loyalty);
        Assert.Equal(PostType.None, original.GetOfficer(0)!.Post);
    }

    /// <summary>修改副本的延迟指令队列，原对象不受影响。</summary>
    [Fact]
    public void Clone_ModifyPendingOrders_ShouldNotAffectOriginal()
    {
        var original = SampleData.CreateWorld();
        original.PendingOrders.Add(new PendingOrder
        {
            OrderId = 1,
            ActorType = ActorType.Player,
            ActorId = 0,
            Action = ActionType.Recruit,
            TargetId = 0,
            Amount = 10,
            ExecuteYear = 1,
            ExecuteMonth = 3,
        });

        var clone = original.Clone();
        clone.PendingOrders.Clear();

        // 原对象的队列不受影响，副本的已被清空。
        Assert.Single(original.PendingOrders);
        Assert.Empty(clone.PendingOrders);
        Assert.Equal(10, original.PendingOrders[0].Amount);
        Assert.Equal(ActionType.Recruit, original.PendingOrders[0].Action);
    }

    /// <summary>修改副本的事件日志，原对象不受影响。</summary>
    [Fact]
    public void Clone_ModifyEventLog_ShouldNotAffectOriginal()
    {
        var original = SampleData.CreateWorld();
        original.EventLog.Add(new EventRecord
        {
            Year = 1, Month = 1, Category = EventCategory.Economy,
            Message = "测试事件", EventKey = "ev.test", FactionId = 0,
        });

        var clone = original.Clone();
        clone.EventLog[0].Message = "被篡改";

        Assert.Equal("测试事件", original.EventLog[0].Message);
    }

    /// <summary>未做任何修改时，克隆应与原状态逐字段相等。</summary>
    [Fact]
    public void Clone_Unmodified_ShouldBeFieldByFieldEqual()
    {
        var original = SampleData.CreateWorld();
        var clone = original.Clone();

        Assert.Null(StateSnapshot.Diff(original, clone));
    }
}
