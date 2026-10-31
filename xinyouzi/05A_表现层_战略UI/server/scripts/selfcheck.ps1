# ============================================================================
#  server/scripts/selfcheck.ps1 —— 05A 第 1 轮（3D）后端自测
#  流程（提示词 §6.2）：
#    new-game(seed 12345) → GET state 校验 6 城 3 势力且槽 0 IsPlayer=true
#    → 下 3 条指令（1 合法 Recruit、1 故意非法 targetId=9999、1 Dispatch）
#    → tick 断言 results 条数=3、非法那条 Message 可读、events 为增量
#    → 再连续 tick 2 次世界正常推进。
#  若 5178 未起服，本脚本自起（--no-build），结束时只关闭自己拉起的进程。
# ============================================================================
param([string]$BaseUrl = 'http://localhost:5178')

$ErrorActionPreference = 'Stop'
$script:fail = 0

function Assert([bool]$cond, [string]$msg) {
    if ($cond) { Write-Host "PASS  $msg" }
    else { Write-Host "FAIL  $msg"; $script:fail++ }
}

# ---- 0. 确保服务器在线 ----
$startedHere = $false
$proc = $null
$cacheTmp = 'E:\新建文件夹 (5)\dotnet-cache\tmp'
if (-not (Test-Path $cacheTmp)) { New-Item -ItemType Directory -Force $cacheTmp | Out-Null }
$serverLog = Join-Path $cacheTmp 'selfcheck-server.log'
$serverErr = Join-Path $cacheTmp 'selfcheck-server.err.log'

