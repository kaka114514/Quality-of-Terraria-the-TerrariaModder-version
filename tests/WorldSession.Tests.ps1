$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\UI\UIManager.cs') -Raw -Encoding UTF8
$panels = @('ExtremeStorageUI', 'SuperVaultUI', 'PlayerStatsUI', 'BuffAndSpawnRateUI', 'StructureLocatorUI', 'TravelShopRefreshUI')
$panelStubs = ($panels | ForEach-Object { @"
public static class $_ {
    public const string PanelId = "$_";
    public static bool IsOpen = true;
    public static bool Visible = true;
    public static float SpawnRateMultiplier = 1f;
    public static void Initialize() {}
    public static void Draw() { if (IsOpen) Counter.Draws++; }
    public static void Update() { if (!IsOpen) TerrariaModder.Core.UI.UIRenderer.UnregisterPanelBounds(PanelId); }
    public static void ResetSession() { IsOpen = false; Counter.Resets++; }
}
"@ }) -join "`n"
$harness = @'
namespace HarmonyLib { public class HarmonyPatch : System.Attribute { public HarmonyPatch(System.Type t, string s) {} } }
namespace Terraria {
    public class Player { public bool active = true, mouseInterface; }
    public class NPC {}
    public static class Main { public static bool gameMenu, dedServ, blockMouse; public static Player LocalPlayer = new Player(); }
}
namespace QualityOfTerraria {
    public static class Mod { public static bool WorldActive = true; }
    public static class WandsAndTools {
        public static bool QuickPlaceActive = true;
        public static void UpdateQuickPlace() {} public static void UpdateLiquidWand() {}
        public static void DrawHUD() { if (QuickPlaceActive) UI.Counter.Draws++; }
        public static void ResetSession() { QuickPlaceActive = false; }
    }
}
namespace QualityOfTerraria.Features {
    public static class Autofisher { public static void Update() {} public static void ResetSession() {} }
    public static class StructureLocator { public static void Update() {} public static void ResetSession() {} }
    public static class StructureMapMarker { public static void EnsureRegistered() {} }
    public static class TargetDummySystem { public static void Update() {} public static void DrawHUD() {} public static void ResetSession() {} }
}
namespace QualityOfTerraria.Patches {
    public static class WorldQoLCompletionPatches { public static void ResetSession() {} }
}
namespace TerrariaModder.Core.UI {
    public static class UIRenderer {
        public static System.Collections.Generic.Dictionary<string, System.Action> Draws = new System.Collections.Generic.Dictionary<string, System.Action>();
        public static System.Collections.Generic.HashSet<string> Bounds = new System.Collections.Generic.HashSet<string>();
        public static bool IsBlocking { get { return Bounds.Count > 0; } }
        public static void RegisterPanelDraw(string id, System.Action draw) { Draws[id] = draw; }
        public static void UnregisterPanelDraw(string id) { Draws.Remove(id); }
        public static void UnregisterPanelBounds(string id) { Bounds.Remove(id); if (Bounds.Count == 0) { Terraria.Main.blockMouse = false; Terraria.Main.LocalPlayer.mouseInterface = false; } }
        public static void ClearAllPanelBounds() { Bounds.Clear(); }
    }
}
namespace QualityOfTerraria.UI {
    public static class Counter { public static int Draws, Resets; }
    __PANELS__
    public static class WorldSessionRegression {
        private static int failures;
        private static void Check(bool passed, string label) {
            System.Console.WriteLine((passed ? "PASS " : "FAIL ") + label);
            if (!passed) failures++;
        }
        private static void DrawAll() { foreach (var draw in TerrariaModder.Core.UI.UIRenderer.Draws.Values) draw(); }
        public static void Run() {
            UIManager.Initialize();
            Terraria.Main.gameMenu = true;
            DrawAll();
            Check(Counter.Draws == 0, "主菜单不绘制功能窗口及HUD");
            Counter.Draws = 0; Terraria.Main.gameMenu = false; Mod.WorldActive = false;
            DrawAll();
            Check(Counter.Draws == 0, "未加载世界不绘制功能窗口");
            Counter.Draws = 0; Mod.WorldActive = true;
            DrawAll();
            Check(Counter.Draws == 7, "有效世界绘制功能窗口、旅商入口和工具HUD");
            Terraria.Main.dedServ = true; Counter.Resets = 0; Counter.Draws = 0;
            UIManager.UpdateAll(); DrawAll();
            Check(Counter.Resets == 0 && Counter.Draws == 0, "专服不绘制且保留快照服务会话");
            Terraria.Main.dedServ = false;
            UIManager.ResetSession();
            Terraria.Main.LocalPlayer.mouseInterface = true;
            Terraria.Main.blockMouse = true;
            UIManager.UpdateAll();
            Check(Terraria.Main.LocalPlayer.mouseInterface && Terraria.Main.blockMouse, "关闭的模组面板不能清除原版界面已有的鼠标保护");
            Terraria.Main.LocalPlayer.mouseInterface = false;
            Terraria.Main.blockMouse = false;
            UIManager.UpdateAll();
            Check(!Terraria.Main.LocalPlayer.mouseInterface && !Terraria.Main.blockMouse, "普通世界操作不凭空新增界面保护");
            TerrariaModder.Core.UI.UIRenderer.Bounds.Add("OtherMod.Panel");
            var reset = typeof(UIManager).GetMethod("ResetSession");
            if (reset != null) reset.Invoke(null, null);
            Check(!ExtremeStorageUI.IsOpen && !SuperVaultUI.IsOpen && !PlayerStatsUI.IsOpen
                && !BuffAndSpawnRateUI.IsOpen && !StructureLocatorUI.IsOpen && !WandsAndTools.QuickPlaceActive,
                "退出时关闭全部窗口并清理工具状态");
            UIManager.UpdateAll();
            Check(TerrariaModder.Core.UI.UIRenderer.Bounds.Contains("OtherMod.Panel"), "会话清理保留其他模组的UI边界");
            TerrariaModder.Core.UI.UIRenderer.Bounds.Add(ExtremeStorageUI.PanelId);
            TerrariaModder.Core.UI.UIRenderer.Bounds.Add(SuperVaultUI.PanelId);
            TerrariaModder.Core.UI.UIRenderer.Bounds.Add(PlayerStatsUI.PanelId);
            PlayerStatsUI.IsOpen = true;
            var clearClosed = typeof(UIManager).GetMethod("ClearClosedPanelBounds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Check(clearClosed != null, "提供清理已关闭面板边界的入口");
            if (clearClosed != null) {
                clearClosed.Invoke(null, null);
                Check(!TerrariaModder.Core.UI.UIRenderer.Bounds.Contains(ExtremeStorageUI.PanelId)
                    && !TerrariaModder.Core.UI.UIRenderer.Bounds.Contains(SuperVaultUI.PanelId)
                    && TerrariaModder.Core.UI.UIRenderer.Bounds.Contains(PlayerStatsUI.PanelId)
                    && TerrariaModder.Core.UI.UIRenderer.Bounds.Contains("OtherMod.Panel"), "只移除关闭的本模组面板边界，保留其他模组边界");
            }
            PlayerStatsUI.IsOpen = false;
            SuperVaultUI.IsOpen = true;
            Terraria.Main.gameMenu = true;
            UIManager.UpdateAll();
            Check(!SuperVaultUI.IsOpen, "返回菜单的兜底更新关闭残留窗口");
            UIManager.Unload();
            Check(TerrariaModder.Core.UI.UIRenderer.Draws.Count == 0, "卸载移除全部本模组绘制回调");
            UIManager.Initialize();
            Check(TerrariaModder.Core.UI.UIRenderer.Draws.Count == 7, "重载只注册一份绘制回调");
            if (failures > 0) throw new System.Exception(failures + " 项生命周期回归失败");
        }
    }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $harness.Replace('__PANELS__', $panelStubs))
[QualityOfTerraria.UI.WorldSessionRegression]::Run()
