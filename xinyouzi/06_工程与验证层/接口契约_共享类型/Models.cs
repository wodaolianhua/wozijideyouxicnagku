// ============================================================================
//  StrategyGame.Contracts —— 数据结构定义
// ============================================================================
//  所有跨包传递的数据结构。字段名是契约的一部分，改名即破坏性变更。
//  字段命名规则：PascalCase。序列化时由 JsonSerializerOptions 控制映射。
// ============================================================================

namespace StrategyGame.Contracts;

// ============================================================================
//  资源与状态
// ============================================================================

/// <summary>势力资源。全部为整数，禁止浮点（浮点累加会产生误差）。
///
/// 【命名陷阱警告】这里的 <see cref="Morale"/> 是<b>势力民心</b>（0-100，全势力一个值），
/// 而 <see cref="Army.Morale"/> 是<b>部队士气</b>（0-100，每支部队一个值）。
/// 两者<b>同名但不同物</b>，且作用完全不同：
/// 势力民心影响 Stability 与兵源，部队士气影响战损倍率与哗变。
/// 早期设计文档曾把两者混为一谈，导致"士气"系统无法实现。
/// 实现时请用 <see cref="Popularity"/>（民心）指代本字段，代码中避免裸用 Morale。</summary>
public sealed class Resources
{
    public int Grain { get; set; }      // 粮
    public int Silver { get; set; }     // 银
    public int Soldier { get; set; }    // 可用兵源

    /// <summary>势力民心 0-100。<b>注意：不是部队士气。</b>建议在实现层重命名为 Popularity。</summary>
    public int Morale { get; set; }

    /// <summary>稳定度 0-100，影响兵源产出倍率（&lt;30 减半，&lt;15 触发动乱）。</summary>
    public int Stability { get; set; }

    public Resources Clone() => new()
    {
        Grain = Grain, Silver = Silver, Soldier = Soldier,
        Morale = Morale, Stability = Stability
    };
}

/// <summary>城池。</summary>
public sealed class City
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OwnerFactionId { get; set; }
    public int Population { get; set; }
    public int MaxPopulation { get; set; }
    public int Defense { get; set; }        // 当前城防值
    public int MaxDefense { get; set; }
    public int FortifyLevel { get; set; }   // 城防等级
    public int FarmLevel { get; set; }      // 屯田等级
    public bool IsCapital { get; set; }
    public TerrainType Terrain { get; set; }
    /// <summary>相邻城池 Id 列表。必须对称：若 A 含 B，则 B 必须含 A。</summary>
    public List<int> AdjacentCityIds { get; set; } = new();

    public City Clone() => new()
    {
        Id = Id, Name = Name, OwnerFactionId = OwnerFactionId,
        Population = Population, MaxPopulation = MaxPopulation,
        Defense = Defense, MaxDefense = MaxDefense,
        FortifyLevel = FortifyLevel, FarmLevel = FarmLevel,
        IsCapital = IsCapital, Terrain = Terrain,
        AdjacentCityIds = new List<int>(AdjacentCityIds)
    };
}

/// <summary>部队。</summary>
public sealed class Army
{
    public int Id { get; set; }
    public int OwnerFactionId { get; set; }
    public int OfficerId { get; set; }        // 统帅，-1 表示无
    public int Soldiers { get; set; }

    /// <summary>部队士气 0-100。<b>注意：不是势力民心（见 Resources.Morale）。</b>
    /// 作用：决定 <see cref="GetMoralePenalty"/> 的战损倍率，以及归零时的哗变风险。
    /// 见 4_数值框架.md 5.2「士气系统」。</summary>
    public int Morale { get; set; }

    public int MoraleMax { get; set; }
    public int PositionCityId { get; set; }
    /// <summary>移动目的地城池 Id，-1 表示无移动计划。</summary>
    public int TargetCityId { get; set; } = -1;
    public ArmyStance Stance { get; set; }
    /// <summary>本月已执行过 Rest，防止同月反复休整刷士气。
    /// 月初士气结算（MoraleSystem）统一翻转为 false，因此每月可休整一次。</summary>
    public bool HasRestedThisMonth { get; set; }

    /// <summary>连续未参与战斗的月数。达到 <see cref="BalanceConfig.MoraleDecayNoBattleThreshold"/>
    /// 后每月触发 "和平腐化" 士气衰减。这是 D12验证问题的一个抓手：
    /// 让"该休整还是该趁胜追击"成为一个真实的战略选择。</summary>
    public int MonthsWithoutBattle { get; set; }

    /// <summary>本结算周期内是否已触发哗变（同一月内防止重复触发）。
    /// 月初士气结算（MoraleSystem）先翻转为 false 再做判定（先清后判）：
    /// 上月哗变、本月士气仍 ≤ 0 时允许再次哗变。</summary>
    public bool HasMutiniedThisMonth { get; set; }

