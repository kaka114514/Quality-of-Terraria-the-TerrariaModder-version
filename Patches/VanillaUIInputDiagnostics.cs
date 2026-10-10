using System;
using System.Collections.Generic;
using System.Reflection;
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
                $"inventory={Main.playerInventory} options={Main.ingameOptionsWindow}");
        }

        internal static void ResetSession() { _lastClick = 0; _samples = 0; }
    }
}
