// ============================================================================
//  岗位取才 —— 岗位/专长/政务到加成的唯一换算处
// ============================================================================
//  【为什么单独成文件】
//  第 4 轮补丁把「人才对城市产出/成本的修正」从「数人头×5 百分比」的占位
//  落地为裁决公式：政务点 + 专长查表，万分比表达。EconomySystem 与
//  ResolverBase 都要消费同一套查表逻辑，若不集中，两处会各写一份、迟早漂移。
//  因此本文件只放岗位取才，不做任何结算，供两方共用。
//
//  【纯函数】所有方法只读入参，无随机、无 IO、无静态可变状态。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core;

/// <summary>
/// 岗位取才换算。集中实现「某城已生效在岗人才 → 城市人才加成（万分比）」的
/// 取才与查表逻辑，供 <c>EconomySystem</c> 与 <c>ResolverBase</c> 共用。
/// </summary>
/// <remarks>
/// 【生效窗口铁律】人才任命后需经过 <see cref="BalanceConfig.PostTakeEffectMonths"/>
/// 个月才生效；生效前（<c>PostedSinceMonths &lt; PostTakeEffectMonths</c>）
/// 该人才不提供任何加成——频繁换将因此有代价。
/// 【岗位白名单】粮产只认 <see cref="PostType.FarmOverseer"/> 与
/// <see cref="PostType.CityGovernor"/>；折扣只认各自所需岗位。其余岗位贡献 0。
/// </remarks>
internal static class PostEffects
{
    /// <summary>
    /// 取目标城「已生效」的唯一在岗人才。无则返回 null。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="city">目标城池。只读。</param>
    /// <param name="config">数值配置。生效窗口月数从此读取。</param>
    /// <returns>已生效在岗人才；不存在或尚未生效时返回 null。</returns>
    /// <remarks>
    /// 筛选条件与 <c>EconomySystem.ResolveInfrastructure</c> 现有写法一致：
    /// 在岗于该城（<c>PostCityId == city.Id</c>）、确有岗位（<c>Post != None</c>）、
    /// 存活（<c>IsAlive</c>）、属于该城势力（<c>OwnerFactionId</c>），
    /// 并额外要求 <c>PostedSinceMonths &gt;= PostTakeEffectMonths</c>。
    /// Resolver 保证一城至多一岗，取 <c>FirstOrDefault</c> 即可。
    /// </remarks>
    public static Officer? GetEffectiveCityOfficer(WorldState state, City city, BalanceConfig config)
    {
        foreach (var officer in state.Officers)
        {
            if (officer.PostCityId == city.Id
                && officer.Post != PostType.None
                && officer.IsAlive
                && officer.OwnerFactionId == city.OwnerFactionId
                && officer.PostedSinceMonths >= config.PostTakeEffectMonths)
            {
                return officer;
            }
        }

        return null;
    }

    /// <summary>
    /// 计算某人才在指定岗位上的城市加成（万分比）。
    /// </summary>
    /// <param name="officer">人才。只读。</param>
    /// <param name="actualPost">实际岗位（取 <c>officer.Post</c> 的存量值，不重新推导）。</param>
    /// <param name="config">数值配置。专长加成表从此读取。</param>
    /// <returns>万分比加成：政务点 × 每点折算 + 专长查表；无匹配专长时专长项为 0。</returns>
    /// <remarks>
    /// <c>G × officer.Governance + 专长加成</c>，其中
    /// <c>G = PlaceholderNumbers.GovernancePointToBonusPerMille</c>（基准 50 = 每点 +0.5%）。
    /// 专长查表先走 <c>"{专长}:{岗位}"</c>（枚举名 <c>ToString()</c>，如
    /// <c>"Agriculture:FarmOverseer"</c>），落空再走 <c>"{专长}:Any"</c>，都缺返回 0。
    /// </remarks>
    public static int GetBonus(Officer officer, PostType actualPost, BalanceConfig config)
    {
        int governance = PlaceholderNumbers.GovernancePointToBonusPerMille * officer.Governance;

        string specialtyKey = $"{officer.Specialty}:{actualPost}";
        string anyKey = $"{officer.Specialty}:Any";

        int specialty = config.SpecialtyBonus.TryGetValue(specialtyKey, out int v)
            ? v
            : config.SpecialtyBonus.TryGetValue(anyKey, out int a) ? a : 0;

        return governance + specialty;
    }
}
