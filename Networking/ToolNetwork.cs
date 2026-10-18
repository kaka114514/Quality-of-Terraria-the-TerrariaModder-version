using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core.Net;
using TerrariaModder.Core.Permissions;
using QualityOfTerraria.Patches;

namespace QualityOfTerraria.Networking
{
    internal static class ToolNetwork
    {
        private const string Operation = "quality-of-terraria.tools.v110";
        private sealed class Peer
        {
            internal Player Player;
            internal string Guid, Nonce, LastRequest, LastResponse;
            internal long Sequence;
            internal int[] Liquids;
            // 大面积框选的分帧铺设任务；完成后单独返回结果。
            internal WandsAndTools.PlacementJob Job;
            internal int JobSlot = -1;
            internal long JobSequence;
            internal bool JobNoConsume;
            internal string JobToken;
        }
        private static readonly Dictionary<int, Peer> Peers = new Dictionary<int, Peer>();
        private static readonly Dictionary<string, int[]> WorldLiquidBalances = new Dictionary<string, int[]>();
        private static bool _initialized;
        private static int _generation;
        private static string _nonce, _pending;
        private static string _payload;
        private static string _action;
        private static Action _afterHello;
        private static long _sequence;
        private static ulong _sent, _liquidFrame;
        private static int _slot = -1, _type, _stack;
        private static byte _prefix;
        private static int[] _singlePlayerLiquids;
        internal static bool Busy => _pending != null;
        internal static bool HasPendingMaterial => Busy && _slot >= 0;
        internal static string LiquidStatus { get; private set; } = "服务器待确认";

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            NetSync.OnStorageRequest += ReceiveRequest;
            NetSync.OnStorageResponse += ReceiveResponse;
        }

        internal static void ResetSession()
        {
            _generation++;
            Peers.Clear();
            WorldLiquidBalances.Clear();
            if (_singlePlayerLiquids != null) SetLiquids(_singlePlayerLiquids);
            _singlePlayerLiquids = null;
            _nonce = _pending = null;
            _payload = null;
            _action = null;
            LiquidStatus = "服务器待确认";
            _afterHello = null;
            _sequence = 0;
            _slot = -1;
            _sent = _liquidFrame = 0;
        }

        internal static void Unload()
        {
            if (_initialized)
            {
                NetSync.OnStorageRequest -= ReceiveRequest;
                NetSync.OnStorageResponse -= ReceiveResponse;
            }
            _initialized = false;
            ResetSession();
        }

        internal static void RevokeServerToken(int client, string token)
        {
            Peer peer;
            if (Peers.TryGetValue(client, out peer) && peer.Nonce == token) Peers.Remove(client);
        }

        internal static void Update()
        {
            foreach (int client in new List<int>(Peers.Keys))
                if (!SamePeer(client, Peers[client])) Peers.Remove(client);
            if (Main.netMode == 2) StepPlacementJobs();
            if (!Busy || Main.netMode != 1) return;
            Player player = Main.LocalPlayer;
            player.mouseInterface = true;
            player.controlUseItem = false;
            if (Main.GameUpdateCount - _sent < 300) return;
            if (_slot >= 0)
            {
                _sent = Main.GameUpdateCount;
                NetSyncPayload.SendRequest(Operation, _payload);
                Main.NewText("正在查询同一工具操作的材料结果；等待期间库存保持锁定", 255, 180, 100);
                return;
            }
            _pending = _nonce = null;
            _payload = null;
            LiquidStatus = "服务器未确认";
            _afterHello = null;
            _slot = -1;
            Main.NewText("工具操作未收到服务器确认，请等待库存同步后重试", 255, 180, 100);
        }

