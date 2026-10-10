using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using TerrariaModder.Core.UI;
using QualityOfTerraria.Patches;
using QualityOfTerraria.Networking;
using TerrariaModder.Core.Net;

namespace QualityOfTerraria.UI
{
    public class BuffAndSpawnRateUI
    {
        public const string PanelId = "QualityOfTerraria.BuffAndSpawnRate";

        public static bool IsOpen { get; set; } = false;

        public static int PanelX = 320;
        public static int PanelY = 220;
        public static int PanelWidth = 360;
        public static int PanelHeight = 370;

        private static bool _isDragging = false;
        private static int _dragOffsetX = 0;
        private static int _dragOffsetY = 0;

        public static float SpawnRateMultiplier = 1.0f; // 0.0x ~ 100.0x
        private static bool _sliderDragging;
        private static float _sliderPreview;

        private static void ClampLayout()
        {
            PanelWidth = Math.Min(360, PanelLayout.ScreenWidth);
            PanelHeight = Math.Min(370, PanelLayout.ScreenHeight);
            PanelLayout.ClampToScreen(ref PanelX, ref PanelY, PanelWidth, PanelHeight);
        }

        public static void ResetSession()
        {
            IsOpen = false;
            _isDragging = false;
            _sliderDragging = false;
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
                _sliderDragging = false;
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
            UIRenderer.DrawPanel(PanelX, PanelY, PanelWidth, PanelHeight, 18, 22, 36, 240);
            UIRenderer.DrawRectOutline(PanelX, PanelY, PanelWidth, PanelHeight, 60, 140, 220, 255, 2);

            // 2. 标题栏
            UIRenderer.DrawRect(PanelX, PanelY, PanelWidth, 32, 28, 42, 68, 255);
            UIRenderer.DrawText("刷怪与世界控制", PanelX + 12, PanelY + 7, 255, 255, 255, 255);

            // 关闭按钮
            int closeBtnX = PanelX + PanelWidth - 28;
            int closeBtnY = PanelY + 5;
            bool closeHover = mouseX >= closeBtnX && mouseX <= closeBtnX + 22 && mouseY >= closeBtnY && mouseY <= closeBtnY + 22;
            UIRenderer.DrawRect(closeBtnX, closeBtnY, 22, 22, (byte)(closeHover ? 220 : 50), 50, 50, 255);
            UIRenderer.DrawText("X", closeBtnX + 6, closeBtnY + 3, 255, 255, 255, 255);
            if (closeHover && UIRenderer.MouseLeftClick)
            {
                Toggle();
                UIRenderer.ConsumeClick();
                return;
            }

            // 3. 刷怪率调节区域
            int y = PanelY + 46;
            float spacingScale = Math.Min(1f, (PanelHeight - 70) / 274f);
            int optionHeight = (int)(28 * spacingScale);
            SpawnRateMultiplier = WorldQoLCompletionPatches.SpawnRateMultiplier;
            UIRenderer.DrawText($"敌怪生成倍率：{(_sliderDragging ? _sliderPreview : SpawnRateMultiplier):0.#}×", PanelX + 16, y, 255, 230, 120, 255);
            y += (int)(28 * spacingScale);

            // 快捷倍率按钮
            float[] presets = { 0.0f, 0.5f, 1.0f, 2.0f, 5.0f, 10.0f, 30.0f, 100.0f };
            string[] presetLabels = { "0×", "0.5×", "1×", "2×", "5×", "10×", "30×", "100×" };
            int btnW = (PanelWidth - 32 - (presets.Length - 1) * 4) / presets.Length;
            int btnH = (int)(24 * spacingScale);

            for (int i = 0; i < presets.Length; i++)
            {
                int bx = PanelX + 16 + i * (btnW + 4);
                bool isCur = Math.Abs(SpawnRateMultiplier - presets[i]) < 0.05f;
                bool hover = mouseX >= bx && mouseX <= bx + btnW && mouseY >= y && mouseY <= y + btnH;

                byte r = isCur ? (byte)40 : (hover ? (byte)50 : (byte)26);
                byte g = isCur ? (byte)120 : (hover ? (byte)70 : (byte)34);
                byte b = isCur ? (byte)200 : (hover ? (byte)100 : (byte)55);

                UIRenderer.DrawRect(bx, y, btnW, btnH, r, g, b, 255);
                UIRenderer.DrawRectOutline(bx, y, btnW, btnH, 80, 160, 240, 255, 1);
                PanelLayout.DrawFittedText(presetLabels[i], bx + 4, y + (btnH - 16) / 2, btnW - 8, 240, 240, 240);

                if (hover && UIRenderer.MouseLeftClick)
                {
                    SubmitSpawnRate(presets[i]);
                    SoundEngine.PlaySound(SoundID.MenuTick);
                    UIRenderer.ConsumeClick();
                }
            }

            DrawSpawnSlider(PanelX + 16, y + btnH + 8, PanelWidth - 32, Math.Max(10, (int)(16 * spacingScale)));
            y += (int)(44 * spacingScale);

            // 4. 节日强制切换
            y += (int)(42 * spacingScale);
            UIRenderer.DrawText("节日事件强制切换：", PanelX + 16, y, 255, 200, 100, 255);
            y += (int)(26 * spacingScale);

            int optionWidth = (PanelWidth - 48) / 2;
            int secondX = PanelX + PanelWidth / 2 + 8;
            DrawToggleOption(PanelX + 16, y, optionWidth, optionHeight, "万圣节", Main.halloween, 0, () =>
            {
                Main.halloween = !Main.halloween;
                SoundEngine.PlaySound(SoundID.MenuTick);
            });

            DrawToggleOption(secondX, y, optionWidth, optionHeight, "圣诞节", Main.xMas, 1, () =>
            {
                Main.xMas = !Main.xMas;
                SoundEngine.PlaySound(SoundID.MenuTick);
            });

            // 5. 世界秘密种子特性
            y += (int)(42 * spacingScale);
            UIRenderer.DrawText("世界种子机制切换：", PanelX + 16, y, 140, 220, 255, 255);
            y += (int)(26 * spacingScale);

            DrawToggleOption(PanelX + 16, y, optionWidth, optionHeight, "醉酒世界", Main.drunkWorld, 2, () =>
            {
                Main.drunkWorld = !Main.drunkWorld;
                SoundEngine.PlaySound(SoundID.MenuTick);
            });

            DrawToggleOption(secondX, y, optionWidth, optionHeight, "天顶世界", Main.zenithWorld, 3, () =>
            {
                Main.zenithWorld = !Main.zenithWorld;
                SoundEngine.PlaySound(SoundID.MenuTick);
            });

            y += (int)(34 * spacingScale);
            DrawToggleOption(PanelX + 16, y, optionWidth, optionHeight, "颠倒世界", Main.remixWorld, 4, () =>
            {
                Main.remixWorld = !Main.remixWorld;
                SoundEngine.PlaySound(SoundID.MenuTick);
            });

            DrawToggleOption(secondX, y, optionWidth, optionHeight, "饥荒世界", Main.dontStarveWorld, 5, () =>
            {
                Main.dontStarveWorld = !Main.dontStarveWorld;
                SoundEngine.PlaySound(SoundID.MenuTick);
            });

            PanelLayout.DrawFittedText(WorldQoLCompletionPatches.SpawnRateStatus,
                PanelX + 16, PanelY + PanelHeight - 26, PanelWidth - 32, 180, 200, 220);
        }

