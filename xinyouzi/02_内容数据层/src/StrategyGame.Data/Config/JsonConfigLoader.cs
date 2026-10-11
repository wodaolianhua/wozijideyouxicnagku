// ============================================================================
//  StrategyGame.Data —— IConfigLoader 的 JSON 实现
// ============================================================================
//  【铁律】加载失败必须抛异常，不允许返回半成品。
//  任务书禁止事项明确写着"禁止静默容错——数据非法必须报错，
//  不能用默认值蒙混过关"。文件不存在、JSON 语法错、字段类型不符，
//  任何一种都必须立刻炸，并说清楚是哪个文件的问题。
//
//  【文件格式：信封结构】
//  数组根节点无处安放 _comment，而任务书 R1 要求每个数值都要有 _comment 说明。
//  所以除 config.json 外，一律采用：
//    { "_comment": "...", "_todos": ["..."], "<载荷数组名>": [ ... ], ... }
//  _comment / _todos 是给策划看的元信息，反序列化时被自动跳过（契约的
//  UnmappedMemberHandling = Skip），但校验器会把它们读出来强制检查。
// ============================================================================

using System.Text.Json;
using StrategyGame.Contracts;

namespace StrategyGame.Data.Config;

/// <summary>数据文件加载异常。加载阶段的任何问题都用它抛出。</summary>
public sealed class ConfigLoadException : Exception
{
    public ConfigLoadException(string message) : base(message) { }
    public ConfigLoadException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// 从 assets/data/*.json 加载配置。
/// </summary>
public sealed class JsonConfigLoader : IConfigLoader
{
    /// <summary>各数据文件名。集中定义，避免字符串散落在代码里。</summary>
    public static class FileNames
    {
        public const string Balance = "config.json";
        public const string Officers = "officers.json";
        public const string Cities = "cities.json";
        public const string Events = "events.json";
        public const string Specialties = "specialties.json";
        public const string Factions = "factions.json";
        public const string Difficulty = "difficulty.json";
    }

    /// <summary>
    /// 词库子目录名（相对数据目录）。
    /// </summary>
    /// <remarks>
    /// 词库放在 names/ 子目录而不是平铺在数据目录根部，理由有二：
    /// 一是它们不是「运行时数据」而是「内容素材」，混在一起会让 7 个数据文件
    /// 的清单变得不整齐；二是平铺会让校验器的「每个 json 必须有 _comment」
    /// 这条规则作用到词库上，而词库的注释需求与数据文件不同。
    /// </remarks>
    public const string NamesSubDirectory = "names";

    /// <summary>词库文件名。文件名即用途，改名要同步改这里的常量。</summary>
    public static class NameFiles
    {
        public const string FactionWords = "faction_words.json";
        public const string CityPrefix = "city_prefix.json";
        public const string OfficerSurname = "officer_surname.json";
        public const string OfficerGiven = "officer_given.json";
        public const string RulerGiven = "ruler_given.json";
        public const string SpecialtyTitle = "specialty_title.json";
    }

    /// <summary>全部词库文件名。</summary>
    public static IReadOnlyList<string> AllNameFiles { get; } = new[]
    {
        NameFiles.FactionWords,
        NameFiles.CityPrefix,
        NameFiles.OfficerSurname,
        NameFiles.OfficerGiven,
        NameFiles.RulerGiven,
        NameFiles.SpecialtyTitle,
    };

    /// <summary>信封中的载荷数组名。与数据文件名对应。</summary>
    private static class Payloads
    {
        public const string Officers = "officers";
        public const string OfficerStates = "officerStates";
        public const string ExtendedBalance = "extendedBalance";
        public const string Cities = "cities";
        public const string CityStates = "cityStates";
        public const string Events = "events";
        public const string Specialties = "specialties";
        public const string Factions = "factions";
        public const string Difficulties = "difficulties";
    }

    private readonly string _dataDirectory;
    private readonly JsonSerializerOptions _options;

    /// <summary>使用默认数据目录。</summary>
    public JsonConfigLoader()
        : this(DefaultDataDirectory(), ConfigJson.Options)
    {
    }

    /// <summary>指定数据目录。</summary>
    public JsonConfigLoader(string dataDirectory)
        : this(dataDirectory, ConfigJson.Options)
    {
    }

