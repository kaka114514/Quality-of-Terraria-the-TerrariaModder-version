$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Mod.cs') -Raw -Encoding UTF8
function Method([string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "找不到方法：$signature" }
    $open = $source.IndexOf('{', $start); $end = $open + 1; $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start).Replace('private void', 'public void')
}
$methods = @('public void OnWorldLoad()', 'public void OnWorldUnload()', 'private void CheckWorldSession()', 'private void SaveSession()', 'private void EndSession()') | ForEach-Object { Method $_ }
$harness = @'
using System;
using Terraria;
using QualityOfTerraria.Networking;
namespace Terraria { public static class Main { public static bool gameMenu, dedServ; public static int netMode; } }
namespace QualityOfTerraria.Networking {
    public static class RemoteStorageOperations { public static void Initialize() {} public static void ResetSession() {} }
    public static class SettingsSync { public static void Initialize() {} public static void ResetSession() {} }
    public static class ToolNetwork { public static void Initialize() {} public static void ResetSession() {} }
    public static class FeatureNetwork { public static void Initialize() {} public static void ResetSession() {} }
    public static class VeinMiningNetwork { public static void Initialize() {} public static void ResetSession() {} }
    public static class NetworkRuntime { public static void ResetSession() {} }
    public static class NetSyncPayload { public static void Reset() {} }
    public static class LocalWorldSettings { public static void Restore() {} }
    public static class StorageTransactionJournal { public static void EndServerSession() {} }
}
namespace QualityOfTerraria.Features { public static class VeinMiningSystem { public static bool Suspended; public static void ResetSession() {} } public static class ToolSpeedSystem { public static void ResetSession() {} } public static class StructureLocator {public static void ResetSession(){}} public static class TargetDummySystem {public static void ResetSession(){}} }
namespace QualityOfTerraria.Patches { public static class StorageInventoryGuard { public static void ResetSession() {} } public static class WorldUIClickPatch { public static void ResetSession() {} } public static class VanillaUIInputDiagnostics { public static void ResetSession() {} } }
namespace QualityOfTerraria {
    public class Logger { public void Info(string text) {} }
    public class Mod {
        public static bool WorldActive, VeinMinerLegacyLoaded;
        private Logger _logger = new Logger();
        private bool _serverSession;
        __METHODS__
    }
    public static class EventLog { public static string Value = ""; }
}
namespace QualityOfTerraria.UI {
    public static class RemoteStorageSnapshots { public static void Initialize() {} public static void ResetSession() {} }
    public static class UIManager { public static void ResetSession() { EventLog.Value += "reset,"; } }
    public static class SuperVaultUI {
        public static void EnsureLoadedForCurrentPlayer() { EventLog.Value += "vault-load,"; }
        public static void SaveVaultData() { EventLog.Value += "vault-save,"; }
    }
    public static class ExtremeStorageUI {
        public static void EnsureLoadedForCurrentWorld() { EventLog.Value += "chest-load,"; }
        public static void SaveMarkedChests() { EventLog.Value += "chest-save,"; }
    }
}
'@
Add-Type -TypeDefinition ("#define QOT_SERVER`n" + $harness.Replace('__METHODS__', ($methods -join "`n")))
function Check($condition, $label) {
    if (!$condition) { throw "$label 实际：$([QualityOfTerraria.EventLog]::Value)" }
    Write-Host "PASS $label"
}
$mod = New-Object QualityOfTerraria.Mod
$mod.OnWorldLoad()
Check ([QualityOfTerraria.Mod]::WorldActive -and [QualityOfTerraria.EventLog]::Value -eq 'reset,vault-load,chest-load,') '进入世界先清旧会话再按稳定身份加载'
[QualityOfTerraria.EventLog]::Value = ''
$mod.SaveSession()
Check ([QualityOfTerraria.EventLog]::Value -eq 'vault-save,chest-save,') '世界保存保存两类数据'
[QualityOfTerraria.EventLog]::Value = ''
$mod.OnWorldUnload()
Check (![QualityOfTerraria.Mod]::WorldActive -and [QualityOfTerraria.EventLog]::Value -eq 'vault-save,chest-save,reset,') '退出先保存后清会话'
[QualityOfTerraria.EventLog]::Value = ''
$mod.EndSession()
Check ([QualityOfTerraria.EventLog]::Value -eq 'reset,') '重复退出不保存已清空数据'
$mod.OnWorldLoad()
[QualityOfTerraria.EventLog]::Value = ''
[Terraria.Main]::gameMenu = $true
$mod.CheckWorldSession()
Check (![QualityOfTerraria.Mod]::WorldActive -and [QualityOfTerraria.EventLog]::Value -eq 'vault-save,chest-save,reset,') '返回菜单帧事件兜底且仍先保存'
[Terraria.Main]::dedServ = $true
$mod.OnWorldLoad()
[QualityOfTerraria.EventLog]::Value = ''
$mod.CheckWorldSession()
Check ([QualityOfTerraria.Mod]::WorldActive -and [QualityOfTerraria.EventLog]::Value -eq '') '专服菜单标记不清理网络服务'
