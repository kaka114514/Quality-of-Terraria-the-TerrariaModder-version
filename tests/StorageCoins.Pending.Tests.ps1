$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Get-Content (Join-Path $root 'Patches/PlayerQoLPatches.cs') -Raw
$start = $source.IndexOf('public static class DoCoinsPatch')
$body = $source.IndexOf('{', $start)
$depth = 1; $end = $body + 1
while ($depth -gt 0) { if ($source[$end] -eq '{') { $depth++ }; if ($source[$end] -eq '}') { $depth-- }; $end++ }
$coins = $source.Substring($start, $end - $start)
$guard = Get-Content (Join-Path $root 'Patches/StorageInventoryGuard.cs') -Raw
$locked = [regex]::Match($guard, 'internal static bool Locked =>[^;]+;').Value
$stub = @'
using System;
using Terraria;
using QualityOfTerraria.Networking;
namespace Terraria {
 public class Item {public int type,stack,maxStack=9999;public byte prefix;public bool IsAir=>type==0||stack<=0;public Item Clone()=>(Item)MemberwiseClone();public void TurnToAir(){type=stack=prefix=0;}}
 public class Chest {public Item[] item=new Item[40];}
 public class Player {public int whoAmI;public Item[] inventory=new Item[59];public Chest bank=new Chest();}
 public static class Main {public static int netMode=1,myPlayer;}
}
namespace QualityOfTerraria {public class Config {public bool AutoSaveCoins=true;}public static class Mod {public static Config Config=new Config();}}
namespace QualityOfTerraria.Networking {public static class RemoteStorageOperations {public static bool Busy;}public static class ToolNetwork {public static bool Busy;}}
namespace QualityOfTerraria.Patches {
 internal static class StorageInventoryGuard { __LOCKED__ }
 __COINS__
 public static class PendingCoinsTests {
  static int checks;static void Check(bool b,string s){checks++;if(!b)throw new Exception(s);}
  static Player P(){var p=new Player();p.inventory[50]=new Item{type=71,stack=10};return p;}
  public static void Run(){
   var p=P();RemoteStorageOperations.Busy=true;DoCoinsPatch.Postfix(p,0);
   Check(p.inventory[50].stack==10&&p.bank.item[0]==null,"FAIL: 存储等待时自动存钱改动了库存和银行，跨断线恢复Before会复制钱币");
   RemoteStorageOperations.Busy=false;ToolNetwork.Busy=true;DoCoinsPatch.Postfix(p,0);
   Check(p.inventory[50].stack==10&&p.bank.item[0]==null,"工具材料等待同样不得移动钱币到未记录的银行");
   ToolNetwork.Busy=false;DoCoinsPatch.Postfix(p,0);
   Check(p.inventory[50].IsAir&&p.bank.item[0].stack==10,"确认后正常自动存钱恢复，钱币仅移动一次");
   var again=P();Main.netMode=0;RemoteStorageOperations.Busy=true;DoCoinsPatch.Postfix(again,0);
   Check(again.inventory[50].stack==10&&again.bank.item[0]==null,"返回单机仍持未确认日志时不可绕过库存保护");
   Console.WriteLine("PASS: "+checks+" 个自动存钱等待保护与确认后恢复场景");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub.Replace('__LOCKED__', $locked).Replace('__COINS__', $coins))
[QualityOfTerraria.Patches.PendingCoinsTests]::Run()
