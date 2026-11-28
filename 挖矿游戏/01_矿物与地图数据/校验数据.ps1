# Validate 01 data (PowerShell 5+)
# Usage: .\校验数据.ps1
# Regenerate data: dotnet run --project .\MiningGame.Data -- gen
$root = Split-Path -Parent $PSScriptRoot

$env:TMP = Join-Path $root '.tmp'
$env:TEMP = $env:TMP
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet_home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
New-Item -ItemType Directory -Force -Path $env:TMP | Out-Null

dotnet run --project (Join-Path $PSScriptRoot 'MiningGame.Data\MiningGame.Data.csproj') -- validate
