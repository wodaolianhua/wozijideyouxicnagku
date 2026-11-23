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

// v1.7：后端默认读 02 的真实数据（7 城 3 势力），不再用内存里的 6 城样例。
SimulationHost.DataDirectory ??= SimulationHost.TryLocateDataDirectory();

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

static object BuildMeta(SimulationHost h)
{
    var faction = h.State.Factions.FirstOrDefault(f => f.IsPlayer) ?? h.State.Factions[0];
    var income = h.PreviewIncome(faction.Id);

    return new
    {
    monthsElapsed = h.MonthsElapsed,
    isFinished = h.IsFinished,
    winnerFactionId = h.WinnerFactionId,
    gameOverReason = h.GameOverReason,
    upstreamGameOverMonth = h.UpstreamGameOverMonth,
    upstreamGameOverReason = h.UpstreamGameOverReason,
    pendingCount = h.State.PendingOrders.Count,
    income = new
    {
        grain = income.Grain,
        silver = income.Silver,
        soldier = income.Soldier,
        departmentEfficiency = new
        {
            interior = income.Interior,
            military = income.Military,
            economy = income.Economy,
        },
    },
    };
}

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

app.MapGet("/api/preview-income/{factionId:int}", (int factionId) =>
{
    lock (sessionLock)
    {
        var h = host;
        if (h is null)
        {
            return Results.BadRequest(new { error = "尚未开局，请先 POST /api/new-game" });
        }

        if (h.State.GetFaction(factionId) is null)
        {
            return Results.BadRequest(new { error = $"势力 {factionId} 不存在" });
        }

        var i = h.PreviewIncome(factionId);
        return Results.Ok(new
        {
            factionId,
            grain = i.Grain,
            silver = i.Silver,
            soldier = i.Soldier,
            departmentEfficiency = new { interior = i.Interior, military = i.Military, economy = i.Economy },
        });
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

        var order = Order.CreatePlayerOrder(action, req.TargetId, req.Amount, req.SecondaryTargetId);
        order.OrderId = nextPlayerOrderId++;

        // 【第 8 轮·即时指令】玩家指令统一即时结算：点完就要看见数字变，
        // 不再进延迟队列等下个月。仍走完整的 校验→Delta→写回 链路
        // （SimulationHost.ExecuteImmediately → CommandProcessor.ProcessAndApply）。
        // 持续型指令同样即时「开工/开拔」，持续时间从当前游戏年月起算
        // （Build 的 UpgradeMonthsRemaining 倒计时、Dispatch 的行军到达年月都是
        // 由当前年月推算，与是否进队列无关）。
        // 延迟队列保留给 AI 通路：AI 指令由 SimulationHost.RunMonth 内
        // 规划器产出并入队，本端点不再有入队分支。
        var immediate = h.ExecuteImmediately(order);
        return Results.Ok(new
        {
            queued = false,
            immediate = true,
            code = immediate.Code,
            message = immediate.Message,
            deltas = immediate.Deltas,
            state = h.State,
            meta = BuildMeta(h),
        });
    }
});

