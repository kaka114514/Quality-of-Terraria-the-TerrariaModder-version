using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.Tile_Entities;
using TerrariaModder.Core.Net;

namespace QualityOfTerraria.Networking
{
    internal static class PylonSync
    {
        private const string Operation = "quality-of-terraria.pylons.v1";
        private const ulong SnapshotRequestInterval = 30;
        private const ulong SnapshotRefreshInterval = 120;
        private static readonly List<TeleportPylonInfo> ClientPylons = new List<TeleportPylonInfo>();
        private static bool _initialized, _ready;
        private static int _sessionNetMode = -1;
        private static ulong _lastRequest;
        private static bool _hasRequestedSnapshot;
        private static bool _loggedWorldNotReady;
        private static ulong _serverSessionStartTick;
        private static ulong _clientMapScanTick = ulong.MaxValue;
        private static readonly List<TeleportPylonInfo> ClientMapPylons = new List<TeleportPylonInfo>();
        private static Action<int, string, string> _requestHandler;
        private static Action<bool, string, string> _responseHandler;

        internal static bool Ready => Main.netMode != 1 || _ready;
        internal static IReadOnlyList<TeleportPylonInfo> Pylons => ClientPylons;

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _sessionNetMode = Main.netMode;
            _serverSessionStartTick = Main.GameUpdateCount;
            _ready = Main.netMode != 1;
            _hasRequestedSnapshot = false;
            _requestHandler = (client, operation, payload) =>
            {
                if (operation != Operation || Main.netMode != 2) return;
                NetworkRuntime.Enqueue(() => SendServerList(client, payload));
            };
            _responseHandler = (success, operation, payload) =>
            {
                if (operation != Operation || Main.netMode != 1) return;
                NetworkRuntime.Enqueue(() => ReceiveClientList(success, payload));
            };
            NetSync.OnStorageRequest += _requestHandler;
            NetSync.OnStorageResponse += _responseHandler;
        }

        internal static void BeginWorldSession()
        {
            if (!_initialized) Initialize();
            _sessionNetMode = Main.netMode;
            _serverSessionStartTick = Main.GameUpdateCount;
            _ready = Main.netMode != 1;
            _lastRequest = 0;
            _hasRequestedSnapshot = false;
            _loggedWorldNotReady = false;
            _clientMapScanTick = ulong.MaxValue;
            ClientPylons.Clear();
            ClientMapPylons.Clear();
            Main.refreshMap = true;
        }

        internal static void ResetSession()
        {
            if (_initialized)
            {
                NetSync.OnStorageRequest -= _requestHandler;
                NetSync.OnStorageResponse -= _responseHandler;
            }
            _initialized = false;
            _sessionNetMode = -1;
            _ready = false;
            _lastRequest = 0;
            _hasRequestedSnapshot = false;
            _serverSessionStartTick = 0;
            _loggedWorldNotReady = false;
            _clientMapScanTick = ulong.MaxValue;
            ClientPylons.Clear();
            ClientMapPylons.Clear();
        }

        internal static void Update()
        {
            if (!_initialized || !Mod.WorldActive) return;
            RefreshSessionMode();
            if (Main.netMode != 1 || !ShouldRequestSnapshot(Main.GameUpdateCount, _lastRequest, _hasRequestedSnapshot, _ready)) return;
            _lastRequest = Main.GameUpdateCount;
            _hasRequestedSnapshot = true;
            NetSyncPayload.SendRequest(Operation, "request");
        }

        internal static bool ShouldRequestSnapshot(ulong currentTick, ulong lastRequest, bool hasRequested, bool ready)
            => ready
                ? hasRequested && currentTick - lastRequest >= SnapshotRefreshInterval
                : !hasRequested || currentTick - lastRequest >= SnapshotRequestInterval;

