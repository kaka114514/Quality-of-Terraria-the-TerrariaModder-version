$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Get-Content (Join-Path $PSScriptRoot 'RemoteStorage.Protocol.Tests.ps1') -Raw
$preamble = $fixture.Substring(0, $fixture.IndexOf("`$test = @'"))
Invoke-Expression ($preamble.Replace('$root = Split-Path $PSScriptRoot', ''))
$ui = Get-Content (Join-Path $root 'UI/ExtremeStorageUI.cs') -Raw
$start = $ui.IndexOf('internal static void AcceptServerMarks(')
$accept = $ui.Substring($start, $ui.IndexOf('internal static void CloseAfterRemoteOpen(', $start)-$start)
$start = $ui.IndexOf('public static void SaveMarkedChests(')
$save = $ui.Substring($start, $ui.IndexOf('public static void LoadMarkedChests(', $start)-$start)
$start = $ui.IndexOf('public static void LoadMarkedChests(')
$load = $ui.Substring($start, $ui.IndexOf('public static bool IsChestMarked(', $start)-$start)
$adapter = 'private static string _loadedWorldName,_loadedPath; private static bool _canSave;' + $accept + $save + $load
$stub = $stub.Replace('public static void AcceptServerMarks(string world,string owner,IEnumerable<long> marks){Marks.UnionWith(marks);}', $adapter.Replace('_markedChests', 'Marks'))
$stub = $stub.Replace('public static int netMode=1,myPlayer', 'public static string worldName="fixture"; public static int netMode=1,myPlayer')
$review = @'
namespace QualityOfTerraria.Networking {
 public static class MarkerIdentityReview {
  static void Server() {
   Main.netMode=2;
   var packets=NetSync.Requests.ToArray();NetSync.Requests.Clear();
   foreach(var p in packets){NetSync.OnStorageRequest(0,p.Operation,p.Payload);NetworkRuntime.Update();}
  }
  static void Client() {
   Main.netMode=1;
   var packets=NetSync.Responses.ToArray();NetSync.Responses.Clear();
   foreach(var p in packets){NetSync.OnStorageResponse(p.Success,p.Operation,p.Payload);NetworkRuntime.Update();}
  }
  public static void Run() {
   Main.chest[0]=new Chest{index=0,x=1,y=1};
   Main.netMode=2;ServerMarkedChests.Change(0,ServerMarkedChests.Key(Main.chest[0]),true);
   Main.netMode=1;RemoteStorageOperations.Initialize();RemoteStorageOperations.Update();Server();Client();
   RemoteStorageOperations.ResetSession();ExtremeStorageUI.Marks.Clear();
   PermissionService.Ids[0]="player-b";
   Main.netMode=2;
   if(ServerMarkedChests.IsMarked(0,Main.chest[0]))throw new Exception("fixture did not isolate server identity");
   Main.netMode=1;RemoteStorageOperations.Initialize();RemoteStorageOperations.Update();Server();Client();
   RemoteStorageOperations.Update();Server();Client();
   Main.netMode=2;
   bool leaked=ServerMarkedChests.IsMarked(0,Main.chest[0]);
   Console.WriteLine("REPRO: player-b starts with no server markers; after own handshake and automatic synchronization player-a marker appears = "+leaked);
   if(leaked)throw new Exception("FAIL: 世界共用客户端缓存自动把玩家A标记同步给玩家B");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub+($sources -join "`n")+$review)
[QualityOfTerraria.Networking.MarkerIdentityReview]::Run()
