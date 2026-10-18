$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$core = Join-Path $root 'Features/VeinMiningSystem.cs'
$patch = Join-Path $root 'Patches/VeinMiningPatches.cs'
if (!(Test-Path $core) -or !(Test-Path $patch)) { throw 'FAIL: 缺少连锁挖掘核心，无法保证8邻接、范围、数量与原版掉落' }
$stubs = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Microsoft.Xna.Framework;
using QualityOfTerraria.Networking;
using QualityOfTerraria.Features;
using QualityOfTerraria.Patches;
using Terraria.Localization;
namespace Microsoft.Xna.Framework {
 public struct Point {public int X,Y;public Point(int x,int y){X=x;Y=y;}}
 public struct Vector2 {public float X,Y;public Vector2(float x,float y){X=x;Y=y;}}
}
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch:Attribute {public HarmonyPatch(){}public HarmonyPatch(Type t,string s,params Type[] p){}}
 public static class AccessTools {public static MethodInfo Method(Type t,string n,Type[] p=null){return p==null?t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static):t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static,null,p,null);}public static T MethodDelegate<T>(MethodInfo m) where T:class {return Delegate.CreateDelegate(typeof(T),m) as T;}}
}
namespace Terraria.DataStructures {public interface IEntitySource{}public class EntitySource_TileBreak:IEntitySource {public Point TileCoords;public EntitySource_TileBreak(int x,int y){TileCoords=new Point(x,y);}}}
namespace Terraria.ID {public static class TileID {public static class Sets {public static bool[] Ore=new bool[256];}}}
namespace Terraria {
 public class Tile {public ushort type;public bool Active=true,Protected,Fail;public int RequiredPick=50;public bool active(){return Active;}}
 public class Item {public int type,stack,pick=100,maxStack=9999;public byte prefix;public bool IsAir {get{return type==0||stack<=0;}}public static int NewItem(){return 0;}}
 public class WorldItem {public Item inner=new Item();public Vector2 Center;public int playerIndexTheItemIsReservedFor=255,Reserved,Syncs;public int type{get{return inner.type;}}public bool active{get{return !inner.IsAir;}}public int stack{get{return inner.stack;}}public void ReserveFor(int p,int t){playerIndexTheItemIsReservedFor=p;Reserved++;}public void SyncItem(){Syncs++;}}
 public class Player {public bool active=true,dead;public int whoAmI;public Item HeldItem=new Item();public Vector2 Center=new Vector2(80,80);public int Space=9999,Received,Pickups;public byte InventoryPrefix;public void PickTile(int x,int y,int power,int damage=-1){}public class ItemSpaceStatus {public bool CanTakeItem;}
  public ItemSpaceStatus ItemSpace(WorldItem w){return new ItemSpaceStatus {CanTakeItem=Space>0};}
  private int GetPickaxeDamage(int x,int y,int pick,int hit,Tile tile){return pick>=tile.RequiredPick?pick:0;}
  private void PickupItem(WorldItem w){Pickups++;if(QualityOfTerraria.Patches.StorageInventoryGuard.Locked)return;if(InventoryPrefix!=w.inner.prefix && Received>0)return;int n=Math.Min(w.inner.stack,Space);Space-=n;Received+=n;w.inner.stack-=n;}
 }
 public static class Main {public static int netMode;public static bool gameMenu;public static string LastText;public static Player[] player=new Player[3];public static Player LocalPlayer {get{return player[0];}}public static Tile[,] tile=new Tile[100,100];public static WorldItem[] item=new WorldItem[1000];public static void NewText(string s,byte r=255,byte g=255,byte b=255){LastText=s;}}
 public static class WorldGen {public static int Kills;public static bool InWorld(int x,int y,int margin=0){return x>=margin&&y>=margin&&x<100-margin&&y<100-margin;}public static bool CanKillTile(int x,int y){return !Main.tile[x,y].Protected;}public static void KillTile(int x,int y,bool fail=false,bool effectOnly=false,bool noItem=false){var t=Main.tile[x,y];if(t.Fail||t.Protected)return;t.Active=false;Kills++;var w=new WorldItem {inner=new Item {type=t.type,stack=1}};Main.item[Kills]=w;QualityOfTerraria.Patches.VeinMiningDropPatch.Postfix(new EntitySource_TileBreak(x,y),Kills);}}
 public static class NetMessage {public static int Tiles;public static void SendTileSquare(int p,int x,int y,int w,int h){if(w>20||h>20)throw new Exception("包过大");Tiles++;}}
}
namespace Terraria.Localization {public static class Lang {public static string GetItemNameValue(int type){return type==7?"测试矿":"矿石";}}}
namespace QualityOfTerraria {public class QualityOfTerrariaConfig {public bool VeinMiningEnabled=true,VeinMiningAutoLoot=true,VeinMiningCheckPickaxePower=true,VeinMiningMatchExactType=true,VeinMiningShowMessages;public int VeinMiningRange=12,VeinMiningMaxBlocks=64;}public static class Mod {public static bool WorldActive=true;public static QualityOfTerrariaConfig Config=new QualityOfTerrariaConfig();public static void LogError(string s,Exception e){throw e;}public static bool IsForceVeinMiningHeld(){return VeinMiningSystem.IsForced;}}}
namespace QualityOfTerraria.Networking {public static class SettingsSync {public static QualityOfTerrariaConfig GetPlayerConfig(int slot){return Mod.Config;}public static bool SaveVerified(QualityOfTerrariaConfig c,out string e){e=null;return true;}}public static class VeinMiningNetwork {public static int Requests;public static void Request(int x,int y,ushort t,bool forced){Requests++;}}}
namespace QualityOfTerraria.Patches {public static class StorageInventoryGuard {public static bool Locked;}}
'@
$tests = @'
namespace QualityOfTerraria.Features {
 public static class VeinTests {
  static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
  static Player Setup(){VeinMiningSystem.ResetSession();VeinMiningSystem.Suspended=false;Main.netMode=0;Main.gameMenu=false;Main.LastText=null;Mod.WorldActive=true;Mod.Config=new QualityOfTerrariaConfig();Main.tile=new Tile[100,100];Main.item=new WorldItem[1000];Main.player[0]=new Player();Main.player[1]=new Player {whoAmI=1};WorldGen.Kills=0;NetMessage.Tiles=0;QualityOfTerraria.Patches.StorageInventoryGuard.Locked=false;TileID.Sets.Ore[7]=true;TileID.Sets.Ore[8]=true;return Main.player[0];}
  static void Ore(int x,int y,int type=7){Main.tile[x,y]=new Tile {type=(ushort)type};}
  static void Drain(){for(int i=0;i<100;i++)VeinMiningSystem.Update();}
  public static void Run(){
   var messagePlayer=Setup();Mod.Config.VeinMiningShowMessages=true;Mod.Config.VeinMiningAutoLoot=false;for(int x=11;x<14;x++)Ore(x,10);VeinMiningSystem.Start(messagePlayer,10,10,7,Mod.Config);Drain();Check(Main.LastText=="【连锁挖矿】连锁挖掉 4 个测试矿"&&!Main.LastText.Contains("含初始矿块"),"提示显示实际矿名、数量包含起始矿块且不显示多余说明");
   var smallVeinPlayer=Setup();Mod.Config.VeinMiningShowMessages=true;Mod.Config.VeinMiningAutoLoot=false;Ore(11,10);VeinMiningSystem.Start(smallVeinPlayer,10,10,7,Mod.Config);Drain();Check(Main.LastText=="【连锁挖矿】连锁挖掉 2 个测试矿","小矿脉提示数量包含起始矿块");
   var p=Setup();Ore(11,11);Ore(12,12);Ore(14,14);Check(VeinMiningSystem.Start(p,10,10,7,Mod.Config),"合法原点任务接受");Drain();Check(WorldGen.Kills==2,"8邻接连接且空洞不跨越");
   p=Setup();Mod.Config.VeinMiningRange=1;Ore(11,10);Ore(12,10);VeinMiningSystem.Start(p,10,10,7,Mod.Config);Drain();Check(WorldGen.Kills==1,"范围按最初原点限制");
   p=Setup();Mod.Config.VeinMiningMaxBlocks=1;Ore(11,10);VeinMiningSystem.Start(p,10,10,7,Mod.Config);Drain();Check(WorldGen.Kills==0,"数量1包含亲挖原块");
   p=Setup();Mod.Config.VeinMiningMaxBlocks=3;for(int x=11;x<20;x++)Ore(x,10);VeinMiningSystem.Start(p,10,10,7,Mod.Config);Drain();Check(WorldGen.Kills==2,"数量上限包含原块");
   p=Setup();Ore(11,10,8);Ore(12,10);VeinMiningSystem.Start(p,10,10,7,Mod.Config);Drain();Check(WorldGen.Kills==0,"严格同种匹配");
   p=Setup();Mod.Config.VeinMiningMatchExactType=false;Ore(11,10,8);Ore(12,10,42);Ore(13,10);VeinMiningSystem.Start(p,10,10,7,Mod.Config);Drain();Check(WorldGen.Kills==1,"混矿模式不连建筑物且不跨过建筑");
   p=Setup();Ore(11,10);Main.tile[11,10].RequiredPick=200;VeinMiningSystem.Start(p,10,10,7,Mod.Config);Drain();Check(WorldGen.Kills==0,"原版镐力限制");
   Mod.Config.VeinMiningCheckPickaxePower=false;Check(!VeinMiningSystem.CanMineOrigin(p,11,10),"原版原点来源必须验证真实镐力，不被连锁开关绕过");VeinMiningSystem.Start(p,10,10,7,Mod.Config);Drain();Check(WorldGen.Kills==1,"可关闭镐力");
   p=Setup();Mod.Config.VeinMiningCheckPickaxePower=false;Ore(11,10);Main.tile[11,10].Protected=true;Ore(12,10);VeinMiningSystem.Start(p,10,10,7,Mod.Config);Drain();Check(WorldGen.Kills==0,"关闭镐力仍保留保护且不跨保护矿块");
   p=Setup();Ore(11,10);Main.tile[11,10].Fail=true;Ore(12,10);VeinMiningSystem.Start(p,10,10,7,Mod.Config);Drain();Check(WorldGen.Kills==0&&Main.tile[12,10].Active,"原版破坏失败不继续穿透");
   p=Setup();Ore(11,10);VeinMiningSystem.Start(p,10,10,7,Mod.Config);VeinMiningSystem.Start(Main.player[1],10,10,7,Mod.Config);Drain();Check(WorldGen.Kills==1,"并发相同矿块仅有一份掉落");
   p=Setup();Ore(11,10);VeinMiningSystem.Start(p,10,10,7,Mod.Config);Main.tile=new Tile[100,100];Drain();Check(WorldGen.Kills==0,"更换世界瓦片数组终止旧作业");
   p=Setup();Ore(11,10);VeinMiningSystem.Start(p,10,10,7,Mod.Config);p.dead=true;Drain();Check(WorldGen.Kills==0,"玩家死亡终止作业");
   p=Setup();Main.netMode=1;Ore(11,10);Check(!VeinMiningSystem.Start(p,10,10,7,Mod.Config),"客户端不能修改连锁世界");Drain();Check(WorldGen.Kills==0,"客户端零掉落");VeinMiningSystem.IsForced=true;VeinMiningSystem.Update();Check(VeinMiningSystem.IsForced,"联机客户端更新保留本帧按住的强制连锁键");
   p=Setup();VeinMiningSystem.Suspended=true;Ore(11,10);Check(!VeinMiningSystem.Start(p,10,10,7,Mod.Config),"旧独立插件同时运行暂停");
   p=Setup();Ore(11,10);var c=VeinMiningSystem.BeginDrops(p,11,10);WorldGen.KillTile(11,10);VeinMiningSystem.EndDrops(c);Check(p.Received==1&&!Main.item[1].active,"真实原版掉落入包");
   p=Setup();p.Space=0;Ore(11,10);c=VeinMiningSystem.BeginDrops(p,11,10);WorldGen.KillTile(11,10);VeinMiningSystem.EndDrops(c);Check(p.Received==0&&Main.item[1].stack==1,"满包真实物品留地面");
   p=Setup();QualityOfTerraria.Patches.StorageInventoryGuard.Locked=true;Ore(11,10);c=VeinMiningSystem.BeginDrops(p,11,10);WorldGen.KillTile(11,10);VeinMiningSystem.EndDrops(c);Check(p.Received==0&&Main.item[1].stack==1,"库存事务锁阻止强制拾取");
   p=Setup();c=VeinMiningSystem.BeginDrops(p,11,10);Ore(15,15);WorldGen.KillTile(15,15);VeinMiningSystem.EndDrops(c);Check(p.Received==0,"无关坐标掉落不吞入包");
   p=Setup();Main.netMode=2;Ore(11,10);c=VeinMiningSystem.BeginDrops(Main.player[1],11,10);WorldGen.KillTile(11,10);VeinMiningSystem.EndDrops(c);Check(Main.player[1].Received==0&&Main.item[1].Reserved==1&&Main.item[1].playerIndexTheItemIsReservedFor==1,"服务端保留真实掉落归属并交给原版客户端拾取");
   p=Setup();for(int x=11;x<40;x++)Ore(x,10);Mod.Config.VeinMiningRange=30;VeinMiningSystem.Start(p,10,10,7,Mod.Config);VeinMiningSystem.Update();Check(WorldGen.Kills<29,"每帧预算不一次完成整矿脉");VeinMiningSystem.ResetSession();Drain();int k=WorldGen.Kills;Check(k<29,"退出清理任务");
    Console.WriteLine("PASS: 连锁连接、范围、原点计数、矿名提示、同种、镐力、保护、竞争、联机强制输入、生命周期及真实掉落24组行为");
  }
 }
}
'@
$source = (Get-Content $core -Raw) + (Get-Content $patch -Raw)
$source = [regex]::Replace($source, '(?m)^using [^;]+;\s*', '')
Add-Type -TypeDefinition ($stubs + $source + $tests)
[QualityOfTerraria.Features.VeinTests]::Run()
