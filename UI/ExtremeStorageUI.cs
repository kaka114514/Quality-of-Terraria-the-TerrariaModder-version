using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using TerrariaModder.Core.UI;
using TerrariaModder.Core.UI.Widgets;
using QualityOfTerraria.Networking;

namespace QualityOfTerraria.UI
{
    public enum StorageTab
    {
        All,
        Weapons,
        Armor,
        Accessories,
        Tools,
        Ammo,
        Blocks,
        Alchemy,
        Furniture,
        ChestManager,
        Settings
    }

    public enum ItemSortMode
    {
        Default, // 默认箱子顺序
        Rarity,  // 稀有度从高到低
        Stack,   // 数量从多到少
        Name,    // 名称 A-Z
        ID,      // 物品ID
        Kind     // 物品种类
    }

    public class ExtremeStorageUI
    {
        public const string PanelId = "QualityOfTerraria.ExtremeStorage";

        public static bool IsOpen { get; set; } = false;

        public static int PanelX = 140;
        public static int PanelY = 80;
        public static int PanelWidth = 780;
        public static int PanelHeight = 580;

        private static bool _isDragging = false;
        private static int _dragOffsetX = 0;
        private static int _dragOffsetY = 0;
        private static bool _storageActionHeld;
        private static int _storageActionNeutralFrames;
        private static ulong _storageActionCooldownUntil;
        private const ulong StorageActionCooldownFrames = 5;

        public static StorageTab CurrentTab = StorageTab.All;
        public static ItemSortMode CurrentSortMode = ItemSortMode.Default;
        public static string SearchText = "";
        public static int CurrentPage = 0; // 当前页码 (0-indexed)
        public static int ChestManagerPage = 0; // 箱子管理页码

        // 标记过的箱子坐标哈希集合 ((long)chest.x << 32 | (long)(uint)chest.y)
        private static readonly HashSet<long> _markedChests = new HashSet<long>();

        private static readonly List<ItemSlotRef> _filteredSlots = new List<ItemSlotRef>();
        private static int _lastScanFrame = -1;

        public struct ItemSlotRef
        {
            public int ChestIndex;
            public int SlotIndex;
            public Item Item;
        }

        private static string _loadedWorldName = null;
        private static string _loadedPath;
        private static bool _canSave;
        private static readonly TextInput _search = new TextInput("搜索物品名称（支持中文）", 80) { KeyBlockId = PanelId + ".Search" };
        private static int _contentY = 110;
        private static int _searchY = 78;
        private static int _footerY = 510;
        private static int _categoryColumns = 9;
        private static int _categoryWidth = 79;
        private static int _categoryHeight = 26;
        private static int _gridColumns = 13;
        private static int _gridRows = 7;
        private static int _slotSize = 46;
        private static bool _compactLayout;
        private static bool IsItemTab => CurrentTab != StorageTab.ChestManager && CurrentTab != StorageTab.Settings;
        private static int GridColumns => _gridColumns;
        private static int GridRows => _gridRows;
        private static int PageSize => GridColumns * GridRows;

        private static void AdjustLayout()
        {
            PanelWidth = Math.Min(780, PanelLayout.ScreenWidth);
            PanelHeight = Math.Min(580, PanelLayout.ScreenHeight);
            _compactLayout = PanelHeight < 420;
            int width = PanelWidth - 32;
            _categoryColumns = Math.Max(1, Math.Min(9, (width + 4) / 56));
            _categoryWidth = (width - (_categoryColumns - 1) * 4) / _categoryColumns;
            _categoryHeight = _compactLayout ? 22 : 26;
            int categoryRows = (9 + _categoryColumns - 1) / _categoryColumns;
            _searchY = 42 + categoryRows * (_categoryHeight + 4) + 6;
            _contentY = _searchY + (IsItemTab ? (_compactLayout ? 24 : 28) + 4 : 0);
            _footerY = PanelHeight - (IsItemTab ? (_compactLayout ? 60 : 70) : 16);
            _gridColumns = Math.Max(1, Math.Min(13, (width + 4) / 52));
            int availableHeight = Math.Max(8, _footerY - _contentY - 4);
            _slotSize = Math.Max(8, Math.Min((width - (_gridColumns - 1) * 4) / _gridColumns, availableHeight));
            if (_slotSize < 40)
            {
                _gridColumns = Math.Max(1, (width + 4) / (_slotSize + 4));
            }
            _gridRows = Math.Max(1, Math.Min(7, (availableHeight + 4) / (_slotSize + 4)));
            CurrentPage = Math.Min(CurrentPage, Math.Max(0, (_filteredSlots.Count - 1) / PageSize));
            PanelLayout.ClampToScreen(ref PanelX, ref PanelY, PanelWidth, PanelHeight);
        }

        public static void ResetSession()
        {
            IsOpen = false;
            _isDragging = false;
            _storageActionHeld = false;
            _storageActionNeutralFrames = 0;
            _storageActionCooldownUntil = 0;
            _search.Unfocus();
            _search.Clear();
            SearchText = "";
            CurrentPage = ChestManagerPage = 0;
            _filteredSlots.Clear();
            _markedChests.Clear();
            _loadedWorldName = null;
            _loadedPath = null;
            _canSave = false;
            RemoteStorageSnapshots.ResetSession();
            UIRenderer.UnregisterPanelBounds(PanelId);
        }

        public static IEnumerable<long> GetMarkedCoordinates() => _markedChests;

        public static Chest GetChestForRead(int index)
        {
            if (Main.chest == null || index < 0 || index >= Main.maxChests) return null;
            if (Main.netMode != 1 || Main.LocalPlayer.chest == index) return Main.chest[index];
            return RemoteStorageSnapshots.GetChest(index);
        }

        internal static Chest GetChestMetadata(int index)
            => index < 0 || index >= Main.maxChests ? null : GetChestForRead(index) ?? Main.chest?[index];

        public static bool CanMutateChest(int index)
        {
            if (Main.chest == null || index < 0 || index >= Main.maxChests) return false;
            Chest chest = Main.chest[index];
            if (chest == null || Chest.IsLocked(chest.x, chest.y)) return false;
            int owner = Chest.UsingChest(index);
            if (owner >= 0 && owner != Main.myPlayer) return false;
            return Main.netMode != 1 || Main.LocalPlayer.chest == index;
        }

        internal static bool MatchesStorageTab(Item item, StorageTab tab) => MatchesTab(item, tab);
        internal static void SortStorageItems(Item[] items, ItemSortMode mode)
            => Array.Sort(items, (a, b) => CompareItems(a, b, mode == ItemSortMode.Default ? ItemSortMode.Rarity : mode));

        internal static void AcceptServerMarks(string world, string owner, IEnumerable<long> marks)
        {
            _loadedWorldName = world + "." + owner;
            string directory = Path.Combine(Main.SavePath, "TerrariaModder", "world-data");
            _loadedPath = Path.Combine(directory, _loadedWorldName + ".markedchests.dat");
            // 服务器按角色持久化；旧本地缓存不能复活已取消的标记。
            _markedChests.Clear();
            _canSave = true;
            _markedChests.UnionWith(marks);
            SaveMarkedChests();
            RemoteStorageSnapshots.Initialize();
            RefreshItems();
        }

