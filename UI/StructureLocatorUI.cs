using System;
using QualityOfTerraria.Features;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.UI
{
    public static class StructureLocatorUI
    {
        public const string PanelId = "QualityOfTerraria.StructureLocatorUI";

        public static bool IsOpen = false;

        public static int PanelX = 260;
        public static int PanelY = 120;
        public static int PanelWidth = 630;
        public static int PanelHeight = 460;

        private static bool _isDragging = false;
        private static int _dragOffsetX = 0;
        private static int _dragOffsetY = 0;

        private static void ClampLayout()
        {
            PanelWidth = Math.Min(630, PanelLayout.ScreenWidth);
            PanelHeight = Math.Min(460, PanelLayout.ScreenHeight);
            PanelLayout.ClampToScreen(ref PanelX, ref PanelY, PanelWidth, PanelHeight);
        }

        public static void ResetSession()
        {
            IsOpen = false;
            _isDragging = false;
            UIRenderer.UnregisterPanelBounds(PanelId);
        }

        public static void Toggle()
        {
            if (!UIManager.CanRender) return;
            ClampLayout();
            IsOpen = !IsOpen;
            if (IsOpen)
            {
                UIRenderer.BringToFront(PanelId);
                SoundEngine.PlaySound(SoundID.MenuOpen);
            }
            else
            {
                UIRenderer.UnregisterPanelBounds(PanelId);
                SoundEngine.PlaySound(SoundID.MenuClose);
            }
        }

        public static void Update()
        {
            if (!IsOpen || !UIManager.CanRender)
            {
                _isDragging = false;
                UIRenderer.UnregisterPanelBounds(PanelId);
                return;
            }

            ClampLayout();
            UIRenderer.RegisterPanelBounds(PanelId, PanelX, PanelY, PanelWidth, PanelHeight);
        }

        public static void Draw()
        {
            if (!IsOpen || !UIManager.CanRender) return;

            ClampLayout();
            int mouseX = UIRenderer.MouseX;
            int mouseY = UIRenderer.MouseY;

            // 拖动处理
            bool headerHover = mouseX >= PanelX && mouseX <= PanelX + PanelWidth - 36 && mouseY >= PanelY && mouseY <= PanelY + 34;
            if (_isDragging)
            {
                Main.player[Main.myPlayer].mouseInterface = true;
                if (Main.mouseLeft)
                {
                    PanelX = mouseX - _dragOffsetX;
                    PanelY = mouseY - _dragOffsetY;
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

            ClampLayout();
            UIRenderer.RegisterPanelBounds(PanelId, PanelX, PanelY, PanelWidth, PanelHeight);

            // 1. 窗口底板与边框
            UIRenderer.DrawPanel(PanelX, PanelY, PanelWidth, PanelHeight, 18, 22, 35, 245);
            UIRenderer.DrawRectOutline(PanelX, PanelY, PanelWidth, PanelHeight, 70, 140, 230, 255, 2);

            // 2. 标题栏
            UIRenderer.DrawRect(PanelX, PanelY, PanelWidth, 34, 26, 36, 58, 255);
            UIRenderer.DrawText("结构定位与快捷键", PanelX + 16, PanelY + 8, 100, 225, 255, 255);

            // 关闭按钮
            int closeX = PanelX + PanelWidth - 28;
            int closeY = PanelY + 6;
            bool closeHover = mouseX >= closeX && mouseX <= closeX + 22 && mouseY >= closeY && mouseY <= closeY + 22;
            UIRenderer.DrawRect(closeX, closeY, 22, 22, (byte)(closeHover ? 220 : 50), 50, 50, 255);
            UIRenderer.DrawText("X", closeX + 6, closeY + 3, 255, 255, 255, 255);
            if (closeHover && UIRenderer.MouseLeftClick)
            {
                Toggle();
                UIRenderer.ConsumeClick();
                return;
            }

            // 分割中线
            int splitX = PanelX + PanelWidth / 2 - 15;
            UIRenderer.DrawRect(splitX, PanelY + 40, 2, PanelHeight - 50, 50, 70, 100, 200);

            // ================= 左侧：10大结构定位球 =================
            PanelLayout.DrawFittedText("点击定位并点亮小地图", PanelX + 14, PanelY + 42, splitX - PanelX - 28, 180, 210, 240);

            StructureLocator.StructureType[] types = new StructureLocator.StructureType[]
            {
                StructureLocator.StructureType.Aether,
                StructureLocator.StructureType.Dungeon,
                StructureLocator.StructureType.Temple,
                StructureLocator.StructureType.PlanteraBulb,
                StructureLocator.StructureType.EnchantedSword,
                StructureLocator.StructureType.FloatingIsland,
                StructureLocator.StructureType.Pyramid,
                StructureLocator.StructureType.BeeHive,
                StructureLocator.StructureType.MarbleCave,
                StructureLocator.StructureType.GraniteCave
            };

            int startY = PanelY + 66;
            int btnHeight = Math.Min(31, (PanelHeight - 100) / 10 - 4);
            int spacing = 4;

            for (int i = 0; i < types.Length; i++)
            {
                var st = types[i];
                int btnY = startY + i * (btnHeight + spacing);
                int btnW = splitX - PanelX - 28;
                int btnX = PanelX + 14;

                bool hover = mouseX >= btnX && mouseX <= btnX + btnW && mouseY >= btnY && mouseY <= btnY + btnHeight;
                byte bg = (byte)(hover ? 70 : 38);

                UIRenderer.DrawRect(btnX, btnY, btnW, btnHeight, bg, (byte)(bg + 20), (byte)(bg + 50), 255);
                UIRenderer.DrawRectOutline(btnX, btnY, btnW, btnHeight, (byte)(hover ? 120 : 60), 160, 240, 255, 1);

                string name = StructureLocator.GetStructureName(st);
                PanelLayout.DrawFittedText($"定位 {name}", btnX + 10, btnY + (btnHeight - 16) / 2, btnW - 20, (byte)(hover ? 255 : 220), 240, 255);

                if (hover && UIRenderer.MouseLeftClick)
                {
                    StructureLocator.LocateAndReveal(st);
                    UIRenderer.ConsumeClick();
                }
            }

            // ================= 右侧：模组快捷键速查表 =================
            int rightX = splitX + 14;
            PanelLayout.DrawFittedText("当前快捷键（悬停查看说明）", rightX, PanelY + 42, PanelX + PanelWidth - rightX - 14, 255, 220, 140);

            (string id, string key, string name, string desc)[] keybinds = new (string, string, string, string)[]
            {
                ("player-stats", "Y", "角色属性面板", "实时攻防暴击穿透监控"),
                ("structure-locator", "G", "结构定位球", "本面板/10大结构全图扫描"),
                ("quick-nurse", "H", "护士瞬间治疗", "一键回血清Debuff(按原版收费)"),
                ("npc-home", "N", "城镇NPC回家", "一键将所有NPC召回各自房间"),
                ("open-bank", ".", "随身猪猪钱罐", "无需放置直接打开储钱罐"),
                ("super-vault", "B", "随身大背包", "100格额外独立随身仓库"),
                ("space-wand", "P", "快速放置", "开启后手持方块/平台/墙拖拽铺设"),
                ("liquid-wand", "L", "液体魔杖切换", "关/水/岩浆/微光/蜂蜜 [;]吸 [']放"),
                ("extreme-storage", "K", "终极存储大面板", "集中仓储、快速堆叠分类存取"),
                ("spawn-rate-control", "[", "刷怪率/节日控制", "0～100倍；联机由服务器控制"),
                ("autofisher", "Z", "自动钓鱼机", "背包有鱼竿和鱼饵，在液体边开启"),
                ("target-dummy", "X", "测试假人桩", "鼠标处生成/移除靶子并测DPS"),
                ("weather-control", "U", "气象控制切换", "切换下雨和放晴"),
                ("toggle-vein-mining", "V", "切换连锁挖矿", "相连矿石按范围和数量上限挖掘"),
                ("toggle-speed-boost", "\\", "切换工具加速", "暂停或恢复挖掘、砍伐和放置加速"),
                ("force-vein-mine", "Shift", "强制连锁（按住）", "临时开启连锁，松开保持原开关")
            };

            int keyStartY = PanelY + 66;
            int keyLineHeight = Math.Min(24, (PanelHeight - 110) / keybinds.Length);
            string description = "";

            for (int i = 0; i < keybinds.Length; i++)
            {
                var item = keybinds[i];
                int lineY = keyStartY + i * keyLineHeight;

                string key = Mod.GetKeyLabel(item.id, item.key + "(默认)");
                int rightWidth = PanelX + PanelWidth - rightX - 14;
                int keyWidth = Math.Min(rightWidth / 2, Math.Max(30, (int)(UIRenderer.MeasureText(key) * 0.8f) + 10));
                UIRenderer.DrawRect(rightX, lineY, keyWidth, keyLineHeight - 2, 45, 55, 80, 255);
                PanelLayout.DrawFittedText(key, rightX + 5, lineY + 1, keyWidth - 10, 255, 255, 180);
                PanelLayout.DrawFittedText(item.name, rightX + keyWidth + 8, lineY + 1,
                    rightWidth - keyWidth - 8, 220, 240, 255);
                if (mouseX >= rightX && mouseX <= rightX + rightWidth && mouseY >= lineY && mouseY < lineY + keyLineHeight)
                    description = key + "：" + item.desc;
            }
            PanelLayout.DrawFittedText(description, rightX, PanelY + PanelHeight - 50,
                PanelX + PanelWidth - rightX - 14, 180, 200, 220);
            if (StructureLocator.IsScanning)
                UIRenderer.DrawTextSmall($"扫描进度：{StructureLocator.ScanProgress:P0}", PanelX + 14, PanelY + PanelHeight - 24, 140, 220, 255, 255);
        }
    }
}




