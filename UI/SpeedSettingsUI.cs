using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TerrariaModder.Core.Config;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.UI
{
    internal static class SpeedSettingsUI
    {
        private const string InputId = "QualityOfTerraria.SpeedInput";
        private static readonly FieldInfo BlockInput = AccessTools.Field(typeof(ModMenu), "_blockInput");
        private static readonly MethodInfo SaveConfig = AccessTools.Method(typeof(ModMenu), "AutoSaveConfig");
        private static string _dragKey, _editKey, _text;
        private static float _preview;
        private static bool _activeRowDrawn;

        internal static void Reset()
        {
            if (_editKey != null) UIRenderer.DisableTextInput();
            _dragKey = _editKey = null;
            UIRenderer.UnregisterKeyInputBlock(InputId);
        }

        internal static bool Draw(int x, int y, int width, ConfigPropertyMeta meta, ModConfig config, string modId)
        {
            if (modId != "quality-of-terraria" || !ReferenceEquals(config, Mod.Config) ||
                (meta.Key != nameof(QualityOfTerrariaConfig.ToolSpeedMultiplier) && meta.Key != nameof(QualityOfTerrariaConfig.PlacementSpeedMultiplier))) return true;
            if (_editKey == meta.Key || _dragKey == meta.Key) _activeRowDrawn = true;
            bool blocked = (bool)(BlockInput.GetValue(null) ?? false);
            float value = Convert.ToSingle(meta.GetValue(config));
            int fieldX = x + width - 76, sliderX = x + Math.Min(180, width / 2), sliderWidth = Math.Max(12, fieldX - sliderX - 12);
            int mx = UIRenderer.MouseX, my = UIRenderer.MouseY;
            bool rowHover = mx >= x && mx < x + width && my >= y && my < y + 26;
            if (rowHover) TerrariaModder.Core.UI.Widgets.Tooltip.Set(meta.Description);
            if (rowHover && UIManager.CanRender) Main.LocalPlayer.mouseInterface = true;
            PanelLayout.DrawFittedText(meta.Label, x + 5, y + 5, sliderX - x - 10, 235, 240, 255);
            bool fieldHover = mx >= fieldX && mx < fieldX + 70 && my >= y && my < y + 26;
            if (_editKey == meta.Key && !blocked)
            {
                UIRenderer.EnableTextInput();
                _text = UIRenderer.GetInputText(_text);
                if (_text.Length > 8) _text = _text.Substring(0, 8);
                if (UIRenderer.CheckInputEscape()) Reset();
                else if (Main.keyState.IsKeyDown(Keys.Enter) || (UIRenderer.MouseLeftClick && !fieldHover))
                {
                    float parsed;
                    if (float.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed)) Commit(meta, config, modId, parsed);
                    Reset();
                }
            }
            bool sliderHover = mx >= sliderX && mx <= sliderX + sliderWidth && my >= y && my < y + 26;
            if (!blocked && sliderHover && UIRenderer.MouseLeftClick)
            {
                Reset(); _dragKey = meta.Key; UIRenderer.ConsumeClick();
                _activeRowDrawn = true;
            }
            if (_dragKey == meta.Key)
            {
                _preview = QualityOfTerrariaConfig.NormalizeSpeed(1f + (mx - sliderX) * 3f / sliderWidth);
                if (!UIRenderer.MouseLeft || blocked) { if (!blocked) Commit(meta, config, modId, _preview); _dragKey = null; }
            }
            if (!blocked && fieldHover && UIRenderer.MouseLeftClick)
            {
                Reset(); _editKey = meta.Key; _text = value.ToString("0.0", CultureInfo.InvariantCulture);
                _activeRowDrawn = true;
                UIRenderer.ClearInput(); UIRenderer.RegisterKeyInputBlock(InputId); UIRenderer.ConsumeClick();
            }
            float shown = _dragKey == meta.Key ? _preview : Convert.ToSingle(meta.GetValue(config));
            int thumb = sliderX + (int)((shown - 1f) * sliderWidth / 3f);
            UIRenderer.DrawRect(sliderX, y + 11, sliderWidth, 4, 55, 80, 115, 255);
            UIRenderer.DrawRect(thumb - 3, y + 5, 6, 16, 170, 220, 255, 255);
            UIRenderer.DrawRect(fieldX, y + 2, 70, 22, 28, 50, 75, 255);
            PanelLayout.DrawFittedText(_editKey == meta.Key ? _text + "|" : shown.ToString("0.0") + "×", fieldX + 5, y + 5, 60, 235, 240, 255);
            return false;
        }

        private static void Commit(ConfigPropertyMeta meta, ModConfig config, string modId, float value)
        {
            meta.SetValue(config, QualityOfTerrariaConfig.NormalizeSpeed(value));
            SaveConfig.Invoke(null, new object[] { modId, meta, config });
        }

        [HarmonyPatch(typeof(ModMenu), "ToggleMenu")]
        private static class ClosePatch
        {
            private static void Postfix() => Reset();
        }

        [HarmonyPatch(typeof(ModMenu), "Draw")]
        private static class VisibilityPatch
        {
            private static void Prefix() { _activeRowDrawn = false; }
            private static void Postfix() { if (!_activeRowDrawn) Reset(); }
        }
    }
}
