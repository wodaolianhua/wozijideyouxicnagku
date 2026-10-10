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

/// <summary>势力资源。全部为整数，禁止浮点（浮点累加会产生误差）。</summary>
public sealed class Resources
{
    public int Grain { get; set; }      // 粮
    public int Silver { get; set; }     // 银
    public int Soldier { get; set; }    // 可用兵源
    public int Morale { get; set; }     // 民心 / 士气
    public int Stability { get; set; }  // 稳定度

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
    public int Morale { get; set; }
    public int MoraleMax { get; set; }
    public int PositionCityId { get; set; }
    /// <summary>移动目的地城池 Id，-1 表示无移动计划。</summary>
    public int TargetCityId { get; set; } = -1;
    public ArmyStance Stance { get; set; }
    /// <summary>本月已执行过 Rest，防止反复休整刷士气。</summary>
    public bool HasRestedThisMonth { get; set; }

    public Army Clone() => new()
    {
        Id = Id, OwnerFactionId = OwnerFactionId, OfficerId = OfficerId,
        Soldiers = Soldiers, Morale = Morale, MoraleMax = MoraleMax,
        PositionCityId = PositionCityId, TargetCityId = TargetCityId,
        Stance = Stance, HasRestedThisMonth = HasRestedThisMonth
    };
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