        internal static void RefreshSessionMode()
        {
            if (_sessionNetMode == Main.netMode) return;
            int previousMode = _sessionNetMode;
            _sessionNetMode = Main.netMode;
            _serverSessionStartTick = Main.GameUpdateCount;
            _ready = Main.netMode != 1;
            _lastRequest = 0;
            _hasRequestedSnapshot = false;
            _loggedWorldNotReady = false;
            _clientMapScanTick = ulong.MaxValue;
            ClientPylons.Clear();
            ClientMapPylons.Clear();
            Main.refreshMap = true;
            Mod.LogInfo($"晶塔同步网络模式从 {previousMode} 切换到 {Main.netMode}，重新获取服务器列表。");
        }
        internal static bool ReplaceClientPylons(List<TeleportPylonInfo> destination, IEnumerable<TeleportPylonInfo> pylons)
        {
            if (ArePylonListsEqual(destination, pylons)) return false;
            destination.Clear();
            foreach (TeleportPylonInfo pylon in pylons) destination.Add(pylon);
            return true;
        }

        internal static bool ArePylonListsEqual(IList<TeleportPylonInfo> left, IEnumerable<TeleportPylonInfo> right)
        {
            if (left == null || right == null) return left == null && right == null;
            int index = 0;
            foreach (TeleportPylonInfo pylon in right)
            {
                if (index >= left.Count || !left[index].Equals(pylon)) return false;
                index++;
            }
            return index == left.Count;
        }

        internal static List<TeleportPylonInfo> BeginMapDraw()
        {
            if (Main.netMode != 1 || !_ready || Main.PylonSystem == null) return null;
            List<TeleportPylonInfo> pylonList = Main.PylonSystem.Pylons;
            var previous = new List<TeleportPylonInfo>(pylonList);
            ReplaceClientPylons(pylonList, ClientPylons);
            return previous;
        }

        internal static void EndMapDraw(List<TeleportPylonInfo> previous)
        {
            if (previous == null || Main.PylonSystem == null) return;
            ReplaceClientPylons(Main.PylonSystem.Pylons, previous);
        }

        internal static List<TeleportPylonInfo> GetClientMapPylons(List<TeleportPylonInfo> vanillaPylons)
        {
            ClientMapPylons.Clear();
            if (Ready)
            {
                MergeClientPylons(ClientMapPylons, ClientPylons);
                return ClientMapPylons;
            }

            MergeClientPylons(ClientMapPylons, vanillaPylons);
            MergeClientPylons(ClientMapPylons, ClientPylons);
            if (Main.tile != null && (_clientMapScanTick == ulong.MaxValue || Main.GameUpdateCount - _clientMapScanTick >= 600))
            {
                int activePylonTiles, parsedPylonFrames;
                List<TeleportPylonInfo> scanned = CollectWorldPylons(out activePylonTiles, out parsedPylonFrames);
                MergeClientPylons(ClientMapPylons, scanned);
                _clientMapScanTick = Main.GameUpdateCount;
                if (Main.netMode == 1 && (activePylonTiles > 0 || scanned.Count > 0))
                    Mod.LogInfo($"客户端晶塔地图数据：原版={(vanillaPylons == null ? 0 : vanillaPylons.Count)}，同步={ClientPylons.Count}，图块={activePylonTiles}，恢复={scanned.Count}。");
            }
            return ClientMapPylons;
        }
        internal static void MergeClientPylons(List<TeleportPylonInfo> destination, IEnumerable<TeleportPylonInfo> synchronizedPylons)
        {
            foreach (TeleportPylonInfo pylon in synchronizedPylons)
                if (!destination.Contains(pylon)) destination.Add(pylon);
        }

        internal static void ApplyClientPylonAdded(TeleportPylonInfo pylon)
        {
            if (Main.netMode != 1) return;
            int existingIndex = ClientPylons.FindIndex(existing => existing.PositionInTiles.Equals(pylon.PositionInTiles));
            if (existingIndex >= 0)
            {
                if (ClientPylons[existingIndex].Equals(pylon)) return;
                ClientPylons[existingIndex] = pylon;
            }
            else ClientPylons.Add(pylon);
            Main.refreshMap = true;
        }

        internal static void ApplyClientPylonRemoved(TeleportPylonInfo pylon)
        {
            if (Main.netMode != 1) return;
            ClientPylons.RemoveAll(existing => existing.PositionInTiles.Equals(pylon.PositionInTiles));
            Main.refreshMap = true;
        }

