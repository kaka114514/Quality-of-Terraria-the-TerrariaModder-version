$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
foreach ($file in @('Networking/NetworkRuntime.cs', 'Networking/SettingsSync.cs')) {
    if (!(Test-Path (Join-Path $root $file))) { throw "FAIL 设置申请行为尚未实现：$file" }
}
$fake = @'
using System;
using System.Collections.Generic;
using System.Reflection;
namespace Terraria {
 public class Player { public bool active=true,mouseInterface; public string name="玩家"; }
 public static class Main { public static int netMode=2,myPlayer=0; public static bool dedServ=true,gameMenu; public static Player[] player={new Player(),new Player(),new Player()}; public static void NewText(string text,byte r=255,byte g=255,byte b=255) {} }
}
namespace TerrariaModder.Core.Config {
 public enum ConfigScope {Client,Server}
 public class LabelAttribute:Attribute{}
 public class ClientAttribute:Attribute{}
 public class ServerAttribute:Attribute{}
 public class ConfigPropertyMeta { public PropertyInfo Property; public string Key {get{return Property.Name;}} public string Label {get{return Key;}} public Type PropertyType {get{return Property.PropertyType;}} public ConfigScope Scope; public bool RestartRequired; public double? Min,Max; public object GetValue(ModConfig c){return Property.GetValue(c);} public void SetValue(ModConfig c,object v){Property.SetValue(c,v);} }
 public static class ConfigManager {public static ConfigScope GetCurrentScope(){return Terraria.Main.netMode==2?ConfigScope.Server:ConfigScope.Client;}public static Dictionary<string,object> ParseJsonRaw(string json){var result=new Dictionary<string,object>();foreach(var p in System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject()){result[p.Name]=p.Value.ValueKind==System.Text.Json.JsonValueKind.True?(object)true:p.Value.ValueKind==System.Text.Json.JsonValueKind.False?(object)false:p.Value.ValueKind==System.Text.Json.JsonValueKind.Number?(object)p.Value.GetDouble():(object)p.Value.GetString();}return result;}}
 public class ModConfig { public int Version=2; public int Saves; public bool FailSave; public string FilePath=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString("N")+".json"); public void Save(){if(FailSave)return;var raw=new Dictionary<string,object>{{"_version",Version}};foreach(var p in GetPropertyMetadata())if(p.Scope==ConfigManager.GetCurrentScope())raw[p.Key]=p.GetValue(this);System.IO.File.WriteAllText(FilePath,System.Text.Json.JsonSerializer.Serialize(raw));Saves++;} public virtual IReadOnlyList<ConfigPropertyMeta> GetPropertyMetadata(){return new ConfigPropertyMeta[0];} }
}
namespace TerrariaModder.Core.Permissions {
 public static class PermissionService {
  public enum PlayerRole {Player,Admin}
  public static Dictionary<int,string> Ids=new Dictionary<int,string>{{0,"甲身份"},{1,"乙身份"},{2,"丙身份"}};
  public static bool Admin;
  public static string GetGuid(int slot){return Ids.ContainsKey(slot)?Ids[slot]:null;}
  public static bool IsAdmin(int slot){return Admin;}
 }
}
namespace TerrariaModder.Core.Net {
 public static class NetSync {
  public static event Action<int,string,string> OnStorageRequest;
  public static event Action<bool,string,string> OnStorageResponse;
  public static bool LocalPlayerIsAdmin;
  public static List<Tuple<int,bool,string,string>> Sent=new List<Tuple<int,bool,string,string>>();
  public static void SendStorageRequest(string op,string data){OnStorageRequest?.Invoke(Terraria.Main.myPlayer,op,data);}
  public static void SendStorageResponseTo(int slot,bool ok,string op,string data){string full;if(!QualityOfTerraria.Networking.NetSyncPayload.TryAcceptResponse(op,data,out full))return;Sent.Add(Tuple.Create(slot,ok,op,full));}
  public static void BroadcastConfigChange(string id,TerrariaModder.Core.Config.ConfigPropertyMeta p,TerrariaModder.Core.Config.ModConfig c,int slot){Broadcasts++;}
  public static int Broadcasts;
  public static void Request(int slot,string op,string data){OnStorageRequest?.Invoke(slot,op,data);}
  public static void Response(bool ok,string op,string data){OnStorageResponse?.Invoke(ok,op,data);}
 }
}
namespace QualityOfTerraria {
 public class QualityOfTerrariaConfig:TerrariaModder.Core.Config.ModConfig {
  [TerrariaModder.Core.Config.Client, TerrariaModder.Core.Config.Label] public bool Personal {get;set;}=true;
  [TerrariaModder.Core.Config.Server, TerrariaModder.Core.Config.Label] public int World {get;set;}=3;
  public string Secret {get;set;}="private";
  public override IReadOnlyList<TerrariaModder.Core.Config.ConfigPropertyMeta> GetPropertyMetadata(){return new[]{
   new TerrariaModder.Core.Config.ConfigPropertyMeta{Property=GetType().GetProperty("Personal"),Scope=TerrariaModder.Core.Config.ConfigScope.Client},
   new TerrariaModder.Core.Config.ConfigPropertyMeta{Property=GetType().GetProperty("World"),Scope=TerrariaModder.Core.Config.ConfigScope.Server,Min=1,Max=25},
   new TerrariaModder.Core.Config.ConfigPropertyMeta{Property=GetType().GetProperty("Secret"),Scope=TerrariaModder.Core.Config.ConfigScope.Client}};}
 }
 public static class Mod {public static bool WorldActive=true;public static QualityOfTerrariaConfig Config=new QualityOfTerrariaConfig();}
}
namespace QualityOfTerraria.UI { public static class SettingsSyncUI {internal static void ResetSession(){}internal static void SetEnabled(bool value){}} }
'@
$tests = @'
namespace QualityOfTerraria.Networking {
 public static class SettingsSyncRegression {
  static void Check(bool v,string s){if(!v)throw new Exception("FAIL "+s);Console.WriteLine("PASS "+s);}
  public static void Run(){
   int calls=0;NetworkRuntime.ResetSession();NetworkRuntime.Enqueue(()=>calls++);Check(calls==0,"网络回调不立即改变游戏状态");NetworkRuntime.Update();Check(calls==1,"主线程更新执行回调");
   NetworkRuntime.Enqueue(()=>calls++);NetworkRuntime.ResetSession();NetworkRuntime.Update();Check(calls==1,"退出清空上一会话回调");
   for(int i=0;i<2000;i++)NetworkRuntime.Enqueue(()=>calls++);NetworkRuntime.Update();Check(calls<=65,"每帧处理量有界");NetworkRuntime.ResetSession();
   var valid=new Dictionary<string,object>{{"Personal",false},{"World",25}};
   string reason;Check(SettingsSync.ValidateSnapshot(valid,out reason),"合法个人和世界配置边界允许同步");
   valid["World"]=26;Check(!SettingsSync.ValidateSnapshot(valid,out reason),"拒绝越界且不靠强制截断掩盖错误");valid["World"]=25.0;Check(!SettingsSync.ValidateSnapshot(valid,out reason),"拒绝数值类型混淆");valid["World"]=25;
   valid["Secret"]="exfiltrate";Check(!SettingsSync.ValidateSnapshot(valid,out reason),"不允许同步敏感字符串");valid.Remove("Secret");valid["Unknown"]=true;Check(!SettingsSync.ValidateSnapshot(valid,out reason),"拒绝未知配置键");valid.Remove("Unknown");
   var getPlayerConfig=typeof(SettingsSync).GetMethod("GetPlayerConfig",BindingFlags.NonPublic|BindingFlags.Static);Check(getPlayerConfig!=null,"服务器有逐玩家个人设置入口");
   SettingsSync.Initialize();Check(getPlayerConfig.Invoke(null,new object[]{1})==null,"没有交换个人设置时服务器不假定玩家启用增益");
   string nonce=new string('a',32);Hello(0,nonce);Hello(1,new string('b',32));
   var configA=(QualityOfTerraria.QualityOfTerrariaConfig)getPlayerConfig.Invoke(null,new object[]{0});var configB=(QualityOfTerraria.QualityOfTerrariaConfig)getPlayerConfig.Invoke(null,new object[]{1});Check(configA.Personal&&configB.Personal&&!object.ReferenceEquals(configA,configB),"服务器不同玩家持有独立个人配置");
   var hello=TerrariaModder.Core.Net.NetSync.Sent[0];string room;using(var r=Read(hello.Item4)){r.ReadString();r.ReadString();room=r.ReadString();}
   TerrariaModder.Core.Net.NetSync.Request(1,"qot.settings.personal",Write(w=>{Header(w,new string('b',32),room);w.Write(2);w.Write(1);w.Write("Personal");w.Write((byte)1);w.Write(false);}));NetworkRuntime.Update();configB=(QualityOfTerraria.QualityOfTerrariaConfig)getPlayerConfig.Invoke(null,new object[]{1});Check(configA.Personal&&!configB.Personal,"玩家取消个人功能更新服务器且不污染其他玩家");
   TerrariaModder.Core.Net.NetSync.Request(1,"qot.settings.personal",Write(w=>{Header(w,new string('b',32),room);w.Write(2);w.Write(1);w.Write("World");w.Write((byte)2);w.Write(24);}));NetworkRuntime.Update();Check(QualityOfTerraria.Mod.Config.World==3,"个人快照不能覆盖世界配置");
   string create=Write(w=>{Header(w,nonce,room);w.Write(2);w.Write(2);w.Write("Personal");w.Write((byte)1);w.Write(false);w.Write("World");w.Write((byte)2);w.Write(20);});
   int beforeCreate=TerrariaModder.Core.Net.NetSync.Sent.Count;TerrariaModder.Core.Net.NetSync.Request(0,"qot.settings.create",create);Check(TerrariaModder.Core.Net.NetSync.Sent.Count==beforeCreate,"申请回调只入队");NetworkRuntime.Update();
   var offer=TerrariaModder.Core.Net.NetSync.Sent.FindLast(x=>x.Item3=="qot.settings.offer");Check(offer!=null&&offer.Item1==1,"服务器仅投递已握手玩家");string id;using(var r=Read(offer.Item4)){r.ReadString();r.ReadString();r.ReadString();id=r.ReadString();}
   QualityOfTerraria.Mod.Config.World=7;QualityOfTerraria.Mod.Config.Personal=true;
   string decide=Write(w=>{Header(w,new string('b',32),room);w.Write(id);w.Write(true);});TerrariaModder.Core.Net.NetSync.Request(1,"qot.settings.decision",decide);NetworkRuntime.Update();Check(QualityOfTerraria.Mod.Config.World==7&&TerrariaModder.Core.Net.NetSync.Broadcasts==0,"非管理员同意不修改共享世界");
   var apply=TerrariaModder.Core.Net.NetSync.Sent.FindLast(x=>x.Item3=="qot.settings.apply");Check(apply!=null,"同意后服务器发送固定快照");
   Terraria.Main.netMode=1;typeof(SettingsSync).GetField("_clientNonce",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,new string('b',32));typeof(SettingsSync).GetField("_room",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,room);
   TerrariaModder.Core.Net.NetSync.Response(true,offer.Item3,offer.Item4);NetworkRuntime.Update();SettingsSync.Decide(SettingsSync.Requests[0],true);NetworkRuntime.Update();
   TerrariaModder.Core.Net.NetSync.Response(true,apply.Item3,apply.Item4);NetworkRuntime.Update();Check(!QualityOfTerraria.Mod.Config.Personal&&QualityOfTerraria.Mod.Config.World==7&&QualityOfTerraria.Mod.Config.Saves==1,"个人同意保存原申请快照且保留世界配置");
   Check(SettingsSync.Status.Contains("未应用世界项"),"无世界权限明确反馈部分应用");
   int personalSends=0;string personalPayload=null;Action<int,string,string> observe=(slot,op,payload)=>{if(op=="qot.settings.personal"){personalSends++;personalPayload=payload;}};TerrariaModder.Core.Net.NetSync.OnStorageRequest+=observe;Terraria.Main.myPlayer=1;
   SettingsSync.Update();SettingsSync.Update();Check(personalSends==1,"每帧检测不重复发送未变化的个人配置");QualityOfTerraria.Mod.Config.Personal=true;SettingsSync.Update();Check(personalSends==2,"个人开关变化后立即发送更新");using(var r=Read(personalPayload)){r.ReadString();r.ReadString();r.ReadString();r.ReadInt32();Check(r.ReadInt32()==1&&r.ReadString()=="Personal"&&r.ReadByte()==1&&r.ReadBoolean(),"个人更新包仅包含Client字段");}TerrariaModder.Core.Net.NetSync.OnStorageRequest-=observe;Terraria.Main.myPlayer=0;
   SettingsSync.ResetSession();NetworkRuntime.ResetSession();TerrariaModder.Core.Net.NetSync.Response(true,apply.Item3,apply.Item4);NetworkRuntime.Update();Check(QualityOfTerraria.Mod.Config.Saves==1,"过期会话不能应用重放");
   Terraria.Main.netMode=2;QualityOfTerraria.Mod.Config=new QualityOfTerraria.QualityOfTerrariaConfig();SettingsSync.Initialize();TerrariaModder.Core.Net.NetSync.Sent.Clear();Hello(0,nonce);Hello(1,new string('b',32));room=Room();
   string adminId=Create(nonce,room,19);TerrariaModder.Core.Permissions.PermissionService.Admin=true;
   Decision(adminId,room,true);Check(QualityOfTerraria.Mod.Config.World==19&&QualityOfTerraria.Mod.Config.Saves==1&&TerrariaModder.Core.Net.NetSync.Broadcasts==1,"管理员同意后服务器保存并广播世界项");
   Decision(adminId,room,true);Check(QualityOfTerraria.Mod.Config.Saves==1&&TerrariaModder.Core.Net.NetSync.Broadcasts==1,"重复同意不重复保存或广播");
   string declineId=Create(nonce,room,21);Decision(declineId,room,false);Check(QualityOfTerraria.Mod.Config.World==19&&QualityOfTerraria.Mod.Config.Saves==1,"拒绝不改变任何设置");
   var refused=TerrariaModder.Core.Net.NetSync.Sent.FindLast(x=>x.Item3=="qot.settings.result");using(var r=Read(refused.Item4)){r.ReadString();r.ReadString();r.ReadString();r.ReadString();r.ReadString();Check(r.ReadString()=="玩家"&&r.ReadString()=="已拒绝","拒绝反馈包含具体玩家名");}
   string staleId=Create(nonce,room,22);TerrariaModder.Core.Permissions.PermissionService.Ids[1]="替换身份";Decision(staleId,room,true);Check(QualityOfTerraria.Mod.Config.World==19,"玩家索引复用不能接受旧申请");SettingsSync.Update();
   var disconnected=TerrariaModder.Core.Net.NetSync.Sent.FindLast(x=>x.Item3=="qot.settings.result");using(var r=Read(disconnected.Item4)){for(int i=0;i<6;i++)r.ReadString();Check(r.ReadString().Contains("已断线"),"接收者断线反馈给发起者");}
   Hello(1,new string('b',32));string failedId=Create(nonce,room,23);QualityOfTerraria.Mod.Config.FailSave=true;Decision(failedId,room,true);Check(QualityOfTerraria.Mod.Config.World==19&&TerrariaModder.Core.Net.NetSync.Broadcasts==1,"世界设置保存失败回滚且不广播");QualityOfTerraria.Mod.Config.FailSave=false;
   string sourceId=Create(nonce,room,24);TerrariaModder.Core.Permissions.PermissionService.Ids[0]="发起者已更换";SettingsSync.Update();Check(TerrariaModder.Core.Net.NetSync.Sent.Exists(x=>x.Item3=="qot.settings.expired"&&x.Item1==1),"发起者断线让接收申请失效");
   SettingsSync.Initialize();TerrariaModder.Core.Net.NetSync.Sent.Clear();Hello(0,nonce);Hello(1,new string('b',32));room=Room();
   Create(nonce,room,10);Create(nonce,room,11);int offers=TerrariaModder.Core.Net.NetSync.Sent.FindAll(x=>x.Item3=="qot.settings.offer").Count;Create(nonce,room,12);Check(TerrariaModder.Core.Net.NetSync.Sent.FindAll(x=>x.Item3=="qot.settings.offer").Count==offers,"同一玩家待处理申请数量受限");
   var proposalMap=(System.Collections.IDictionary)typeof(SettingsSync).GetField("Proposals",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);foreach(var p in proposalMap.Values)p.GetType().GetField("Expires",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,DateTime.UtcNow.AddSeconds(-1));SettingsSync.Update();Check(proposalMap.Count==0&&TerrariaModder.Core.Net.NetSync.Sent.Exists(x=>x.Item3=="qot.settings.expired"),"超时申请清理并通知接收者");
   SettingsSync.Unload();Console.WriteLine("设置申请行为检查通过");
  }
  static System.IO.BinaryReader Read(string data){return new System.IO.BinaryReader(new System.IO.MemoryStream(Convert.FromBase64String(data)));}
  static string Write(Action<System.IO.BinaryWriter> f){using(var s=new System.IO.MemoryStream()){using(var w=new System.IO.BinaryWriter(s,System.Text.Encoding.UTF8,true)){f(w);}return Convert.ToBase64String(s.ToArray());}}
  static void Header(System.IO.BinaryWriter w,string nonce,string room){w.Write("1.1.0");w.Write(nonce);w.Write(room);}
  static void Hello(int slot,string nonce){TerrariaModder.Core.Net.NetSync.Request(slot,"qot.settings.hello",Write(w=>{w.Write("1.1.0");w.Write(nonce);w.Write(2);w.Write(1);w.Write("Personal");w.Write((byte)1);w.Write(true);}));NetworkRuntime.Update();}
  static string Room(){var h=TerrariaModder.Core.Net.NetSync.Sent.FindLast(x=>x.Item3=="qot.settings.hello");using(var r=Read(h.Item4)){r.ReadString();r.ReadString();return r.ReadString();}}
  static string Create(string nonce,string room,int world){TerrariaModder.Core.Net.NetSync.Request(0,"qot.settings.create",Write(w=>{Header(w,nonce,room);w.Write(2);w.Write(2);w.Write("Personal");w.Write((byte)1);w.Write(false);w.Write("World");w.Write((byte)2);w.Write(world);}));NetworkRuntime.Update();var offer=TerrariaModder.Core.Net.NetSync.Sent.FindLast(x=>x.Item3=="qot.settings.offer");using(var r=Read(offer.Item4)){r.ReadString();r.ReadString();r.ReadString();return r.ReadString();}}
  static void Decision(string id,string room,bool approve){TerrariaModder.Core.Net.NetSync.Request(1,"qot.settings.decision",Write(w=>{Header(w,new string('b',32),room);w.Write(id);w.Write(approve);}));NetworkRuntime.Update();}
 }
}
'@
$production = (Get-Content (Join-Path $root 'Networking/NetworkRuntime.cs') -Raw) + (Get-Content (Join-Path $root 'Networking/NetSyncPayload.cs') -Raw) + (Get-Content (Join-Path $root 'Networking/SettingsSync.cs') -Raw)
$usings = [regex]::Matches(($production + $fake), '(?m)^using [^;]+;') | ForEach-Object Value | Sort-Object -Unique
$fake = [regex]::Replace($fake, '(?m)^using [^;]+;', '')
$production = [regex]::Replace($production, '(?m)^using [^;]+;', '')
Add-Type -TypeDefinition (($usings -join "`n") + "`n" + $fake + $production + $tests)
[QualityOfTerraria.Networking.SettingsSyncRegression]::Run()
