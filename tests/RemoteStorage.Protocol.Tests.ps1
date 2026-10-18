$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$sources = @('Networking/NetSyncPayload.cs','Networking/RemoteStorageOperations.cs','Networking/ServerMarkedChests.cs','Networking/StorageTransfers.cs','StoragePersistence.cs') | ForEach-Object {
    [regex]::Replace((Get-Content (Join-Path $root $_) -Raw), '(?m)^using [^;]+;\s*', '')
}
$stub = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core.Net;
using TerrariaModder.Core.Permissions;
using QualityOfTerraria.UI;
namespace HarmonyLib {public static class AccessTools {public static System.Reflection.MethodInfo Method(Type t,string n){return t.GetMethod(n);}}}
namespace Terraria {
 public class Item {
  public int type,stack,maxStack=99; public byte prefix; public bool favorited; public bool IsAir {get{return type==0||stack<=0;}}
  public void SetDefaults(int id){type=id;stack=1;maxStack=99;} public bool Prefix(byte p){prefix=p;return true;}
  public Item Clone(){return (Item)MemberwiseClone();} public void TurnToAir(){type=0;stack=0;prefix=0;} public string AffixName(){return "item"+type;}
 }
 public class Player { public bool active=true,dead; public int whoAmI,itemAnimation,chest=-1; public Item[] inventory=Enumerable.Range(0,59).Select(i=>new Item()).ToArray(); public void OpenChest(int x,int y,int index){chest=index;} }
 public class WorldData {public Guid UniqueId=new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");}
 public static class Main {
  public static int netMode=1,myPlayer,maxPlayers=2,maxChests=260,maxTilesX=1000,maxTilesY=1000;
  public static ulong GameUpdateCount=1; public static string SavePath=Path.Combine(Path.GetTempPath(),"qot-protocol-"+Guid.NewGuid().ToString("N"));
  public static Player LocalPlayer=new Player(); public static Player[] player={new Player(),new Player()};
  public static PlayerData ActivePlayerFileData=new PlayerData();
  public static Chest[] chest=new Chest[260]; public static Item mouseItem=new Item(); public static WorldData ActiveWorldFileData=new WorldData();
  public static void NewText(string s,params byte[] rgb) {Messages.Add(s);} public static List<string> Messages=new List<string>();
 }
 public class PlayerData { public string Path="protocol-fixture"; public bool IsCloudSave; }
 public class Chest {
  public int index,x,y; public bool Locked; public int Owner=-1; public string name=""; public Item[] item=Enumerable.Range(0,40).Select(i=>new Item()).ToArray();
  public static int FindChest(int x,int y){for(int i=0;i<Main.chest.Length;i++)if(Main.chest[i]!=null&&Main.chest[i].x==x&&Main.chest[i].y==y)return i;return -1;}
  public static bool IsLocked(int x,int y){int i=FindChest(x,y);return i>=0&&Main.chest[i].Locked;}
  public static int UsingChest(int i){return Main.chest[i].Owner;}
  public static Chest CreateOutOfArray(int index,int x,int y,int count){return new Chest{index=index,x=x,y=y};}
 }
 public static class Recipe {public static void UpdateRecipeList(){}}
 public static class NetMessage {
  public static bool FailChest; public static int EquipmentSyncs,ChestSyncs;
  public static void SendTileSquare(int player,int x,int y,int size){}
  public static void SendData(int id,int remote=-1,int ignore=-1,object text=null,int player=0,int slot=0){
   if(id==MessageID.SyncEquipment)EquipmentSyncs++;
   if(id==MessageID.SyncEquipment&&Main.netMode==1)Main.player[player].inventory[slot]=Main.LocalPlayer.inventory[slot].Clone();
   if(id==MessageID.SyncChestItem){ChestSyncs++;if(FailChest)throw new IOException("injected chest packet failure");}
  }
 }
}
namespace Terraria.ID {public static class ItemID {public const int Count=1000;} public static class MessageID {public const int SyncEquipment=5,SyncChestItem=32;} }
namespace Terraria.GameContent {public static class CraftingRequests {public static bool HasPendingRequests;} }
namespace TerrariaModder.Core.Permissions {
 public static class PermissionService {public static string[] Ids={"player-a","player-b"};public static string GetGuid(int player){return Ids[player];}public static bool HasModAccess(int p,string m){return true;} }
}
namespace TerrariaModder.Core.Net {
 public struct Packet {public int Player;public string Operation,Payload;public bool Success;}
 public static class NetSync {
  public static Action<int,string,string> OnStorageRequest;public static Action<bool,string,string> OnStorageResponse;
  public static List<Packet> Requests=new List<Packet>(),Responses=new List<Packet>();
  public static void SendStorageRequest(string op,string data){Requests.Add(new Packet{Operation=op,Payload=data});}
  public static void SendStorageResponseTo(int p,bool success,string op,string data){Responses.Add(new Packet{Player=p,Success=success,Operation=op,Payload=data});}
 }
}
namespace QualityOfTerraria {
 public static class Mod {public static bool WorldActive=true;public static List<string> Errors=new List<string>();public static void LogError(string s,Exception e){Errors.Add(s+e.Message);}public static void LogInfo(string s){} }
}
namespace QualityOfTerraria.Networking {
 public static class StorageTransactionJournal {
  public class ClientRecord {public string World,Id,Before,Request,After;public bool Confirmed;}
  public static ClientRecord Pending;public static bool ClientLocked {get{return Pending!=null&&!Pending.Confirmed;}}public static string Status="";
  static Dictionary<string,string> Results=new Dictionary<string,string>();static Dictionary<string,HashSet<string>> Tokens=new Dictionary<string,HashSet<string>>();static Dictionary<string,string> Current=new Dictionary<string,string>();
  public static void InitializeClient(string id){}public static void InitializeServer(string world){}
  public static bool BeginClient(string world,string id,string before,string request){Pending=new ClientRecord{World=world,Id=id,Before=before,Request=request};return true;}
  public static bool CompleteClient(){Pending=null;return true;}
  public static bool CompleteClient(Item[] inventory,Item cursor){if(Pending!=null){Pending.Confirmed=true;Pending.After="confirmed";}return true;}
  public static void RegisterServerToken(string guid,string token){if(!Tokens.ContainsKey(guid))Tokens[guid]=new HashSet<string>();Tokens[guid].Add(token);Current[guid]=token;}
  public static void RecordServer(string guid,string id,string response){Results[guid+":"+id]=response;}
  public static bool TryGetServer(string guid,string id,out string response){if(Results.TryGetValue(guid+":"+id,out response))return true;string token=id.Substring(0,id.LastIndexOf('.'));return Tokens.ContainsKey(guid)&&Tokens[guid].Contains(token)&&Current[guid]!=token;}
 }
 public static class ToolNetwork {public static bool Busy;public static void RevokeServerToken(int client,string token){}}
 public static class NetworkRuntime {static Queue<Action> Queue=new Queue<Action>();public static void Enqueue(Action a){Queue.Enqueue(a);}public static void Update(){while(Queue.Count>0)Queue.Dequeue()();}}
}
namespace QualityOfTerraria.UI {
 public enum StorageTab {All,Weapons,Armor,Accessories,Tools,Ammo,Blocks,Alchemy,Furniture,ChestManager,Settings}
 public enum ItemSortMode {Default,Rarity,Stack,Name,ID,Kind}
 public static class UIManager {public static bool CanRender=true;}
 public static class RemoteStorageSnapshots {public static bool RefreshPending;public static void Initialize(){}public static void Invalidate(){}public static void RefreshAfterMutation(){}}
 public static class ExtremeStorageUI {
  public static HashSet<long> Marks=new HashSet<long>();public static string SearchText="";public static StorageTab CurrentTab;public static ItemSortMode CurrentSortMode;
  public static IEnumerable<long> GetMarkedCoordinates(){return Marks;}public static void AcceptServerMarks(string world,string owner,IEnumerable<long> marks){Marks.UnionWith(marks);}
  public static void RestoreRejectedMark(long key,bool mark){if(mark)Marks.Add(key);else Marks.Remove(key);}
  public static bool MatchesStorageTab(Item i,StorageTab tab){return tab==StorageTab.All||i.type==(int)tab;}
  public static bool MatchesSearch(string n,string q){return n.Contains(q);}public static void SortStorageItems(Item[] a,ItemSortMode mode){Array.Sort(a,(x,y)=>x.IsAir?(y.IsAir?0:1):y.IsAir?-1:x.type.CompareTo(y.type));}
  public static void RefreshItems(){}public static bool IsChestMarked(Chest c){return Marks.Contains(((long)c.x<<32)|(uint)c.y);}
  public static Chest GetChestForRead(int i){return Main.chest[i];}public static void CloseAfterRemoteOpen(){}
  public static bool CanMutateChest(int i){return i>=0&&Main.chest[i]!=null&&!Main.chest[i].Locked&&(Main.chest[i].Owner<0||Main.chest[i].Owner==Main.myPlayer)&&(Main.netMode!=1||Main.LocalPlayer.chest==i);}
  public static IEnumerable<Chest> GetMarkedReadableChests(){return Main.chest.Where(c=>c!=null&&IsChestMarked(c));}
 }
}
'@
$test = @'
namespace QualityOfTerraria.Networking {
 public static class ProtocolTests {
  static int checks;static void Check(bool b,string s){checks++;if(!b)throw new Exception(s);}
  static Item I(int t,int n,int p=0){return new Item{type=t,stack=n,prefix=(byte)p};}
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
  static void RoundTrip(){Server();Client();Main.GameUpdateCount++;}
  static Packet LastMove(){return NetSync.Requests.Last(p=>p.Operation.EndsWith("move"));}
  public static void Run(){
   try {
    Main.chest[0]=new Chest{index=0,x=1,y=1};Main.chest[1]=new Chest{index=1,x=900,y=900,Owner=0};
    Main.chest[0].item[0]=I(10,70,4);Main.chest[1].item[0]=I(10,40,5);
    Main.netMode=2;Check(RemoteStorageOperations.WorldIdentity==ServerMarkedChests.WorldIdentity,"服务器世界身份不得依赖客户端握手字段");Main.netMode=1;
    RemoteStorageOperations.Initialize();RemoteStorageOperations.Update();
    Check(!RemoteStorageOperations.Ready,"握手前不可读写");RoundTrip();Check(RemoteStorageOperations.Ready,"真实服务器握手后可用");
    ExtremeStorageUI.Marks.Add(ServerMarkedChests.Key(Main.chest[0]));ExtremeStorageUI.Marks.Add(ServerMarkedChests.Key(Main.chest[1]));
    RemoteStorageOperations.Update();var oldMarks=NetSync.Requests.Single();RoundTrip();
    Main.netMode=2;Check(ServerMarkedChests.IsMarked(0,Main.chest[0])&&ServerMarkedChests.IsMarked(0,Main.chest[1]),"跨世界远端坐标个人标记确认");
    Check(!ServerMarkedChests.IsMarked(1,Main.chest[0]),"两玩家标记不共享");Main.netMode=1;
    NetMessage.EquipmentSyncs=0;NetMessage.ChestSyncs=0;Check(RemoteStorageOperations.RequestTake(1,0,Main.chest[1].item[0],true),"远端无需靠近可请求取出");Check(NetMessage.EquipmentSyncs==0,"远端取物请求不广播59个未变化装备槽");var move=LastMove();
    Check(Main.LocalPlayer.inventory[0].IsAir&&Main.chest[1].item[0].stack==40,"请求阶段不能本地改物品");Server();
    Check(Main.player[0].inventory[0].stack==40&&Main.player[0].inventory[0].prefix==5&&Main.chest[1].item[0].IsAir,"服务器取出保留前缀和数量");Check(NetMessage.ChestSyncs==1,"服务器单格取物只同步发生变化的箱子槽位");
    Client();Check(Main.LocalPlayer.inventory[0].stack==40&&!RemoteStorageOperations.Busy,"确认后才更新角色背包");
    NetSync.Requests.Add(move);RoundTrip();Check(Main.LocalPlayer.inventory[0].stack==40,"重复请求不重复发放");
    RemoteStorageOperations.RequestToolbar(0);RoundTrip();Check(Main.LocalPlayer.inventory[0].IsAir&&Main.chest.Any(c=>c!=null&&c.item.Any(i=>i.type==10&&i.prefix==5&&i.stack==40)),"全部存入保持前缀守恒");
    Main.chest[1].item[0]=I(10,40,5);Main.chest[0].item[1].TurnToAir();
    Main.chest[1].Locked=true;RemoteStorageOperations.RequestTake(1,0,Main.chest[1].item[0],true);RoundTrip();
    Check(Main.LocalPlayer.inventory[0].IsAir&&Main.chest[1].item[0].stack==40,"锁箱不转移");Main.chest[1].Locked=false;
    Main.chest[1].Owner=1;RemoteStorageOperations.RequestTake(1,0,Main.chest[1].item[0],true);RoundTrip();
    Check(Main.LocalPlayer.inventory[0].IsAir&&Main.chest[1].item[0].stack==40,"被他人占用不转移");Main.chest[1].Owner=-1;
    Main.mouseItem=I(22,15,7);RemoteStorageOperations.RequestDeposit(Main.mouseItem);RoundTrip();
    Check(Main.mouseItem.IsAir&&Main.chest.Any(c=>c!=null&&c.item.Any(i=>i.type==22&&i.stack==15&&i.prefix==7)),"鼠标存入需要服务器实际同步的58号槽位");
    RemoteStorageOperations.RequestTake(0,0,Main.chest[0].item[0],false);Server();
    Main.LocalPlayer.inventory[12]=I(44,3);Client();
    Check(Main.LocalPlayer.inventory[0].stack==35&&Main.LocalPlayer.inventory[12].type==44&&!RemoteStorageOperations.Busy,"无关槽位外部变化保留并正确确认实际改动槽");
    RemoteStorageOperations.RequestTake(0,0,Main.chest[0].item[0],true);
    Main.player[0].inventory[0].stack=1;RoundTrip();Check(Main.chest[0].item[0].stack==35&&Main.LocalPlayer.inventory[0].stack==35,"服务器背包不匹配不能转移或覆盖本地");
    Main.LocalPlayer.inventory[0]=I(10,35,4);Main.LocalPlayer.inventory[1]=I(10,40,5);Main.LocalPlayer.inventory[12].favorited=true;
    RemoteStorageOperations.RequestToolbar(0);RoundTrip();Check(Main.LocalPlayer.inventory[12].stack==3,"收藏物品全部存入时保留");
    RemoteStorageOperations.RequestToolbar(2);RoundTrip();Check(Main.chest.SelectMany(c=>c==null?new Item[0]:c.item).Where(i=>i.type==10&&i.prefix==4).Sum(i=>i.stack)==70,"服务器跨箱整理数量不变");
    Check(Main.chest.SelectMany(c=>c==null?new Item[0]:c.item).Where(i=>i.type==10&&i.prefix==5).Sum(i=>i.stack)==80,"不同前缀不会合并");
    ExtremeStorageUI.Marks.Remove(ServerMarkedChests.Key(Main.chest[1]));RemoteStorageOperations.Update();RoundTrip();
    NetSync.Requests.Add(oldMarks);RoundTrip();Main.netMode=2;Check(!ServerMarkedChests.IsMarked(0,Main.chest[1]),"迟到旧标记请求不能恢复已取消标记");Main.netMode=1;
    Main.chest[0].Owner=0;NetMessage.FailChest=true;
    RemoteStorageOperations.RequestTake(0,0,Main.chest[0].item[0],true);move=LastMove();Server();Client();
    Check(!RemoteStorageOperations.Busy&&Main.LocalPlayer.inventory.Any(i=>i.type==10&&i.prefix==4),"槽位广播异常不能丢失已缓存提交结果");
    int total=Main.LocalPlayer.inventory.Where(i=>i.type==10&&i.prefix==4).Sum(i=>i.stack);NetSync.Requests.Add(move);RoundTrip();
    Check(Main.LocalPlayer.inventory.Where(i=>i.type==10&&i.prefix==4).Sum(i=>i.stack)==total,"广播异常后重发不重复扣物品");NetMessage.FailChest=false;
    Main.chest[0].Owner=-1;
    Check(RemoteStorageOperations.RequestOpenChest(0),"原版箱子界面可申请远程打开");RoundTrip();
    Check(Main.LocalPlayer.chest==0&&RemoteStorageOperations.KeepRemoteChest(Main.LocalPlayer),"服务器确认后原版箱子界面保持远程会话");
    Main.chest[0].Locked=true;Check(!RemoteStorageOperations.KeepRemoteChest(Main.LocalPlayer),"远程打开仍遵守锁箱限制");Main.chest[0].Locked=false;
    // 新增：测试已确认但未落盘的记录不再发送 recover
    Main.chest[1].item[0]=I(33,20,3);ExtremeStorageUI.Marks.Add(ServerMarkedChests.Key(Main.chest[1]));RemoteStorageOperations.Update();RoundTrip();
    NetSync.Requests.Clear();NetSync.Responses.Clear();
    Check(RemoteStorageOperations.RequestTake(1,0,Main.chest[1].item[0],true),"开始第一次取物请求");RoundTrip();
    Check(Main.LocalPlayer.inventory.Any(i=>i.type==33&&i.stack==20),"第一次取物成功");
    Check(StorageTransactionJournal.Pending!=null&&StorageTransactionJournal.Pending.Confirmed,"CompleteClient保留Confirmed记录");
    Check(!RemoteStorageOperations.Busy,"已确认记录不锁库存");
    int oldRequests=NetSync.Requests.Count;for(int tick=0;tick<200;tick++){Main.GameUpdateCount++;RemoteStorageOperations.Update();}
    Check(NetSync.Requests.Count==oldRequests,"已确认记录200帧内不发送recover请求");
    Check(NetSync.Requests.All(r=>!r.Operation.EndsWith("recover")),"Update循环不得误当断线事务");
    int oldMessages=Main.Messages.Count;for(int tick=0;tick<10;tick++){Client();}
    Check(Main.Messages.Count==oldMessages,"已确认记录不再重复显示提示");
    RemoteStorageOperations.ResetSession();Check(!RemoteStorageOperations.Ready&&!RemoteStorageOperations.Busy&&NetSync.OnStorageRequest==null,"退出精确解绑并清会话");
    Console.WriteLine("PASS: "+checks+" 个真实生产协议、服务器物品守恒、重放与已确认记录场景（模拟传输）");
   } finally {RemoteStorageOperations.ResetSession();}
  }
 }
}
'@
Add-Type -TypeDefinition ($stub + ($sources -join "`n") + $test)
[QualityOfTerraria.Networking.ProtocolTests]::Run()