    /// <summary>自上次士气月度结算以来是否参战（契约 v1.4）。
    /// <para><b>生命周期（与 11 步流水线对齐）</b>：DispatchResolver 在第 7 步
    /// 战斗发生时置 true（攻守双方对称）并把 MonthsWithoutBattle 清零；
    /// MoraleSystem 在下月第 3 步读取：true = 参战周期，不恢复不腐化，读完置回 false。
    /// 新设部队默认 false，因此开局第一次士气结算不会被误判为参战——
    /// 这解决了「MonthsWithoutBattle == 0 无法区分新部队与刚参战部队」的信号碰撞。</para></summary>
    public bool HasEngagedThisMonth { get; set; }

    public Army Clone() => new()
    {
        Id = Id, OwnerFactionId = OwnerFactionId, OfficerId = OfficerId,
        Soldiers = Soldiers, Morale = Morale, MoraleMax = MoraleMax,
        PositionCityId = PositionCityId, TargetCityId = TargetCityId,
        Stance = Stance, HasRestedThisMonth = HasRestedThisMonth,
        MonthsWithoutBattle = MonthsWithoutBattle,
        HasMutiniedThisMonth = HasMutiniedThisMonth,
        HasEngagedThisMonth = HasEngagedThisMonth
    };

    /// <summary>士气惩罚倍率（万分比）。10000 = 无惩罚。
    /// <para>阈值与倍率来源：4_数值框架.md 5.2「低士气惩罚」——
    /// ≥60 无罚、30-59 ×1.5、10-29 ×2.5、&lt;10 ×4.0。</para>
    /// <para><b>档位是代入实算定的，不是拍脑袋</b>：原设计 ×1.2/1.5/2.0 在实测中
    /// 士气从 60 掉到 10 战局结果完全不变（仍是攻陷，只是多烧点兵），
    /// 士气系统形同装饰。×1.5/2.5/4.0 才能让「士气 20」时
    /// 均势攻城从攻陷翻为击退 —— 这才构��真实压力。</para>
    /// <para><b>实现注意</b>：本倍率必须在与 SiegeThresholdLoss 比较<b>之前</b>乘上 lossRate，
    /// 且<b>不得</b>对 lossRate 做 min(x, 1.0) 钳制——否则低士气反而不受影响。</para>
    /// </summary>
    public int GetMoralePenalty()
    {
        if (Morale >= 60) return 10000;
        if (Morale >= 30) return 15000;
        if (Morale >= 10) return 25000;
        return 40000;
    }
}

/// <summary>
/// 玩家档案。
///
/// 【设计定位】玩家是"用人的那个人"，不是人才系统的一员，因此不占岗位、
/// 不可被任命、不进入 Officer 列表。但玩家可以亲自上阵，所以需要一个
/// 独立的统帅属性。
///
/// 【亲征的代价】玩家亲征时统帅位由玩家占据，该部队不再享受任何 Officer
/// 加成。这是刻意设计：让"亲自上"与"派名将上"形成真实权衡，
/// 而不是"想玩就玩"的无代价选项。
/// </summary>
public sealed class PlayerProfile
{
    /// <summary>固定为 0，与 ActorType.Player 的 ActorId 对应。</summary>
    public int Id { get; set; } = 0;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 玩家统帅值0-100。亲征时的战损计算依据。
    /// 独立成长，不与 Officer.Command 共用成长资源。
    /// </summary>
    public int Command { get; set; }

    /// <summary>
    /// 玩家是否正在亲征。true 时所率领的 Army.OfficerId 必须为 -1。
    /// </summary>
    public bool IsPersonallyCommanding { get; set; }

    /// <summary>玩家当前亲征率领的部队 Id，-1 表示未亲征。</summary>
    public int CommandingArmyId { get; set; } = -1;

    public PlayerProfile Clone() => new()
    {
        Id = Id, Name = Name, Command = Command,
        IsPersonallyCommanding = IsPersonallyCommanding,
        CommandingArmyId = CommandingArmyId
    };
}

/// <summary>人才。</summary>
public sealed class Officer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OwnerFactionId { get; set; }   // -1 = 在野
    public int PostCityId { get; set; }       // -1 = 未任职
    public PostType Post { get; set; }
    /// <summary>本月刚换任，尚未生效。用于实现"换任需要时间"。</summary>
    public int PostedSinceMonths { get; set; }
    public int Command { get; set; }          // 统率 0-100
    public int Governance { get; set; }       // 政务 0-100
    public int Loyalty { get; set; }          // 忠诚 0-100
    public int Age { get; set; }
    public bool IsAlive { get; set; }
    public OfficerSpecialty Specialty { get; set; }

    public Officer Clone() => new()
    {
        Id = Id, Name = Name, OwnerFactionId = OwnerFactionId,
        PostCityId = PostCityId, Post = Post, PostedSinceMonths = PostedSinceMonths,
        Command = Command, Governance = Governance, Loyalty = Loyalty,
        Age = Age, IsAlive = IsAlive, Specialty = Specialty
    };
}

