using MiningGame.Contracts;

namespace MiningGame.Core;

public sealed class SellResolver : ISellResolver
{
    public SellResult Sell(GameState state)
    {
        var result = new SellResult();

        // 原矿按基础单价卖
        foreach (var item in state.Backpack.Items)
        {
            if (item.Amount <= 0) continue;
            result.Sold.Add(new BackpackItem { Mineral = item.Mineral, Amount = item.Amount });
            if (state.Prices.TryGetValue((int)item.Mineral, out int price))
                result.Income += price * item.Amount;
        }

        // 成品按成色加价卖：单价 = 原矿单价 * GradePricePercent[grade] / 100
        foreach (var good in state.PolishedGoods)
        {
            if (good.Amount <= 0) continue;
            int unitPrice = UnitPrice(state, good.Mineral, good.Grade);
            result.SoldProducts.Add(new PolishedSaleLine
            {
                Mineral = good.Mineral,
                Grade = good.Grade,
                Amount = good.Amount,
                UnitPrice = unitPrice,
                Subtotal = unitPrice * good.Amount,
            });
            result.Income += unitPrice * good.Amount;
        }

        return result;
    }

    public static int UnitPrice(GameState state, MineralType mineral, QualityGrade grade)
    {
        state.Prices.TryGetValue((int)mineral, out int basePrice);
        var percents = state.PolishSettings?.GradePricePercent;
        int percent = percents != null && (int)grade < percents.Count && percents[(int)grade] > 0
            ? percents[(int)grade]
            : 100;
        return basePrice * percent / 100;
    }
}
