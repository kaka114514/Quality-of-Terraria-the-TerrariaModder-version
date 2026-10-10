$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$file = Join-Path $root 'Networking/LocalWorldSettings.cs'
if (!(Test-Path $file)) { throw 'FAIL: 单机世界设置使用Server作用域后没有可保存的本地配置' }
$sources = @($file,(Join-Path $root 'StoragePersistence.cs')) | ForEach-Object { [regex]::Replace((Get-Content $_ -Raw), '(?m)^using [^;]+;\s*', '') }
$stub = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TerrariaModder.Core.Config;
namespace TerrariaModder.Core.Config {
 public enum ConfigScope {Client,Server}
 public class ConfigPropertyMeta {
  public string Key;public ConfigScope Scope;public Type PropertyType;
  public object GetValue(ModConfig c){return c.Values[Key];}public void SetValue(ModConfig c,object v){c.Values[Key]=Convert.ChangeType(v,PropertyType);}
 }
 public class ModConfig {
  public string FilePath=Path.Combine(Path.GetTempPath(),"qot-world-"+Guid.NewGuid().ToString("N"),"client.json");public int Version=2;
  public Dictionary<string,object> Values=new Dictionary<string,object>{{"world",1f},{"client",true}};
  public IEnumerable<ConfigPropertyMeta> GetPropertyMetadata(){return new[]{new ConfigPropertyMeta{Key="world",Scope=ConfigScope.Server,PropertyType=typeof(float)},new ConfigPropertyMeta{Key="client",Scope=ConfigScope.Client,PropertyType=typeof(bool)}};}
 }
 public static class ConfigManager {public static ConfigScope Scope=ConfigScope.Client;public static ConfigScope GetCurrentScope(){return Scope;}public static Dictionary<string,object> ParseJsonRaw(string s){return new Dictionary<string,object>();}}
}
namespace QualityOfTerraria {public static class Mod {public static ModConfig Config=new ModConfig();public static void LogError(string s,Exception e){}}}
'@
$tests = @'
namespace QualityOfTerraria.Networking {
 public static class LocalWorldTests {
  static void Check(bool b,string s){if(!b)throw new Exception(s);}
  public static void Run(){
   LocalWorldSettings.Initialize();Mod.Config.Values["world"]=35f;
   string error;Check(LocalWorldSettings.Save(out error),"单机世界配置可持久保存");
   Mod.Config.Values["world"]=1f;Mod.Config.Values["client"]=false;
   LocalWorldSettings.Restore();Check((float)Mod.Config.Values["world"]==35f,"重新进入单机恢复自己的世界配置");
   Check(!(bool)Mod.Config.Values["client"],"世界配置不能覆盖个人设置");
   Mod.Config.Values["world"]=80f;LocalWorldSettings.Restore();Check((float)Mod.Config.Values["world"]==35f,"联机服务器值不污染单机配置");
   Mod.Config.FilePath=Path.Combine(Mod.Config.FilePath,"invalid","config.json");
   LocalWorldSettings.Initialize();Directory.CreateDirectory(Mod.Config.FilePath+".world-settings.dat");
   Check(!LocalWorldSettings.Save(out error)&&!string.IsNullOrEmpty(error),"写盘失败可观察且不报成功");
   ConfigManager.Scope=ConfigScope.Server;LocalWorldSettings.Initialize();
   Check(!LocalWorldSettings.Save(out error),"专服使用Core世界配置，不能错写单机文件");
   Console.WriteLine("PASS: 6 个单机世界设置保存、恢复与作用域场景");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub+($sources -join "`n")+$tests)
[QualityOfTerraria.Networking.LocalWorldTests]::Run()
