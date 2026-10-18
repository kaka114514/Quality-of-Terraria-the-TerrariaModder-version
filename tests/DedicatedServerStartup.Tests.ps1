$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Get-Content (Join-Path $root 'Mod.cs') -Raw

function Extract([string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "缺少生产入口：$signature" }
    $open = $source.IndexOf('{', $start); $end = $open + 1; $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start).Replace('private void', 'public void')
}

$methods = @(
    'public override void Initialize(ModContext context)',
    'public void OnContentReady(ModContext context)',
    'public void OnWorldLoad()',
    'public void OnWorldUnload()',
    'public override void Unload()',
    'private void SaveSession()',
    'private void EndSession()'
) | ForEach-Object { Extract $_ }

$harness = @'
using System;
using System.Reflection;
using QualityOfTerraria.Networking;
using Terraria;
using TerrariaModder.Core.Events;
namespace Terraria { public static class Main { public static bool dedServ, gameMenu; public static int netMode; } }
namespace TerrariaModder.Core.Logging { public interface ILogger { void Info(string s); void Warn(string s); } public class TestLogger : ILogger { public void Info(string s) {} public void Warn(string s) {} } }
namespace TerrariaModder.Core {
 using TerrariaModder.Core.Logging;
 public interface IMod {void Initialize(ModContext c);void Unload();}
 public interface IModLifecycle {void OnContentReady(ModContext c);void OnWorldLoad();void OnWorldUnload();void OnConfigChanged();}
 public class ModBase : IMod {public virtual void Initialize(ModContext c) {} public virtual void Unload() {}}
 public class ModContext { public ILogger Logger = new TestLogger(); public bool IsServer=>Terraria.Main.dedServ; public T GetConfig<T>() where T : new() { return new T(); } public bool IsModLoaded(string id) { return false; } public void RegisterKeybind(string id, string label, string description, string key, Action callback) {} }
 public static class ServerRuntime {public static FakeServer Instance;public static IMod Load(ModContext c){Instance=new FakeServer();Instance.Initialize(c);return Instance;}}
 public class FakeServer:IMod,IModLifecycle {public bool Initialized,WorldActive,ContentReady,Unloaded;public void Initialize(ModContext c){Initialized=true;}public void OnContentReady(ModContext c){ContentReady=true;}public void OnWorldLoad(){WorldActive=true;QualityOfTerraria.Networking.RemoteStorageOperations.Initialize();QualityOfTerraria.Networking.SettingsSync.Initialize();QualityOfTerraria.Networking.ToolNetwork.Initialize();QualityOfTerraria.Networking.FeatureNetwork.Initialize();QualityOfTerraria.Networking.VeinMiningNetwork.Initialize();QualityOfTerraria.UI.RemoteStorageSnapshots.Initialize();}public void OnWorldUnload(){WorldActive=false;}public void OnConfigChanged(){}public void Unload(){Unloaded=true;WorldActive=false;}}
}
namespace TerrariaModder.Core.Events { public static class FrameEvents { public static event Action OnPreUpdate { add{} remove{} } } public static class GameEvents { public static event Action OnWorldSave { add{} remove{} } public static event Action OnReturnToMenu { add{} remove{} } } }
namespace HarmonyLib { public class Harmony {public Harmony(string id){} public void UnpatchAll(string id){}} }
namespace QualityOfTerraria { public class QualityOfTerrariaConfig {} }
namespace QualityOfTerraria.Networking {
 public static class LocalWorldSettings { public static void Initialize() {} public static void Restore() {} }
 public static class RemoteStorageOperations { public static int Initializes, Saves; public static void Initialize(){Initializes++;} public static void ResetSession(){} public static void Unload(){} }
 public static class SettingsSync { public static int Initializes; public static void Initialize(){Initializes++;} public static void ResetSession(){} public static void Unload(){} }
 public static class ToolNetwork { public static int Initializes; public static void Initialize(){Initializes++;} public static void ResetSession(){} public static void Unload(){} }
 public static class FeatureNetwork { public static int Initializes; public static void Initialize(){Initializes++;} public static void ResetSession(){} public static void Unload(){} }
 public static class VeinMiningNetwork { public static int Initializes; public static void Initialize(){Initializes++;} public static void ResetSession(){} public static void Unload(){} }
 public static class StorageTransactionJournal { public static void EndServerSession(){} }
 public static class NetworkRuntime { public static void ResetSession(){} }
 public static class NetSyncPayload { public static void Reset(){} }
}
namespace QualityOfTerraria.Features { using TerrariaModder.Core; public static class VeinConfigMigration { public static void Apply(ModContext c){} } public static class VeinMiningSystem { public static bool Suspended; public static void Toggle(){} public static void ResetSession(){} } public static class ToolSpeedSystem { public static void Toggle(){} public static void ResetSession(){} } public static class Autofisher { public static void Toggle(){} } public static class StructureLocator {public static void ResetSession(){} public static void Update(){}} public static class TargetDummySystem { public static void ToggleOrSpawnAtCursor(){} public static void ResetSession(){} public static void Update(){} } }
namespace QualityOfTerraria.Patches { public static class StorageInventoryGuard { public static void ResetSession(){} } public static class WorldUIClickPatch { public static void ResetSession(){} } public static class VanillaUIInputDiagnostics { public static void ResetSession(){} } }
namespace QualityOfTerraria {
 using TerrariaModder.Core; using TerrariaModder.Core.Logging;
 public class Mod : ModBase {
  private const string HarmonyId="QualityOfTerraria.Server"; public static QualityOfTerrariaConfig Config {get;private set;} public static bool WorldActive {get;private set;} internal static bool VeinMinerLegacyLoaded {get;private set;}
  private static Mod _instance; private ModContext _context; private ILogger _logger; private bool _serverSession; private IMod _serverRuntime; private bool _contentReady,_patchesApplied;
  public bool IsServerRuntimeLoaded=>_serverRuntime!=null;
  public void CheckWorldSession(){} public void UpdateNetworkSession(){} private void ApplyReadyPatches(){if(!_contentReady||_patchesApplied)return;_patchesApplied=true;}
  __METHODS__
 }
}
namespace QualityOfTerraria { public static class WandsAndTools { public static void ExecuteQuickPlace(){} public static void ExecuteLiquidWand(){} public static void ExecuteNPCHome(){} public static void ExecuteQuickNurse(){} public static void ExecuteOpenBank(){} public static void ExecuteWeatherController(){} } }
namespace QualityOfTerraria.UI {
 public static class UIManager { public static int Initializes, Resets; public static void Initialize(){Initializes++;} public static void ResetSession(){Resets++;} public static void Unload(){} }
 public static class SuperVaultUI { public static int Loads, Saves; public static void Toggle(){} public static void EnsureLoadedForCurrentPlayer(){Loads++;} public static void SaveVaultData(){Saves++;} }
 public static class ExtremeStorageUI { public static int Loads, Saves; public static void Toggle(){} public static void EnsureLoadedForCurrentWorld(){Loads++;} public static void SaveMarkedChests(){Saves++;} }
 public static class PlayerStatsUI { public static void Toggle(){} } public static class StructureLocatorUI { public static void Toggle(){} public static class Dummy {public static void ResetSession(){}} } public static class BuffAndSpawnRateUI { public static void Toggle(){} public static class Dummy {public static void ResetSession(){}} } public static class RemoteStorageSnapshots { public static int Initializes,Resets; public static void Initialize(){Initializes++;} public static void ResetSession(){Resets++;} }
}
public static class DedicatedServerStartupTests {
 static void Check(bool b,string s){if(!b)throw new Exception("FAIL "+s);}
 public static void Run(){
  Terraria.Main.dedServ=true;Terraria.Main.netMode=2;var server=new QualityOfTerraria.Mod();server.Initialize(new TerrariaModder.Core.ModContext());
  Check(QualityOfTerraria.UI.UIManager.Initializes==0,"专服初始化阶段不碰客户端UI");
  Check(server.IsServerRuntimeLoaded&&TerrariaModder.Core.ServerRuntime.Instance.Initialized,"专服启动匹配的服务器运行模块");
  server.OnContentReady(new TerrariaModder.Core.ModContext());Check(TerrariaModder.Core.ServerRuntime.Instance.ContentReady,"专服内容就绪回调进入服务器模块");
  server.OnWorldLoad();Check(TerrariaModder.Core.ServerRuntime.Instance.WorldActive,"专服世界会话启动");
  Check(QualityOfTerraria.Networking.RemoteStorageOperations.Initializes==1&&QualityOfTerraria.Networking.SettingsSync.Initializes==1&&QualityOfTerraria.Networking.ToolNetwork.Initializes==1&&QualityOfTerraria.Networking.FeatureNetwork.Initializes==1&&QualityOfTerraria.Networking.VeinMiningNetwork.Initializes==1&&QualityOfTerraria.UI.RemoteStorageSnapshots.Initializes==1,"专服仍初始化网络处理器");
  Check(QualityOfTerraria.UI.UIManager.Initializes==0&&QualityOfTerraria.UI.UIManager.Resets==0&&QualityOfTerraria.UI.SuperVaultUI.Loads==0&&QualityOfTerraria.UI.ExtremeStorageUI.Loads==0,"专服不加载玩家仓库或界面");
  typeof(QualityOfTerraria.Mod).GetProperty("WorldActive",BindingFlags.Static|BindingFlags.Public).GetSetMethod(true).Invoke(null,new object[]{true});
  server.SaveSession();Check(QualityOfTerraria.UI.SuperVaultUI.Saves==0&&QualityOfTerraria.UI.ExtremeStorageUI.Saves==0,"专服保存不写客户端界面数据");
  server.EndSession();Check(QualityOfTerraria.UI.UIManager.Resets==0,"专服结束会话不调用客户端UI");
  server.OnWorldUnload();Check(!TerrariaModder.Core.ServerRuntime.Instance.WorldActive,"专服世界卸载结束服务器会话");
  server.Unload();Check(TerrariaModder.Core.ServerRuntime.Instance.Unloaded,"卸载委托至服务器模块");
  Console.WriteLine("PASS 专服模块选择、网络服务和UI隔离");
 }
}
'@

Add-Type -TypeDefinition $harness.Replace('__METHODS__', ($methods -join "`n"))
[DedicatedServerStartupTests]::Run()
