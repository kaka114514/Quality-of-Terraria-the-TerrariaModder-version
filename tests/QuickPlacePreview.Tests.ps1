$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') {
    & "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
    exit $LASTEXITCODE
}
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\WandsAndTools.cs') -Raw -Encoding UTF8

function Get-MethodSource([string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "未找到方法：$signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}

$update = Get-MethodSource 'public static void UpdateQuickPlace()'
$uiHit = Get-MethodSource 'private static bool IsMouseOverToolUI()'
$projection = Get-MethodSource 'public static Rectangle GetTileWorldToScreenRect('
$preview = [regex]::Match($source, '(?s)else (if \([^\r\n]+\)\s*\{\s*// 未拖拽时，为鼠标悬停瓦片提供单格准星高亮.*?\r?\n\s*})').Groups[1].Value
if (!$preview) { throw '未找到单格预览绘制代码' }

# 执行生产代码中的输入、投影和预览分支；隔离游戏进程与 GPU 绘制。
$harness = @'
using System;
using Microsoft.Xna.Framework;

public class Player { public bool active = true, dead, mouseInterface, controlUseItem; }
public class View { public Matrix ZoomMatrix; }
public static class Main {
    public static Player LocalPlayer = new Player();
    public static bool playerInventory, gameMenu, drawingPlayerChat, editChest, editSign, mouseRight, mouseLeft;
    public static int mouseX, mouseY;
    public static Vector2 screenPosition;
    public static View GameViewMatrix = new View();
    public static float UIScale;
    public static Vector2 MouseWorld { get { return screenPosition + new Vector2(mouseX, mouseY); } }
}
public static class PlayerInput {
    private static int _worldMouseX, _worldMouseY;
    public static void SetZoom_UI() {
        _worldMouseX = Main.mouseX; _worldMouseY = Main.mouseY;
        Vector2 raw = Vector2.Transform(new Vector2(Main.mouseX, Main.mouseY), Main.GameViewMatrix.ZoomMatrix);
        Main.mouseX = (int)(raw.X / Main.UIScale); Main.mouseY = (int)(raw.Y / Main.UIScale);
    }
    public static void SetZoom_World() { Main.mouseX = _worldMouseX; Main.mouseY = _worldMouseY; }
}
public static class UIRenderer {
    public static int MouseX { get { return Main.mouseX; } }
    public static int MouseY { get { return Main.mouseY; } }
    public static Rectangle LastOutline;
    public static bool HoverPanel;
    public static bool IsMouseOverAnyPanel() { return HoverPanel; }
    public static void DrawRectOutline(int x, int y, int w, int h, int r, int g, int b, int a, int thickness) {
        LastOutline = new Rectangle(x, y, w, h);
    }
}
public static class SoundID { public const int MenuClose = 0; }
public static class SoundEngine { public static void PlaySound(int id) {} }
public static class QuickPlacePreviewRegression {
    public static bool QuickPlaceActive = true;
    private static bool _isDragging;
    private static int _dragStartX = -1, _dragStartY = -1, _dragCurX = -1, _dragCurY = -1;
    private static int _hoverTileX = -1, _hoverTileY = -1;
    private static bool _hasHoverTile;
    private static Rectangle _placedTiles;
    public enum LiquidWandMode { Off }
    public static LiquidWandMode CurrentLiquidMode;
    public static int QuickPlaceHudX = 20, QuickPlaceHudY = 80, QuickPlaceHudW = 300, QuickPlaceHudH = 32;
    public static int LiquidHudX = 20, LiquidHudY = 118, LiquidHudW = 390, LiquidHudH = 32;
    private static void ExecuteDragPlacement(Player player, int x1, int y1, int x2, int y2) {
        _placedTiles = new Rectangle(x1, y1, x2 - x1 + 1, y2 - y1 + 1);
    }
    __UPDATE__
    __UIHIT__
    __PROJECTION__
    private static void DrawIdlePreview() { __PREVIEW__ }

    public static string Run() {
        float[] zooms = { 1f, 1.5f, 1f, 1.5f };
        float[] scales = { 1f, 1f, 1.25f, 1.25f };
        Rectangle[] expected = {
            new Rectangle(720, 416, 16, 16), new Rectangle(600, 354, 24, 24),
            new Rectangle(576, 333, 13, 13), new Rectangle(480, 283, 19, 19)
        };
        int failures = 0;
        string result = "";
        for (int i = 0; i < zooms.Length; i++) {
            for (int drag = 0; drag < 3; drag++) {
                _isDragging = false;
                Main.mouseLeft = drag > 0;
                Main.screenPosition = new Vector2(1600, 800);
                Main.UIScale = scales[i];
                Main.GameViewMatrix.ZoomMatrix = Matrix.CreateTranslation(-960, -540, 0)
                    * Matrix.CreateScale(zooms[i], zooms[i], 1) * Matrix.CreateTranslation(960, 540, 0);
                // 更新阶段已经由游戏换算到世界坐标；UI 阶段切换为 UI 坐标。
                Main.mouseX = 728; Main.mouseY = 424;
                UpdateQuickPlace();
                if (drag == 2) {
                    Main.mouseLeft = false;
                    Main.mouseX = 760; Main.mouseY = 456;
                    UpdateQuickPlace();
                }
                Main.mouseX = (int)((960 + (728 - 960) * zooms[i]) / scales[i]);
                Main.mouseY = (int)((540 + (424 - 540) * zooms[i]) / scales[i]);
                Rectangle actual;
                if (drag == 0) {
                    DrawIdlePreview(); actual = UIRenderer.LastOutline;
                } else if (drag == 1) {
                    actual = GetTileWorldToScreenRect(_dragStartX, _dragStartY, _dragCurX, _dragCurY);
                } else {
                    actual = GetTileWorldToScreenRect(_placedTiles.X, _placedTiles.Y,
                        _placedTiles.Right - 1, _placedTiles.Bottom - 1);
                }
                bool passed = actual == expected[i];
                if (!passed) failures++;
                result += (passed ? "PASS" : "FAIL") + " " + (drag == 0 ? "悬停" : drag == 1 ? "拖拽" : "松开铺设")
                    + " zoom=" + zooms[i] + " ui=" + scales[i]
                    + " expected=" + expected[i] + " actual=" + actual + Environment.NewLine;
            }
        }
        Main.GameViewMatrix.ZoomMatrix = Matrix.Identity;
        Main.UIScale = 1f;
        Main.mouseX = 744; Main.mouseY = 440;
        UpdateQuickPlace();
        DrawIdlePreview();
        bool moved = UIRenderer.LastOutline == new Rectangle(736, 432, 16, 16);
        if (!moved) failures++;
        result += (moved ? "PASS" : "FAIL") + " 连续移动刷新悬停瓦片" + Environment.NewLine;
        string[] blocked = { "快速放置 HUD", "其他面板", "聊天输入", "角色死亡", "首次尚无坐标" };
        for (int i = 0; i < blocked.Length; i++) {
            _isDragging = false;
            Main.mouseLeft = false;
            Main.mouseX = 728; Main.mouseY = 424;
            UpdateQuickPlace();
            if (i == 0) { Main.mouseX = 100; Main.mouseY = 90; }
            if (i == 1) UIRenderer.HoverPanel = true;
            if (i == 2) Main.drawingPlayerChat = true;
            if (i == 3) Main.LocalPlayer.dead = true;
            if (i == 4) _hasHoverTile = false;
            else UpdateQuickPlace();
            UIRenderer.LastOutline = Rectangle.Empty;
            DrawIdlePreview();
            bool passed = UIRenderer.LastOutline == Rectangle.Empty;
            if (!passed) failures++;
            result += (passed ? "PASS" : "FAIL") + " 隐藏预览：" + blocked[i] + Environment.NewLine;
            UIRenderer.HoverPanel = false;
            Main.drawingPlayerChat = false;
            Main.LocalPlayer.dead = false;
        }
        if (failures > 0) throw new Exception(result);
        return result;
    }
}
'@
$harness = $harness.Replace('__UPDATE__', $update).Replace('__UIHIT__', $uiHit).Replace('__PROJECTION__', $projection).Replace('__PREVIEW__', $preview)
$xna = 'C:\Windows\Microsoft.NET\assembly\GAC_32\Microsoft.Xna.Framework\v4.0_4.0.0.0__842cf8be1de50553\Microsoft.Xna.Framework.dll'
Add-Type -TypeDefinition $harness -ReferencedAssemblies $xna
[QuickPlacePreviewRegression]::Run()
