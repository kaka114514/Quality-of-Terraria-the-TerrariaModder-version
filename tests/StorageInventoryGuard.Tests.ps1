$ErrorActionPreference = 'Stop'
$path = Join-Path (Split-Path $PSScriptRoot) 'Patches/StorageInventoryGuard.cs'
if (!(Test-Path $path)) { throw 'FAIL: 等待服务器期间仍可改变库存，缺少并发物品操作保护' }
$source = Get-Content $path -Raw
$stubs = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;
using QualityOfTerraria.Networking;
using TerrariaModder.Core.UI;
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch:Attribute { public HarmonyPatch(){} public HarmonyPatch(Type t,string s,params Type[] args){} }
 public static class AccessTools { public static MethodInfo Method(Type t,string s) { return t.GetMethod(s); } public static MethodInfo PropertyGetter(Type t,string s) { return t.GetProperty(s).GetGetMethod(); } }
}
namespace Terraria {
 public class Item {} public class GetItemSettings {}
 public class Player { public bool dead,active=true; public int respawnTimer; public int Drops,Coins; public bool HasLockedInventory(){return false;} public Item GetItem(Item item,GetItemSettings settings){return item;} public void DropItems(bool b){Drops++;} public long DropCoins(){Coins++;return 10;} }
 public static class Main { public static int netMode=1; public static Player LocalPlayer=new Player(); }
}
namespace Terraria.UI { public class ItemSlot { } }
namespace Terraria.GameContent { public class CraftingRequests {} }
namespace TerrariaModder.Core.UI { public static class UIRenderer { public static bool MouseLeftClick {get{return true;}} public static bool MouseRightClick {get{return true;}} } }
namespace QualityOfTerraria.Networking { public static class RemoteStorageOperations { public static bool Busy; } public static class ToolNetwork { public static bool Busy; } }
namespace QualityOfTerraria.Features { public class Autofisher {} }
namespace QualityOfTerraria { public class WandsAndTools {} }
'@
$tests = @'
namespace QualityOfTerraria.Patches {
 public static class GuardTests {
  static void Check(bool b,string s){if(!b)throw new Exception(s);}
  public static void Run() {
   var p=Terraria.Main.LocalPlayer;
   RemoteStorageOperations.Busy=true;
   Check(StorageInventoryGuard.Locked,"远程存取等待必须冻结物品操作");
   Check(!StorageInventoryGuard.InventoryInputPatch.Prefix(),"左右键输入必须阻止");
   bool locked=false;StorageInventoryGuard.LockedInventoryPatch.Postfix(p,ref locked);Check(locked,"原版库存锁状态");
   var item=new Item(); Item remaining=null;
   Check(!StorageInventoryGuard.PickupPatch.Prefix(p,item,ref remaining)&&ReferenceEquals(item,remaining),"拾取等待时保留原物品，不吞物品");
   Check(!StorageInventoryGuard.PlayerActionsPatch.Prefix(p),"等待时禁止喝药和丢弃");
   p.dead=true;p.respawnTimer=0;
   Check(!StorageInventoryGuard.DeathItemsPatch.Prefix(p,false),"死亡掉落延迟到库存确认");
   long coins=99;Check(!StorageInventoryGuard.DeathCoinsPatch.Prefix(p,ref coins)&&coins==0,"金币掉落等待确认");
   StorageInventoryGuard.Update();Check(p.respawnTimer>=2&&p.Drops==0,"有库存等待时不能先复活");
   RemoteStorageOperations.Busy=false;StorageInventoryGuard.Update();Check(p.Drops==1&&p.Coins==1,"确认后每项死亡掉落仅执行一次");
   StorageInventoryGuard.Update();Check(p.Drops==1&&p.Coins==1,"不可重复死亡掉落");
   ToolNetwork.Busy=true;Check(StorageInventoryGuard.Locked,"建造等待也冻结库存");
   Terraria.Main.netMode=2;Check(!StorageInventoryGuard.Locked,"服务器执行不被客户端锁拦截");
   Console.WriteLine("PASS: 12 个等待确认与死亡掉落保护场景");
  }
 }
}
'@
$source = [regex]::Replace($source, '(?m)^using [^;]+;\s*', '')
Add-Type -TypeDefinition ($stubs+$source+$tests)
[QualityOfTerraria.Patches.GuardTests]::Run()
