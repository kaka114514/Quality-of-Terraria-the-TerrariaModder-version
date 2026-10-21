using System;
using QualityOfTerraria.Features;
using Terraria;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.UI
{
    public static class UIManager
    {
        private static bool _initialized;
        public const string HudOverlayId = "QualityOfTerraria.HUDOverlay";
        public static bool CanRender => Mod.WorldActive && !Main.gameMenu && !Main.dedServ
            && Main.LocalPlayer != null && Main.LocalPlayer.active;

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            RegisterDraw(QuickStorageUI.PanelId, QuickStorageUI.Draw);
            RegisterDraw(ExtremeStorageUI.PanelId, ExtremeStorageUI.Draw);
            RegisterDraw(SuperVaultUI.PanelId, SuperVaultUI.Draw);
            RegisterDraw(PlayerStatsUI.PanelId, PlayerStatsUI.Draw);
            RegisterDraw(BuffAndSpawnRateUI.PanelId, BuffAndSpawnRateUI.Draw);
            RegisterDraw(StructureLocatorUI.PanelId, StructureLocatorUI.Draw);
            RegisterDraw(HudOverlayId, DrawHUDOverlay);
            RegisterDraw(TravelShopRefreshUI.PanelId, TravelShopRefreshUI.Draw);
            RegisterDraw(ReforgeRollbackUI.PanelId, ReforgeRollbackUI.Draw);
            SuperVaultUI.Initialize();
        }

        private static void RegisterDraw(string id, Action draw)
        {
            UIRenderer.RegisterPanelDraw(id, () => { if (CanRender) PreserveVanillaInput(draw); });
        }

        private static void PreserveVanillaInput(Action action)
        {
            Player player = Main.LocalPlayer;
            bool preserve = !UIRenderer.IsBlocking;
            bool mouseInterface = player.mouseInterface, blockMouse = Main.blockMouse;
            try { action(); }
            finally
            {
                // Core注销不存在／最后一个面板时会清全局输入状态；不能顺带清掉原版背包的悬停保护。
                if (preserve) { player.mouseInterface |= mouseInterface; Main.blockMouse |= blockMouse; }
            }
        }

        public static void UpdateAll()
        {
            // 专服不运行界面更新，但必须保留本世界的快照网络订阅。
            if (Main.dedServ && Mod.WorldActive) return;
            if (!CanRender)
            {
                ResetSession();
                return;
            }
            if (!_initialized) Initialize();
            PreserveVanillaInput(UpdatePanels);
        }

        internal static void ClearClosedPanelBounds()
        {
            if (!QuickStorageUI.IsOpen) UIRenderer.UnregisterPanelBounds(QuickStorageUI.PanelId);
            if (!ExtremeStorageUI.IsOpen) UIRenderer.UnregisterPanelBounds(ExtremeStorageUI.PanelId);
            if (!SuperVaultUI.IsOpen) UIRenderer.UnregisterPanelBounds(SuperVaultUI.PanelId);
            if (!PlayerStatsUI.IsOpen) UIRenderer.UnregisterPanelBounds(PlayerStatsUI.PanelId);
            if (!BuffAndSpawnRateUI.IsOpen) UIRenderer.UnregisterPanelBounds(BuffAndSpawnRateUI.PanelId);
            if (!StructureLocatorUI.IsOpen) UIRenderer.UnregisterPanelBounds(StructureLocatorUI.PanelId);
            if (!TravelShopRefreshUI.Visible) UIRenderer.UnregisterPanelBounds(TravelShopRefreshUI.PanelId);
            if (!ReforgeRollbackUI.Visible) UIRenderer.UnregisterPanelBounds(ReforgeRollbackUI.PanelId);
        }

        private static void UpdatePanels()
        {
            QuickStorageUI.Update();
            ExtremeStorageUI.Update();
            SuperVaultUI.Update();
            PlayerStatsUI.Update();
            BuffAndSpawnRateUI.Update();
            StructureLocatorUI.Update();
            TravelShopRefreshUI.Update();
            ReforgeRollbackUI.Update();
            StructureMapMarker.EnsureRegistered();
            StructureLocator.Update();
            WandsAndTools.UpdateQuickPlace();
            WandsAndTools.UpdateLiquidWand();
            Autofisher.Update();
            TargetDummySystem.Update();
        }

        public static void ResetSession()
        {
            QuickStorageUI.ResetSession();
            ExtremeStorageUI.ResetSession();
            SuperVaultUI.ResetSession();
            PlayerStatsUI.ResetSession();
            BuffAndSpawnRateUI.ResetSession();
            StructureLocatorUI.ResetSession();
            TravelShopRefreshUI.ResetSession();
            ReforgeRollbackUI.ResetSession();
            WandsAndTools.ResetSession();
            Autofisher.ResetSession();
            TargetDummySystem.ResetSession();
            StructureLocator.ResetSession();
            Patches.WorldQoLCompletionPatches.ResetSession();
            UIRenderer.UnregisterPanelBounds(HudOverlayId);
        }

        public static void Unload()
        {
            ResetSession();
            UIRenderer.UnregisterPanelDraw(QuickStorageUI.PanelId);
            UIRenderer.UnregisterPanelDraw(ExtremeStorageUI.PanelId);
            UIRenderer.UnregisterPanelDraw(SuperVaultUI.PanelId);
            UIRenderer.UnregisterPanelDraw(PlayerStatsUI.PanelId);
            UIRenderer.UnregisterPanelDraw(BuffAndSpawnRateUI.PanelId);
            UIRenderer.UnregisterPanelDraw(StructureLocatorUI.PanelId);
            UIRenderer.UnregisterPanelDraw(HudOverlayId);
            UIRenderer.UnregisterPanelDraw(TravelShopRefreshUI.PanelId);
            UIRenderer.UnregisterPanelDraw(ReforgeRollbackUI.PanelId);
            _initialized = false;
        }

        private static void DrawHUDOverlay()
        {
            WandsAndTools.DrawHUD();
            TargetDummySystem.DrawHUD();
        }
    }
}
