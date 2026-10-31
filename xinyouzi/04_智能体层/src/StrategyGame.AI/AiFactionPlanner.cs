// ============================================================================
//  StrategyGame.AI —— AI 势力决策器
//  铁律：只读 state，产出 Order，绝不修改 WorldState
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.AI.Personnel;

namespace StrategyGame.AI;

/// <summary>
/// AI 势力决策器。根据势力状态选择策略并产出下月 Order 列表。
/// 每势力每月产出指令数不超过 BalanceConfig.MaxOrdersPerFactionPerMonth。
/// </summary>
public sealed class AiFactionPlanner : IFactionPlanner
{
    private readonly BalanceConfig _config;
    private readonly IRandom _random;
    private readonly IPersonnelPolicy _personnel;

    /// <summary>兵力安全线：低于此值不再主动进攻。</summary>
    private const int SoldierSafetyThreshold = 500;

    /// <summary>士气休整线：低于此值优先 Rest。</summary>
    private const int MoraleRestThreshold = 30;

    /// <summary>征兵每兵固定附加粮耗（与 03 Resolver 占位常量对齐，06 联调锁定）。</summary>
    private const int RecruitGrainPerSoldier = 2;

    /// <summary>Develop/Fortify 固定投入银量（结算端 baseCost = 基准 × Amount / 100）。</summary>
    private const int EconomyAmount = 100;

    public AiFactionPlanner(BalanceConfig config, IRandom random, IPersonnelPolicy? personnel = null)
    {
        _config = config;
        _random = random;
        _personnel = personnel ?? new BestFitPolicy(config, random);
    }

    /// <summary>为指定势力规划下月指令。</summary>
    public List<Order> Plan(WorldState state, int factionId)
    {
        var orders = new List<Order>();
        var faction = state.GetFaction(factionId);
        if (faction == null || faction.IsPlayer) return orders;

        int maxOrders = Math.Max(1, _config.MaxOrdersPerFactionPerMonth);

        // 1. 人才任命 —— 让人才有事做（P0 唯一合法的忠诚维护形态）
        AddPersonnelOrders(state, factionId, orders, maxOrders);

        // 2. 策略性指令（屯田/征兵/出兵/休整/修城）
        AddStrategicOrders(state, factionId, orders, maxOrders);

        return orders;
    }

    /// <summary>为所有非玩家势力规划。</summary>
    public List<Order> PlanAll(WorldState state)
    {
        var all = new List<Order>();
        foreach (var f in state.Factions)
        {
            if (f.IsPlayer) continue;
            all.AddRange(Plan(state, f.Id));
        }
        return all;
    }

    // ========================================================================
    //  人才任命
    //  岗位由结算端按专长强制推导，本层只做两件事：选哪个人、放到哪座缺人的城。
    //  选才标准：Governance 降序（政务岗）。一城一岗，一人不派两城。
    // ========================================================================

    private void AddPersonnelOrders(WorldState state, int factionId, List<Order> orders, int maxOrders)
    {
        if (orders.Count >= maxOrders) return;

        var cities = state.Cities.Where(c => c.OwnerFactionId == factionId).ToList();
        if (cities.Count == 0) return;

        // 缺岗城：不存在任意在岗人才（IsAlive && Post != None && PostCityId == 该城 && 本势力）
        var vacantCities = cities
            .Where(c => !state.Officers.Any(o =>
                o.OwnerFactionId == factionId && o.IsAlive &&
                o.Post != PostType.None && o.PostCityId == c.Id))
            .OrderByDescending(c => c.Population)
            .ThenBy(c => c.Id)
            .ToList();

        if (vacantCities.Count == 0) return;

        // 本计划内已派出去的人，防止同一人被派到两座城
        var assignedIds = new HashSet<int>();

        foreach (var city in vacantCities)
        {
            if (orders.Count >= maxOrders) break;

            var best = SelectGovernanceBest(state, factionId, assignedIds);
            if (best == null) break; // 空闲人才耗尽

            assignedIds.Add(best.Id);
            orders.Add(Order.CreateAiOrder(ActionType.AssignPost, factionId, best.Id,
                amount: -1, secondary: city.Id));
        }
    }

