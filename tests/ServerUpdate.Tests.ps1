$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Mod.cs') -Raw -Encoding UTF8
$signature = 'internal static void UpdateServerSession()'
$start = $source.IndexOf($signature)
if ($start -lt 0) { throw "找不到专服帧更新入口：$signature" }
$open = $source.IndexOf('{', $start); $end = $open + 1; $depth = 1
while ($depth -gt 0) { if ($source[$end] -eq '{') { $depth++ }; if ($source[$end] -eq '}') { $depth-- }; $end++ }
$method = $source.Substring($start, $end - $start).Replace('internal static void', 'public static void')
$harness = @'
using System;
using Terraria;
namespace Terraria { public static class Main { public static int netMode; public static object ActiveWorldFileData; } public static class WorldGen {public static bool isGeneratingOrLoadingWorld;} }
namespace QualityOfTerraria {
 public class Mod {
  public static bool WorldActive;
  private static Mod _instance;
  public int Loads,Unloads,Updates;
  public static void SetInstance(Mod value){_instance=value;}
  public void OnWorldLoad(){Loads++;WorldActive=true;}
  public void EndSession(){Unloads++;WorldActive=false;}
  public void UpdateNetworkSession(){Updates++;}
  __METHOD__
 }
 public static class ServerUpdateTests {
  static int checks;
  static void Check(bool value,string message){checks++;if(!value)throw new Exception("FAIL "+message);Console.WriteLine("PASS "+message);}
  public static void Run(){
   var server=new Mod();Mod.SetInstance(null);Terraria.Main.netMode=2;Terraria.Main.ActiveWorldFileData=new object();Mod.UpdateServerSession();Check(server.Loads==0&&server.Updates==0,"运行模块未建立时不访问游戏世界");
   Mod.SetInstance(server);Terraria.Main.netMode=1;Mod.UpdateServerSession();Check(server.Loads==0&&server.Updates==0,"客户端不执行专服更新");
   Terraria.Main.netMode=2;Terraria.Main.ActiveWorldFileData=null;Mod.UpdateServerSession();Check(server.Loads==0&&server.Updates==0,"没有服务器世界时不处理请求");
   Terraria.Main.ActiveWorldFileData=new object();Terraria.WorldGen.isGeneratingOrLoadingWorld=true;Mod.UpdateServerSession();Check(server.Loads==0&&server.Updates==0,"世界加载期间不处理请求");
   Terraria.WorldGen.isGeneratingOrLoadingWorld=false;Mod.UpdateServerSession();Check(server.Loads==1&&server.Updates==1,"世界就绪后初始化会话并处理首帧请求");
   Mod.UpdateServerSession();Check(server.Loads==1&&server.Updates==2,"每个专服主线程更新都处理网络队列且不重复初始化");
   Terraria.WorldGen.isGeneratingOrLoadingWorld=true;Mod.UpdateServerSession();Check(server.Unloads==1&&!Mod.WorldActive&&server.Updates==2,"切换世界结束会话并停止请求处理");
   Terraria.WorldGen.isGeneratingOrLoadingWorld=false;Mod.UpdateServerSession();Check(server.Loads==2&&server.Updates==3,"新世界重新建立网络会话");
   Terraria.Main.ActiveWorldFileData=null;Mod.UpdateServerSession();Check(server.Unloads==2&&!Mod.WorldActive&&server.Updates==3,"世界数据退出后结束会话并停止网络处理");
   Console.WriteLine("专服更新生命周期 "+checks+" 项通过");
  }
 }
}
'@
Add-Type -TypeDefinition $harness.Replace('__METHOD__', $method)
[QualityOfTerraria.ServerUpdateTests]::Run()
