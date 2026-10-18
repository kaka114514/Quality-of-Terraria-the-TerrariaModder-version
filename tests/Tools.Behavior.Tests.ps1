param([switch]$Baseline)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') {
    $forward = @()
    if ($Baseline) { $forward += '-Baseline' }
    & "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath @forward
    exit $LASTEXITCODE
}
$root = Join-Path $PSScriptRoot '..'
if ($Baseline) { $root = Join-Path $root 'bin\optimization-baseline' }
$tools = Get-Content (Join-Path $root 'WandsAndTools.cs') -Raw -Encoding UTF8
$dummy = Get-Content (Join-Path $root 'Features/TargetDummySystem.cs') -Raw -Encoding UTF8
$qol = Get-Content (Join-Path $root 'Patches/PlayerQoLPatches.cs') -Raw -Encoding UTF8
$town = Get-Content (Join-Path $root 'Patches/TownNPCPatches.cs') -Raw -Encoding UTF8
function Method([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "找不到生产方法 $signature" }
    $open = $source.IndexOf('{', $start); $end = $open + 1; $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$placement = (Method $tools 'private static void ExecuteDragPlacement(').Replace('private static', 'public static')
$liquid = Method $tools 'public static void UpdateLiquidWand()'
$quickInput = Method $tools 'public static void UpdateQuickPlace()'
$uiHit = ''
if (!$Baseline) { $uiHit = Method $tools 'private static bool IsMouseOverToolUI()' }
$wall = ''
$dummyFields = $dummy.Substring($dummy.IndexOf('public static bool IsTrackingDummy'), $dummy.IndexOf('public static void ToggleOrSpawnAtCursor') - $dummy.IndexOf('public static bool IsTrackingDummy'))
$record = Method $dummy 'public static void RecordDamage('
$update = Method $dummy 'public static void Update()'
$reset = Method $dummy 'public static void Reset()'
$extraTools = ''; $extraDummy = ''; $recall = ''; $hits = ''
if (!$Baseline) {
    $extraTools = (Method $tools 'private static int TransferLiquid(') + (Method $tools 'private static void StoreLiquid(')
    $extraTools += Method $tools 'public static void ResetSession()'
    foreach ($signature in @('internal static int ApplyDragPlacement(', 'internal static void ApplyLiquid(', 'internal static void SyncTileArea(')) {
        $extraTools += (Method $tools $signature).Replace('internal static', 'public static')
    }
    # 分帧铺设整块实现（常量、任务类与步进方法）一起带入，测试与生产共用同一份代码。
    $jobStart = $tools.IndexOf('        // 一次框选超过该格数时按帧铺设')
    $jobEnd = $tools.IndexOf('        internal static int ApplyDragPlacement(')
    if ($jobStart -lt 0 -or $jobEnd -lt $jobStart) { throw '找不到快速放置分帧铺设实现' }
    $extraTools += $tools.Substring($jobStart, $jobEnd - $jobStart).Replace('internal static', 'public static').Replace('internal sealed class', 'public sealed class').Replace('internal const', 'public const').Replace('private static PlacementJob _localJob;', 'public static PlacementJob _localJob;')
    $extraDummy = (Method $dummy 'public static void RecordDamage(int dummyIndex, int damage, long hitSource)') + (Method $dummy 'public static void ResetSession()')
    $recallClass = Method $qol 'public static class InstantRecallPatch'
    $recall = $recallClass + (Method $qol 'public static class ToolStatsPatch') + (Method $qol 'public static class ToolAnimationPatch')
    $recall = $recall.Replace('Networking.SettingsSync', 'SettingsSync').Replace('Features.ToolSpeedSystem', 'ToolSpeedSystem')
    $hits = (Method $town 'public static class DummyProjectileSourcePatch') + (Method $town 'public static class TownNPCImmortalPatch')
} else {
    $recall = Method $qol 'public static class PlayerUpdatePatch'
}
$harness = @'
using System;
using System.Collections.Generic;
using TargetDummySystem = Dummy;
public struct ProjectileKey { public uint Bits; public static implicit operator uint(ProjectileKey key) { return key.Bits; } }
public class Projectile { public int owner; public ProjectileKey key; }
public class NPC { public int type=488, whoAmI=1; public bool townNPC; }
public static class NPCID { public const int TargetDummy=488,OldMan=37,TravellingMerchant=368; }
public class Item {
 public int type, stack, createTile = -1, createWall, placeStyle, useTime = 90, pick, axe, hammer;
 public byte prefix;
 public string Name = "stone";
 public bool IsAir { get { return stack == 0; } }
 public void TurnToAir() { stack = 0; createTile=-1; createWall=0; }
}
public class Player {
 public bool active = true, dead, mouseInterface, controlUseItem; public int selectedItem, whoAmI, chest = -1, itemTime, itemTimeMax, itemAnimation = 90, itemAnimationMax=90;
 public float pickSpeed=1, tileSpeed=1, wallSpeed=1;
 public Item HeldItem = new Item(); public static int tileRangeX = 5, tileRangeY = 4;
 public Item[] inventory=new Item[60]; public Vec Center=new Vec(320,320);
 public int SpawnCount; public void Spawn(int context) { SpawnCount++; }
}
public struct Vec { public float X, Y; public Vec(float x,float y) { X=x; Y=y; } }
public class Tile {
 public byte liquid; public ushort type, wall; public bool IsActive; private int LiquidType;
 public bool active() { return IsActive; } public int liquidType() { return LiquidType; } public void liquidType(int t) { LiquidType=t; }
}
public static class Main {
 public static Player LocalPlayer = new Player(); public static int myPlayer, maxTilesX=60, maxTilesY=60, maxChests=1000, netMode;
 public static Player[] player = new Player[] { LocalPlayer, new Player() };
 public static ulong GameUpdateCount; public static bool playerInventory, gameMenu, drawingPlayerChat, editChest, editSign;
 public static int mouseX,mouseY; public static float UIScale=1; public static bool mouseLeft,mouseRight;
 public static bool FollowMouse; public static Vec MouseWorld {get {return FollowMouse?new Vec(mouseX,mouseY):new Vec(320,320);}}
 public static Tile[,] tile = new Tile[60,60]; public static bool[] tileSolid = new bool[100];
 public static string LastText; public static void NewText(string text,int r,int g,int b) { LastText=text; } public static void NewText(string text) { LastText=text; }
}
public static class WorldGen {
 public static void PlaceTile(int x,int y,int type,bool mute,bool forced,int player,int style) { Main.tile[x,y].IsActive=true; Main.tile[x,y].type=(ushort)type; }
 public static void PlaceWall(int x,int y,int type,bool mute) { Main.tile[x,y].wall=(ushort)type; }
 public static bool CanKillTile(int x,int y) { return true; }
 public static void KillTile(int x,int y,bool fail,bool effectOnly,bool noItem) { Main.tile[x,y].IsActive=false; }
 public static void SquareTileFrame(int x,int y,bool reset) {}
}
public static class VeinMiningSystem { public static bool CanMineTile(Player player,int x,int y,int power) { return power > 0 && Main.tile[x,y].IsActive; } }
public static class NetMessage { public static void SendTileSquare(int a,int b,int c,int d,int e=0) {} public static void sendWater(int x,int y) {} }
public static class Liquid { public static void AddWater(int x,int y) {} }
public static class SoundID { public static int Dig,MenuClose; }
public static class SoundEngine { public static void PlaySound(int sound) {} }
public enum Keys { OemSemicolon, OemQuotes }
public struct KeyboardState { public bool IsKeyDown(Keys key) { return Keyboard.Pressed == key; } }
public static class Keyboard { public static Keys Pressed; public static KeyboardState GetState() { return new KeyboardState(); } }
public struct MouseState { public int X,Y; }
public static class Mouse { public static int X,Y; public static MouseState GetState() { return new MouseState {X=X,Y=Y}; } }
public static class PlayerInput {
 public static float Zoom=1;
 public static void SetZoom_UI() { Main.mouseX=(int)(Mouse.X/Main.UIScale); Main.mouseY=(int)(Mouse.Y/Main.UIScale); }
 public static void SetZoom_World() { Main.mouseX=(int)(960+(Mouse.X-960)/Zoom); Main.mouseY=(int)(540+(Mouse.Y-540)/Zoom); }
}
public static class UIRenderer {
 public static int MouseX {get {return Main.mouseX;}} public static int MouseY {get {return Main.mouseY;}}
 public static bool HoverPanel,RegisteredPanel;
 static bool Hit(int x,int y) { return RegisteredPanel && x>=490 && x<530 && y>=40 && y<80; }
 public static bool IsMouseOverAnyPanel() { return HoverPanel || Hit(Main.mouseX,Main.mouseY); }
 public static bool IsMouseOverAnyPanelUpdatePhase() { return HoverPanel || Hit((int)(Mouse.X/Main.UIScale),(int)(Mouse.Y/Main.UIScale)); }
}
public class Config { public bool WandMaterialNoConsume, MaxStack9999=true, InstantRecall=true, FastToolSpeed, ExtraTileReach, NoDamageVariance, TownNPCImmortal; }
public static class SettingsSync { public static Config[] Profiles={new Config(),new Config {WandMaterialNoConsume=true}}; public static Config GetPlayerConfig(int player) {return Main.netMode==2?Profiles[player]:Mod.Config;} }
public static class ToolSpeedSystem {public static bool SpeedActive=true;public static float GetMultiplier(int boost){return 1+Math.Max(0,Math.Min(3,boost));}}
public static class ToolNetwork { public static bool Busy; public static int Requests; public static void Request(string action,int slot=-1,params int[] values) {Requests++;} }
public static class RemoteStorageOperations { public static bool Busy; }
public static class Mod { public static Config Config = new Config(); public static bool WorldActive=true; }
public static class PlayerSpawnContext { public static int RecallFromItem; }
public static class ItemID { public const int MagicMirror=50, IceMirror=3199, CellPhone=3124, RecallPotion=2350, PotionOfReturn=4870, Shellphone=5358, ShellphoneDummy=5357, ShellphoneHell=5361, ShellphoneOcean=5360, ShellphoneSpawn=5359, MagicConch=4263, DemonConch=4819; }
namespace Terraria.Testing { public static class DebugOptions { public static bool NoDamageVar; } }
namespace UI { public static class UIManager { public static void UpdateAll() {} } public static class ExtremeStorageUI { public static void MarkChest(int i) {} } }
public static class Tools {
 public static bool QuickPlaceActive=true; public enum LiquidWandMode { Off, Water, Lava, Shimmer, Honey }
 public static LiquidWandMode CurrentLiquidMode=LiquidWandMode.Water;
 public static int StoredWater,StoredLava,StoredShimmer,StoredHoney;
 public static int QuickPlaceHudX=20,QuickPlaceHudY=80,QuickPlaceHudW=300,QuickPlaceHudH=32,LiquidHudX=20,LiquidHudY=118,LiquidHudW=390,LiquidHudH=32;
 public static bool _isDragging,_isDraggingQuickPlaceHud,_isDraggingLiquidHud,_hasHoverTile;
 public static int _dragStartX,_dragStartY,_dragCurX,_dragCurY,_hoverTileX,_hoverTileY,_dragOffsetQuickPlaceX,_dragOffsetQuickPlaceY,_dragOffsetLiquidX,_dragOffsetLiquidY;
 __PLACEMENT__ __WALL__ __LIQUID__ __QUICKINPUT__ __UIHIT__ __EXTRATOOLS__
}
public static class Dummy { public static bool _isDragging; public static int _dragOffsetX,_dragOffsetY; __DUMMYFIELDS__ __RECORD__ __EXTRADUMMY__ __UPDATE__ __RESET__ }
public static class QoL { __RECALL__ }
public static class Hooks { [ThreadStatic] private static Projectile _damageProjectile; __HITS__ }
public static class ToolsBehaviorRegression {
 static int failures, checks;
 static void Check(bool value,string label) { checks++; if(!value) { failures++; Console.WriteLine("FAIL: "+label); } }
 static void ClearTiles() { Main.tile=new Tile[60,60]; Main.LocalPlayer.mouseInterface=false; Tools.StoredWater=Tools.StoredLava=Tools.StoredHoney=Tools.StoredShimmer=0; }
 static void SetMouse(int logicalX,int logicalY,float zoom,float scale) {
  Main.UIScale=scale; PlayerInput.Zoom=zoom; Mouse.X=(int)(logicalX*scale); Mouse.Y=(int)(logicalY*scale);
  Main.mouseX=(int)(960+(Mouse.X-960)/zoom); Main.mouseY=(int)(540+(Mouse.Y-540)/zoom);
 }
 public static int Run(bool baseline) {
  foreach (int count in new int[] {998,999,1000}) {
   foreach (bool maxStack in new bool[] {false,true}) {
    ClearTiles(); Mod.Config.MaxStack9999=maxStack; Mod.Config.WandMaterialNoConsume=false;
    Main.LocalPlayer.HeldItem=new Item {stack=count,createTile=1}; Tools.ExecuteDragPlacement(Main.LocalPlayer,20,20,20,20);
    Check(Main.LocalPlayer.HeldItem.stack==count-1,"材料 "+count+" 堆叠开关 "+maxStack);
   }
  }
  Mod.Config.WandMaterialNoConsume=true; ClearTiles(); Main.LocalPlayer.HeldItem=new Item {stack=1,createTile=1};
  Tools.ExecuteDragPlacement(Main.LocalPlayer,20,20,20,20); Check(Main.LocalPlayer.HeldItem.stack==1,"免消耗独立开关");
  {
   int previousNet=Main.netMode; Main.netMode=0; Mod.Config.WandMaterialNoConsume=false;
   ClearTiles(); Main.LocalPlayer.inventory=new Item[60];
   for(int i=0;i<60;i++)Main.LocalPlayer.inventory[i]=new Item();
   Main.LocalPlayer.selectedItem=0; Main.LocalPlayer.inventory[0]=new Item {stack=1000,createTile=1};
   Main.LocalPlayer.HeldItem=Main.LocalPlayer.inventory[0]; Tools._localJob=null;
   Tools.ExecuteDragPlacement(Main.LocalPlayer,10,10,40,40);
   Check(Tools._localJob!=null && Tools._localJob.Placed<961,"大面积框选进入逐帧铺设且单帧不完井");
   int jobFrames=0; while(Tools._localJob!=null && jobFrames++<50) Tools.UpdatePlacementJobs();
   int placedTiles=0; for(int x=10;x<=40;x++)for(int y=10;y<=40;y++) if(Main.tile[x,y]!=null && Main.tile[x,y].IsActive) placedTiles++;
   Check(jobFrames<50 && Tools._localJob==null && placedTiles==961 && Main.LocalPlayer.inventory[0].stack==1000-961,"逐帧铺满分帧区域并按实际格数扣除材料");
   ClearTiles(); Tools._localJob=null; Main.netMode=previousNet; Mod.Config.WandMaterialNoConsume=true;
  }
  {
   // 原版方块替换：背包里有能挖掉目标方块的镐子时，手持方块可以直接替换它。
   int previousNet=Main.netMode; Main.netMode=0; Mod.Config.WandMaterialNoConsume=false;
   ClearTiles(); Main.LocalPlayer.selectedItem=0;
   Main.LocalPlayer.inventory[0]=new Item {stack=5,createTile=2}; Main.LocalPlayer.HeldItem=Main.LocalPlayer.inventory[0];
   Main.LocalPlayer.inventory[1]=new Item {stack=1,pick=100};
   Main.tile[20,20]=new Tile {IsActive=true,type=1};
   int replaced=Tools.ApplyDragPlacement(Main.LocalPlayer,20,20,20,20);
   Check(replaced==1 && Main.tile[20,20].IsActive && Main.tile[20,20].type==2 && Main.LocalPlayer.inventory[0].stack==4,"背包有合适镐子时直接替换已有方块");
   ClearTiles(); Main.tile[20,20]=new Tile {IsActive=true,type=1};
   Main.LocalPlayer.inventory[0].stack=5; Main.LocalPlayer.inventory[1]=new Item {stack=1,pick=0};
   Tools.ExecuteDragPlacement(Main.LocalPlayer,20,20,20,20);
   Check(Main.tile[20,20].type==1 && Main.LocalPlayer.inventory[0].stack==5,"没有能挖掉该方块的镐子时不覆盖");
   ClearTiles(); Main.netMode=previousNet; Mod.Config.WandMaterialNoConsume=true;
  }
  ClearTiles(); Main.tile[20,20]=new Tile {liquid=17}; Keyboard.Pressed=Keys.OemSemicolon; Tools.UpdateLiquidWand();
  Check(Tools.StoredWater==17 && Main.tile[20,20].liquid==0,"吸收 17 单位守恒");
  Tools.StoredWater=500; Main.tile[20,20].liquid=200; Keyboard.Pressed=Keys.OemQuotes; Tools.UpdateLiquidWand();
  Check(Tools.StoredWater==445 && Main.tile[20,20].liquid==255,"只补缺少的55单位");
  int stored=Tools.StoredWater; Tools.UpdateLiquidWand(); Check(Tools.StoredWater==stored,"长按满格不扣量");
  Main.tile[20,20].liquid=0; Tools.StoredWater=12; Tools.UpdateLiquidWand(); Check(Tools.StoredWater==0 && Main.tile[20,20].liquid==12,"不足一格精确放出");
  Main.tile[20,20].liquid=17; Main.tile[20,20].liquidType(1); Tools.StoredWater=12; Tools.UpdateLiquidWand();
  Check(Main.tile[20,20].liquid==12 && Main.tile[20,20].liquidType()==0 && Tools.StoredLava==17 && Tools.StoredWater==0,"替换液体先回收原17单位");
  ClearTiles(); Main.tile[20,20]=new Tile {liquid=7}; Main.LocalPlayer.mouseInterface=true; Keyboard.Pressed=Keys.OemSemicolon;
  Tools.UpdateLiquidWand(); Check(Main.tile[20,20].liquid==0 && Tools.StoredWater==7,"快速放置标记不阻断吸收");
  UIRenderer.HoverPanel=true; Main.tile[20,20].liquid=8; Tools.UpdateLiquidWand(); Check(Main.tile[20,20].liquid==8,"UI悬停阻断吸收"); UIRenderer.HoverPanel=false;
  Dummy.Reset(); Main.GameUpdateCount=10;
  if (baseline) { Dummy.RecordDamage(1,100); Dummy.RecordDamage(1,50); } else { __INDEPENDENT__ }
  Dummy.Update(); Check(Dummy.TotalDamage==150 && Dummy.InstantDPS==150 && Dummy.AverageDPS==150,"同帧独立命中都进入三个统计");
  Dummy.Reset(); Main.GameUpdateCount=10;
  if (baseline) { Dummy.RecordDamage(1,100); Dummy.RecordDamage(1,150); } else { __REPEATED__ }
  Dummy.Update(); Check(Dummy.TotalDamage==150 && Dummy.InstantDPS==150 && Dummy.AverageDPS==150,"重复来源取最大且滑动窗口同步");
  Main.GameUpdateCount=70; Dummy.Update(); Check(Dummy.InstantDPS==0,"60帧窗口准确过期");
  Main.LocalPlayer.HeldItem=new Item {type=ItemID.ShellphoneOcean,useTime=90}; Main.LocalPlayer.itemTime=90;
  __RECALLCALL__
  Check(Main.LocalPlayer.SpawnCount==0,"瞬传不调用统一出生点路径");
  if (!baseline) { __RECALLCHECKS__ }
  Console.WriteLine("工具与假人行为检查："+(checks-failures)+"/"+checks+" 通过"); return failures;
 }
}
'@
$harness=$harness.Replace('__PLACEMENT__',$placement).Replace('__WALL__',$wall).Replace('__LIQUID__',$liquid).Replace('__QUICKINPUT__',$quickInput).Replace('__UIHIT__',$uiHit).Replace('__EXTRATOOLS__',$extraTools).Replace('__DUMMYFIELDS__',$dummyFields).Replace('__RECORD__',$record).Replace('__EXTRADUMMY__',$extraDummy).Replace('__UPDATE__',$update).Replace('__RESET__',$reset).Replace('__RECALL__',$recall).Replace('__HITS__',$hits)
if ($Baseline) {
    $harness=$harness.Replace('__INDEPENDENT__','').Replace('__REPEATED__','').Replace('__RECALLCALL__','QoL.PlayerUpdatePatch.Postfix(Main.LocalPlayer,0);').Replace('__RECALLCHECKS__','')
} else {
    $harness=$harness.Replace('__INDEPENDENT__','Dummy.RecordDamage(1,100,101); Dummy.RecordDamage(1,50,102);').Replace('__REPEATED__','Dummy.RecordDamage(1,100,101); Dummy.RecordDamage(1,150,101);').Replace('__RECALLCALL__','QoL.InstantRecallPatch.Postfix(Main.LocalPlayer,Main.LocalPlayer.HeldItem);').Replace('__RECALLCHECKS__',@'
   Check(Main.LocalPlayer.itemTime==46,"贝壳海洋模式只缩短等待");
   Check(Main.LocalPlayer.itemAnimation==46,"传送动画同步缩短防重复消耗");
   Main.LocalPlayer.HeldItem.type=ItemID.PotionOfReturn; Main.LocalPlayer.itemTime=90; QoL.InstantRecallPatch.Postfix(Main.LocalPlayer,Main.LocalPlayer.HeldItem); Check(Main.LocalPlayer.itemTime==21,"返回药水保留20帧原版触发点");
   Main.LocalPlayer.HeldItem.type=999; Main.LocalPlayer.itemTime=90; QoL.InstantRecallPatch.Postfix(Main.LocalPlayer,Main.LocalPlayer.HeldItem); Check(Main.LocalPlayer.itemTime==90,"其他物品不变");
   foreach(int type in new int[] {ItemID.MagicMirror,ItemID.IceMirror,ItemID.CellPhone,ItemID.Shellphone,ItemID.ShellphoneOcean,ItemID.ShellphoneHell,ItemID.ShellphoneSpawn,ItemID.MagicConch,ItemID.DemonConch}) {
    Main.LocalPlayer.HeldItem.type=type; Main.LocalPlayer.itemTime=90; Main.LocalPlayer.itemAnimation=90; QoL.InstantRecallPatch.Postfix(Main.LocalPlayer,Main.LocalPlayer.HeldItem); Check(Main.LocalPlayer.itemTime==46 && Main.LocalPlayer.itemAnimation==46,"原版镜子/贝壳触发点 "+type);
   }
   Mod.Config.InstantRecall=false; Main.LocalPlayer.itemTime=90; QoL.InstantRecallPatch.Postfix(Main.LocalPlayer,Main.LocalPlayer.HeldItem); Check(Main.LocalPlayer.itemTime==90,"瞬传关闭不修改原版计时");
   foreach(bool speed in new bool[] {false,true}) foreach(bool reach in new bool[] {false,true}) {
    Mod.Config.DigSpeedBoost=Mod.Config.AxeSpeedBoost=Mod.Config.PlaceSpeedBoost=speed?1:0; Mod.Config.ExtraTileReach=reach?5:0; Main.LocalPlayer.pickSpeed=0.8f; Main.LocalPlayer.tileSpeed=1.5f; Main.LocalPlayer.wallSpeed=1; Player.tileRangeX=5; Player.tileRangeY=4;
    QoL.ToolStatsPatch.Postfix(Main.LocalPlayer); Check(Player.tileRangeX==(reach?10:5) && Player.tileRangeY==(reach?9:4),"距离与速度独立 "+speed+"/"+reach);
    Check(Main.LocalPlayer.pickSpeed==(speed?0.4f:0.8f) && Main.LocalPlayer.tileSpeed==(speed?0.75f:1.5f) && Main.LocalPlayer.wallSpeed==(speed?0.5f:1),"工具与放置速度 "+speed+"/"+reach);
   }
   Main.LocalPlayer.HeldItem.axe=10; Main.LocalPlayer.itemTime=20; Main.LocalPlayer.itemAnimation=40; Main.LocalPlayer.itemAnimationMax=40; QoL.InstantRecallPatch.Postfix(Main.LocalPlayer,Main.LocalPlayer.HeldItem); QoL.ToolAnimationPatch.Postfix(Main.LocalPlayer,Main.LocalPlayer.HeldItem);
   Check(Main.LocalPlayer.itemTime==10 && Main.LocalPlayer.itemAnimation==20,"斧锤使用与动画加速");
   Main.netMode=2;
   foreach(bool serverDefault in new bool[] {false,true}) { Mod.Config.WandMaterialNoConsume=serverDefault;
   foreach(int id in new int[] {0,1}) {
    ClearTiles(); Player actor=new Player {whoAmI=id,HeldItem=new Item {stack=2,createTile=1}};
    Tools.ApplyDragPlacement(actor,20,20,20,20); Check(actor.HeldItem.stack==(id==0?1:2),"服务器快速放置按个人无耗设置隔离 "+id);
   }
   }
   SettingsSync.Profiles[0]=null; ClearTiles(); Player unconfirmed=new Player {whoAmI=0,HeldItem=new Item {stack=2,createTile=1}};
   Check(Tools.ApplyDragPlacement(unconfirmed,20,20,20,20)==0 && unconfirmed.HeldItem.stack==2,"个人配置未确认不允许服务器快速放置");
   SettingsSync.Profiles[0]=new Config(); Main.netMode=0;
   ClearTiles(); Mod.Config.WandMaterialNoConsume=false; Main.LocalPlayer.selectedItem=0;
   Main.LocalPlayer.inventory[0]=new Item {stack=1,createTile=1}; Main.LocalPlayer.HeldItem=Main.LocalPlayer.inventory[0];
   Tools.ExecuteDragPlacement(Main.LocalPlayer,19,20,21,20);
   int tileCount=0; foreach(Tile t in Main.tile) if(t!=null && t.IsActive) tileCount++;
   Check(tileCount==1 && Main.LocalPlayer.inventory[0].stack==0,"放置材料耗尽立即停止，不能继续免费铺设");
   ClearTiles(); Main.netMode=1; Main.LocalPlayer.HeldItem=new Item {stack=2,createTile=1}; Main.LocalPlayer.inventory[0]=new Item {stack=2,createWall=1}; Main.tile[20,20]=new Tile {liquid=17};
   Tools.ExecuteDragPlacement(Main.LocalPlayer,20,20,20,20); Tools.CurrentLiquidMode=Tools.LiquidWandMode.Water; Keyboard.Pressed=Keys.OemSemicolon; Main.FollowMouse=false; Tools.UpdateLiquidWand();
   Check(!Main.tile[20,20].IsActive && Main.tile[20,20].wall==0 && Main.tile[20,20].liquid==17 && Tools.StoredWater==0 && ToolNetwork.Requests==2,"联机客户端放置与吸液只发请求不改世界");
   Main.netMode=0;
   Tools.StoredWater=17; Tools.StoredLava=3; Tools._isDragging=Tools._isDraggingQuickPlaceHud=Tools._isDraggingLiquidHud=Tools._hasHoverTile=true; Tools.ResetSession();
   Check(!Tools.QuickPlaceActive && Tools.CurrentLiquidMode==Tools.LiquidWandMode.Off && !Tools._isDragging && !Tools._isDraggingQuickPlaceHud && !Tools._isDraggingLiquidHud && !Tools._hasHoverTile,"世界退出关闭工具与预览拖动");
   Check(Tools.StoredWater==17 && Tools.StoredLava==3,"退出世界保留已吸液体进程库存");
   Dummy.ActiveDummyWhoAmI=1; Dummy.IsTrackingDummy=true; Dummy._isDragging=true; Dummy.ResetSession();
   Check(!Dummy.IsTrackingDummy && Dummy.ActiveDummyWhoAmI==-1 && Dummy.TotalDamage==0 && Dummy.InstantDPS==0 && Dummy.AverageDPS==0 && !Dummy._isDragging,"世界退出清假人统计绑定HUD");
   Main.GameUpdateCount=100; Projectile outer,inner; NPC npc=new NPC(); Projectile a=new Projectile {key=new ProjectileKey {Bits=101}},b=new Projectile {key=new ProjectileKey {Bits=102}};
   Hooks.DummyProjectileSourcePatch.Prefix(a,out outer); Hooks.TownNPCImmortalPatch.Postfix(npc,false,0,100);
   Hooks.DummyProjectileSourcePatch.Prefix(b,out inner); Hooks.TownNPCImmortalPatch.Postfix(npc,false,0,50); Hooks.DummyProjectileSourcePatch.Finalizer(inner);
   Hooks.TownNPCImmortalPatch.Postfix(npc,false,0,150); Hooks.DummyProjectileSourcePatch.Finalizer(outer);
   Hooks.TownNPCImmortalPatch.Postfix(npc,false,0,20); Hooks.TownNPCImmortalPatch.Postfix(npc,false,0,20);
   Hooks.TownNPCImmortalPatch.Postfix(npc,true,0,500); Dummy.Update();
   Check(Dummy.TotalDamage==240 && Dummy.InstantDPS==240 && Dummy.AverageDPS==240,"真实补丁来源嵌套恢复、爆炸去重、独立命中与网络回调过滤");
   float[] zooms={1.5f,1f,1.5f}, scales={1f,1.25f,1.25f};
   for(int i=0;i<zooms.Length;i++) {
    foreach(int target in new int[] {0,1,2,3}) {
     ClearTiles(); Main.FollowMouse=true; Tools.QuickPlaceActive=true; Tools.CurrentLiquidMode=Tools.LiquidWandMode.Water; Tools._isDragging=false;
     Main.mouseLeft=true; Keyboard.Pressed=Keys.OemSemicolon; UIRenderer.RegisteredPanel=target==2;
     SetMouse(target==2?500:target==3?450:50,target==0?90:target==1?130:target==2?50:400,zooms[i],scales[i]);
     int worldX=Main.mouseX,worldY=Main.mouseY, tileX=(int)(Main.MouseWorld.X/16f),tileY=(int)(Main.MouseWorld.Y/16f); Main.tile[tileX,tileY]=new Tile {liquid=17};
     Tools.UpdateQuickPlace(); Tools.UpdateLiquidWand();
     bool blocked=target!=3;
     Check(Tools._isDragging==!blocked && Tools._hasHoverTile==!blocked && (Main.tile[tileX,tileY].liquid==17)==blocked,"更新阶段阻断 "+target+" zoom="+zooms[i]+" ui="+scales[i]);
     Check(Main.mouseX==worldX && Main.mouseY==worldY && Main.MouseWorld.X==worldX && Main.MouseWorld.Y==worldY,"UI命中判断返回后恢复世界坐标");
     if(!blocked) Check(Tools._hoverTileX==tileX && Tools._hoverTileY==tileY && Tools.StoredWater==17,"世界瓦片读取保留world阶段且两工具共同工作");
    }
   }
'@)
}
if (!$Baseline) { $harness=$harness.Replace('FastToolSpeed, ExtraTileReach, NoDamageVariance', 'NoDamageVariance').Replace('TownNPCImmortal; }', 'TownNPCImmortal; public int ExtraTileReach=5,DigSpeedBoost,AxeSpeedBoost,PlaceSpeedBoost; }') }
# 旧版 Windows PowerShell 的编译器不支持插值字符串；仅消息文案降级，不改执行算法。
$harness = $harness.Replace('$"', '"')
Add-Type -TypeDefinition $harness -Language CSharp
$result=[ToolsBehaviorRegression]::Run($Baseline.IsPresent)
if($result -gt 0) { exit 1 }