    /// <summary>
    /// 通过政策层取 Governance 最高者，排除本计划已派出的人。
    /// 优先使用 BestFitPolicy 的 excludedIds 重载；若持有的是其它实现则退化为不带排除。
    /// </summary>
    private Officer? SelectGovernanceBest(WorldState state, int factionId, HashSet<int> excluded)
    {
        return _personnel is BestFitPolicy concrete
            ? concrete.SelectBestFor(state, factionId, PostType.CityGovernor, excluded)
            : _personnel.SelectBestFor(state, factionId, PostType.CityGovernor);
    }

    // ========================================================================
    //  策略性指令
    // ========================================================================

    private void AddStrategicOrders(WorldState state, int factionId, List<Order> orders, int maxOrders)
    {
        var faction = state.GetFaction(factionId);
        if (faction == null) return;

        var cities = state.Cities.Where(c => c.OwnerFactionId == factionId).ToList();
        if (cities.Count == 0) return;

        var res = faction.Resources;
        var armies = state.Armies.Where(a => a.OwnerFactionId == factionId).ToList();
        int totalSoldiers = armies.Sum(a => a.Soldiers);

        // 【v1.5 裁决】休整是士气维护，不受“缺兵/屯田”策略门控：
        // 和平腐化 −6/月、哗变每月连发可散兵 30%，等部队进军事分支再休整为时已晚。
        // 03 第 6 轮起，本月已休整的部队月度结算挂起腐化（Rest 另给 +3/月，冷却 1 月/军）。
        var restArmy = armies
            .Where(a => a.Soldiers > 0
                && a.Morale < MoraleRestThreshold
                && !a.HasRestedThisMonth)
            .OrderBy(a => a.Morale)
            .ThenBy(a => a.Id)
            .FirstOrDefault();
        if (restArmy != null && orders.Count < maxOrders)
        {
            orders.Add(Order.CreateAiOrder(ActionType.Rest, factionId, restArmy.Id));
            return; // 休整优先，本月不再出屯田/征兵/进攻指令（与 AddMilitaryOrders 既有语义一致）
        }
        // TODO：多军情景每月只发 1 条 Rest（取士气最低、Id 最小者），未来扩军后再议多条休整。

        // 判断威胁：是否有相邻敌城
        bool threatened = IsThreatened(state, factionId, cities);

        // 兵源瓶颈：人口少、兵少 → 优先屯田攒兵源
        // v1.5：第 8 步补给每月清池、第 9 步才规划，有军势力的 res.Soldier 结构上恒为 0，
        // 兵源瓶颈只按在役总兵力判定，不再看池余量。
        bool soldierStarved = totalSoldiers < SoldierSafetyThreshold;

        // 选择策略
        if (soldierStarved)
        {
            // 经济策略：屯田 + 必要征兵
            AddEconOrders(state, factionId, orders, maxOrders, cities, res);
        }
        else if (threatened && totalSoldiers < 3000)
        {
            // 防守策略：征兵 + 修城
            AddDefenseOrders(state, factionId, orders, maxOrders, cities, res);
        }
        else if (totalSoldiers >= SoldierSafetyThreshold)
        {
            // 军事策略：能打就打，但先看士气
            AddMilitaryOrders(state, factionId, orders, maxOrders, cities, armies, res);
        }
        else
        {
            AddEconOrders(state, factionId, orders, maxOrders, cities, res);
        }
    }

    /// <summary>经济策略：优先屯田（提升粮产与兵源），其次征兵。</summary>
    private void AddEconOrders(WorldState state, int factionId, List<Order> orders, int maxOrders,
        List<City> cities, Resources res)
    {
        if (orders.Count >= maxOrders) return;

        // 屯田：选 FarmLevel 最低的城；Amount=100（银量投入），余额留余量
        var farmTarget = cities.OrderBy(c => c.FarmLevel).First();
        if (res.Silver >= _config.BaseDevelopCostSilver)
        {
            orders.Add(Order.CreateAiOrder(ActionType.Develop, factionId, farmTarget.Id, EconomyAmount));
        }

        // 征兵：如果兵源不足且有余粮
        if (orders.Count < maxOrders && res.Soldier > 0)
        {
            var recruitTarget = cities.OrderByDescending(c => c.Population).First();
            int recruitAmount = Math.Min(res.Soldier, 500);
            if (recruitAmount > 0 && CanAffordRecruit(res, recruitAmount))
            {
                orders.Add(Order.CreateAiOrder(ActionType.Recruit, factionId, recruitTarget.Id, recruitAmount));
            }
        }
    }

