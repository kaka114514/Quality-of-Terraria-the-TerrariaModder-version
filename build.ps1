# QualityOfTerraria 一键构建与部署脚本
$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "开始构建 Quality of Terraria (更好的体验)..." -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$projectDir = $PSScriptRoot
$targetModDir = "D:\App\Game\Steam\steamapps\common\Terraria\TerrariaModder\mods\quality-of-terraria"
$targetDll = Join-Path $targetModDir "QualityOfTerraria.dll"
$targetServer = Join-Path $targetModDir "QualityOfTerraria.Server.bin"

# 1. 寻找带有 SDK 的 dotnet.exe 路径
$dotnetCmd = "dotnet"
if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
    $dotnetCmd = "$env:USERPROFILE\.dotnet\dotnet.exe"
}

# 2. 执行编译
& $dotnetCmd build "$projectDir\QualityOfTerraria.csproj" -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 编译失败，请检查代码错误！" -ForegroundColor Red
    exit 1
}

$outputDll = Join-Path $projectDir "bin\Release\QualityOfTerraria.dll"
$outputServer = Join-Path $projectDir "bin\Release\QualityOfTerraria.Server.bin"
& (Join-Path $projectDir 'build-server.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 专用服务器模块编译失败！" -ForegroundColor Red
    exit 1
}
if (!(Test-Path $outputDll)) {
    Write-Host "❌ 未找到编译输出文件: $outputDll" -ForegroundColor Red
    exit 1
}

if (!(Test-Path $outputServer)) {
    Write-Host "❌ 未找到专用服务器模块: $outputServer" -ForegroundColor Red
    exit 1
}

# 2. 部署到游戏 Mod 目录
if (!(Test-Path $targetModDir)) {
    New-Item -ItemType Directory -Path $targetModDir -Force | Out-Null
}

if (Test-Path -LiteralPath $targetDll) {
    Copy-Item -LiteralPath $targetDll -Destination ($targetDll + '.bak') -Force
}
if (Test-Path -LiteralPath $targetServer) {
    Copy-Item -LiteralPath $targetServer -Destination ($targetServer + '.bak') -Force
}
Copy-Item -LiteralPath $outputDll -Destination $targetDll -Force
Copy-Item -LiteralPath $outputServer -Destination $targetServer -Force
Copy-Item -LiteralPath (Join-Path $projectDir 'manifest.json') -Destination (Join-Path $targetModDir 'manifest.json') -Force
Write-Host ""
Write-Host "==========================================" -ForegroundColor Green
Write-Host "✅ 构建并部署成功！" -ForegroundColor Green
Write-Host "📁 输出目标: $targetDll" -ForegroundColor Green
Write-Host "🕒 更新时间: $((Get-Item $targetDll).LastWriteTime)" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
