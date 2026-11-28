using MiningGame.Contracts;

namespace MiningGame.Core;

public sealed class PolishResolver : IPolishResolver
{
    public PolishResult Polish(GameState state, int units, int seed)
    {
        var result = new PolishResult();

        if (state.MinerY > state.Grid.SurfaceY)
        {
            result.Code = PolishResultCode.NotAtSurface;
            return result;
        }

        var cfg = state.PolishSettings ?? new PolishConfig();
        int available = state.Backpack.Items.Where(i => i.Amount > 0).Sum(i => i.Amount);
        if (available <= 0)
        {
            result.Code = PolishResultCode.NothingToPolish;
            return result;
        }

        int requested = units > 0 ? units : Math.Max(1, cfg.UnitsPerAction);
        int perUnit = Math.Max(0, cfg.StaminaPerUnit);
        int affordable = perUnit > 0 ? state.Stamina / perUnit : requested;
        int take = Math.Min(requested, Math.Min(available, affordable));
        if (take <= 0)
        {
            result.Code = PolishResultCode.OutOfStamina;
            return result;
        }

        var produced = new List<PolishedItem>();
        int index = 0;

        foreach (var item in state.Backpack.Items)
        {
            int consume = Math.Min(item.Amount, take - result.ConsumedUnits);
            for (int k = 0; k < consume; k++)
            {
                var grade = RollGrade(item.Mineral, cfg, seed, index);
                index++;

                var same = produced.FirstOrDefault(p => p.Mineral == item.Mineral && p.Grade == grade);
                if (same == null)
                {
                    same = new PolishedItem { Mineral = item.Mineral, Grade = grade, Amount = 0 };
                    produced.Add(same);
                }
                same.Amount += 1;
            }

            if (result.ConsumedUnits >= take) break;
        }

        result.Code = PolishResultCode.Ok;
        result.ConsumedUnits = take;
        result.StaminaCost = take * perUnit;
        result.Produced = produced;
        return result;
    }

    // 成色抽取：基础权重 + 深矿加成。totally deterministic（seed + 序号 + 矿物）。
    public static QualityGrade RollGrade(MineralType mineral, PolishConfig cfg, int seed, int index)
    {
        int gradeCount = Enum.GetValues<QualityGrade>().Length;
        int tier = (int)mineral;
        int perTier = Math.Max(0, cfg.GradeBonusPerTier);

        long total = 0;
        var weights = new int[gradeCount];
        for (int g = 0; g < gradeCount; g++)
        {
            int baseWeight = g < cfg.GradeWeights.Count ? Math.Max(0, cfg.GradeWeights[g]) : 0;
            weights[g] = baseWeight + tier * perTier * g;
            total += weights[g];
        }

        if (total <= 0) return QualityGrade.Common;

        uint state = Rng.Mix(seed, index, (int)mineral);
        long roll = Rng.Next(ref state) % total;

        long acc = 0;
        for (int g = 0; g < gradeCount; g++)
        {
            acc += weights[g];
            if (roll < acc) return (QualityGrade)g;
        }
        return QualityGrade.Common;
    }
}
