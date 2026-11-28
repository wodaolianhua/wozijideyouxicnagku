using MiningGame.Contracts;
using Xunit;

namespace MiningGame.Core.Tests;

public class SellPolishTests
{
    [Fact]
    public void Sell_EmptyBackpack_ZeroIncome()
    {
        var state = TestWorld.AtSurface();
        var sale = new SellResolver().Sell(state);
        Assert.Equal(0, sale.Income);
        Assert.Empty(sale.Sold);
        Assert.Empty(sale.SoldProducts);
    }

    [Fact]
    public void Sell_RawOre_BasePrice()
    {
        var state = TestWorld.AtSurface();
        TestWorld.FillBackpack(state, MineralType.Iron, 2);

        var sale = new SellResolver().Sell(state);
        Assert.Equal(32, sale.Income); // 16 * 2
        Assert.Single(sale.Sold);
    }

    [Fact]
    public void Sell_PolishedGoods_PricedByQuality()
    {
        var state = TestWorld.AtSurface();
        TestWorld.FillBackpack(state, MineralType.Gold, 1);
        state.PolishedGoods.Add(new PolishedItem { Mineral = MineralType.Gold, Grade = QualityGrade.Flawless, Amount = 2 });

        var sale = new SellResolver().Sell(state);
        // 金 135；完美 360% -> 486/件 *2 = 972；原矿 135 -> 1107
        Assert.Equal(1107, sale.Income);
        Assert.Single(sale.SoldProducts);
        Assert.Equal(486, sale.SoldProducts[0].UnitPrice);
        Assert.Equal(972, sale.SoldProducts[0].Subtotal);
    }

    [Fact]
    public void Sell_RequiresSurface()
    {
        var state = TestWorld.State();
        TestWorld.FillBackpack(state, MineralType.Iron, 1);

        var result = new MiningSimulator().Advance(
            state, new[] { new GameAction { Kind = ActionKind.Sell } }, 1, 7);

        Assert.Equal(AdvanceResultCode.InvalidTarget, result.Code);
        Assert.Equal(0, result.State.Money - 100);
        Assert.Single(result.State.Backpack.Items);
    }

    [Fact]
    public void Sell_AtSurface_ClearsAndAddsMoney()
    {
        var state = TestWorld.AtSurface();
        TestWorld.FillBackpack(state, MineralType.Iron, 2);
        state.PolishedGoods.Add(new PolishedItem { Mineral = MineralType.Iron, Grade = QualityGrade.Fine, Amount = 1 });

        var result = new MiningSimulator().Advance(
            state, new[] { new GameAction { Kind = ActionKind.Sell } }, 1, 7);

        // 原矿 32 + 铁精良 16*190%=30 -> 62
        Assert.Equal(62, result.Outcomes[0].Sale.Income);
        Assert.Equal(162, result.State.Money);
        Assert.Empty(result.State.Backpack.Items);
        Assert.Empty(result.State.PolishedGoods);
    }

    [Fact]
    public void Polish_RequiresSurface()
    {
        var state = TestWorld.State();
        TestWorld.FillBackpack(state, MineralType.Iron, 1);
        state.MinerY = 2;

        var result = new MiningSimulator().Advance(
            state, new[] { new GameAction { Kind = ActionKind.Polish } }, 1, 7);

        Assert.Equal(AdvanceResultCode.InvalidTarget, result.Code);
        Assert.Empty(result.State.PolishedGoods);
        Assert.Single(result.State.Backpack.Items);
    }

    [Fact]
    public void Polish_NotAtSurface_Direct()
    {
        var state = TestWorld.State();
        TestWorld.FillBackpack(state, MineralType.Iron, 1);
        Assert.Equal(PolishResultCode.NotAtSurface, new PolishResolver().Polish(state, 1, 1).Code);
    }

    [Fact]
    public void Polish_NothingToPolish()
    {
        var state = TestWorld.AtSurface();
        Assert.Equal(PolishResultCode.NothingToPolish, new PolishResolver().Polish(state, 1, 1).Code);
    }

    [Fact]
    public void Polish_OutOfStamina()
    {
        var state = TestWorld.AtSurface();
        TestWorld.FillBackpack(state, MineralType.Iron, 1);
        state.Stamina = 0;
        Assert.Equal(PolishResultCode.OutOfStamina, new PolishResolver().Polish(state, 1, 1).Code);
    }

    [Fact]
    public void Polish_ConsumesOre_ProducesGradedGoods()
    {
        var state = TestWorld.AtSurface();
        TestWorld.FillBackpack(state, MineralType.Iron, 2);

        var result = new MiningSimulator().Advance(
            state, new[] { new GameAction { Kind = ActionKind.Polish } }, 1, 99);

        var polish = result.Outcomes[0].Polish;
        Assert.Equal(PolishResultCode.Ok, polish.Code);
        Assert.Equal(2, polish.ConsumedUnits); // UnitsPerAction=5 但只有 2 单位
        Assert.Equal(2, polish.Produced.Sum(p => p.Amount));
        Assert.Empty(result.State.Backpack.Items);
        Assert.Equal(2, result.State.PolishedGoods.Sum(p => p.Amount));
        Assert.Equal(38, result.State.Stamina); // 40 - 2
        Assert.All(polish.Produced, p => Assert.InRange((int)p.Grade, 0, 4));
    }

    [Fact]
    public void Polish_IsDeterministic_ForSameSeed()
    {
        var a = TestWorld.AtSurface();
        var b = TestWorld.AtSurface();
        TestWorld.FillBackpack(a, MineralType.Silver, 5);
        TestWorld.FillBackpack(b, MineralType.Silver, 5);

        var ra = new PolishResolver().Polish(a, 5, 12345);
        var rb = new PolishResolver().Polish(b, 5, 12345);

        Assert.Equal(
            ra.Produced.Select(p => $"{p.Mineral}:{p.Grade}:{p.Amount}"),
            rb.Produced.Select(p => $"{p.Mineral}:{p.Grade}:{p.Amount}"));
    }

    [Fact]
    public void Polish_DeeperMineral_SkewsToBetterGrades()
    {
        var cfg = TestWorld.Config().PolishSettings;
        int coal = 0, rare = 0;

        for (int i = 0; i < 200; i++)
        {
            coal += (int)PolishResolver.RollGrade(MineralType.Coal, cfg, i, 0);
            rare += (int)PolishResolver.RollGrade(MineralType.RareCrystal, cfg, i, 0);
        }

        Assert.True(rare > coal, $"稀有晶体成色总和 {rare} 应高于煤 {coal}");
    }
}
