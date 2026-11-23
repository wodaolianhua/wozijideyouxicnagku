// ============================================================================
//  存档系统 —— JSON 格式，可调试、可人工检查
//  【为什么必须含 Seed 与 Difficulty】
//    · Seed：读档后随机序列必须与存档前一致，否则世界分叉；
//    · Difficulty：难度必须随档持久化，否则读档后跑的难度对不上。
//  【为什么额外存随机源内部状态】
//    IRandom 契约提供 GetState/SetState 就是为存档准备的：
//    AI 选才会在并列时消耗随机数，只恢复 Seed 不恢复内部状态，
//    读档后的 AI 决策就会与不存档时不同，后续状态必然分叉。
//
//  【格式】SaveFile 是一层包装：格式版本号 + 世界状态 + 已模拟月数 +
//  OrderId 计数器 + 随机源状态。契约类型全部为无参构造 + 公共属性，
//  System.Text.Json 可直接往返。
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using StrategyGame.Contracts;

namespace StrategyGame.Sim;

/// <summary>
/// 存档文件内容。序列化与反序列化的顶层对象。
/// </summary>
public sealed class SaveFile
{
    /// <summary>存档格式版本号。初版 = 1。</summary>
    public int FormatVersion { get; set; } = SaveSystem.CurrentFormatVersion;

    /// <summary>世界状态。自带 Seed 与 Difficulty。</summary>
    public WorldState State { get; set; } = new();

    /// <summary>已模拟的月数。</summary>
    public int MonthsElapsed { get; set; }

    /// <summary>本局自增的 OrderId 计数器的当前值（下一个可用值）。</summary>
    public int NextOrderId { get; set; } = 1;

    /// <summary>随机源内部状态（<see cref="IRandom.GetState"/>）。</summary>
    public int RandomState { get; set; }

    /// <summary>月数上限。</summary>
    public int MaxMonths { get; set; } = SimulationHost.DefaultMaxMonths;

    /// <summary>
    /// 本局是否启用了种子敏感化。读档后必须保持同一口径。
    /// </summary>
    public bool ApplySeedVariance { get; set; }

    /// <summary>逐月随机源状态快照（回放用；存档续跑时按原样接续）。</summary>
    public List<int> MonthRandomStates { get; set; } = new();
}

/// <summary>
/// JSON 存档系统。
/// </summary>
public static class SaveSystem
{
    /// <summary>当前存档格式版本号。</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// 存档的 JSON 序列化选项。
    /// </summary>
    /// <remarks>
    /// 【选项选择】
    ///   · WriteIndented = true —— 存档要可人工检查、可 diff，缩进是刚需；
    ///   · 枚举序列化为字符串 —— 跨版本可读，数字枚举改序会静默读错；
    ///   · 不设 PropertyNamingPolicy —— 存档是本包内部格式，
    ///     保持 C# 属性名原样，避免「存档文件里的字段名」与契约字段名对不上号。
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// 把宿主导出为存档对象。
    /// </summary>
    /// <param name="host">宿主。</param>
    /// <returns>存档对象。</returns>
    public static SaveFile CreateSave(SimulationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.BuildSave();
    }

    /// <summary>
    /// 序列化为 JSON 文本。
    /// </summary>
    /// <param name="save">存档对象。</param>
    /// <returns>JSON 文本。</returns>
    public static string Serialize(SaveFile save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return JsonSerializer.Serialize(save, Options);
    }

    /// <summary>
    /// 从 JSON 文本反序列化。
    /// </summary>
    /// <param name="json">JSON 文本。</param>
    /// <returns>存档对象。</returns>
    public static SaveFile Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        var save = JsonSerializer.Deserialize<SaveFile>(json, Options)
                   ?? throw new InvalidDataException("存档 JSON 反序列化结果为 null。");
        Validate(save);
        return save;
    }

    /// <summary>
    /// 写入存档文件。
    /// </summary>
    /// <param name="save">存档对象。</param>
    /// <param name="path">文件绝对路径。</param>
    public static void WriteToFile(SaveFile save, string path)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentException.ThrowIfNullOrEmpty(path);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, Serialize(save));
    }

    /// <summary>
    /// 读取存档文件。
    /// </summary>
    /// <param name="path">文件绝对路径。</param>
    /// <returns>存档对象。</returns>
    public static SaveFile ReadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Deserialize(File.ReadAllText(path));
    }

    /// <summary>
    /// 校验存档的完整性。
    /// </summary>
    /// <param name="save">存档对象。</param>
    /// <exception cref="InvalidDataException">存档不合法时抛出。</exception>
    public static void Validate(SaveFile save)
    {
        ArgumentNullException.ThrowIfNull(save);

        if (save.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"存档格式版本不支持：期望 {CurrentFormatVersion}，实际 {save.FormatVersion}。");
        }

        if (save.State is null)
        {
            throw new InvalidDataException("存档缺少世界状态。");
        }

        if (save.NextOrderId < 1)
        {
            throw new InvalidDataException($"存档的 OrderId 计数器非法：{save.NextOrderId}。");
        }
    }
}
