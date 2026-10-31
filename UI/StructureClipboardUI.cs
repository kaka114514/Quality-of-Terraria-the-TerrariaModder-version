using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using TerrariaModder.Core.UI;
using QualityOfTerraria.Features;

namespace QualityOfTerraria.UI
{
    // 结构管理面板只负责本地结构列表和选择状态，世界写入由工具/服务器流程处理。
    internal static class StructureClipboardUI
    {
        internal const string PanelId = "QualityOfTerraria.StructureClipboard";
        internal static bool IsOpen { get; private set; }
        internal static int PanelX = 180, PanelY = 90, PanelWidth = 560, PanelHeight = 480;
        internal static int SelectedIndex { get; private set; } = -1;
        internal static bool ConsumeMaterials { get => Mod.Config == null || Mod.Config.PasteConsumeMaterials; set { if (Mod.Config != null) Mod.Config.PasteConsumeMaterials = value; } }
        internal static bool DestroyTerrain { get => Mod.Config != null && Mod.Config.PasteDestroyTerrain; set { if (Mod.Config != null) Mod.Config.PasteDestroyTerrain = value; } }
        internal static bool QuickPlaceNoConsume => Mod.Config != null && Mod.Config.WandMaterialNoConsume;
        internal static bool QuickPlaceDestroyTerrain { get => Mod.Config != null && Mod.Config.QuickPlaceDestroyTerrain; set { if (Mod.Config != null) Mod.Config.QuickPlaceDestroyTerrain = value; } }
        internal static bool DestroyBackgroundWalls { get => Mod.Config == null || Mod.Config.DestroyBackgroundWalls; set { if (Mod.Config != null) Mod.Config.DestroyBackgroundWalls = value; } }
        internal static bool DestroyDrops { get => Mod.Config == null || Mod.Config.DestroyDrops; set { if (Mod.Config != null) Mod.Config.DestroyDrops = value; } }
        internal static StructureClipboard.Structure SelectedStructure => SelectedIndex >= 0 && SelectedIndex < StructureClipboard.Structures.Count ? StructureClipboard.Structures[SelectedIndex] : null;
        private static bool _dragging;
        private static bool _renaming;
        private static string _renameText;
        private static int _gridOffset;
        private static string _search = string.Empty;
        private static bool _searchEditing;
        private const string SearchInputId = "QualityOfTerraria.StructureSearch";
        private const string RenameInputId = "QualityOfTerraria.StructureRename";
        private static int _dragOffsetX, _dragOffsetY;

        internal static void Toggle()
        {
            if (!UIManager.CanRender) return;
            StructureClipboard.EnsureInitialized();
            if (SelectedIndex < 0 && StructureClipboard.Structures.Count > 0) Select(0);
            IsOpen = !IsOpen;
            if (!IsOpen && _searchEditing)
            {
                UIRenderer.UnregisterKeyInputBlock(SearchInputId);
                UIRenderer.DisableTextInput();
                _searchEditing = false;
            }
            if (IsOpen) { UIRenderer.BringToFront(PanelId); SoundEngine.PlaySound(SoundID.MenuOpen); }
            else { UIRenderer.UnregisterPanelBounds(PanelId); SoundEngine.PlaySound(SoundID.MenuClose); }
        }

        internal static void ResetSession()
        {
            IsOpen = false; SelectedIndex = -1; _dragging = false; _gridOffset = 0; _search = string.Empty;
            if (_searchEditing) UIRenderer.UnregisterKeyInputBlock(SearchInputId);
            _searchEditing = false;
            if (_renaming) UIRenderer.UnregisterKeyInputBlock(RenameInputId);
            _renaming = false; _renameText = null;
            UIRenderer.UnregisterPanelBounds(PanelId);
        }

        internal static bool Select(int index)
        {
            StructureClipboard.EnsureInitialized();
            if (index < 0 || index >= StructureClipboard.Structures.Count) return false;
            SelectedIndex = index; StructureClipboard.Select(index);
            return true;
        }

