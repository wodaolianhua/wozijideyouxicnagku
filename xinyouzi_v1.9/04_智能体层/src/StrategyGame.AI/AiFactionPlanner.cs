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
    /// <summary>
    /// 兵力安全线：低于此值不再主动进攻。
    /// 【v1.7 调整】原值 500 在 7 城开局下永远达不到——开局每城驻军只有
    /// 150-270 人（300/800/600 兵源按城均分），AI 从第一月起就判定「兵不够」，
    /// 六十个月一次兵都没出过，世界完全静止。降到 120 让开局就能互相试探。
    /// </summary>
    private const int SoldierSafetyThreshold = 120;

    /// <summary>士气休整线：低于此值优先 Rest。</summary>
    /// <summary>
    /// 士气休整线。低于此值优先 Rest。
    /// 【v1.7 调整】原值 30 与「和平腐化 -6 / 自然恢复 +3」叠加会形成死锁：
    /// 士气跌破 30 → 只休整不进攻 → 不参战继续腐化 → 士气更低 → 永不进攻。
    /// 降到 18，让 AI 在士气还撑得住时先打一仗（参战月不腐化，胜仗还 +10）。
    /// </summary>
    private const int MoraleRestThreshold = 18;

    /// <summary>
    /// 攻城所需的兵力优势（万分比）。18000 = 攻方兵力需达守军 1.8 倍才动手。
    /// </summary>
    /// <remarks>
    /// 【v1.8】原实现只看「自己兵够不够 120」，不看对面有多少人 —— AI 会拿 300 人去撞
    /// 800 人的城，一只只送死，然后把整场战争耗光。
    /// 1.8 倍这个数是从战斗公式反推的：均势下攻方一轮打掉守军约 45%，
    /// 1.8 倍则一轮打掉 81%，守军补员远跟不上，第二个月必然破城。
    /// 【v1.8 二调】1.6 倍实测太急：玩家开局只有两座城、每城五百驻军，
    /// 苍岭两城每城九百，比例正好压线，第 6 个月就把玩家的城打掉了——
    /// 玩家还没来得及点第一个按钮。1.8 倍要求 AI 先合兵（两军并一军）才够打，
    /// 自然给玩家留出了前一年的发育窗口。
    /// </remarks>
    private const int AssaultAdvantageRatio = 18000;

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
        else if (threatened && IsOutmatched(state, factionId, cities, totalSoldiers))
        {
            // 防守策略：征兵 + 修城
            // 【v1.8】原判据是「受威胁且总兵力 < 3000」，而开局全军只有三四百人、
            // 每月才涨几十，3000 这条线十几年都够不着 —— 势力一旦受威胁就永远龟缩，
            // 谁都不打谁。改成横向比：只有真的打不过邻居才转防守。
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

    /// <summary>
    /// 判断本势力是否在兵力上明显弱于邻国（邻国兵力超过自身两倍）。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <param name="cities">本势力城池。</param>
    /// <param name="totalSoldiers">本势力在役总兵力。</param>
    /// <returns>true 表示应当转攻为守。</returns>
    private static bool IsOutmatched(WorldState state, int factionId, List<City> cities, int totalSoldiers)
    {
        long neighborForce = 0;
        foreach (var city in cities)
        {
            foreach (var adjId in city.AdjacentCityIds)
            {
                foreach (var army in state.Armies)
                {
                    if (army.PositionCityId == adjId && army.OwnerFactionId != factionId)
                    {
                        neighborForce += army.Soldiers;
                    }
                }
            }
        }

        return neighborForce > (long)totalSoldiers * 2;
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

        // =====================================================================
        // 第一步：城下的围城部队继续打 —— 不打就等于给守军回血
        // =====================================================================
        // 【v1.8 修复】未决结算会把攻方部队留在敌城城下（姿态 Besieging），
        // 此时它的 PositionCityId 是敌城，不能再当「出发城」用。
        // 原实现从这里直接取 PositionCityId 出兵，指令被 InvalidTarget 拒掉，
        // 围城就此中断；守军每月靠兵源池回血，城永远打不下来。
        // 正确做法：从一座与本城相邻的己方城池「遥令」这支围城部队继续攻城
        // （DispatchResolver 的第二轮兜底会挑到城下的围城部队）。
        var besieger = armies
            .Where(a => a.Soldiers > 0 && a.Stance == ArmyStance.Besieging)
            .OrderByDescending(a => a.Soldiers)
            .ThenBy(a => a.Id)
            .FirstOrDefault();

        if (besieger != null && besieger.Soldiers >= SoldierSafetyThreshold)
        {
            var besieged = state.GetCity(besieger.PositionCityId);
            if (besieged != null && besieged.OwnerFactionId != factionId)
            {
                var rally = cities
                    .Where(c => c.AdjacentCityIds.Contains(besieged.Id))
                    .OrderBy(c => c.Id)
                    .FirstOrDefault();

                if (rally != null)
                {
                    orders.Add(Order.CreateAiOrder(
                        ActionType.Dispatch, factionId, besieged.Id, besieger.Soldiers, rally.Id));
                    return;
                }
            }
        }

        // =====================================================================
        // 第二步：就地出击 —— 只挑「本城真的挨着敌城、且兵力占优」的部队
        // =====================================================================
        // 【v1.8 修复·AI 出兵坐标错位】原实现先全局找「第一个相邻敌城」，
        // 再拿最强部队的所在地当出发城，两者常常不在同一处。
        // 无头模拟里 182 条指令有 114 条因此被 RejectedNotAdjacent 驳回，
        // 表面看 AI 天天出兵，实际一次都没打起来。
        // 现在反过来：以部队所在地为锚点找它够得着的敌城，并核对兵力优势。
        var attackers = armies
            .Where(a => a.Soldiers > 0 && a.Stance != ArmyStance.Besieging)
            .OrderByDescending(a => a.Soldiers)
            .ThenBy(a => a.Id)
            .ToList();

        // 记录「够得着的目标里最省事的一座」，用于劣势时的集结方向。
        City? bestTarget = null;
        Army? bestArmy = null;

        foreach (var army in attackers)
        {
            if (army.Soldiers < SoldierSafetyThreshold) break; // 已按兵力降序，后面只会更少

            var target = FindAdjacentEnemyCity(state, factionId, army.PositionCityId);
            if (target == null) continue;

            if (HasAssaultAdvantage(state, army, target))
            {
                orders.Add(Order.CreateAiOrder(
                    ActionType.Dispatch, factionId, target.Id, army.Soldiers, army.PositionCityId));
                return;
            }

            // 打不动，但记住方向：把别的兵调过来合兵。
            if (bestTarget == null || army.Soldiers > bestArmy!.Soldiers)
            {
                bestTarget = target;
                bestArmy = army;
            }
        }

        // =====================================================================
        // 第三步：打不动 → 集结（合兵）。这是「集中兵力」在系统里的唯一入口。
        // =====================================================================
        int? massCityId = FindMassingDestination(state, factionId, cities, bestArmy);
        if (massCityId.HasValue)
        {
            var massCity = state.GetCity(massCityId.Value);

            var mover = attackers
                .Where(a => a.Soldiers >= SoldierSafetyThreshold
                    && a.PositionCityId != massCityId.Value
                    && massCity != null
                    && massCity.AdjacentCityIds.Contains(a.PositionCityId))
                .OrderByDescending(a => a.Soldiers)
                .ThenBy(a => a.Id)
                .FirstOrDefault();

            if (mover != null)
            {
                orders.Add(Order.CreateAiOrder(
                    ActionType.Dispatch, factionId, massCityId.Value,
                    mover.Soldiers, mover.PositionCityId));
                return;
            }
        }

        // =====================================================================
        // 第四步：兵还不够 → 征兵扩军。没有这一步，兵源自然增长对谁都一样，
        // 谁也别想攒出优势，战争会永久停摆。
        // =====================================================================
        if (orders.Count < maxOrders && res.Soldier > 0)
        {
            var recruitTarget = cities.OrderByDescending(c => c.Population).ThenBy(c => c.Id).First();
            int recruitAmount = Math.Min(res.Soldier, RecruitBatch);
            if (recruitAmount > 0 && CanAffordRecruit(res, recruitAmount))
            {
                orders.Add(Order.CreateAiOrder(ActionType.Recruit, factionId, recruitTarget.Id, recruitAmount));
            }
        }
    }

    /// <summary>单次征兵的批量上限。</summary>
    private const int RecruitBatch = 800;

    /// <summary>攻方是否对目标城具备足够的兵力优势。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="army">攻方部队。只读。</param>
    /// <param name="target">目标城。只读。</param>
    /// <returns>true 表示值得开打。</returns>
    /// <remarks>
    /// 守军为城内全部敌军之和；守军为空时无条件可攻（空城直接拿下）。
    /// </remarks>
    private static bool HasAssaultAdvantage(WorldState state, Army army, City target)
    {
        long defenders = 0;
        foreach (var a in state.Armies)
        {
            if (a.PositionCityId == target.Id && a.OwnerFactionId != army.OwnerFactionId)
            {
                defenders += a.Soldiers;
            }
        }

        if (defenders <= 0)
        {
            return true;
        }

        return (long)army.Soldiers * 10000 >= defenders * AssaultAdvantageRatio;
    }

    /// <summary>
    /// 选出「该往哪座城集结」：优先选己方已有部队驻守、且与某支待调部队相邻的前线城市。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <param name="cities">己方城池。</param>
    /// <param name="frontArmy">已经贴在前线的那支部队，可为 null。</param>
    /// <returns>集结城 Id；无合适目标时为 null。</returns>
    private static int? FindMassingDestination(
        WorldState state, int factionId, List<City> cities, Army? frontArmy)
    {
        // 情况一：已经有部队贴着敌人 —— 把别的兵调到它所在的城，两军合兵。
        if (frontArmy != null)
        {
            return frontArmy.PositionCityId;
        }

        // 情况二：全在后方的城 —— 找一座挨着敌城的己方城当集结点。
        var frontier = cities
            .Where(c => c.AdjacentCityIds.Any(id => IsEnemyCity(state, factionId, id)))
            .OrderByDescending(c => c.Population)
            .ThenBy(c => c.Id)
            .FirstOrDefault();

        return frontier?.Id;
    }

    /// <summary>判断某城是否为指定势力的敌城。</summary>
    private static bool IsEnemyCity(WorldState state, int factionId, int cityId)
    {
        var city = state.GetCity(cityId);
        return city != null && city.OwnerFactionId != factionId;
    }

    /// <summary>找出与指定城池相邻、且不属本势力的城池（按 Id 升序取第一座）。</summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <param name="fromCityId">出发城 Id。不在己方手里时返回 null。</param>
    /// <returns>可攻击的相邻敌城；没有则 null。</returns>
    private static City? FindAdjacentEnemyCity(WorldState state, int factionId, int fromCityId)
    {
        var from = state.GetCity(fromCityId);
        if (from == null || from.OwnerFactionId != factionId)
        {
            return null;
        }

        foreach (var adjId in from.AdjacentCityIds)
        {
            var adj = state.GetCity(adjId);
            if (adj != null && adj.OwnerFactionId != factionId)
            {
                return adj;
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
