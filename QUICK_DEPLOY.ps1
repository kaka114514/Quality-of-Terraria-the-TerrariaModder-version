$ErrorActionPreference = "Stop"
Write-Host "=== 快速编译和部署 ===" -ForegroundColor Cyan

# 编译客户端
Write-Host "`n[1/6] 编译客户端..." -ForegroundColor Yellow
dotnet build .\QualityOfTerraria.csproj -c Release
if ($LASTEXITCODE -ne 0) { Write-Host "❌ 客户端编译失败" -ForegroundColor Red; pause; exit 1 }
Write-Host "✓ 客户端编译成功" -ForegroundColor Green

# 编译服务器
Write-Host "`n[2/6] 编译服务器..." -ForegroundColor Yellow
& .\build-server.ps1
if ($LASTEXITCODE -ne 0) { Write-Host "❌ 服务器编译失败" -ForegroundColor Red; pause; exit 1 }
Write-Host "✓ 服务器编译成功" -ForegroundColor Green

# 检查文件
Write-Host "`n[3/6] 检查文件..." -ForegroundColor Yellow
$gameMod = "D:\App\Game\Steam\steamapps\common\Terraria\TerrariaModder\mods\quality-of-terraria"
$srcClient = ".\bin\Release\QualityOfTerraria.dll"
$srcServer = ".\bin\Release\QualityOfTerraria.Server.bin"
$dstClient = "$gameMod\QualityOfTerraria.dll"
$dstServer = "$gameMod\QualityOfTerraria.Server.bin"

if (!(Test-Path $srcClient)) { Write-Host "❌ 找不到客户端 DLL: $srcClient" -ForegroundColor Red; pause; exit 1 }
if (!(Test-Path $srcServer)) { Write-Host "❌ 找不到服务器 BIN: $srcServer" -ForegroundColor Red; pause; exit 1 }
Write-Host "✓ 源文件存在" -ForegroundColor Green
Write-Host "  客户端: $((Get-Item $srcClient).LastWriteTime.ToString('HH:mm:ss'))" -ForegroundColor Gray
Write-Host "  服务器: $((Get-Item $srcServer).LastWriteTime.ToString('HH:mm:ss'))" -ForegroundColor Gray

# 复制客户端 DLL
Write-Host "`n[4/6] 复制客户端 DLL..." -ForegroundColor Yellow
$retry = 0
while ($retry -lt 3) {
    try {
        Copy-Item $srcClient $dstClient -Force
        Write-Host "✓ 客户端复制成功" -ForegroundColor Green
        break
    } catch {
        $retry++
        if ($retry -eq 3) {
            Write-Host "❌ 复制失败（文件可能被游戏锁定）: $_" -ForegroundColor Red
            Write-Host "请完全退出游戏和服务器，然后重试。" -ForegroundColor Yellow
            pause; exit 1
        }
        Write-Host "⚠ 重试 $retry/3..." -ForegroundColor Yellow
        Start-Sleep -Seconds 1
    }
}

# 复制服务器模块
Write-Host "`n[5/6] 复制服务器模块..." -ForegroundColor Yellow
$retry = 0
while ($retry -lt 3) {
    try {
        Copy-Item $srcServer $dstServer -Force
        Write-Host "✓ 服务器复制成功" -ForegroundColor Green
        break
    } catch {
        $retry++
        if ($retry -eq 3) {
            Write-Host "❌ 复制失败（文件可能被游戏锁定）: $_" -ForegroundColor Red
            Write-Host "请完全退出游戏和服务器，然后重试。" -ForegroundColor Yellow
            pause; exit 1
        }
        Write-Host "⚠ 重试 $retry/3..." -ForegroundColor Yellow
        Start-Sleep -Seconds 1
    }
}

# 验证
Write-Host "`n[6/6] 验证部署..." -ForegroundColor Yellow
$dstClientTime = (Get-Item $dstClient).LastWriteTime
$dstServerTime = (Get-Item $dstServer).LastWriteTime
$srcClientTime = (Get-Item $srcClient).LastWriteTime
$srcServerTime = (Get-Item $srcServer).LastWriteTime

$clientOk = ($dstClientTime - $srcClientTime).TotalSeconds -ge -5
$serverOk = ($dstServerTime - $srcServerTime).TotalSeconds -ge -5

if (!$clientOk -or !$serverOk) {
    Write-Host "❌ 时间戳不匹配！" -ForegroundColor Red
    Write-Host "  客户端 - 源: $($srcClientTime.ToString('HH:mm:ss')), 目标: $($dstClientTime.ToString('HH:mm:ss')) $(if($clientOk){'✓'}else{'❌'})" -ForegroundColor Gray
    Write-Host "  服务器 - 源: $($srcServerTime.ToString('HH:mm:ss')), 目标: $($dstServerTime.ToString('HH:mm:ss')) $(if($serverOk){'✓'}else{'❌'})" -ForegroundColor Gray
    pause; exit 1
}
Write-Host "✓ 部署成功，时间戳匹配" -ForegroundColor Green
Write-Host "  客户端: $($dstClientTime.ToString('HH:mm:ss'))" -ForegroundColor Gray
Write-Host "  服务器: $($dstServerTime.ToString('HH:mm:ss'))" -ForegroundColor Gray

Write-Host "`n=== 完成！请重启游戏和服务器 ===" -ForegroundColor Cyan
pause
