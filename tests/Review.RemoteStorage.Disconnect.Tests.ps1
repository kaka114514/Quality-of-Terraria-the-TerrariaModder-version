$ErrorActionPreference = 'Stop'
$fixture = Get-Content (Join-Path $PSScriptRoot 'RemoteStorage.Protocol.Tests.ps1') -Raw
$preamble = $fixture.Substring(0, $fixture.IndexOf("`$test = @'"))
$root = Split-Path $PSScriptRoot
$preamble = $preamble.Replace('$root = Split-Path $PSScriptRoot', '')
Invoke-Expression $preamble
$review = @'
namespace QualityOfTerraria.Networking {
 public static class DisconnectReview {
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
  static void Check(bool b,string s){if(!b)throw new Exception(s);}
  static int Owned(){return Main.LocalPlayer.inventory.Where(i=>i.type==10).Sum(i=>i.stack)+Main.chest[0].item.Where(i=>i.type==10).Sum(i=>i.stack);}
  public static void Run() {
   Main.chest[0]=new Chest{index=0,x=1,y=1};
   Main.netMode=2;Check(ServerMarkedChests.Change(0,ServerMarkedChests.Key(Main.chest[0]),true),"initial marker");
   Main.netMode=1;RemoteStorageOperations.Initialize();RemoteStorageOperations.Update();Server();Client();
   Main.LocalPlayer.inventory[0]=new Item{type=10,stack=10,prefix=4};
   Check(RemoteStorageOperations.RequestDeposit(Main.LocalPlayer.inventory[0]),"deposit request");
   Server();
   Check(Main.chest[0].item[0].stack==10&&Main.player[0].inventory[0].IsAir,"server transaction committed");
   Main.netMode=1;RemoteStorageOperations.ResetSession();
   Check(RemoteStorageOperations.Busy&&StorageTransactionJournal.Pending!=null,"退出后未确认库存仍有日志与保护");
   NetSync.Responses.Clear();
   Main.LocalPlayer.inventory[0]=new Item(); // 上次角色存档早于本次取得10件物品，日志保存的是更晚的真实库存。
   RemoteStorageOperations.Initialize();RemoteStorageOperations.Update();Server();Client();
   RemoteStorageOperations.Update();Server();Client();
   Check(!RemoteStorageOperations.Busy&&Main.LocalPlayer.inventory[0].IsAir&&Owned()==10,"存入后断线重连只应用旧结果，不复制物品");
   Check(RemoteStorageOperations.RequestTake(0,0,Main.chest[0].item[0],true),"take request");Server();
   Main.netMode=1;NetSync.Responses.Clear();RemoteStorageOperations.ResetSession();
   Check(RemoteStorageOperations.Busy,"取出确认丢失后仍保留日志");
   RemoteStorageOperations.Initialize();RemoteStorageOperations.Update();Server();Client();RemoteStorageOperations.Update();Server();Client();
   Check(!RemoteStorageOperations.Busy&&Owned()==10&&Main.LocalPlayer.inventory[0].stack==10,"取出后断线重连恢复唯一一份物品");
   Check(RemoteStorageOperations.RequestDeposit(Main.LocalPlayer.inventory[0]),"unreceived request");
   NetSync.Requests.Clear();RemoteStorageOperations.ResetSession();Main.LocalPlayer.inventory[0]=new Item();
   RemoteStorageOperations.Initialize();RemoteStorageOperations.Update();Server();Client();RemoteStorageOperations.Update();Server();Client();
   Check(!RemoteStorageOperations.Busy&&Owned()==10&&Main.LocalPlayer.inventory[0].stack==10,"服务器明确未执行时恢复日志中的最新原库存");
   Console.WriteLine("PASS: 存入、取出确认丢失以及请求未送达的三种跨断线恢复保持物品总量10");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub+($sources -join "`n")+$review)
[QualityOfTerraria.Networking.DisconnectReview]::Run()
