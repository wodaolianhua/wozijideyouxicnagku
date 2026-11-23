using StrategyGame.Presentation;

var builder = WebApplication.CreateBuilder(args);

const int Port = 5178;
builder.WebHost.UseUrls($"http://localhost:{Port}");

// 全链 PascalCase：.NET 默认命名策略是 camelCase，这里显式关掉，保证与契约一致。
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.DictionaryKeyPolicy = null;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var mapPath = ResolveMapPath(
    Environment.GetEnvironmentVariable("MAP_DATA_PATH") ?? builder.Configuration["MapDataPath"]);

var store = new WorldStore(mapPath);
builder.Services.AddSingleton(store);

var app = builder.Build();

app.Logger.LogInformation("地图数据：{MapPath}", mapPath);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => new { Status = "ok", Port });
app.MapGet("/api/world", (WorldStore s) => s.Snapshot());
app.MapPost("/api/reset", (WorldStore s) => s.Reset());
app.MapPost("/api/advance", (AdvanceRequest req, WorldStore s) => s.Advance(req.Orders, req.Steps));
app.MapPost("/api/fastforward", (FastForwardRequest req, WorldStore s) => s.FastForward(req.Orders, req.MaxSteps));
app.MapPost("/api/preview", (PreviewRequest req, WorldStore s) => s.Preview(req.ArmyId, req.Waypoints));

app.Run();

static string ResolveMapPath(string? configured)
{
    var candidates = new List<string>();

    if (!string.IsNullOrWhiteSpace(configured))
    {
        if (Path.IsPathRooted(configured))
        {
            candidates.Add(configured);
        }
        else
        {
            candidates.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configured)));
            candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured)));
        }
    }

    candidates.Add(Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(), "..", "..", "01_地图数据", "初始地图.json")));

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new FileNotFoundException(
        "找不到地图数据文件 初始地图.json。已尝试以下路径：" +
        Environment.NewLine + string.Join(Environment.NewLine, candidates) +
        Environment.NewLine + "可用环境变量 MAP_DATA_PATH 指定绝对路径，或在 appsettings.json 修改 MapDataPath。");
}