        internal static bool RenameSelected(string name, out string error)
        {
            if (SelectedIndex < 0) { error = "请先选择结构"; return false; }
            return StructureClipboard.Rename(SelectedIndex, name, out error);
        }

        internal static bool DeleteSelected(out string error)
        {
            if (SelectedIndex < 0) { error = "请先选择结构"; return false; }
            bool result = StructureClipboard.Delete(SelectedIndex, out error);
            if (result) SelectedIndex = Math.Min(SelectedIndex, StructureClipboard.Structures.Count - 1);
            return result;
        }

        internal static void Update()
        {
            if (!IsOpen || !UIManager.CanRender) { _dragging = false; UIRenderer.UnregisterPanelBounds(PanelId); return; }
            StructureClipboard.EnsureInitialized();
            PanelLayout.ClampToScreen(ref PanelX, ref PanelY, PanelWidth, PanelHeight);
            UIRenderer.RegisterPanelBounds(PanelId, PanelX, PanelY, PanelWidth, PanelHeight);
        }

        internal static void Draw()
        {
            if (!IsOpen || !UIManager.CanRender) return;
            StructureClipboard.EnsureInitialized();
            PanelLayout.ClampToScreen(ref PanelX, ref PanelY, PanelWidth, PanelHeight);
            int mx = UIRenderer.MouseX, my = UIRenderer.MouseY;
            bool closeHover = mx >= PanelX + PanelWidth - 72 && mx < PanelX + PanelWidth - 12 && my >= PanelY + 4 && my < PanelY + 28;
            if (closeHover && UIRenderer.MouseLeftClick)
            {
                UIRenderer.ConsumeClick();
                Toggle();
                return;
            }
            UIRenderer.DrawPanel(PanelX, PanelY, PanelWidth, PanelHeight, 18, 28, 45, 245);
            PanelLayout.DrawFittedText("结构复制与粘贴", PanelX + 12, PanelY + 8, PanelWidth - 92, 235, 245, 255);
            UIRenderer.DrawRect(PanelX + PanelWidth - 72, PanelY + 4, 60, 24, (byte)(closeHover ? 220 : 60), 60, 70, 245);
            PanelLayout.DrawFittedText("关闭", PanelX + PanelWidth - 67, PanelY + 9, 50, 245, 245, 255);
            if (_searchEditing)
            {
                UIRenderer.EnableTextInput();
                UIRenderer.HandleIME();
                _search = UIRenderer.GetInputText(_search ?? string.Empty);
                if (Main.keyState.IsKeyDown(Keys.Enter) || UIRenderer.CheckInputEscape())
                {
                    UIRenderer.UnregisterKeyInputBlock(SearchInputId);
                    UIRenderer.DisableTextInput();
                    _searchEditing = false;
                }
            }
            bool header = mx >= PanelX && mx < PanelX + PanelWidth && my >= PanelY && my < PanelY + 32;
            if (_dragging)
            {
                Main.LocalPlayer.mouseInterface = true;
                if (Main.mouseLeft) { PanelX = mx - _dragOffsetX; PanelY = my - _dragOffsetY; }
                else _dragging = false;
            }
            else if (header && UIRenderer.MouseLeftClick)
            {
                _dragging = true; _dragOffsetX = mx - PanelX; _dragOffsetY = my - PanelY; UIRenderer.ConsumeClick();
            }

            int top = PanelY + 38;
            DrawOption("粘贴消耗材料", ConsumeMaterials, PanelX + 12, top, 145);
            DrawOption("粘贴破坏地形", DestroyTerrain, PanelX + 170, top, 145);
            DrawOption("快速放置破坏地形", QuickPlaceDestroyTerrain, PanelX + 328, top, 210);
            DrawOption("快速放置不消耗", QuickPlaceNoConsume, PanelX + 12, top + 30, 145);
            DrawOption("破坏背景墙", DestroyBackgroundWalls, PanelX + 170, top + 30, 145);
            DrawOption("破坏掉落物", DestroyDrops, PanelX + 328, top + 30, 210);
            int searchX = PanelX + 12, searchY = top + 60, searchWidth = PanelWidth - 24;
            bool searchHover = mx >= searchX && mx < searchX + searchWidth && my >= searchY && my < searchY + 28;
            if (searchHover) Main.LocalPlayer.mouseInterface = true;
            if (searchHover && UIRenderer.MouseLeftClick && !_searchEditing)
            {
                _searchEditing = true;
                UIRenderer.ClearInput(); UIRenderer.EnableTextInput(); UIRenderer.RegisterKeyInputBlock(SearchInputId); UIRenderer.ConsumeClick();
            }
            UIRenderer.DrawRect(searchX, searchY + 2, searchWidth, 24, (byte)(searchHover ? 45 : 25), 55, 70, 255);
            PanelLayout.DrawFittedText("搜索结构：" + (string.IsNullOrEmpty(_search) ? "请输入名称" : _search) + (_searchEditing ? "|" : ""), searchX + 7, searchY + 6, searchWidth - 14, 235, 240, 255);
            List<int> matches = StructureClipboard.Structures
                .Select((structure, index) => new { structure, index })
                .Where(x => string.IsNullOrWhiteSpace(_search) || (x.structure?.Name ?? string.Empty).IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(x => x.index).ToList();
            PanelLayout.DrawFittedText("已保存结构（最多100个）", PanelX + 12, top + 92, PanelWidth - 24, 180, 210, 240);
            int gridTop = top + 120, gridBottom = PanelY + PanelHeight - 54;
            int columns = 4, cardWidth = 126, cardHeight = 84, gap = 6;
            int visibleRows = Math.Max(1, (gridBottom - gridTop) / (cardHeight + gap));
            int totalRows = (matches.Count + columns - 1) / columns;
            int maxOffset = Math.Max(0, totalRows - visibleRows);
            if (mx >= PanelX + 12 && mx < PanelX + PanelWidth - 12 && my >= gridTop && my < gridBottom && UIRenderer.ScrollWheel != 0)
            {
                _gridOffset = Math.Max(0, Math.Min(maxOffset, _gridOffset + (UIRenderer.ScrollWheel < 0 ? 1 : -1)));
                UIRenderer.ConsumeScroll();
            }
            int first = _gridOffset * columns;
            for (int i = first; i < matches.Count && i < first + visibleRows * columns; i++)
            {
                int structureIndex = matches[i];
                var structure = StructureClipboard.Structures[structureIndex];
                int column = (i - first) % columns, row = (i - first) / columns;
                int x = PanelX + 12 + column * (cardWidth + gap), y = gridTop + row * (cardHeight + gap);
                bool hover = mx >= x && mx < x + cardWidth && my >= y && my < y + cardHeight;
                if (hover) Main.LocalPlayer.mouseInterface = true;
                if (hover && UIRenderer.MouseLeftClick) { Select(structureIndex); UIRenderer.ConsumeClick(); }
                UIRenderer.DrawRect(x, y, cardWidth, cardHeight, (byte)(structureIndex == SelectedIndex ? 60 : hover ? 45 : 30), 70, 100, 220);
                DrawThumbnail(structure, x + 4, y + 4, cardWidth - 8, cardHeight - 28);
                PanelLayout.DrawFittedText(structure.Name, x + 6, y + cardHeight - 20, cardWidth - 12, 225, 235, 245);
                if (hover) TerrariaModder.Core.UI.Widgets.Tooltip.Set(StructureClipboard.FormatMaterialSummary(structure));
            }
            int actionY = PanelY + PanelHeight - 38;
            if (SelectedStructure != null)
            {
                DrawAction("重命名", PanelX + 12, actionY, 90, BeginRename);
                DrawAction("删除", PanelX + 112, actionY, 70, DeleteSelectedWithMessage);
                DrawAction("应用", PanelX + 192, actionY, 70, ApplySelected);
                if (_renaming) DrawRenameInput(PanelX + 274, actionY, PanelWidth - 286);
            }
        }

        private static void DrawThumbnail(StructureClipboard.Structure structure, int x, int y, int width, int height)
        {
            if (structure == null || structure.Cells == null) return;
            UIRenderer.DrawRect(x, y, width, height, 12, 18, 30, 180);
            Rectangle bounds = StructurePreviewRenderer.GetPreview(structure).Bounds;
            float scale = Math.Min(width / (float)Math.Max(1, bounds.Width), height / (float)Math.Max(1, bounds.Height));
            Vector2 origin = new Vector2(x + (width - bounds.Width * scale) / 2f - bounds.X * scale,
                y + (height - bounds.Height * scale) / 2f - bounds.Y * scale);
            StructurePreviewRenderer.Draw(structure, origin, new Vector2(scale), Color.White, new Rectangle(x, y, width, height));
        }
        private static void DrawAction(string text, int x, int y, int width, Action action)
        {
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + 28;
            if (hover) Main.LocalPlayer.mouseInterface = true;
            UIRenderer.DrawRect(x, y, width, 26, (byte)(hover ? 55 : 30), 60, 90, 230);
            PanelLayout.DrawFittedText(text, x + 6, y + 5, width - 12, 240, 245, 255);
            if (hover && UIRenderer.MouseLeftClick) { UIRenderer.ConsumeClick(); action(); }
        }

        private static void BeginRename()
        {
            _renaming = true; _renameText = SelectedStructure?.Name ?? string.Empty;
            UIRenderer.ClearInput(); UIRenderer.EnableTextInput(); UIRenderer.RegisterKeyInputBlock(RenameInputId);
        }

        private static void DrawRenameInput(int x, int y, int width)
        {
            UIRenderer.EnableTextInput(); UIRenderer.HandleIME(); _renameText = UIRenderer.GetInputText(_renameText ?? string.Empty);
            if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter))
            {
                string error;
                if (!RenameSelected(_renameText, out error)) Main.NewText("结构重命名失败：" + error, 255, 150, 120);
                UIRenderer.UnregisterKeyInputBlock(RenameInputId); UIRenderer.DisableTextInput(); _renaming = false;
            }
            UIRenderer.DrawRect(x, y + 2, Math.Max(80, width), 24, 30, 55, 75, 230);
            PanelLayout.DrawFittedText(_renameText + "|", x + 6, y + 6, Math.Max(68, width - 12), 240, 245, 255);
        }

