$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Get-Content (Join-Path $root 'Networking/RemoteStorageOperations.cs') -Raw
if (!$source.Contains('private static bool IsOperation(string operation)')) { throw '缺少远程存储操作白名单' }
if ($source.Contains('operation.StartsWith(Prefix, StringComparison.Ordinal)')) { throw '远程存储处理器仍会吞掉快照等其他通道' }
if (!$source.Contains('operation == Prefix + "move"')) { throw '远程存储白名单缺少物品操作' }
if (!$source.Contains('RemoteStorageSnapshots.RefreshAfterMutation()')) { throw '物品确认后必须保留旧快照并排队刷新，不能直接清空面板' }
$snapshot = Get-Content (Join-Path $root 'UI/RemoteStorageSnapshots.cs') -Raw
if (!$snapshot.Contains('quality-of-terraria.snapshot.v1')) { throw '缺少远程快照通道' }
if (!$snapshot.Contains('Convert.ToBase64String(stream.ToArray())')) { throw '远程快照响应未统一编码' }
if (!$snapshot.Contains('internal static bool RefreshPending')) { throw '远程快照刷新期间缺少输入保护' }
Write-Host 'PASS: 远程快照与普通存储通道使用独立操作白名单和Base64载荷'