        internal static bool ShouldDelayServerSnapshot()
        {
            return Main.netMode == 2 && Main.GameUpdateCount - _serverSessionStartTick < 600UL;
        }

        internal static void RefreshServerPylonsImmediately()
        {
            if (Main.netMode != 2 || Main.PylonSystem == null) return;
            Main.PylonSystem.RequestImmediateUpdate();
            string payload = EncodePylons(RefreshServerPylons());
            for (int client = 0; client < Main.maxPlayers; client++)
                if (Main.player[client].active) NetSyncPayload.SendResponseTo(client, true, Operation, payload);
        }

        private static void SendServerList(int client, string payload)
        {
            if (payload != "request" || client < 0 || client >= Main.maxPlayers || !Main.player[client].active) return;
            if (ShouldDelayServerSnapshot()) return;
            if (!IsServerWorldReady())
            {
                if (!_loggedWorldNotReady) Mod.LogInfo("晶塔同步等待服务器世界数据加载，客户端将自动重试。");
                _loggedWorldNotReady = true;
                return;
            }
            _loggedWorldNotReady = false;
            List<TeleportPylonInfo> pylons = RefreshServerPylons();
            NetSyncPayload.SendResponseTo(client, true, Operation, EncodePylons(pylons));
        }

        internal static bool IsServerWorldReady()
            => Main.netMode != 1 && Main.PylonSystem != null && Main.tile != null && Main.maxTilesX > 0 && Main.maxTilesY > 0;

        internal static List<TeleportPylonInfo> RefreshServerPylons()
        {
            var pylons = new List<TeleportPylonInfo>();
            if (Main.netMode == 1) return pylons;
            if (Main.PylonSystem == null)
            {
                Mod.LogInfo("晶塔快照跳过：原版晶塔系统尚未初始化。");
                return pylons;
            }
            pylons = CollectAuthoritativePylons();
            if (pylons.Count > 0) return pylons;
            if (Main.tile == null)
            {
                Mod.LogInfo("晶塔快照为空：原版列表为空且世界图块尚未加载。");
                return pylons;
            }

            int activePylonTiles, parsedPylonFrames;
            List<TeleportPylonInfo> recovered = CollectWorldPylons(out activePylonTiles, out parsedPylonFrames);
            foreach (TeleportPylonInfo pylon in recovered) Main.PylonSystem.AddForClient(pylon);
            string worldName = Main.ActiveWorldFileData == null ? "unknown" : Main.ActiveWorldFileData.Name;
            Mod.LogInfo($"晶塔回退扫描：世界={worldName}，地图={Main.maxTilesX}x{Main.maxTilesY}，活动晶塔图块={activePylonTiles}，有效帧={parsedPylonFrames}，恢复={recovered.Count}。");
            return recovered;
        }

        internal static bool TryGetPylonInfo(int x, int y, bool active, int tileType, int frameX, int frameY, out TeleportPylonInfo pylon)
        {
            pylon = default(TeleportPylonInfo);
            if (!active || tileType != 597 || frameX < 0 || frameY < 0) return false;
            int type = frameX / 54;
            if (type > 32) return false;
            int topLeftX = x - frameX / 18 % 3;
            int topLeftY = y - frameY / 18 % 4;
            if (topLeftX < 0 || topLeftY < 0) return false;
            pylon = new TeleportPylonInfo { PositionInTiles = new Point16(topLeftX, topLeftY), TypeOfPylon = (TeleportPylonType)type };
            return true;
        }

        internal static bool TryFindPylonAround(int x, int y, out TeleportPylonInfo pylon)
        {
            pylon = default(TeleportPylonInfo);
            if (Main.tile == null) return false;
            int minX = Math.Max(0, x - 2), maxX = Math.Min(Main.maxTilesX - 1, x + 2);
            int minY = Math.Max(0, y - 3), maxY = Math.Min(Main.maxTilesY - 1, y + 3);
            for (int tileX = minX; tileX <= maxX; tileX++)
            for (int tileY = minY; tileY <= maxY; tileY++)
            {
                Tile tile = Main.tile[tileX, tileY];
                if (tile == null || !tile.active() || tile.type != 597) continue;
                if (TryGetPylonInfo(tileX, tileY, true, tile.type, tile.frameX, tile.frameY, out pylon)) return true;
            }
            return false;
        }

