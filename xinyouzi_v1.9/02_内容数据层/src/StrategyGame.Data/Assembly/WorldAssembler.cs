// ============================================================================
//  世界组装器：把「静态数据」拼成「可运行的世界状态」
// ============================================================================
//  【为什么需要这一层】
//  数据层里城池被拆成两半：cities.json 的 cities[] 是身份（叫什么、归谁、连着谁），
//  cityStates[] 是当前状态（人口、城防、等级）。人才同理（officers + officerStates）。
//  规则层只认契约里的 City / Officer，不认这两张表。
//  中间的拼装必须有人做，而且只能做一次——否则每个调用方各写一遍，
//  迟早出现「模拟器里的城有 7 座、界面上只有 6 座」这种鬼问题。
//
//  【纯组装】本文件不含任何玩法计算：不调平衡公式、不改数值、不做随机。
//  它只负责把数值搬对地方，算错一个字段就会让整局数值失真。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Data.Config;

namespace StrategyGame.Data.Assembly;

/// <summary>
/// 把 <see cref="Config.GameDataSet"/> 组装成可运行的 <see cref="WorldState"/>。
/// </summary>
public static class WorldAssembler
{
    /// <summary>
    /// 组装世界。
    /// </summary>
    /// <param name="data">已加载并通过校验的内容数据。</param>
    /// <param name="seed">随机种子。写入世界状态，供确定性随机使用。</param>
    /// <param name="playerFactionName">玩家势力显示名；为空时沿用数据里的名字。</param>
    /// <returns>可直接交给规则层的世界状态。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">数据不自洽（缺状态、孤城、势力不存在）时抛出。</exception>
    public static WorldState Build(GameDataSet data, int seed, string? playerFactionName = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        var world = new WorldState
        {
            Year = 1,
            Month = 1,
            Seed = seed,
            Difficulty = DifficultyLevel.Normal,
            Player = new PlayerProfile
            {
                Id = Order.PlayerId,
                Name = string.IsNullOrWhiteSpace(playerFactionName) ? "玩家" : playerFactionName,
                Command = 50,
                IsPersonallyCommanding = false,
                CommandingArmyId = -1,
            },
            Factions = new List<Faction>(),
            Cities = new List<City>(),
            Armies = new List<Army>(),
            Officers = new List<Officer>(),
            PendingOrders = new List<PendingOrder>(),
            EventLog = new List<EventRecord>(),
        };

        // ---- 势力：深拷贝一份，避免调用方后续改动污染数据集 ----
        foreach (var f in data.Factions)
        {
            var faction = new Faction
            {
                Id = f.Id,
                Name = f.IsPlayer && !string.IsNullOrWhiteSpace(playerFactionName)
                    ? playerFactionName
                    : f.Name,
                IsPlayer = f.IsPlayer,
                CapitalCityId = f.CapitalCityId,
                Resources = f.Resources.Clone(),
                Prestige = f.Prestige,
                UnlockedScale = f.UnlockedScale,
                DepartmentHead = new Dictionary<DepartmentType, int>(f.DepartmentHead ?? new()),
                LastRecruitMonth = f.LastRecruitMonth,
            };

            world.Factions.Add(faction);
        }

        // ---- 城池：身份 + 状态 ----
        var stateByCity = new Dictionary<int, Config.CityStateData>();
        foreach (var s in data.CityStates)
        {
            stateByCity[s.CityId] = s;
        }

        foreach (var t in data.Cities)
        {
            if (!stateByCity.TryGetValue(t.Id, out var s))
            {
                throw new InvalidOperationException(
                    $"城池 {t.Id}（{t.Name}）缺少 cityStates 条目，无法组装世界。");
            }

            world.Cities.Add(new City
            {
                Id = t.Id,
                Name = t.Name,
                OwnerFactionId = t.FactionId,
                Population = t.Population,
                MaxPopulation = s.MaxPopulation,
                Defense = s.Defense,
                MaxDefense = s.MaxDefense,
                FortifyLevel = s.FortifyLevel,
                FarmLevel = s.FarmLevel,
                BarracksLevel = s.BarracksLevel,
                MarketLevel = s.MarketLevel,
                ProvinceId = s.ProvinceId,
                UpgradingBuilding = -1,
                UpgradeMonthsRemaining = 0,
                IsCapital = t.IsCapital,
                Terrain = t.Terrain,
                AdjacentCityIds = new List<int>(t.AdjacentCityIds),
            });
        }

        // ---- 人才：属性 + 归属 ----
        var stateByOfficer = new Dictionary<int, Config.OfficerStateData>();
        foreach (var s in data.OfficerStates)
        {
            stateByOfficer[s.OfficerId] = s;
        }

        foreach (var t in data.Officers)
        {
            stateByOfficer.TryGetValue(t.Id, out var s);

            world.Officers.Add(new Officer
            {
                Id = t.Id,
                Name = t.Name,
                OwnerFactionId = s?.OwnerFactionId ?? -1,
                PostCityId = s?.PostCityId ?? -1,
                Post = s?.Post ?? PostType.None,
                PostedSinceMonths = 0,
                Command = t.Command,
                Governance = t.Governance,
                Intellect = t.Intellect,
                Charisma = t.Charisma,
                Loyalty = t.Loyalty,
                Age = t.Age,
                IsAlive = s?.IsAlive ?? true,
                Specialty = t.Specialty,
                NameChangeUsed = false,
            });
        }

        return world;
    }

    /// <summary>
    /// 取某势力的初始部队。开局每个势力在其首都驻一支 0 人士兵的空架子，
    /// 由征兵把兵源灌进来。
    /// </summary>
    /// <param name="world">已组装的世界。</param>
    /// <returns>补齐部队后的世界（原地修改并返回）。</returns>
    /// <remarks>
    /// 【为什么建 0 人部队】出征指令要求「从自己有兵的城出发」，
    /// 而部队是承载兵力的实体。没有部队实体，玩家征来的兵无处安放。
    /// 0 人只是占位，界面不会显示一支空部队。
    /// </remarks>
    public static WorldState EnsureGarrisons(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);

        int nextId = 1;
        foreach (var city in world.Cities)
        {
            if (world.Armies.Any(a => a.PositionCityId == city.Id))
            {
                continue;
            }

            world.Armies.Add(new Army
            {
                Id = nextId++,
                OwnerFactionId = city.OwnerFactionId,
                OfficerId = -1,
                Soldiers = 0,
                Morale = 60,
                MoraleMax = 100,
                PositionCityId = city.Id,
                TargetCityId = -1,
                Stance = ArmyStance.Idle,
            });
        }

        // 把势力开局兵源编成现役部队，否则兵源池永远进不了「自动补给」循环：
        // 第 8 步只补给 Soldiers > 0 的部队，0 兵部队会被当成已覆灭而跳过，
        // 结果就是兵源越攒越多、却没有任何一支部队能吃到一个兵。
        foreach (var faction in world.Factions)
        {
            var garrisons = world.Armies
                .Where(a => a.OwnerFactionId == faction.Id)
                .OrderBy(a => a.Id)
                .ToList();

            int pool = faction.Resources.Soldier;
            if (garrisons.Count == 0 || pool <= 0)
            {
                continue;
            }

            int share = pool / garrisons.Count;
            int remainder = pool % garrisons.Count;

            for (int i = 0; i < garrisons.Count; i++)
            {
                garrisons[i].Soldiers = share + (i < remainder ? 1 : 0);
            }

            faction.Resources.Soldier = 0;
        }

        return world;
    }
}
