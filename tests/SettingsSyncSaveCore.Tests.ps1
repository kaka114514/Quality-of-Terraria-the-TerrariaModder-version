param([string]$AssemblyPath = (Join-Path $PSScriptRoot '../bin/Release/QualityOfTerraria.dll'))
$ErrorActionPreference = 'Stop'
$game = 'D:\App\Game\Steam\steamapps\common\Terraria'
[Reflection.Assembly]::LoadFrom((Join-Path $game 'Terraria.exe')) | Out-Null
$harmonyAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $game 'TerrariaModder/core/deps/0Harmony.dll'))
$core = [Reflection.Assembly]::LoadFrom((Join-Path $game 'TerrariaModder/core/TerrariaModder.Core.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath))
$flags = [Reflection.BindingFlags]'Static,NonPublic,Public'
$sync = $assembly.GetType('QualityOfTerraria.Networking.SettingsSync')
$verify = $sync.GetMethod('SaveVerified', $flags)
if ($null -eq $verify) { throw 'FAIL 尚未实现真实Core保存结果核验' }
Add-Type -ReferencedAssemblies $harmonyAssembly.Location -TypeDefinition @'
public static class SettingsSaveCoreProbe {
 public static int Broadcasts;
 public static bool Admin(ref bool __result) { __result=true; return false; }
 public static bool Broadcast() { Broadcasts++; return false; }
}
'@
$config = [Activator]::CreateInstance($assembly.GetType('QualityOfTerraria.QualityOfTerrariaConfig'))
$mod = $assembly.GetType('QualityOfTerraria.Mod')
if ($mod.GetProperty('Config', $flags)) { $mod.GetProperty('Config', $flags).SetValue($null, $config, $null) }
else { $mod.GetField('Config', $flags).SetValue($null, $config) }
$filePath = $core.GetType('TerrariaModder.Core.Config.ModConfig').GetProperty('FilePath')
$directory = Join-Path (Join-Path $PSScriptRoot '../bin/settings-sync-verification') ('save-core-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$originalScope = [Environment]::GetEnvironmentVariable('TERRARIA_MODDER_DEDSERV')
$harmony = [HarmonyLib.Harmony]::new('quality-of-terraria.settings-save-test')
try {
    [Environment]::SetEnvironmentVariable('TERRARIA_MODDER_DEDSERV', $null)
    $blocked = Join-Path $directory 'blocked.client.json'
    New-Item -ItemType Directory -Path $blocked | Out-Null
    $filePath.SetValue($config, $blocked, $null)
    $argsVerify = [object[]]@($config, $null)
    if ($verify.Invoke($null, $argsVerify)) { throw 'FAIL Core吞下保存异常后被错误报告保存成功' }
    Write-Output 'PASS 真实Core吞下磁盘异常后SaveVerified仍检测保存失败'
    $snapshot = [Collections.Generic.Dictionary[string,object]]::new()
    $snapshot.Add('InfinitePotions', $false)
    $result = $sync.GetMethod('ApplyPersonal', $flags).Invoke($null, [object[]]@(,$snapshot))
    if (!$config.InfinitePotions -or !$result.Contains('未应用')) { throw 'FAIL 个人设置保存失败未回滚全部变更' }
    Write-Output 'PASS 真实Core个人设置保存失败回滚且不报告已保存'
    $filePath.SetValue($config, (Join-Path $directory 'success.client.json'), $null)
    $result = $sync.GetMethod('ApplyPersonal', $flags).Invoke($null, [object[]]@(,$snapshot))
    if ($config.InfinitePotions -or !$result.Contains('已保存')) { throw 'FAIL 真实Core成功保存没有核验配置文件' }
    Write-Output 'PASS 真实Core成功保存后所有当前Scope配置键重新读取一致'
    [Environment]::SetEnvironmentVariable('TERRARIA_MODDER_DEDSERV', '1')
    $blockedServer = Join-Path $directory 'blocked.server.json'
    New-Item -ItemType Directory -Path $blockedServer | Out-Null
    $filePath.SetValue($config, $blockedServer, $null)
    $permission = $core.GetType('TerrariaModder.Core.Permissions.PermissionService').GetMethod('IsAdmin')
    $broadcast = $core.GetType('TerrariaModder.Core.Net.NetSync').GetMethod('BroadcastConfigChange')
    $harmony.Patch($permission, [HarmonyLib.HarmonyMethod]::new([SettingsSaveCoreProbe].GetMethod('Admin')), $null, $null, $null) | Out-Null
    $harmony.Patch($broadcast, [HarmonyLib.HarmonyMethod]::new([SettingsSaveCoreProbe].GetMethod('Broadcast')), $null, $null, $null) | Out-Null
    $proposalType = $sync.GetNestedType('Proposal', [Reflection.BindingFlags]::NonPublic)
    $proposal = [Activator]::CreateInstance($proposalType, $true)
    $snapshot = [Collections.Generic.Dictionary[string,object]]::new()
    $snapshot.Add('SpawnRateMultiplier', [single]3)
    $proposalType.GetField('Snapshot', [Reflection.BindingFlags]'Instance,NonPublic').SetValue($proposal, $snapshot)
    $oldRate = $config.SpawnRateMultiplier
    $result = $sync.GetMethod('ApplyWorld', $flags).Invoke($null, [object[]]@($proposal, [int]0))
    if ($config.SpawnRateMultiplier -ne $oldRate -or !$result.Contains('未应用') -or [SettingsSaveCoreProbe]::Broadcasts -ne 0) { throw 'FAIL 世界设置真实保存失败未回滚或已经广播' }
    Write-Output 'PASS 真实Core服务器保存失败回滚世界项且未广播'
} finally {
    $harmony.UnpatchAll('quality-of-terraria.settings-save-test')
    [Environment]::SetEnvironmentVariable('TERRARIA_MODDER_DEDSERV', $originalScope)
}
