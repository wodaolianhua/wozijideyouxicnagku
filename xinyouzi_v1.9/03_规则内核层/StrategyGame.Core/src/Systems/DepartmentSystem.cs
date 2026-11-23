// ============================================================================
//  部门效率系统
// ============================================================================
//  【为什么这段是全项目最重要的一段】
//  它把「换了个人，产出当场不一样」变成一条可验证的公式。
//  如果这段算错，玩家把治理 55 的人放进内政部看不到任何变化，
//  那「用人」这个核心玩法就只是界面上的一个按钮。
//
//  【纯函数】只读 WorldState，不修改任何字段，不产生随机，不用浮点。
// ============================================================================

using StrategyGame.Contracts;

namespace StrategyGame.Core.Systems;

/// <summary>
/// 势力级部门的效率计算。
/// </summary>
/// <remarks>
/// 公式：<c>效率 = 10000 + (主官关键属性 - 50) × 200</c>，万分比。
/// 属性 50 → 10000（100%），55 → 11000（110%），40 → 8000（80%）。
/// <para>
/// 空岗固定 8000（80%），不是 10000：没人管就该有损耗，
/// 这是「必须用人」的压迫感来源，改成 10000 会让任命变成纯收益、失去取舍。
/// </para>
/// </remarks>
public static class DepartmentSystem
{
    /// <summary>效率基数。10000 = 100%。</summary>
    public const int EfficiencyBase = 10_000;

    /// <summary>无主官时的效率。8000 = 80%。</summary>
    public const int VacantEfficiency = 8_000;

    /// <summary>每点属性折算的效率增量。</summary>
    public const int EfficiencyPerAttributePoint = 200;

    /// <summary>效率下限。防御性钳制，正常属性算不出这个值。</summary>
    public const int MinEfficiency = 4_000;

    /// <summary>效率上限。防御性钳制。</summary>
    public const int MaxEfficiency = 16_000;

    /// <summary>属性基准值。属性等于它时效率恰好 100%。</summary>
    public const int AttributeBaseline = 50;

    /// <summary>
    /// 计算某势力某部门的效率（万分比）。
    /// </summary>
    /// <param name="state">世界状态。只读。</param>
    /// <param name="factionId">势力 Id。</param>
    /// <param name="department">部门。</param>
    /// <returns>万分比效率，空岗返回 8000。</returns>
    public static int GetEfficiency(WorldState state, int factionId, DepartmentType department)
    {
        ArgumentNullException.ThrowIfNull(state);

        var faction = state.GetFaction(factionId);
        if (faction?.DepartmentHead == null)
        {
            return VacantEfficiency;
        }

        if (!faction.DepartmentHead.TryGetValue(department, out int officerId) || officerId < 0)
        {
            return VacantEfficiency;
        }

        var officer = state.GetOfficer(officerId);
        if (officer == null || !officer.IsAlive || officer.OwnerFactionId != factionId)
        {
            return VacantEfficiency;
        }

        int attribute = GetKeyAttribute(officer, department);
        int efficiency = EfficiencyBase + (attribute - AttributeBaseline) * EfficiencyPerAttributePoint;

        return Math.Clamp(efficiency, MinEfficiency, MaxEfficiency);
    }

    /// <summary>
    /// 取某部门对应的主官关键属性。
    /// </summary>
    /// <param name="officer">主官。只读。</param>
    /// <param name="department">部门。</param>
    /// <returns>关键属性值 0-100。</returns>
    /// <remarks>
    /// 经济部门取治理与魅力的均值：既要会算账，也要会跟商人打交道。
    /// 整数除法，向下取整，与全项目的截断方向一致。
    /// </remarks>
    public static int GetKeyAttribute(Officer officer, DepartmentType department)
    {
        ArgumentNullException.ThrowIfNull(officer);

        return department switch
        {
            DepartmentType.Military => officer.Command,
            DepartmentType.Interior => officer.Governance,
            DepartmentType.Economy => (officer.Governance + officer.Charisma) / 2,
            DepartmentType.Science => officer.Intellect,
            DepartmentType.Diplomacy => officer.Charisma,
            _ => AttributeBaseline,
        };
    }

    /// <summary>
    /// 把基础产出按部门效率折算。
    /// </summary>
    /// <param name="baseAmount">基础产出（未乘部门效率）。</param>
    /// <param name="efficiency">部门效率，万分比。</param>
    /// <returns>折算后的产出，向下取整。</returns>
    /// <remarks>
    /// 先乘后除、最后截断，与 <c>EconomySystem</c> 的其余乘法保持同一方向。
    /// </remarks>
    public static int Apply(int baseAmount, int efficiency)
        => (int)((long)baseAmount * efficiency / EfficiencyBase);
}
