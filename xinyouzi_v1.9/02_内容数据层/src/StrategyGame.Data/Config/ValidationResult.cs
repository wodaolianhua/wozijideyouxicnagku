// ============================================================================
//  StrategyGame.Data —— 校验结果模型
// ============================================================================

namespace StrategyGame.Data.Config;

/// <summary>校验问题的严重级别。</summary>
public enum ValidationSeverity
{
    /// <summary>提示。不阻止启动，但策划应该看一眼。</summary>
    Info = 0,

    /// <summary>警告。数据能用但可疑。</summary>
    Warning = 1,

    /// <summary>错误。数据非法，必须修，不允许用默认值蒙混过关。</summary>
    Error = 2,
}

/// <summary>一条校验问题。</summary>
public sealed class ValidationIssue
{
    public ValidationIssue(ValidationSeverity severity, string code, string message, string location)
    {
        Severity = severity;
        Code = code;
        Message = message;
        Location = location;
    }

    /// <summary>严重级别。</summary>
    public ValidationSeverity Severity { get; }

    /// <summary>问题分类码。便于测试断言与聚合统计，例如 "CITY_ADJ_MISSING"。</summary>
    public string Code { get; }

    /// <summary>面向策划的中文可读描述。</summary>
    public string Message { get; }

    /// <summary>问题位置。定位到具体文件与字段，例如 "cities.json#cities[2].population"。</summary>
    public string Location { get; }

    /// <summary>可读单行输出。</summary>
    public override string ToString()
    {
        var level = Severity switch
        {
            ValidationSeverity.Error => "错误",
            ValidationSeverity.Warning => "警告",
            _ => "提示",
        };
        return $"[{level}] {Location} —— {Message}（{Code}）";
    }
}

/// <summary>
/// 校验结果。承载全部问题，收集而非抛出。
/// </summary>
/// <remarks>
/// 【为什么收集而不是抛异常】策划改错一个数可能同时触发十处校验。
/// 如果第一次不匹配就抛异常，策划只能一次次改、一次次重跑，效率极低。
/// 所以校验器一次把所有问题全报出来，由调用方决定是否中断启动。
/// </remarks>
public sealed class ValidationResult
{
    private readonly List<ValidationIssue> _issues = new();

    /// <summary>全部问题，按发现顺序排列。</summary>
    public IReadOnlyList<ValidationIssue> Issues => _issues;

    /// <summary>是否存在错误级问题。true 表示数据非法，禁止启动。</summary>
    public bool HasErrors => _issues.Any(i => i.Severity == ValidationSeverity.Error);

    /// <summary>仅错误级问题。</summary>
    public IEnumerable<ValidationIssue> Errors =>
        _issues.Where(i => i.Severity == ValidationSeverity.Error);

    /// <summary>仅警告级问题。</summary>
    public IEnumerable<ValidationIssue> Warnings =>
        _issues.Where(i => i.Severity == ValidationSeverity.Warning);

    /// <summary>追加一条问题。</summary>
    public void Add(ValidationIssue issue) => _issues.Add(issue);

    /// <summary>追加一条错误。</summary>
    /// <param name="code">问题分类码。</param>
    /// <param name="location">问题位置：文件 + 字段路径。</param>
    /// <param name="message">面向策划的中文描述。</param>
    public void Error(string code, string location, string message) =>
        _issues.Add(new ValidationIssue(ValidationSeverity.Error, code, message, location));

    /// <summary>追加一条警告。</summary>
    public void Warn(string code, string location, string message) =>
        _issues.Add(new ValidationIssue(ValidationSeverity.Warning, code, message, location));

    /// <summary>追加一条提示。</summary>
    public void Info(string code, string location, string message) =>
        _issues.Add(new ValidationIssue(ValidationSeverity.Info, code, message, location));

    /// <summary>
    /// 生成多行可读报告。策划直接看这段字就能定位问题，不需要看堆栈。
    /// </summary>
    public string ToReport()
    {
        if (_issues.Count == 0) return "配置校验通过，未发现问题。";

        var lines = new List<string>
        {
            $"配置校验完成：共 {_issues.Count} 个问题（错误 {Errors.Count()}，警告 {Warnings.Count()}）。",
            new string('-', 60),
        };
        foreach (var issue in _issues) lines.Add(issue.ToString());
        lines.Add(new string('-', 60));
        lines.Add(HasErrors ? "结论：数据非法，禁止启动。" : "结论：数据可用，但存在需要确认的警告。");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>数量统计，供测试与调用方快速判断。</summary>
    public int CountByCode(string code) => _issues.Count(i => i.Code == code);
}
