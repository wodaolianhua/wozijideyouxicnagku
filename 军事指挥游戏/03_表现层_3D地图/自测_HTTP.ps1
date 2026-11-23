# 表现层第 1 轮 · HTTP 全链路自测
# 用法：
#   powershell -ExecutionPolicy Bypass -File .\自测_HTTP.ps1
#   powershell -ExecutionPolicy Bypass -File .\自测_HTTP.ps1 -NoStart   # 复用已启动的服务

param(
    [switch]$NoStart,
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'StrategyGame.Presentation\StrategyGame.Presentation.csproj'
$base = 'http://localhost:5178'

$env:NUGET_PACKAGES = Join-Path $root '.nuget-packages'
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_NOLOGO = '1'

$script:passed = 0
$script:failed = 0
$server = $null

function Assert-True {
    param([string]$Name, $Condition, [string]$Detail = '')
    if ($Condition) {
        $script:passed++
        Write-Host "[PASS] $Name" -ForegroundColor Green
    }
    else {
        $script:failed++
        Write-Host "[FAIL] $Name  $Detail" -ForegroundColor Red
    }
}

function Test-Health {
    try {
        $res = Invoke-RestMethod "$base/api/health" -TimeoutSec 2
        return ($res.Status -eq 'ok')
    }
    catch {
        return $false
    }
}

Write-Host '=== 表现层第 1 轮 HTTP 自测 ===' -ForegroundColor Cyan

try {
    if (-not (Test-Health)) {
        if ($NoStart) {
            throw "服务未在 $base 启动；去掉 -NoStart 让脚本自动构建并起服。"
        }

        Write-Host '构建宿主…'
        & dotnet build $project -c Debug | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build 失败' }

        $outLog = Join-Path $root '自测_服务器输出.log'
        $errLog = Join-Path $root '自测_服务器错误.log'
        Write-Host '启动宿主（后台）…'
        $server = Start-Process -FilePath 'dotnet' `
            -ArgumentList @('run', '--project', $project, '--no-build') `
            -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $outLog -RedirectStandardError $errLog

        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while (-not (Test-Health)) {
            if ((Get-Date) -gt $deadline) {
                $tail = ''
                if (Test-Path $outLog) { $tail = (Get-Content $outLog -Tail 20) -join "`n" }
                throw "等待 $base 启动超时。服务输出：`n$tail"
            }
            Start-Sleep -Milliseconds 800
        }
        Write-Host "服务已就绪：$base" -ForegroundColor Cyan
    }
    else {
        Write-Host "复用已在运行的服务：$base" -ForegroundColor Cyan
    }

    # 1. 静态页与脚本
    $index = Invoke-WebRequest "$base/" -UseBasicParsing
    Assert-True '浏览器入口返回 200' ($index.StatusCode -eq 200) "status=$($index.StatusCode)"
    Assert-True 'landing page 引入了 app.js' ($index.Content -like '*app.js*')

    $js = Invoke-WebRequest "$base/app.js" -UseBasicParsing
    Assert-True '静态 app.js 可访问' (($js.StatusCode -eq 200) -and ($js.Content.Length -gt 1000)) "len=$($js.Content.Length)"

    # 2. 初始世界
    $reset = Invoke-RestMethod "$base/api/reset" -Method Post -ContentType 'application/json' -Body '{}'
    Assert-True '重开回到时间步 0' ($reset.Time -eq 0) "t=$($reset.Time)"

    $world = Invoke-RestMethod "$base/api/world"
    Assert-True '世界数据：18 节点' ($world.Map.Nodes.Count -eq 18) "count=$($world.Map.Nodes.Count)"
    Assert-True '世界数据：29 条边' ($world.Map.Edges.Count -eq 29) "count=$($world.Map.Edges.Count)"
    Assert-True '世界数据：9 支部队' ($world.Armies.Count -eq 9) "count=$($world.Armies.Count)"
    $playerTotal = (($world.Armies | Where-Object { $_.Faction -eq 0 } | Measure-Object -Property Strength -Sum).Sum)
    Assert-True '我方总兵力 2600' ($playerTotal -eq 2600) "sum=$playerTotal"
    Assert-True 'JSON 字段为 PascalCase（Map / Armies）' (($null -ne $world.Map) -and ($null -ne $world.Map.Nodes))

    # 3. 路径预览（不下令、不改状态）
    $previewBody = @{ ArmyId = 'A_P01'; Waypoints = @('N02', 'N06') } | ConvertTo-Json -Depth 5
    $preview = Invoke-RestMethod "$base/api/preview" -Method Post -ContentType 'application/json' -Body $previewBody
    Assert-True '折线预览返回可达路线' (($preview.Code -eq 0) -and ($preview.Path.Count -gt 1)) "code=$($preview.Code) path=$($preview.Path -join '>')"
    Assert-True '预览路线从当前节点 N01 起' ($preview.Path[0] -eq 'N01') "first=$($preview.Path[0])"

    $worldAfterPreview = Invoke-RestMethod "$base/api/world"
    Assert-True '预览不会改动世界状态' ($worldAfterPreview.Time -eq 0) "t=$($worldAfterPreview.Time)"

    # 4. 下令并推进 1 步
    $orderBody = @{
        Orders = @(@{ ArmyId = 'A_P01'; Kind = 0; Waypoints = @('N06') })
        Steps  = 1
    } | ConvertTo-Json -Depth 6
    $adv = Invoke-RestMethod "$base/api/advance" -Method Post -ContentType 'application/json' -Body $orderBody
    $advTrail = $adv.Trails | Where-Object { $_.ArmyId -eq 'A_P01' }
    $advArmy = $adv.State.Armies | Where-Object { $_.Id -eq 'A_P01' }

    Assert-True '下令推进返回 Code=Ok' ($adv.Code -eq 0) "code=$($adv.Code)"
    Assert-True '推进后时间步为 1' ($adv.State.Time -eq 1) "t=$($adv.State.Time)"
    Assert-True '返回 9 条部队轨迹' ($adv.Trails.Count -eq 9) "count=$($adv.Trails.Count)"
    Assert-True '我方轨迹含起点与推进点' (($null -ne $advTrail) -and ($advTrail.Points.Count -ge 2)) "points=$($advTrail.Points.Count)"
    Assert-True '部队计划路径已写入 N06' (($advArmy.PlannedPath -contains 'N06') -and ($advArmy.EdgeProgress -gt 0)) "path=$($advArmy.PlannedPath -join '>') progress=$($advArmy.EdgeProgress)"

    # 5. 无命令继续快进（续走）
    $ffBody = @{ Orders = @(); MaxSteps = 200 } | ConvertTo-Json -Depth 4
    $ff = Invoke-RestMethod "$base/api/fastforward" -Method Post -ContentType 'application/json' -Body $ffBody
    $ffArmy = $ff.State.Armies | Where-Object { $_.Id -eq 'A_P01' }
    Assert-True '快进返回 Code=Ok' ($ff.Code -eq 0) "code=$($ff.Code)"
    Assert-True '快进推进了时间' ($ff.State.Time -gt 1) "t=$($ff.State.Time)"
    Assert-True '快进返回 9 条部队轨迹' ($ff.Trails.Count -eq 9) "count=$($ff.Trails.Count)"
    Assert-True '续走抵达目标节点 N06' ($ffArmy.NodeId -eq 'N06') "node=$($ffArmy.NodeId)"

    # 6. 快进遇敌：损失必须来自 LossByArmy 且已扣进兵力
    Invoke-RestMethod "$base/api/reset" -Method Post -ContentType 'application/json' -Body '{}' | Out-Null
    $orderBody2 = @{
        Orders = @(@{ ArmyId = 'A_P01'; Kind = 0; Waypoints = @('N05') })
        Steps  = 1
    } | ConvertTo-Json -Depth 6
    Invoke-RestMethod "$base/api/advance" -Method Post -ContentType 'application/json' -Body $orderBody2 | Out-Null

    $ff2 = Invoke-RestMethod "$base/api/fastforward" -Method Post -ContentType 'application/json' -Body $ffBody
    $enc = $ff2.Encounters | Select-Object -First 1
    $p2 = $ff2.State.Armies | Where-Object { $_.Id -eq 'A_P01' }

    Assert-True '快进遇敌并停在事发步' ($ff2.Encounters.Count -ge 1) "encounters=$($ff2.Encounters.Count)"
    Assert-True '遭遇事件带时间与节点' (($null -ne $enc) -and ($enc.Time -eq $ff2.State.Time) -and (-not [string]::IsNullOrEmpty($enc.NodeId))) "t=$($enc.Time) node=$($enc.NodeId)"
    Assert-True '遭遇损失含双方部队（LossByArmy）' (($null -ne $enc.Loss.LossByArmy.A_P01) -and ($null -ne $enc.Loss.LossByArmy.A_E01)) "loss=$($enc.Loss.LossByArmy | ConvertTo-Json -Compress)"

    $lossSum = 0
    foreach ($event in $ff2.Encounters) {
        if ($null -ne $event.Loss.LossByArmy.A_P01) {
            $lossSum += [int]$event.Loss.LossByArmy.A_P01
        }
    }
    Assert-True '遭遇后兵力已按损失扣减' ($p2.Strength -eq (800 - $lossSum)) "strength=$($p2.Strength) loss=$lossSum"

    # 7. 重开恢复初始状态
    $reset2 = Invoke-RestMethod "$base/api/reset" -Method Post -ContentType 'application/json' -Body '{}'
    $p3 = $reset2.Armies | Where-Object { $_.Id -eq 'A_P01' }
    Assert-True '重开恢复时间步 0' ($reset2.Time -eq 0) "t=$($reset2.Time)"
    Assert-True '重开恢复初始兵力 800' ($p3.Strength -eq 800) "strength=$($p3.Strength)"
}
finally {
    if ($null -ne $server) {
        Write-Host '停止本次启动的服务…' -ForegroundColor DarkGray
        & taskkill /PID $server.Id /T /F | Out-Null
    }
}

Write-Host ''
if ($script:failed -eq 0) {
    Write-Host "全部通过：$($script:passed) 项" -ForegroundColor Green
    exit 0
}
else {
    Write-Host "通过 $($script:passed) 项，失败 $($script:failed) 项" -ForegroundColor Red
    exit 1
}
