using System;
using HarmonyLib;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using TerrariaModder.Core.UI;
using QualityOfTerraria.Patches;

namespace QualityOfTerraria.UI
{
    internal static class QuickStorageUI
    {
        internal const string PanelId = "QualityOfTerraria.QuickStorage";
        internal static bool IsOpen { get; private set; }
        private static readonly string[] Names = { "猪猪钱罐", "保险箱", "护卫熔炉", "虚空袋" };
        private static readonly Action<Player, int, int, int> OpenContainer =
            (Action<Player, int, int, int>)Delegate.CreateDelegate(typeof(Action<Player, int, int, int>), AccessTools.Method(typeof(Player), "OpenChest"));
        private static Player _owner;
        private static int _bank = -1;
        private static int _x, _y, _width, _height;

        internal static void Toggle()
        {
            if (!UIManager.CanRender || Main.LocalPlayer.dead || StorageInventoryGuard.Locked) return;
            if (_owner == Main.LocalPlayer && _bank == Main.LocalPlayer.chest && _bank < -1)
            {
                Main.LocalPlayer.chest = -1;
                ResetSession();
                SoundEngine.PlaySound(SoundID.MenuClose);
                return;
            }
            IsOpen = !IsOpen;
            if (IsOpen) { Main.playerInventory = true; UIRenderer.BringToFront(PanelId); }
            else UIRenderer.UnregisterPanelBounds(PanelId);
        }

        internal static void OpenBank(int bank)
        {
            if (!UIManager.CanRender || Main.LocalPlayer.dead || StorageInventoryGuard.Locked || bank > -2 || bank < -5) return;
            Player player = Main.LocalPlayer;
            player.piggyBankProjTracker.Clear(); player.voidLensChest.Clear();
            player.SetTalkNPC(-1); Main.SetNPCShopIndex(0); Main.npcChatText = ""; Main.editChest = false;
            OpenContainer(player, (int)player.Center.X / 16, (int)player.Center.Y / 16, bank);
            _owner = player; _bank = bank;
            IsOpen = false;
            UIRenderer.UnregisterPanelBounds(PanelId);
            // 33号包的 number 是世界箱子索引。个人收纳只需释放原世界箱的占用。
            if (Main.netMode == 1) NetMessage.SendData(MessageID.SyncPlayerChest, -1, -1, null, -1);
            SoundEngine.PlaySound(SoundID.MenuOpen);
        }

        internal static bool KeepOpen(Player player)
        {
            if (Main.netMode == 2 || player != _owner || _bank == -1) return false;
            if (player != Main.LocalPlayer || !player.active || player.dead || !Main.playerInventory || Main.gameMenu || player.chest != _bank)
            {
                if (player.chest == _bank) player.chest = -1;
                _owner = null; _bank = -1;
                return false;
            }
            return true;
        }

        internal static void Update()
        {
            if (_owner != null) KeepOpen(_owner);
            if (!UIManager.CanRender || Main.LocalPlayer.dead || !Main.playerInventory) IsOpen = false;
            if (!IsOpen) { UIRenderer.UnregisterPanelBounds(PanelId); return; }
            _width = Math.Min(260, PanelLayout.ScreenWidth);
            _height = Math.Min(212, PanelLayout.ScreenHeight);
            _x = Math.Max(0, PanelLayout.ScreenWidth - _width - 24); _y = 100;
            PanelLayout.ClampToScreen(ref _x, ref _y, _width, _height);
            UIRenderer.RegisterPanelBounds(PanelId, _x, _y, _width, _height);
        }

        internal static void Draw()
        {
            if (!IsOpen || !UIManager.CanRender) return;
            int mx = UIRenderer.MouseX, my = UIRenderer.MouseY;
            if (mx >= _x && mx < _x + _width && my >= _y && my < _y + _height) Main.LocalPlayer.mouseInterface = true;
            UIRenderer.DrawPanel(_x, _y, _width, _height, 18, 22, 36, 240);
            PanelLayout.DrawFittedText("打开快捷收纳", _x + 12, _y + 8, _width - 50, 240, 240, 255);
            if (Button("×", _x + _width - 28, _y + 4, 24, 24)) { IsOpen = false; UIRenderer.UnregisterPanelBounds(PanelId); return; }
            int row = Math.Max(1, (_height - 44) / 4);
            for (int i = 0; i < Names.Length; i++)
                if (Button(Names[i], _x + 12, _y + 36 + i * row, _width - 24, row - 4)) { OpenBank(-2 - i); return; }
        }

        private static bool Button(string text, int x, int y, int width, int height)
        {
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + height;
            UIRenderer.DrawRect(x, y, width, height, (byte)(hover ? 45 : 28), 65, 95, 245);
            PanelLayout.DrawFittedText(text, x + 6, y + Math.Max(0, (height - 16) / 2), width - 12, 235, 240, 255);
            if (!hover || !UIRenderer.MouseLeftClick) return false;
            UIRenderer.ConsumeClick();
            return true;
        }

        internal static void ResetSession()
        {
            if (_owner != null && _owner.chest == _bank) _owner.chest = -1;
            _owner = null; _bank = -1; IsOpen = false;
            UIRenderer.UnregisterPanelBounds(PanelId);
        }
    }
}
