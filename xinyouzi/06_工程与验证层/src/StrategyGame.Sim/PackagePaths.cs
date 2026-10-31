// ============================================================================
//  包路径定位 —— 06 包内所有输出与临时文件都必须落在本包目录内
//  【环境铁律】禁止向 C 盘写任何文件。测试临时文件用本包 .tmp/，
//  CSV 默认输出用本包 out/。因此需要一个稳定的「包根」定位方式。
// ============================================================================

namespace StrategyGame.Sim;

/// <summary>
/// 定位 06 包的根目录。
/// </summary>
/// <remarks>
/// 【定位策略】从给定起点目录逐级向上找，同时含「接口契约_共享类型」与「src」
/// 两个子目录的那一层即包根。这比依赖固定层数稳（bin 层级会随配置变化）。
/// </remarks>
public static class PackagePaths
{
    /// <summary>包根目录名。</summary>
    public const string PackageFolderName = "06_工程与验证层";

    /// <summary>契约目录名（包根的标志之一）。</summary>
    private const string ContractsFolderName = "接口契约_共享类型";

    /// <summary>源码目录名（包根的标志之一）。</summary>
    private const string SrcFolderName = "src";

    /// <summary>临时目录名。</summary>
    public const string TempFolderName = ".tmp";

    /// <summary>输出目录名。</summary>
    public const string OutputFolderName = "out";

    /// <summary>
    /// 查找包根目录。
    /// </summary>
    /// <returns>包根的绝对路径。</returns>
    /// <exception cref="DirectoryNotFoundException">找不到包根时抛出。</exception>
    public static string FindPackageRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (IsPackageRoot(dir.FullName))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            $"未能定位 {PackageFolderName} 包根目录（向上查找同时含「{ContractsFolderName}」与「{SrcFolderName}」的目录）。");
    }

    /// <summary>
    /// 判断某目录是否为包根。
    /// </summary>
    /// <param name="path">目录绝对路径。</param>
    /// <returns>是则返回 true。</returns>
    public static bool IsPackageRoot(string path)
        => Directory.Exists(Path.Combine(path, ContractsFolderName))
           && Directory.Exists(Path.Combine(path, SrcFolderName));

    /// <summary>
    /// 取包内的目录绝对路径，必要时创建。
    /// </summary>
    /// <param name="folderName">子目录名。</param>
    /// <returns>绝对路径。</returns>
    public static string GetOrCreateFolder(string folderName)
    {
        string path = Path.Combine(FindPackageRoot(), folderName);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 取包内临时目录（测试临时文件专用，禁止使用 Path.GetTempPath）。
    /// </summary>
    /// <returns>临时目录绝对路径。</returns>
    public static string GetTempFolder() => GetOrCreateFolder(TempFolderName);

    /// <summary>
    /// 取包内输出目录。
    /// </summary>
    /// <returns>输出目录绝对路径。</returns>
    public static string GetOutputFolder() => GetOrCreateFolder(OutputFolderName);

    /// <summary>
    /// 把命令行传入的输出路径解析为绝对路径。
    /// </summary>
    /// <param name="path">用户传入路径。空则用包内 out/result.csv。</param>
    /// <returns>绝对路径。</returns>
    public static string ResolveOutputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Path.Combine(GetOutputFolder(), "result.csv");
        }

        return Path.GetFullPath(path);
    }
}
