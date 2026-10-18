using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.Patches
{
    [HarmonyPatch(typeof(Main), "DrawInterface_0_InterfaceLogic1")]
    internal static class WorldUIClickPatch
    {
        private static bool _initialized, _wasDown, _pressed;

        public static void Prefix()
        {
            _pressed = false;
            if (Main.gameMenu || Main.dedServ || !Mod.WorldActive) { _initialized = false; return; }
            bool down = Mouse.GetState().LeftButton == ButtonState.Pressed;
            if (!_initialized) { _wasDown = down; _initialized = true; }
            _pressed = down && !_wasDown;
            _wasDown = down;

            if (Main.playerInventory || Main.ingameOptionsWindow)
            {
                Player player = Main.LocalPlayer;
                bool mouseInterface = player.mouseInterface, blockMouse = Main.blockMouse;
                UI.UIManager.ClearClosedPanelBounds();
                player.mouseInterface |= mouseInterface;
                Main.blockMouse |= blockMouse;
            }
        }

        public static void Postfix()
        {
            // 原版鼠标槽仍处于使用／重用物品时会清掉release；只给可见UI恢复真实的新按下边沿。
            if (!_pressed || Main.gameMenu || Main.dedServ || !Mod.WorldActive) return;
            if (!Main.playerInventory && !Main.ingameOptionsWindow && !ModMenu.IsVisible &&
                !UI.ExtremeStorageUI.IsOpen && !UI.SuperVaultUI.IsOpen && !UI.PlayerStatsUI.IsOpen &&
                !UI.BuffAndSpawnRateUI.IsOpen && !UI.StructureLocatorUI.IsOpen) return;
            Main.mouseLeft = true;
            Main.mouseLeftRelease = true;
        }

        internal static void ResetSession() { _initialized = _wasDown = _pressed = false; }
    }
}
