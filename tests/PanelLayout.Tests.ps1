$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\UI\PanelLayout.cs') -Raw -Encoding UTF8
$stubs = @"
namespace Terraria {
    public static class Main { public static float UIScale = 1f; public static int screenWidth = 1280, screenHeight = 720; }
}
namespace Terraria.GameInput {
    public static class PlayerInput { public static int RealScreenWidth = 1280, RealScreenHeight = 720; }
}
namespace TerrariaModder.Core.UI {
    public static class UIRenderer {
        public static float LastScale;
        public static int MeasureText(string text) { return text.Length * 20; }
        public static void DrawTextScaled(string text, int x, int y, byte r, byte g, byte b, byte a, float scale) { LastScale = scale; }
    }
}
namespace QualityOfTerraria.UI {
    public static class LayoutRegression {
        private static void Check(bool passed, string label) {
            if (!passed) throw new System.Exception(label);
            System.Console.WriteLine("PASS " + label);
        }
        public static void Run() {
            int x = 400, y = 160;
            PanelLayout.ClampToScreen(ref x, ref y, 560, 620);
            Check(x == 400 && y == 100, "720高度首次打开校正底部");
            Terraria.Main.UIScale = 1.25f;
            PanelLayout.ClampToScreen(ref x, ref y, 560, 500);
            Check(x == 400 && y == 76, "UI比例变化后保留底部操作");
            Terraria.Main.screenWidth = 1024; Terraria.Main.screenHeight = 576;
            Check(PanelLayout.ScreenWidth == 1024 && PanelLayout.ScreenHeight == 576, "更新和绘制阶段共用物理视口");
            x = -50; y = -20;
            PanelLayout.ClampToScreen(ref x, ref y, 1200, 800);
            Check(x == 0 && y == 0, "负坐标与超大窗口保持关闭按钮可达");
            PanelLayout.DrawFittedText("abcdefghij", 0, 0, 60, 0, 0, 0);
            Check(TerrariaModder.Core.UI.UIRenderer.LastScale * 200 <= 60.01f, "长文案按可用宽度缩放");
            PanelLayout.DrawFittedText("ab", 0, 0, 200, 0, 0, 0);
            Check(TerrariaModder.Core.UI.UIRenderer.LastScale == 0.8f, "短文案维持小字体大小");
        }
    }
}
"@
Add-Type -TypeDefinition ($source + "`n" + $stubs)
[QualityOfTerraria.UI.LayoutRegression]::Run()