/// <summary>势力。</summary>
public sealed class Faction
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsPlayer { get; set; }
    public int CapitalCityId { get; set; }
    public Resources Resources { get; set; } = new();

    public Faction Clone() => new()
    {
        Id = Id, Name = Name, IsPlayer = IsPlayer,
        CapitalCityId = CapitalCityId, Resources = Resources.Clone()
    };
}

/// <summary>待执行指令（延迟指令队列）。</summary>
public sealed class PendingOrder
{
    public int OrderId { get; set; }
    public ActorType ActorType { get; set; }
    public int ActorId { get; set; }
    public ActionType Action { get; set; }
    public int TargetId { get; set; }
    public int SecondaryTargetId { get; set; } = -1;
    public int Amount { get; set; }
    public int ExecuteYear { get; set; }
    public int ExecuteMonth { get; set; }

    public PendingOrder Clone() => new()
    {
        OrderId = OrderId, ActorType = ActorType, ActorId = ActorId,
        Action = Action, TargetId = TargetId, SecondaryTargetId = SecondaryTargetId,
        Amount = Amount, ExecuteYear = ExecuteYear, ExecuteMonth = ExecuteMonth
    };
}

/// <summary>事件记录。用于回合结算界面展示与回放。</summary>
public sealed class EventRecord
{
    public int Year { get; set; }
    public int Month { get; set; }
    public EventCategory Category { get; set; }
    /// <summary>面向玩家显示的文本。中文。</summary>
    public string Message { get; set; } = string.Empty;
    public int FactionId { get; set; }
    /// <summary>稳定的事件标识符，供 UI 决定图标与配色。非本地化文本。</summary>
    public string EventKey { get; set; } = string.Empty;

    public EventRecord Clone() => new()
    {
        Year = Year, Month = Month, Category = Category,
        Message = Message, FactionId = FactionId, EventKey = EventKey
    };
}

/// <summary>世界状态——唯一真相来源。全部字段必须可深拷贝。</summary>
public sealed class WorldState
{
    public int Year { get; set; } = 1;
    /// <summary>月份 1-12。</summary>
    public int Month { get; set; } = 1;
    /// <summary>随机种子。存档必须包含此字段，否则读档后随机序列会变。</summary>
    public int Seed { get; set; }

    /// <summary>
    /// 难度档位。必须在存档与回放中保留，否则无法重现同一难度。
    /// 铁律：此值在开局确定后不得运行时修改。
    /// </summary>
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Normal;

    /// <summary>本势力本月已执行的指令数，用于人力上限控制。</summary>
    public int ActionsThisMonth { get; set; }

    /// <summary>
    /// 玩家档案。固定只有一个，Id 恒为 0。
    /// 玩家不在 Officer 列表中，因为玩家是"用人的那个人"，不是被用的人。
    /// </summary>
    public PlayerProfile Player { get; set; } = new();

    public List<Faction> Factions { get; set; } = new();
    public List<City> Cities { get; set; } = new();
    public List<Army> Armies { get; set; } = new();
    public List<Officer> Officers { get; set; } = new();
    public List<PendingOrder> PendingOrders { get; set; } = new();
    public List<EventRecord> EventLog { get; set; } = new();

    /// <summary>深拷贝。必须拷贝全部子对象，不允许浅拷贝。</summary>
    public WorldState Clone() => new()
    {
        Year = Year, Month = Month, Seed = Seed,
        Difficulty = Difficulty, ActionsThisMonth = ActionsThisMonth,
        Player = Player.Clone(),
        Factions = Factions.ConvertAll(f => f.Clone()),
        Cities = Cities.ConvertAll(c => c.Clone()),
        Armies = Armies.ConvertAll(a => a.Clone()),
        Officers = Officers.ConvertAll(o => o.Clone()),
        PendingOrders = PendingOrders.ConvertAll(p => p.Clone()),
        EventLog = EventLog.ConvertAll(e => e.Clone())
    };

    // 查找方法：找不到返回 null，禁止抛异常
    public Faction? GetFaction(int id) => Factions.Find(f => f.Id == id);
    public City? GetCity(int id) => Cities.Find(c => c.Id == id);
    public Army? GetArmy(int id) => Armies.Find(a => a.Id == id);
    public Officer? GetOfficer(int id) => Officers.Find(o => o.Id == id);

