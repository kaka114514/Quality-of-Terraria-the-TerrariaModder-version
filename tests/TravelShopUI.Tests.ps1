$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$file = Join-Path $root 'UI/TravelShopRefreshUI.cs'
if (!(Test-Path $file)) { throw 'FAIL: 旅商刷新功能缺少商店内可点击入口' }
$source = [regex]::Replace((Get-Content $file -Raw), '(?m)^using [^;]+;\s*', '')
$stub = @'
using System;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core.UI;
using QualityOfTerraria.Networking;
using QualityOfTerraria.Patches;
namespace Terraria {
 public class NPC {public bool active=true;public int type;}
 public class Player {public bool mouseInterface;public int talkNPC=0;}
 public class Chest {public int Setups;public void SetupShop(int i){Setups++;}}
 public class Main {
  public static int netMode,npcShop=1,myPlayer,maxNPCs=1;public static bool playerInventory=true;
  public static Player LocalPlayer=new Player();public static Player[] player={LocalPlayer};
  public static NPC[] npc={new NPC{type=NPCID.TravellingMerchant}};public static int[] travelShop={1,2,3};
  public static Main instance=new Main();public int invBottom=160;public Chest[] shop={new Chest(),new Chest()};
  public static void NewText(string s,params byte[] c){}
 }
}
namespace Terraria.ID {public static class NPCID {public const int TravellingMerchant=368;}}
namespace TerrariaModder.Core.UI {
 public static class UIRenderer {
  public static int MouseX,MouseY;public static bool MouseLeftClick;public static int Draws,Clicks,Removed;
  public static void RegisterPanelBounds(string id,int x,int y,int w,int h){}public static void UnregisterPanelBounds(string id){Removed++;}
  public static void DrawRect(int x,int y,int w,int h,params byte[] c){Draws++;}public static void DrawRectOutline(int x,int y,int w,int h,byte a,byte b,byte c,byte d,int t){}
  public static void ConsumeClick(){Clicks++;MouseLeftClick=false;}
 }
}
namespace QualityOfTerraria {
 public class Config {public bool RefreshTravelShop=true;}
 public static class Mod {public static Config Config=new Config();}
}
namespace QualityOfTerraria.Networking {public static class ToolNetwork {public static bool Busy;public static string Action;public static void Request(string s,int slot=-1,params int[] data){Action=s;}}}
namespace QualityOfTerraria.Patches {public static class WorldFeatureActions {public static int Calls;public static bool RefreshTravelShop(){Calls++;Main.travelShop[0]++;return true;}}}
namespace QualityOfTerraria.UI {
 public static class UIManager {public static bool CanRender=true;}
 public static class PanelLayout {public static int ScreenWidth=800,ScreenHeight=600;public static void DrawFittedText(string s,int x,int y,int width,byte r,byte g,byte b){}}
}
'@
$test = @'
namespace QualityOfTerraria.UI {
 public static class TravelTests {
  static void Check(bool b,string s){if(!b)throw new Exception(s);}
  public static void Run(){
   TravelShopRefreshUI.Update();int setups=Main.instance.shop[1].Setups;
   Main.travelShop[1]=99;TravelShopRefreshUI.Update();Check(Main.instance.shop[1].Setups==setups+1,"其他玩家刷新时当前旅商网格更新");
   TravelShopRefreshUI.Update();Check(Main.instance.shop[1].Setups==setups+1,"未变化不能反复重建商店");
   UIRenderer.MouseX=80;UIRenderer.MouseY=445;UIRenderer.MouseLeftClick=true;TravelShopRefreshUI.Draw();
   Check(WorldFeatureActions.Calls==1&&UIRenderer.Clicks==1&&Main.LocalPlayer.mouseInterface,"单机按钮可触达且消费点击");
   Main.netMode=1;UIRenderer.MouseLeftClick=true;TravelShopRefreshUI.Draw();Check(ToolNetwork.Action=="travel-shop"&&WorldFeatureActions.Calls==1,"联机刷新不本地生成货物");
   Main.npc[0].type=1;int draws=UIRenderer.Draws;TravelShopRefreshUI.Draw();Check(UIRenderer.Draws==draws,"其他NPC商店不出现旅商按钮");
   Main.npc[0].type=NPCID.TravellingMerchant;Mod.Config.RefreshTravelShop=false;draws=UIRenderer.Draws;TravelShopRefreshUI.Draw();Check(UIRenderer.Draws==draws,"设置关闭后隐藏刷新按钮");
   Console.WriteLine("PASS: 6个旅商商店入口、多人库存刷新与点击场景");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub+$source+$test)
[QualityOfTerraria.UI.TravelTests]::Run()
