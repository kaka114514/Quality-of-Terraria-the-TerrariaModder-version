using System;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core.UI;
using QualityOfTerraria.Networking;
using QualityOfTerraria.Patches;

namespace QualityOfTerraria.UI
{
    internal static class TravelShopRefreshUI
    {
        internal const string PanelId = "QualityOfTerraria.TravelShopRefresh";
        private static int[] _lastShop;

        private static bool ShopOpen => UIManager.CanRender && Main.instance != null && Main.playerInventory && Main.npcShop > 0 &&
            Main.npcShop < Main.instance.shop.Length && Main.LocalPlayer.talkNPC >= 0 && Main.LocalPlayer.talkNPC < Main.maxNPCs &&
            Main.npc[Main.LocalPlayer.talkNPC].active && Main.npc[Main.LocalPlayer.talkNPC].type == NPCID.TravellingMerchant;

        internal static void Update()
        {
            if (!ShopOpen) { _lastShop = null; UIRenderer.UnregisterPanelBounds(PanelId); return; }
            bool changed = _lastShop != null && _lastShop.Length == Main.travelShop.Length;
            if (changed)
            {
                changed = false;
                for (int i = 0; i < Main.travelShop.Length; i++) if (_lastShop[i] != Main.travelShop[i]) { changed = true; break; }
            }
            if (changed) Main.instance.shop[Main.npcShop].SetupShop(Main.npcShop);
            _lastShop = (int[])Main.travelShop.Clone();
        }

        internal static void Draw()
        {
            if (!ShopOpen || Mod.Config?.RefreshTravelShop != true) { UIRenderer.UnregisterPanelBounds(PanelId); return; }
            int width = Math.Min(220, PanelLayout.ScreenWidth - 32);
            int x = Math.Max(16, Math.Min(74, PanelLayout.ScreenWidth - width - 16));
            int y = Math.Max(16, Math.Min(Main.instance.invBottom + 282, PanelLayout.ScreenHeight - 38));
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + 28;
            UIRenderer.RegisterPanelBounds(PanelId, x, y, width, 28);
            if (hover) Main.LocalPlayer.mouseInterface = true;
            UIRenderer.DrawRect(x, y, width, 28, (byte)(hover ? 42 : 28), 65, 95, 245);
            UIRenderer.DrawRectOutline(x, y, width, 28, 85, 160, 220, 255, 1);
            PanelLayout.DrawFittedText(ToolNetwork.Busy ? "等待服务器确认…" : "刷新旅商商店", x + 8, y + 6, width - 16, 235, 240, 255);
            if (!hover || !UIRenderer.MouseLeftClick) return;
            UIRenderer.ConsumeClick();
            if (Main.netMode == 1) ToolNetwork.Request("travel-shop");
            else if (WorldFeatureActions.RefreshTravelShop())
            {
                Main.instance.shop[Main.npcShop].SetupShop(Main.npcShop);
                _lastShop = (int[])Main.travelShop.Clone();
                Main.NewText("旅商商店已刷新。", 150, 230, 255);
            }
        }

        internal static void ResetSession() { _lastShop = null; UIRenderer.UnregisterPanelBounds(PanelId); }
    }
}