        internal static float SliderValue(int mouseX, int x, int width)
        {
            return width <= 0 ? 0f : (float)Math.Round(Math.Max(0f, Math.Min(100f, (mouseX - x) * 100f / width)), 1);
        }

        private static void SubmitSpawnRate(float value)
        {
            if (Main.netMode == 1)
            {
                if (!NetSync.LocalPlayerIsAdmin) { Main.NewText("只有房主或管理员可以调整房间刷怪倍率。", 255, 180, 100); return; }
                ToolNetwork.Request("spawn-rate", -1, (int)Math.Round(value * 10f));
            }
            else if (!WorldQoLCompletionPatches.SetSpawnRate(value)) Main.NewText("刷怪倍率保存失败，已保留原值。", 255, 180, 100);
        }

        private static void DrawSpawnSlider(int x, int y, int width, int height)
        {
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX <= x + width && UIRenderer.MouseY >= y - 4 && UIRenderer.MouseY <= y + height + 4;
            if (hover) Main.LocalPlayer.mouseInterface = true;
            if (hover && UIRenderer.MouseLeftClick)
            {
                if (Main.netMode == 1 && !NetSync.LocalPlayerIsAdmin) Main.NewText("只有房主或管理员可以调整房间刷怪倍率。", 255, 180, 100);
                else _sliderDragging = true;
                UIRenderer.ConsumeClick();
            }
            if (_sliderDragging)
            {
                Main.LocalPlayer.mouseInterface = true;
                _sliderPreview = SliderValue(UIRenderer.MouseX, x, width);
                if (!Main.mouseLeft) { _sliderDragging = false; SubmitSpawnRate(_sliderPreview); }
            }
            float value = _sliderDragging ? _sliderPreview : WorldQoLCompletionPatches.SpawnRateMultiplier;
            int thumb = x + (int)Math.Round(width * value / 100f);
            UIRenderer.DrawRect(x, y + height / 2 - 2, width, 4, 55, 80, 115, 255);
            UIRenderer.DrawRect(x, y + height / 2 - 2, Math.Max(0, thumb - x), 4, 80, 170, 240, 255);
            UIRenderer.DrawRect(Math.Max(x, Math.Min(x + width - 8, thumb - 4)), y, 8, height, 170, 220, 255, 255);
        }

        private static void DrawToggleOption(int x, int y, int w, int h, string label, bool state, int control, Action onToggle)
        {
            int mouseX = UIRenderer.MouseX;
            int mouseY = UIRenderer.MouseY;
            bool hover = mouseX >= x && mouseX <= x + w && mouseY >= y && mouseY <= y + h;

            byte r = state ? (byte)30 : (hover ? (byte)45 : (byte)22);
            byte g = state ? (byte)100 : (hover ? (byte)55 : (byte)28);
            byte b = state ? (byte)50 : (hover ? (byte)75 : (byte)40);

            UIRenderer.DrawRect(x, y, w, h, r, g, b, 255);
            UIRenderer.DrawRectOutline(x, y, w, h, (byte)(state ? 100 : 60), (byte)(state ? 220 : 100), (byte)(state ? 120 : 120), 255, 1);
            PanelLayout.DrawFittedText(label, x + 8, y + (h - 16) / 2, w - 16,
                (byte)(state ? 255 : 200), (byte)(state ? 255 : 200), (byte)(state ? 255 : 200));

            if (hover && UIRenderer.MouseLeftClick)
            {
                if (Main.netMode == 1)
                {
                    if (NetSync.LocalPlayerIsAdmin) ToolNetwork.Request("world-toggle", -1, control, state ? 0 : 1);
                    else Main.NewText("只有房主或管理员可以调整共享世界规则。", 255, 180, 100);
                }
                else onToggle?.Invoke();
                UIRenderer.ConsumeClick();
            }
        }
    }
}

