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
        private const ulong PylonPlacementGrace = 60;
        private const ulong PylonRemovalConfirmationDelay = 30;
        private const ulong ClientPredictionValidationDelay = 120;
        private const ulong ClientRemovalGrace = 120;
        private const ulong ClientReconcileTimeout = 300;
        private static readonly List<TeleportPylonInfo> ClientPylons = new List<TeleportPylonInfo>();
        private static readonly List<TeleportPylonInfo> ServerPylons = new List<TeleportPylonInfo>();
        private static readonly Dictionary<Point16, ulong> ServerPylonAddedAt = new Dictionary<Point16, ulong>();
        private static readonly Dictionary<Point16, ulong> PendingServerPylonRemovals = new Dictionary<Point16, ulong>();
        private static readonly Dictionary<Point16, ulong> ClientPredictedAt = new Dictionary<Point16, ulong>();
        private static readonly Dictionary<Point16, TeleportPylonInfo> ClientPredictedPylons = new Dictionary<Point16, TeleportPylonInfo>();
        private static readonly Dictionary<Point16, ulong> ClientRemovedAt = new Dictionary<Point16, ulong>();
        private static readonly Dictionary<Point16, PendingClientPylonChange> PendingClientPylonChanges =
            new Dictionary<Point16, PendingClientPylonChange>();
        private static bool _initialized, _ready;
        private static int _sessionNetMode = -1;
        private static ulong _lastRequest;
        private static bool _hasRequestedSnapshot;
        private static bool _serverTilesScanned;
        private static bool _loggedWorldNotReady;
        private static ulong _serverSessionStartTick;
        private static ulong _clientMapScanTick = ulong.MaxValue;
        private static readonly List<TeleportPylonInfo> ClientMapPylons = new List<TeleportPylonInfo>();
        private static Action<int, string, string> _requestHandler;
        private static Action<bool, string, string> _responseHandler;

        private sealed class PendingClientPylonChange
        {
            internal TeleportPylonInfo Pylon;
            internal bool Added;
            internal ulong Expires;
        }

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
                string complete;
                if (!NetSyncPayload.TryAcceptRequest(client, operation, payload, out complete)) return;
                NetworkRuntime.Enqueue(() => SendServerList(client, complete));
            };
            _responseHandler = (success, operation, payload) =>
            {
                if (operation != Operation || Main.netMode != 1) return;
                string complete;
                if (!NetSyncPayload.TryAcceptResponse(operation, payload, out complete)) return;
                NetworkRuntime.Enqueue(() => ReceiveClientList(success, complete));
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
            _serverTilesScanned = false;
            _loggedWorldNotReady = false;
            _clientMapScanTick = ulong.MaxValue;
            ClientPylons.Clear();
            ServerPylons.Clear();
            ServerPylonAddedAt.Clear();
            PendingServerPylonRemovals.Clear();
            ClientPredictedAt.Clear();
            ClientPredictedPylons.Clear();
            ClientRemovedAt.Clear();
            PendingClientPylonChanges.Clear();
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
            _serverTilesScanned = false;
            _serverSessionStartTick = 0;
            _loggedWorldNotReady = false;
            _clientMapScanTick = ulong.MaxValue;
            ClientPylons.Clear();
            ServerPylons.Clear();
            ServerPylonAddedAt.Clear();
            PendingServerPylonRemovals.Clear();
            ClientPredictedAt.Clear();
            ClientPredictedPylons.Clear();
            ClientRemovedAt.Clear();
            PendingClientPylonChanges.Clear();
            ClientMapPylons.Clear();
        }

        internal static void Update()
        {
            if (!_initialized || !Mod.WorldActive) return;
            RefreshSessionMode();
            if (Main.netMode == 2)
            {
                ConfirmPendingClientPylonChanges(Main.GameUpdateCount);
                ConfirmPendingServerPylonRemovals(Main.GameUpdateCount);
                return;
            }
            PruneClientPredictions(Main.GameUpdateCount);
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
            _serverTilesScanned = false;
            _loggedWorldNotReady = false;
            _clientMapScanTick = ulong.MaxValue;
            ClientPylons.Clear();
            ServerPylons.Clear();
            ServerPylonAddedAt.Clear();
            PendingServerPylonRemovals.Clear();
            ClientPredictedAt.Clear();
            ClientPredictedPylons.Clear();
            ClientRemovedAt.Clear();
            PendingClientPylonChanges.Clear();
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

        internal static bool ApplyServerSnapshot(List<TeleportPylonInfo> destination, IEnumerable<TeleportPylonInfo> snapshot)
        {
            if (destination == null || snapshot == null) return false;
            bool changed = false;
            foreach (TeleportPylonInfo pylon in snapshot)
            {
                int index = destination.FindIndex(existing => existing.PositionInTiles.Equals(pylon.PositionInTiles));
                if (index >= 0)
                {
                    if (destination[index].Equals(pylon)) continue;
                    destination[index] = pylon;
                }
                else destination.Add(pylon);
                changed = true;
            }
            return changed;
        }

        internal static void ApplyServerPylonAdded(TETeleportationPylon pylon)
        {
            if (Main.netMode != 2 || pylon == null) return;
            TeleportPylonType type;
            if (!pylon.TryGetPylonType(out type)) return;
            ApplyServerSnapshot(ServerPylons, new[]
            {
                new TeleportPylonInfo { PositionInTiles = pylon.Position, TypeOfPylon = type }
            });
            ServerPylonAddedAt[pylon.Position] = Main.GameUpdateCount;
            PendingServerPylonRemovals.Remove(pylon.Position);
        }

        internal static void ApplyServerPylonRemoved(TETeleportationPylon pylon)
        {
            if (Main.netMode != 2 || pylon == null) return;
            PendingServerPylonRemovals[pylon.Position] = Main.GameUpdateCount + PylonRemovalConfirmationDelay;
        }

        internal static void EnsureServerPylonsInVanillaList()
        {
            if (Main.netMode != 2 || Main.PylonSystem == null || ServerPylons.Count == 0) return;
            MergeClientPylons(Main.PylonSystem.Pylons, ServerPylons);
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
                MergeClientPylons(ClientMapPylons, ClientPredictedPylons.Values);
                FilterClientRemovals(ClientMapPylons, Main.GameUpdateCount);
                return ClientMapPylons;
            }

            MergeClientPylons(ClientMapPylons, vanillaPylons);
            MergeClientPylons(ClientMapPylons, ClientPylons);
            MergeClientPylons(ClientMapPylons, ClientPredictedPylons.Values);
            if (Main.tile != null && (_clientMapScanTick == ulong.MaxValue || Main.GameUpdateCount - _clientMapScanTick >= 600))
            {
                int activePylonTiles, parsedPylonFrames;
                List<TeleportPylonInfo> scanned = CollectWorldPylons(out activePylonTiles, out parsedPylonFrames);
                MergeClientPylons(ClientMapPylons, scanned);
                _clientMapScanTick = Main.GameUpdateCount;
                if (Main.netMode == 1 && (activePylonTiles > 0 || scanned.Count > 0))
                    Mod.LogInfo($"客户端晶塔地图数据：原版={(vanillaPylons == null ? 0 : vanillaPylons.Count)}，同步={ClientPylons.Count}，图块={activePylonTiles}，恢复={scanned.Count}。");
            }
            FilterClientRemovals(ClientMapPylons, Main.GameUpdateCount);
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
            // 原版 AddForClient 与服务器列表可能不同步；地图状态只接受本地操作预测和服务器快照。
            Main.refreshMap = true;
        }

        internal static void ApplyClientPylonAddedPrediction(TeleportPylonInfo pylon)
        {
            if (Main.netMode != 1) return;
            Point16 position = pylon.PositionInTiles;
            bool wasRemoved = ClientRemovedAt.Remove(position);
            TeleportPylonInfo predicted;
            if (!wasRemoved && ClientPredictedPylons.TryGetValue(position, out predicted) && predicted.Equals(pylon)) return;

            int existingIndex = ClientPylons.FindIndex(existing => existing.PositionInTiles.Equals(position));
            if (existingIndex >= 0)
            {
                ClientPylons[existingIndex] = pylon;
            }
            else ClientPylons.Add(pylon);
            ClientPredictedPylons[position] = pylon;
            ClientPredictedAt[position] = Main.GameUpdateCount;
            QueueClientPylonChange(pylon, true);
            Main.refreshMap = true;
        }

        internal static void ApplyClientPylonRemoved(TeleportPylonInfo pylon)
        {
            if (Main.netMode != 1) return;
            // 服务器快照与本地预测负责结束状态，原版通知只作为刷新提示，避免来回闪烁。
            Main.refreshMap = true;
        }

        internal static void ApplyClientPylonRemovedPrediction(TeleportPylonInfo pylon)
        {
            if (Main.netMode != 1) return;
            Point16 position = pylon.PositionInTiles;
            if (ClientRemovedAt.ContainsKey(position)) return;
            ClientPylons.RemoveAll(existing => existing.PositionInTiles.Equals(position));
            ClientPredictedPylons.Remove(position);
            ClientPredictedAt.Remove(position);
            ClientRemovedAt[position] = Main.GameUpdateCount;
            QueueClientPylonChange(pylon, false);
            Main.refreshMap = true;
        }

        private static void QueueClientPylonChange(TeleportPylonInfo pylon, bool added)
        {
            string payload = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}",
                added ? "add" : "remove", pylon.PositionInTiles.X, pylon.PositionInTiles.Y, (int)pylon.TypeOfPylon);
            Mod.LogInfo($"晶塔本地变更：{payload}。");
            NetSyncPayload.SendRequest(Operation, payload);
        }

        private static void PruneClientPredictions(ulong currentTick)
        {
            if (Main.netMode != 1 || ClientPredictedPylons.Count == 0) return;
            bool changed = false;
            foreach (var pair in new List<KeyValuePair<Point16, TeleportPylonInfo>>(ClientPredictedPylons))
            {
                ulong predictedAt;
                if (!ClientPredictedAt.TryGetValue(pair.Key, out predictedAt) ||
                    currentTick - predictedAt < ClientPredictionValidationDelay || HasCompletePylon(pair.Value)) continue;
                ClientPredictedPylons.Remove(pair.Key);
                ClientPredictedAt.Remove(pair.Key);
                ClientPylons.RemoveAll(pylon => pylon.PositionInTiles.Equals(pair.Key));
                changed = true;
            }
            if (changed) Main.refreshMap = true;
        }

        private static bool FilterClientRemovals(List<TeleportPylonInfo> pylons, ulong currentTick)
        {
            if (ClientRemovedAt.Count == 0) return false;
            bool changed = false;
            foreach (var pair in new List<KeyValuePair<Point16, ulong>>(ClientRemovedAt))
            {
                bool elapsed = currentTick - pair.Value >= ClientRemovalGrace;
                bool footprintExists = HasPylonFootprint(pair.Key);
                bool serverStillHas = ClientPylons.FindIndex(pylon => pylon.PositionInTiles.Equals(pair.Key)) >= 0;
                if (ClientPredictedPylons.ContainsKey(pair.Key) || (elapsed && (footprintExists || !serverStillHas)))
                {
                    ClientRemovedAt.Remove(pair.Key);
                    continue;
                }
                changed |= pylons.RemoveAll(pylon => pylon.PositionInTiles.Equals(pair.Key)) > 0;
            }
            return changed;
        }

        private static bool HasPylonFootprint(Point16 position)
        {
            int x = position.X, y = position.Y;
            if (Main.tile == null || x < 0 || y < 0 || x + 2 >= Main.maxTilesX || y + 3 >= Main.maxTilesY) return false;
            for (int dx = 0; dx < 3; dx++)
            for (int dy = 0; dy < 4; dy++)
            {
                Tile tile = Main.tile[x + dx, y + dy];
                TeleportPylonInfo actual;
                if (tile == null || !TryGetPylonInfo(x + dx, y + dy, tile.active(), tile.type, tile.frameX, tile.frameY,
                    out actual) || !actual.PositionInTiles.Equals(position)) return false;
            }
            return true;
        }

        private static bool MergeClientPredictions(ulong currentTick)
        {
            bool changed = false;
            foreach (var pair in new List<KeyValuePair<Point16, TeleportPylonInfo>>(ClientPredictedPylons))
            {
                if (ClientPylons.Contains(pair.Value))
                {
                    ClientPredictedPylons.Remove(pair.Key);
                    ClientPredictedAt.Remove(pair.Key);
                    continue;
                }
                ulong predictedAt;
                if (ClientPredictedAt.TryGetValue(pair.Key, out predictedAt) &&
                    currentTick - predictedAt >= ClientPredictionValidationDelay && !HasCompletePylon(pair.Value))
                    continue;
                if (!ClientRemovedAt.ContainsKey(pair.Key))
                {
                    ClientPylons.Add(pair.Value);
                    changed = true;
                }
            }
            changed |= FilterClientRemovals(ClientPylons, currentTick);
            return changed;
        }

        internal static bool ShouldDelayServerSnapshot()
        {
            return Main.netMode == 2 && Main.GameUpdateCount - _serverSessionStartTick < 600UL;
        }

        internal static void RefreshServerPylonsImmediately()
        {
            if (Main.netMode != 2 || Main.PylonSystem == null) return;
            // OnPlaced/OnRemoved 已经刷新原版列表；拆除期间不扫描尚未清除的图块。
            ApplyServerSnapshot(ServerPylons, CollectAuthoritativePylons());
            BroadcastServerPylons();
        }

        internal static bool ConfirmPendingServerPylonRemovals(ulong currentTick)
        {
            if (Main.netMode != 2 || PendingServerPylonRemovals.Count == 0) return false;
            bool changed = false;
            foreach (var pending in new List<KeyValuePair<Point16, ulong>>(PendingServerPylonRemovals))
            {
                if (currentTick < pending.Value) continue;
                int cachedIndex = ServerPylons.FindIndex(item => item.PositionInTiles.Equals(pending.Key));
                if (cachedIndex >= 0 && HasCompletePylon(ServerPylons[cachedIndex]))
                {
                    RestoreMissingServerPylonEntity(ServerPylons[cachedIndex]);
                    ServerPylonAddedAt[ServerPylons[cachedIndex].PositionInTiles] = currentTick;
                    PendingServerPylonRemovals.Remove(pending.Key);
                    continue;
                }

                if (ServerPylons.RemoveAll(item => item.PositionInTiles.Equals(pending.Key)) > 0) changed = true;
                ServerPylonAddedAt.Remove(pending.Key);
                PendingServerPylonRemovals.Remove(pending.Key);
            }
            if (changed) BroadcastServerPylons();
            return changed;
        }

        internal static void QueueServerPylonChange(int client, string payload)
        {
            if (Main.netMode != 2 || client < 0 || client >= Main.maxPlayers || !Main.player[client].active) return;
            string[] parts = payload == null ? null : payload.Split(':');
            if (parts == null || parts.Length != 4 || (parts[0] != "add" && parts[0] != "remove")) return;
            int x, y, type;
            if (!int.TryParse(parts[1], out x) || !int.TryParse(parts[2], out y) || !int.TryParse(parts[3], out type) ||
                x < 0 || y < 0 || x + 2 >= Main.maxTilesX || y + 3 >= Main.maxTilesY || type < 0 || type > 32) return;
            var position = new Point16(x, y);
            if (PendingClientPylonChanges.Count >= 256 && !PendingClientPylonChanges.ContainsKey(position)) return;
            PendingClientPylonChanges[position] = new PendingClientPylonChange
            {
                Pylon = new TeleportPylonInfo { PositionInTiles = position, TypeOfPylon = (TeleportPylonType)type },
                Added = parts[0] == "add",
                Expires = Main.GameUpdateCount + ClientReconcileTimeout
            };
        }

        internal static bool ConfirmPendingClientPylonChanges(ulong currentTick)
        {
            if (Main.netMode != 2 || PendingClientPylonChanges.Count == 0 || !IsServerWorldReady()) return false;
            bool changed = false;
            foreach (var pending in new List<KeyValuePair<Point16, PendingClientPylonChange>>(PendingClientPylonChanges))
            {
                PendingClientPylonChange change = pending.Value;
                if (change.Added && HasCompletePylon(change.Pylon))
                {
                    RestoreMissingServerPylonEntity(change.Pylon);
                    ApplyServerSnapshot(ServerPylons, new[] { change.Pylon });
                    ServerPylonAddedAt[pending.Key] = currentTick;
                    PendingServerPylonRemovals.Remove(pending.Key);
                    PendingClientPylonChanges.Remove(pending.Key);
                    Mod.LogInfo($"晶塔服务器核验：新增 ({pending.Key.X}, {pending.Key.Y})，当前 {ServerPylons.Count} 个。");
                    changed = true;
                }
                else if (!change.Added && !HasCompletePylon(change.Pylon))
                {
                    if (ServerPylons.RemoveAll(pylon => pylon.PositionInTiles.Equals(pending.Key)) > 0) changed = true;
                    ServerPylonAddedAt.Remove(pending.Key);
                    PendingServerPylonRemovals.Remove(pending.Key);
                    PendingClientPylonChanges.Remove(pending.Key);
                    Mod.LogInfo($"晶塔服务器核验：移除 ({pending.Key.X}, {pending.Key.Y})，当前 {ServerPylons.Count} 个。");
                }
                else if (currentTick >= change.Expires)
                {
                    PendingClientPylonChanges.Remove(pending.Key);
                }
            }
            if (changed) BroadcastServerPylons();
            return changed;
        }

        private static void RestoreMissingServerPylonEntity(TeleportPylonInfo pylon)
        {
            Point16 position = pylon.PositionInTiles;
            if (TileEntity.ByPosition.ContainsKey(position)) return;
            int id = TETeleportationPylon.Place(position.X, position.Y);
            if (id >= 0) NetMessage.SendData(86, -1, -1, null, id, position.X, position.Y);
        }

        private static void BroadcastServerPylons()
        {
            string payload = EncodePylons(ServerPylons);
            for (int client = 0; client < Main.maxPlayers; client++)
                if (Main.player[client].active) NetSyncPayload.SendResponseTo(client, true, Operation, payload);
        }

        private static void SendServerList(int client, string payload)
        {
            if (client < 0 || client >= Main.maxPlayers || !Main.player[client].active) return;
            if (payload != null && (payload.StartsWith("add:", StringComparison.Ordinal) ||
                payload.StartsWith("remove:", StringComparison.Ordinal)))
            {
                QueueServerPylonChange(client, payload);
                return;
            }
            if (payload != "request") return;
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
            if (_serverTilesScanned)
            {
                ulong now = Main.GameUpdateCount;
                ServerPylons.RemoveAll(pylon => !HasCompletePylon(pylon) &&
                    !WithinPlacementGrace(pylon.PositionInTiles, now) &&
                    !PendingServerPylonRemovals.ContainsKey(pylon.PositionInTiles));
                ApplyServerSnapshot(ServerPylons, pylons);
                return new List<TeleportPylonInfo>(ServerPylons);
            }
            if (Main.tile == null)
            {
                Mod.LogInfo("晶塔快照为空：原版列表为空且世界图块尚未加载。");
                return pylons;
            }

            int activePylonTiles, parsedPylonFrames;
            List<TeleportPylonInfo> recovered = CollectWorldPylons(out activePylonTiles, out parsedPylonFrames);
            _serverTilesScanned = true;
            foreach (TeleportPylonInfo pylon in recovered)
            {
                if (!HasCompletePylon(pylon) || TileEntity.ByPosition.ContainsKey(pylon.PositionInTiles)) continue;
                // 恢复原版实体，避免下一次原版列表重建再次发送移除通知。
                RestoreMissingServerPylonEntity(pylon);
            }
            ApplyServerSnapshot(ServerPylons, pylons);
            bool changed = ApplyServerSnapshot(ServerPylons, recovered);
            string worldName = Main.ActiveWorldFileData == null ? "unknown" : Main.ActiveWorldFileData.Name;
            Mod.LogInfo($"晶塔回退扫描：世界={worldName}，地图={Main.maxTilesX}x{Main.maxTilesY}，活动晶塔图块={activePylonTiles}，有效帧={parsedPylonFrames}，本次={recovered.Count}，缓存={ServerPylons.Count}，变更={changed}。");
            return new List<TeleportPylonInfo>(ServerPylons);
        }

        private static bool WithinPlacementGrace(Point16 position, ulong currentTick)
        {
            ulong added;
            return ServerPylonAddedAt.TryGetValue(position, out added) && currentTick - added < PylonPlacementGrace;
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

        internal static bool HasCompletePylon(TeleportPylonInfo pylon)
        {
            int x = pylon.PositionInTiles.X, y = pylon.PositionInTiles.Y;
            if (Main.tile == null || x < 0 || y < 0 || x + 2 >= Main.maxTilesX || y + 3 >= Main.maxTilesY) return false;
            for (int dx = 0; dx < 3; dx++)
            for (int dy = 0; dy < 4; dy++)
            {
                Tile tile = Main.tile[x + dx, y + dy];
                TeleportPylonInfo actual;
                if (tile == null || !TryGetPylonInfo(x + dx, y + dy, tile.active(), tile.type, tile.frameX, tile.frameY, out actual)
                    || !actual.Equals(pylon)) return false;
            }
            return true;
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
                if (!pylons.Contains(pylon) && HasCompletePylon(pylon)) pylons.Add(pylon);
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
                    changed |= MergeClientPredictions(Main.GameUpdateCount);
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
