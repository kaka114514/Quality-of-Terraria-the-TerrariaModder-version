using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace QualityOfTerraria.Features
{
    public static class StructureLocator
    {
        public enum StructureType { Aether, Dungeon, Temple, FloatingIsland, Pyramid, PlanteraBulb, EnchantedSword, BeeHive, MarbleCave, GraniteCave }
        public static bool IsScanning { get; private set; }
        public static float ScanProgress { get; private set; }
        private static StructureType _type;
        private static int _nextTile, _width, _height;
        private static Point? _closest;
        private static float _closestDistance;
        private static Vector2 _origin;
        private const int TilesPerFrame = 8192;

        public static string GetStructureName(StructureType type)
        {
            switch (type)
            {
                case StructureType.Aether: return "以太洞 (微光湖)";
                case StructureType.Dungeon: return "地牢入口";
                case StructureType.Temple: return "丛林神庙 (祭坛)";
                case StructureType.FloatingIsland: return "空岛 (天域箱)";
                case StructureType.Pyramid: return "沙漠金字塔";
                case StructureType.PlanteraBulb: return "世纪之花花苞";
                case StructureType.EnchantedSword: return "附魔剑冢";
                case StructureType.BeeHive: return "蜂巢 (幼虫)";
                case StructureType.MarbleCave: return "大理石洞";
                case StructureType.GraniteCave: return "花岗岩洞";
                default: return "未知结构";
            }
        }

        // 返回值表示请求已接受；扫描结果由 Update 完成后提示。
        public static bool LocateAndReveal(StructureType type)
        {
            Player player = Main.LocalPlayer;
            if (Main.gameMenu || player == null || !player.active || Main.tile == null) return false;
            ResetSession();
            _type = type;
            _origin = player.Center;
            _width = Main.maxTilesX;
            _height = Main.maxTilesY;
            Point? direct = FindKnownPosition(type);
            if (direct.HasValue) { Finish(direct); return true; }
            IsScanning = true;
            Main.NewText($"【结构定位】正在扫描 {GetStructureName(type)}…", 100, 240, 255);
            return true;
        }

        public static void Update()
        {
            if (!IsScanning) return;
            if (Main.gameMenu || Main.tile == null || Main.maxTilesX != _width || Main.maxTilesY != _height) { ResetSession(); return; }
            int total = _width * _height;
            Stopwatch clock = Stopwatch.StartNew();
            for (int count = 0; count < TilesPerFrame && _nextTile < total; count++)
            {
                int x = _nextTile / _height;
                int y = _nextTile % _height;
                _nextTile++;
                if (Matches(_type, Main.tile[x, y]))
                {
                    Point pos = new Point(x, y);
                    if (_type != StructureType.PlanteraBulb && _type != StructureType.BeeHive) { Finish(pos); return; }
                    float distance = Vector2.DistanceSquared(_origin, new Vector2(x * 16, y * 16));
                    if (distance < _closestDistance) { _closestDistance = distance; _closest = pos; }
                }
                if ((count & 255) == 255 && clock.ElapsedMilliseconds >= 2) break;
            }
            ScanProgress = total == 0 ? 1f : _nextTile / (float)total;
            if (_nextTile >= total) Finish(_closest);
        }

        public static void ResetSession()
        {
            IsScanning = false;
            ScanProgress = 0f;
            _nextTile = 0;
            _closest = null;
            _closestDistance = float.MaxValue;
        }

        private static Point? FindKnownPosition(StructureType type)
        {
            if (type == StructureType.Dungeon && WorldGen.InWorld(Main.dungeonX, Main.dungeonY)) return new Point(Main.dungeonX, Main.dungeonY);
            // GenVars 可能属于上次生成的世界，必须先验证实际瓦片。
            if (type == StructureType.Aether)
            {
                int x = (int)GenVars.shimmerPosition.X, y = (int)GenVars.shimmerPosition.Y;
                if (WorldGen.InWorld(x, y) && Matches(type, Main.tile[x, y])) return new Point(x, y);
            }
            if (type == StructureType.Temple && WorldGen.InWorld(GenVars.lAltarX, GenVars.lAltarY))
            {
                Tile tile = Main.tile[GenVars.lAltarX, GenVars.lAltarY];
                if (tile != null && tile.active() && tile.type == TileID.LihzahrdAltar) return new Point(GenVars.lAltarX, GenVars.lAltarY);
            }
            if (type == StructureType.FloatingIsland || type == StructureType.Pyramid)
                for (int c = 0; c < Main.maxChests; c++)
                {
                    Chest chest = Main.chest[c];
                    if (chest != null && WorldGen.InWorld(chest.x, chest.y) && Matches(type, Main.tile[chest.x, chest.y])) return new Point(chest.x, chest.y);
                }
            return null;
        }

        private static bool Matches(StructureType type, Tile tile)
        {
            if (tile == null) return false;
            if (type == StructureType.Aether) return tile.liquid > 0 && tile.liquidType() == 3;
            if (!tile.active()) return false;
            switch (type)
            {
                case StructureType.Temple: return tile.type == TileID.LihzahrdAltar || tile.type == TileID.LihzahrdBrick;
                case StructureType.PlanteraBulb: return tile.type == TileID.PlanteraBulb;
                case StructureType.BeeHive: return tile.type == TileID.Larva;
                case StructureType.EnchantedSword: return tile.type == TileID.LargePiles2 && tile.frameX == 918;
                case StructureType.FloatingIsland: return tile.type == TileID.Containers && tile.frameX / 36 == 13;
                case StructureType.Pyramid: return tile.type == TileID.Containers && tile.frameX / 36 == 12;
                case StructureType.MarbleCave: return tile.type == TileID.Marble;
                case StructureType.GraniteCave: return tile.type == TileID.Granite;
                default: return false;
            }
        }

        private static void Finish(Point? target)
        {
            IsScanning = false;
            ScanProgress = 1f;
            if (!target.HasValue)
            {
                string scope = Main.netMode == 1 ? "已同步的世界区域" : "当前世界";
                Main.NewText($"【结构定位】未能在{scope}中找到 {GetStructureName(_type)}！", 255, 120, 120);
                return;
            }
            Point pos = target.Value;
            if (Main.Map != null)
            {
                for (int x = Math.Max(0, pos.X - 25); x <= Math.Min(_width - 1, pos.X + 25); x++)
                    for (int y = Math.Max(0, pos.Y - 25); y <= Math.Min(_height - 1, pos.Y + 25); y++) Main.Map.Update(x, y, 255);
                Main.refreshMap = true;
            }
            int distance = (int)(Vector2.Distance(_origin, new Vector2(pos.X * 16, pos.Y * 16)) / 16f);
            Main.NewText($"【结构定位】已找到 {GetStructureName(_type)}！坐标 [X={pos.X}, Y={pos.Y}]，距离约 {distance} 格，已点亮小地图。", 100, 240, 255);
            SoundEngine.PlaySound(SoundID.Item29);
        }
    }
}
