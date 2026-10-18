using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.UI
{
    public class SuperVaultUI
    {
        public const string PanelId = "QualityOfTerraria.SuperVault";

        public static bool IsOpen { get; set; } = false;

        public static int PanelX = 260;
        public static int PanelY = 160;
        public static int PanelWidth = 560;
        public static int PanelHeight = 620;

        private static bool _isDragging = false;
        private static int _dragOffsetX = 0;
        private static int _dragOffsetY = 0;

        public static Item[] VaultItems = new Item[100];
        private static string _loadedPlayerName = null;
        private static string _loadedPath;
        private static bool _canSave;
        public static bool CanStoreItems => _loadedPath != null && _canSave;

        private static void AdjustLayout()
        {
            PanelWidth = Math.Min(560, PanelLayout.ScreenWidth);
            PanelHeight = Math.Min(620, PanelLayout.ScreenHeight);
            PanelLayout.ClampToScreen(ref PanelX, ref PanelY, PanelWidth, PanelHeight);
        }

        public static void ResetSession()
        {
            IsOpen = false;
            _isDragging = false;
            _loadedPlayerName = null;
            _loadedPath = null;
            _canSave = false;
            for (int i = 0; i < VaultItems.Length; i++) VaultItems[i] = new Item();
            UIRenderer.UnregisterPanelBounds(PanelId);
        }

        public static void EnsureLoadedForCurrentPlayer()
        {
            if (Main.ActivePlayerFileData == null || string.IsNullOrEmpty(Main.ActivePlayerFileData.Path)) return;
            string curName = StoragePersistence.Identity((Main.ActivePlayerFileData.IsCloudSave ? "cloud:" : "local:") + Main.ActivePlayerFileData.Path.ToUpperInvariant());
            if (_loadedPlayerName != curName)
            {
                for (int i = 0; i < VaultItems.Length; i++)
                {
                    VaultItems[i] = new Item();
                }
                _loadedPlayerName = curName;
                _loadedPath = Path.Combine(Main.SavePath, "TerrariaModder", "player-data", curName + ".supervault.dat");
                LoadVaultData();
            }
        }

        internal static void Initialize()
        {
            EnsureLoadedForCurrentPlayer();
        }

        public static void Toggle()
        {
            if (!UIManager.CanRender) return;
            EnsureLoadedForCurrentPlayer();
            IsOpen = !IsOpen;
            if (IsOpen)
            {
                AdjustLayout();
                UIRenderer.BringToFront(PanelId);
                SoundEngine.PlaySound(SoundID.MenuOpen);
            }
            else
            {
                SaveVaultData();
                UIRenderer.UnregisterPanelBounds(PanelId);
                SoundEngine.PlaySound(SoundID.MenuClose);
            }
        }

        public static void Update()
        {
            if (!IsOpen)
            {
                _isDragging = false;
                UIRenderer.UnregisterPanelBounds(PanelId);
                return;
            }

            AdjustLayout();
            UIRenderer.RegisterPanelBounds(PanelId, PanelX, PanelY, PanelWidth, PanelHeight);
        }

        public static void Draw()
        {
            if (!UIManager.CanRender || !IsOpen)
                return;

            Initialize();

            if (Patches.StorageInventoryGuard.Locked)
            {
                UIRenderer.DrawPanel(PanelX, PanelY, PanelWidth, 64, 18, 22, 36, 240);
                PanelLayout.DrawFittedText("物品操作等待确认：" + Networking.RemoteStorageOperations.Status, PanelX + 12, PanelY + 24, PanelWidth - 24, 255, 220, 120);
                return;
            }

            int mouseX = UIRenderer.MouseX;
            int mouseY = UIRenderer.MouseY;
            if (mouseX >= PanelX && mouseX <= PanelX + PanelWidth && mouseY >= PanelY && mouseY <= PanelY + PanelHeight)
                Main.LocalPlayer.mouseInterface = true;

            // 拖动处理
            bool headerHover = mouseX >= PanelX && mouseX <= PanelX + PanelWidth - 36 && mouseY >= PanelY && mouseY <= PanelY + 36;
            if (_isDragging)
            {
                Main.player[Main.myPlayer].mouseInterface = true;
                if (Main.mouseLeft)
                {
                    PanelX = Math.Max(0, Math.Min(PanelLayout.ScreenWidth - PanelWidth, mouseX - _dragOffsetX));
                    PanelY = Math.Max(0, Math.Min(PanelLayout.ScreenHeight - PanelHeight, mouseY - _dragOffsetY));
                }
                else
                {
                    _isDragging = false;
                }
            }
            else if ((UIRenderer.MouseLeftClick || (Main.mouseLeft && Main.mouseLeftRelease)) && headerHover)
            {
                _isDragging = true;
                _dragOffsetX = mouseX - PanelX;
                _dragOffsetY = mouseY - PanelY;
                UIRenderer.ConsumeClick();
            }

            AdjustLayout();
            UIRenderer.RegisterPanelBounds(PanelId, PanelX, PanelY, PanelWidth, PanelHeight);

            // 1. 底板与边框
            UIRenderer.DrawPanel(PanelX, PanelY, PanelWidth, PanelHeight, 18, 26, 42, 245);
            UIRenderer.DrawRectOutline(PanelX, PanelY, PanelWidth, PanelHeight, 50, 150, 220, 255, 2);

            // 2. 标题栏
            UIRenderer.DrawRect(PanelX, PanelY, PanelWidth, 36, 28, 48, 76, 255);
            PanelLayout.DrawFittedText("随身大背包 · 100格", PanelX + 16, PanelY + 9, PanelWidth - 60, 255, 255, 255);

            // 关闭按钮
            int closeBtnX = PanelX + PanelWidth - 32;
            int closeBtnY = PanelY + 6;
            bool closeHover = mouseX >= closeBtnX && mouseX <= closeBtnX + 24 && mouseY >= closeBtnY && mouseY <= closeBtnY + 24;
            UIRenderer.DrawRect(closeBtnX, closeBtnY, 24, 24, (byte)(closeHover ? 220 : 60), 60, 60, 255);
            UIRenderer.DrawText("X", closeBtnX + 7, closeBtnY + 4, 255, 255, 255, 255);
            if (closeHover && UIRenderer.MouseLeftClick)
            {
                Toggle();
                UIRenderer.ConsumeClick();
                return;
            }

            // 3. 10x10 网格绘制 (水平居中: 23px边距, 垂直间距紧凑自然)
            if (!_canSave)
            {
                PanelLayout.DrawFittedText("存档无法恢复：暂停存取以保护原文件，请查看模组日志。", PanelX + 16, PanelY + 56, PanelWidth - 32, 255, 180, 100);
                return;
            }
            int startX = PanelX + 16;
            int startY = PanelY + 48;
            int cols = 10;
            int rows = 10;
            int spacing = 4;
            int slotSize = Math.Max(8, Math.Min(46, Math.Min((PanelWidth - 32) / 10 - spacing, (PanelHeight - 104) / 10 - spacing)));

            Item hoveredItem = null;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int index = r * cols + c;
                    int x = startX + c * (slotSize + spacing);
                    int y = startY + r * (slotSize + spacing);

                    bool hover = mouseX >= x && mouseX <= x + slotSize && mouseY >= y && mouseY <= y + slotSize;
                    if (hover)
                    {
                        Main.player[Main.myPlayer].mouseInterface = true;
                    }

                    UIRenderer.DrawRect(x, y, slotSize, slotSize, (byte)(hover ? 38 : 22), (byte)(hover ? 52 : 30), (byte)(hover ? 80 : 48), 255);
                    UIRenderer.DrawRectOutline(x, y, slotSize, slotSize, (byte)(hover ? 100 : 50), (byte)(hover ? 180 : 90), (byte)(hover ? 255 : 130), 255, 1);

                    Item item = VaultItems[index];
                    if (item != null && !item.IsAir)
                    {
                        StorageItemDrawing.DrawIcon(item, x + 3, y + 3, slotSize - 6, slotSize - 6);

                        if (item.stack > 1)
                        {
                            string stackStr = item.stack > 9999 ? "9999+" : item.stack.ToString();
                            UIRenderer.DrawTextSmall(stackStr, x + slotSize - 26, y + slotSize - 14, 255, 255, 200, 255);
                        }

                        if (hover)
                        {
                            hoveredItem = item;
                        }
                    }

                    if (hover && UIRenderer.MouseLeftClick)
                    {
                        // 场景 1: 手持一组物品点击槽位
                        if (Main.mouseItem != null && !Main.mouseItem.IsAir)
                        {
                            if (item == null || item.IsAir)
                            {
                                // 直接整组放入空槽
                                VaultItems[index] = Main.mouseItem.Clone();
                                Main.mouseItem.TurnToAir();
                                SoundEngine.PlaySound(SoundID.Grab);
                                SaveVaultData();
                            }
                            else if (item.type == Main.mouseItem.type && item.prefix == Main.mouseItem.prefix && item.stack < item.maxStack)
                            {
                                // 同类型物品合并堆叠
                                int space = item.maxStack - item.stack;
                                int toAdd = Math.Min(space, Main.mouseItem.stack);
                                item.stack += toAdd;
                                Main.mouseItem.stack -= toAdd;
                                if (Main.mouseItem.stack <= 0)
                                {
                                    Main.mouseItem.TurnToAir();
                                }
                                SoundEngine.PlaySound(SoundID.Grab);
                                SaveVaultData();
                            }
                            else
                            {
                                // 交换物品
                                Item temp = VaultItems[index];
                                VaultItems[index] = Main.mouseItem.Clone();
                                Main.mouseItem = temp;
                                SoundEngine.PlaySound(SoundID.Grab);
                                SaveVaultData();
                            }
                        }
                        // 场景 2: 光标为空点击非空槽位
                        else if (item != null && !item.IsAir)
                        {
                            bool shift = Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) || Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);
                            if (shift)
                            {
                                // Shift点击：快速存入玩家背包
                                Player player = Main.LocalPlayer;
                                Item remaining = player.GetItem(item.Clone(), GetItemSettings.PickupItemFromWorld);
                                if (remaining == null || remaining.IsAir)
                                {
                                    item.TurnToAir();
                                }
                                else
                                {
                                    item.stack = remaining.stack;
                                }
                                SoundEngine.PlaySound(SoundID.Grab);
                                SaveVaultData();
                            }
                            else
                            {
                                // 普通左键：抓取整组到鼠标光标
                                Main.mouseItem = item.Clone();
                                item.TurnToAir();
                                SoundEngine.PlaySound(SoundID.Grab);
                                SaveVaultData();
                            }
                        }
                        UIRenderer.ConsumeClick();
                    }
                }
            }

            // Tooltip
            if (hoveredItem != null)
            {
                int tipX = mouseX + 16;
                int tipY = mouseY + 16;
                string tipName = hoveredItem.AffixName();
                int tipW = Math.Min(PanelLayout.ScreenWidth, Math.Max(140, UIRenderer.MeasureText(tipName) + 20));
                int tipH = Math.Min(36, PanelLayout.ScreenHeight);
                PanelLayout.ClampToScreen(ref tipX, ref tipY, tipW, tipH);

                UIRenderer.DrawPanel(tipX, tipY, tipW, tipH, 10, 14, 26, 245);
                UIRenderer.DrawRectOutline(tipX, tipY, tipW, tipH, 80, 160, 255, 255, 1);
                PanelLayout.DrawFittedText(tipName, tipX + 10, tipY + 8, tipW - 20, 255, 230, 140);
            }

            // 4. 底部工具栏 (与第10行间距 12px，彻底消除重叠)
            int btnY = PanelY + PanelHeight - 46;
            int buttonWidth = (PanelWidth - 40) / 3;
            DrawButton(PanelX + 12, btnY, buttonWidth, 32, "存入大背包", () =>
            {
                DepositInventoryToVault();
            });

            DrawButton(PanelX + 20 + buttonWidth, btnY, buttonWidth, 32, "全部取出", () =>
            {
                LootVaultToInventory();
            });

            DrawButton(PanelX + 28 + buttonWidth * 2, btnY, buttonWidth, 32, "智能整理", () =>
            {
                SortVault();
            });

            // 鼠标手持物品点击面板任意空白处快速存入
            if (Main.mouseItem != null && !Main.mouseItem.IsAir && UIRenderer.MouseLeftClick)
            {
                bool inPanel = mouseX >= PanelX && mouseX <= PanelX + PanelWidth && mouseY >= PanelY && mouseY <= PanelY + PanelHeight;
                if (inPanel)
                {
                    if (DepositItem(Main.mouseItem))
                    {
                        SoundEngine.PlaySound(SoundID.Grab);
                    }
                    UIRenderer.ConsumeClick();
                }
            }
        }

        private static void DrawButton(int x, int y, int w, int h, string text, Action onClick)
        {
            int mouseX = UIRenderer.MouseX;
            int mouseY = UIRenderer.MouseY;
            bool hover = mouseX >= x && mouseX <= x + w && mouseY >= y && mouseY <= y + h;
            if (hover)
            {
                Main.player[Main.myPlayer].mouseInterface = true;
            }

            byte r = hover ? (byte)45 : (byte)28;
            byte g = hover ? (byte)85 : (byte)46;
            byte b = hover ? (byte)150 : (byte)80;

            UIRenderer.DrawRect(x, y, w, h, r, g, b, 255);
            UIRenderer.DrawRectOutline(x, y, w, h, (byte)(hover ? 120 : 60), (byte)(hover ? 200 : 100), 255, 255, 1);
            PanelLayout.DrawFittedText(text, x + 10, y + 8, w - 20, 240, 240, 240);

            if (hover && UIRenderer.MouseLeftClick)
            {
                onClick?.Invoke();
                UIRenderer.ConsumeClick();
            }
        }

        public static bool DepositItem(Item item)
        {
            if (!_canSave || item == null || item.IsAir || item.stack <= 0)
                return false;

            int originalStack = item.stack;

            // 1. 优先寻找已有同类物品堆叠
            for (int v = 0; v < VaultItems.Length; v++)
            {
                Item vaultItem = VaultItems[v];
                if (vaultItem != null && !vaultItem.IsAir && vaultItem.type == item.type && vaultItem.prefix == item.prefix && vaultItem.stack < vaultItem.maxStack)
                {
                    int canAdd = vaultItem.maxStack - vaultItem.stack;
                    int toAdd = Math.Min(item.stack, canAdd);
                    vaultItem.stack += toAdd;
                    item.stack -= toAdd;
                    if (item.stack <= 0)
                    {
                        item.TurnToAir();
                        SaveVaultData();
                        return true;
                    }
                }
            }

            // 2. 放入第一个空槽
            if (!item.IsAir && item.stack > 0)
            {
                for (int v = 0; v < VaultItems.Length; v++)
                {
                    if (VaultItems[v] == null || VaultItems[v].IsAir)
                    {
                        VaultItems[v] = item.Clone();
                        item.TurnToAir();
                        SaveVaultData();
                        return true;
                    }
                }
            }

            if (item.stack < originalStack)
            {
                SaveVaultData();
                return true;
            }

            return false;
        }

        private static void DepositInventoryToVault()
        {
            Player player = Main.LocalPlayer;
            bool any = false;
            for (int i = 0; i < 50; i++)
            {
                Item item = player.inventory[i];
                if (item != null && !item.IsAir && !item.favorited)
                {
                    if (DepositItem(item))
                    {
                        any = true;
                    }
                }
            }
            if (any)
            {
                SoundEngine.PlaySound(SoundID.Grab);
                SaveVaultData();
            }
        }

        private static void LootVaultToInventory()
        {
            Player player = Main.LocalPlayer;
            for (int v = 0; v < VaultItems.Length; v++)
            {
                Item item = VaultItems[v];
                if (item != null && !item.IsAir)
                {
                    Item remaining = player.GetItem(item.Clone(), GetItemSettings.PickupItemFromWorld);
                    if (remaining == null || remaining.IsAir)
                    {
                        item.TurnToAir();
                    }
                    else
                    {
                        item.stack = remaining.stack;
                    }
                }
            }
            SoundEngine.PlaySound(SoundID.Grab);
            SaveVaultData();
        }

        private static void SortVault()
        {
            Array.Sort(VaultItems, (a, b) =>
            {
                if (a == null || a.IsAir) return 1;
                if (b == null || b.IsAir) return -1;
                if (a.rare != b.rare) return b.rare.CompareTo(a.rare);
                return a.type.CompareTo(b.type);
            });
            SoundEngine.PlaySound(SoundID.MenuTick);
            SaveVaultData();
        }

        public static void SaveVaultData()
        {
            if (_loadedPath == null || !_canSave) return;
            try
            {
                StoragePersistence.Write(_loadedPath, writer =>
                {
                    writer.Write(VaultItems.Length);
                    foreach (Item item in VaultItems)
                    {
                        bool empty = item == null || item.IsAir;
                        writer.Write(empty ? 0 : item.type);
                        writer.Write(empty ? 0 : item.stack);
                        writer.Write(empty ? (byte)0 : item.prefix);
                    }
                });
            }
            catch (Exception error) { Mod.LogError("保存大背包失败：" + _loadedPath, error); }
        }

        public static void LoadVaultData()
        {
            if (_loadedPath == null) return;
            string legacy = Path.Combine(Path.GetDirectoryName(_loadedPath), Main.LocalPlayer.name + ".supervault.dat");
            string source = File.Exists(_loadedPath) || File.Exists(_loadedPath + ".bak") ? _loadedPath : legacy;
            _canSave = !File.Exists(source) && !File.Exists(source + ".bak");
            Item[] loaded;
            if (StoragePersistence.Read(source, reader =>
            {
                int count = reader.ReadInt32();
                if (count != 100) throw new InvalidDataException("大背包格数不合法");
                Item[] result = new Item[count];
                for (int i = 0; i < count; i++)
                {
                    int type = reader.ReadInt32();
                    int stack = reader.ReadInt32();
                    byte prefix = reader.ReadByte();
                    if (type < 0 || type >= ItemID.Count || stack < 0 || (type == 0 && stack != 0))
                        throw new InvalidDataException("大背包物品记录不合法");
                    Item item = new Item();
                    if (type > 0 && stack > 0)
                    {
                        item.SetDefaults(type);
                        item.Prefix(prefix);
                        item.stack = stack;
                    }
                    result[i] = item;
                }
                return result;
            }, out loaded))
            {
                VaultItems = loaded;
                _canSave = true;
                if (source != _loadedPath)
                {
                    SaveVaultData();
                    if (File.Exists(_loadedPath))
                    {
                        try
                        {
                            File.Move(source, source + ".migrated");
                            if (File.Exists(source + ".bak")) File.Move(source + ".bak", source + ".bak.migrated");
                        }
                        catch (Exception error) { Mod.LogError("保留旧大背包存档失败", error); }
                    }
                }
            }
        }
    }
}
