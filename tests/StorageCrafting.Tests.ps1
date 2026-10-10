$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Get-Content (Join-Path $root 'Patches/PortableCraftingPatches.cs') -Raw
$start = $source.IndexOf('public static class MarkedChestCraftingPatch')
$end = $source.IndexOf('[HarmonyPatch(typeof(Terraria.GameContent.CraftingRequests)', $start)
$adapter = $source.Substring($start, $end - $start)
$harness = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using HarmonyLib;
using QualityOfTerraria.UI;
namespace HarmonyLib { public static class AccessTools { public static FieldInfo Field(Type t,string n) { return t.GetField(n,BindingFlags.NonPublic|BindingFlags.Static); } public static MethodInfo Method(Type t,string n) { return t.GetMethod(n,BindingFlags.NonPublic|BindingFlags.Static); } } }
namespace Terraria {
 public class Item { public int stack; }
 public class Chest {
  public Item[] item; public int index, maxItems; public bool bankChest;
  public static Chest CreateBank(int i) { var c=new Chest { index=i, bankChest=true, maxItems=40,item=new Item[40] }; for(int j=0;j<40;j++) c.item[j]=new Item(); return c; }
 }
 public class Player { public int whoAmI; public Chest bank=Chest.CreateBank(-2), bank2=Chest.CreateBank(-3), bank3=Chest.CreateBank(-4), bank4=Chest.CreateBank(-5); }
 public static class Main { public static int myPlayer; }
 public static class Recipe {
  private static List<Chest> _recipeChests=new List<Chest>(); public static int Owned;
  private static void CollectItems(Item[] items,int count) { for(int i=0;i<count;i++) Owned+=items[i].stack; }
  public static List<Chest> Chests { get { return _recipeChests; } }
 }
}
namespace QualityOfTerraria.UI {
 public static class UIManager { public static bool CanRender=true; }
 public static class SuperVaultUI { public static Item[] VaultItems; public static bool CanStoreItems=true; public static void EnsureLoadedForCurrentPlayer() {} }
 public static class ExtremeStorageUI { public static List<Chest> Marked=new List<Chest>(); public static IEnumerable<Chest> GetMarkedReadableChests() { return Marked; } }
}
namespace QualityOfTerraria.Patches {
 __ADAPTER__
 public static class CraftingAdapterBehavior {
  static void Check(bool v,string m) { if(!v) throw new Exception(m); }
  public static void Run() {
   var player=new Player(); player.bank.item[0].stack=2; player.bank2.item[0].stack=3; player.bank3.item[0].stack=4; player.bank4.item[0].stack=5;
   SuperVaultUI.VaultItems=new Item[100]; for(int i=0;i<100;i++) SuperVaultUI.VaultItems[i]=new Item(); SuperVaultUI.VaultItems[0].stack=7;
   var marked=Chest.CreateBank(3); marked.bankChest=false; marked.item[0].stack=9;
   ExtremeStorageUI.Marked.Add(marked);
   // 原版已经读取打开的箱子时，额外入口不能重复计算其快照。
   Recipe.Chests.Add(marked); Recipe.Owned=9;
   var snapshot=Chest.CreateBank(3); snapshot.bankChest=false; snapshot.item[0].stack=9; ExtremeStorageUI.Marked.Add(snapshot);
   MarkedChestCraftingPatch.Postfix(player);
   Check(Recipe.Owned==30, "四银行、大背包和标记箱只统计一次");
   var vault=Recipe.Chests.Find(c=>c.index==-6); Check(vault.maxItems==100 && Object.ReferenceEquals(vault.item,SuperVaultUI.VaultItems), "制作材料保持实际仓库引用");
   vault.item[0].stack-=4; Check(SuperVaultUI.VaultItems[0].stack==3, "原版在材料列表扣除会落到实际大背包");
   Check(Recipe.Chests.FindAll(c=>c.index==3).Count==1, "原版箱和远程快照按索引去重");
   Recipe.Chests.Clear(); Recipe.Owned=0; ExtremeStorageUI.Marked.Clear(); ExtremeStorageUI.Marked.Add(marked);
   MarkedChestCraftingPatch.Postfix(player);
   marked.item[0].stack-=3; Check(Recipe.Chests.Find(c=>c.index==3).item[0].stack==6, "标记箱材料保持实际引用");
   Recipe.Chests.Clear(); SuperVaultUI.CanStoreItems=false; MarkedChestCraftingPatch.Postfix(player);
   Check(Recipe.Chests.Find(c=>c.index==-6)==null, "不可保存的大背包禁止参与合成");
  }
 }
}
'@
Add-Type -TypeDefinition ($harness.Replace('__ADAPTER__', $adapter))
[QualityOfTerraria.Patches.CraftingAdapterBehavior]::Run()
'6 项材料读取、去重和扣除引用检查通过；游戏原生服务器合成响应仍待联机实测'
