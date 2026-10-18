using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.Patches
{
    // 原版UI有独立于加载器点击getter的闸门，保留现场状态以定位游戏内交互问题。
    [HarmonyPatch]
    internal static class VanillaUIInputDiagnostics
    {
        private static readonly FieldInfo RegisteredPanels = AccessTools.Field(typeof(UIRenderer), "_registeredPanels");
        private static ulong _lastClick;
        private static int _samples;

        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Main), "DrawInventory");
            yield return AccessTools.Method(typeof(Main), "DrawSettingButton");
            yield return AccessTools.Method(typeof(Terraria.IngameOptions), "Draw");
        }

        public static void Prefix(MethodBase __originalMethod, out bool __state)
        {
            __state = false;
            if (!Mod.WorldActive || Main.gameMenu || Main.dedServ || _samples >= 24) return;
            if (Mouse.GetState().LeftButton != ButtonState.Pressed || (_lastClick != 0 && Main.GameUpdateCount - _lastClick < 20)) return;
            _lastClick = Main.GameUpdateCount; _samples++; __state = true;
            Capture("before", __originalMethod.Name);
        }

        public static void Postfix(MethodBase __originalMethod, bool __state, bool __runOriginal)
        {
            if (__state) Capture(__runOriginal ? "after" : "skipped", __originalMethod.Name);
        }

        private static void Capture(string phase, string method)
        {
            Player player = Main.LocalPlayer;
            if (player == null) return;
            Mod.LogInfo($"[原版UI输入] {method}/{phase} left={Main.mouseLeft} release={Main.mouseLeftRelease} " +
                $"ignore={PlayerInput.IgnoreMouseInterface} animation={player.itemAnimation} time={player.itemTime} " +
                $"reuse={player.reuseDelay} channel={player.channel} pendingReuse={player.pendingItemReuse} " +
                $"mouseInterface={player.mouseInterface} blockMouse={Main.blockMouse} inventoryLock={StorageInventoryGuard.Locked} " +
                $"coreBlocking={UIRenderer.IsBlocking} coreHover={UIRenderer.IsMouseOverAnyPanel()} paused={Main.gamePaused} " +
                $"inventory={Main.playerInventory} options={Main.ingameOptionsWindow} hoveredPanels={DescribeHoveredPanels()}");
        }

        private static string DescribeHoveredPanels()
        {
            var panels = RegisteredPanels?.GetValue(null) as IDictionary;
            if (panels == null) return "unavailable";
            int mouseX = UIRenderer.MouseX, mouseY = UIRenderer.MouseY;
            var result = new StringBuilder();
            foreach (DictionaryEntry entry in panels)
            {
                object bounds = entry.Value;
                Type type = bounds.GetType();
                int x = (int)type.GetField("Item1").GetValue(bounds);
                int y = (int)type.GetField("Item2").GetValue(bounds);
                int width = (int)type.GetField("Item3").GetValue(bounds);
                int height = (int)type.GetField("Item4").GetValue(bounds);
                if (mouseX < x || mouseX >= x + width || mouseY < y || mouseY >= y + height) continue;
                if (result.Length > 0) result.Append(',');
                result.Append(entry.Key).Append('@').Append(x).Append(',').Append(y).Append(',').Append(width).Append(',').Append(height);
            }
            return result.Length == 0 ? "none" : result.ToString();
        }

        internal static void ResetSession() { _lastClick = 0; _samples = 0; }
    }
}
