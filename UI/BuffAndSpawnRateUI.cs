using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using TerrariaModder.Core.UI;
using QualityOfTerraria.Patches;
using QualityOfTerraria.Networking;
using QualityOfTerraria.Features;
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
        public static int PanelHeight = 450;

        private static bool _isDragging = false;
        private static int _dragOffsetX = 0;
        private static int _dragOffsetY = 0;

        private static bool _sliderDragging;
        private static float _sliderPreview;
        private static int _spawnSliderKind;
        private static int _tab, _timePreview;
        private static bool _timeDragging, _timeEdited;

        private static void ClampLayout()
        {
            PanelWidth = Math.Min(360, PanelLayout.ScreenWidth);
            PanelHeight = Math.Min(450, PanelLayout.ScreenHeight);
            PanelLayout.ClampToScreen(ref PanelX, ref PanelY, PanelWidth, PanelHeight);
        }

        public static void ResetSession()
        {
            IsOpen = false;
            _isDragging = false;
            _sliderDragging = _timeDragging = _timeEdited = false;
            UIRenderer.UnregisterPanelBounds(PanelId);
        }

        public static void Toggle()
        {
            if (!UIManager.CanRender) return;
            ClampLayout();
            IsOpen = !IsOpen;
            if (IsOpen)
            {
                _timePreview = WorldControlSystem.GetClockMinute(); _timeEdited = false;
                UIRenderer.BringToFront(PanelId);
                SoundEngine.PlaySound(SoundID.MenuOpen);
            }
            else
            {
                _sliderDragging = _timeDragging = false;
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

            int tabY = PanelY + 40, tabWidth = (PanelWidth - 40) / 3;
            string[] tabs = { "刷怪与时间", "节日与事件", "世界特性" };
            for (int i = 0; i < tabs.Length; i++)
                if (DrawAction(tabs[i], PanelX + 12 + i * (tabWidth + 8), tabY, tabWidth, 26, _tab == i))
                { _tab = i; _sliderDragging = _timeDragging = false; }
            int y = tabY + 42, x = PanelX + 16, width = PanelWidth - 32;
            int row = Math.Max(22, Math.Min(32, (PanelHeight - 145) / 6));
            if (_tab == 0)
            {
                PanelLayout.DrawFittedText("敌对生物倍率：" + SpawnValueText(0) + "（上限500×）", x, y, width, 255, 230, 120);
                y += row;
                DrawSpawnSlider(x, y + 4, width, 14, 0);
                y += row;
                DrawSpawnPresets(x, y, width, row, 0);
                y += row;
                PanelLayout.DrawFittedText("小动物倍率：" + SpawnValueText(1) + "（兔子、鸟、蝴蝶等）", x, y, width, 255, 230, 120);
                y += row;
                DrawSpawnSlider(x, y + 4, width, 14, 1);
                y += row;
                DrawSpawnPresets(x, y, width, row, 1);
                y += row;
                if (!_timeEdited && !_timeDragging) _timePreview = WorldControlSystem.GetClockMinute();
                PanelLayout.DrawFittedText("时间：" + ClockText(WorldControlSystem.GetClockMinute()) + " → " + ClockText(_timePreview), x, y, width, 190, 225, 255);
                y += row;
                DrawTimeSlider(x, y + 4, width - 70, 16);
                if (DrawAction("应用", x + width - 60, y, 60, 26))
                {
                    if (Main.netMode == 1) ToolNetwork.Request("world-time", -1, _timePreview);
                    else ApplyLocal(() => WorldControlSystem.SetTime(_timePreview));
                    _timeEdited = false;
                }
                PanelLayout.DrawFittedText("00:00                         23:59", x, y + 25, width - 70, 160, 180, 205);
                PanelLayout.DrawFittedText("白天：04:30–19:30    夜晚：19:30–04:30", x, y + 43, width, 175, 205, 225);
            }
            else if (_tab == 1)
            {
                int half = (width - 10) / 2;
                DrawToggleOption(x, y, half, row - 4, "万圣节", Main.halloween, 0, () => WorldControlSystem.SetHoliday(0, !Main.halloween));
                DrawToggleOption(x + half + 10, y, half, row - 4, "圣诞节", Main.xMas, 1, () => WorldControlSystem.SetHoliday(1, !Main.xMas));
                y += row + 8;
                PanelLayout.DrawFittedText("特殊事件（夜间：血月／南瓜月／霜月）", x, y, width, 255, 220, 140);
                y += row;
                string[] events = { "血月", "日食", "南瓜月", "霜月" };
                for (int i = 0; i < events.Length; i++)
                {
                    bool active = WorldControlSystem.EventActive(i);
                    PanelLayout.DrawFittedText(events[i] + (active ? "：进行中" : "：未开启"), x, y, width - 80, 215, 235, 250);
                    if (DrawAction(active ? "结束" : "开始", x + width - 70, y - 3, 70, row - 4, active))
                    {
                        int id = i;
                        if (Main.netMode == 1) ToolNetwork.Request("world-event", -1, id, active ? 0 : 1);
                        else ApplyLocal(() => WorldControlSystem.SetEvent(id, !active));
                    }
                    y += row;
                }
            }
            else
            {
                PanelLayout.DrawFittedText("仅切换运行机制，不重新生成地形", x, y, width, 255, 220, 140);
                y += row + 8;
                int half = (width - 10) / 2;
                DrawToggleOption(x, y, half, row, "醉酒世界", Main.drunkWorld, 2, () => Main.drunkWorld = !Main.drunkWorld);
                DrawToggleOption(x + half + 10, y, half, row, "天顶世界", Main.zenithWorld, 3, () => Main.zenithWorld = !Main.zenithWorld);
                y += row + 8;
                DrawToggleOption(x, y, half, row, "颠倒世界", Main.remixWorld, 4, () => Main.remixWorld = !Main.remixWorld);
                DrawToggleOption(x + half + 10, y, half, row, "饥荒世界", Main.dontStarveWorld, 5, () => Main.dontStarveWorld = !Main.dontStarveWorld);
            }
            PanelLayout.DrawFittedText(WorldQoLCompletionPatches.SpawnRateStatus,
                PanelX + 16, PanelY + PanelHeight - 23, PanelWidth - 32, 180, 200, 220);
        }

        private static string ClockText(int minute) => (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00");

        private static string SpawnValueText(int kind)
        {
            float value = _sliderDragging && _spawnSliderKind == kind ? _sliderPreview :
                (kind == 0 ? WorldQoLCompletionPatches.HostileSpawnRateMultiplier : WorldQoLCompletionPatches.CritterSpawnRateMultiplier);
            return value.ToString("0.#") + "×";
        }

        private static void ApplyLocal(Func<string> action)
        {
            try { Main.NewText(action(), 150, 230, 255); }
            catch (Exception error) { Main.NewText(error.Message, 255, 180, 100); }
        }

        private static bool DrawAction(string text, int x, int y, int width, int height, bool selected = false)
        {
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + height;
            if (hover) Main.LocalPlayer.mouseInterface = true;
            UIRenderer.DrawRect(x, y, width, height, (byte)(hover ? 50 : 28), (byte)(selected ? 100 : 55), 85, 255);
            PanelLayout.DrawFittedText(text, x + 5, y + Math.Max(0, (height - 16) / 2), width - 10, 235, 240, 255);
            if (!hover || !UIRenderer.MouseLeftClick) return false;
            UIRenderer.ConsumeClick();
            return true;
        }

        private static void DrawTimeSlider(int x, int y, int width, int height)
        {
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX <= x + width && UIRenderer.MouseY >= y - 3 && UIRenderer.MouseY <= y + height + 3;
            if (hover && UIRenderer.MouseLeftClick) { _timeDragging = true; UIRenderer.ConsumeClick(); }
            if (_timeDragging)
            {
                Main.LocalPlayer.mouseInterface = true;
                _timePreview = Math.Max(0, Math.Min(1439, (int)Math.Round((UIRenderer.MouseX - x) * 1439.0 / Math.Max(1, width))));
                _timeEdited = true;
                if (!Main.mouseLeft) _timeDragging = false;
            }
            int thumb = x + (int)(_timePreview * width / 1439.0);
            UIRenderer.DrawRect(x, y + height / 2 - 2, width, 4, 55, 80, 115, 255);
            UIRenderer.DrawRect(Math.Max(x, Math.Min(x + width - 8, thumb - 4)), y, 8, height, 170, 220, 255, 255);
        }

        internal static float SliderValue(int mouseX, int x, int width)
        {
            return width <= 0 ? 0f : (float)Math.Round(Math.Max(0f, Math.Min(500f, (mouseX - x) * 500f / width)), 1);
        }

        private static void SubmitSpawnRate(int kind, float value)
        {
            if (Main.netMode == 1)
            {
                ToolNetwork.Request("spawn-rate-one", -1, kind, (int)Math.Round(value * 10f));
            }
            else if (!WorldQoLCompletionPatches.SetSingleSpawnRate(kind, value)) Main.NewText("刷怪倍率保存失败，已保留原值。", 255, 180, 100);
        }

        private static void DrawSpawnSlider(int x, int y, int width, int height, int kind)
        {
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX <= x + width && UIRenderer.MouseY >= y - 4 && UIRenderer.MouseY <= y + height + 4;
            if (hover) Main.LocalPlayer.mouseInterface = true;
            if (hover && UIRenderer.MouseLeftClick)
            {
                if (Main.netMode == 1 && !NetSync.LocalPlayerIsAdmin) Main.NewText("只有房主或管理员可以调整房间刷怪倍率。", 255, 180, 100);
                _sliderDragging = true; _spawnSliderKind = kind;
                UIRenderer.ConsumeClick();
            }
            if (_sliderDragging && _spawnSliderKind == kind)
            {
                Main.LocalPlayer.mouseInterface = true;
                _sliderPreview = SliderValue(UIRenderer.MouseX, x, width);
                if (!Main.mouseLeft)
                {
                    _sliderDragging = false;
                    SubmitSpawnRate(kind, _sliderPreview);
                }
            }
            float current = kind == 0 ? WorldQoLCompletionPatches.HostileSpawnRateMultiplier : WorldQoLCompletionPatches.CritterSpawnRateMultiplier;
            float value = _sliderDragging && _spawnSliderKind == kind ? _sliderPreview : current;
            int thumb = x + (int)Math.Round(width * value / 500f);
            UIRenderer.DrawRect(x, y + height / 2 - 2, width, 4, 55, 80, 115, 255);
            UIRenderer.DrawRect(x, y + height / 2 - 2, Math.Max(0, thumb - x), 4, 80, 170, 240, 255);
            UIRenderer.DrawRect(Math.Max(x, Math.Min(x + width - 8, thumb - 4)), y, 8, height, 170, 220, 255, 255);
        }

        private static void DrawSpawnPresets(int x, int y, int width, int height, int kind)
        {
            float[] presets = { 0f, 1f, 5f, 10f, 20f, 50f, 100f, 200f, 500f };
            int gap = 3;
            int buttonWidth = Math.Max(20, (width - gap * (presets.Length - 1)) / presets.Length);
            for (int i = 0; i < presets.Length; i++)
            {
                int buttonX = x + i * (buttonWidth + gap);
                if (DrawAction(presets[i].ToString("0") + "×", buttonX, y, buttonWidth, height - 5,
                    Math.Abs((kind == 0 ? WorldQoLCompletionPatches.HostileSpawnRateMultiplier : WorldQoLCompletionPatches.CritterSpawnRateMultiplier) - presets[i]) < 0.01f))
                    SubmitSpawnRate(kind, presets[i]);
            }
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
                    ToolNetwork.Request("world-toggle", -1, control, state ? 0 : 1);
                }
                else onToggle?.Invoke();
                UIRenderer.ConsumeClick();
            }
        }
    }
}

