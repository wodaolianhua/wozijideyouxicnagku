// ============================================================================
//  人物事件抽取
// ============================================================================
//  对应流水线的第 6 步。按事件库概率抽取人物事件。
//  随机必须走注入的 IRandom，禁止 Random.Shared——否则确定性测试必然失败。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Systems;

/// <summary>
/// 人物事件系统。按概率从事件库抽取本回合发生的事件。
/// </summary>
/// <remarks>
/// 【确定性铁律】所有随机取样必须来自注入的 <see cref="IRandom"/>。
/// 使用 Random.Shared 会让同 Seed 的两次模拟产生不同结果，
/// 无头模拟与回放全部失效。
/// </remarks>
public sealed class OfficerEventSystem
{
    /// <summary>
    /// 概率刻度基数。契约 v1.2 规定事件概率为万分比，10000 = 100%。
    /// </summary>
    public const int ProbabilityScale = 10_000;

    /// <summary>
    /// 由整数事件 Id 派生稳定 EventKey 时使用的前缀。
    /// </summary>
    /// <remarks>
    /// EventKey 是给表现层决定图标与配色的稳定标识，
    /// 而 <c>GameEventTemplate.Id</c> 在 v1.2 已改为整数。
    /// 两者类型不同，加统一前缀派生成字符串，不修改契约任一侧。
    /// </remarks>
    public const string EventKeyPrefix = "ev.template.";

    private readonly List<GameEventTemplate> _events;
    private readonly IRandom _random;
    private readonly int _probabilityCap;

    /// <summary>创建人物事件系统。</summary>
    /// <param name="events">事件模板库。为空时本系统不产出任何事件。</param>
    /// <param name="random">可播种随机源。禁止传 Random.Shared。</param>
    /// <param name="factionId">事件归属势力 Id。</param>
    /// <param name="probabilityCap">
    /// 抽取次数上限。TODO(设计): 待事件概率框架定稿后替换为按类别的概率上限规则。
    /// 当前保守取 1，避免单月刷屏。
    /// </param>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public OfficerEventSystem(
        List<GameEventTemplate> events,
        IRandom random,
        int factionId,
        int probabilityCap = 1)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        FactionId = factionId;
        _probabilityCap = probabilityCap;
    }

    /// <summary>事件归属势力 Id。</summary>
    public int FactionId { get; }

    /// <summary>
    /// 抽取本回合的人物事件。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <returns>本回合发生的事件记录列表。</returns>
    public List<EventRecord> DrawEvents(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var drawn = new List<EventRecord>();
        if (_events.Count == 0)
        {
            return drawn;
        }

        foreach (var template in _events)
        {
            if (drawn.Count >= _probabilityCap)
            {
                break;
            }

            // 概率为万分比（契约v1.2），因此取样空间为 0-9999。
            // 全程整数判定，避免浮点比较带来的边界不确定性。
            if (template.Probability <= 0)
            {
                continue;
            }

            int roll = _random.Next(ProbabilityScale);
            if (roll < template.Probability)
            {
                drawn.Add(new EventRecord
                {
                    Year = state.Year,
                    Month = state.Month,
                    Category = template.Category,
                    Message = template.Message,

                    // 【v1.2】GameEventTemplate.Id 已是 int，而 EventRecord.EventKey 是
                    // 稳定字符串（UI 依它选图标），故统一加前缀派生，两侧都不改契约。
                    EventKey = $"{EventKeyPrefix}{template.Id}",
                    FactionId = FactionId,
                });
            }
        }

        return drawn;
    }
}