// ============================================================================
//  改名（一人一次）
// ============================================================================
//  【为什么单独一个端点、还直接写状态】StateDelta 只携带 int（oldValue/newValue），
//  人名是字符串，无法用 Delta 表达。这里用「一次性」换取简洁：
//  校验全部前置（归属、是否已改过、长度、非空），通过后原子地写 Name 与
//  NameChangeUsed 两个字段，并在事件流里留一条 Personnel 事件备查。
//  这是全项目唯一绕过 Delta 的写入口，故集中在此处，不散落。
//
//  【设计依据】玩家明确要求「第一次能给人才改名字，之后就不能改了」：
//  一次性是刻意的——改名是「赐名」，等同于认下这个人，反复改就没有分量。
app.MapPost("/api/rename-officer", (RenameRequest? req) =>
{
    if (req is null)
    {
        return Results.BadRequest(new { error = "缺少请求体" });
    }

    lock (sessionLock)
    {
        var h = host;
        if (h is null)
        {
            return Results.BadRequest(new { error = "尚未开局，请先 POST /api/new-game" });
        }

        var state = h.State;
        var officer = state.GetOfficer(req.OfficerId);
        if (officer is null || !officer.IsAlive)
        {
            return Results.BadRequest(new { error = $"人才 {req.OfficerId} 不存在或已不在世" });
        }

        var player = state.Factions.FirstOrDefault(f => f.IsPlayer);
        if (player is null || officer.OwnerFactionId != player.Id)
        {
            return Results.BadRequest(new { error = "只能给本势力门下的人才赐名" });
        }

        if (officer.NameChangeUsed)
        {
            return Results.BadRequest(new { error = $"{officer.Name} 已经改过一次名了，改名只有一次机会" });
        }

        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return Results.BadRequest(new { error = "名字不能为空" });
        }

        if (name.Length > 8)
        {
            return Results.BadRequest(new { error = "名字最多 8 个字" });
        }

        var oldName = officer.Name;
        officer.Name = name;
        officer.NameChangeUsed = true;

        state.EventLog.Add(new EventRecord
        {
            Year = state.Year,
            Month = state.Month,
            Category = EventCategory.Personnel,
            EventKey = EventKeys.RenameOfficer,
            Message = $"{player.Name}为门下{oldName}赐名「{name}」，此后不再更改。",
            FactionId = player.Id,
        });

        return Results.Ok(new { ok = true, oldName, name, state, meta = BuildMeta(h) });
    }
});

app.MapPost("/api/tick", () =>
{    lock (sessionLock)
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

// ============================================================================
//  存档（v1.9）
//  序列化、校验、随机源状态的存取都在 06 · SaveSystem 与 SimulationHost 上，
//  这里只负责「文件怎么放」和「HTTP 怎么收」。
//
//  · 存档目录：内容根目录 /saves，首次访问自动创建
//  · 一个存档 = 一个 json 文件，id 就是文件名去掉 .json
//  · 存档名写进文件名，不另存 sidecar：删一个文件就是删一个档，
//    也不会出现「json 在、meta 丢了」这种半残状态
// ============================================================================
string savesDir = Path.Combine(app.Environment.ContentRootPath, "saves");
Directory.CreateDirectory(savesDir);

// 只允许字母、数字、中文、连字符、下划线；堵住 ../ 这类路径穿越
static bool IsSafeSaveId(string? id)
{
    if (string.IsNullOrWhiteSpace(id) || id.Length > 120)
    {
        return false;
    }

    foreach (char c in id)
    {
        if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
        {
            return false;
        }
    }

    return true;
}

// 把显示名压成能进文件名的形态：空白转下划线，去掉文件系统非法字符
static string ToFileSlug(string name)
{
    var sb = new System.Text.StringBuilder(name.Length);
    foreach (char c in name)
    {
        if (char.IsWhiteSpace(c))
        {
            sb.Append('_');
        }
        else if (c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c))
        {
            continue;
        }
        else
        {
            sb.Append(c);
        }
    }

    string slug = sb.ToString().Trim('_');
    return slug.Length == 0 ? "存档" : (slug.Length > 40 ? slug[..40] : slug);
}

// 从 id 还原显示名：去掉 "yyyyMMdd-HHmmss-" 前缀
static string NameFromSaveId(string id)
{
    return id.Length > 16 && id[8] == '-' && id[15] == '-' ? id[16..] : id;
}

app.MapPost("/api/save", (SaveRequest? req) =>
{
    lock (sessionLock)
    {
        if (host is null)
        {
            return Results.BadRequest(new { error = "尚未开局，请先 POST /api/new-game" });
        }

        string displayName = string.IsNullOrWhiteSpace(req?.Name)
            ? $"存档{host.State.Year}年{host.State.Month}月"
            : req!.Name!.Trim();

        if (displayName.Length > 40)
        {
            displayName = displayName[..40];
        }

        string slug = ToFileSlug(displayName);

        // 时间戳只用于文件名，不参与模拟，所以这里用系统时间不违反确定性铁律。
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string id = $"{stamp}-{slug}";
        string path = Path.Combine(savesDir, id + ".json");

        // 同秒内保存两次时加序号，不覆盖上一份
        for (int n = 2; File.Exists(path); n++)
        {
            id = $"{stamp}-{slug}-{n}";
            path = Path.Combine(savesDir, id + ".json");
        }

        SaveSystem.WriteToFile(host.BuildSave(), path);

        return Results.Ok(new
        {
            id,
            name = displayName,
            file = Path.GetFileName(path),
            size = new FileInfo(path).Length,
            year = host.State.Year,
            month = host.State.Month,
            cities = host.State.Cities.Count,
        });
    }
});

app.MapGet("/api/saves", () =>
{
    lock (sessionLock)
    {
        var entries = new List<(string Id, string Name, int Year, int Month, int Cities, long Size, DateTime SavedAt)>();

        foreach (string file in Directory.EnumerateFiles(savesDir, "*.json"))
        {
            try
            {
                var save = SaveSystem.ReadFromFile(file);
                string id = Path.GetFileNameWithoutExtension(file);
                entries.Add((
                    id,
                    NameFromSaveId(id),
                    save.State.Year,
                    save.State.Month,
                    save.State.Cities.Count,
                    new FileInfo(file).Length,
                    File.GetLastWriteTime(file)));
            }
            catch (Exception)
            {
                // 损坏的档不进列表，但也不能让它把整个接口带崩
            }
        }

        var ordered = entries
            .OrderByDescending(e => e.SavedAt)
            .ThenByDescending(e => e.Id, StringComparer.Ordinal)
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.Year,
                e.Month,
                e.Cities,
                e.Size,
                savedAt = e.SavedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            })
            .ToList();

        return Results.Ok(new { saves = ordered, total = ordered.Count });
    }
});

