// ============================================================================
//  05A 表现层 · 战略 UI · WebHost
//  ASP.NET Core minimal API：单局内存会话（一个 SimulationHost 实例 + 一把锁），
//  静态托管 client 构建产物（wwwroot/）。单端口 http://localhost:5178，
//  无 HTTPS、无 CORS、无数据库、无登录。
//
//  端点（提示词 §3，不多不少）：
//    POST /api/new-game  { "seed": 12345 }（缺省 12345）
//    GET  /api/state
//    POST /api/order     { "action", "targetId", "secondaryTargetId", "amount" }
//    POST /api/tick
//
//  【关于 SimulationHost.LastTurn】RunMonth() 原为 void，逐条 ResolveResult 无只读通道。
//  经用户裁决（2026-10-31）豁免「禁改 06」后，06 侧新增只读属性 LastTurn 记录最近一次
//  RunMonth 的 TurnResult，本后端据此把逐条 code/message/deltas 按队列顺序带回表现层。
//  除该属性外未改动 03/04/06/契约任何源码。
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using StrategyGame.Contracts;
using StrategyGame.Core;
using StrategyGame.Sim;

var builder = WebApplication.CreateBuilder(args);

// 线格式规范（Contracts.WireFormat）：camelCase + 枚举字符串 + 忽略未声明字段
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
});

var app = builder.Build();

// 单端口、无 HTTPS：清空默认绑定后固定 5178
app.Urls.Clear();
app.Urls.Add("http://localhost:5178");

// 静态文件 + 默认页（wwwroot/）
app.UseDefaultFiles();
app.UseStaticFiles();

// ---- 单局内存会话：一个 SimulationHost 实例 + 一把锁，所有端点串行访问 ----
var sessionLock = new object();
SimulationHost? host = null;
int nextPlayerOrderId = 100000;

static object BuildMeta(SimulationHost h) => new
{
    monthsElapsed = h.MonthsElapsed,
    isFinished = h.IsFinished,
    winnerFactionId = h.WinnerFactionId,
    gameOverReason = h.GameOverReason,
    upstreamGameOverMonth = h.UpstreamGameOverMonth,
    upstreamGameOverReason = h.UpstreamGameOverReason,
    pendingCount = h.State.PendingOrders.Count,
};

app.MapPost("/api/new-game", (NewGameRequest? req) =>
{
    int seed = req?.Seed ?? 12345;
    if (seed <= 0)
    {
        return Results.BadRequest(new { error = "seed 必须为正整数" });
    }

    lock (sessionLock)
    {
        var h = new SimulationHost(seed, SimulationHost.DefaultMaxMonths);
        h.State.Factions[0].IsPlayer = true; // 玩家身份 = 槽 0（汉）
        host = h;
        nextPlayerOrderId = 100000;
        return Results.Ok(new { state = h.State, meta = BuildMeta(h) });
    }
});

app.MapGet("/api/state", () =>
{
    lock (sessionLock)
    {
        var h = host;
        if (h is null)
        {
            return Results.BadRequest(new { error = "尚未开局，请先 POST /api/new-game" });
        }

        return Results.Ok(new { state = h.State, meta = BuildMeta(h) });
    }
});

app.MapPost("/api/order", (OrderRequest? req) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.Action))
    {
        return Results.BadRequest(new { error = "缺少 action" });
    }

    if (!Enum.TryParse(req.Action, ignoreCase: true, out ActionType action) || !Enum.IsDefined(action))
    {
        return Results.BadRequest(new { error = $"无法解析的 action：{req.Action}" });
    }

    lock (sessionLock)
    {
        var h = host;
        if (h is null)
        {
            return Results.BadRequest(new { error = "尚未开局，请先 POST /api/new-game" });
        }

        // 仅入队不结算；OrderId 从 100000 起自增；后端不做合法性预校验（码 8 原样放行）
        var order = Order.CreatePlayerOrder(action, req.TargetId, req.Amount, req.SecondaryTargetId);
        order.OrderId = nextPlayerOrderId++;
        var (executeYear, executeMonth) = SimulationHost.NextMonth(h.State.Year, h.State.Month);
        order.ExecuteYear = executeYear;
        order.ExecuteMonth = executeMonth;
        h.State.PendingOrders.Add(order.ToPendingOrder());
        return Results.Ok(new { queued = true, pendingCount = h.State.PendingOrders.Count });
    }
});

app.MapPost("/api/tick", () =>
{
    lock (sessionLock)
    {
        var h = host;
        if (h is null)
        {
            return Results.BadRequest(new { error = "尚未开局，请先 POST /api/new-game" });
        }

        if (h.IsFinished)
        {
            return Results.BadRequest(new { error = "本局已终局" });
        }

        var state = h.State;
        var (nextYear, nextMonth) = SimulationHost.NextMonth(state.Year, state.Month);

        // 快照本月将结算队列（队列内指令一律下月到期，按入队顺序逐条结算）
        var dueSnapshot = state.PendingOrders
            .Where(p => p.ExecuteYear == nextYear && p.ExecuteMonth == nextMonth)
            .Select(p => new
            {
                p.OrderId,
                p.ActorType,
                p.ActorId,
                p.Action,
                p.TargetId,
                p.SecondaryTargetId,
                p.Amount,
                p.ExecuteYear,
                p.ExecuteMonth,
            })
            .ToList();

        int logStart = state.EventLog.Count;
        h.RunMonth();
        var turn = h.LastTurn;

        // 断言：turn.Results 与快照顺序一一对应
        if (turn is null || turn.Results.Count != dueSnapshot.Count)
        {
            return Results.Problem(
                $"结算结果条数与队列快照不一致：results={turn?.Results.Count ?? -1}，快照={dueSnapshot.Count}（接缝异常，请上报）");
        }

        var results = new List<object>(dueSnapshot.Count);
        for (int i = 0; i < dueSnapshot.Count; i++)
        {
            var o = dueSnapshot[i];
            var r = turn.Results[i];
            results.Add(new
            {
                o.OrderId,
                o.ActorType,
                o.ActorId,
                o.Action,
                o.TargetId,
                o.SecondaryTargetId,
                o.Amount,
                o.ExecuteYear,
                o.ExecuteMonth,
                code = r.Code,
                message = r.Message,
                deltas = r.Deltas,
                events = r.Events,
            });
        }

        // 本月事件 = State.EventLog 增量（turn.Events 是累计量，禁止直接用）
        var events = state.EventLog.Skip(logStart).ToList();

        return Results.Ok(new
        {
            year = state.Year,
            month = state.Month,
            results,
            events,
            meta = BuildMeta(h),
            state,
        });
    }
});

app.Run();

// ---- 请求体 ----
internal record NewGameRequest(int Seed = 12345);

internal record OrderRequest(string Action = "", int TargetId = -1, int SecondaryTargetId = -1, int Amount = 0);
