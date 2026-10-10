using System;
using Terraria;
using Terraria.GameInput;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.UI
{
    public static class PanelLayout
    {
        public static int ScreenWidth => Math.Max(1, (int)(PlayerInput.RealScreenWidth / Main.UIScale));
        public static int ScreenHeight => Math.Max(1, (int)(PlayerInput.RealScreenHeight / Main.UIScale));

        public static void ClampToScreen(ref int x, ref int y, int width, int height)
        {
            x = Math.Max(0, Math.Min(Math.Max(0, ScreenWidth - width), x));
            y = Math.Max(0, Math.Min(Math.Max(0, ScreenHeight - height), y));
        }

        public static void DrawFittedText(string text, int x, int y, int width, byte r, byte g, byte b)
        {
            float scale = Math.Min(0.8f, Math.Max(1, width) / (float)Math.Max(1, UIRenderer.MeasureText(text)));
            UIRenderer.DrawTextScaled(text, x, y, r, g, b, 255, scale);
        }
    }
}
