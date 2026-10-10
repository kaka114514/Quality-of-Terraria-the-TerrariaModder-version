$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$production = Join-Path $root 'StoragePersistence.cs'
if (!(Test-Path $production)) { throw '失败：缺少原子写入与备份恢复实现，当前覆盖保存不能保留有效副本。' }
$stub = 'namespace QualityOfTerraria { public static class Mod { public static int Errors; public static void LogError(string message, System.Exception error) { Errors++; } } }'
Add-Type -TypeDefinition ((Get-Content $production -Raw) + $stub + (Get-Content (Join-Path $PSScriptRoot 'StorageHarness.txt') -Raw)) -ErrorAction Stop
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('qot-storage-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
try {
    [QualityOfTerraria.StorageRegression]::Run($fixture)
} finally {
    $fixtureFull = [IO.Path]::GetFullPath($fixture)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$fixtureFull.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '临时样本路径越界，拒绝清理' }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
'存储回归检查通过'
