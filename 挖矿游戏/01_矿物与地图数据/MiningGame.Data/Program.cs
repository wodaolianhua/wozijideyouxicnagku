using System.Text.Encodings.Web;
using System.Text.Json;
using MiningGame.Contracts;

// 用法（PowerShell）：
//   dotnet run --project MiningGame.Data -- gen       生成 data/*.json
//   dotnet run --project MiningGame.Data -- validate  校验数据
var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "validate";
var dataDir = args.Length > 1 ? Path.GetFullPath(args[1]) : FindDataDir();

if (dataDir == null)
{
    Console.Error.WriteLine("找不到 01_矿物与地图数据/data 目录");
    return 2;
}

return mode switch
{
    "gen" => Generate(dataDir),
    "validate" => Validate(dataDir),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("用法: gen | validate [dataDir]");
    return 2;
}

static string? FindDataDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "分发契约.sh")))
            return Path.Combine(dir.FullName, "01_矿物与地图数据", "data");
        var candidate = Path.Combine(dir.FullName, "01_矿物与地图数据", "data");
        if (Directory.Exists(Path.Combine(dir.FullName, "01_矿物与地图数据")))
            return candidate;
    }
    return null;
}

static JsonSerializerOptions JsonOpts() => new()
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

static int Generate(string dataDir)
{
    const int width = 36;
    const int height = 30;
    const int surfaceY = 3;
    const int shaftX = 17;

    var rng = new Random(20261128);
    var cells = new Cell[width * height];

    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            var cell = new Cell { X = x, Y = y, Mineral = MineralType.Soil };
            if (y <= surfaceY)
            {
                cell.Tile = TileType.Air;
                cell.Hardness = 0;
            }
            else if (y >= height - 2 || x == 0 || x == width - 1)
            {
                cell.Tile = TileType.Bedrock;
                cell.Hardness = 99;
            }
            else
            {
                cell.Tile = TileType.Dirt;
                cell.Hardness = 1;
                // 越深越多石头，逼玩家升级工具
                if (y >= 9 && rng.Next(100) < Math.Min(70, (y - 8) * 8))
                {
                    cell.Tile = TileType.Stone;
                    cell.Hardness = 2;
                }
            }
            cells[y * width + x] = cell;
        }
    }

    // 开局竖井：从地表往下 3 格是空的，玩家能立刻下井
    for (int y = surfaceY + 1; y <= surfaceY + 3 && y < height; y++)
    {
        SetAir(cells, width, shaftX, y);
    }

    // 各矿物按深度分层铺矿脉（越深越硬越贵）
    var layers = new (MineralType Mineral, int Hardness, int MinY, int MaxY, int Veins)[]
    {
        (MineralType.Coal, 2, 5, 11, 12),
        (MineralType.Iron, 3, 9, 17, 10),
        (MineralType.Copper, 4, 14, 22, 9),
        (MineralType.Silver, 5, 19, 26, 8),
        (MineralType.Gold, 6, 23, 28, 7),
        (MineralType.RareCrystal, 7, 26, 28, 4),
    };

    foreach (var layer in layers)
    {
        int placed = 0, attempts = 0;
        while (placed < layer.Veins && attempts < 800)
        {
            attempts++;
            int cx = rng.Next(2, width - 2);
            int cy = rng.Next(layer.MinY, Math.Min(layer.MaxY, height - 3) + 1);
            int veinW = rng.Next(2, 4);
            int veinH = rng.Next(1, 3);

            bool ok = true;
            for (int dy = 0; dy < veinH && ok; dy++)
            {
                for (int dx = 0; dx < veinW && ok; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x <= 0 || x >= width - 1 || y <= surfaceY || y >= height - 2) { ok = false; break; }
                    var c = cells[y * width + x];
                    if (c.Tile == TileType.Bedrock) ok = false;
                }
            }
            if (!ok) continue;

            for (int dy = 0; dy < veinH; dy++)
            {
                for (int dx = 0; dx < veinW; dx++)
                {
                    var c = cells[(cy + dy) * width + (cx + dx)];
                    c.Tile = TileType.Ore;
                    c.Mineral = layer.Mineral;
                    c.Hardness = layer.Hardness;
                    c.OreAmount = rng.Next(2, 6);
                }
            }
            placed++;
        }
    }

    // 井口旁边固定放两小片煤矿，保证第一次下井就能看到矿
    PlaceVein(cells, width, surfaceY, shaftX - 3, surfaceY + 2, 2, 1, MineralType.Coal, 2, 4);
    PlaceVein(cells, width, surfaceY, shaftX + 2, surfaceY + 3, 2, 1, MineralType.Coal, 2, 4);
    // 竖井正下方铺一格煤矿：走到井底再往下一挖就能出货，第一次玩立刻有反馈
    PlaceVein(cells, width, surfaceY, shaftX, surfaceY + 4, 2, 1, MineralType.Coal, 2, 4);

    // 每种矿物都保证至少出现一次
    foreach (var layer in layers)
    {
        bool exists = cells.Any(c => c.Tile == TileType.Ore && c.Mineral == layer.Mineral);
        if (!exists)
        {
            PlaceVein(cells, width, surfaceY, shaftX - 5, Math.Max(layer.MinY, surfaceY + 2), 2, 1,
                layer.Mineral, layer.Hardness, 3);
        }
    }

    var grid = new MineGrid
    {
        Width = width,
        Height = height,
        SurfaceY = surfaceY,
        ShaftX = shaftX,
        Cells = cells.ToList(),
    };

    var config = BuildWorldConfig();

    Directory.CreateDirectory(dataDir);
    File.WriteAllText(Path.Combine(dataDir, "mine_grid.json"), JsonSerializer.Serialize(grid, JsonOpts()));
    File.WriteAllText(Path.Combine(dataDir, "world_config.json"), JsonSerializer.Serialize(config, JsonOpts()));

    Console.WriteLine($"已生成: {Path.Combine(dataDir, "mine_grid.json")}");
    Console.WriteLine($"已生成: {Path.Combine(dataDir, "world_config.json")}");
    Console.WriteLine($"网格 {width}x{height}，地表 y={surfaceY}，井口 x={shaftX}");
    return 0;
}