        internal static void Request(string action, int slot = -1, params int[] values)
        {
            if (Main.netMode != 1 || !Mod.WorldActive || Busy) return;
            // 建造请求由服务器按物品类型、前缀、数量和选中槽位核验；本地使用动画不能阻止
            // 快速放置，因为手持方块拖拽本身会保持原版使用动画。
            if (RemoteStorageOperations.Busy)
            {
                Main.NewText("正在等待服务器存储操作确认，请稍后使用工具", 255, 180, 100);
                return;
            }
            if (!NetSync.LocalPlayerHasModAccess("quality-of-terraria"))
            {
                Main.NewText("服务器未授予本模组工具权限", 255, 120, 120);
                return;
            }
            if (action == "liquid" && _liquidFrame != 0 && Main.GameUpdateCount - _liquidFrame < 10) return;
            // 服务器签发世界会话后继续本次操作，旧请求不能在重连后执行。
            if (_nonce == null)
            {
                if (_singlePlayerLiquids == null) _singlePlayerLiquids = GetLiquids();
                string requestedAction = action; int requestedSlot = slot; int[] requestedValues = (int[])values.Clone();
                _afterHello = () => Request(requestedAction, requestedSlot, requestedValues);
                action = "hello"; slot = -1; values = new int[0];
            }
            _pending = Guid.NewGuid().ToString("N");
            _action = action;
            _sent = Main.GameUpdateCount;
            if (action == "liquid") { _liquidFrame = _sent; LiquidStatus = "服务器待确认"; }
            _slot = slot;
            Item item = slot >= 0 && slot < Main.LocalPlayer.inventory.Length ? Main.LocalPlayer.inventory[slot] : null;
            _type = item == null || item.IsAir ? 0 : item.type;
            _stack = item == null || item.IsAir ? 0 : item.stack;
            _prefix = item == null || item.IsAir ? (byte)0 : item.prefix;
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(_pending); writer.Write(_nonce ?? ""); writer.Write(++_sequence);
                writer.Write(action); writer.Write(slot); writer.Write(_type); writer.Write(_stack); writer.Write(_prefix);
                writer.Write(values.Length); foreach (int value in values) writer.Write(value);
                _payload = Convert.ToBase64String(stream.ToArray());
                if (slot >= 0)
                {
                    string before = InventoryState(Main.LocalPlayer, true);
                    if (!StorageTransactionJournal.BeginClient(RemoteStorageOperations.WorldIdentity, _nonce + "." + _sequence, before, _payload))
                    {
                        _pending = _payload = null; _slot = -1;
                        Main.NewText(StorageTransactionJournal.Status, 255, 160, 100);
                        return;
                    }
                    Main.LocalPlayer.inventory[58] = Main.mouseItem;
                    for (int i = 0; i < 59; i++) NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, Main.myPlayer, i);
                }
                NetSyncPayload.SendRequest(Operation, _payload);
            }
            // 联机卡顿诊断：请求本身在主线程完成日志写入与库存同步，超过一帧就记录一次。
            if (clock.ElapsedMilliseconds >= 8) Mod.LogInfo($"[快速放置] 客户端请求准备耗时 {clock.ElapsedMilliseconds}ms（{action}）");
            if (action != "liquid") Main.NewText("正在等待服务器工具操作确认", 200, 220, 255);
        }

        private static bool SamePeer(int client, Peer peer)
        {
            return client >= 0 && client < Main.maxPlayers && Main.player[client] != null && Main.player[client].active &&
                   ReferenceEquals(peer.Player, Main.player[client]) && peer.Guid == PermissionService.GetGuid(client);
        }

        private static void ReceiveRequest(int client, string operation, string payload)
        {
            if (operation != Operation || !_initialized) return;
            string complete;
            if (!NetSyncPayload.TryAcceptRequest(client, operation, payload, out complete)) return;
            int generation = _generation;
            NetworkRuntime.Enqueue(() => { if (_initialized && generation == _generation) HandleRequest(client, complete); });
        }

        private static void HandleRequest(int client, string payload)
        {
            if (Main.netMode != 2 || !Mod.WorldActive || client < 0 || client >= Main.maxPlayers) return;
            Player player = Main.player[client];
            if (player == null || !player.active) return;
            string token = "", message = "工具请求无效";
            Peer peer = null;
            int slot = -1;
            long requestSequence = 0;
            bool ok = false, remember = false, noConsume = false;
            try
            {
                if (payload == null || payload.Length > 1024) throw new InvalidDataException(message);
                using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(payload))))
                {
                    token = reader.ReadString(); string nonce = reader.ReadString(); long sequence = reader.ReadInt64();
                    requestSequence = sequence;
                    string action = reader.ReadString(); slot = reader.ReadInt32(); int type = reader.ReadInt32(), stack = reader.ReadInt32(); byte prefix = reader.ReadByte();
                    int count = reader.ReadInt32();
                    if (token.Length != 32 || nonce.Length > 32 || sequence <= 0 || count < 0 || count > 5) throw new InvalidDataException(message);
                    var values = new int[count]; for (int i = 0; i < count; i++) values[i] = reader.ReadInt32();
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException(message);
                    if (!PermissionService.HasModAccess(client, "quality-of-terraria")) throw new InvalidDataException("服务器未授予本模组工具权限");
                    if (string.IsNullOrEmpty(PermissionService.GetGuid(client))) throw new InvalidDataException("玩家身份尚未确认");
                    if (Peers.TryGetValue(client, out peer) && !SamePeer(client, peer)) { Peers.Remove(client); peer = null; }
                    if (action == "hello")
                    {
                        if (peer == null)
                        {
                            string identity = PermissionService.GetGuid(client);
                            int[] balance;
                            if (!WorldLiquidBalances.TryGetValue(identity, out balance)) WorldLiquidBalances[identity] = balance = new int[4];
                            peer = new Peer { Player = player, Guid = identity, Liquids = balance };
                        }
                        peer.Nonce = Guid.NewGuid().ToString("N");
                        peer.Sequence = 0;
                        peer.LastRequest = peer.LastResponse = null;
                        Peers[client] = peer;
                        StorageTransactionJournal.InitializeServer(RemoteStorageOperations.WorldIdentity);
                        StorageTransactionJournal.RegisterServerToken(peer.Guid, peer.Nonce);
                        message = "工具服务器会话已确认";
                        ok = remember = true;
                    }
                    else
                    {
                        if (peer == null || nonce != peer.Nonce) throw new InvalidDataException("工具会话已过期，请重新使用");
                        if (sequence == peer.Sequence && peer.LastRequest == payload)
                        {
                            // 分帧铺设还在进行时不重复执行，也不提前返回结果。
                            if (peer.LastResponse != null)
                                NetSyncPayload.SendResponseTo(client, true, Operation, RefreshResponseWorldFlags(peer.LastResponse));
                            return;
                        }
                        if (sequence <= peer.Sequence) throw new InvalidDataException("工具请求重复或过期");
                        peer.Sequence = sequence;
                        remember = true;
                        if (player.dead) throw new InvalidDataException("玩家已死亡，工具操作未执行");
                        if (action == "place")
                        {
                            var config = SettingsSync.GetPlayerConfig(client);
                            if (config == null) throw new InvalidDataException("正在等待个人设置交换确认，请稍后使用建造工具");
                            noConsume = config.WandMaterialNoConsume;
                            if (slot < 0 || slot >= 50 || player.inventory[slot] == null) throw new InvalidDataException("未找到可用材料");
                            Item material = player.inventory[slot];
                            if (material.IsAir || material.type != type || (!noConsume && material.stack != stack) || material.prefix != prefix) throw new InvalidDataException("材料已改变，请等待库存同步后重试");
                            if (count != 4 || player.selectedItem != slot || (material.createTile < 0 && material.createWall <= 0)) throw new InvalidDataException("手持材料与请求不一致");
                            CheckArea(values);
                            if (WandsAndTools.PlacementTiles(values[0], values[1], values[2], values[3]) > WandsAndTools.SynchronousPlacementLimit)
                            {
                                // 大范围铺设分帧执行：先记住请求，完成后按同一序号返回结果，客户端重发不会重复铺设。
                                peer.Job = WandsAndTools.CreatePlacementJob(player, slot, material.createTile < 0, values[0], values[1], values[2], values[3], noConsume);
                                peer.JobSlot = slot;
                                peer.JobSequence = sequence;
                                peer.JobNoConsume = noConsume;
                                peer.JobToken = token;
                                peer.LastRequest = payload;
                                peer.LastResponse = null;
                                return;
                            }
                            message = "快速放置：服务器已铺设 " + WandsAndTools.ApplyDragPlacement(player, values[0], values[1], values[2], values[3]) + " 格";
                        }
                        else if (action == "liquid")
                        {
                            if (count != 5 || values[0] < 10 || values[0] >= Main.maxTilesX - 10 || values[1] < 10 || values[1] >= Main.maxTilesY - 10 ||
                                values[2] < 1 || values[2] > 4 || values[3] < 0 || values[3] > 1 || values[4] < 0 || values[4] > 1) throw new InvalidDataException("液体坐标或模式无效");
                            int[] local = GetLiquids();
                            try
                            {
                                SetLiquids(peer.Liquids);
                                WandsAndTools.ApplyLiquid(values[0], values[1], (WandsAndTools.LiquidWandMode)values[2], values[3] != 0, values[4] != 0);
                                Array.Copy(GetLiquids(), peer.Liquids, 4);
                            }
                            finally { SetLiquids(local); }
                            message = "液体操作已由服务器确认";
                        }
                        else if (action == "home" || action == "weather")
                        {
                            if (count != 0 || !PermissionService.IsAdmin(client)) throw new InvalidDataException("城镇召回和气象控制需要房主或服务器管理权限");
                            if (action == "home") message = "城镇管理：服务器已召回 " + WandsAndTools.ApplyNPCHome() + " 位城镇NPC";
                            else { WandsAndTools.ApplyWeather(); message = Main.raining ? "气象控制：服务器已开启暴雨" : "气象控制：服务器已转晴"; }
                        }
                        else if (action == "spawn-rate")
                        {
                            if (!PermissionService.IsAdmin(client)) throw new InvalidDataException("刷怪倍率需要房主或服务器管理权限");
                            if (count != 1 || values[0] < 0 || values[0] > 1000) throw new InvalidDataException("刷怪倍率必须为0至100倍");
                            if (!WorldQoLCompletionPatches.SetSpawnRate(values[0] / 10f)) throw new InvalidDataException("服务器未应用刷怪倍率");
                            message = "服务器刷怪倍率已设为 " + (values[0] / 10f).ToString("0.#") + "x";
                        }
                        else if (action == "world-toggle")
                        {
                            if (!PermissionService.IsAdmin(client)) throw new InvalidDataException("节日和世界状态需要房主或服务器管理权限");
                            if (count != 2 || values[0] < 0 || values[0] > 5 || values[1] < 0 || values[1] > 1) throw new InvalidDataException("世界状态请求无效");
                            bool[] flags = GetWorldFlags(); flags[values[0]] = values[1] != 0; SetWorldFlags(flags);
                            NetMessage.SendData(MessageID.WorldData);
                            message = "服务器已确认世界状态";
                        }
                        else if (action == "travel-shop")
                        {
                            // talkNPC属于客户端对话状态，原版未同步此字段；服务器核验附近活旅商。
                            if (count != 0 || !player.isNearNPC(NPCID.TravellingMerchant, 200f)) throw new InvalidDataException("请在旅商附近打开商店后刷新");
                            if (!WorldFeatureActions.RefreshTravelShop()) throw new InvalidDataException("旅商刷新未开启或旅商已经离开");
                            message = "旅商货物已由服务器刷新";
                        }
                        else throw new InvalidDataException("未知工具操作");
                        ok = true;
                    }
                }
            }
            catch (Exception error) { message = error.Message; }
            string response = RecordAndBuildResponse(peer, remember, token, slot, player, ok, message, noConsume, requestSequence);
            if (peer != null && remember) { peer.LastRequest = payload; peer.LastResponse = response; }
            if (ok && slot >= 0 && !noConsume) NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, client, slot);
            NetSyncPayload.SendResponseTo(client, ok, Operation, response);
        }

        // 服务端分帧完成大面积铺设；完成后按原请求序号返回与立即铺设一致的响应。
        private static void StepPlacementJobs()
        {
            if (!Mod.WorldActive || Peers.Count == 0) return;
            foreach (KeyValuePair<int, Peer> pair in new List<KeyValuePair<int, Peer>>(Peers))
            {
                Peer peer = pair.Value;
                if (peer.Job == null) continue;
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                WandsAndTools.StepPlacementJob(peer.Job, WandsAndTools.PlacementChunksPerFrame);
                // 服务端卡顿诊断：单帧铺设耗时，超过一帧就记录一次。
                if (clock.ElapsedMilliseconds >= 8) Mod.LogInfo($"[快速放置] 服务器单帧铺设耗时 {clock.ElapsedMilliseconds}ms（已铺设 {peer.Job.Placed} 格）");
                if (!peer.Job.Done) continue;
                int placed = peer.Job.Placed;
                peer.Job = null;
                CompletePlacement(pair.Key, peer, placed);
            }
        }

        private static void CompletePlacement(int client, Peer peer, int placed)
        {
            int slot = peer.JobSlot;
            string message = "快速放置：服务器已铺设 " + placed + " 格";
            string response = RecordAndBuildResponse(peer, true, peer.JobToken, slot, peer.Player, true, message, peer.JobNoConsume, peer.JobSequence);
            peer.JobSlot = -1;
            peer.JobToken = null;
            peer.LastResponse = response;
            if (slot >= 0 && !peer.JobNoConsume) NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, client, slot);
            NetSyncPayload.SendResponseTo(client, true, Operation, response);
        }

        private static string RecordAndBuildResponse(Peer peer, bool record, string token, int slot, Player player, bool ok, string message, bool noConsume, long sequence)
        {
            if (peer != null && record && slot >= 0)
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    // 日志valid表示库存结果已确定；动作失败也可能已部分消耗材料。
                    writer.Write(peer.Nonce); writer.Write(sequence); writer.Write(true); writer.Write(message);
                    writer.Write(Convert.FromBase64String(InventoryState(player, false)));
                    StorageTransactionJournal.RecordServer(peer.Guid, peer.Nonce + "." + sequence, Convert.ToBase64String(stream.ToArray()));
                }
            }
            return BuildResponse(token, peer, slot, player, ok, message, noConsume);
        }

        private static string InventoryState(Player player, bool client)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                for (int slot = 0; slot < 59; slot++)
                {
                    Item item = client && slot == 58 ? Main.mouseItem : player.inventory[slot];
                    bool empty = item == null || item.IsAir;
                    writer.Write(empty ? 0 : item.type); writer.Write(empty ? 0 : item.stack);
                    writer.Write(empty ? (byte)0 : item.prefix); writer.Write(!empty && item.favorited);
                }
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static void CheckArea(int[] area)
        {
            if (area[0] < 10 || area[0] >= Main.maxTilesX - 10 || area[2] < 10 || area[2] >= Main.maxTilesX - 10 ||
                area[1] < 10 || area[1] >= Main.maxTilesY - 10 || area[3] < 10 || area[3] >= Main.maxTilesY - 10 ||
                ((long)Math.Abs(area[2] - area[0]) + 1) * (Math.Abs(area[3] - area[1]) + 1) > 4096)
                throw new InvalidDataException("框选超出世界或4096格单次上限，请缩小范围");
        }

        private static string BuildResponse(string token, Peer peer, int slot, Player player, bool ok, string message, bool noConsume)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(token); writer.Write(peer?.Nonce ?? ""); writer.Write(ok); writer.Write(message);
                writer.Write(slot);
                Item item = slot >= 0 && slot < 50 ? player.inventory[slot] : null;
                writer.Write(item == null || item.IsAir ? 0 : item.type); writer.Write(item == null || item.IsAir ? 0 : item.stack);
                writer.Write(item == null || item.IsAir ? (byte)0 : item.prefix);
                foreach (int amount in peer?.Liquids ?? new int[4]) writer.Write(amount);
                writer.Write(noConsume);
                foreach (bool flag in GetWorldFlags()) writer.Write(flag);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static string RefreshResponseWorldFlags(string response)
        {
            byte[] bytes = Convert.FromBase64String(response);
            bool[] flags = GetWorldFlags();
            // 协议最后六字节是世界状态；保留原操作的材料结果，只刷新当前世界快照。
            for (int i = 0; i < flags.Length; i++) bytes[bytes.Length - flags.Length + i] = flags[i] ? (byte)1 : (byte)0;
            return Convert.ToBase64String(bytes);
        }

        private static void ReceiveResponse(bool success, string operation, string payload)
        {
            if (operation != Operation || !_initialized) return;
            string complete;
            if (!NetSyncPayload.TryAcceptResponse(operation, payload, out complete)) return;
            int generation = _generation;
            NetworkRuntime.Enqueue(() => { if (_initialized && generation == _generation) HandleResponse(complete); });
        }

        private static void HandleResponse(string payload)
        {
            if (Main.netMode != 1 || !Mod.WorldActive || !Busy || payload == null || payload.Length > 4096) return;
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(payload))))
                {
                    if (reader.ReadString() != _pending) return;
                    string nonce = reader.ReadString(); bool ok = reader.ReadBoolean(); string message = reader.ReadString();
                    int slot = reader.ReadInt32(), type = reader.ReadInt32(), stack = reader.ReadInt32(); byte prefix = reader.ReadByte();
                    var liquids = new int[4]; for (int i = 0; i < 4; i++) liquids[i] = reader.ReadInt32();
                    bool noConsume = reader.ReadBoolean();
                    var flags = new bool[6]; for (int i = 0; i < 6; i++) flags[i] = reader.ReadBoolean();
                    if (nonce.Length > 32 || slot != _slot || type < 0 || type >= ItemID.Count || stack < 0 || reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("服务器工具响应无效");
                    foreach (int amount in liquids) if (amount < 0) throw new InvalidDataException("服务器液体库存无效");
                    if (slot >= 0 && nonce != _nonce)
                    {
                        _pending = _payload = _nonce = null; _slot = -1;
                        Main.NewText("工具会话已失效，保留事务日志并等待服务器恢复材料结果", 255, 160, 100);
                        return;
                    }
                    _nonce = nonce.Length == 32 ? nonce : null;
                    bool conflict = false;
                    if (slot >= 0 && !noConsume)
                    {
                        Item item = Main.LocalPlayer.inventory[slot];
                        // 原版SyncEquipment可能先到；只接受请求前状态或同一服务器结果。
                        bool expected = item.type == _type && item.stack == _stack && item.prefix == _prefix;
                        bool already = (item.IsAir && type == 0) || (item.type == type && item.stack == stack && item.prefix == prefix);
                        bool validResult = (type == 0 && stack == 0) || (type == _type && prefix == _prefix && stack <= _stack);
                        if (!validResult) { conflict = true; message += "；材料结果不符合预期，保留事务等待核验"; }
                        else if (expected) { if (type == 0) item.TurnToAir(); else item.stack = stack; }
                        else if (!already) { conflict = true; message += "；材料发生外部变化，保留等待状态以避免覆盖物品"; }
                    }
                    SetLiquids(liquids);
                    SetWorldFlags(flags);
                    Player localPlayer = Main.LocalPlayer;
                    if (ok && _action == "travel-shop" && localPlayer.talkNPC >= 0 && localPlayer.talkNPC < Main.maxNPCs &&
                        Main.npc[localPlayer.talkNPC].active && Main.npc[localPlayer.talkNPC].type == NPCID.TravellingMerchant &&
                        Main.npcShop > 0 && Main.npcShop < Main.instance.shop.Length)
                        Main.instance.shop[Main.npcShop].SetupShop(Main.npcShop);
                    LiquidStatus = ok ? "服务器已确认" : "服务器未确认";
                    if (_action != "liquid" || !ok) Main.NewText(message, ok ? (byte)120 : (byte)255, ok ? (byte)255 : (byte)160, 120);
                    if (conflict) return;
                    if (slot >= 0 && !StorageTransactionJournal.CompleteClient())
                    {
                        Main.NewText(StorageTransactionJournal.Status, 255, 160, 100);
                        return;
                    }
                    _pending = null; _slot = -1;
                    _payload = null;
                    Action afterHello = _afterHello; _afterHello = null;
                    if (ok && _nonce != null) afterHello?.Invoke();
                }
            }
            catch (Exception error)
            {
                if (_slot < 0) { _pending = _nonce = _payload = null; _slot = -1; }
                LiquidStatus = "服务器未确认";
                _afterHello = null;
                Mod.LogError("工具服务器响应读取失败", error);
                Main.NewText("工具服务器响应读取失败，请等待库存同步", 255, 120, 120);
            }
        }

        private static int[] GetLiquids() => new[] { WandsAndTools.StoredWater, WandsAndTools.StoredLava, WandsAndTools.StoredShimmer, WandsAndTools.StoredHoney };
        private static bool[] GetWorldFlags() => new[] { Main.halloween, Main.xMas, Main.drunkWorld, Main.zenithWorld, Main.remixWorld, Main.dontStarveWorld };
        private static void SetWorldFlags(bool[] flags)
        {
            Main.halloween = flags[0]; Main.xMas = flags[1]; Main.drunkWorld = flags[2];
            Main.zenithWorld = flags[3]; Main.remixWorld = flags[4]; Main.dontStarveWorld = flags[5];
        }
        private static void SetLiquids(int[] amounts)
        {
            WandsAndTools.StoredWater = amounts[0]; WandsAndTools.StoredLava = amounts[1];
            WandsAndTools.StoredShimmer = amounts[2]; WandsAndTools.StoredHoney = amounts[3];
        }
    }
}
