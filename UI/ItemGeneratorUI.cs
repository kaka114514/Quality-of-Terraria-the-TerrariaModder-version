using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using HarmonyLib;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.UI
{
    internal static class ItemGeneratorUI
    {
        internal const string PanelId = "QualityOfTerraria.ItemGenerator";
        private const int Columns = 13;
        private const int Rows = 7;
        private const int PageSize = Columns * Rows;
        private static readonly string[] Categories = ItemCategory.Names;
        private static string _search = "";
        private static string _quantity = "1";
        private static string _inputId;
        private static bool _open;
        private static bool _dragging;
        private static bool _escapeWasDown;
        private static int _dragX, _dragY;
        private static int _category;
        private static int _page;
        private static int _selectedType = -1;
        private static string _hoveredItemName;

        internal static int PanelX = 170, PanelY = 80, PanelWidth = 860, PanelHeight = 520;
        internal static bool IsExpanded => _open;
        internal static int RowCount => 1;

        internal static void ResetSession()
        {
            _open = false; _dragging = false; _escapeWasDown = false;
            _search = ""; _quantity = "1"; _inputId = null;
            _category = 0; _page = 0; _selectedType = -1;
            EndInput();
            UIRenderer.UnregisterPanelBounds(PanelId);
        }

        internal static bool IsValidQuantity(int quantity) => quantity >= 1 && quantity <= 9999;
        internal static bool IsValidItem(int itemType) => itemType > 0 && itemType < ItemID.Count;

        internal static IEnumerable<int> FindMatches(string search, int limit)
        {
            string query = (search ?? "").Trim();
            int count = 0;
            for (int type = 1; type < ItemID.Count && count < Math.Max(0, limit); type++)
            {
                if (!MatchesCategory(type, _category)) continue;
                string name;
                try { name = Lang.GetItemNameValue(type); } catch { continue; }
                if (string.IsNullOrEmpty(name) || (query.Length > 0 && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                yield return type;
                count++;
            }
        }

        internal static bool GiveSinglePlayer(Player player, int itemType, int quantity, string message)
        {
            if (player == null || !IsValidItem(itemType) || !IsValidQuantity(quantity)) return false;
            var item = new Item(); item.SetDefaults(itemType); item.stack = quantity;
            Item remaining = player.GetItem(item, GetItemSettings.PickupItemFromWorld);
            if (!remaining.IsAir) Main.NewText("物品生成器：背包空间不足，部分物品未能加入", 255, 180, 100);
            else if (!string.IsNullOrEmpty(message)) Main.NewText(message, 150, 230, 255);
            return true;
        }

        internal static void Toggle()
        {
            if (!UIManager.CanRender) return;
            _open = !_open;
            if (!_open) EndInput();
            else { UIRenderer.BringToFront(PanelId); SoundEngine.PlaySound(Terraria.ID.SoundID.MenuOpen); }
        }

        internal static void ToggleFromHotkey()
        {
            if (Mod.Config == null || !Mod.Config.ItemGeneratorEnabled)
            {
                Main.NewText("物品生成器快捷键已关闭，请先在模组设置中开启。", 255, 190, 120);
                return;
            }
            Toggle();
        }

        internal static void DrawRow(int x, int y, int width, int row)
        {
            if (row != 0) return;
            bool hover = Hit(x, y, width, 28);
            if (hover && Mod.WorldActive) Main.LocalPlayer.mouseInterface = true;
            bool blocked = (bool)(AccessTools.Field(typeof(ModMenu), "_blockInput")?.GetValue(null) ?? false);
            bool enabled = Mod.Config != null && Mod.Config.ItemGeneratorEnabled;
            DrawButton(enabled ? "打开物品生成器（T）" : "物品生成器（请先开启快捷键）", x, y, width, 28, enabled && hover && !blocked, Toggle);
        }

        internal static void Update()
        {
            if (!_open || !UIManager.CanRender)
            {
                _dragging = false;
                UIRenderer.UnregisterPanelBounds(PanelId);
                return;
            }
            if (Main.keyState.IsKeyDown(Keys.Escape) && !_escapeWasDown) Toggle();
            _escapeWasDown = Main.keyState.IsKeyDown(Keys.Escape);
            PanelLayout.ClampToScreen(ref PanelX, ref PanelY, PanelWidth, PanelHeight);
            UIRenderer.RegisterPanelBounds(PanelId, PanelX, PanelY, PanelWidth, PanelHeight);
        }

        internal static void Draw()
        {
            if (!_open || !UIManager.CanRender) return;
            int mx = UIRenderer.MouseX, my = UIRenderer.MouseY;
            _hoveredItemName = null;
            if (Hit(PanelX, PanelY, PanelWidth, PanelHeight)) Main.LocalPlayer.mouseInterface = true;
            UIRenderer.DrawPanel(PanelX, PanelY, PanelWidth, PanelHeight, 18, 28, 45, 245);
            PanelLayout.DrawFittedText("物品生成器", PanelX + 12, PanelY + 8, 220, 235, 245, 255);
            bool closeHover = Hit(PanelX + PanelWidth - 72, PanelY + 4, 60, 24);
            if (DrawButton("关闭", PanelX + PanelWidth - 72, PanelY + 4, 60, 24, closeHover, Toggle)) return;
            bool header = Hit(PanelX, PanelY, PanelWidth, 32);
            if (_dragging)
            {
                Main.LocalPlayer.mouseInterface = true;
                if (Main.mouseLeft) { PanelX = mx - _dragX; PanelY = my - _dragY; }
                else _dragging = false;
            }
            else if (header && UIRenderer.MouseLeftClick)
            {
                _dragging = true; _dragX = mx - PanelX; _dragY = my - PanelY; UIRenderer.ConsumeClick();
            }

            int top = PanelY + 38;
            DrawInput("搜索物品", ref _search, PanelX + 12, top, 250, "QualityOfTerraria.ItemGenerator.Search");
            DrawInput("数量（1-9999）", ref _quantity, PanelX + 272, top, 150, "QualityOfTerraria.ItemGenerator.Quantity");
            int categoryX = PanelX + 12;
            const int categoryColumns = 6;
            int categoryRows = (Categories.Length + categoryColumns - 1) / categoryColumns;
            int categoryWidth = Math.Max(1, (PanelWidth - 24 - (categoryColumns - 1) * 4) / categoryColumns);
            for (int i = 0; i < Categories.Length; i++)
            {
                int x = categoryX + (i % categoryColumns) * (categoryWidth + 4), y = top + 32 + (i / categoryColumns) * 28;
                if (DrawButton(Categories[i], x, y, categoryWidth, 24, Hit(x, y, categoryWidth, 24), () => { _category = i; _page = 0; }))
                    _selectedType = -1;
            }

            List<int> all = FindAll(_search).ToList();
            int pages = Math.Max(1, (all.Count + PageSize - 1) / PageSize);
            _page = Math.Max(0, Math.Min(_page, pages - 1));
            int gridTop = top + 32 + categoryRows * 28 + 4, gap = 2;
            int cellW = Math.Max(1, (PanelWidth - 24 - (Columns - 1) * gap) / Columns);
            int cellH = Math.Max(1, Math.Min(48, (PanelY + PanelHeight - 44 - gridTop - (Rows - 1) * gap) / Rows));
            for (int i = 0; i < PageSize; i++)
            {
                int index = _page * PageSize + i;
                if (index >= all.Count) break;
                int x = PanelX + 12 + (i % Columns) * (cellW + gap), y = gridTop + (i / Columns) * (cellH + gap);
                DrawItemCell(x, y, cellW, cellH, all[index]);
            }
            int footer = PanelY + PanelHeight - 36;
            PanelLayout.DrawFittedText($"第 {_page + 1}/{pages} 页，共 {all.Count} 件", PanelX + 12, footer + 6, 180, 190, 220, 240);
            DrawButton("上一页", PanelX + PanelWidth - 190, footer, 78, 26, _page > 0 && Hit(PanelX + PanelWidth - 190, footer, 78, 26), () => _page--);
            DrawButton("下一页", PanelX + PanelWidth - 102, footer, 78, 26, _page + 1 < pages && Hit(PanelX + PanelWidth - 102, footer, 78, 26), () => _page++);
            if (!string.IsNullOrEmpty(_hoveredItemName)) DrawItemTooltip(_hoveredItemName, mx, my);
        }

        private static IEnumerable<int> FindAll(string search)
        {
            string query = (search ?? "").Trim();
            for (int type = 1; type < ItemID.Count; type++)
            {
                if (!MatchesCategory(type, _category)) continue;
                string name; try { name = Lang.GetItemNameValue(type); } catch { continue; }
                if (!string.IsNullOrEmpty(name) && (query.Length == 0 || name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)) yield return type;
            }
        }

        private static bool MatchesCategory(int type, int category)
        {
            var item = new Item(); item.SetDefaults(type);
            switch (category)
            {
                default: return ItemCategory.Matches(item, category);
            }
        }

        private static void DrawItemCell(int x, int y, int width, int height, int type)
        {
            bool hover = Hit(x, y, width, height);
            if (hover)
            {
                Main.LocalPlayer.mouseInterface = true;
                _hoveredItemName = Lang.GetItemNameValue(type);
            }
            UIRenderer.DrawRect(x, y, width, height, (byte)(hover ? 52 : 30), 65, 95, 230);
            var item = new Item(); item.SetDefaults(type);
            StorageItemDrawing.DrawIcon(item, x + width / 2 - 18, y + 3, 36, 36);
            if (hover && UIRenderer.MouseLeftClick)
            {
                Generate(type);
                UIRenderer.ConsumeClick();
            }
        }

        private static void DrawItemTooltip(string name, int mouseX, int mouseY)
        {
            int width = Math.Max(88, Math.Min(260, (int)(UIRenderer.MeasureText(name) * 0.8f) + 20));
            const int height = 30;
            int x = mouseX + 16, y = mouseY + 18;
            if (x + width > PanelLayout.ScreenWidth) x = mouseX - width - 12;
            if (y + height > PanelLayout.ScreenHeight) y = mouseY - height - 12;
            x = Math.Max(0, x); y = Math.Max(0, y);
            UIRenderer.DrawRect(x, y, width, height, 15, 22, 34, 250);
            UIRenderer.DrawRectOutline(x, y, width, height, 100, 165, 205, 255, 1);
            PanelLayout.DrawFittedText(name, x + 8, y + 7, width - 16, 245, 245, 245);
        }

        private static void Generate(int itemType)
        {
            int quantity;
            if (!int.TryParse(_quantity, out quantity) || !IsValidQuantity(quantity))
            {
                Main.NewText("物品生成器：数量必须是1至9999", 255, 150, 120); return;
            }
            _selectedType = itemType;
            EndInput();
            if (Main.netMode == 1)
            {
                Networking.ToolNetwork.Request("give-item", -1, itemType, quantity);
                Main.NewText("物品生成器：正在等待服务器确认", 200, 220, 255);
            }
            else GiveSinglePlayer(Main.LocalPlayer, itemType, quantity, "物品生成器：已生成 " + Lang.GetItemNameValue(itemType) + " ×" + quantity);
        }

        private static void DrawInput(string label, ref string value, int x, int y, int width, string id)
        {
            bool hover = Hit(x, y, width, 28);
            if (hover) Main.LocalPlayer.mouseInterface = true;
            if (hover && UIRenderer.MouseLeftClick && _inputId != id)
            {
                _inputId = id; UIRenderer.ClearInput(); UIRenderer.EnableTextInput(); UIRenderer.RegisterKeyInputBlock(id); UIRenderer.ConsumeClick();
            }
            if (_inputId == id)
            {
                UIRenderer.EnableTextInput(); UIRenderer.HandleIME(); value = UIRenderer.GetInputText(value ?? "");
                if (id.EndsWith("Quantity", StringComparison.Ordinal)) value = new string(value.Where(char.IsDigit).Take(4).ToArray());
                if (Main.keyState.IsKeyDown(Keys.Enter) || UIRenderer.CheckInputEscape()) EndInput();
            }
            UIRenderer.DrawRect(x, y + 2, width, 24, (byte)(hover ? 45 : 25), 55, 70, 255);
            PanelLayout.DrawFittedText(label + "：" + (string.IsNullOrEmpty(value) ? "请输入" : value) + (_inputId == id ? "|" : ""), x + 7, y + 6, width - 14, 235, 240, 255);
        }

        private static void EndInput()
        {
            if (_inputId != null) UIRenderer.UnregisterKeyInputBlock(_inputId);
            _inputId = null; UIRenderer.DisableTextInput(); UIRenderer.ClearInput();
        }

        private static bool Hit(int x, int y, int width, int height) => UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + height;

        private static bool DrawButton(string text, int x, int y, int width, int height, bool enabled, Action action)
        {
            UIRenderer.DrawRect(x, y, width, height, (byte)(enabled ? 45 : 28), 65, 95, 245);
            PanelLayout.DrawFittedText(text, x + 5, y + 5, width - 10, 240, 245, 255);
            if (!enabled || !UIRenderer.MouseLeftClick) return false;
            UIRenderer.ConsumeClick(); action?.Invoke(); return true;
        }
    }
}