$online = $false
try {
    $null = Invoke-WebRequest -Uri "$BaseUrl/" -UseBasicParsing -TimeoutSec 2
    $online = $true
} catch {
    $online = $false
}
if (-not $online) {
    $proj = Join-Path $PSScriptRoot '..\StrategyGame.WebHost\StrategyGame.WebHost.csproj'
    $proc = Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
        -ArgumentList @('run', '--project', (Resolve-Path $proj).Path, '--no-build', '--no-launch-profile') `
        -RedirectStandardOutput $serverLog -RedirectStandardError $serverErr `
        -PassThru -WindowStyle Hidden
    $startedHere = $true
    $ok = $false
    for ($i = 0; $i -lt 80; $i++) {
        try {
            $null = Invoke-WebRequest -Uri "$BaseUrl/" -UseBasicParsing -TimeoutSec 2
            $ok = $true
            break
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    Assert $ok "服务器自起成功（http://localhost:5178）"
} else {
    Write-Host "INFO  服务器已在 $BaseUrl 在线，直接使用"
}

try {
    # ---- 1. new-game ----
    $ng = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/new-game" -ContentType 'application/json' -Body '{"seed":12345}'
    Assert ($null -ne $ng.state) "POST /api/new-game 返回 state"
    Assert (@($ng.state.cities).Count -eq 6) "开局 6 城（实际 $(@($ng.state.cities).Count)）"
    Assert (@($ng.state.factions).Count -eq 3) "开局 3 势力（实际 $(@($ng.state.factions).Count)）"
    Assert ($ng.state.factions[0].isPlayer -eq $true) "槽 0 IsPlayer=true"
    Assert ($ng.meta.pendingCount -eq 0) "开局待结算队列为空"

    # ---- 2. GET state ----
    $st = Invoke-RestMethod -Uri "$BaseUrl/api/state"
    Assert ($st.state.seed -eq 12345) "GET /api/state：seed=12345"
    Assert ($st.meta.monthsElapsed -eq 0) "GET /api/state：monthsElapsed=0"
    $myCity = @($st.state.cities | Where-Object { $_.ownerFactionId -eq 0 })[0]
    $myArmy = @($st.state.armies | Where-Object { $_.ownerFactionId -eq 0 })[0]
    $adj = @($myCity.adjacentCityIds)[0]
    Write-Host "INFO  玩家城=$($myCity.name)#$($myCity.id) 玩家军团=$($myArmy.id)（$($myArmy.soldiers) 人） 首个相邻城=$adj"

    # ---- 3. 三条指令 ----
    $r1 = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/order" -ContentType 'application/json' `
        -Body (@{ action = 'Recruit'; targetId = 9999; secondaryTargetId = -1; amount = 100 } | ConvertTo-Json -Compress)
    Assert ($r1.queued -eq $true -and $r1.pendingCount -eq 1) "指令1 非法 Recruit(targetId=9999)：queued=true pendingCount=1"

    $r2 = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/order" -ContentType 'application/json' `
        -Body (@{ action = 'Recruit'; targetId = $myCity.id; secondaryTargetId = -1; amount = 50 } | ConvertTo-Json -Compress)
    Assert ($r2.queued -eq $true -and $r2.pendingCount -eq 2) "指令2 合法 Recruit($($myCity.name), 50)：queued=true pendingCount=2"

    $r3 = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/order" -ContentType 'application/json' `
        -Body (@{ action = 'Dispatch'; targetId = $adj; secondaryTargetId = $myCity.id; amount = $myArmy.soldiers } | ConvertTo-Json -Compress)
    Assert ($r3.queued -eq $true -and $r3.pendingCount -eq 3) "指令3 Dispatch(→$adj, $($myArmy.soldiers) 人)：queued=true pendingCount=3"

    # ---- 4. tick：results 条数=3、非法那条 Message 可读、events 为增量 ----
    $tk = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/tick" -ContentType 'application/json' -Body '{}'
    Assert ($null -ne $tk.results) "POST /api/tick 返回 results"
    Assert (@($tk.results).Count -eq 3) "tick results 条数=3（实际 $(@($tk.results).Count)）"

    $illegal = @($tk.results | Where-Object { $_.orderId -eq 100000 })[0]
    Assert ($null -ne $illegal) "非法指令回执存在（orderId=100000）"
    if ($null -ne $illegal) {
        Assert ($illegal.code -eq 'RejectedInvalidTarget') "非法指令 code=RejectedInvalidTarget（实际 $($illegal.code)）"
        Assert (-not [string]::IsNullOrWhiteSpace($illegal.message)) "非法指令 Message 可读：「$($illegal.message)」"
    }

    $legal = @($tk.results | Where-Object { $_.orderId -eq 100001 })[0]
    Assert ($null -ne $legal -and $legal.code -eq 'Success') "合法 Recruit code=Success（实际 $($legal.code) / $($legal.message)）"

    $disp = @($tk.results | Where-Object { $_.orderId -eq 100002 })[0]
    Assert ($null -ne $disp) "Dispatch 回执存在（orderId=100002，code=$($disp.code)）"

    # events 增量 = state.eventLog 尾部
    $evCount = @($tk.events).Count
    $evStr = (@($tk.events) | ForEach-Object { "$($_.year)/$($_.month)/$($_.eventKey)" }) -join '|'
    $tail = @($tk.state.eventLog | Select-Object -Last $evCount)
    $tailStr = ($tail | ForEach-Object { "$($_.year)/$($_.month)/$($_.eventKey)" }) -join '|'
    Assert ($evStr -eq $tailStr) "events 为本月 EventLog 增量（$evCount 条，与 eventLog 尾部一致）"

    # ---- 5. 再连续 tick 2 次 ----
    foreach ($i in 2..3) {
        $preN = ((Invoke-RestMethod -Uri "$BaseUrl/api/state").meta.pendingCount)
        $t = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/tick" -ContentType 'application/json' -Body '{}'
        Assert ($null -ne $t.year -and $null -ne $t.month) "第 $i 次 tick 正常返回（$($t.year) 年 $($t.month) 月）"
        Assert (@($t.results).Count -eq $preN) "第 $i 次 tick results 条数=队列快照（$preN）"
        Assert (-not $t.meta.isFinished) "第 $i 次 tick 后世界未提前终局"
    }

    # ---- 6. 终态一致性 ----
    $fin = Invoke-RestMethod -Uri "$BaseUrl/api/state"
    Assert (@($fin.state.pendingOrders).Count -eq $fin.meta.pendingCount) "终态：队列条数与 meta.pendingCount 一致"
    Assert ($fin.meta.monthsElapsed -eq 3) "终态：monthsElapsed=3（实际 $($fin.meta.monthsElapsed)）"
}
finally {
    if ($startedHere -and $null -ne $proc) {
        try { taskkill /T /F /PID $proc.Id | Out-Null } catch {}
        Write-Host "INFO  已关闭自起的服务器进程（PID $($proc.Id)）"
    }
}

Write-Host ''
if ($script:fail -eq 0) {
    Write-Host 'SELFCHECK PASS —— 全部断言通过'
    exit 0
} else {
    Write-Host "SELFCHECK FAIL —— $($script:fail) 条断言未通过"
    exit 1
}
