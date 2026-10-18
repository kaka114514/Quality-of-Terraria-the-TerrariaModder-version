$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Join-Path $root 'bin/storage-journal-native'
New-Item $fixture -ItemType Directory -Force | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/><Compile Include="../../Networking/StorageTransactionJournal.cs" Link="Journal.cs"/><Compile Include="../../StoragePersistence.cs" Link="StoragePersistence.cs"/><Reference Include="Terraria"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/Terraria.exe</HintPath></Reference><Reference Include="0Harmony"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/0Harmony.dll</HintPath></Reference></ItemGroup></Project>
'@ | Set-Content (Join-Path $fixture 'Journal.Native.csproj')
@'
using System;using System.IO;using System.Reflection;using HarmonyLib;using QualityOfTerraria.Networking;
namespace QualityOfTerraria {public static class Mod {public static void LogError(string s,Exception e){}}}
public static class NativeJournal {
 public static int Main(){string game="D:/App/Game/Steam/steamapps/common/Terraria";AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>{string name=new AssemblyName(args.Name).Name;foreach(string path in new[]{Path.Combine(game,name+".dll"),Path.Combine(game,name+".exe"),Path.Combine(game,"TerrariaModder/core/deps",name+".dll"),"E:/Work/泰拉瑞亚更好的体验移植/lib/"+name+".dll"})if(File.Exists(path))return Assembly.LoadFrom(path);return null;};return Run();}
 static int Run(){try{var h=new Harmony("qot.storage-journal.native");var methods=h.CreateClassProcessor(typeof(StorageTransactionJournal.PendingSavePatch)).Patch();if(methods.Count!=1)throw new Exception("角色保存入口不是唯一挂接");Console.WriteLine("PASS: 真实1.4.5.8 x86 Player.SavePlayer Prefix/Postfix挂接（未启动游戏）");h.UnpatchAll(h.Id);return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
'@ | Set-Content (Join-Path $fixture 'Harness.cs')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'Journal.Native.csproj') -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'FAIL: 事务日志无法绑定原版保存API' }
& (Join-Path $fixture 'bin/Release/net48/Journal.Native.exe')
if ($LASTEXITCODE -ne 0) { throw 'FAIL: 事务日志真实x86保存Hook挂接失败' }
