$ErrorActionPreference = 'Stop'
$source=Get-Content (Join-Path (Split-Path $PSScriptRoot) 'Mod.cs') -Raw
$signature='internal static bool IsForceVeinMiningHeld()'
$start=$source.IndexOf($signature)
if($start -lt 0){throw 'FAIL: 更好的体验尚未接入强制连锁按键'}
$open=$source.IndexOf('{',$start);$end=$open+1;$depth=1
while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
$method=$source.Substring($start,$end-$start)
$stub=@'
using System;
using Terraria;
using Terraria.GameInput;
using TerrariaModder.Core.Input;
using TerrariaModder.Core.UI;
namespace Terraria {
 public class Player {public bool active=true,dead,mouseInterface;}
 public static class Main {public static bool gameMenu,dedServ,drawingPlayerChat,editSign,editChest;public static int netMode;public static Player LocalPlayer=new Player();}
}
namespace Terraria.GameInput {public static class PlayerInput {public static bool WritingText;}}
namespace TerrariaModder.Core.UI {public static class UIRenderer {public static bool IsWaitingForKeyInput;}}
namespace TerrariaModder.Core.Input {
 public class KeyCombo {public int Key=160;public bool Ctrl,Shift,Alt;public bool IsPressed(){return InputState.IsKeyDown(Key)&&Ctrl==InputState.Ctrl&&Shift==InputState.Shift&&Alt==InputState.Alt;}}
 public class Keybind {public KeyCombo CurrentKey=new KeyCombo();}
 public static class InputState {public static int Down;public static bool Ctrl,Shift,Alt;public static bool IsKeyDown(int key){return key==Down;}public static bool IsCtrlDown(){return Ctrl;}public static bool IsShiftDown(){return Shift;}public static bool IsAltDown(){return Alt;}}
}
namespace QualityOfTerraria.Patches {public static class StorageInventoryGuard {public static bool Locked;}}
namespace QualityOfTerraria {
 public class Context {public Keybind Bind=new Keybind();public Keybind GetKeybind(string name){return Bind;}}
 public class Mod {public static bool WorldActive=true,VeinMinerLegacyLoaded;private static Mod _instance=new Mod();private Context _context=new Context();__METHOD__ public static Context Context=>_instance._context;}
 public static class InputTests {
  static void Check(bool b,string s){if(!b)throw new Exception(s);}
  public static void Run(){
   InputState.Down=160;InputState.Shift=true;
   Check(!Mod.Context.Bind.CurrentKey.IsPressed(),"复现Core单独Shift严格修饰键判断");
   Check(Mod.IsForceVeinMiningHeld(),"左Shift按住临时强制连锁");
   InputState.Down=0;InputState.Shift=false;Check(!Mod.IsForceVeinMiningHeld(),"松键即停止强制模式");
   Mod.Context.Bind.CurrentKey.Key=16;InputState.Down=161;InputState.Shift=true;Check(Mod.IsForceVeinMiningHeld(),"通用Shift改键接受右Shift");
   Mod.Context.Bind.CurrentKey.Key=65;InputState.Down=65;InputState.Shift=false;Check(Mod.IsForceVeinMiningHeld(),"普通按住键遵守加载器改键");
   PlayerInput.WritingText=true;Check(!Mod.IsForceVeinMiningHeld(),"文本输入时不强制挖矿");PlayerInput.WritingText=false;
   Main.LocalPlayer.mouseInterface=true;Check(!Mod.IsForceVeinMiningHeld(),"界面操作时不挖矿");Main.LocalPlayer.mouseInterface=false;
   Patches.StorageInventoryGuard.Locked=true;Check(!Mod.IsForceVeinMiningHeld(),"库存等待时不触发拾取");Patches.StorageInventoryGuard.Locked=false;
   VeinMinerLegacyLoaded(true);Check(!Mod.IsForceVeinMiningHeld(),"旧插件同时加载时暂停重复连锁");VeinMinerLegacyLoaded(false);
   Main.LocalPlayer.dead=true;Check(!Mod.IsForceVeinMiningHeld(),"死亡时禁用");Main.LocalPlayer.dead=false;
   Main.netMode=2;Check(!Mod.IsForceVeinMiningHeld(),"专服不读取客户端键盘");
   Console.WriteLine("PASS: 11个强制连锁、修饰键、界面及生命周期输入场景");
  }
  static void VeinMinerLegacyLoaded(bool v){Mod.VeinMinerLegacyLoaded=v;}
 }
}
'@
Add-Type -TypeDefinition $stub.Replace('__METHOD__',$method)
[QualityOfTerraria.InputTests]::Run()