        private static void DeleteSelectedWithMessage()
        {
            string error;
            if (!DeleteSelected(out error)) Main.NewText("结构删除失败：" + error, 255, 150, 120);
        }

        private static void ApplySelected()
        {
            if (SelectedIndex >= 0 && StructureClipboard.Select(SelectedIndex)) Main.NewText("结构已选中，请按 P 进入粘贴模式后左键放置", 120, 255, 150);
        }

        private static void DrawOption(string label, bool value, int x, int y, int width)
        {
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + 26;
            if (hover) Main.LocalPlayer.mouseInterface = true;
            if (hover && UIRenderer.MouseLeftClick)
            {
                if (label == "粘贴消耗材料") ConsumeMaterials = !ConsumeMaterials;
                else if (label == "粘贴破坏地形") DestroyTerrain = !DestroyTerrain;
                else if (label == "快速放置破坏地形") QuickPlaceDestroyTerrain = !QuickPlaceDestroyTerrain;
                else if (label == "快速放置不消耗") Mod.Config.WandMaterialNoConsume = !Mod.Config.WandMaterialNoConsume;
                else if (label == "破坏背景墙") DestroyBackgroundWalls = !DestroyBackgroundWalls;
                else if (label == "破坏掉落物") DestroyDrops = !DestroyDrops;
                string error;
                if (!Networking.SettingsSync.SaveVerified(Mod.Config, out error)) Main.NewText("结构与快速工具设置保存失败：" + error, 255, 150, 120);
                UIRenderer.ConsumeClick();
            }
            UIRenderer.DrawRect(x, y, width, 26, (byte)(value ? 60 : 30), value ? (byte)150 : (byte)90, value ? (byte)90 : (byte)90, 220);
            PanelLayout.DrawFittedText((value ? "☑ " : "☐ ") + label, x + 8, y + 5, width - 16, 235, 235, 245);
        }
    }
}
