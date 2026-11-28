using MiningGame.Contracts;

namespace MiningGame.Core;

public sealed class BackpackPolicy : IBackpackPolicy
{
    public bool CanAdd(Backpack backpack, int amount) =>
        amount <= 0 || backpack.Used + amount <= backpack.Capacity;

    public int Valuation(Backpack backpack, IReadOnlyDictionary<int, int> prices)
    {
        int total = 0;
        foreach (var item in backpack.Items)
        {
            if (prices.TryGetValue((int)item.Mineral, out int price)) total += price * item.Amount;
        }
        return total;
    }

    // 装矿：调用方先保证 CanAdd；合并同类项，同步 Used。
    public void Add(Backpack backpack, MineralType mineral, int amount)
    {
        if (amount <= 0) return;
        var item = backpack.Items.FirstOrDefault(i => i.Mineral == mineral);
        if (item == null)
        {
            item = new BackpackItem { Mineral = mineral, Amount = 0 };
            backpack.Items.Add(item);
        }
        item.Amount += amount;
        backpack.Used += amount;
    }
}
