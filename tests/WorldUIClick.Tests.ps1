$ErrorActionPreference = 'Stop'
$path = Join-Path (Split-Path $PSScriptRoot) 'Patches/WorldUIClickPatch.cs'
if (!(Test-Path $path)) { throw 'FAIL: 世界绘制提前清除左键边沿后没有独立UI点击恢复' }
$source = [regex]::Replace((Get-Content $path -Raw), '(?m)^using [^;]+;\s*', '')
$stub = @'
using System;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TerrariaModder.Core.UI;
namespace HarmonyLib { public class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string n){}} }
namespace Microsoft.Xna.Framework.Input { public enum ButtonState {Released,Pressed} public struct MouseState {public ButtonState LeftButton;} public static class Mouse {public static bool Pressed;public static MouseState GetState(){return new MouseState{LeftButton=Pressed?ButtonState.Pressed:ButtonState.Released};}} }
namespace Terraria { public static class Main {public static bool gameMenu,dedServ,playerInventory,ingameOptionsWindow,mouseLeft,mouseLeftRelease;} }
namespace TerrariaModder.Core.UI {public static class ModMenu {public static bool IsVisible;}}
namespace QualityOfTerraria {public static class Mod {public static bool WorldActive=true;}}
namespace QualityOfTerraria.UI {
 public static class ExtremeStorageUI {public static bool IsOpen;}
 public static class SuperVaultUI {public static bool IsOpen;}
 public static class PlayerStatsUI {public static bool IsOpen;}
 public static class BuffAndSpawnRateUI {public static bool IsOpen;}
 public static class StructureLocatorUI {public static bool IsOpen;}
 public static class TravelShopRefreshUI {public static bool IsOpen;}
}
'@
$test = @'
namespace QualityOfTerraria.Patches {
 public static class ClickTests {
  static void Check(bool b,string s){if(!b)throw new Exception(s);}
  static void Frame(bool down) {Mouse.Pressed=down;WorldUIClickPatch.Prefix();Main.mouseLeftRelease=false;WorldUIClickPatch.Postfix();}
  public static void Run(){
   Main.playerInventory=true;Frame(false);Frame(true);Check(Main.mouseLeft&&Main.mouseLeftRelease,"原版背包点击边沿恢复");
   Frame(true);Check(!Main.mouseLeftRelease,"按住不产生重复点击");
   Frame(false);Main.playerInventory=false;Main.ingameOptionsWindow=true;Frame(true);Check(Main.mouseLeftRelease,"原版设置按钮恢复");
   Frame(false);Main.ingameOptionsWindow=false;ModMenu.IsVisible=true;Frame(true);Check(Main.mouseLeftRelease,"加载器设置不丢点击");
   Frame(false);ModMenu.IsVisible=false;UI.SuperVaultUI.IsOpen=true;Frame(true);Check(Main.mouseLeftRelease,"本模组窗口点击恢复");
   Frame(false);UI.SuperVaultUI.IsOpen=false;Frame(true);Check(!Main.mouseLeftRelease,"世界普通攻击保留原版规则");
   Frame(false);Main.playerInventory=true;Main.gameMenu=true;Frame(true);Check(!Main.mouseLeftRelease,"主菜单不改变游戏输入");
   Main.gameMenu=false;Frame(false);Main.dedServ=true;Frame(true);Check(!Main.mouseLeftRelease,"服务器不读取物理鼠标");
   Main.dedServ=false;WorldUIClickPatch.ResetSession();Frame(true);Check(!Main.mouseLeftRelease,"切世界时按住的左键不能凭空变成新点击");
   Console.WriteLine("PASS: 9个原版／模组UI边沿、持续按住和游戏操作隔离场景");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub+$source+$test)
[QualityOfTerraria.Patches.ClickTests]::Run()
