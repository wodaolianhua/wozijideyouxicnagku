// ============================================================================
//  状态快照工具—— 供纯净性与确定性测试做逐字段比对
// ============================================================================
//  【为什么不能直接用Equals】WorldState 及其子对象都没有重写 Equals，
//  默认是引用比较。两个内容相同但实例不同的状态会被判为不等，
//  测试就失去了意义。因此这里做真正的逐字段展开比对。
// ============================================================================

using System.Text;
using StrategyGame.Contracts;

namespace StrategyGame.Core.Tests;

/// <summary>
/// 把 <see cref="WorldState"/> 展开成可逐行比对的字符串，用于状态差异检测。
/// </summary>
/// <remarks>
/// 覆盖契约中 WorldState 的全部字段。任何新增字段都必须在此登记，
/// 否则确定性测试会出现「漏检字段」的假阴性。
/// </remarks>
public static class StateSnapshot
{
    /// <summary>
    /// 把状态展开为多行文本，每行一个「路径=值」。
    /// </summary>
    /// <param name="state">待展开的状态。</param>
    /// <returns>可直接用字符串比较判定的快照文本。</returns>
    public static string Capture(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sb = new StringBuilder();

        sb.AppendLine($"World.Year={state.Year}");
        sb.AppendLine($"World.Month={state.Month}");
        sb.AppendLine($"World.Seed={state.Seed}");
        sb.AppendLine($"World.Difficulty={state.Difficulty}");
        sb.AppendLine($"World.ActionsThisMonth={state.ActionsThisMonth}");
        sb.AppendLine($"Player.Id={state.Player.Id}");
        sb.AppendLine($"Player.Name={state.Player.Name}");
        sb.AppendLine($"Player.Command={state.Player.Command}");
        sb.AppendLine($"Player.IsPersonallyCommanding={state.Player.IsPersonallyCommanding}");
        sb.AppendLine($"Player.CommandingArmyId={state.Player.CommandingArmyId}");

        foreach (var f in state.Factions.OrderBy(x => x.Id))
        {
            sb.AppendLine($"Faction[{f.Id}].Name={f.Name}");
            sb.AppendLine($"Faction[{f.Id}].IsPlayer={f.IsPlayer}");
            sb.AppendLine($"Faction[{f.Id}].CapitalCityId={f.CapitalCityId}");
            sb.AppendLine($"Faction[{f.Id}].Grain={f.Resources.Grain}");
            sb.AppendLine($"Faction[{f.Id}].Silver={f.Resources.Silver}");
            sb.AppendLine($"Faction[{f.Id}].Soldier={f.Resources.Soldier}");
            sb.AppendLine($"Faction[{f.Id}].Morale={f.Resources.Morale}");
            sb.AppendLine($"Faction[{f.Id}].Stability={f.Resources.Stability}");
        }

        foreach (var c in state.Cities.OrderBy(x => x.Id))
        {
            sb.AppendLine($"City[{c.Id}].Name={c.Name}");
            sb.AppendLine($"City[{c.Id}].OwnerFactionId={c.OwnerFactionId}");
            sb.AppendLine($"City[{c.Id}].Population={c.Population}");
            sb.AppendLine($"City[{c.Id}].MaxPopulation={c.MaxPopulation}");
            sb.AppendLine($"City[{c.Id}].Defense={c.Defense}");
            sb.AppendLine($"City[{c.Id}].MaxDefense={c.MaxDefense}");
            sb.AppendLine($"City[{c.Id}].FortifyLevel={c.FortifyLevel}");
            sb.AppendLine($"City[{c.Id}].FarmLevel={c.FarmLevel}");
            sb.AppendLine($"City[{c.Id}].IsCapital={c.IsCapital}");
            sb.AppendLine($"City[{c.Id}].Terrain={c.Terrain}");
            sb.AppendLine(
                $"City[{c.Id}].Adjacent={string.Join(",", c.AdjacentCityIds.OrderBy(x => x))}");
        }

        foreach (var a in state.Armies.OrderBy(x => x.Id))
        {
            sb.AppendLine($"Army[{a.Id}].OwnerFactionId={a.OwnerFactionId}");
            sb.AppendLine($"Army[{a.Id}].OfficerId={a.OfficerId}");
            sb.AppendLine($"Army[{a.Id}].Soldiers={a.Soldiers}");
            sb.AppendLine($"Army[{a.Id}].Morale={a.Morale}");
            sb.AppendLine($"Army[{a.Id}].MoraleMax={a.MoraleMax}");
            sb.AppendLine($"Army[{a.Id}].PositionCityId={a.PositionCityId}");
            sb.AppendLine($"Army[{a.Id}].TargetCityId={a.TargetCityId}");
            sb.AppendLine($"Army[{a.Id}].Stance={a.Stance}");
            sb.AppendLine($"Army[{a.Id}].HasRestedThisMonth={a.HasRestedThisMonth}");
            // v1.2/v1.4 新增的士气字段。漏登记会让确定性/纯净性测试
            // 对「和平腐化」「哗变」「参战周期判定」这些机制变成假阴性。
            sb.AppendLine($"Army[{a.Id}].MonthsWithoutBattle={a.MonthsWithoutBattle}");
            sb.AppendLine($"Army[{a.Id}].HasMutiniedThisMonth={a.HasMutiniedThisMonth}");
            sb.AppendLine($"Army[{a.Id}].HasEngagedThisMonth={a.HasEngagedThisMonth}");
        }

        foreach (var o in state.Officers.OrderBy(x => x.Id))
        {
            sb.AppendLine($"Officer[{o.Id}].Name={o.Name}");
            sb.AppendLine($"Officer[{o.Id}].OwnerFactionId={o.OwnerFactionId}");
            sb.AppendLine($"Officer[{o.Id}].PostCityId={o.PostCityId}");
            sb.AppendLine($"Officer[{o.Id}].Post={o.Post}");
            sb.AppendLine($"Officer[{o.Id}].PostedSinceMonths={o.PostedSinceMonths}");
            sb.AppendLine($"Officer[{o.Id}].Command={o.Command}");
            sb.AppendLine($"Officer[{o.Id}].Governance={o.Governance}");
            sb.AppendLine($"Officer[{o.Id}].Loyalty={o.Loyalty}");
            sb.AppendLine($"Officer[{o.Id}].Age={o.Age}");
            sb.AppendLine($"Officer[{o.Id}].IsAlive={o.IsAlive}");
            sb.AppendLine($"Officer[{o.Id}].Specialty={o.Specialty}");
        }

        foreach (var p in state.PendingOrders.OrderBy(x => x.OrderId))
        {
            sb.AppendLine($"Pending[{p.OrderId}].Actor={p.ActorType}/{p.ActorId}");
            sb.AppendLine($"Pending[{p.OrderId}].Action={p.Action}");
            sb.AppendLine($"Pending[{p.OrderId}].TargetId={p.TargetId}");
            sb.AppendLine($"Pending[{p.OrderId}].SecondaryTargetId={p.SecondaryTargetId}");
            sb.AppendLine($"Pending[{p.OrderId}].Amount={p.Amount}");
            sb.AppendLine($"Pending[{p.OrderId}].When={p.ExecuteYear}-{p.ExecuteMonth}");
        }

        for (int i = 0; i < state.EventLog.Count; i++)
        {
            var e = state.EventLog[i];
            sb.AppendLine($"EventLog[{i}].Key={e.EventKey}");
            sb.AppendLine($"EventLog[{i}].Message={e.Message}");
            sb.AppendLine($"EventLog[{i}].Category={e.Category}");
            sb.AppendLine($"EventLog[{i}].FactionId={e.FactionId}");
            sb.AppendLine($"EventLog[{i}].When={e.Year}-{e.Month}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 比较两个状态并返回首个差异描述。
    /// </summary>
    /// <param name="left">状态A。</param>
    /// <param name="right">状态 B。</param>
    /// <returns>无差异返回 null；有差异返回可读描述。</returns>
    public static string? Diff(WorldState left, WorldState right)
    {
        string a = Capture(left);
        string b = Capture(right);

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return null;
        }

        string[] linesA = a.Split('\n');
        string[] linesB = b.Split('\n');

        int max = Math.Max(linesA.Length, linesB.Length);
        var differences = new List<string>();

        for (int i = 0; i < max && differences.Count < 10; i++)
        {
            string lineA = i < linesA.Length ? linesA[i].Trim() : "<缺失>";
            string lineB = i < linesB.Length ? linesB[i].Trim() : "<缺失>";

            if (!string.Equals(lineA, lineB, StringComparison.Ordinal))
            {
                differences.Add($"  期望: {lineA}  实际: {lineB}");
            }
        }

        return "状态存在差异:\n" + string.Join("\n", differences);
    }
}
