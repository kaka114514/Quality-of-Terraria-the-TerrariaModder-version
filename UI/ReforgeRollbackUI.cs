using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI.Chat;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.UI
{
    internal static class ReforgeRollbackUI
    {
        internal const string PanelId = "QualityOfTerraria.ReforgeRollback";

        // 词缀历史记录
        private static readonly Dictionary<string, List<byte>> _prefixHistory = new Dictionary<string, List<byte>>();
        private static readonly Dictionary<string, int> _historyIndex = new Dictionary<string, int>();
        private static readonly Dictionary<string, byte> _lastRecordedPrefix = new Dictionary<string, byte>();

        internal static bool Visible => IsReforgeOpen && Mod.Config?.ImprovePrefix == true && CanRollback;

        private static bool IsReforgeOpen => UIManager.CanRender && Main.instance != null && Main.playerInventory &&
            Main.npcShop == 0 && !Main.reforgeItem.IsAir && Main.LocalPlayer.talkNPC >= 0 &&
            Main.LocalPlayer.talkNPC < Main.maxNPCs && Main.npc[Main.LocalPlayer.talkNPC].active &&
            Main.npc[Main.LocalPlayer.talkNPC].type == NPCID.GoblinTinkerer;

        private static bool CanRollback
        {
            get
            {
                if (Main.reforgeItem.IsAir) return false;
                string key = GetItemKey(Main.reforgeItem);
                return _prefixHistory.ContainsKey(key) && _prefixHistory[key].Count > 1;
            }
        }

        private static string GetItemKey(Item item)
        {
            // 使用类型作为key，这样同一物品的所有词条都记录在一起
            return $"{item.type}_{Main.LocalPlayer.whoAmI}";
        }

        // 记录词缀变化（每帧自动调用）
        internal static void RecordPrefix(Item item)
        {
            if (item == null || item.IsAir) return;

            string key = GetItemKey(item);
            byte currentPrefix = item.prefix;

            // 初始化历史记录
            if (!_prefixHistory.ContainsKey(key))
            {
                _prefixHistory[key] = new List<byte>();
                _historyIndex[key] = 0;
                _lastRecordedPrefix[key] = 0;
            }

            List<byte> history = _prefixHistory[key];
            byte lastPrefix = _lastRecordedPrefix[key];

            // 如果这是第一次记录，直接添加当前词条
            if (history.Count == 0 && currentPrefix > 0)
            {
                history.Add(currentPrefix);
                _lastRecordedPrefix[key] = currentPrefix;
                _historyIndex[key] = 0;
                return;
            }

            // 如果词条发生了变化，说明重铸了
            if (currentPrefix != lastPrefix && currentPrefix > 0)
            {
                // 去重：如果历史中已有此词条，移除它
                if (history.Contains(currentPrefix))
                {
                    history.Remove(currentPrefix);
                }

                // 添加新词条到历史末尾
                history.Add(currentPrefix);
                _lastRecordedPrefix[key] = currentPrefix;

                // 重置索引到最新位置
                _historyIndex[key] = history.Count - 1;
            }
        }

        // 重置历史记录
        internal static void ResetHistory()
        {
            _prefixHistory.Clear();
            _historyIndex.Clear();
            _lastRecordedPrefix.Clear();
        }

        internal static void Update()
        {
            if (!IsReforgeOpen)
            {
                UIRenderer.UnregisterPanelBounds(PanelId);
                return;
            }

            // 当物品移除时重置当前物品的记录前缀
            if (Main.reforgeItem.IsAir)
            {
                // 不清空字典，保留历史
            }
            else
            {
                // 每帧检测词条变化并记录
                RecordPrefix(Main.reforgeItem);
            }
        }

        internal static void Draw()
        {
            if (!Visible)
            {
                UIRenderer.UnregisterPanelBounds(PanelId);
                return;
            }

            // 计算按钮位置（在重铸槽下方）
            int buttonWidth = 120;
            int buttonHeight = 28;
            int x = 50; // 重铸槽左侧位置
            int y = 330; // 重铸槽下方

            // 检测鼠标悬停
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + buttonWidth &&
                        UIRenderer.MouseY >= y && UIRenderer.MouseY < y + buttonHeight;

            UIRenderer.RegisterPanelBounds(PanelId, x, y, buttonWidth, buttonHeight);
            if (hover) Main.LocalPlayer.mouseInterface = true;

            // 绘制按钮背景
            UIRenderer.DrawRect(x, y, buttonWidth, buttonHeight, (byte)(hover ? 42 : 28), 65, 95, 245);
            UIRenderer.DrawRectOutline(x, y, buttonWidth, buttonHeight, 85, 160, 220, 255, 1);

            // 绘制按钮文字
            PanelLayout.DrawFittedText("点击回溯词条", x + 8, y + 7, buttonWidth - 16, 235, 240, 255);

            // 检测点击
            if (!hover || !UIRenderer.MouseLeftClick) return;

            UIRenderer.ConsumeClick();
            RollbackPrefix();
        }

        private static void RollbackPrefix()
        {
            if (Main.reforgeItem.IsAir) return;

            string key = GetItemKey(Main.reforgeItem);
            if (!_prefixHistory.ContainsKey(key) || _prefixHistory[key].Count <= 1) return;

            List<byte> list = _prefixHistory[key];

            // 获取当前索引
            int currentIndex = _historyIndex[key];

            // 回滚到上一个词缀（循环）
            int newIndex = (currentIndex - 1 + list.Count) % list.Count;
            _historyIndex[key] = newIndex;
            byte targetPrefix = list[newIndex];

            // 关键修复：先清除所有属性，再应用新前缀
            int itemType = Main.reforgeItem.type;
            int stack = Main.reforgeItem.stack;
            bool favorited = Main.reforgeItem.favorited;

            // 重新创建物品（清除所有前缀叠加的属性）
            Main.reforgeItem.SetDefaults(itemType);
            Main.reforgeItem.stack = stack;
            Main.reforgeItem.favorited = favorited;

            // 应用目标前缀（在干净基础上）
            Main.reforgeItem.Prefix(targetPrefix);

            // 更新记录的前缀，避免重复记录回滚操作
            _lastRecordedPrefix[key] = targetPrefix;

            // 构建历史列表
            string targetName = Lang.prefix[targetPrefix].Value;
            List<string> historyNames = list.ConvertAll(p => Lang.prefix[p].Value);
            string historyStr = string.Join("/", historyNames);

            // 格式：[重铸回溯]已回滚词条为：强力 【历史5个：缓慢/强力/神级/恶魔/恐怖】
            string message = $"[重铸回溯]已回滚词条为：{targetName} 【历史{list.Count}个：{historyStr}】";

            SoundEngine.PlaySound(SoundID.Item37);
            Main.NewText(message, 100, 180, 255); // 蓝色 RGB(100, 180, 255)
        }

        internal static void ResetSession()
        {
            ResetHistory();
            UIRenderer.UnregisterPanelBounds(PanelId);
        }
    }
}