    /// <summary>防守策略：征兵 + 修城。</summary>
    private void AddDefenseOrders(WorldState state, int factionId, List<Order> orders, int maxOrders,
        List<City> cities, Resources res)
    {
        if (orders.Count >= maxOrders) return;

        // 修城：选防御最低的城；Amount=100（银量投入）
        var fortTarget = cities.OrderBy(c => c.Defense).First();
        if (res.Silver >= _config.BaseFortifyCostSilver)
        {
            orders.Add(Order.CreateAiOrder(ActionType.Fortify, factionId, fortTarget.Id, EconomyAmount));
        }

        // 征兵
        if (orders.Count < maxOrders && res.Soldier > 0)
        {
            var recruitTarget = cities.OrderByDescending(c => c.Population).First();
            int recruitAmount = Math.Min(res.Soldier, 800);
            if (recruitAmount > 0 && CanAffordRecruit(res, recruitAmount))
            {
                orders.Add(Order.CreateAiOrder(ActionType.Recruit, factionId, recruitTarget.Id, recruitAmount));
            }
        }
    }

    /// <summary>征兵余额估算，与 03 结算端占位公式同形。</summary>
    private bool CanAffordRecruit(Resources res, int n)
    {
        int silverNeed = _config.BaseRecruitCostSilver * n / 100;
        int grainNeed = _config.BaseRecruitCostGrain * n / 100 + RecruitGrainPerSoldier * n;
        return res.Silver >= silverNeed && res.Grain >= grainNeed;
    }

    /// <summary>军事策略：士气低先休整，否则出兵攻相邻敌城。</summary>
    private void AddMilitaryOrders(WorldState state, int factionId, List<Order> orders, int maxOrders,
        List<City> cities, List<Army> armies, Resources res)
    {
        if (orders.Count >= maxOrders) return;

        // 先检查士气：有部队士气低 → 休整
        var lowMoraleArmy = armies.FirstOrDefault(a => a.Morale < MoraleRestThreshold && !a.HasRestedThisMonth);
        if (lowMoraleArmy != null)
        {
            orders.Add(Order.CreateAiOrder(ActionType.Rest, factionId, lowMoraleArmy.Id));
            return; // 休整优先，本月不进攻
        }

        // 找可攻目标：相邻的非己方城
        var attackTarget = FindAttackTarget(state, factionId, cities);
        if (attackTarget == null) return;

        // 选最强部队出击
        var army = armies.OrderByDescending(a => a.Soldiers).FirstOrDefault(a => a.Soldiers > 0);
        if (army == null) return;

        // 兵力不足安全线则不攻
        if (army.Soldiers < SoldierSafetyThreshold) return;

        // 从该城出发（SecondaryTargetId 为出发城）
        int fromCity = army.PositionCityId;
        orders.Add(Order.CreateAiOrder(ActionType.Dispatch, factionId, attackTarget.Id, army.Soldiers, fromCity));
    }

    /// <summary>找一个可攻击的相邻敌城（非己方）。</summary>
    private City? FindAttackTarget(WorldState state, int factionId, List<City> ownedCities)
    {
        foreach (var city in ownedCities)
        {
            foreach (var adjId in city.AdjacentCityIds)
            {
                var adj = state.GetCity(adjId);
                if (adj != null && adj.OwnerFactionId != factionId)
                {
                    return adj;
                }
            }
        }
        return null;
    }

    /// <summary>判断是否受威胁：是否有相邻敌城且敌方有兵。</summary>
    private static bool IsThreatened(WorldState state, int factionId, List<City> ownedCities)
    {
        foreach (var city in ownedCities)
        {
            foreach (var adjId in city.AdjacentCityIds)
            {
                var adj = state.GetCity(adjId);
                if (adj == null || adj.OwnerFactionId == factionId) continue;
                // 敌方城存在即算威胁（简化判断）
                return true;
            }
        }
        return false;
    }
}
