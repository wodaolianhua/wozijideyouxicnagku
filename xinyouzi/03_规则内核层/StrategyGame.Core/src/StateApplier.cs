// ============================================================================
//  状态写回器—— 全项目唯一的状态写入通道
// ============================================================================
//  【为什么这是架构的核心】
//  Resolver 是纯函数，只产出 StateDelta。真正写回内存状态的动作全部集中
//  在这里。这带来三个下游能力：
//    1. 无头模拟：对比两次模拟的状态差异
//    2. 回放：从Order 日志重放出同一状态
//    3. 存档：精确回滚到任意时点
//  如果状态写入散落在各Resolver 内部，这三件事全部做不到。
//
//  【实现要点】
//  - 只做字段赋值，无任何玩法逻辑（计算在Resolver 里已完成）
//  - 支持 "Resources.Grain" 形式的点号路径，解析嵌套属性
//  - 未知字段名抛异常而非静默忽略：静默忽略会让配置错误永久潜伏
// ============================================================================

using System.Reflection;
using StrategyGame.Contracts;

namespace StrategyGame.Core;

/// <summary>
/// 把 <see cref="StateDelta"/> 列表写回 <see cref="WorldState"/>。
/// </summary>
/// <remarks>
/// 【铁律】这是规则层唯一允许修改 <see cref="WorldState"/> 的地方（回合流水线的
/// 时间推进与人力计数除外，它们属于调度而非玩法）。
/// 任何 Resolver 内出现直接写state 的代码，都视为破坏纯函数约束。
/// </remarks>
public sealed class StateApplier : IStateApplier
{
    /// <summary>属性名 → setter 的缓存。避免每条Delta 都做一次反射查找。</summary>
    private static readonly Dictionary<(Type Type, string Field), Action<object, object?>> SetterCache = new();

    /// <summary>缓存的锁。写回可能来自多线程模拟器，故加锁保护。</summary>
    private static readonly object CacheLock = new();

    /// <summary>
    /// 按顺序把全部 delta 写回状态。
    /// </summary>
    /// <param name="state">待修改的状态。此处是唯一允许被修改的地方。</param>
    /// <param name="deltas">状态增量列表，按顺序应用。后者能看到前者的结果。</param>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">
    /// delta 的 TargetType / TargetId / Field 无法解析时抛出。
    /// </exception>
    public void Apply(WorldState state, List<StateDelta> deltas)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (deltas == null)
        {
            throw new ArgumentNullException(nameof(deltas));
        }

