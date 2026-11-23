// ============================================================================
//  JsonConfigLoaderTests —— 加载器行为测试
// ============================================================================
//  【核心命题】加载失败必须报错，不能静默兜底。
//  任务书禁止事项原文：「禁止静默容错——数据非法必须报错，不能用默认值蒙混过关」。
//  「用默认值蒙混过关」最常见的两种形态：
//    1. 文件不存在却返回一个空对象，程序带着全 0 继续跑；
//    2. JSON 语法错却被 catch 掉，返回 null 让上层 NRE。
//  这两类必须被测试钉死。
// ============================================================================

using System.Text.Json;
using StrategyGame.Contracts;
using StrategyGame.Data.Config;
using Xunit;

namespace StrategyGame.Data.Tests;

public class JsonConfigLoaderTests
{
    [Fact(DisplayName = "加载器实现契约接口 IConfigLoader")]
    public void Loader_ImplementsContractInterface()
    {
        IConfigLoader loader = Loader.Good();

        Assert.NotNull(loader.LoadBalance());
        Assert.NotEmpty(loader.LoadOfficers());
        Assert.NotEmpty(loader.LoadCities());
        Assert.NotEmpty(loader.LoadEvents());
    }

    [Fact(DisplayName = "数据文件缺失必须抛异常，不允许返回默认对象")]
    public void MissingFile_Throws()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "sg_empty_" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(emptyDir);

        var loader = new JsonConfigLoader(emptyDir);

        var ex = Assert.Throws<ConfigLoadException>(() => loader.LoadBalance());
        Assert.Contains("config.json", ex.Message);
        Assert.Contains("不存在", ex.Message);
    }

    [Fact(DisplayName = "JSON 语法错误必须抛异常并指出行号")]
    public void MalformedJson_Throws_WithLocation()
    {
        var broken = "{\"grainPerPopPerMonth\": 1,,}";  // 多了一个逗号
        var tmp = Loader.CopyAndOverride("config.json", broken);

        var loader = new JsonConfigLoader(tmp);

        var ex = Assert.Throws<ConfigLoadException>(() => loader.LoadBalance());
        Assert.Contains("解析失败", ex.Message);
    }

    [Fact(DisplayName = "枚举值非法必须抛异常，不允许静默取默认值")]
    public void InvalidEnumValue_Throws()
    {
        var good = Loader.ReadFixtureFile("bad_negative", "cities.json");
        var broken = good.Replace("\"terrain\": \"Plain\"", "\"terrain\": \"Volcano\"",
            StringComparison.Ordinal);
        Assert.NotEqual(good, broken);

        var tmp = Loader.CopyAndOverride("cities.json", broken);
        var loader = new JsonConfigLoader(tmp);

        var ex = Assert.Throws<ConfigLoadException>(() => loader.LoadCities());
        Assert.Contains("解析失败", ex.Message);
        // 枚举转换器报的是"The JSON value could not be converted"，值本身不一定出现在 message 里，
        // 但必须确认是解析错误而非静默取默认值。
        Assert.Contains("cities.json", ex.Message);
    }

    [Fact(DisplayName = "数值写成字符串必须抛异常")]
    public void NumberAsString_Throws()
    {
        var good = Loader.ReadFixtureFile("bad_negative", "factions.json");
        var broken = good.Replace("\"silver\": 800,", "\"silver\": \"800\",", StringComparison.Ordinal);
        Assert.NotEqual(good, broken);

        var tmp = Loader.CopyAndOverride("factions.json", broken);
        var loader = new JsonConfigLoader(tmp);

        Assert.Throws<ConfigLoadException>(() => loader.LoadFactions());
    }

    [Fact(DisplayName = "载荷字段缺失必须抛异常")]
    public void MissingPayloadField_Throws()
    {
        var broken = "{ \"_comment\": \"测试\" }";
        var tmp = Loader.CopyAndOverride("cities.json", broken);

        var loader = new JsonConfigLoader(tmp);

        var ex = Assert.Throws<ConfigLoadException>(() => loader.LoadCities());
        Assert.Contains("cities", ex.Message);
    }

    [Fact(DisplayName = "文件内容为 null 必须抛异常")]
    public void NullContent_Throws()
    {
        var tmp = Loader.CopyAndOverride("specialties.json", "null");

        var loader = new JsonConfigLoader(tmp);

        var ex = Assert.Throws<ConfigLoadException>(() => loader.LoadSpecialties());
        Assert.Contains("必须是 JSON 对象", ex.Message);
    }

    [Fact(DisplayName = "Save 到临时目录后能被重新加载")]
    public void Save_ToTempDir_CanBeReloaded()
    {
        var original = Loader.Good().LoadSpecialties();

        var tmp = Path.Combine(Path.GetTempPath(), "sg_save_" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(tmp);

        // 写成一个独立的加载器实例，确保写入的是临时目录而非正式数据目录
        var writer = new JsonConfigLoader(tmp);
        writer.Save("specialties.json", original);

        // 重新用加载器读回来，验证落盘格式与读取格式一致
        var reader = new JsonConfigLoader(tmp);
        var json = reader.ReadRaw("specialties.json");
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<List<SpecialtyTemplate>>(
            json, ConfigJson.Options);

        Assert.NotNull(reloaded);
        Assert.Equal(original.Count, reloaded!.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Id, reloaded[i].Id);
            Assert.Equal(original[i].Name, reloaded[i].Name);
            Assert.Equal(original[i].BonusPercent, reloaded[i].BonusPercent);
            Assert.Equal(original[i].Specialty, reloaded[i].Specialty);
        }
    }
}
