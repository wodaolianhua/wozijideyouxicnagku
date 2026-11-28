using MiningGame.Contracts;
using MiningGame.Core;
using MiningGame.Web;

var builder = WebApplication.CreateBuilder(args);

// 全链 PascalCase：不做 camelCase 转换，契约字段名原样收发
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddSingleton<MiningSimulator>();

var app = builder.Build();

var root = FindRepoRoot(app.Environment.ContentRootPath)
           ?? throw new InvalidOperationException("找不到项目根目录（分发契约.sh）");
var dataDir = Path.Combine(root, "01_矿物与地图数据", "data");
var (grid, config) = DataLoader.Load(dataDir);
var initial = GameStateFactory.Create(grid, config);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/state", () => GameStateCloner.Clone(initial));

app.MapPost("/api/advance", (AdvanceRequest req, MiningSimulator sim) =>
{
    if (!IsValid(req.State, out var error)) return Results.BadRequest(new { Error = error });
    var result = sim.Advance(req.State!, req.Actions ?? new List<GameAction>(), req.Steps, req.Seed);
    return Results.Ok(result);
});

app.MapPost("/api/fast-forward", (FastForwardRequest req, MiningSimulator sim) =>
{
    if (!IsValid(req.State, out var error)) return Results.BadRequest(new { Error = error });
    var result = sim.FastForward(req.State!, req.Actions ?? new List<GameAction>(), req.MaxSteps, req.Seed);
    return Results.Ok(result);
});

app.MapGet("/api/health", () => Results.Ok(new { Ok = true, Grid = $"{grid.Width}x{grid.Height}" }));

app.Run();

static bool IsValid(GameState? state, out string error)
{
    if (state?.Grid?.Cells == null || state.Grid.Width <= 0 || state.Grid.Height <= 0)
    {
        error = "缺少有效的 State";
        return false;
    }
    if (state.Grid.Cells.Count != state.Grid.Width * state.Grid.Height)
    {
        error = "State.Grid.Cells 数量与宽高不一致";
        return false;
    }
    error = "";
    return true;
}

static string? FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "分发契约.sh"))) return dir.FullName;
    }
    return null;
}

public sealed class AdvanceRequest
{
    public GameState? State { get; set; }
    public List<GameAction>? Actions { get; set; }
    public int Steps { get; set; } = 1;
    public int Seed { get; set; }
}

public sealed class FastForwardRequest
{
    public GameState? State { get; set; }
    public List<GameAction>? Actions { get; set; }
    public int MaxSteps { get; set; } = 200;
    public int Seed { get; set; }
}
