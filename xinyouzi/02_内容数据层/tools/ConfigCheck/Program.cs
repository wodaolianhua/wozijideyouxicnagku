// ============================================================================
//  ConfigCheck —— 命令行配置校验工具
// ============================================================================
//  【给谁用】
//  策划改完 JSON，跑这一条命令就知道有没有写错，不需要打开 IDE、不需要跑测试。
//  CI 也会跑它——数据校验不通过，构建就该失败。
//
//  【退出码约定】
//  0 = 校验通过（可能含警告）
//  1 = 存在错误，数据非法，禁止启动
//  2 = 加载失败（文件缺失 / JSON 语法错）
// ============================================================================

using System.Text;
using StrategyGame.Data.Config;

namespace StrategyGame.Data.Tools;

public static class Program
{
    public static int Main(string[] args)
    {
        // 控制台默认 GBK，中文会乱码。显式切到 UTF-8。
        Console.OutputEncoding = Encoding.UTF8;

        var quiet = args.Contains("--quiet");
        var positional = args.Where(a => !a.StartsWith("--")).ToArray();

        string dataDir;
        try
        {
            dataDir = positional.Length > 0
                ? Path.GetFullPath(positional[0])
                : JsonConfigLoader.DefaultDataDirectory();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[加载失败] {ex.Message}");
            return 2;
        }

        GameDataSet data;
        try
        {
            var loader = new JsonConfigLoader(dataDir);
            data = loader.LoadAll();
        }
        catch (ConfigLoadException ex)
        {
            Console.Error.WriteLine("========================================");
            Console.Error.WriteLine("[加载失败] 数据文件无法解析，禁止启动。");
            Console.Error.WriteLine("========================================");
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var result = ConfigValidator.Validate(data);

        if (quiet)
        {
            Console.WriteLine(result.HasErrors
                ? $"校验失败：{result.Errors.Count()} 个错误"
                : $"校验通过（{result.Warnings.Count()} 个警告）");
            return result.HasErrors ? 1 : 0;
        }

        Console.WriteLine("========================================");
        Console.WriteLine($"数据目录：{dataDir}");
        Console.WriteLine($"数据统计：城池 {data.Cities.Count}，人才 {data.Officers.Count}，"
                          + $"事件 {data.Events.Count}，特质 {data.Specialties.Count}，"
                          + $"势力 {data.Factions.Count}，难度 {data.Difficulties.Count}");
        var names = data.Names;
        Console.WriteLine($"词库统计：势力形容词 {names.FactionAdjectives.Count}，"
                          + $"势力名词 {names.FactionNouns.Count}，"
                          + $"城池前缀 {names.CityPrefixes.Count}，城池后缀 {names.CitySuffixes.Count}，"
                          + $"姓氏 {names.OfficerSurnames.Count}，人才名 {names.OfficerGivenNames.Count}，"
                          + $"君主名 {names.RulerGivenNames.Count}，"
                          + $"称号 {names.SpecialtyTitles.Values.Sum(t => t.Count)}，方言 {names.Dialects.Count}");
        Console.WriteLine($"玩家期望城池数：{data.Extended.ExpectedPlayerCityCount}（来自 config.json，非写死）");
        Console.WriteLine("========================================");
        Console.WriteLine(result.ToReport());

        // 把占位 TODO 一并列出，提醒定稿后要回来处理
        if (data.DeclaredTodos.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"---- 占位待办（{data.DeclaredTodos.Count} 项）----");
            foreach (var todo in data.DeclaredTodos) Console.WriteLine("  · " + todo);
        }

        return result.HasErrors ? 1 : 0;
    }
}
