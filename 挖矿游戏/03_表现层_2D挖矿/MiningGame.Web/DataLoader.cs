using System.Text.Json;
using MiningGame.Contracts;

namespace MiningGame.Web;

public static class DataLoader
{
    public static (MineGrid Grid, WorldConfig Config) Load(string dataDir)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };

        var gridPath = Path.Combine(dataDir, "mine_grid.json");
        var configPath = Path.Combine(dataDir, "world_config.json");

        if (!File.Exists(gridPath)) throw new FileNotFoundException("缺少地图数据", gridPath);
        if (!File.Exists(configPath)) throw new FileNotFoundException("缺少世界配置", configPath);

        var grid = JsonSerializer.Deserialize<MineGrid>(File.ReadAllText(gridPath), options)
                   ?? throw new InvalidDataException("mine_grid.json 反序列化失败");
        var config = JsonSerializer.Deserialize<WorldConfig>(File.ReadAllText(configPath), options)
                     ?? throw new InvalidDataException("world_config.json 反序列化失败");

        if (grid.Cells.Count != grid.Width * grid.Height)
            throw new InvalidDataException($"网格数量不一致：{grid.Cells.Count} != {grid.Width * grid.Height}");

        return (grid, config);
    }
}
