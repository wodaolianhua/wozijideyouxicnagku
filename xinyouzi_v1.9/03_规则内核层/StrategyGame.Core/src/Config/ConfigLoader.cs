// ============================================================================
//  配置加载器
// ============================================================================
//  契约的 IConfigLoader 由 02 包实现，本包提供一个读本地 JSON 的参考实现，
//  使 03 可以独立编译、独立测试（不依赖 02 包）。
//  02 包落地后，用它的实现替换即可，接口不变。
// ============================================================================

using System.Text.Json;
using StrategyGame.Contracts;

namespace StrategyGame.Core.Config;

/// <summary>
/// 读本地 JSON 文件的配置加载器。
/// </summary>
/// <remarks>
/// 【线格式统一】全部使用契约的 <see cref="WireFormat.Options"/>，
/// 因此 camelCase 命名与字符串枚举的约定自动生效，与 05A 表现层对齐。
/// </remarks>
public sealed class JsonConfigLoader : IConfigLoader
{
    private readonly string _dataDirectory;

    /// <summary>创建JSON 配置加载器。</summary>
    /// <param name="dataDirectory">数据目录。各 JSON 文件所在位置。</param>
    /// <exception cref="ArgumentException">目录路径为空时抛出。</exception>
    public JsonConfigLoader(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("数据目录不能为空。", nameof(dataDirectory));
        }

        _dataDirectory = dataDirectory;
    }

    /// <summary>加载全局平衡参数。</summary>
    /// <returns>平衡配置。文件不存在时返回一份全零的默认配置，由校验器负责报错。</returns>
    public BalanceConfig LoadBalance()
        => Load<BalanceConfig>(WireFormat.FileNames.Balance) ?? new BalanceConfig();

    /// <summary>加载人才模板。</summary>
    /// <returns>人才模板列表。文件不存在时返回空列表。</returns>
    public List<OfficerTemplate> LoadOfficers()
        => Load<List<OfficerTemplate>>(WireFormat.FileNames.Officers) ?? new List<OfficerTemplate>();

    /// <summary>加载城池模板。</summary>
    /// <returns>城池模板列表。文件不存在时返回空列表。</returns>
    public List<CityTemplate> LoadCities()
        => Load<List<CityTemplate>>(WireFormat.FileNames.Cities) ?? new List<CityTemplate>();

    /// <summary>加载事件模板。</summary>
    /// <returns>事件模板列表。文件不存在时返回空列表。</returns>
    public List<GameEventTemplate> LoadEvents()
        => Load<List<GameEventTemplate>>(WireFormat.FileNames.Events) ?? new List<GameEventTemplate>();

    /// <summary>
    /// 读取并反序列化指定文件。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="fileName">文件名。</param>
    /// <returns>反序列化结果；文件不存在或内容为空时返回 null。</returns>
    /// <remarks>
    /// 【为什么不抛异常】配置加载发生在开局，缺失文件应当由
    /// <see cref="ConfigValidator"/> 汇总成问题列表，而不是在读取阶段中断。
    /// 这与R5「校验器返回所有问题列表，不抛异常」的要求一致。
    /// </remarks>
    private T? Load<T>(string fileName)
        where T : class
    {
        string path = Path.Combine(_dataDirectory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        string json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(json, WireFormat.Options);
    }
}

/// <summary>
/// 内存配置加载器。测试与无头模拟专用，避免依赖文件系统。
/// </summary>
/// <remarks>
/// 无头模拟器要跑成千上万局，碰磁盘会成为瓶颈。
/// 这个加载器让模拟器可以把配置常驻内存。
/// </remarks>
public sealed class InMemoryConfigLoader : IConfigLoader
{
    private readonly BalanceConfig _balance;
    private readonly List<OfficerTemplate> _officers;
    private readonly List<CityTemplate> _cities;
    private readonly List<GameEventTemplate> _events;

    /// <summary>创建内存配置加载器。</summary>
    /// <param name="balance">平衡配置。</param>
    /// <param name="officers">人才模板列表。</param>
    /// <param name="cities">城池模板列表。</param>
    /// <param name="events">事件模板列表。</param>
    public InMemoryConfigLoader(
        BalanceConfig balance,
        List<OfficerTemplate>? officers = null,
        List<CityTemplate>? cities = null,
        List<GameEventTemplate>? events = null)
    {
        _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        _officers = officers ?? new List<OfficerTemplate>();
        _cities = cities ?? new List<CityTemplate>();
        _events = events ?? new List<GameEventTemplate>();
    }

    /// <summary>返回构造时传入的平衡配置。</summary>
    /// <returns>平衡配置。</returns>
    public BalanceConfig LoadBalance() => _balance;

    /// <summary>返回构造时传入的人才模板列表。</summary>
    /// <returns>人才模板列表。</returns>
    public List<OfficerTemplate> LoadOfficers() => _officers;

    /// <summary>返回构造时传入的城池模板列表。</summary>
    /// <returns>城池模板列表。</returns>
    public List<CityTemplate> LoadCities() => _cities;

    /// <summary>返回构造时传入的事件模板列表。</summary>
    /// <returns>事件模板列表。</returns>
    public List<GameEventTemplate> LoadEvents() => _events;
}