app.MapPost("/api/load", (LoadRequest? req) =>
{
    lock (sessionLock)
    {
        if (!IsSafeSaveId(req?.Id))
        {
            return Results.BadRequest(new { error = "存档 id 不合法" });
        }

        string path = Path.Combine(savesDir, req!.Id! + ".json");
        if (!File.Exists(path))
        {
            return Results.NotFound(new { error = $"存档不存在：{req.Id}" });
        }

        SaveFile save;
        try
        {
            save = SaveSystem.ReadFromFile(path);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"存档损坏，无法读取：{ex.Message}" });
        }

        var restored = SimulationHost.Restore(save);
        host = restored;

        // 玩家指令 id 计数器属于会话层、不在存档里。
        // 读档后从「现存待结算指令的最大 id + 1」接着发，避免和旧队列撞号。
        int maxPendingOrderId = restored.State.PendingOrders.Count == 0
            ? 0
            : restored.State.PendingOrders.Max(p => p.OrderId);
        nextPlayerOrderId = Math.Max(100000, maxPendingOrderId + 1);

        return Results.Ok(new
        {
            loaded = true,
            id = req.Id,
            name = NameFromSaveId(req.Id!),
            state = restored.State,
            meta = BuildMeta(restored),
        });
    }
});

app.MapDelete("/api/saves/{id}", (string id) =>
{
    lock (sessionLock)
    {
        if (!IsSafeSaveId(id))
        {
            return Results.BadRequest(new { error = "存档 id 不合法" });
        }

        string path = Path.Combine(savesDir, id + ".json");
        if (!File.Exists(path))
        {
            return Results.NotFound(new { error = $"存档不存在：{id}" });
        }

        File.Delete(path);
        return Results.Ok(new { deleted = true, id });
    }
});

app.Run();

// ---- 请求体 ----
internal record NewGameRequest(int Seed = 12345);

internal record OrderRequest(string Action = "", int TargetId = -1, int SecondaryTargetId = -1, int Amount = 0);

internal record RenameRequest(int OfficerId = -1, string? Name = null);

internal record SaveRequest(string? Name = null);

internal record LoadRequest(string? Id = null);
