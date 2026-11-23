// ============================================================================
//  测试公共辅助
// ============================================================================
//  【为什么需要把坏数据做成 fixture 文件，而不是在代码里 new 一个对象】
//  校验器最怕的 bug 是「只在内存对象上生效，真实 JSON 文件走不到」。
//  所以坏数据必须以真实文件形式存在，让测试完整走一遍
//  读取 → 反序列化 → 校验 的生产链路。
// ============================================================================

using StrategyGame.Contracts;
using StrategyGame.Data.Config;
using Xunit;

namespace StrategyGame.Data.Tests;

/// <summary>定位数据目录与测试样本目录。</summary>
public static class TestPaths
{
    /// <summary>项目内的 assets/data 目录（交付的真实数据）。</summary>
    public static string GoodDataDir { get; } = LocateGoodData();

    /// <summary>测试样本目录（故意写坏的数据）。</summary>
    public static string FixtureDir { get; } = LocateFixture();

    private static string LocateGoodData()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "assets", "data");
            if (System.IO.File.Exists(Path.Combine(candidate, JsonConfigLoader.FileNames.Balance)))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("测试未能定位到 assets/data 目录。");
    }

    private static string LocateFixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures");
            if (System.IO.Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("测试未能定位到 fixtures 目录。");
    }
}

/// <summary>
/// 加载器工厂。
/// </summary>
/// <remarks>
/// 关键设计：LoadGood 每次返回一个全新反序列化结果，测试之间互不污染。
/// LoadFixture 把 fixture 目录里的文件拷进临时目录再加载——因为坏数据文件
/// 通常只坏一个字段，其余字段仍需合法才能走到被测的那条校验规则。
/// </remarks>
public static class Loader
{
    /// <summary>加载交付的真实数据。</summary>
    public static JsonConfigLoader Good()
        => new(TestPaths.GoodDataDir);

    /// <summary>加载真实数据并执行全部校验。</summary>
    public static (GameDataSet Data, ValidationResult Result) GoodValidated()
    {
        var loader = Good();
        var data = loader.LoadAll();
        return (data, ConfigValidator.Validate(data));
    }

    /// <summary>
    /// 把 fixture 目录复制成一份可加载的数据集，并覆盖指定的坏文件。
    /// </summary>
    /// <param name="fixtureName">fixtures 下的子目录名。</param>
    public static JsonConfigLoader FromFixture(string fixtureName)
    {
        var src = Path.Combine(TestPaths.FixtureDir, fixtureName);
        if (!System.IO.Directory.Exists(src))
            throw new DirectoryNotFoundException($"fixture 目录不存在：{src}");

        return new JsonConfigLoader(src);
    }

    /// <summary>把生产数据复制到临时目录，并用一个坏文件覆盖其中的某个文件。</summary>
    /// <param name="fileName">要覆盖的文件名，如 cities.json。</param>
    /// <param name="content">覆盖用的内容。</param>
    public static string CopyAndOverride(string fileName, string content)
    {
        var tmp = Path.Combine(Path.GetTempPath(),
            "sg_test_" + Guid.NewGuid().ToString("N")[..10]);
        System.IO.Directory.CreateDirectory(tmp);

        foreach (var src in System.IO.Directory.GetFiles(TestPaths.GoodDataDir, "*.json"))
        {
            System.IO.File.Copy(src, Path.Combine(tmp, Path.GetFileName(src)));
        }

        // 词库目录必须一起复制：LoadAll 会加载 names/，
        // 缺了它测试抛的是「文件缺失」而不是被测的那条校验规则，测就白测了。
        var namesDir = Path.Combine(TestPaths.GoodDataDir, "names");
        var namesDst = Path.Combine(tmp, "names");
        System.IO.Directory.CreateDirectory(namesDst);
        foreach (var src in System.IO.Directory.GetFiles(namesDir, "*.json"))
        {
            System.IO.File.Copy(src, Path.Combine(namesDst, Path.GetFileName(src)));
        }

        System.IO.File.WriteAllText(Path.Combine(tmp, fileName), content, new System.Text.UTF8Encoding(false));
        return tmp;
    }

    /// <summary>读取 fixture 目录下的某个文件内容。</summary>
    public static string ReadFixtureFile(string fixtureName, string fileName)
    {
        var path = Path.Combine(TestPaths.FixtureDir, fixtureName, fileName);
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException($"fixture 文件不存在：{path}");
        return System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
    }
}

/// <summary>常用断言辅助。</summary>
public static class Assert2
{
    /// <summary>断言校验结果中存在指定分类码的错误。</summary>
    public static ValidationIssue HasError(ValidationResult result, string code)
    {
        Assert.True(result.HasErrors,
            $"期望存在错误级问题 {code}，但校验结果中没有错误。完整报告：\n{result.ToReport()}");

        var issue = result.Errors.FirstOrDefault(i => i.Code == code);
        Assert.True(issue is not null,
            $"期望存在错误 {code}，实际错误为：{string.Join(", ", result.Errors.Select(e => e.Code))}");

        return issue!;
    }

    /// <summary>断言校验结果中不存在指定分类码的问题（任何级别都不允许）。</summary>
    public static void NoIssue(ValidationResult result, string code)
    {
        var found = result.Issues.Where(i => i.Code == code).ToList();
        Assert.True(found.Count == 0,
            $"不应出现 {code}，实际出现 {found.Count} 次：{string.Join(" | ", found.Select(f => f.ToString()))}");
    }
}