        internal static void CloseAfterRemoteOpen()
        {
            IsOpen = false; _isDragging = false; _search.Unfocus();
            UIRenderer.UnregisterPanelBounds(PanelId);
        }

        internal static void RestoreRejectedMark(long key, bool marked)
        {
            if (marked) _markedChests.Add(key); else _markedChests.Remove(key);
            SaveMarkedChests();
        }

        public static IEnumerable<Chest> GetMarkedReadableChests()
        {
            EnsureLoadedForCurrentWorld();
            RemoteStorageSnapshots.Update();
            if (_markedChests.Count == 0) yield break;
            for (int c = 0; c < Main.maxChests; c++)
            {
                Chest chest = GetChestForRead(c);
                bool remoteSnapshot = Main.netMode == 1 && Main.LocalPlayer.chest != c;
                if (chest != null && IsChestMarked(chest) && (remoteSnapshot || !Chest.IsLocked(chest.x, chest.y)))
                {
                    int owner = Chest.UsingChest(c);
                    if (owner < 0 || owner == Main.myPlayer) yield return chest;
                }
            }
        }

        public static void EnsureLoadedForCurrentWorld()
        {
            if (Main.netMode == 1) return; // 联机世界身份和个人标记由服务器握手确认。
            if (Main.ActiveWorldFileData == null || Main.ActiveWorldFileData.UniqueId == Guid.Empty) return;
            string curWorld = Main.ActiveWorldFileData.UniqueId.ToString("N");
            if (_loadedWorldName != curWorld)
            {
                _loadedWorldName = curWorld;
                _loadedPath = Path.Combine(Main.SavePath, "TerrariaModder", "world-data", curWorld + ".markedchests.dat");
                LoadMarkedChests();
                RemoteStorageSnapshots.Initialize();
            }
        }

        public static void Toggle()
        {
            if (!UIManager.CanRender) return;
            EnsureLoadedForCurrentWorld();
            IsOpen = !IsOpen;
            if (IsOpen)
            {
                AdjustLayout();
                UIRenderer.BringToFront(PanelId);
                SoundEngine.PlaySound(SoundID.MenuOpen);
                CurrentPage = 0;
                RefreshItems();
            }
            else
            {
                _isDragging = false;
                _search.Unfocus();
                UIRenderer.UnregisterPanelBounds(PanelId);
                SoundEngine.PlaySound(SoundID.MenuClose);
            }
        }

        public static void Update()
        {
            RemoteStorageSnapshots.Update();
            if (!IsOpen)
            {
                _isDragging = false;
                _search.Unfocus();
                UIRenderer.UnregisterPanelBounds(PanelId);
                return;
            }

            AdjustLayout();
            if ((int)Main.GameUpdateCount - _lastScanFrame >= 60) RefreshItems();
            UIRenderer.RegisterPanelBounds(PanelId, PanelX, PanelY, PanelWidth, PanelHeight);
        }

        // ================= 箱子标记持久化与自动标记 =================

        public static void SaveMarkedChests()
        {
            if (_loadedPath == null || !_canSave) return;
            try
            {
                StoragePersistence.Write(_loadedPath, writer =>
                {
                    writer.Write(_markedChests.Count);
                    foreach (long key in _markedChests) writer.Write(key);
                });
            }
            catch (Exception error) { Mod.LogError("保存箱子标记失败：" + _loadedPath, error); }
        }

