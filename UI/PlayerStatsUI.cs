using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.UI
{
    public class PlayerStatsUI
    {
        public const string PanelId = "QualityOfTerraria.PlayerStats";

        public static bool IsOpen { get; set; } = false;

        public static int PanelX = 20;
        public static int PanelY = 260;
        public static int PanelWidth = 320;
        public static int PanelHeight = 360;

        private static bool _isDragging = false;
        private static int _dragOffsetX = 0;
        private static int _dragOffsetY = 0;

        private static void ClampLayout()
        {
            PanelWidth = Math.Min(320, PanelLayout.ScreenWidth);
            PanelHeight = Math.Min(360, PanelLayout.ScreenHeight);
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
            if (!IsOpen || !UIManager.CanRender)
                return;

            Player player = Main.LocalPlayer;
            if (player == null || !player.active)
                return;

            ClampLayout();
            int mouseX = UIRenderer.MouseX;
            int mouseY = UIRenderer.MouseY;

            // 拖动处理
            bool headerHover = mouseX >= PanelX && mouseX <= PanelX + PanelWidth - 30 && mouseY >= PanelY && mouseY <= PanelY + 32;
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

            // 1. 底板与边框
            UIRenderer.DrawPanel(PanelX, PanelY, PanelWidth, PanelHeight, 16, 20, 32, 235);
            UIRenderer.DrawRectOutline(PanelX, PanelY, PanelWidth, PanelHeight, 80, 160, 220, 255, 1);

            // 2. 标题栏
            UIRenderer.DrawRect(PanelX, PanelY, PanelWidth, 32, 26, 38, 62, 255);
            UIRenderer.DrawText("角色实时攻防属性", PanelX + 12, PanelY + 7, 255, 255, 255, 255);

            // 关闭按钮
            int closeBtnX = PanelX + PanelWidth - 28;
            int closeBtnY = PanelY + 4;
            bool closeHover = mouseX >= closeBtnX && mouseX <= closeBtnX + 22 && mouseY >= closeBtnY && mouseY <= closeBtnY + 22;
            UIRenderer.DrawRect(closeBtnX, closeBtnY, 22, 22, (byte)(closeHover ? 220 : 50), 50, 50, 255);
            UIRenderer.DrawText("X", closeBtnX + 6, closeBtnY + 3, 255, 255, 255, 255);
            if (closeHover && UIRenderer.MouseLeftClick)
            {
                Toggle();
                UIRenderer.ConsumeClick();
                return;
            }

            // 3. 各项实时属性数值
            int lineY = PanelY + 42;
            int lineHeight = Math.Min(24, (PanelHeight - 48) / 13);

            float meleeDmg = (player.meleeDamage - 1f) * 100f;
            float rangedDmg = (player.rangedDamage - 1f) * 100f;
            float magicDmg = (player.magicDamage - 1f) * 100f;
            float summonDmg = (player.minionDamage - 1f) * 100f;

            int meleeCrit = player.meleeCrit;
            int rangedCrit = player.rangedCrit;
            int magicCrit = player.magicCrit;

            DrawStatLine("近战伤害加成", $"{meleeDmg:+0.0;-0.0;0.0}%", lineY, 255, 160, 160);
            lineY += lineHeight;
            DrawStatLine("近战暴击率", $"{meleeCrit}%", lineY, 255, 180, 180);
            lineY += lineHeight;

            DrawStatLine("远程伤害加成", $"{rangedDmg:+0.0;-0.0;0.0}%", lineY, 180, 255, 180);
            lineY += lineHeight;
            DrawStatLine("远程暴击率", $"{rangedCrit}%", lineY, 200, 255, 200);
            lineY += lineHeight;

            DrawStatLine("魔法伤害加成", $"{magicDmg:+0.0;-0.0;0.0}%", lineY, 180, 200, 255);
            lineY += lineHeight;
            DrawStatLine("魔法暴击率", $"{magicCrit}%", lineY, 200, 220, 255);
            lineY += lineHeight;

            DrawStatLine("召唤伤害加成", $"{summonDmg:+0.0;-0.0;0.0}%", lineY, 255, 220, 160);
            lineY += lineHeight;
            DrawStatLine("仆从 / 哨兵上限", $"{player.maxMinions} / {player.maxTurrets}", lineY, 255, 230, 180);
            lineY += lineHeight;

            DrawStatLine("防御力 / 免伤", $"{player.statDefense} / {player.endurance * 100:0.0}%", lineY, 220, 220, 220);
            lineY += lineHeight;
            DrawStatLine("护甲穿透", $"{player.armorPenetration}", lineY, 255, 180, 120);
            lineY += lineHeight;

            DrawStatLine("生命秒回速度", $"{player.lifeRegen / 2f:0.0} HP/s", lineY, 255, 140, 180);
            lineY += lineHeight;

            DrawStatLine("移动速度加成", $"{(player.moveSpeed - 1f) * 100:+0;-0;0}%", lineY, 160, 240, 255);
            lineY += lineHeight;

            DrawStatLine("基础钓鱼力", $"{player.fishingSkill}", lineY, 120, 220, 240);
        }

        private static void DrawStatLine(string label, string val, int y, byte r, byte g, byte b)
        {
            int valueX = PanelX + PanelWidth / 2;
            PanelLayout.DrawFittedText(label, PanelX + 16, y, PanelWidth / 2 - 24, 200, 200, 200);
            PanelLayout.DrawFittedText(val, valueX, y, PanelWidth / 2 - 16, r, g, b);
        }
    }
}