static void PlaceVein(Cell[] cells, int width, int surfaceY, int x, int y, int w, int h,
    MineralType mineral, int hardness, int amount)
{
    for (int dy = 0; dy < h; dy++)
    {
        for (int dx = 0; dx < w; dx++)
        {
            int cx = x + dx, cy = y + dy;
            if (cx <= 0 || cx >= width - 1 || cy <= surfaceY) continue;
            int index = cy * width + cx;
            if (index < 0 || index >= cells.Length) continue;
            var c = cells[index];
            if (c.Tile == TileType.Bedrock) continue;
            c.Tile = TileType.Ore;
            c.Mineral = mineral;
            c.Hardness = hardness;
            c.OreAmount = amount;
        }
    }
}

static void SetAir(Cell[] cells, int width, int x, int y)
{
    var cell = cells[y * width + x];
    cell.Tile = TileType.Air;
    cell.Hardness = 0;
    cell.OreAmount = 0;
}

static WorldConfig BuildWorldConfig() => new()
{
    StartingMoney = 45,
    BackpackCapacity = 24,
    MaxStamina = 40,
    SurfaceStaminaRegen = 2,
    Prices = new Dictionary<int, int>
    {
        { (int)MineralType.Soil, 1 },
        { (int)MineralType.Coal, 7 },
        { (int)MineralType.Iron, 16 },
        { (int)MineralType.Copper, 30 },
        { (int)MineralType.Silver, 62 },
        { (int)MineralType.Gold, 135 },
        { (int)MineralType.RareCrystal, 330 },
    },
    ToolShop = new List<ToolSpec>
    {
        new() { Kind = ToolKind.BareHands, Name = "徒手", Power = 1, Price = 0 },
        new() { Kind = ToolKind.Pickaxe, Name = "镐", Power = 3, Price = 30 },
        new() { Kind = ToolKind.Drill, Name = "电钻", Power = 6, Price = 180 },
        new() { Kind = ToolKind.Excavator, Name = "挖掘机", Power = 8, Price = 900 },
    },
    WorkerShop = new List<WorkerSpec>
    {
        new() { Kind = WorkerKind.Robot, Name = "机器人", Power = 3, Interval = 3, Wage = 0, Price = 220 },
        new() { Kind = WorkerKind.HiredMiner, Name = "雇工", Power = 6, Interval = 2, Wage = 2, Price = 150 },
    },
    PolishSettings = new PolishConfig
    {
        GradeWeights = new List<int> { 60, 26, 10, 3, 1 },
        GradePricePercent = new List<int> { 100, 140, 190, 260, 360 },
        GradeBonusPerTier = 2,
        UnitsPerAction = 5,
        StaminaPerUnit = 1,
    },
};

