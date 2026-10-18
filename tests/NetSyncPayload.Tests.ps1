$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$h=@'
using System;
using System.Collections.Generic;
using System.Linq;
using QualityOfTerraria.Networking;
using TerrariaModder.Core.Net;
namespace TerrariaModder.Core.Net {
 public static class NetSync {
  public static readonly List<string> Requests=new List<string>();
  public static readonly List<string> Responses=new List<string>();
  public static void SendStorageRequest(string op,string p){Requests.Add(op+"\n"+p);}
  public static void SendStorageResponseTo(int c,bool ok,string op,string p){Responses.Add(c+"\n"+ok+"\n"+op+"\n"+p);}
 }
}
'@
$proj=@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/><Compile Include="$root/Networking/NetSyncPayload.cs"/><Compile Include="$root/bin/net-sync-payload/NetSyncPayload.Harness.cs"/></ItemGroup></Project>
"@
$dir=Join-Path $root 'bin/net-sync-payload';New-Item -ItemType Directory -Force $dir|Out-Null
Set-Content (Join-Path $dir 'NetSyncPayload.csproj') $proj
Set-Content (Join-Path $dir 'NetSyncPayload.Harness.cs') $h
Add-Content (Join-Path $dir 'NetSyncPayload.Harness.cs') @'
class Harness {
 static int checks;
 static void Check(bool ok,string s){checks++;if(!ok)throw new Exception(s);}
 static void Main(){
  string value=new string('A',900)+"==";
  NetSyncPayload.SendRequest("qot.storage",value);
  Check(NetSync.Requests.Count>1,"长请求已分片");
  string full=null; foreach(string wire in NetSync.Requests){string[] p=wire.Split(new[]{'\n'},2);string x;bool ready=NetSyncPayload.TryAcceptRequest(0,"qot.storage",p[1],out x);if(ready)full=x;else Check(p[1].StartsWith("~qot1|"),"中间分片格式正确");}
  Check(full==value,"请求重组保持原文");
  NetSync.Requests.Clear(); NetSyncPayload.SendResponseTo(2,true,"qot.storage",value);
  full=null; foreach(string wire in NetSync.Responses){string[] p=wire.Split(new[]{'\n'},4);string x; bool ready=NetSyncPayload.TryAcceptResponse("qot.storage",p[3],out x);if(ready)full=x;}
  Check(full==value,"响应重组保持原文");
  string shortValue="abc";NetSync.Requests.Clear();NetSyncPayload.SendRequest("qot.short",shortValue);Check(NetSync.Requests.Single().EndsWith("\nabc"),"短消息保持原协议");
  string ignored;Check(!NetSyncPayload.TryAcceptRequest(0,"qot.storage","~qot1|badid|0|1|"+new string('A',181),out ignored),"拒绝超长伪造分片");
  NetSyncPayload.Reset();Console.WriteLine("NetSync载荷分片检查: "+checks+" 项通过");
 }
}
'@
dotnet run --project (Join-Path $dir 'NetSyncPayload.csproj') -c Release --no-restore
