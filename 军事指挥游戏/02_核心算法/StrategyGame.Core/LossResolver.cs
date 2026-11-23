using StrategyGame.Contracts;
using StrategyGame.Core.Internal;

namespace StrategyGame.Core;

/// <summary>
/// 损失结算：按双方兵力、兵种、地形防御与伏击加成算损失。
/// 纯函数、确定性：同一 encounter + state 永远得到同一结果，不使用随机数。
/// </summary>
public sealed class LossResolver : ILossResolver
{
    public LossResult Resolve(EncounterEvent encounter, WorldState state, int seed)
    {
        var result = new LossResult();
        if (encounter?.ArmyIds is null || state?.Armies is null)
        {
            return result;
        }

        var participants = new List<Army>();
        foreach (var id in encounter.ArmyIds)
        {
            var army = MarchMath.FindArmy(state, id);
            if (army is not null)
            {
                participants.Add(army);
            }
        }

        foreach (var army in participants)
        {
            var raw = 0.0;
            foreach (var enemy in participants)
            {
                if (MarchMath.SameId(enemy.Id, army.Id))
                {
                    continue;
                }

                var attack = enemy.Strength * MarchRules.UnitAttackFactor(enemy.UnitType);
                var defense = MarchRules.TerrainDefenseFactor(GroundOf(state, army));
                raw += MarchRules.BaseLossRate * attack / defense;
            }

            if (encounter.IsAmbush && !string.IsNullOrEmpty(encounter.AmbusherId))
            {
                if (MarchMath.SameId(army.Id, encounter.AmbusherId))
                {
                    raw *= MarchRules.AmbusherLossFactor;
                }
                else
                {
                    raw *= MarchRules.AmbushedLossFactor;
                }
            }

            var loss = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
            result.LossByArmy[army.Id] = Math.Clamp(loss, 0, Math.Max(0, army.Strength));
        }

        return result;
    }

    private static TerrainType GroundOf(WorldState state, Army army)
    {
        var path = army.PlannedPath;
        var nodeId = army.NodeId;
        if (path is { Count: > 0 } && army.PathIndex >= 0 && army.PathIndex + 1 < path.Count)
        {
            nodeId = path[army.PathIndex];
        }

        return MarchMath.FindNode(state.Map, nodeId)?.Terrain ?? TerrainType.Plain;
    }
}