static int Validate(string dataDir)
{
    int pass = 0, fail = 0;
    void Check(string name, Func<bool> predicate, string? detail = null)
    {
        bool ok;
        try { ok = predicate(); }
        catch (Exception ex) { ok = false; detail = ex.Message; }
        if (ok) { pass++; Console.WriteLine($"PASS  {name}"); }
        else { fail++; Console.WriteLine($"FAIL  {name}{(string.IsNullOrEmpty(detail) ? "" : "  -> " + detail)}"); }
    }

    MineGrid grid;
    WorldConfig config;
    try
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
        grid = JsonSerializer.Deserialize<MineGrid>(File.ReadAllText(Path.Combine(dataDir, "mine_grid.json")), opts)!;
        config = JsonSerializer.Deserialize<WorldConfig>(File.ReadAllText(Path.Combine(dataDir, "world_config.json")), opts)!;
    }
    catch (Exception ex)
    {
        Console.WriteLine("FAIL  数据文件读取/反序列化 -> " + ex.Message);
        Console.WriteLine("结果: 0 PASS / 1 FAIL");
        return 1;
    }

    Check("网格尺寸与 Cell 数量一致", () => grid.Cells.Count == grid.Width * grid.Height,
        $"{grid.Cells.Count} != {grid.Width}x{grid.Height}");
    Check("Cell 坐标与下标一致", () => grid.Cells.Select((c, i) => c.X == i % grid.Width && c.Y == i / grid.Width).All(v => v));
    Check("地表行是空气", () => Enumerable.Range(0, grid.Width)
        .All(x => grid.Cells[grid.SurfaceY * grid.Width + x].Tile == TileType.Air));
    Check("井口位置是空气", () =>
    {
        var c = grid.Cells[grid.SurfaceY * grid.Width + grid.ShaftX];
        return c.Tile == TileType.Air;
    });
    Check("左右与底部有基岩封边", () =>
    {
        for (int y = grid.SurfaceY + 1; y < grid.Height; y++)
        {
            if (grid.Cells[y * grid.Width].Tile != TileType.Bedrock) return false;
            if (grid.Cells[y * grid.Width + grid.Width - 1].Tile != TileType.Bedrock) return false;
        }
        for (int y = grid.Height - 2; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (grid.Cells[y * grid.Width + x].Tile != TileType.Bedrock) return false;
        return true;
    });
    Check("每种矿物（除泥土）都有矿脉", () =>
    {
        foreach (MineralType m in Enum.GetValues<MineralType>().Where(m => m != MineralType.Soil))
            if (!grid.Cells.Any(c => c.Tile == TileType.Ore && c.Mineral == m)) return false;
        return true;
    });
    Check("矿脉硬度>0 且产量>0", () => grid.Cells.Where(c => c.Tile == TileType.Ore).All(c => c.Hardness > 0 && c.OreAmount > 0));
    Check("越深矿物越值钱（价格随矿物等级递增）", () =>
    {
        for (int i = 1; i < 7; i++)
        {
            if (!config.Prices.TryGetValue(i - 1, out int prev) || !config.Prices.TryGetValue(i, out int cur)) return false;
            if (cur <= prev) return false;
        }
        return true;
    });
    Check("单价表覆盖全部矿物", () => Enum.GetValues<MineralType>().All(m => config.Prices.ContainsKey((int)m)));
    Check("工具能覆盖对应深度", () =>
    {
        int Power(ToolKind kind) => config.ToolShop.First(t => t.Kind == kind).Power;
        int Hardness(MineralType m) => grid.Cells.Where(c => c.Tile == TileType.Ore && c.Mineral == m)
            .Select(c => c.Hardness).DefaultIfEmpty(0).Max();

        return Power(ToolKind.BareHands) >= 1
            && Power(ToolKind.Pickaxe) >= Hardness(MineralType.Iron)
            && Power(ToolKind.Drill) >= Hardness(MineralType.Gold)
            && Power(ToolKind.Excavator) >= Hardness(MineralType.RareCrystal);
    });
    Check("启动资金够买第一把镐", () => config.StartingMoney >= config.ToolShop.First(t => t.Kind == ToolKind.Pickaxe).Price);
    Check("成色权重覆盖 5 档", () => config.PolishSettings.GradeWeights.Count == Enum.GetValues<QualityGrade>().Length
        && config.PolishSettings.GradeWeights.All(w => w >= 0) && config.PolishSettings.GradeWeights.Any(w => w > 0));
    Check("成色价格百分比 5 档且递增", () =>
    {
        var p = config.PolishSettings.GradePricePercent;
        return p.Count == Enum.GetValues<QualityGrade>().Length
            && p[0] >= 100
            && Enumerable.Range(1, p.Count - 1).All(i => p[i] > p[i - 1]);
    });
    Check("打磨参数合法", () => config.PolishSettings.UnitsPerAction > 0
        && config.PolishSettings.StaminaPerUnit > 0
        && config.PolishSettings.GradeBonusPerTier >= 0);
    Check("背包容量/体力上限为正", () => config.BackpackCapacity > 0 && config.MaxStamina > 0);

    Check("所有矿脉从井口可达（不封死）", () =>
    {
        var seen = new bool[grid.Width * grid.Height];
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((grid.ShaftX, grid.SurfaceY));
        seen[grid.SurfaceY * grid.Width + grid.ShaftX] = true;

        var dirs = new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= grid.Width || ny >= grid.Height) continue;
                int idx = ny * grid.Width + nx;
                if (seen[idx]) continue;
                if (grid.Cells[idx].Tile == TileType.Bedrock) continue;
                seen[idx] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return grid.Cells.Where(c => c.Tile == TileType.Ore)
            .All(c => seen[c.Y * grid.Width + c.X]);
    });

    Console.WriteLine();
    Console.WriteLine($"结果: {pass} PASS / {fail} FAIL");
    return fail == 0 ? 0 : 1;
}
