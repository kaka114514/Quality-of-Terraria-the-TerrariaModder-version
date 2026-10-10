$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$production=Get-Content (Join-Path $root 'Networking/ToolNetwork.cs') -Raw
$runtime=Get-Content (Join-Path $root 'Networking/NetworkRuntime.cs') -Raw
$production=[regex]::Replace($production,'(?m)^using .*;\r?\n','')
$runtime=[regex]::Replace($runtime,'(?m)^using .*;\r?\n','')
$harness=@'
using System;using System.Collections.Generic;using System.IO;using Terraria;using Terraria.ID;using TerrariaModder.Core.Net;using TerrariaModder.Core.Permissions;using QualityOfTerraria;using QualityOfTerraria.Networking;using QualityOfTerraria.Patches;
namespace Terraria {
 public class Item {public int type=1,stack=10;public byte prefix=3;public bool favorited;public int createTile=1,createWall=1;public bool IsAir=>type==0||stack==0;public void TurnToAir(){type=0;stack=0;prefix=0;}}
 public class Player {public bool active=true,dead,mouseInterface,controlUseItem;public int selectedItem,talkNPC=-1,itemAnimation;public bool NearMerchant;public bool isNearNPC(int type,float range)=>NearMerchant;public Item[] inventory=Make();static Item[] Make(){var a=new Item[59];for(int i=0;i<a.Length;i++)a[i]=new Item {type=0,stack=0,prefix=0};a[0]=new Item();return a;}}
 public class NPC {public bool active=true;public int type=368;}public class Chest {public static int ShopRefreshes;public void SetupShop(int id){ShopRefreshes++;}}
 public class Main {public static Main instance=new Main();public static int netMode=1,maxPlayers=2,maxNPCs=1,npcShop;public Chest[] shop={new Chest(),new Chest()};public static NPC[] npc={new NPC()};public static int maxTilesX=1000,maxTilesY=1000,myPlayer;public static bool raining,halloween,xMas,drunkWorld,zenithWorld,remixWorld,dontStarveWorld;public static ulong GameUpdateCount;public static Player[] player={new Player(),new Player()};public static Player LocalPlayer=>player[myPlayer];public static Item mouseItem=new Item {type=0,stack=0,prefix=0};public static string Text;public static void NewText(string s,int r,int g,int b){Text=s;}}
 public static class NetMessage {public static int Player,Slot,EquipmentSyncs;public static void SendData(int id,int a=-1,int b=-1,object text=null,int player=0,float slot=0){Player=player;Slot=(int)slot;if(id==5)EquipmentSyncs++;}}
}
namespace Terraria.GameContent {public static class CraftingRequests {public static bool HasPendingRequests;}}
namespace Terraria.ID {public static class MessageID {public const int SyncEquipment=5,WorldData=7;}public static class NPCID {public const int TravellingMerchant=368;}public static class ItemID {public const int Count=10000;}}
namespace TerrariaModder.Core.Net {public static class NetSync {public static Action<int,string,string> OnStorageRequest;public static Action<bool,string,string> OnStorageResponse;public static string Request,Response,Operation;public static bool Success;public static bool LocalPlayerHasModAccess(string mod)=>PermissionService.Access;public static void SendStorageRequest(string op,string s){Request=s;Operation=op;}public static void SendStorageResponseTo(int client,bool ok,string op,string s){Success=ok;Response=s;}}}
namespace TerrariaModder.Core.Permissions {public static class PermissionService {public static bool Access=true,Admin;public static string Identity="player-guid";public static string GetGuid(int slot)=>Identity;public static bool HasModAccess(int slot,string mod)=>Access;public static bool IsAdmin(int slot)=>Admin;}}
namespace QualityOfTerraria {public static class Mod {public static bool WorldActive=true;public static Config Config=new Config();public static void LogError(string s,Exception e){throw new Exception(s,e);}}
 public class Config {public bool WandMaterialNoConsume;}
 public static class WandsAndTools {public enum LiquidWandMode{Off,Water,Lava,Shimmer,Honey}public static int StoredWater,StoredLava,StoredShimmer,StoredHoney,Placed,Homes,Weather;public static Player Actor;public static bool ThrowAfter;
 public static int ApplyDragPlacement(Player p,int x1,int y1,int x2,int y2){Actor=p;Placed++;if(!SettingsSync.GetPlayerConfig(p==Main.player[0]?0:1).WandMaterialNoConsume)p.inventory[p.selectedItem].stack--;if(ThrowAfter){ThrowAfter=false;throw new Exception("执行中同步异常");}return 1;}public static int ApplyWallWand(Player p){Actor=p;Placed++;if(!SettingsSync.GetPlayerConfig(p==Main.player[0]?0:1).WandMaterialNoConsume)p.inventory[0].stack--;return 1;}public static int FindWallSlot(Player p)=>0;
 public static void ApplyLiquid(int x,int y,LiquidWandMode m,bool absorb,bool release){if(absorb)StoredWater+=17;if(release && StoredWater>0)StoredWater--;}
 public static int ApplyNPCHome(){Homes++;return 1;}public static void ApplyWeather(){Weather++;Main.raining=!Main.raining;}}
}
namespace QualityOfTerraria.Patches {public static class WorldQoLCompletionPatches {public static float Rate=1;public static bool SetSpawnRate(float n){Rate=n;return true;}}public static class WorldFeatureActions {public static int Refreshes;public static bool RefreshTravelShop(){Refreshes++;return true;}}}
namespace QualityOfTerraria.Networking {public static class StorageTransactionJournal {public class Record{public string Id,Before,Request;}public static Record Pending;public static bool BeginOk=true,SaveOk=true;public static string Status="日志保存失败";public static int Begins,Completes;public static Dictionary<string,string> Replies=new Dictionary<string,string>();public static string Registered;
 public static bool BeginClient(string world,string id,string before,string request){Begins++;if(!BeginOk||Pending!=null)return false;Pending=new Record{Id=id,Before=before,Request=request};return true;}public static bool CompleteClient(){Completes++;if(!SaveOk)return false;Pending=null;return true;}public static void InitializeServer(string world){}public static void RegisterServerToken(string guid,string token){Registered=token;}public static void RecordServer(string guid,string id,string response){Replies[guid+":"+id]=response;}}
public static class SettingsSync {public static Config[] Profiles={new Config(),new Config {WandMaterialNoConsume=true}};public static Config GetPlayerConfig(int slot)=>Main.netMode==2?Profiles[slot]:Mod.Config;}public static class RemoteStorageOperations {public static bool Busy;public static string WorldIdentity="11111111111111111111111111111111";}}
public static class ToolNetworkTests {
 static int checks;static void Check(bool ok,string name){checks++;if(!ok)throw new Exception("FAIL: "+name);}
 static void Server(string request){Main.netMode=2;Main.myPlayer=1;NetSync.Response=null;NetSync.OnStorageRequest(0,NetSync.Operation,request);NetworkRuntime.Update();}
 static void Client(){Main.netMode=1;Main.myPlayer=0;NetSync.OnStorageResponse(NetSync.Success,NetSync.Operation,NetSync.Response);NetworkRuntime.Update();}
 static void Send(string op,int slot=-1,params int[] values){Main.netMode=1;Main.myPlayer=0;Main.GameUpdateCount+=10;ToolNetwork.Request(op,slot,values);}
 static int Stack(string state){using(var r=new BinaryReader(new MemoryStream(Convert.FromBase64String(state)))){r.ReadInt32();return r.ReadInt32();}}
 static int ServerStack(string response,out bool valid){using(var r=new BinaryReader(new MemoryStream(Convert.FromBase64String(response)))){r.ReadString();r.ReadInt64();valid=r.ReadBoolean();r.ReadString();r.ReadInt32();return r.ReadInt32();}}
 public static void Run(){ToolNetwork.Initialize();Send("place",0,20,20,20,20);string hello=NetSync.Request;
  Main.netMode=2;Main.myPlayer=1;NetSync.OnStorageRequest(0,NetSync.Operation,hello);Check(NetSync.Response==null,"收包回调不直接修改游戏或发送结果");NetworkRuntime.Update();Client();Check(ToolNetwork.Busy && NetSync.Request!=hello,"首次工具使用握手后自动继续操作");
  Send("place",0,20,20,20,20);string place=NetSync.Request;Check(StorageTransactionJournal.Pending!=null && StorageTransactionJournal.Pending.Request==place && Stack(StorageTransactionJournal.Pending.Before)==10 && ToolNetwork.HasPendingMaterial,"材料请求发送前持久记录59槽原库存与请求");Server(place);Check(WandsAndTools.Placed==1 && Main.player[0].inventory[0].stack==9 && ReferenceEquals(WandsAndTools.Actor,Main.player[0]),"服务器使用真实发送者材料");Check(NetMessage.Player==0 && NetMessage.Slot==0,"服务器同步真实玩家材料槽");string response=NetSync.Response;bool journalValid;string journalId=StorageTransactionJournal.Pending.Id;Check(ServerStack(StorageTransactionJournal.Replies[PermissionService.Identity+":"+journalId],out journalValid)==9 && journalValid,"服务器日志兼容统一恢复格式且库存数量守恒");Client();Check(StorageTransactionJournal.Pending==null,"正常材料确认保存后完成日志");Check(Main.player[0].inventory[0].prefix==3,"材料前缀保持");
  Server(place);Check(WandsAndTools.Placed==1 && NetSync.Response==response,"重复请求只返回缓存不再次消耗");Client();
  string beforeCraft=NetSync.Request;int beginsBefore=StorageTransactionJournal.Begins;Terraria.GameContent.CraftingRequests.HasPendingRequests=true;Send("wall",0);
  Check(NetSync.Request==beforeCraft && StorageTransactionJournal.Begins==beginsBefore && !ToolNetwork.Busy,"原版合成请求未确认时不记日志不发工具请求");Terraria.GameContent.CraftingRequests.HasPendingRequests=false;
  Main.player[0].itemAnimation=10;Send("wall",0);Check(NetSync.Request==beforeCraft && StorageTransactionJournal.Begins==beginsBefore && !ToolNetwork.Busy,"正在使用物品时不记日志不发工具请求");Main.player[0].itemAnimation=0;
  StorageTransactionJournal.BeginOk=false;string lastSent=NetSync.Request;Send("wall",0);Check(NetSync.Request==lastSent && !ToolNetwork.Busy,"请求前日志失败不发送、不改变材料");StorageTransactionJournal.BeginOk=true;
  Send("wall",0);string deadId=StorageTransactionJournal.Pending.Id;Main.player[0].dead=true;Server(NetSync.Request);Client();bool deadValid;ServerStack(StorageTransactionJournal.Replies[PermissionService.Identity+":"+deadId],out deadValid);Check(deadValid && StorageTransactionJournal.Pending==null,"服务器玩家已死亡也记录拒绝并完成等待日志");Main.player[0].dead=false;
  SettingsSync.Profiles[0]=null;Send("wall",0);Server(NetSync.Request);Client();Check(StorageTransactionJournal.Pending==null,"拒绝材料请求也完成客户端日志");Check(!NetSync.Success && WandsAndTools.Placed==1 && Main.Text.Contains("个人设置交换"),"未确认个人配置拒绝材料操作并明确反馈");
  SettingsSync.Profiles[0]=new Config {WandMaterialNoConsume=true};int clientQuantity=Main.player[0].inventory[0].stack,syncs=NetMessage.EquipmentSyncs;Send("place",0,20,20,20,20);Main.player[0].inventory[0].stack--;syncs=NetMessage.EquipmentSyncs;Server(NetSync.Request);
  Check(NetSync.Success && NetMessage.EquipmentSyncs==syncs && Main.player[0].inventory[0].stack==clientQuantity-1,"个人无耗时数量快照不同仍施工且不改服务器数量或发库存包");
  Main.player[0].inventory[0].stack=clientQuantity;Client();Check(Main.player[0].inventory[0].stack==clientQuantity,"无耗应答不覆盖客户端材料数量");
  SettingsSync.Profiles[0]=new Config();
  Send("wall",0);Main.player[0].inventory[0].stack--;int before=WandsAndTools.Placed;Server(NetSync.Request);Check(!NetSync.Success && WandsAndTools.Placed==before,"材料状态改变拒绝过期操作");Client();
  Send("weather");Server(NetSync.Request);Check(!NetSync.Success && WandsAndTools.Weather==0,"无管理员权限不能改变天气");Client();
  PermissionService.Admin=true;Send("weather");string weather=NetSync.Request;Server(weather);Client();Server(weather);Check(WandsAndTools.Weather==1,"管理员天气请求重放只执行一次");Client();
  PermissionService.Admin=false;Send("spawn-rate",-1,1000);Server(NetSync.Request);Client();Check(WorldQoLCompletionPatches.Rate==1,"非管理员刷怪倍率请求拒绝");
  PermissionService.Admin=true;foreach(int v in new[]{0,1000}){Send("spawn-rate",-1,v);Server(NetSync.Request);Client();Check(WorldQoLCompletionPatches.Rate==v/10f,"刷怪倍率边界 "+v);}
  foreach(int v in new[]{-1,1001}){Send("spawn-rate",-1,v);Server(NetSync.Request);Client();Check(!NetSync.Success && WorldQoLCompletionPatches.Rate==100,"刷怪倍率越界拒绝 "+v);}
  Send("world-toggle",-1,3,1);string toggle=NetSync.Request;Server(toggle);Main.zenithWorld=false;Client();Check(Main.zenithWorld,"客户端世界状态仅由服务器确认响应更新");Server(toggle);Client();Check(Main.zenithWorld,"世界切换按目标值写入，重复请求不会翻回");
  PermissionService.Admin=false;Send("world-toggle",-1,3,0);Server(NetSync.Request);Client();Check(!NetSync.Success && Main.zenithWorld,"非管理员不能切换世界状态");
  Send("travel-shop");Server(NetSync.Request);Client();Check(!NetSync.Success && WorldFeatureActions.Refreshes==0,"服务器拒绝远离旅商刷新");
  Main.player[0].NearMerchant=true;Main.player[0].talkNPC=0;Main.npcShop=1;Send("travel-shop");string travel=NetSync.Request;Server(travel);Client();Server(travel);Client();Check(WorldFeatureActions.Refreshes==1,"旅商附近普通有权限玩家可刷新且重复只刷新一次");Check(Chest.ShopRefreshes==1,"服务器旅商刷新确认后重建当前商店网格");
  PermissionService.Admin=true;
  Send("world-toggle",-1,3,1);string worldRetry=NetSync.Request;Server(worldRetry);Main.zenithWorld=false;Server(worldRetry);Client();Check(!Main.zenithWorld,"重查旧世界操作返回当前状态，不覆盖其他管理员的新状态");
  Send("liquid",-1,20,20,1,1,0);Server(NetSync.Request);Client();Check(WandsAndTools.StoredWater==17,"服务器液体库存响应确认");
  Send("liquid",-1,20,20,1,0,1);string liquid=NetSync.Request;Server(liquid);Client();Server(liquid);Client();Check(WandsAndTools.StoredWater==16,"液体重复请求保持数量守恒");
  Send("place",0,10,10,999,999);Server(NetSync.Request);Check(!NetSync.Success && WandsAndTools.Placed==before,"超大框选不修改世界");Client();
  Send("wall",0);string retry=NetSync.Request;NetSync.OnStorageResponse(true,"other-mod.operation",response);NetworkRuntime.Update();Check(ToolNetwork.Busy,"不消费其他模组响应或取消当前物品请求");
  Server(retry);before++;Main.netMode=1;Main.myPlayer=0;Main.GameUpdateCount+=300;ToolNetwork.Update();Check(ToolNetwork.Busy && NetSync.Request==retry,"有材料请求超时保持锁定并查询相同payload");
  Server(retry);Client();Check(WandsAndTools.Placed==before && !ToolNetwork.Busy,"丢失应答后重查只扣一次材料");
  int expectedStack=Main.player[0].inventory[0].stack;Send("place",0,20,20,20,20);string conflictRequest=NetSync.Request;Server(conflictRequest);before++;
  Main.player[0].inventory[0]=new Item {type=2,stack=1,prefix=0};Client();Check(ToolNetwork.Busy && Main.player[0].inventory[0].type==2,"材料槽外部变更时不覆盖物品、不解除事务锁");
  Main.player[0].inventory[0]=new Item {type=1,stack=expectedStack,prefix=3};Server(conflictRequest);Client();Check(!ToolNetwork.Busy && Main.player[0].inventory[0].stack==expectedStack-1 && WandsAndTools.Placed==before,"材料冲突恢复后同一答复正确完成且不再次施工");
  int partialBefore=Main.player[0].inventory[0].stack;Send("place",0,20,20,20,20);string partialId=StorageTransactionJournal.Pending.Id;WandsAndTools.ThrowAfter=true;Server(NetSync.Request);before++;
  bool partialValid;Check(!NetSync.Success && ServerStack(StorageTransactionJournal.Replies[PermissionService.Identity+":"+partialId],out partialValid)==partialBefore-1 && partialValid,"部分执行后异常日志仍确认真实after库存");
  Main.player[0].inventory[0].stack=partialBefore;Client();Check(Main.player[0].inventory[0].stack==partialBefore-1 && StorageTransactionJournal.Pending==null,"部分执行失败响应按真实扣后库存保存，不恢复未扣Before");
  StorageTransactionJournal.SaveOk=false;int saveQuantity=Main.player[0].inventory[0].stack;Send("wall",0);string saveRequest=NetSync.Request;Server(saveRequest);before++;Client();
  Check(ToolNetwork.HasPendingMaterial && StorageTransactionJournal.Pending!=null && Main.player[0].inventory[0].stack==saveQuantity-1,"确认库存保存失败保留日志与材料等待锁");
  StorageTransactionJournal.SaveOk=true;Server(saveRequest);Client();Check(!ToolNetwork.Busy && StorageTransactionJournal.Pending==null && WandsAndTools.Placed==before,"保存失败后缓存重查完成，不再次扣材料施工");
  int reconnectQuantity=Main.player[0].inventory[0].stack;Send("wall",0);string disconnectedRequest=NetSync.Request;string disconnectedId=StorageTransactionJournal.Pending.Id;Server(disconnectedRequest);before++;
  ToolNetwork.ResetSession();Check(!ToolNetwork.HasPendingMaterial && StorageTransactionJournal.Pending!=null,"断线清工具等待但保留跨断线日志供统一恢复");
  bool executedValid;int recovered=ServerStack(StorageTransactionJournal.Replies[PermissionService.Identity+":"+disconnectedId],out executedValid);Check(executedValid && recovered==reconnectQuantity-1 && Stack(StorageTransactionJournal.Pending.Before)==reconnectQuantity,"断线后服务器日志恢复扣后数量，材料总量守恒");
  Main.player[0].inventory[0].stack=recovered;StorageTransactionJournal.CompleteClient();
  Send("world-toggle",-1,0,0);Server(NetSync.Request);Client();Server(NetSync.Request);Client();
  Send("wall",0);string unexecutedRequest=NetSync.Request;string unusedId=StorageTransactionJournal.Pending.Id;string oldToken=unusedId.Substring(0,unusedId.LastIndexOf('.'));Main.netMode=2;ToolNetwork.RevokeServerToken(0,oldToken);Server(unexecutedRequest);
  Check(!NetSync.Success && WandsAndTools.Placed==before && !StorageTransactionJournal.Replies.ContainsKey(PermissionService.Identity+":"+unusedId),"恢复撤销旧工具token后迟到未执行包不能再扣材料");Client();Check(StorageTransactionJournal.Pending!=null && !ToolNetwork.HasPendingMaterial,"未知旧token不清日志，交统一恢复");StorageTransactionJournal.CompleteClient();Send("world-toggle",-1,0,0);Server(NetSync.Request);Client();Server(NetSync.Request);Client();
  Send("place",0,20,20,20,20);string stale=NetSync.Request;PermissionService.Identity="new-player-guid";Server(stale);Check(!NetSync.Success && WandsAndTools.Placed==before,"索引复用身份变化拒绝旧会话");Client();StorageTransactionJournal.CompleteClient();
  ToolNetwork.ResetSession();Send("wall",0);Main.GameUpdateCount+=300;ToolNetwork.Update();Check(!ToolNetwork.Busy && Main.Text.Contains("未收到服务器确认"),"超时不能反馈成功");
  ToolNetwork.ResetSession();Send("home");Main.netMode=2;NetSync.OnStorageRequest(0,NetSync.Operation,NetSync.Request);ToolNetwork.ResetSession();NetworkRuntime.Update();Check(WandsAndTools.Homes==0,"返回菜单丢弃旧会话排队请求");
  ToolNetwork.Unload();Check(NetSync.OnStorageRequest==null && NetSync.OnStorageResponse==null,"卸载只解绑自身事件");Console.WriteLine("工具网络行为检查: "+checks+"/"+checks);
 }
}
'@
Add-Type -TypeDefinition ($harness+"`n"+$production+"`n"+$runtime) -Language CSharp
[ToolNetworkTests]::Run()