    /// <summary>指定数据目录与序列化选项。</summary>
    public JsonConfigLoader(string dataDirectory, JsonSerializerOptions options)
    {
        _dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>数据目录路径。</summary>
    public string DataDirectory => _dataDirectory;

    /// <summary>全部数据文件名。</summary>
    public static IReadOnlyList<string> AllFiles { get; } = new[]
    {
        FileNames.Balance,
        FileNames.Officers,
        FileNames.Cities,
        FileNames.Events,
        FileNames.Specialties,
        FileNames.Factions,
        FileNames.Difficulty,
    };

    /// <summary>
    /// 默认数据目录。优先取环境变量，其次从程序目录向上查找 assets/data。
    /// </summary>
    public static string DefaultDataDirectory()
    {
        var fromEnv = Environment.GetEnvironmentVariable("STRATEGYGAME_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "assets", "data");
            if (System.IO.Directory.Exists(candidate)
                && System.IO.File.Exists(Path.Combine(candidate, FileNames.Balance)))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "找不到 assets/data 数据目录。已从程序目录向上逐级查找，"
            + "均未找到 assets/data/config.json。"
            + "请确认已执行构建（数据文件会随工程复制到输出目录），"
            + "或设置环境变量 STRATEGYGAME_DATA_DIR 指向数据目录。");
    }

    // ========================================================================
    //  IConfigLoader 实现
    // ========================================================================

    /// <summary>
    /// 加载全局平衡参数。
    /// </summary>
    /// <remarks>
    /// config.json 是唯一不使用信封的件：BalanceConfig 的字段直接铺在根节点，
    /// _comment / _todos 作为同级的额外键存在。这样策划打开文件第一眼
    /// 看到的就是参数名与数值，不需要先展开一层"config"。
    /// </remarks>
    public BalanceConfig LoadBalance()
        => DeserializeRoot<BalanceConfig>(FileNames.Balance);

    /// <summary>加载人才初始表。</summary>
    public List<OfficerTemplate> LoadOfficers()
        => DeserializePayload<List<OfficerTemplate>>(FileNames.Officers, Payloads.Officers);

    /// <summary>加载城池初始表。</summary>
    public List<CityTemplate> LoadCities()
        => DeserializePayload<List<CityTemplate>>(FileNames.Cities, Payloads.Cities);

    /// <summary>加载事件库。</summary>
    public List<GameEventTemplate> LoadEvents()
        => DeserializePayload<List<GameEventTemplate>>(FileNames.Events, Payloads.Events);

    // ========================================================================
    //  扩展加载（契约接口未覆盖，但本包数据文件包含）
    // ========================================================================

    /// <summary>加载人才开局状态（归属势力与任职）。</summary>
    public List<OfficerStateData> LoadOfficerStates()
        => DeserializePayload<List<OfficerStateData>>(FileNames.Officers, Payloads.OfficerStates);

    /// <summary>加载城池开局状态。</summary>
    public List<CityStateData> LoadCityStates()
        => DeserializePayload<List<CityStateData>>(FileNames.Cities, Payloads.CityStates);

    /// <summary>加载特质效果表。</summary>
    public List<SpecialtyTemplate> LoadSpecialties()
        => DeserializePayload<List<SpecialtyTemplate>>(FileNames.Specialties, Payloads.Specialties);

    /// <summary>加载势力初始表。</summary>
    public List<Faction> LoadFactions()
        => DeserializePayload<List<Faction>>(FileNames.Factions, Payloads.Factions);

    /// <summary>加载难度档位表。</summary>
    public List<DifficultyPreset> LoadDifficulties()
        => DeserializePayload<List<DifficultyPreset>>(FileNames.Difficulty, Payloads.Difficulties);

    /// <summary>加载 02 包自己的平衡细则（契约外的部分）。</summary>
    public ExtendedBalance LoadExtendedBalance()
        => DeserializePayload<ExtendedBalance>(FileNames.Balance, Payloads.ExtendedBalance);

    /// <summary>
    /// 加载全部名字词库，合并成一个 NameWordBank。
    /// </summary>
    /// <remarks>
    /// 词库文件各自结构不同（势力词是形容词/名词两段、称号是按枚举分组的字典），
    /// 所以逐文件按各自形状读取，而不是强行套同一个模型——那样会在词库里
    /// 塞进一堆字段名占位，策划看到会困惑。
    /// </remarks>
    public NameWordBank LoadNameWordBank()
    {
        var bank = new NameWordBank();

        var faction = DeserializeNameFile<NameFactionWordsFile>(NameFiles.FactionWords);
        bank.FactionAdjectives = faction.Adjectives;
        bank.FactionNouns = faction.Nouns;

        var city = DeserializeNameFile<NameCityFile>(NameFiles.CityPrefix);
        bank.CityPrefixes = city.Prefixes;
        bank.CitySuffixes = city.Suffixes;

        var surname = DeserializeNameList(NameFiles.OfficerSurname, "surnames");
        bank.OfficerSurnames = surname;

        var given = DeserializeNameList(NameFiles.OfficerGiven, "givenNames");
        bank.OfficerGivenNames = given;

        var ruler = DeserializeNameList(NameFiles.RulerGiven, "givenNames");
        bank.RulerGivenNames = ruler;

        var titles = DeserializeNameFile<NameSpecialtyTitleFile>(NameFiles.SpecialtyTitle);
        bank.SpecialtyTitles = titles.Titles;
        bank.Dialects = titles.Dialects;

        return bank;
    }

    /// <summary>读取词库文件中的单个词条数组。</summary>
    private List<string> DeserializeNameList(string fileName, string payloadName)
    {
        var path = Path.Combine(_dataDirectory, NamesSubDirectory, fileName);
        if (!System.IO.File.Exists(path))
        {
            throw new ConfigLoadException(
                $"词库文件 {fileName} 不存在。期望路径：{path}。"
                + "词库缺失会导致名字生成无法启动，不允许用空词库兜底。");
        }

        var json = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
        try
        {
            using var doc = JsonDocument.Parse(json,
                new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Skip });

            if (!doc.RootElement.TryGetProperty(payloadName, out var payload))
            {
                throw new ConfigLoadException(
                    $"词库文件 {fileName} 中缺少词条数组 \"{payloadName}\"。请确认键名拼写完全一致。");
            }

            var result = payload.Deserialize<List<string>>(_options);
            if (result is null)
            {
                throw new ConfigLoadException($"词库文件 {fileName} 的 \"{payloadName}\" 为 null。");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw Translate($"names/{fileName}#{payloadName}", ex);
        }
    }

    /// <summary>读取词库目录下的某个文件并反序列化。</summary>
    private T DeserializeNameFile<T>(string fileName, string? payloadName = null)
        where T : class
    {
        var path = Path.Combine(_dataDirectory, NamesSubDirectory, fileName);
        if (!System.IO.File.Exists(path))
        {
            throw new ConfigLoadException(
                $"词库文件 {fileName} 不存在。期望路径：{path}。"
                + "词库缺失会导致名字生成无法启动，不允许用空词库兜底。");
        }

        var json = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
        try
        {
            using var doc = JsonDocument.Parse(json,
                new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Skip });

            var target = doc.RootElement;
            if (payloadName is not null)
            {
                if (!doc.RootElement.TryGetProperty(payloadName, out var payload))
                {
                    throw new ConfigLoadException(
                        $"词库文件 {fileName} 中缺少载荷字段 \"{payloadName}\"。");
                }

                target = payload;
            }

            var result = target.Deserialize<T>(_options);
            if (result is null)
            {
                throw new ConfigLoadException($"词库文件 {fileName} 的内容为 null。");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw Translate($"names/{fileName}", ex);
        }
    }

    /// <summary>
    /// 一次性加载全部数据，附带原始 JSON 文本与 _comment / _todos 元信息。
    /// </summary>
    /// <remarks>
    /// 校验器需要原始文本才能做浮点数与 _comment 覆盖度检查，
    /// 所以这里把文本一并带出来，而不是只返回反序列化结果。
    /// </remarks>
    public GameDataSet LoadAll()
    {
        var data = new GameDataSet
        {
            Balance = LoadBalance(),
            Extended = LoadExtendedBalance(),
            Officers = LoadOfficers(),
            Cities = LoadCities(),
            Events = LoadEvents(),
            Specialties = LoadSpecialties(),
            Factions = LoadFactions(),
            Difficulties = LoadDifficulties(),
            OfficerStates = LoadOfficerStates(),
            CityStates = LoadCityStates(),
            Names = LoadNameWordBank(),
        };

        foreach (var file in AllFiles)
        {
            var json = ReadRaw(file);
            data.RawJson[file] = json;
            ExtractMeta(file, json, data);
        }

        return data;
    }

    // ========================================================================
    //  底层读写
    // ========================================================================

    /// <summary>读取原始 JSON 文本。</summary>
    public string ReadRaw(string fileName)
    {
        var path = Path.Combine(_dataDirectory, fileName);
        if (!System.IO.File.Exists(path))
        {
            throw new ConfigLoadException(
                $"数据文件 {fileName} 不存在。期望路径：{path}。"
                + "数据文件缺失必须报错，不允许用空配置继续启动。");
        }

        // 显式指定 UTF-8：数据文件由策划手工编辑，必须保证中文不乱码。
        return System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
    }

    /// <summary>把整个 JSON 文档反序列化为 T（用于 config.json 这种平铺文件）。</summary>
    public T DeserializeRoot<T>(string fileName)
    {
        var json = ReadRaw(fileName);
        try
        {
            var result = JsonSerializer.Deserialize<T>(json, _options);
            if (result is null)
            {
                throw new ConfigLoadException(
                    $"数据文件 {fileName} 的内容为 null。空配置会让所有数值退化为 0，"
                    + "属于严重错误，禁止以默认值继续。");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw Translate(fileName, ex);
        }
    }

    /// <summary>取出信封中的指定载荷数组并反序列化。</summary>
    public T DeserializePayload<T>(string fileName, string payloadName)
    {
        var json = ReadRaw(fileName);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json,
                new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw Translate(fileName, ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ConfigLoadException(
                    $"数据文件 {fileName} 的根节点必须是 JSON 对象（信封结构），实际为 {doc.RootElement.ValueKind}。"
                    + "请确认使用了 \"{ \\\"_comment\\\": ..., \\\"" + payloadName + "\\\": [...] }\" 的写法。");
            }

            if (!doc.RootElement.TryGetProperty(payloadName, out var payload))
            {
                throw new ConfigLoadException(
                    $"数据文件 {fileName} 中缺少载荷字段 \"{payloadName}\"。请确认键名拼写完全一致。");
            }

            try
            {
                var result = payload.Deserialize<T>(_options);
                if (result is null)
                {
                    throw new ConfigLoadException(
                        $"数据文件 {fileName} 的 \"{payloadName}\" 为 null。"
                        + "空列表会让该类实体整体消失，属于严重错误，禁止以默认值继续。");
                }

                return result;
            }
            catch (JsonException ex)
            {
                throw Translate($"{fileName}#{payloadName}", ex);
            }
        }
    }

    /// <summary>把 JsonException 翻译成人能看懂的话，不让堆栈直接砸到策划脸上。</summary>
    private static ConfigLoadException Translate(string where, JsonException ex)
    {
        var line = ex.LineNumber is null ? "未知" : ex.LineNumber.ToString();
        var col = ex.BytePositionInLine is null ? "未知" : ex.BytePositionInLine.ToString();
        return new ConfigLoadException(
            $"数据文件 {where} 解析失败：{ex.Message}（行 {line}，字节位置 {col}）。"
            + "常见原因：字段名拼错、数值写成了小数或带引号、枚举值不在契约定义内、末尾多了逗号。", ex);
    }

    /// <summary>序列化为 JSON 文本。</summary>
    public static string Serialize<T>(T value, bool indented = true)
    {
        var options = indented ? ConfigJson.Options : ConfigJson.CompactOptions;
        return JsonSerializer.Serialize(value, options);
    }

    /// <summary>
    /// 把数据写回磁盘。
    /// </summary>
    /// <remarks>
    /// 往返测试要验证"加载→序列化→再加载"数据完全一致；
    /// 策划改完数值也能用同一个入口存回去，避免手改 JSON 时缩进和字段名跑偏。
    /// </remarks>
    public void Save<T>(string fileName, T value)
        => System.IO.File.WriteAllText(
            Path.Combine(_dataDirectory, fileName),
            Serialize(value),
            new System.Text.UTF8Encoding(false));

    /// <summary>
    /// 从原始 JSON 提取 _comment 与 _todos。
    /// </summary>
    /// <remarks>
    /// 这两个字段是给策划看的元信息，不参与反序列化。但它们必须被校验器读出来：
    /// 没有 _comment 的文件要报错，占位 TODO 要上报。
    /// </remarks>
    private static void ExtractMeta(string fileName, string json, GameDataSet data)
    {
        try
        {
            using var doc = JsonDocument.Parse(json,
                new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Skip });

            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                switch (prop.Name)
                {
                    case "_comment" when prop.Value.ValueKind == JsonValueKind.String:
                        data.FileComments[fileName] = prop.Value.GetString() ?? string.Empty;
                        break;

                    case "_todos" when prop.Value.ValueKind == JsonValueKind.Array:
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var text = item.GetString();
                                if (!string.IsNullOrWhiteSpace(text)) data.DeclaredTodos.Add($"{fileName}: {text}");
                            }
                        }
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // 语法错误已由反序列化负责报错，这里不重复抛。
        }
    }
}