        foreach (var delta in deltas)
        {
            ApplyOne(state, delta);
        }
    }

    /// <summary>
    /// 写回单条 delta。
    /// </summary>
    /// <param name="state">待修改的状态。</param>
    /// <param name="delta">单条状态增量。</param>
    /// <exception cref="InvalidOperationException">字段无法解析时抛出。</exception>
    private static void ApplyOne(WorldState state, StateDelta delta)
    {
        if (delta == null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        object? target = ResolveTarget(state, delta);

        // 点号路径：先解析根对象，再逐级深入。
        var parts = delta.Field.Split('.', StringSplitOptions.RemoveEmptyEntries);
        object current = target!;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            PropertyInfo? next = current.GetType().GetProperty(parts[i]);
            if (next == null)
            {
                throw new InvalidOperationException(
                    $"StateDelta 字段路径无效：TargetType={delta.TargetType}, " +
                    $"TargetId={delta.TargetId}, Field=\"{delta.Field}\"，" +
                    $"无法解析中间段 \"{parts[i]}\"。");
            }

            object? nested = next.GetValue(current);
            if (nested == null)
            {
                throw new InvalidOperationException(
                    $"StateDelta 字段路径无效：TargetType={delta.TargetType}, " +
                    $"TargetId={delta.TargetId}, Field=\"{delta.Field}\"，" +
                    $"中间段 \"{parts[i]}\" 为 null。");
            }

            current = nested;
        }

        string leaf = parts[^1];
        var setter = GetSetter(current.GetType(), leaf);

        // bool 字段用 0/1 承载：契约的 StateDelta 只有 int 通道。
        object? value = leaf.Length > 0 && IsBooleanField(current.GetType(), leaf)
            ? delta.NewValue != 0
            : delta.NewValue;

        setter(current, value);
    }

    /// <summary>
    /// 根据 TargetType / TargetId 找到被写入的对象。
    /// </summary>
    /// <param name="state">世界状态。</param>
    /// <param name="delta">状态增量。</param>
    /// <returns>被写入的对象实例。</returns>
    /// <exception cref="InvalidOperationException">
    /// TargetType 非法或 TargetId 找不到时抛出。找不到对象时若静默跳过，
    /// 会让回放系统悄悄丢数据，因此必须抛出。
    /// </exception>
    private static object ResolveTarget(WorldState state, StateDelta delta)
    {
        switch (delta.TargetType)
        {
            case DeltaTarget.WorldState:
                return state;

            case DeltaTarget.Faction:
            {
                var faction = state.GetFaction(delta.TargetId);
                if (faction == null)
                {
                    throw new InvalidOperationException(
                        $"StateDelta 目标不存在：TargetType=Faction, TargetId={delta.TargetId}。");
                }

                return faction;
            }

            case DeltaTarget.City:
            {
                var city = state.GetCity(delta.TargetId);
                if (city == null)
                {
                    throw new InvalidOperationException(
                        $"StateDelta 目标不存在：TargetType=City, TargetId={delta.TargetId}。");
                }

                return city;
            }

            case DeltaTarget.Army:
            {
                var army = state.GetArmy(delta.TargetId);
                if (army == null)
                {
                    throw new InvalidOperationException(
                        $"StateDelta 目标不存在：TargetType=Army, TargetId={delta.TargetId}。");
                }

                return army;
            }

            case DeltaTarget.Officer:
            {
                var officer = state.GetOfficer(delta.TargetId);
                if (officer == null)
                {
                    throw new InvalidOperationException(
                        $"StateDelta 目标不存在：TargetType=Officer, TargetId={delta.TargetId}。");
                }

                return officer;
            }

            default:
                throw new InvalidOperationException(
                    $"StateDelta.TargetType 非法：\"{delta.TargetType}\"，" +
                    $"合法取值为 {DeltaTarget.Faction} / {DeltaTarget.City} / " +
                    $"{DeltaTarget.Army} / {DeltaTarget.Officer} / {DeltaTarget.WorldState}。");
        }
    }

    /// <summary>
    /// 获取某类型某字段的 setter，带缓存。
    /// </summary>
    /// <param name="type">目标类型。</param>
    /// <param name="field">字段名（必须是可写属性）。</param>
    /// <returns>可调用的 setter。</returns>
    /// <exception cref="InvalidOperationException">属性不存在或只读时抛出。</exception>
    private static Action<object, object?> GetSetter(Type type, string field)
    {
        var key = (type, field);

        lock (CacheLock)
        {
            if (SetterCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        PropertyInfo? property = type.GetProperty(field);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"StateDelta.Field 无效：类型 {type.Name} 上不存在属性 \"{field}\"。");
        }

        if (!property.CanWrite)
        {
            throw new InvalidOperationException(
                $"StateDelta.Field 无效：属性 {type.Name}.{field} 是只读的，无法写回。");
        }

        MethodInfo setterMethod = property.GetSetMethod()
            ?? throw new InvalidOperationException(
                $"StateDelta.Field 无效：属性 {type.Name}.{field} 没有公开 setter。");

        Action<object, object?> setter = (instance, value) => setterMethod.Invoke(instance, new[] { value });

        lock (CacheLock)
        {
            SetterCache[key] = setter;
        }

        return setter;
    }

    /// <summary>
    /// 判断指定属性是否为布尔类型（决定是否把 int 当作 0/1 解读）。
    /// </summary>
    /// <param name="type">目标类型。</param>
    /// <param name="field">字段名。</param>
    /// <returns>属性类型为 bool 时返回 true。</returns>
    private static bool IsBooleanField(Type type, string field)
    {
        PropertyInfo? property = type.GetProperty(field);
        return property?.PropertyType == typeof(bool);
    }
}
