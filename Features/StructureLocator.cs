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
        private static Action<Point?> _complete;
        private static bool _remotePending;
        private static readonly Dictionary<StructureType, Point> _lastFound = new Dictionary<StructureType, Point>();
        private static readonly Dictionary<StructureType, Point> _markers = new Dictionary<StructureType, Point>();
        private static readonly List<KeyValuePair<StructureType, Point>> _markerSnapshot = new List<KeyValuePair<StructureType, Point>>();

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
            if (Main.netMode == 1) return Networking.FeatureNetwork.RequestLocator(type);
            if (Main.netMode == 2) return false;
            StartScan(type, player.Center, null);
            return true;
        }

        internal static void StartScan(StructureType type, Vector2 origin, Action<Point?> complete)
        {
            ResetScanState();
            _complete = complete;
            _type = type;
            _origin = origin;
            _width = Main.maxTilesX;
            _height = Main.maxTilesY;
            Point? direct = _lastFound.ContainsKey(type) ? null : FindKnownPosition(type);
            if (direct.HasValue) { Finish(direct); return; }
            IsScanning = true;
            if (Main.netMode != 2) Main.NewText($"【结构定位】正在扫描 {GetStructureName(type)}…", 100, 240, 255);
        }

        public static void Update()
        {
            if (!IsScanning || _remotePending) return;
            if (!Mod.WorldActive || (Main.gameMenu && !Main.dedServ) || Main.tile == null || Main.maxTilesX != _width || Main.maxTilesY != _height) { ResetSession(); return; }
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
                    if (_lastFound.TryGetValue(_type, out Point previous) && (long)x * _height + y <= (long)previous.X * _height + previous.Y) continue;
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
            ResetScanState();
            _lastFound.Clear();
            _markers.Clear();
            _markerSnapshot.Clear();
        }

        private static void ResetScanState()
        {
            IsScanning = false;
            ScanProgress = 0f;
            _nextTile = 0;
            _closest = null;
            _closestDistance = float.MaxValue;
            _complete = null;
            _remotePending = false;
        }

        internal static void AwaitServer()
        {
            ResetScanState();
            _remotePending = IsScanning = true;
        }

        internal static void ReceiveServerResult(StructureType type, Point? target)
        {
            ResetScanState();
            _type = type;
            _origin = Main.LocalPlayer.Center;
            _width = Main.maxTilesX;
            _height = Main.maxTilesY;
            Finish(target);
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
            if (_complete != null)
            {
                Action<Point?> callback = _complete;
                _complete = null;
                callback(target);
                return;
            }
            if (Main.netMode == 2) return;
            if (!target.HasValue)
            {
                string scope = "当前世界";
                Main.NewText(_lastFound.ContainsKey(_type) ? $"【结构定位】{GetStructureName(_type)}已没有更多结果。" : $"【结构定位】未能在{scope}中找到 {GetStructureName(_type)}！", 255, 120, 120);
                return;
            }
            Point pos = target.Value;
            _lastFound[_type] = pos;
            _markers[_type] = pos;
            RefreshMarkerSnapshot();
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

        // 地图标记由 Features.StructureMapMarker 用原版地图层绘制；这里只维护当前会话的标记数据。
        internal static IReadOnlyList<KeyValuePair<StructureType, Point>> Markers => _markerSnapshot;

        // 地图上右键取消某个标记；只移除显示，不改变"继续找下一个"的搜索位置。
        internal static void RemoveMarker(StructureType type)
        {
            if (!_markers.Remove(type)) return;
            RefreshMarkerSnapshot();
        }

        private static void RefreshMarkerSnapshot()
        {
            _markerSnapshot.Clear();
            foreach (KeyValuePair<StructureType, Point> marker in _markers) _markerSnapshot.Add(marker);
        }
    }
}
