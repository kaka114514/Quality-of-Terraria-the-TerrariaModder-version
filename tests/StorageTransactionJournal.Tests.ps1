$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$journal = Join-Path $root 'Networking/StorageTransactionJournal.cs'
if (!(Test-Path $journal)) { throw 'FAIL: 没有跨断线事务日志，确认前退出会丢弃未确认库存' }
$sources = @($journal, (Join-Path $root 'StoragePersistence.cs'), (Join-Path $root 'Networking/StorageTransfers.cs')) | ForEach-Object { [regex]::Replace((Get-Content $_ -Raw), '(?m)^using [^;]+;\s*', '') }
$stub = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Terraria;
using Terraria.IO;
using Terraria.ID;
using Terraria.Utilities;
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)]public class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string name,params Type[] args){}}
}
namespace Terraria {
 public class Item {public int type,stack,maxStack=99;public byte prefix;public bool favorited;public bool IsAir=>type==0||stack<=0;public Item Clone()=>(Item)MemberwiseClone();public void TurnToAir(){type=stack=prefix=0;}public void SetDefaults(int id){type=id;stack=1;}public bool Prefix(byte p){prefix=p;return true;}}
 public class Player {
  public Item[] inventory=Enumerable.Range(0,59).Select(i=>new Item()).ToArray();
  public static int Saves;public static bool FailSave,StaleSave;
  public static void SavePlayer(PlayerFileData file,bool map,bool skip) {Saves++;if(FailSave)throw new IOException("injected save failure");if(!StaleSave)Saved[file.Path]=new Player{inventory=file.Player.inventory.Select(i=>i.Clone()).ToArray()};}
  public static Dictionary<string,Player> Saved=new Dictionary<string,Player>();
  public static PlayerFileData LoadPlayer(string path,bool cloud){return new PlayerFileData{Path=path,Player=Saved.ContainsKey(path)?Saved[path]:new Player()};}
 }
 public static class Main {public static string SavePath=Path.Combine(Path.GetTempPath(),"qot-journal-"+Guid.NewGuid().ToString("N"));public static int netMode=1;public static bool ServerSideCharacter;public static Player LocalPlayer=new Player();public static Item mouseItem=new Item();public static PlayerFileData ActivePlayerFileData=new PlayerFileData{Path="player-a",Player=LocalPlayer};}
}
namespace Terraria.IO {public class PlayerFileData {public string Path;public bool IsCloudSave,ServerSideCharacter;public Player Player;}}
namespace Terraria.ID {public static class ItemID {public const int Count=1000;}}
namespace Terraria.Utilities {public static class FileUtilities {public static byte[] ReadAllBytes(string path,bool cloud){if(!Player.Saved.ContainsKey(path))throw new IOException("no character save");using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){foreach(Item item in Player.Saved[path].inventory.Take(58)){writer.Write(item.type);writer.Write(item.stack);writer.Write(item.prefix);writer.Write(item.favorited);}}return stream.ToArray();}}}}
namespace QualityOfTerraria {public static class Mod {public static void LogError(string s,Exception e){}}}
'@
$tests = @'
namespace QualityOfTerraria.Networking {
 public static class JournalTests {
  static int checks;static void Check(bool b,string s){checks++;if(!b)throw new Exception(s);}
  public static void Run(){
   string world="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",id="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.1",error;
   StorageTransactionJournal.InitializeClient("character-a");
   Check(!StorageTransactionJournal.ClientLocked&&StorageTransactionJournal.Pending==null,"新角色没有库存锁");
   Check(StorageTransactionJournal.BeginClient(world,id,"AQ==","Ag=="),"发送前原子写事务日志");
   Check(!StorageTransactionJournal.BeginClient(world,id+"x","AQ==","Ag=="),"不能覆盖未确认事务");
   StorageTransactionJournal.InitializeClient("character-a");
   Check(StorageTransactionJournal.Pending.Id==id&&StorageTransactionJournal.Pending.Before=="AQ=="&&StorageTransactionJournal.Pending.Request=="Ag==","重新初始化读取原事务库存与请求");
   Check(!StorageTransactionJournal.PendingSavePatch.Prefix(Main.ActivePlayerFileData),"未确认角色不能保存旧库存");
   Check(StorageTransactionJournal.PendingSavePatch.Prefix(new PlayerFileData{Path="other-character",Player=Main.LocalPlayer}),"切到另一角色存档不得阻断或污染另一个角色日志");
   StorageTransactionJournal.InitializeServer(world);StorageTransactionJournal.RecordServer("guid-a",id,"response");
   StorageTransactionJournal.InitializeServer(world);
   Check(StorageTransactionJournal.TryGetServer("guid-a",id,out error)&&error=="response","服务器客户端重连保留同身份结果");
   Check(!StorageTransactionJournal.TryGetServer("guid-b",id,out error),"他人身份不能读取事务结果");
   StorageTransactionJournal.RegisterServerToken("guid-a","dddddddddddddddddddddddddddddddd");
   Check(!StorageTransactionJournal.TryGetServer("guid-a","dddddddddddddddddddddddddddddddd.2",out error),"仍活动的token不能提前断定旧请求未执行");
   StorageTransactionJournal.RegisterServerToken("guid-a","eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");
   Check(StorageTransactionJournal.TryGetServer("guid-a","dddddddddddddddddddddddddddddddd.2",out error)&&error==null,"重连使旧已登记token失效后可确定没有执行结果");
   Check(!StorageTransactionJournal.TryGetServer("guid-a","unknown-token.2",out error),"未登记旧token不能误判未执行");
   StorageTransactionJournal.InitializeServer("cccccccccccccccccccccccccccccccc");
   Check(!StorageTransactionJournal.TryGetServer("guid-a",id,out error),"其他世界不能恢复事务结果");
   StorageTransactionJournal.InitializeServer(world);
   Check(StorageTransactionJournal.TryGetServer("guid-a",id,out error),"切回原世界保留事务結果");
   StorageTransactionJournal.EndServerSession();StorageTransactionJournal.InitializeServer(world);
   Check(!StorageTransactionJournal.TryGetServer("guid-a",id,out error),"世界重新读取旧存档后不能沿用之前内存提交结果");
   Main.LocalPlayer.inventory[0]=new Item{type=10,stack=3,prefix=4};
   Player.FailSave=true;Check(!StorageTransactionJournal.CompleteClient()&&StorageTransactionJournal.ClientLocked,"角色写盘失败保留journal和锁");Player.FailSave=false;
   Player.StaleSave=true;Check(!StorageTransactionJournal.CompleteClient()&&StorageTransactionJournal.Pending!=null,"云盘静默写入失败读回旧库存不得清journal");Player.StaleSave=false;
   Check(StorageTransactionJournal.CompleteClient()&&StorageTransactionJournal.Pending==null&&!StorageTransactionJournal.ClientLocked,"角色库存读回核验后清journal");
   Check(Player.Saved["player-a"].inventory[0].stack==3,"角色库存先已落盘");
   StorageTransactionJournal.InitializeClient("character-a");Check(StorageTransactionJournal.Pending==null,"已结算journal不会恢复旧操作");
   Check(StorageTransactionJournal.PendingSavePatch.Prefix(Main.ActivePlayerFileData),"确认后正常角色保存恢复");
   Check(StorageTransactionJournal.BeginClient(world,id+"2","AQ==","Ag=="),"再开始鼠标存入事务");
   Main.mouseItem=new Item{type=22,stack=7,prefix=5};
   for(int i=0;i<50;i++)Main.LocalPlayer.inventory[i]=new Item{type=10,stack=99};
   Check(StorageTransactionJournal.CompleteClient()&&!StorageTransactionJournal.ClientLocked,"满背包鼠标余量也可完成确认与解锁");
   Main.mouseItem=new Item();StorageTransactionJournal.InitializeClient("character-a");
   Check(Main.mouseItem.type==22&&Main.mouseItem.stack==7&&Main.mouseItem.prefix==5,"重载恢复完整鼠标余量与前缀");
   Main.LocalPlayer.inventory[0]=Main.mouseItem;Main.mouseItem=new Item();
   Player.SavePlayer(Main.ActivePlayerFileData,false,false);StorageTransactionJournal.PendingSavePatch.Postfix(Main.ActivePlayerFileData);
   StorageTransactionJournal.InitializeClient("character-a");Check(Main.mouseItem.IsAir&&Main.LocalPlayer.inventory[0].type==22,"鼠标存入已保存库存后不能再次恢复旧cursor");
   Check(StorageTransactionJournal.BeginClient(world,id+"3","AQ==","Ag=="),"可开始无需立即整份保存的存取事务");
   Player.Saves=0;Check(StorageTransactionJournal.CompleteClient(Main.LocalPlayer.inventory,Main.mouseItem)&&Player.Saves==0,"存取确认不在点击响应中同步调用原版整份角色保存");
   Check(StorageTransactionJournal.Pending!=null&&StorageTransactionJournal.Pending.Confirmed,"快速确认保留已确认库存快照");
   Player.Saved["player-a"].inventory[0].stack=6;
   StorageTransactionJournal.InitializeClient("character-a");Check(!StorageTransactionJournal.ClientLocked,"已确认事务可恢复库存快照而不锁定");
    string path=Path.Combine(Main.SavePath,"TerrariaModder","player-data",StoragePersistence.Identity("character-b")+".storage-transaction.dat");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"broken");File.WriteAllText(path+".bak","broken");
    StorageTransactionJournal.InitializeClient("character-b");
    // 主记录与备份都损坏时必须保留库存锁，不能用空记录覆盖未知事务。
    Check(StorageTransactionJournal.ClientLocked&&File.Exists(path+".bak"),"主记录和备份损坏时保留库存锁与备份");
    Check(!StorageTransactionJournal.BeginClient(world,id,"AQ==","Ag=="),"损坏记录不能直接开始新事务");
   Console.WriteLine("PASS: "+checks+" 个事务持久化、跨断线身份隔离、保存核验与坏档保护场景");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub+($sources -join "`n")+$tests)
[QualityOfTerraria.Networking.JournalTests]::Run()