        public static void LoadMarkedChests()
        {
            _markedChests.Clear();
            if (_loadedPath == null) return;
            string legacy = Path.Combine(Path.GetDirectoryName(_loadedPath), Main.worldName + ".markedchests.dat");
            string source = Main.netMode == 1 || File.Exists(_loadedPath) || File.Exists(_loadedPath + ".bak") ? _loadedPath : legacy;
            _canSave = !File.Exists(source) && !File.Exists(source + ".bak");
            HashSet<long> loaded;
            if (StoragePersistence.Read(source, reader =>
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > Main.maxChests) throw new InvalidDataException("箱子标记数量不合法");
                var result = new HashSet<long>();
                for (int i = 0; i < count; i++)
                {
                    long key = reader.ReadInt64();
                    int x = (int)(key >> 32), y = (int)key;
                    if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                        throw new InvalidDataException("箱子标记坐标不合法");
                    result.Add(key);
                }
                return result;
            }, out loaded))
            {
                _markedChests.UnionWith(loaded);
                _canSave = true;
                if (source != _loadedPath)
                {
                    SaveMarkedChests();
                    if (File.Exists(_loadedPath))
                    {
                        try
                        {
                            File.Move(source, source + ".migrated");
                            if (File.Exists(source + ".bak")) File.Move(source + ".bak", source + ".bak.migrated");
                        }
                        catch (Exception error) { Mod.LogError("保留旧箱子标记失败", error); }
                    }
                }
            }
        }

        public static bool IsChestMarked(Chest chest)
        {
            EnsureLoadedForCurrentWorld();
            if (chest == null || chest.x < 0 || chest.y < 0) return false;
            long key = ((long)chest.x << 32) | (long)(uint)chest.y;
            return _markedChests.Contains(key);
        }

        public static void MarkChest(int chestIndex)
        {
            EnsureLoadedForCurrentWorld();
            if (chestIndex < 0 || chestIndex >= Main.maxChests || Main.chest == null)
                return;

            Chest chest = Main.chest[chestIndex];
            if (chest == null || chest.x < 0 || chest.y < 0)
                return;

            long key = ((long)chest.x << 32) | (long)(uint)chest.y;
            if (_markedChests.Add(key))
            {
                string name = !string.IsNullOrEmpty(chest.name) ? chest.name : $"箱子 #{chestIndex + 1}";
                Main.NewText($"【终极存储】已标记箱子：{name}", 120, 240, 255);
                SaveMarkedChests();
                RefreshItems();
            }
        }

        public static void ToggleMarkChest(Chest chest)
        {
            if (chest == null || chest.x < 0 || chest.y < 0) return;
            long key = ((long)chest.x << 32) | (long)(uint)chest.y;
            if (_markedChests.Contains(key))
            {
                _markedChests.Remove(key);
            }
            else
            {
                _markedChests.Add(key);
            }
            SaveMarkedChests();
            RefreshItems();
        }

        public static void MarkAllChests()
        {
            if (Main.chest == null) return;
            int count = 0;
            for (int c = 0; c < Main.maxChests; c++)
            {
                Chest chest = GetChestMetadata(c);
                if (chest != null && chest.x >= 0 && chest.y >= 0)
                {
                    long key = ((long)chest.x << 32) | (long)(uint)chest.y;
                    if (_markedChests.Add(key))
                    {
                        count++;
                    }
                }
            }
            SaveMarkedChests();
            RefreshItems();
            SoundEngine.PlaySound(SoundID.MenuOpen);
            Main.NewText($"【终极存储】已标记当前已发现的 {count} 个箱子。", 100, 255, 180);
        }

        public static void ClearAllMarkedChests()
        {
            _markedChests.Clear();
            SaveMarkedChests();
            RefreshItems();
            SoundEngine.PlaySound(SoundID.MenuClose);
            Main.NewText("【终极存储】已清除所有箱子标记。", 255, 180, 100);
        }

        public static void MarkNearbyChests(float tileRadius = 100f)
        {
            Player player = Main.LocalPlayer;
            if (player == null || Main.chest == null) return;

            int count = 0;
            float maxDist = tileRadius * 16f;
            for (int c = 0; c < Main.maxChests; c++)
            {
                Chest chest = GetChestMetadata(c);
                if (chest != null && chest.x >= 0 && chest.y >= 0)
                {
                    float dist = Vector2.Distance(player.Center, new Vector2(chest.x * 16, chest.y * 16));
                    if (dist <= maxDist)
                    {
                        long key = ((long)chest.x << 32) | (long)(uint)chest.y;
                        if (_markedChests.Add(key))
                        {
                            count++;
                        }
                    }
                }
            }
            SaveMarkedChests();
            RefreshItems();
            SoundEngine.PlaySound(SoundID.MenuOpen);
            Main.NewText($"【终极存储】已标记附近 {count} 个箱子！", 100, 255, 180);
        }

        // ================= 物品刷新与筛选排序 =================

        public static void RefreshItems()
        {
            _lastScanFrame = (int)Main.GameUpdateCount;
            _filteredSlots.Clear();

            Player player = Main.LocalPlayer;
            if (player == null || !player.active || Main.chest == null)
                return;

            // 仅遍历与聚合已标记的实体箱子
            for (int c = 0; c < Main.maxChests; c++)
            {
                Chest chest = GetChestForRead(c);
                if (chest == null || chest.item == null || chest.x < 0 || chest.y < 0)
                    continue;

                // 核心：只显示标记过的箱子！
                if (!IsChestMarked(chest))
                    continue;

                for (int s = 0; s < chest.item.Length; s++)
                {
                    Item item = chest.item[s];
                    if (item == null || item.IsAir || item.stack <= 0)
                        continue;

                    if (!MatchesTab(item, CurrentTab))
                        continue;

                    if (!MatchesSearch(item.AffixName(), SearchText))
                        continue;

                    _filteredSlots.Add(new ItemSlotRef
                    {
                        ChestIndex = c,
                        SlotIndex = s,
                        Item = item
                    });
                }
            }

            // 执行用户指定的排序方式
            ApplySorting();

            // 确保当前页码合法
            int pageSize = PageSize;
            int totalPages = Math.Max(1, (int)Math.Ceiling((double)_filteredSlots.Count / pageSize));
            if (CurrentPage >= totalPages) CurrentPage = totalPages - 1;
            if (CurrentPage < 0) CurrentPage = 0;
        }

        internal static bool MatchesSearch(string name, string search) => string.IsNullOrWhiteSpace(search) || name.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

        private static void ApplySorting()
        {
            if (CurrentSortMode != ItemSortMode.Default)
                _filteredSlots.Sort((a, b) => CompareItems(a.Item, b.Item, CurrentSortMode));
        }

        private static int CompareItems(Item a, Item b, ItemSortMode mode)
        {
            bool emptyA = a == null || a.IsAir;
            bool emptyB = b == null || b.IsAir;
            if (emptyA || emptyB) return emptyA == emptyB ? 0 : (emptyA ? 1 : -1);
            int result = 0;
            switch (mode)
            {
                case ItemSortMode.Kind:
                    result = GetItemKind(a).CompareTo(GetItemKind(b));
                    if (result == 0) result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    break;
                case ItemSortMode.Stack: result = b.stack.CompareTo(a.stack); break;
                case ItemSortMode.Name: result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); break;
                case ItemSortMode.ID: result = a.type.CompareTo(b.type); break;
                default: result = b.rare.CompareTo(a.rare); break;
            }
            if (result == 0) result = a.type.CompareTo(b.type);
            if (result == 0) result = a.prefix.CompareTo(b.prefix);
            if (result == 0) result = b.stack.CompareTo(a.stack);
            return result;
        }

        private static int GetItemKind(Item item)
        {
            if (MatchesTab(item, StorageTab.Accessories)) return 2;
            if (MatchesTab(item, StorageTab.Armor)) return 1;
            if (MatchesTab(item, StorageTab.Tools)) return 3;
            if (MatchesTab(item, StorageTab.Ammo)) return 4;
            if (MatchesTab(item, StorageTab.Weapons)) return 0;
            if (MatchesTab(item, StorageTab.Alchemy)) return 6;
            if (MatchesTab(item, StorageTab.Furniture)) return 7;
            if (MatchesTab(item, StorageTab.Blocks)) return 5;
            return 8;
        }

        private static bool MatchesTab(Item item, StorageTab tab)
        {
            if (item == null || item.IsAir) return false;

            switch (tab)
            {
                case StorageTab.All:
                    return true;
                case StorageTab.Weapons:
                    // 纯武器：有伤害且不是饰品、不是挖掘/伐木/锤/钓鱼工具
                    return item.damage > 0 && item.ammo == 0 && !item.accessory && item.pick == 0 && item.axe == 0 && item.hammer == 0 && item.fishingPole == 0;
                case StorageTab.Armor:
                    // 防具与时装 (非饰品)
                    return (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0 || item.vanity) && !item.accessory;
                case StorageTab.Accessories:
                    // 饰品
                    return item.accessory;
                case StorageTab.Tools:
                    // 工具：镐、斧、锤、钓竿、电线油漆工具、魔镜等
                    return item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.fishingPole > 0 || item.type == ItemID.WireCutter || item.type == ItemID.Wrench || item.type == ItemID.Paintbrush || item.type == ItemID.MagicMirror || item.type == ItemID.CellPhone;
                case StorageTab.Ammo:
                    return item.ammo > 0;
                case StorageTab.Blocks:
                    // 纯建筑方块与墙体 (排除家具)
                    return (item.createTile >= 0 || item.createWall > 0) && item.consumable && !IsFurniture(item);
                case StorageTab.Alchemy:
                    // 药水、增益、恢复、草药、种子
                    return item.buffType > 0 || item.healLife > 0 || item.healMana > 0 || item.bait > 0 ||
                           (item.createTile == TileID.ImmatureHerbs || item.createTile == TileID.MatureHerbs || item.createTile == TileID.BloomingHerbs);
                case StorageTab.Furniture:
                    return IsFurniture(item);
                case StorageTab.ChestManager:
                case StorageTab.Settings:
                    return false;
                default:
                    return true;
            }
        }

        private static bool IsFurniture(Item item)
        {
            if (item.createTile < 0) return false;
            int t = item.createTile;
            return t == TileID.WorkBenches || t == TileID.Tables || t == TileID.Chairs || t == TileID.Beds ||
                   t == TileID.ClosedDoor || t == TileID.OpenDoor || t == TileID.Torches || t == TileID.Campfire ||
                   t == TileID.Furnaces || t == TileID.Anvils || t == TileID.Bottles || t == TileID.Bookcases ||
                   t == TileID.Containers || t == TileID.PiggyBank || t == TileID.Safes || t == TileID.DefendersForge ||
                   t == TileID.GrandfatherClocks || t == TileID.Statues || t == TileID.Banners || t == TileID.Chandeliers ||
                   t == TileID.Lamps || t == TileID.HangingLanterns || t == TileID.Candles || t == TileID.Pianos ||
                   t == TileID.Dressers || t == TileID.Bathtubs || t == TileID.Sinks || t == TileID.Toilets ||
                   t == TileID.DemonAltar || t == TileID.CookingPots || t == TileID.Hellforge || t == TileID.MythrilAnvil ||
                   t == TileID.AdamantiteForge || t == TileID.TinkerersWorkbench || t == TileID.Autohammer ||
                   t == TileID.AlchemyTable || t == TileID.HeavyWorkBench;
        }

        // ================= 界面渲染与交互 =================

        public static void Draw()
        {
            if (!UIManager.CanRender || !IsOpen)
                return;

            UpdateStorageActionState();

            if (Patches.StorageInventoryGuard.Locked)
            {
                UIRenderer.DrawPanel(PanelX, PanelY, PanelWidth, 64, 18, 22, 36, 240);
                PanelLayout.DrawFittedText("物品操作等待确认：" + RemoteStorageOperations.Status, PanelX + 12, PanelY + 24, PanelWidth - 24, 255, 220, 120);
                return;
            }

            int mouseX = UIRenderer.MouseX;
            int mouseY = UIRenderer.MouseY;
            if (mouseX >= PanelX && mouseX <= PanelX + PanelWidth && mouseY >= PanelY && mouseY <= PanelY + PanelHeight)
                Main.LocalPlayer.mouseInterface = true;

            // 1. 窗口拖拽处理
            bool headerHover = mouseX >= PanelX && mouseX <= PanelX + PanelWidth - 184 && mouseY >= PanelY && mouseY <= PanelY + 36;
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

            // 2. 窗口底板与边框
            UIRenderer.DrawPanel(PanelX, PanelY, PanelWidth, PanelHeight, 18, 22, 36, 245);
            UIRenderer.DrawRectOutline(PanelX, PanelY, PanelWidth, PanelHeight, 60, 130, 220, 255, 2);

            // 3. 标题栏
            UIRenderer.DrawRect(PanelX, PanelY, PanelWidth, 36, 28, 38, 64, 255);
            PanelLayout.DrawFittedText("终极存储", PanelX + 16, PanelY + 9, PanelWidth - 200, 255, 255, 255);

            // 关闭按钮 (X)
            int closeBtnX = PanelX + PanelWidth - 32;
            int closeBtnY = PanelY + 6;
            bool closeHover = mouseX >= closeBtnX && mouseX <= closeBtnX + 24 && mouseY >= closeBtnY && mouseY <= closeBtnY + 24;
            UIRenderer.DrawRect(closeBtnX, closeBtnY, 24, 24, (byte)(closeHover ? 220 : 60), (byte)(closeHover ? 60 : 70), (byte)(closeHover ? 60 : 90), 255);
            UIRenderer.DrawText("X", closeBtnX + 7, closeBtnY + 4, 255, 255, 255, 255);
            if (closeHover && UIRenderer.MouseLeftClick)
            {
                Toggle();
                UIRenderer.ConsumeClick();
                return;
            }

            string[] views = { "箱子管理", "说明" };
            for (int i = 0; i < views.Length; i++)
            {
                StorageTab view = i == 0 ? StorageTab.ChestManager : StorageTab.Settings;
                int x = i == 0 ? PanelX + PanelWidth - 178 : PanelX + PanelWidth - 88;
                int width = i == 0 ? 84 : 48;
                if (DrawButton(views[i], x, PanelY + 5, width, 26, CurrentTab == view))
                {
                    CurrentTab = CurrentTab == view ? StorageTab.All : view;
                    CurrentPage = ChestManagerPage = 0;
                    _search.Unfocus();
                    AdjustLayout();
                    RefreshItems();
                    SoundEngine.PlaySound(SoundID.MenuTick);
                }
            }

            // 4. Tab 分类栏
            string[] tabNames = { "全部", "武器", "防具", "饰品", "工具", "弹药", "建筑", "药水", "家具" };

            for (int i = 0; i < tabNames.Length; i++)
            {
                StorageTab tab = (StorageTab)i;
                int tabX = PanelX + 16 + (i % _categoryColumns) * (_categoryWidth + 4);
                int tabY = PanelY + 42 + (i / _categoryColumns) * (_categoryHeight + 4);
                if (DrawButton(tabNames[i], tabX, tabY, _categoryWidth, _categoryHeight, CurrentTab == tab))
                {
                    CurrentTab = tab;
                    CurrentPage = 0;
                    AdjustLayout();
                    RefreshItems();
                    SoundEngine.PlaySound(SoundID.MenuTick);
                }
            }

            int searchY = PanelY + _searchY;
            if (IsItemTab)
            {
                int searchX = PanelX + 16;
                int rowHeight = _compactLayout ? 24 : 28;
                int sortWidth = PanelWidth < 500 ? 110 : 144;
                int sortX = PanelX + PanelWidth - 16 - sortWidth;
                int clearX = sortX - 6 - 44;
                int searchWidth = clearX - 6 - searchX;
                bool inSearch = mouseX >= searchX && mouseX <= searchX + searchWidth && mouseY >= searchY && mouseY <= searchY + rowHeight;
                if (UIRenderer.MouseLeftClick && !inSearch) _search.Unfocus();
                _search.Update();
                string value = _search.Draw(searchX, searchY, searchWidth, rowHeight);
                if (DrawButton("清空", clearX, searchY, 44, rowHeight))
                {
                    _search.Clear();
                    value = "";
                }
                if (value != SearchText) { SearchText = value; CurrentPage = 0; RefreshItems(); }
                if (DrawButton("顺序：" + GetSortModeDisplayName(CurrentSortMode), sortX, searchY, sortWidth, rowHeight))
                {
                    CurrentSortMode = CurrentSortMode == ItemSortMode.Default ? ItemSortMode.Kind
                        : CurrentSortMode == ItemSortMode.Kind ? ItemSortMode.Rarity
                        : CurrentSortMode == ItemSortMode.ID ? ItemSortMode.Default
                        : (ItemSortMode)((int)CurrentSortMode + 1);
                    CurrentPage = 0;
                    RefreshItems();
                    SoundEngine.PlaySound(SoundID.MenuTick);
                }
            }
            else _search.Unfocus();

            // 5. 内容区域
            if (CurrentTab == StorageTab.ChestManager)
            {
                DrawChestManagerPage(PanelX + 16, PanelY + _contentY);
            }
            else if (CurrentTab == StorageTab.Settings)
            {
                DrawSettingsPage(PanelX + 24, PanelY + _contentY);
            }
            else
            {
                DrawItemGrid(PanelX + 16, PanelY + _contentY);
            }

            // 6. 底部工具栏
            DrawBottomToolbar(PanelX + 16, PanelY + _footerY);
        }

        private static void DrawItemGrid(int startX, int startY)
        {
            int cols = GridColumns;
            int rows = GridRows;
            int spacing = 4;
            int slotSize = _slotSize;
            startX += (PanelWidth - 32 - (cols * slotSize + (cols - 1) * spacing)) / 2;
            int pageSize = cols * rows;

            int mouseX = UIRenderer.MouseX;
            int mouseY = UIRenderer.MouseY;

            // 若尚未标记任何箱子，给予明显指引
            if (_markedChests.Count == 0)
            {
                int availableHeight = _footerY - _contentY;
                string[] lines = availableHeight < 80
                    ? new[] { "尚未标记储物箱", "打开箱子，或在“箱子管理”中标记。" }
                    : new[] { "尚未标记储物箱", "在游戏中打开箱子即可自动标记。", "也可在“箱子管理”中选择标记。" };
                int lineHeight = Math.Min(28, (availableHeight - 8) / lines.Length);
                for (int i = 0; i < lines.Length; i++)
                    PanelLayout.DrawFittedText(lines[i], startX, startY + 4 + i * lineHeight,
                        PanelWidth - 32, (byte)(i == 0 ? 255 : 180), 220, (byte)(i == 0 ? 120 : 220));
                return;
            }

            int totalItems = _filteredSlots.Count;
            int startIndex = CurrentPage * pageSize;

            Item hoveredItem = null;
            ItemSlotRef hoveredSlotRef = default;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int index = startIndex + (r * cols + c);
                    int x = startX + c * (slotSize + spacing);
                    int y = startY + r * (slotSize + spacing);

                    bool hover = mouseX >= x && mouseX <= x + slotSize && mouseY >= y && mouseY <= y + slotSize;

                    // 槽位底色
                    UIRenderer.DrawRect(x, y, slotSize, slotSize, (byte)(hover ? 42 : 25), (byte)(hover ? 52 : 32), (byte)(hover ? 82 : 50), 255);
                    UIRenderer.DrawRectOutline(x, y, slotSize, slotSize, (byte)(hover ? 120 : 60), (byte)(hover ? 180 : 90), (byte)(hover ? 255 : 130), 255, 1);

                    if (index < totalItems)
                    {
                        ItemSlotRef slotRef = _filteredSlots[index];
                        Item item = slotRef.Item;

                        if (item != null && !item.IsAir)
                        {
                            // 绘制物品图标
                            StorageItemDrawing.DrawIcon(item, x + 3, y + 3, slotSize - 6, slotSize - 6);

                            // 绘制堆叠数量 (右下角阴影)
                            if (item.stack > 1)
                            {
                                string stackStr = item.stack > 9999 ? "9999+" : item.stack.ToString();
                                int textWidth = Math.Min(slotSize - 4, (int)(UIRenderer.MeasureText(stackStr) * 0.8f));
                                int textX = x + slotSize - textWidth - 2;
                                int textY = y + slotSize - 14;
                                PanelLayout.DrawFittedText(stackStr, textX + 1, textY + 1, textWidth, 0, 0, 0);
                                PanelLayout.DrawFittedText(stackStr, textX, textY, textWidth, 255, 255, 200);
                            }

                            if (hover)
                            {
                                Main.player[Main.myPlayer].mouseInterface = true;

                                // 手持一组物品点击：直接存入终极存储
                                if (Main.mouseItem != null && !Main.mouseItem.IsAir)
                                {
                                    if (UIRenderer.MouseLeftClick)
                                    {
                                        if (BeginStorageAction() && PutItemIntoAnyChest(Main.mouseItem))
                                        {
                                            ArmStorageActionCooldown();
                                            SoundEngine.PlaySound(SoundID.Grab);
                                            RefreshItems();
                                        }
                                        UIRenderer.ConsumeClick();
                                    }
                                }
                                else
                                {
                                    hoveredItem = item;
                                    hoveredSlotRef = slotRef;

                                    // 左键点击：取出到背包
                                    if (UIRenderer.MouseLeftClick)
                                    {
                                        if (BeginStorageAction() && TakeItemToPlayer(slotRef, takeAll: true))
                                            ArmStorageActionCooldown();
                                        UIRenderer.ConsumeClick();
                                    }
                                    // 右键点击：取出一半或单个
                                    else if (UIRenderer.MouseRightClick)
                                    {
                                        if (BeginStorageAction() && TakeItemToPlayer(slotRef, takeAll: false))
                                            ArmStorageActionCooldown();
                                        UIRenderer.ConsumeRightClick();
                                    }
                                }
                            }
                        }
                    }
                    else if (hover)
                    {
                        Main.player[Main.myPlayer].mouseInterface = true;
                        if (Main.mouseItem != null && !Main.mouseItem.IsAir && UIRenderer.MouseLeftClick)
                        {
                            if (BeginStorageAction() && PutItemIntoAnyChest(Main.mouseItem))
                            {
                                ArmStorageActionCooldown();
                                SoundEngine.PlaySound(SoundID.Grab);
                                RefreshItems();
                            }
                            UIRenderer.ConsumeClick();
                        }
                    }
                }
            }

            // 悬停 Tooltip 绘制 (带稀有度颜色与所属箱子)
            if (hoveredItem != null)
            {
                DrawItemTooltip(hoveredItem, hoveredSlotRef, mouseX, mouseY);
            }
        }

        // ================= 箱子管理页面 (ChestManager Tab) =================

        private static void DrawChestManagerPage(int startX, int startY)
        {
            int mouseX = UIRenderer.MouseX;
            int mouseY = UIRenderer.MouseY;

            // 收集世界中所有有效箱子
            List<int> validChests = new List<int>();
            if (Main.chest != null)
            {
                for (int c = 0; c < Main.maxChests; c++)
                {
                    Chest chest = GetChestMetadata(c);
                    if (chest != null && chest.x >= 0 && chest.y >= 0)
                    {
                        validChests.Add(c);
                    }
                }
            }

            // 顶部统计与快捷批量标记按钮
            int markedCount = 0;
            foreach (int c in validChests)
            {
                if (IsChestMarked(GetChestMetadata(c))) markedCount++;
            }

            string summaryText = $"已标记: {markedCount} / 共发现: {validChests.Count} 个世界储物箱";
            PanelLayout.DrawFittedText(summaryText, startX, startY + 4, PanelWidth - 32, 220, 220, 200);

            int btnW = (PanelWidth - 52) / 3;
            startY += 24;
            string[] actions = { "全部标记", "取消标记", "标记附近" };
            for (int i = 0; i < actions.Length; i++)
            {
                if (!DrawButton(actions[i], startX + i * (btnW + 10), startY + 4, btnW, 26)) continue;
                if (i == 0) MarkAllChests();
                else if (i == 1) ClearAllMarkedChests();
                else MarkNearbyChests(100f);
            }
            // 箱子列表分页
            int pageSize = Math.Max(1, Math.Min(8, (_footerY - _contentY - 94) / 44));
            int totalPages = Math.Max(1, (int)Math.Ceiling((double)validChests.Count / pageSize));
            if (ChestManagerPage >= totalPages) ChestManagerPage = totalPages - 1;
            if (ChestManagerPage < 0) ChestManagerPage = 0;

            int listStartY = startY + 36;
            int itemH = 40;
            int startIdx = ChestManagerPage * pageSize;

            for (int i = 0; i < pageSize; i++)
            {
                int idx = startIdx + i;
                if (idx >= validChests.Count) break;

                int cIndex = validChests[idx];
                Chest chest = GetChestMetadata(cIndex);
                int itemY = listStartY + i * (itemH + 4);
                int itemW = PanelWidth - 32;

                bool isMarked = IsChestMarked(chest);

                // 行底色
                UIRenderer.DrawRect(startX, itemY, itemW, itemH, 24, 30, 45, 220);
                UIRenderer.DrawRectOutline(startX, itemY, itemW, itemH, (byte)(isMarked ? 80 : 50), (byte)(isMarked ? 180 : 70), (byte)(isMarked ? 120 : 90), 255, 1);

                // 箱子名称与编号
                string chestTitle = !string.IsNullOrEmpty(chest.name) ? chest.name : $"储物箱 #{cIndex + 1}";
                PanelLayout.DrawFittedText(chestTitle, startX + 12, itemY + 4, itemW - 208,
                    (byte)(isMarked ? 255 : 200), (byte)(isMarked ? 255 : 200), (byte)(isMarked ? 180 : 200));

                // 箱子坐标与物品数
                int inChestCount = 0;
                for (int s = 0; s < chest.item.Length; s++)
                {
                    if (chest.item[s] != null && !chest.item[s].IsAir) inChestCount++;
                }
                string detail = GetChestForRead(cIndex) == null ? $"坐标: ({chest.x}, {chest.y}) | 内容尚未确认" : $"坐标: ({chest.x}, {chest.y}) | {inChestCount}/40 格";
                PanelLayout.DrawFittedText(detail, startX + 12, itemY + 22, itemW - 208, 160, 180, 200);

                // 标记开关按钮
                int tBtnX = startX + itemW - 130;
                int tBtnY = itemY + 6;
                int tBtnW = 110;
                int tBtnH = 28;
                if (DrawButton("打开", tBtnX - 66, tBtnY, 60, tBtnH, false, isMarked))
                    RemoteStorageOperations.RequestOpenChest(cIndex);

                bool tHover = mouseX >= tBtnX && mouseX <= tBtnX + tBtnW && mouseY >= tBtnY && mouseY <= tBtnY + tBtnH;
                byte tr = isMarked ? (byte)(tHover ? 35 : 20) : (byte)(tHover ? 50 : 30);
                byte tg = isMarked ? (byte)(tHover ? 110 : 80) : (byte)(tHover ? 50 : 30);
                byte tb = isMarked ? (byte)(tHover ? 55 : 35) : (byte)(tHover ? 60 : 40);

                UIRenderer.DrawRect(tBtnX, tBtnY, tBtnW, tBtnH, tr, tg, tb, 255);
                UIRenderer.DrawRectOutline(tBtnX, tBtnY, tBtnW, tBtnH, (byte)(isMarked ? 80 : 80), (byte)(isMarked ? 220 : 100), (byte)(isMarked ? 120 : 120), 255, 1);

                string btnText = isMarked ? "✓ 已标记" : "＋ 点击标记";
                UIRenderer.DrawTextSmall(btnText, tBtnX + (isMarked ? 24 : 18), tBtnY + 6, (byte)(isMarked ? 180 : 200), (byte)(isMarked ? 255 : 200), (byte)(isMarked ? 180 : 220), 255);

                if (tHover && UIRenderer.MouseLeftClick)
                {
                    ToggleMarkChest(chest);
                    UIRenderer.ConsumeClick();
                }
            }

            // 箱子管理翻页控制
            int cmPageY = listStartY + pageSize * (itemH + 4) + 6;
            int cmPrevX = startX + 20;
            int cmNextX = cmPrevX + 160;

            bool cmPrevH = mouseX >= cmPrevX && mouseX <= cmPrevX + 60 && mouseY >= cmPageY && mouseY <= cmPageY + 24;
            UIRenderer.DrawRect(cmPrevX, cmPageY, 60, 24, (byte)(cmPrevH ? 50 : 30), 40, 60, 255);
            UIRenderer.DrawRectOutline(cmPrevX, cmPageY, 60, 24, 80, 140, 200, 255, 1);
            UIRenderer.DrawTextSmall("◀ 上页", cmPrevX + 8, cmPageY + 4, 220, 240, 255, 255);
            if (cmPrevH && UIRenderer.MouseLeftClick)
            {
                ChestManagerPage = Math.Max(0, ChestManagerPage - 1);
                UIRenderer.ConsumeClick();
            }

            string cmPageStr = $"{ChestManagerPage + 1} / {totalPages} 页";
            UIRenderer.DrawTextSmall(cmPageStr, cmPrevX + 76, cmPageY + 4, 255, 230, 150, 255);

            bool cmNextH = mouseX >= cmNextX && mouseX <= cmNextX + 60 && mouseY >= cmPageY && mouseY <= cmPageY + 24;
            UIRenderer.DrawRect(cmNextX, cmPageY, 60, 24, (byte)(cmNextH ? 50 : 30), 40, 60, 255);
            UIRenderer.DrawRectOutline(cmNextX, cmPageY, 60, 24, 80, 140, 200, 255, 1);
            UIRenderer.DrawTextSmall("下页 ▶", cmNextX + 8, cmPageY + 4, 220, 240, 255, 255);
            if (cmNextH && UIRenderer.MouseLeftClick)
            {
                ChestManagerPage = Math.Min(totalPages - 1, ChestManagerPage + 1);
                UIRenderer.ConsumeClick();
            }
        }

        private static void DrawItemTooltip(Item item, ItemSlotRef slotRef, int mouseX, int mouseY)
        {
            string tipName = item.AffixName();
            string chestName = GetChestForRead(slotRef.ChestIndex)?.name;
            string chestInfo = !string.IsNullOrEmpty(chestName) ? $"箱子 #{slotRef.ChestIndex + 1} ({chestName})" : $"箱子 #{slotRef.ChestIndex + 1}";

            int tipW = Math.Min(PanelLayout.ScreenWidth, Math.Max(180, Math.Max(UIRenderer.MeasureText(tipName), UIRenderer.MeasureText(chestInfo)) + 24));
            int tipH = Math.Min(56, PanelLayout.ScreenHeight);
            int tipX = mouseX + 16, tipY = mouseY + 16;
            PanelLayout.ClampToScreen(ref tipX, ref tipY, tipW, tipH);

            UIRenderer.DrawPanel(tipX, tipY, tipW, tipH, 12, 16, 28, 245);
            UIRenderer.DrawRectOutline(tipX, tipY, tipW, tipH, 80, 160, 255, 255, 1);

            // 品质颜色
            Color rareColor = GetRarityColor(item.rare);
            PanelLayout.DrawFittedText(tipName, tipX + 10, tipY + 6, tipW - 20, rareColor.R, rareColor.G, rareColor.B);
            PanelLayout.DrawFittedText(chestInfo, tipX + 10, tipY + 32, tipW - 20, 160, 200, 240);
        }

        private static Color GetRarityColor(int rare)
        {
            switch (rare)
            {
                case -11: return new Color(255, 175, 0);
                case -1: return new Color(130, 130, 130);
                case 1: return new Color(150, 150, 255);
                case 2: return new Color(150, 255, 150);
                case 3: return new Color(255, 200, 150);
                case 4: return new Color(255, 150, 150);
                case 5: return new Color(255, 150, 255);
                case 6: return new Color(210, 160, 255);
                case 7: return new Color(150, 255, 10);
                case 8: return new Color(255, 255, 10);
                case 9: return new Color(5, 200, 255);
                case 10: return new Color(255, 40, 100);
                case 11: return new Color(180, 90, 255);
                default: return Color.White;
            }
        }

        private static bool TakeItemToPlayer(ItemSlotRef slotRef, bool takeAll)
        {
            Player player = Main.LocalPlayer;
            if (player == null || slotRef.Item == null || slotRef.Item.IsAir)
                return false;

            if (Main.netMode == 1)
            {
                return RemoteStorageOperations.RequestTake(slotRef.ChestIndex, slotRef.SlotIndex, slotRef.Item, takeAll);
            }

            if (!CanMutateChest(slotRef.ChestIndex))
            {
                Main.NewText("联机存取请先在游戏中打开该箱子；远程快照只能查看和合成。", 255, 210, 120);
                return false;
            }
            Chest chest = Main.chest[slotRef.ChestIndex];
            if (chest == null || chest.item == null || slotRef.SlotIndex < 0 || slotRef.SlotIndex >= chest.item.Length)
                return false;

            Item chestItem = chest.item[slotRef.SlotIndex];
            if (chestItem == null || chestItem.IsAir || chestItem.stack <= 0)
                return false;

            int countToTake = takeAll ? chestItem.stack : Math.Max(1, chestItem.stack / 2);
            Item itemToGive = chestItem.Clone();
            itemToGive.stack = countToTake;

            // 存入玩家背包
            Item remaining = player.GetItem(itemToGive, GetItemSettings.PickupItemFromWorld);
            int remainingCount = remaining != null && !remaining.IsAir ? remaining.stack : 0;
            int taken = countToTake - remainingCount;

            if (taken > 0)
            {
                chestItem.stack -= taken;
                if (chestItem.stack <= 0)
                {
                    chestItem.TurnToAir();
                }

                // 多人联机发包同步箱子槽位！
                if (Main.netMode == 1)
                {
                    NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, slotRef.ChestIndex, slotRef.SlotIndex);
                }

                Recipe.UpdateRecipeList();
                SoundEngine.PlaySound(SoundID.Grab);
                RefreshItems();
                return true;
            }
            return false;
        }

        private static bool DrawButton(string text, int x, int y, int width, int height, bool selected = false, bool enabled = true)
        {
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX <= x + width
                && UIRenderer.MouseY >= y && UIRenderer.MouseY <= y + height;
            byte r = (byte)(selected ? 40 : hover && enabled ? 40 : 26);
            byte g = (byte)(selected ? 80 : hover && enabled ? 60 : 34);
            byte b = (byte)(selected ? 130 : hover && enabled ? 90 : 50);
            UIRenderer.DrawRect(x, y, width, height, r, g, b, 255);
            UIRenderer.DrawRectOutline(x, y, width, height, 65, (byte)(selected ? 145 : 85), (byte)(selected ? 210 : 120), 255, 1);
            int textWidth = Math.Min(width - 8, (int)(UIRenderer.MeasureText(text) * 0.8f));
            byte color = (byte)(enabled ? 235 : 120);
            PanelLayout.DrawFittedText(text, x + (width - textWidth) / 2, y + (height - 16) / 2, textWidth, color, color, color);
            if (hover) Main.player[Main.myPlayer].mouseInterface = true;
            if (!hover || !enabled || !UIRenderer.MouseLeftClick) return false;
            UIRenderer.ConsumeClick();
            return true;
        }

        private static void DrawBottomToolbar(int startX, int startY)
        {
            if (!IsItemTab) return;
            int width = PanelWidth - 32;
            int buttonWidth = (width - 18) / 4;
            int height = _compactLayout ? 24 : 28;
            string[] actions = { "全部存入", "快速堆叠", "整理箱子", "全部取出" };
            for (int i = 0; i < actions.Length; i++)
                if (DrawButton(actions[i], startX + i * (buttonWidth + 6), startY, buttonWidth, height))
                    HandleToolbarAction(i);

            int pageY = startY + height + 6;
            int pageWidth = Math.Min(76, width / 4);
            int totalPages = Math.Max(1, (_filteredSlots.Count + PageSize - 1) / PageSize);
            if (DrawButton("上一页", startX, pageY, pageWidth, height, false, CurrentPage > 0))
            {
                CurrentPage--;
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
            int nextX = PanelX + PanelWidth - 16 - pageWidth;
            if (DrawButton("下一页", nextX, pageY, pageWidth, height, false, CurrentPage < totalPages - 1))
            {
                CurrentPage++;
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
            string pageText = $"{CurrentPage + 1} / {totalPages} 页 · {_filteredSlots.Count} 组";
            int textStart = startX + pageWidth + 8;
            int available = nextX - 8 - textStart;
            int textWidth = Math.Min(available, (int)(UIRenderer.MeasureText(pageText) * 0.8f));
            PanelLayout.DrawFittedText(pageText, textStart + (available - textWidth) / 2,
                pageY + (height - 16) / 2, textWidth, 220, 220, 200);
        }

        private static string GetSortModeDisplayName(ItemSortMode mode)
        {
            switch (mode)
            {
                case ItemSortMode.Rarity: return "稀有度";
                case ItemSortMode.Stack: return "数量";
                case ItemSortMode.Name: return "名称";
                case ItemSortMode.ID: return "ID";
                case ItemSortMode.Kind: return "种类";
                case ItemSortMode.Default:
                default: return "默认";
            }
        }

        private static void HandleToolbarAction(int actionIndex)
        {
            if (!BeginStorageAction()) return;
            Player player = Main.LocalPlayer;
            if (player == null || Main.chest == null)
                return;

            if (Main.netMode == 1)
            {
                if (RemoteStorageOperations.RequestToolbar(actionIndex)) ArmStorageActionCooldown();
                return;
            }

            switch (actionIndex)
            {
                case 0: // 全部存入
                    DepositMatchingItems();
                    break;
                case 1: // 快速堆叠
                    QuickStackToAllChests();
                    break;
                case 2: // 智能整理
                    SortChests();
                    break;
                case 3: // 全部取出
                    LootAllItems();
                    break;
            }

            RefreshItems();
            ArmStorageActionCooldown();
        }

        // 远程操作确认后会在后续帧解锁库存；鼠标仍按住时不能把同一点击变成多次请求。
        private static bool BeginStorageAction()
        {
            if (_storageActionHeld) return false;
            if (Main.GameUpdateCount < _storageActionCooldownUntil) return false;
            _storageActionHeld = true;
            _storageActionNeutralFrames = 0;
            return true;
        }

        private static void UpdateStorageActionState()
        {
            bool inputActive = Main.mouseLeft || Main.mouseRight || UIRenderer.MouseLeftClick || UIRenderer.MouseRightClick;
            if (inputActive)
            {
                _storageActionNeutralFrames = 0;
                return;
            }
            if (_storageActionHeld && ++_storageActionNeutralFrames >= 1)
            {
                _storageActionHeld = false;
                _storageActionNeutralFrames = 0;
            }
        }

        private static void ArmStorageActionCooldown()
        {
            _storageActionCooldownUntil = Main.GameUpdateCount + StorageActionCooldownFrames;
        }

        // Shift 快速存储入口：检查是否可以开始存储（供 InventoryAndReforgePatches 调用）
        public static bool BeginQuickDeposit()
        {
            // 只检查冷却，不需要按住锁存（Shift 存储不会连续触发同一个槽位）
            return Main.GameUpdateCount >= _storageActionCooldownUntil;
        }

        // Shift 快速存储成功后设置冷却（供 InventoryAndReforgePatches 调用）
        public static void ArmQuickDepositCooldown()
        {
            _storageActionCooldownUntil = Main.GameUpdateCount + StorageActionCooldownFrames;
        }

        private static void DepositMatchingItems()
        {
            Player player = Main.LocalPlayer;
            for (int i = 0; i < 50; i++)
            {
                Item item = player.inventory[i];
                if (item != null && !item.IsAir && !item.favorited)
                {
                    if (MatchesTab(item, CurrentTab))
                    {
                        PutItemIntoAnyChest(item);
                    }
                }
            }
            SoundEngine.PlaySound(SoundID.Grab);
        }

        private static void QuickStackToAllChests()
        {
            Player player = Main.LocalPlayer;
            for (int i = 0; i < 50; i++)
            {
                Item item = player.inventory[i];
                if (item != null && !item.IsAir && !item.favorited)
                {
                    StackItemIntoExistingChests(item);
                }
            }
            SoundEngine.PlaySound(SoundID.Grab);
        }

        public static bool PutItemIntoAnyChest(Item item)
        {
            return PutItemIntoChests(item, null, 0);
        }

        public static bool PutItemIntoNearbyChest(Item item, Player player, float tileRadius = 100f)
        {
            if (player == null) return false;
            return PutItemIntoChests(item, player, tileRadius);
        }

        private static bool PutItemIntoChests(Item item, Player nearbyPlayer, float tileRadius)
        {
            if (item == null || item.IsAir || item.stack <= 0)
                return false;

            if (Main.netMode == 1)
            {
                // 新渔获尚不属于服务器背包库存，交回调用者按原版拾取，不发送客户端创造物品的请求。
                if (nearbyPlayer != null && Array.IndexOf(Main.LocalPlayer.inventory, item) < 0 && !ReferenceEquals(item, Main.mouseItem)) return false;
                RemoteStorageOperations.RequestDeposit(item);
                return false; // 应答前保持原物品，不能提前扣除或把请求当作成功。
            }

            int originalStack = item.stack;

            // 优先堆叠到同类物品
            StackItemIntoExistingChests(item, nearbyPlayer, tileRadius);
            if (item.IsAir || item.stack <= 0)
            {
                item.TurnToAir();
                return true;
            }

            // 放入已标记箱子的第一个空槽
            for (int c = 0; c < Main.maxChests; c++)
            {
                if (!CanMutateChest(c)) continue;
                Chest chest = Main.chest[c];
                if (chest == null || chest.item == null || chest.x < 0 || chest.y < 0) continue;
                if (!IsChestMarked(chest)) continue;
                if (nearbyPlayer != null && Vector2.Distance(nearbyPlayer.Center, new Vector2(chest.x * 16, chest.y * 16)) > tileRadius * 16f) continue;

                for (int s = 0; s < chest.item.Length; s++)
                {
                    if (chest.item[s] == null || chest.item[s].IsAir)
                    {
                        chest.item[s] = item.Clone();
                        item.TurnToAir();

                        if (Main.netMode == 1)
                        {
                            NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, c, s);
                        }
                        return true;
                    }
                }
            }

            return item.stack < originalStack;
        }

        private static bool StackItemIntoExistingChests(Item item, Player nearbyPlayer = null, float tileRadius = 0)
        {
            for (int c = 0; c < Main.maxChests; c++)
            {
                if (!CanMutateChest(c)) continue;
                Chest chest = Main.chest[c];
                if (chest == null || chest.item == null || chest.x < 0 || chest.y < 0) continue;
                if (!IsChestMarked(chest)) continue;
                if (nearbyPlayer != null && Vector2.Distance(nearbyPlayer.Center, new Vector2(chest.x * 16, chest.y * 16)) > tileRadius * 16f) continue;

                for (int s = 0; s < chest.item.Length; s++)
                {
                    Item target = chest.item[s];
                    if (target != null && target.type == item.type && target.prefix == item.prefix && target.stack < target.maxStack)
                    {
                        int canAdd = target.maxStack - target.stack;
                        int toAdd = Math.Min(item.stack, canAdd);
                        target.stack += toAdd;
                        item.stack -= toAdd;

                        if (Main.netMode == 1)
                        {
                            NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, c, s);
                        }

                        if (item.stack <= 0)
                        {
                            item.TurnToAir();
                            return true;
                        }
                    }
                }
            }
            return item.IsAir;
        }

        private static void SortChests()
        {
            var indices = new List<int>();
            for (int c = 0; c < Main.maxChests; c++)
            {
                if (!CanMutateChest(c)) continue;
                Chest chest = Main.chest[c];
                if (chest == null || chest.item == null || chest.x < 0 || chest.y < 0) continue;
                if (!IsChestMarked(chest)) continue;
                indices.Add(c);
            }

            // 只把同ID、同前缀的堆叠填入更早的可写箱子，保留原物品对象及属性。
            var partialStacks = new Dictionary<long, Queue<Item>>();
            foreach (int c in indices)
            {
                foreach (Item item in Main.chest[c].item)
                {
                    if (item == null || item.IsAir) continue;
                    long key = ((long)item.type << 8) | item.prefix;
                    Queue<Item> targets;
                    if (!partialStacks.TryGetValue(key, out targets))
                    {
                        targets = new Queue<Item>();
                        partialStacks.Add(key, targets);
                    }
                    while (item.stack > 0 && targets.Count > 0)
                    {
                        Item target = targets.Peek();
                        int moved = Math.Min(item.stack, target.maxStack - target.stack);
                        target.stack += moved;
                        item.stack -= moved;
                        if (target.stack >= target.maxStack) targets.Dequeue();
                    }
                    if (item.stack == 0) item.TurnToAir();
                    else if (item.stack < item.maxStack) targets.Enqueue(item);
                }
            }

            foreach (int c in indices)
            {
                Chest chest = Main.chest[c];

                ItemSortMode mode = CurrentSortMode == ItemSortMode.Default ? ItemSortMode.Rarity : CurrentSortMode;
                Array.Sort(chest.item, (a, b) => CompareItems(a, b, mode));

                if (Main.netMode == 1)
                {
                    for (int s = 0; s < chest.item.Length; s++)
                    {
                        NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, c, s);
                    }
                }
            }
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        private static void LootAllItems()
        {
            foreach (var slot in _filteredSlots.ToArray())
            {
                TakeItemToPlayer(slot, takeAll: true);
            }
        }

        private static void DrawSettingsPage(int startX, int startY)
        {
            var config = Mod.Config;
            string[] lines = {
                "箱内合成：已接入原版材料读取、扣除与联机确认",
                "无限药水：" + (config?.InfinitePotions == true ? "开启" : "关闭") + "，每组至少 " + (config?.PotionStackRequirement ?? 30) + " 瓶",
                "便携制作站：" + (config?.PortableCraftingStations == true ? "开启" : "关闭"),
                "增益站：" + (config?.InfiniteStationsAndBanners == true ? "开启" : "关闭"),
                Main.netMode == 1 ? "联机：全世界标记箱子可远程存取；由服务器确认" : "单人：已标记且未锁/未占用的箱子参与存取与合成",
                !_canSave ? "标记存档无法恢复：本会话标记不会保存，请查看日志" : Main.netMode == 1 ? RemoteStorageOperations.Status : "物品来源：个人存储、大背包和已标记箱子"
            };
            int lineHeight = Math.Min(30, (_footerY - _contentY) / lines.Length);
            for (int i = 0; i < lines.Length; i++)
                PanelLayout.DrawFittedText(lines[i], startX, startY + i * lineHeight, PanelWidth - 48, 180, 235, 220);
        }
    }
}