        internal static bool TryGetPlacedPylonInfo(int x, int y, int style, out TeleportPylonInfo pylon)
        {
            pylon = default(TeleportPylonInfo);
            if (x < 1 || y < 3 || style < 0 || style > 32) return false;
            pylon = new TeleportPylonInfo
            {
                PositionInTiles = new Point16(x - 1, y - 3),
                TypeOfPylon = (TeleportPylonType)style
            };
            return true;
        }

        private static List<TeleportPylonInfo> CollectWorldPylons(out int activePylonTiles, out int parsedPylonFrames)
        {
            var pylons = new List<TeleportPylonInfo>();
            activePylonTiles = 0;
            parsedPylonFrames = 0;
            for (int x = 0; x < Main.maxTilesX; x++)
            for (int y = 0; y < Main.maxTilesY; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile == null || !tile.active() || tile.type != 597) continue;
                activePylonTiles++;
                TeleportPylonInfo pylon;
                if (!TryGetPylonInfo(x, y, true, tile.type, tile.frameX, tile.frameY, out pylon)) continue;
                parsedPylonFrames++;
                if (!pylons.Contains(pylon)) pylons.Add(pylon);
            }
            return pylons;
        }

        internal static List<TeleportPylonInfo> CollectAuthoritativePylons()
        {
            var pylons = new List<TeleportPylonInfo>();
            foreach (TileEntity entity in TileEntity.ByPosition.Values)
            {
                var pylon = entity as TETeleportationPylon;
                TeleportPylonType type;
                if (pylon == null || !pylon.TryGetPylonType(out type)) continue;
                pylons.Add(new TeleportPylonInfo { PositionInTiles = pylon.Position, TypeOfPylon = type });
            }
            return pylons;
        }

        private static string EncodePylons(List<TeleportPylonInfo> pylons)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(pylons.Count);
                foreach (var pylon in pylons)
                {
                    writer.Write((short)pylon.PositionInTiles.X);
                    writer.Write((short)pylon.PositionInTiles.Y);
                    writer.Write((int)pylon.TypeOfPylon);
                }
                Mod.LogInfo($"服务器晶塔快照：{pylons.Count} 个。");
                return Convert.ToBase64String(stream.ToArray());
            }
        }
        private static void ReceiveClientList(bool success, string payload)
        {
            if (!success || payload == null) return;
            try
            {
                byte[] bytes = Convert.FromBase64String(payload);
                using (var reader = new BinaryReader(new MemoryStream(bytes)))
                {
                    int count = reader.ReadInt32();
                    if (count < 0 || count > Main.maxChests) throw new InvalidDataException("服务器晶塔列表数量无效");
                    var received = new List<TeleportPylonInfo>(count);
                    for (int i = 0; i < count; i++)
                    {
                        short x = reader.ReadInt16(), y = reader.ReadInt16();
                        int type = reader.ReadInt32();
                        if (x < 0 || y < 0 || type < 0 || type > 32) throw new InvalidDataException("服务器晶塔列表内容无效");
                        received.Add(new TeleportPylonInfo { PositionInTiles = new Point16(x, y), TypeOfPylon = (TeleportPylonType)type });
                    }
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("服务器晶塔列表含多余数据");
                    bool changed = ReplaceClientPylons(ClientPylons, received);
                    bool wasReady = _ready;
                    _ready = true;
                    if (changed || !wasReady) Main.refreshMap = true;
                    if (changed || !wasReady)
                        Mod.LogInfo($"已同步服务器晶塔地图列表：{ClientPylons.Count} 个。");
                }
            }
            catch (Exception error) { Mod.LogError("读取服务器晶塔列表失败", error); }
        }
    }
}
