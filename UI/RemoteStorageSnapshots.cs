using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core.Net;

namespace QualityOfTerraria.UI
{
    // 此通道只获取数据。材料转移与合成分别沿用已打开箱会话和原版 CraftingRequests。
    internal static class RemoteStorageSnapshots
    {
        private const string Operation = "quality-of-terraria.snapshot.v1";
        private static readonly Dictionary<int, Chest> _chests = new Dictionary<int, Chest>();
        private static readonly Dictionary<int, ulong> _received = new Dictionary<int, ulong>();
        private static readonly Queue<long> _queue = new Queue<long>();
        private static bool _initialized, _pending;
        private static string _session;
        private static string _pendingToken;
        private static readonly HashSet<long> _pendingKeys = new HashSet<long>();
        private static ulong _requestFrame, _refreshFrame;
        internal static string Status { get; private set; } = "远程箱子快照尚未确认";

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _session = Guid.NewGuid().ToString("N");
            NetSync.OnStorageRequest += HandleRequest;
            NetSync.OnStorageResponse += HandleResponse;
        }

        internal static void ResetSession()
        {
            if (_initialized)
            {
                NetSync.OnStorageRequest -= HandleRequest;
                NetSync.OnStorageResponse -= HandleResponse;
            }
            _initialized = _pending = false;
            _chests.Clear();
            _received.Clear();
            _queue.Clear();
            _session = null;
            _pendingToken = null;
            _pendingKeys.Clear();
            _refreshFrame = _requestFrame = 0;
            Status = "远程箱子快照尚未确认";
        }

        internal static Chest GetChest(int index)
        {
            ulong frame;
            Chest chest;
            return _received.TryGetValue(index, out frame) && Main.GameUpdateCount - frame <= 900 && _chests.TryGetValue(index, out chest) ? chest : null;
        }

        internal static void Update()
        {
            if (!_initialized || Main.netMode != 1 || !UIManager.CanRender) return;
            if (_pending)
            {
                if (Main.GameUpdateCount - _requestFrame < 300) return;
                _pending = false;
                Status = "远程快照请求未确认；过期数据不可用";
            }
            if (_queue.Count == 0)
            {
                if (_refreshFrame != 0 && Main.GameUpdateCount - _refreshFrame < 300) return;
                foreach (long key in ExtremeStorageUI.GetMarkedCoordinates()) _queue.Enqueue(key);
                _refreshFrame = Main.GameUpdateCount;
            }
            if (_queue.Count == 0) return;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                _pendingToken = _session + "." + Guid.NewGuid().ToString("N");
                writer.Write(_pendingToken);
                int count = Math.Min(12, _queue.Count);
                writer.Write(count);
                _pendingKeys.Clear();
                for (int i = 0; i < count; i++)
                {
                    long key = _queue.Dequeue();
                    _pendingKeys.Add(key);
                    writer.Write(key);
                }
                _pending = true;
                _requestFrame = Main.GameUpdateCount;
                Status = "正在等待服务器箱子快照";
                NetSync.SendStorageRequest(Operation, Convert.ToBase64String(stream.ToArray()));
            }
        }

        private static void HandleRequest(int client, string operation, string payload)
        {
            if (operation != Operation || Main.netMode != 2 || client < 0 || client >= Main.maxPlayers || !Main.player[client].active) return;
            try
            {
                if (payload.Length > 1024) throw new InvalidDataException("快照请求过大");
                using (var input = new BinaryReader(new MemoryStream(Convert.FromBase64String(payload))))
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(input.ReadString());
                    int count = input.ReadInt32();
                    if (count < 1 || count > 12) throw new InvalidDataException("快照数量不合法");
                    writer.Write(count);
                    for (int i = 0; i < count; i++)
                    {
                        long key = input.ReadInt64();
                        int x = (int)(key >> 32), y = (int)key;
                        writer.Write(key);
                        int index = x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY ? Chest.FindChest(x, y) : -1;
                        Chest chest = index >= 0 ? Main.chest[index] : null;
                        int owner = index >= 0 ? Chest.UsingChest(index) : -1;
                        bool available = chest != null && !Chest.IsLocked(x, y) && (owner < 0 || owner == client);
                        writer.Write(available ? index : -1);
                        if (!available) continue;
                        writer.Write(chest.name ?? "");
                        writer.Write(chest.item.Length);
                        foreach (Item item in chest.item)
                        {
                            bool empty = item == null || item.IsAir;
                            writer.Write(empty ? 0 : item.type);
                            writer.Write(empty ? 0 : item.stack);
                            writer.Write(empty ? (byte)0 : item.prefix);
                        }
                    }
                    NetSync.SendStorageResponseTo(client, true, Operation, Convert.ToBase64String(stream.ToArray()));
                }
            }
            catch (Exception error) { Mod.LogError("获取服务器箱子快照失败", error); }
        }

        private static void HandleResponse(bool success, string operation, string payload)
        {
            if (operation != Operation || Main.netMode != 1 || !_pending) return;
            try
            {
                if (!success || payload.Length > 32768) throw new InvalidDataException("服务器快照不可用");
                using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(payload))))
                {
                    if (reader.ReadString() != _pendingToken) return;
                    int count = reader.ReadInt32();
                    if (count != _pendingKeys.Count) throw new InvalidDataException("快照数量不合法");
                    var snapshots = new Dictionary<int, Chest>();
                    var keys = new HashSet<long>();
                    for (int i = 0; i < count; i++)
                    {
                        long key = reader.ReadInt64();
                        if (!_pendingKeys.Contains(key) || !keys.Add(key)) throw new InvalidDataException("快照坐标与请求不一致");
                        int index = reader.ReadInt32();
                        if (index < 0) continue;
                        string name = reader.ReadString();
                        int slots = reader.ReadInt32();
                        if (index >= Main.maxChests || slots != 40) throw new InvalidDataException("箱子快照不合法");
                        var chest = Chest.CreateOutOfArray(index, (int)(key >> 32), (int)key, slots);
                        chest.name = name;
                        for (int s = 0; s < slots; s++)
                        {
                            int type = reader.ReadInt32(), stack = reader.ReadInt32();
                            byte prefix = reader.ReadByte();
                            if (type < 0 || type >= ItemID.Count || stack < 0) throw new InvalidDataException("快照物品不合法");
                            var item = new Item();
                            if (type > 0 && stack > 0) { item.SetDefaults(type); item.Prefix(prefix); item.stack = stack; }
                            chest.item[s] = item;
                        }
                        snapshots.Add(index, chest);
                    }
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("快照含多余数据");
                    // 整批校验完成后再替换，损坏响应不会部分污染缓存。
                    foreach (int oldIndex in new List<int>(_chests.Keys))
                        if (keys.Contains(((long)_chests[oldIndex].x << 32) | (uint)_chests[oldIndex].y)) { _chests.Remove(oldIndex); _received.Remove(oldIndex); }
                    foreach (var pair in snapshots) { _chests[pair.Key] = pair.Value; _received[pair.Key] = Main.GameUpdateCount; }
                }
                _pending = false;
                Status = "远程快照已确认；实际合成仍由服务器核验";
                ExtremeStorageUI.RefreshItems();
                Recipe.UpdateRecipeList();
            }
            catch (Exception error)
            {
                _pending = false;
                Status = "远程快照读取失败";
                Mod.LogError(Status, error);
            }
        }

        internal static void Invalidate()
        {
            _chests.Clear();
            _received.Clear();
            _pending = false;
            _pendingToken = null;
            _queue.Clear();
            _refreshFrame = 0;
        }
    }
}