    /// <summary>
    /// 取出该部队的实际统帅属性。
    /// 若玩家正在亲征率领该部队，返回玩家统帅值，且不叠加任何 Officer 加成。
    /// </summary>
    public int GetEffectiveCommand(int armyId)
    {
        var army = GetArmy(armyId);
        if (army == null) return 0;
        if (Player.IsPersonallyCommanding && Player.CommandingArmyId == armyId)
            return Player.Command;
        var officer = GetOfficer(army.OfficerId);
        return officer?.Command ?? 0;
    }
}

// ============================================================================
//  指令与结算
// ============================================================================

/// <summary>统一指令。所有玩法的唯一入口。玩家、人才、AI 产生完全相同的结构。</summary>
public sealed class Order
{
    public int OrderId { get; set; }
    public ActorType ActorType { get; set; }
    /// <summary>Player 恒为 0；Officer 为 OfficerId；AiFaction 为 FactionId。</summary>
    public int ActorId { get; set; }
    public ActionType Action { get; set; }
    public int TargetId { get; set; }
    /// <summary>次目标。Dispatch 为出发城；AssignPost 为岗位所在城；其余为 -1。</summary>
    public int SecondaryTargetId { get; set; } = -1;
    /// <summary>数量。Recruit 为征兵数；Dispatch 为兵力；Fortify/Develop 为资源投入量。</summary>
    public int Amount { get; set; }
    public int ExecuteYear { get; set; }
    public int ExecuteMonth { get; set; }

    /// <summary>玩家的 PlayerId 固定为 0。</summary>
    public const int PlayerId = 0;

    public static Order CreatePlayerOrder(ActionType action, int target, int amount = 0, int secondary = -1)
        => new() { ActorType = ActorType.Player, ActorId = PlayerId, Action = action, TargetId = target, Amount = amount, SecondaryTargetId = secondary };

    public static Order CreateOfficerOrder(ActionType action, int officerId, int target, int amount = 0, int secondary = -1)
        => new() { ActorType = ActorType.Officer, ActorId = officerId, Action = action, TargetId = target, Amount = amount, SecondaryTargetId = secondary };

    public static Order CreateAiOrder(ActionType action, int factionId, int target, int amount = 0, int secondary = -1)
        => new() { ActorType = ActorType.AiFaction, ActorId = factionId, Action = action, TargetId = target, Amount = amount, SecondaryTargetId = secondary };

    public Order Clone() => (Order)MemberwiseClone();
}

/// <summary>
/// 状态增量。表现层据此做动画，无头模拟器据此做差异分析。
/// 这是 Resolver 唯一的状态输出形式。
/// </summary>
public sealed class StateDelta
{
    /// <summary>目标类型名。取值必须来自：Faction / City / Army / Officer / WorldState。</summary>
    public string TargetType { get; set; } = string.Empty;
    public int TargetId { get; set; }
    /// <summary>字段名。取值必须与 Contracts 中的属性名完全一致，如 "Defense"。</summary>
    public string Field { get; set; } = string.Empty;
    public int OldValue { get; set; }
    public int NewValue { get; set; }
}

/// <summary>结算结果。</summary>
public sealed class ResolveResult
{
    public ResolveResultCode Code { get; set; }
    /// <summary>失败原因，面向玩家显示。成功时为 null。必须是中文。</summary>
    public string? Message { get; set; }
    public List<StateDelta> Deltas { get; set; } = new();
    public List<EventRecord> Events { get; set; } = new();

    public bool IsSuccess => Code == ResolveResultCode.Success;

    // ===== 便捷构造方法 =====
    public static ResolveResult Ok() => new() { Code = ResolveResultCode.Success };

    public static ResolveResult Fail(ResolveResultCode code, string message)
        => new() { Code = code, Message = message };

    public ResolveResult WithDelta(string targetType, int targetId, string field, int oldValue, int newValue)
    {
        Deltas.Add(new StateDelta
        {
            TargetType = targetType, TargetId = targetId,
            Field = field, OldValue = oldValue, NewValue = newValue
        });
        return this;
    }

    public ResolveResult WithEvent(EventCategory category, string eventKey, string message, int factionId, int year, int month)
    {
        Events.Add(new EventRecord
        {
            Category = category, EventKey = eventKey,
            Message = message, FactionId = factionId, Year = year, Month = month
        });
        return this;
    }
}

/// <summary>时间工具。日期与单调整数的互转。</summary>
public static class TimeUtil
{
    public static readonly int[] DaysPerMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
    public const int MonthsPerYear = 12;

    /// <summary>把年+月转为可用于比较的单调整数。用于指令执行时间排序。</summary>
    public static int TotalMonths(int year, int month) => (year - 1) * MonthsPerYear + (month - 1);

    public static (int year, int month) FromTotalMonths(int total)
        => (total / MonthsPerYear + 1, total % MonthsPerYear + 1);

    public static bool IsYearEnd(int month) => month >= MonthsPerYear;
}