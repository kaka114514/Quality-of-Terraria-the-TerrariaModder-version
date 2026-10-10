$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '../UI/SettingsSyncUI.cs'
if (!(Test-Path $path)) { throw 'FAIL 本模组配置区尚未包含同步申请按钮和滚动列表' }
$fake = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
namespace HarmonyLib {
 public class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string n){}}
 public class CodeInstruction {public OpCode opcode;public object operand;public CodeInstruction(OpCode code,object value){opcode=code;operand=value;}}
 public static class AccessTools {public static MethodInfo Method(Type t,string n){return t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);} public static FieldInfo Field(Type t,string n){return t.GetField(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);}}
}
namespace Terraria {public class Player {public bool mouseInterface;}public static class Main {public static Player[] player={new Player()};public static int myPlayer;}}
namespace TerrariaModder.Core.Config {public class ConfigPropertyMeta{}public class ModConfig{}}
namespace TerrariaModder.Core {
 public class Manifest {public string Id;}
 public class Context {public TerrariaModder.Core.Config.ModConfig Config;}
 public class ModInfo {public Manifest Manifest;public Context Context;}
}
namespace TerrariaModder.Core.Permissions {public static class PermissionService {public static void OnClientConnect(){}public static void OnClientDisconnect(){}}}
namespace TerrariaModder.Core.UI {
 public static class ModMenu {public static bool IsVisible=true;public static bool _blockInput;private static void DrawConfigTab(){}private static void DrawConfigProperty(){}}
 public static class UIRenderer {
  public static int MouseX=-100,MouseY=-100,ScrollWheel;public static bool MouseLeftClick;public static List<string> Texts=new List<string>();public static int Clicks;
  public static void DrawRect(int x,int y,int w,int h,byte r,byte g,byte b,byte a){}
  public static void ConsumeClick(){MouseLeftClick=false;Clicks++;}public static void ConsumeScroll(){ScrollWheel=0;}
 }
}
namespace QualityOfTerraria {
 public static class Mod {public static bool WorldActive=true;public static TerrariaModder.Core.Config.ModConfig Config=new TerrariaModder.Core.Config.ModConfig();}
}
namespace QualityOfTerraria.Networking {
 public static class NetworkRuntime {internal static void Enqueue(Action action){action();}}
 public static class SettingsSync {
  internal class RequestRow {internal string Id,Name,State;internal bool Incoming,Pending;}
  internal static string Status="已连接";internal static List<RequestRow> Requests=new List<RequestRow>();internal static int Decisions,Sent;
  internal static void RequestSync(){Sent++;}internal static void Decide(RequestRow row,bool approve){row.Pending=false;Decisions++;}internal static void QueueConnectionChanged(int slot){}
 }
}
namespace QualityOfTerraria.UI {public static class PanelLayout {public static void DrawFittedText(string s,int x,int y,int w,byte r,byte g,byte b){TerrariaModder.Core.UI.UIRenderer.Texts.Add(s);}}}
'@
$tests = @'
namespace QualityOfTerraria.UI {
 public static class SettingsSyncUIRegression {
  static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);}
  public static void Run(){
   var rows=new List<ValueTuple<string,object,TerrariaModder.Core.ModInfo>>();
   var other=new TerrariaModder.Core.ModInfo{Manifest=new TerrariaModder.Core.Manifest{Id="other"},Context=new TerrariaModder.Core.Context{Config=new TerrariaModder.Core.Config.ModConfig()}};
   var own=new TerrariaModder.Core.ModInfo{Manifest=new TerrariaModder.Core.Manifest{Id="quality-of-terraria"},Context=new TerrariaModder.Core.Context{Config=Mod.Config}};
   SettingsSyncUI.AddConfigRow(rows,ValueTuple.Create("header",(object)"Other",other));Check(rows.Count==1,"其他模组配置区没有注入申请行");
   SettingsSyncUI.AddConfigRow(rows,ValueTuple.Create("header",(object)"QoT",own));Check(rows.Count==4,"没有申请时只显示同步按钮与状态，不预留空行");
   var marker=(TerrariaModder.Core.Config.ConfigPropertyMeta)rows[2].Item2;
   Check(SettingsSyncUI.DrawConfigRow(0,0,480,marker,other.Context.Config,"other"),"其他mod原绘制继续执行");
   TerrariaModder.Core.UI.UIRenderer.MouseX=5;TerrariaModder.Core.UI.UIRenderer.MouseY=5;TerrariaModder.Core.UI.UIRenderer.MouseLeftClick=true;
   Check(!SettingsSyncUI.DrawConfigRow(0,0,480,marker,Mod.Config,"quality-of-terraria"),"本mod按钮替代仅自己的占位行");
   Check(TerrariaModder.Core.UI.UIRenderer.Texts.Contains("将设置同步给房间内其他玩家"),"按钮显示用户指定名称");Check(Networking.SettingsSync.Sent==1&&Terraria.Main.player[0].mouseInterface&&TerrariaModder.Core.UI.UIRenderer.Clicks==1,"点击发申请并消费游戏输入");
   for(int i=0;i<12;i++)Networking.SettingsSync.Requests.Add(new Networking.SettingsSync.RequestRow{Id="id"+i,Name="玩家"+i,State="等待同意",Incoming=true,Pending=true});
   rows.Clear();SettingsSyncUI.AddConfigRow(rows,ValueTuple.Create("header",(object)"QoT",own));Check(rows.Count==7,"长列表最多显示四个申请行");
   var requestMarker=(TerrariaModder.Core.Config.ConfigPropertyMeta)rows[3].Item2;
   TerrariaModder.Core.UI.UIRenderer.MouseLeftClick=false;TerrariaModder.Core.UI.UIRenderer.ScrollWheel=-120;SettingsSyncUI.DrawConfigRow(0,0,480,requestMarker,Mod.Config,"quality-of-terraria");
   Check(TerrariaModder.Core.UI.UIRenderer.Texts.Exists(t=>t.Contains("玩家1")),"列表溢出滚动切换申请");Check(TerrariaModder.Core.UI.UIRenderer.ScrollWheel==0,"列表滚动被消费");
   TerrariaModder.Core.UI.UIRenderer.MouseX=390;TerrariaModder.Core.UI.UIRenderer.MouseY=5;TerrariaModder.Core.UI.UIRenderer.MouseLeftClick=true;SettingsSyncUI.DrawConfigRow(0,0,480,requestMarker,Mod.Config,"quality-of-terraria");Check(Networking.SettingsSync.Decisions==1,"当前滚动行同意对应玩家");
   TerrariaModder.Core.UI.ModMenu._blockInput=true;TerrariaModder.Core.UI.UIRenderer.MouseLeftClick=true;SettingsSyncUI.DrawConfigRow(0,0,480,marker,Mod.Config,"quality-of-terraria");Check(Networking.SettingsSync.Sent==1,"Core输入编辑阻塞期间不抢点击");
   SettingsSyncUI.SetEnabled(false);var detached=new List<ValueTuple<string,object,TerrariaModder.Core.ModInfo>>();SettingsSyncUI.AddConfigRow(detached,ValueTuple.Create("header",(object)"QoT",own));Check(detached.Count==1,"卸载后不再插入同步申请区");
   Console.WriteLine("设置申请UI行为检查通过");
  }
 }
}
'@
$production = Get-Content $path -Raw
$usings = [regex]::Matches(($production+$fake), '(?m)^using [^;]+;') | ForEach-Object Value | Sort-Object -Unique
$production = [regex]::Replace($production, '(?m)^using [^;]+;', '')
$fake = [regex]::Replace($fake, '(?m)^using [^;]+;', '')
Add-Type -TypeDefinition (($usings -join "`n")+"`n"+$fake+$production+$tests)
[QualityOfTerraria.UI.SettingsSyncUIRegression]::Run()
