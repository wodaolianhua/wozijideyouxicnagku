# Start the mining game (PowerShell 5+)
# Keeps temp/cache on E: and serves frontend + API on one port.
# Usage: right-click "Run with PowerShell", or: .\启动游戏.ps1
$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $PSScriptRoot 'MiningGame.Web'
$project = Join-Path $web 'MiningGame.Web.csproj'
$dll = Join-Path $web 'bin\Debug\net8.0\MiningGame.Web.dll'

$env:TMP = Join-Path $root '.tmp'
$env:TEMP = $env:TMP
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet_home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
New-Item -ItemType Directory -Force -Path $env:TMP | Out-Null

Write-Host "Mining game: http://127.0.0.1:5188" -ForegroundColor Cyan
Write-Host "(Ctrl+C to stop)" -ForegroundColor DarkGray

Push-Location $web
try {
    if (Test-Path $dll) {
        # Already built: run directly (also avoids rebuilding static assets).
        dotnet $dll --urls http://127.0.0.1:5188
    } else {
        dotnet run --project $project --no-launch-profile -- --urls http://127.0.0.1:5188
    }
} finally {
    Pop-Location
}
