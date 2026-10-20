using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core.Net;
using TerrariaModder.Core.Permissions;
using QualityOfTerraria.UI;

namespace QualityOfTerraria.Networking
{
    internal static class RemoteStorageOperations
    {
        private const string Prefix = "quality-of-terraria.storage.v2.";
        private sealed class Peer
        {
            internal Player Player;
            internal string Identity, CharacterId, Hello, Token;
            internal long LastSequence;
            internal long LastMarkSequence;
            internal string LastMarkRequest, LastMarkReply;
            internal long LastOpenSequence;
            internal string LastOpenRequest, LastOpenReply;
            internal int RemoteChest = -1;
            internal readonly Dictionary<long, string> Replies = new Dictionary<long, string>();
            internal readonly Dictionary<long, string> Requests = new Dictionary<long, string>();
        }
        private struct State
        {
            internal int Type, Stack;
            internal byte Affix;
            internal bool Favorite;
            internal State(Item item)
            {
                bool empty = item == null || item.IsAir;
                Type = empty ? 0 : item.type; Stack = empty ? 0 : item.stack;
                Affix = empty ? (byte)0 : item.prefix; Favorite = !empty && item.favorited;
            }
            internal bool Matches(Item item)
            {
                State other = new State(item);
                return Type == other.Type && Stack == other.Stack && Affix == other.Affix;
            }
            internal void Write(BinaryWriter writer)
            { writer.Write(Type); writer.Write(Stack); writer.Write(Affix); writer.Write(Favorite); }
            internal static State Read(BinaryReader reader)
            {
                var state = new State { Type = reader.ReadInt32(), Stack = reader.ReadInt32(), Affix = reader.ReadByte(), Favorite = reader.ReadBoolean() };
                if (state.Type < 0 || state.Type >= ItemID.Count || state.Stack < 0 || state.Stack > 1000000 ||
                    (state.Type == 0) != (state.Stack == 0)) throw new InvalidDataException("物品状态不合法");
                return state;
            }
            internal Item Item()
            {
                var item = new Item();
                if (Type > 0) { item.SetDefaults(Type); item.Prefix(Affix); item.stack = Stack; item.favorited = Favorite; }
                return item;
            }
        }

        private static readonly Dictionary<int, Peer> Peers = new Dictionary<int, Peer>();
        private static readonly HashSet<long> ReceivedMarks = new HashSet<long>();
        private static readonly HashSet<int> ReceivedParts = new HashSet<int>();
        private static readonly Dictionary<int, long[]> PartKeys = new Dictionary<int, long[]>();
        private static readonly HashSet<long> SentMarks = new HashSet<long>();
        private static bool _initialized;
        private static int _lifecycle;
        private static Action<int, string, string> _receiveRequest;
        private static Action<bool, string, string> _receiveResponse;
        private static string _hello, _characterId, _token, _world, _pending, _markPending, _openPending;
        private static string _receivingToken, _receivingWorld, _receivingOwner;
        private static int _receivingParts;
        private static State[] _before;
        private static long _sequence, _pendingSequence, _openSequence;
        private static int _remoteChest = -1;
        private static ulong _lastHello, _lastSend, _lastMarks, _lastOpen;
        private static string _recoveryPending;
        private static ulong _lastRecovery;
        private static bool _recoveredInventory;
        private static ulong _lastTransferMessage;
        private static int _pendingTransferCount;
        private static KeyValuePair<long, bool>[] _markChanges;
        private static ulong _lastBusyWarning; // 上次显示"请等待"提示的时间

        internal static bool Busy => _pending != null || StorageTransactionJournal.ClientLocked;
        internal static bool Ready => !string.IsNullOrEmpty(_token);
        internal static string WorldIdentity => Main.netMode == 2 ? ServerMarkedChests.WorldIdentity : _world;
        internal static string Status { get; private set; } = "等待服务器确认标记箱子";

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _hello = Guid.NewGuid().ToString("N");
            if (Main.netMode != 2)
            {
                var file = Main.ActivePlayerFileData;
                try { _characterId = StorageCharacterIdentity.GetOrCreate(file); }
                catch (Exception error) { Status = error.Message; Mod.LogError("读取角色箱子标记身份失败", error); }
                StorageTransactionJournal.InitializeClient(file == null || string.IsNullOrEmpty(file.Path) ? null :
                    (file.IsCloudSave ? "cloud:" : "local:") + file.Path);
                if (StorageTransactionJournal.ClientLocked) Status = StorageTransactionJournal.Status;
            }
            int lifecycle = ++_lifecycle;
            _receiveRequest = (client, operation, payload) =>
            {
                if (!IsOperation(operation)) return;
                NetworkRuntime.Enqueue(() => { if (_initialized && lifecycle == _lifecycle) HandleRequest(client, operation, payload); });
            };
            _receiveResponse = (success, operation, payload) =>
            {
                if (!IsOperation(operation)) return;
                NetworkRuntime.Enqueue(() => { if (_initialized && lifecycle == _lifecycle) HandleResponse(success, operation, payload); });
            };
            NetSync.OnStorageRequest += _receiveRequest;
            NetSync.OnStorageResponse += _receiveResponse;
        }

        private static bool IsOperation(string operation)
        {
            return operation == Prefix + "hello" || operation == Prefix + "marks" || operation == Prefix + "move"
                || operation == Prefix + "open" || operation == Prefix + "recover" || operation == Prefix + "closed";
        }

        internal static void Update()
        {
            if (!_initialized || !Mod.WorldActive) return;
            if (Main.netMode == 2)
            {
                foreach (int client in Peers.Keys.ToArray())
                {
                    if (!Main.player[client].active || !ReferenceEquals(Main.player[client], Peers[client].Player) || PermissionService.GetGuid(client) != Peers[client].Identity) { Peers.Remove(client); continue; }
                    Peer peer = Peers[client];
                    if (peer.RemoteChest >= 0 && Main.player[client].chest == peer.RemoteChest && !ServerMarkedChests.CanAccess(client, peer.RemoteChest))
                    {
                        int index = peer.RemoteChest;
                        Main.player[client].chest = -1; peer.RemoteChest = -1;
                        NetSyncPayload.SendResponseTo(client, true, Prefix + "closed", Encode(writer => { writer.Write(peer.Token); writer.Write(0L); writer.Write(index); }));
                    }
                }
                return;
            }
            if (Main.netMode != 1 || !UIManager.CanRender) return;
            // 显示累积的转移消息（超时或批次完成）
            if (_pendingTransferCount > 0 && Main.GameUpdateCount - _lastTransferMessage > 12)
            {
                Main.NewText($"已转移 {_pendingTransferCount} 件物品", 150, 230, 255);
                _pendingTransferCount = 0;
            }
            if (_remoteChest >= 0 && Main.LocalPlayer.chest != _remoteChest) _remoteChest = -1;
            if (!Ready)
            {
                if (_characterId == null) return;
                if (_lastHello != 0 && Main.GameUpdateCount - _lastHello < 120) return;
                _lastHello = Main.GameUpdateCount;
                NetSyncPayload.SendRequest(Prefix + "hello", _hello + "|" + _characterId);
                return;
            }
            if (_markPending != null && Main.GameUpdateCount - _lastMarks >= 180)
            {
                _lastMarks = Main.GameUpdateCount;
                NetSyncPayload.SendRequest(Prefix + "marks", _markPending);
            }
            else if (_markPending == null) SynchronizeMarks();
            var recovery = StorageTransactionJournal.Pending;
            // 已确认但尚未落盘的记录不再作为断线事务恢复；等待角色保存后自动清除。
            if (_pending == null && recovery != null && !recovery.Confirmed && !ToolNetwork.Busy)
            {
                if (recovery.World != _world) { Status = "角色有未确认的存取，请返回原服务器和原世界恢复库存。"; return; }
                if (_recoveryPending == null || Main.GameUpdateCount - _lastRecovery >= 180)
                {
                    _recoveryPending = Encode(writer => { writer.Write(_token); writer.Write(recovery.Id); });
                    _lastRecovery = Main.GameUpdateCount;
                    NetSyncPayload.SendRequest(Prefix + "recover", _recoveryPending);
                    Status = "正在恢复断线前的未确认库存";
                }
            }
            if (_pending != null && Main.GameUpdateCount - _lastSend >= 180)
            {
                _lastSend = Main.GameUpdateCount;
                Status = "等待服务器确认；正在查询同一操作，未再次扣除物品";
                NetSyncPayload.SendRequest(Prefix + "move", _pending);
            }
            if (_openPending != null && Main.GameUpdateCount - _lastOpen >= 180)
            { _lastOpen = Main.GameUpdateCount; NetSyncPayload.SendRequest(Prefix + "open", _openPending); }
        }

        private static string Encode(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) write(writer);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static BinaryReader Decode(string payload, int maxLength = 8192)
        {
            if (payload == null || payload.Length > maxLength) throw new InvalidDataException("箱子请求过大");
            return new BinaryReader(new MemoryStream(Convert.FromBase64String(payload)));
        }

        private static void End(BinaryReader reader)
        { if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("箱子请求含多余数据"); }

        private static bool ValidPeer(int client, out Peer peer)
        {
            peer = null;
            return Main.netMode == 2 && client >= 0 && client < Main.maxPlayers && Main.player[client].active &&
                Peers.TryGetValue(client, out peer) &&
                ReferenceEquals(peer.Player, Main.player[client]) && peer.Identity == PermissionService.GetGuid(client);
        }

        internal static string GetCharacterId(int client)
        {
            Peer peer;
            return ValidPeer(client, out peer) ? peer.CharacterId : null;
        }

        private static void HandleRequest(int client, string operation, string payload)
        {
            if (Main.netMode != 2 || client < 0 || client >= Main.maxPlayers || !Main.player[client].active) return;
            try
            {
                if (operation == Prefix + "hello") { Hello(client, payload); return; }
                Peer peer;
                if (!ValidPeer(client, out peer)) return;
                string complete;
                if (!NetSyncPayload.TryAcceptRequest(client, operation, payload, out complete)) return;
                payload = complete;
                using (var reader = Decode(complete))
                {
                    if (reader.ReadString() != peer.Token) return;
                    if (operation == Prefix + "marks") ChangeMarks(client, peer, payload, reader);
                    else if (operation == Prefix + "move") Execute(client, peer, payload, reader);
                    else if (operation == Prefix + "open") Open(client, peer, payload, reader);
                    else if (operation == Prefix + "recover") Recover(client, peer, reader);
                }
            }
            catch (Exception error) { Mod.LogError("处理远程箱子请求失败", error); }
        }

        private static void Hello(int client, string hello)
        {
            Guid nonce, character;
            // 个人标记箱是普通玩家功能；HasModAccess 代表管理员/显式授权，不能用作连接身份检查。
            if (hello == null || hello.Length != 65 || hello[32] != '|' ||
                !Guid.TryParseExact(hello.Substring(0, 32), "N", out nonce) ||
                !Guid.TryParseExact(hello.Substring(33), "N", out character) || character == Guid.Empty) return;
            string characterId = character.ToString("N");
            string identity = PermissionService.GetGuid(client), world = ServerMarkedChests.WorldIdentity;
            if (string.IsNullOrEmpty(identity) || string.IsNullOrEmpty(world) || world == Guid.Empty.ToString("N")) return;
            StorageTransactionJournal.InitializeServer(world);
            Peer peer;
            if (!Peers.TryGetValue(client, out peer) || !ReferenceEquals(peer.Player, Main.player[client]) || peer.Identity != identity || peer.Hello != hello)
            {
                peer = new Peer { Player = Main.player[client], Identity = identity, CharacterId = characterId, Hello = hello, Token = Guid.NewGuid().ToString("N") };
                Peers[client] = peer;
            }
            StorageTransactionJournal.RegisterServerToken(identity, peer.Token);
            long[] marks = (ServerMarkedChests.GetKeys(client) ?? new HashSet<long>()).ToArray();
            int parts = Math.Max(1, (marks.Length + 127) / 128);
            for (int part = 0; part < parts; part++)
            {
                int offset = part * 128, count = Math.Min(128, marks.Length - offset), number = part;
                NetSyncPayload.SendResponseTo(client, true, Prefix + "hello", Encode(writer =>
                {
                    writer.Write(hello.Substring(0, 32)); writer.Write(peer.Token); writer.Write(world); writer.Write(StoragePersistence.Identity("character:" + peer.CharacterId));
                    writer.Write(number); writer.Write(parts); writer.Write(count);
                    for (int i = 0; i < count; i++) writer.Write(marks[offset + i]);
                }));
            }
        }

        private static void ChangeMarks(int client, Peer peer, string payload, BinaryReader reader)
        {
            long sequence = reader.ReadInt64();
            if (sequence == peer.LastMarkSequence && peer.LastMarkRequest == payload)
            { NetSyncPayload.SendResponseTo(client, true, Prefix + "marks", peer.LastMarkReply); return; }
            if (sequence <= peer.LastMarkSequence) return;
            int count = reader.ReadInt32();
            if (count < 1 || count > 128) throw new InvalidDataException("标记数量不合法");
            var changes = new List<KeyValuePair<long, bool>>();
            for (int i = 0; i < count; i++) changes.Add(new KeyValuePair<long, bool>(reader.ReadInt64(), reader.ReadBoolean()));
            End(reader);
            var rejected = ServerMarkedChests.ChangeBatch(client, changes);
            string response = Encode(writer =>
            {
                writer.Write(peer.Token); writer.Write(sequence); writer.Write(rejected.Count);
                foreach (long key in rejected) writer.Write(key);
            });
            peer.LastMarkSequence = sequence; peer.LastMarkRequest = payload; peer.LastMarkReply = response;
            NetSyncPayload.SendResponseTo(client, true, Prefix + "marks", response);
        }

        private static void SynchronizeMarks()
        {
            var current = new HashSet<long>(ExtremeStorageUI.GetMarkedCoordinates());
            _markChanges = current.Except(SentMarks).Select(key => new KeyValuePair<long, bool>(key, true))
                .Concat(SentMarks.Except(current).Select(key => new KeyValuePair<long, bool>(key, false))).Take(128).ToArray();
            if (_markChanges.Length == 0) return;
            long sequence = ++_sequence;
            _markPending = Encode(writer =>
            {
                writer.Write(_token); writer.Write(sequence); writer.Write(_markChanges.Length);
                foreach (var change in _markChanges) { writer.Write(change.Key); writer.Write(change.Value); }
            });
            _lastMarks = Main.GameUpdateCount;
            NetSyncPayload.SendRequest(Prefix + "marks", _markPending);
        }

        internal static bool RequestTake(int chest, int slot, Item expected, bool all)
            => Request(all ? 0 : 1, chest, slot, expected);
        internal static bool RequestOpenChest(int index)
        {
            // 打开请求只需坐标；箱子内容和可访问性由服务器在 Open 中核验并返回。
            Chest chest = ExtremeStorageUI.GetChestMetadata(index);
            if (chest == null || !ExtremeStorageUI.IsChestMarked(chest)) { Main.NewText("请先标记箱子。", 255, 210, 120); return false; }
            if (Main.netMode == 0)
            {
                if (!ExtremeStorageUI.CanMutateChest(index)) return false;
                _remoteChest = index;
                ExtremeStorageUI.CloseAfterRemoteOpen();
                OpenVanillaChest(Main.LocalPlayer, chest.x, chest.y, index);
                return true;
            }
            if (Main.netMode != 1 || !Ready || _openPending != null || Busy || ToolNetwork.Busy || Main.LocalPlayer.dead) return false;
            SynchronizeMarksIfIdle();
            if (_markPending != null) { Main.NewText("正在确认箱子标记，请稍后再试。", 255, 210, 120); return false; }
            _openSequence = ++_sequence;
            _openPending = Encode(writer => { writer.Write(_token); writer.Write(_openSequence); writer.Write(index); writer.Write(chest.x); writer.Write(chest.y); });
            _lastOpen = Main.GameUpdateCount;
            NetSyncPayload.SendRequest(Prefix + "open", _openPending);
            return true;
        }

        internal static bool KeepRemoteChest(Player player)
        {
            if (player == null || player.chest < 0) return false;
            if (Main.netMode == 2)
            {
                Peer peer;
                return ValidPeer(player.whoAmI, out peer) && peer.RemoteChest == player.chest && ServerMarkedChests.CanAccess(player.whoAmI, player.chest);
            }
            return player == Main.LocalPlayer && _remoteChest == player.chest && ExtremeStorageUI.CanMutateChest(player.chest) &&
                ExtremeStorageUI.IsChestMarked(Main.chest[player.chest]);
        }

        private static void OpenVanillaChest(Player player, int x, int y, int index)
            => HarmonyLib.AccessTools.Method(typeof(Player), "OpenChest").Invoke(player, new object[] { x, y, index });

        private static void Open(int client, Peer peer, string payload, BinaryReader reader)
        {
            long sequence = reader.ReadInt64();
            if (sequence == peer.LastOpenSequence && peer.LastOpenRequest == payload)
            { NetSyncPayload.SendResponseTo(client, true, Prefix + "open", peer.LastOpenReply); return; }
            if (sequence <= peer.LastOpenSequence) return;
            int index = reader.ReadInt32(), x = reader.ReadInt32(), y = reader.ReadInt32(); End(reader);
            bool valid = !Main.player[client].dead && ServerMarkedChests.CanAccess(client, index) && Main.chest[index].x == x && Main.chest[index].y == y;
            string response = Encode(writer =>
            {
                writer.Write(peer.Token); writer.Write(sequence); writer.Write(valid);
                writer.Write(valid ? "箱子已远程打开" : "箱子未标记、锁定、被占用或已变化");
                if (!valid) return;
                Chest chest = Main.chest[index];
                writer.Write(index); writer.Write(x); writer.Write(y); writer.Write(chest.name ?? "");
                foreach (Item item in chest.item) new State(item).Write(writer);
            });
            peer.LastOpenSequence = sequence; peer.LastOpenRequest = payload; peer.LastOpenReply = response;
            if (valid)
            {
                peer.RemoteChest = index; Main.player[client].chest = index;
                NetMessage.SendTileSquare(client, x, y, 4);
            }
            NetSyncPayload.SendResponseTo(client, true, Prefix + "open", response);
        }
        internal static bool RequestToolbar(int action) => Request(action + 3, -1, -1, null);
        internal static bool RequestDeposit(Item item)
        {
            if (ReferenceEquals(item, Main.mouseItem)) return Request(2, -1, 58, item);
            int slot = Array.IndexOf(Main.LocalPlayer.inventory, item);
            return slot >= 0 && slot < 58 && Request(7, -1, slot, item);
        }

        private static bool Request(int action, int chest, int slot, Item expected)
        {
            Player player = Main.LocalPlayer;
            if (Main.netMode != 1 || !Ready || Busy || RemoteStorageSnapshots.RefreshPending || ToolNetwork.Busy || player == null || player.dead || player.itemAnimation > 0 || Terraria.GameContent.CraftingRequests.HasPendingRequests)
            {
                // 限流：只在 30 帧（0.5 秒）内首次被拦截时提示
                ulong now = Main.GameUpdateCount;
                if (now - _lastBusyWarning >= 30)
                {
                    _lastBusyWarning = now;
                    Main.NewText(Busy || RemoteStorageSnapshots.RefreshPending || ToolNetwork.Busy ? "请等待上一次远程存储操作确认。" : "正在等待服务器准备箱子存取，请稍后再试。", 255, 210, 120);
                }
                return false;
            }
            // 标记确认完成后才允许依赖它的物品操作。
            SynchronizeMarksIfIdle();
            if (_markPending != null) { Main.NewText("正在同步箱子标记，请稍后再试。", 255, 210, 120); return false; }
            _before = new State[59];
            for (int i = 0; i < 58; i++) _before[i] = new State(player.inventory[i]);
            _before[58] = new State(Main.mouseItem);
            player.inventory[58] = Main.mouseItem;
            // 普通单格存取使用服务器已经同步的玩家背包快照；鼠标物品只需同步虚拟的
            // 58 号槽。批量工具操作仍同步完整背包，以覆盖玩家刚整理过的槽位。
            if (action == 2 || action == 7)
                NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, action == 2 ? 58 : slot);
            else if (action >= 3 && action <= 6)
                for (int i = 0; i < 59; i++)
                    NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, i);
            _pendingSequence = ++_sequence;
            string search = ExtremeStorageUI.SearchText ?? "";
            if (search.Length > 96) search = search.Substring(0, 96);
            _pending = Encode(writer =>
            {
                writer.Write(_token); writer.Write(_pendingSequence); writer.Write(action); writer.Write(chest); writer.Write(slot);
                new State(expected).Write(writer);
                writer.Write((int)ExtremeStorageUI.CurrentTab); writer.Write((int)ExtremeStorageUI.CurrentSortMode); writer.Write(search);
                foreach (State state in _before) state.Write(writer);
            });
            _lastSend = Main.GameUpdateCount;
            string before = Encode(writer => { foreach (State state in _before) state.Write(writer); });
            if (!StorageTransactionJournal.BeginClient(_world, _token + "." + _pendingSequence, before, _pending))
            {
                _pending = null; _before = null;
                Main.NewText(StorageTransactionJournal.Status, 255, 160, 100); return false;
            }
            Status = "正在等待服务器确认物品操作";
            Mod.LogInfo($"[终极存储] 客户端发送请求 seq={_pendingSequence} action={action} chest={chest} slot={slot}");
            NetSyncPayload.SendRequest(Prefix + "move", _pending);
            return true;
        }

        private static void Recover(int client, Peer peer, BinaryReader reader)
        {
            string id = reader.ReadString(); End(reader);
            if (id.Length > 128) throw new InvalidDataException("恢复事务标识不合法");
            int separator = id.LastIndexOf('.');
            if (separator > 0) ToolNetwork.RevokeServerToken(client, id.Substring(0, separator));
            string previous;
            bool known = StorageTransactionJournal.TryGetServer(peer.Identity, id, out previous);
            NetSyncPayload.SendResponseTo(client, true, Prefix + "recover", Encode(writer =>
            {
                writer.Write(peer.Token); writer.Write(0L); writer.Write(id); writer.Write(known); writer.Write(previous != null);
                writer.Write(previous ?? "");
            }));
        }

        private static void SynchronizeMarksIfIdle() { if (_markPending == null) SynchronizeMarks(); }


        private static void Execute(int client, Peer peer, string payload, BinaryReader reader)
        {
            long sequence = reader.ReadInt64();
            string saved;
            if (peer.Replies.TryGetValue(sequence, out saved))
            {
                Mod.LogInfo($"[终极存储] 服务器重放请求 seq={sequence} client={client}");
                if (peer.Requests[sequence] == payload) NetSyncPayload.SendResponseTo(client, true, Prefix + "move", saved);
                return;
            }
            if (sequence <= peer.LastSequence) return;
            int action = reader.ReadInt32(), chestIndex = reader.ReadInt32(), slot = reader.ReadInt32();
            Mod.LogInfo($"[终极存储] 服务器执行请求 seq={sequence} client={client} action={action} chest={chestIndex} slot={slot}");
            State expected = State.Read(reader);
            int tab = reader.ReadInt32(), sort = reader.ReadInt32();
            string search = reader.ReadString();
            if (action < 0 || action > 7 || tab < 0 || tab > (int)StorageTab.Furniture || sort < 0 || sort > (int)ItemSortMode.Kind || search.Length > 96)
                throw new InvalidDataException("存取参数不合法");
            var before = new State[59];
            for (int i = 0; i < before.Length; i++) before[i] = State.Read(reader);
            End(reader);
            Player player = Main.player[client];
            string message = "操作已由服务器确认";
            bool valid = !player.dead && player.inventory.Length >= 59;
            for (int i = 0; valid && i < 59; i++) valid = before[i].Matches(player.inventory[i]);
            var readable = ServerMarkedChests.GetReadable(client).ToList();
            // 单格取物只会读取一个来源箱子。之前每次点击都克隆所有标记箱，
            // 箱子较多时服务器主线程会在一次响应中分配数万件 Item，造成联机卡顿。
            List<Chest> chests = readable;
            if (!valid) message = "背包状态已变化，未转移物品，请重试";
            else if ((action == 0 || action == 1) &&
                (!ServerMarkedChests.CanAccess(client, chestIndex) || slot < 0 || slot >= Main.chest[chestIndex].item.Length || !expected.Matches(Main.chest[chestIndex].item[slot])))
            { valid = false; message = "箱子已变化、锁定或被占用，未转移物品"; }
            else if ((action == 2 || action == 7) && (slot < 0 || slot > 58 || !expected.Matches(player.inventory[slot])))
            { valid = false; message = "待存入物品已变化，未转移物品"; }
            else if (chests.Count == 0) { valid = false; message = "没有可操作的标记箱子"; }
            if (valid && (action == 0 || action == 1))
            {
                Chest sourceChest = Main.chest[chestIndex];
                chests = new List<Chest> { sourceChest };
            }
            var stagedInventory = CloneSlots(player.inventory);
            var inventory = new Item[50];
            Item[][] boxes = valid ? chests.Select(chest => CloneSlots(chest.item)).ToArray() : new Item[0][];
            var changedChestSlots = new Dictionary<int, List<int>>();
            if (valid)
            {
                // 先在服务器实际库存的副本上计算，全部成功并缓存确认后一次提交。
                for (int i = 0; i < 59; i++) if (!stagedInventory[i].IsAir) stagedInventory[i].favorited = before[i].Favorite;
                Array.Copy(stagedInventory, inventory, 50);
                int moved = 0;
                if (action == 0 || action == 1)
                {
                    Item source = boxes[0][slot];
                    moved = StorageTransfers.Move(source, new[] { inventory }, false, action == 0 ? source.stack : Math.Max(1, source.stack / 2));
                }
                else if (action == 2 || action == 7) moved = StorageTransfers.Move(stagedInventory[slot], boxes);
                else if (action == 3 || action == 4)
                {
                    foreach (Item item in inventory)
                        if (item != null && !item.IsAir && !item.favorited && (action == 4 || ExtremeStorageUI.MatchesStorageTab(item, (StorageTab)tab)))
                            moved += StorageTransfers.Move(item, boxes, action == 4);
                }
                else if (action == 5)
                {
                    StorageTransfers.Merge(boxes);
                    foreach (Item[] box in boxes) ExtremeStorageUI.SortStorageItems(box, (ItemSortMode)sort);
                }
                else if (action == 6)
                {
                    foreach (Item[] box in boxes)
                        foreach (Item item in box)
                            if (item != null && !item.IsAir && ExtremeStorageUI.MatchesStorageTab(item, (StorageTab)tab) && ExtremeStorageUI.MatchesSearch(item.AffixName(), search))
                                moved += StorageTransfers.Move(item, new[] { inventory });
                }
                Array.Copy(inventory, stagedInventory, 50);
                message = action == 5 ? "[终极存储]：标记箱子已整理" : moved > 0 ? $"已转移 {moved} 件物品" : "没有可转移的物品或目标存储空间已满";
                for (int c = 0; c < chests.Count; c++)
                    for (int s = 0; s < chests[c].item.Length; s++)
                        if (!new State(boxes[c][s]).Matches(chests[c].item[s]))
                        {
                            List<int> slots;
                            if (!changedChestSlots.TryGetValue(chests[c].index, out slots))
                                changedChestSlots.Add(chests[c].index, slots = new List<int>());
                            slots.Add(s);
                        }
            }
            string response = Encode(writer =>
            {
                writer.Write(peer.Token); writer.Write(sequence); writer.Write(valid); writer.Write(message);
                for (int i = 0; i < 59; i++) new State(stagedInventory[i]).Write(writer);
            });
            StorageTransactionJournal.RecordServer(peer.Identity, peer.Token + "." + sequence, response);
            peer.LastSequence = sequence;
            peer.Replies[sequence] = response; peer.Requests[sequence] = payload;
            if (valid)
            {
                Array.Copy(stagedInventory, player.inventory, stagedInventory.Length);
                for (int c = 0; c < chests.Count; c++) Array.Copy(boxes[c], chests[c].item, boxes[c].Length);
            }
            if (peer.Replies.Count > 32)
            {
                long oldest = peer.Replies.Keys.Min(); peer.Replies.Remove(oldest); peer.Requests.Remove(oldest);
            }
            NetSyncPayload.SendResponseTo(client, true, Prefix + "move", response);
            if (valid)
                foreach (var changed in changedChestSlots)
                    if (Chest.UsingChest(changed.Key) == client)
                        foreach (int s in changed.Value)
                            NetMessage.SendData(MessageID.SyncChestItem, client, -1, null, changed.Key, s);
        }

        private static Item[] CloneSlots(Item[] slots)
            => slots.Select(item => item == null ? new Item() : item.IsAir ? item : item.Clone()).ToArray();

        private static void HandleResponse(bool success, string operation, string payload)
        {
            if (Main.netMode != 1 || !success) return;
            try
            {
                string complete;
                if (!NetSyncPayload.TryAcceptResponse(operation, payload, out complete)) return;
                using (var reader = Decode(complete))
                {
                    if (operation == Prefix + "hello") { AcceptHello(reader); return; }
                    if (reader.ReadString() != _token) return;
                    long sequence = reader.ReadInt64();
                    if (operation == Prefix + "move")
                        Mod.LogInfo($"[终极存储] 客户端收到响应 seq={sequence} pending={_pendingSequence} busy={Busy}");
                    if (operation == Prefix + "recover")
                    {
                        string id = reader.ReadString(); bool known = reader.ReadBoolean(), executed = reader.ReadBoolean();
                        string previous = reader.ReadString(); End(reader);
                        var record = StorageTransactionJournal.Pending;
                        if (record == null || record.World != _world || record.Id != id) return;
                        if (!known) { Status = "服务器无法确认旧事务结果；保留日志和库存锁，请勿删除记录。"; return; }
                        if (!_recoveredInventory)
                        {
                            using (var old = Decode(record.Before))
                            {
                                _before = new State[59];
                                for (int i = 0; i < 59; i++) _before[i] = State.Read(old);
                                End(old);
                            }
                            for (int i = 0; i < 59; i++) Main.LocalPlayer.inventory[i] = _before[i].Item();
                            Main.mouseItem = Main.LocalPlayer.inventory[58];
                            _recoveredInventory = true;
                        }
                        if (!executed)
                        {
                            if (StorageTransactionJournal.CompleteClient())
                            {
                                _recoveryPending = null; _before = null; _recoveredInventory = false;
                                Status = "旧操作未执行，原库存已恢复并保存";
                                for (int i = 0; i < 59; i++) NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, Main.myPlayer, i);
                            }
                            else Status = StorageTransactionJournal.Status;
                            Main.NewText(Status, 150, 230, 255); return;
                        }
                        using (var old = Decode(previous))
                        {
                            old.ReadString(); _pendingSequence = old.ReadInt64();
                            byte[] body = old.ReadBytes((int)(old.BaseStream.Length - old.BaseStream.Position));
                            _pending = record.Request;
                            string adapted = Encode(writer => { writer.Write(_token); writer.Write(_pendingSequence); writer.Write(body); });
                            HandleResponse(true, Prefix + "move", adapted);
                        }
                        return;
                    }
                    if (operation == Prefix + "closed")
                    {
                        int index = reader.ReadInt32(); End(reader);
                        if (_remoteChest == index) { if (Main.LocalPlayer.chest == index) Main.LocalPlayer.chest = -1; _remoteChest = -1; }
                        return;
                    }
                    if (operation == Prefix + "open")
                    {
                        if (_openPending == null || sequence != _openSequence) return;
                        bool validOpen = reader.ReadBoolean(); string text = reader.ReadString();
                        if (validOpen)
                        {
                            int index = reader.ReadInt32(), x = reader.ReadInt32(), y = reader.ReadInt32();
                            string name = reader.ReadString();
                            if (index < 0 || index >= Main.maxChests || !ServerMarkedChests.ValidCoordinate(((long)x << 32) | (uint)y)) throw new InvalidDataException("远程箱子坐标不合法");
                            Chest chest = Chest.CreateOutOfArray(index, x, y, 40); chest.name = name;
                            for (int i = 0; i < 40; i++) chest.item[i] = State.Read(reader).Item();
                            End(reader);
                            Main.chest[index] = chest; _remoteChest = index;
                            ExtremeStorageUI.CloseAfterRemoteOpen(); OpenVanillaChest(Main.LocalPlayer, x, y, index);
                        }
                        else End(reader);
                        _openPending = null; Status = text; Main.NewText(text, 150, 230, 255); return;
                    }
                    if (operation == Prefix + "marks")
                    {
                        if (_markPending == null) return;
                        using (var pending = Decode(_markPending))
                        { pending.ReadString(); if (pending.ReadInt64() != sequence) return; }
                        int count = reader.ReadInt32();
                        if (count < 0 || count > 128) throw new InvalidDataException("拒绝标记数量不合法");
                        var rejected = new HashSet<long>();
                        for (int i = 0; i < count; i++) rejected.Add(reader.ReadInt64());
                        End(reader);
                        foreach (var change in _markChanges)
                        {
                            if (rejected.Contains(change.Key)) { ExtremeStorageUI.RestoreRejectedMark(change.Key, SentMarks.Contains(change.Key)); continue; }
                            if (change.Value) SentMarks.Add(change.Key); else SentMarks.Remove(change.Key);
                        }
                        _markPending = null;
                        RemoteStorageSnapshots.Invalidate();
                        Status = rejected.Count > 0 ? "部分箱子标记不可用，已恢复服务器状态" : "全世界标记箱子已确认";
                        return;
                    }
                    if (operation != Prefix + "move" || !Busy || sequence != _pendingSequence) return;
                    bool valid = reader.ReadBoolean();
                    string message = reader.ReadString();
                    var after = new State[59];
                    for (int i = 0; i < 59; i++) after[i] = State.Read(reader);
                    End(reader);
                    Player player = Main.LocalPlayer;
                    bool unchanged = player != null;
                    for (int i = 0; unchanged && i < 59; i++)
                    {
                        Item current = i == 58 ? Main.mouseItem : player.inventory[i];
                        if (_before[i].Matches(after[i].Item())) continue; // 无关槽位的外部变化不能被覆盖。
                        unchanged = _before[i].Matches(current) || after[i].Matches(current);
                    }
                    // 库存输入在等待期间冻结。失败时绝不用服务器的过期库存覆盖当前库存。
                    if (valid && unchanged)
                    {
                        var changedSlots = new List<int>();
                        for (int i = 0; i < 59; i++)
                            if (!_before[i].Matches(after[i].Item()))
                            {
                                player.inventory[i] = after[i].Item();
                                changedSlots.Add(i);
                            }
                        if (_before[58].Matches(after[58].Item())) player.inventory[58] = Main.mouseItem;
                        else Main.mouseItem = player.inventory[58];
                        foreach (int slot in changedSlots)
                            NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, slot);
                    }
                    else if (valid)
                    {
                        Status = "待确认槽位被其他功能改动，保留库存锁定以避免重复或丢失物品";
                        Main.NewText(Status, 255, 160, 100);
                        return;
                    }
                    if (!StorageTransactionJournal.CompleteClient(player.inventory, Main.mouseItem))
                    {
                        if (_recoveredInventory)
                        {
                            _pending = null; // 新连接只查询恢复结果，不能用旧连接token重发修改请求。
                        }
                        Status = StorageTransactionJournal.Status; Main.NewText(Status, 255, 160, 100); return;
                    }
                    _pending = null; _before = null; _recoveryPending = null; _recoveredInventory = false;
                    Status = message;

                    // 合并短时间内的多个"已转移"消息，避免 Shift+扫过背包时重复提示。
                    if (message.StartsWith("已转移 ") && message.Contains("件物品"))
                    {
                        int transferred;
                        int start = message.IndexOf(' ') + 1;
                        int end = message.IndexOf(' ', start);
                        if (end > start && int.TryParse(message.Substring(start, end - start), out transferred))
                        {
                            ulong now = Main.GameUpdateCount;
                            if (now - _lastTransferMessage <= 6) // 100ms 内合并（原 12 帧=200ms，改为 6 帧=100ms 更快显示）
                            {
                                _pendingTransferCount += transferred;
                            }
                            else
                            {
                                if (_pendingTransferCount > 0)
                                    Main.NewText($"已转移 {_pendingTransferCount} 件物品", 150, 230, 255);
                                _pendingTransferCount = transferred;
                            }
                            _lastTransferMessage = now;
                        }
                        else Main.NewText(message, 150, 230, 255);
                    }
                    else Main.NewText(message, 150, 230, 255);
                    RemoteStorageSnapshots.RefreshAfterMutation();
                    // 远程存取只改变背包和箱子数据；Recipe.UpdateRecipeList 会遍历全部配方，
                    // 在多人响应主线程上会造成明显停顿。原版打开制作界面时会重新计算。
                    ExtremeStorageUI.RefreshItems();
                }
            }
            catch (Exception error) { Mod.LogError("读取远程存取确认失败；保留等待状态", error); }
        }

        private static void AcceptHello(BinaryReader reader)
        {
            if (reader.ReadString() != _hello) return;
            string token = reader.ReadString(), world = reader.ReadString(), owner = reader.ReadString();
            int part = reader.ReadInt32(), parts = reader.ReadInt32(), count = reader.ReadInt32();
            Guid value;
            if (!Guid.TryParseExact(token, "N", out value) || !Guid.TryParseExact(world, "N", out value) || value == Guid.Empty ||
                owner.Length != 64 || !owner.All(Uri.IsHexDigit) || parts < 1 || parts > (Main.maxChests + 127) / 128 || part < 0 || part >= parts || count < 0 || count > 128)
                throw new InvalidDataException("服务器标记握手不合法");
            var keys = new List<long>();
            for (int i = 0; i < count; i++)
            {
                long key = reader.ReadInt64();
                if (!ServerMarkedChests.ValidCoordinate(key)) throw new InvalidDataException("标记坐标不合法");
                keys.Add(key);
            }
            End(reader);
            if (Ready) return;
            if (_receivingToken != null && (_receivingToken != token || _receivingWorld != world || _receivingOwner != owner || _receivingParts != parts))
            { RestartHello(); return; }
            _receivingToken = token; _receivingWorld = world; _receivingOwner = owner; _receivingParts = parts;
            long[] previous;
            if (PartKeys.TryGetValue(part, out previous) && !previous.SequenceEqual(keys)) { RestartHello(); return; }
            PartKeys[part] = keys.ToArray();
            if (ReceivedMarks.Count + keys.Except(ReceivedMarks).Count() > Main.maxChests) { RestartHello(); return; }
            _world = world;
            ReceivedMarks.UnionWith(keys); ReceivedParts.Add(part);
            if (ReceivedParts.Count != parts) return;
            SentMarks.UnionWith(ReceivedMarks);
            _token = token;
            ExtremeStorageUI.AcceptServerMarks(world, owner, ReceivedMarks);
            Status = "全世界标记箱子已就绪";
            RemoteStorageSnapshots.Invalidate();
        }

        private static void RestartHello()
        {
            ReceivedMarks.Clear(); ReceivedParts.Clear(); PartKeys.Clear();
            _receivingToken = _receivingWorld = _receivingOwner = null; _receivingParts = 0;
            _hello = Guid.NewGuid().ToString("N"); _lastHello = 0;
            Status = "标记握手批次不一致，正在重新确认";
        }

        internal static bool IsMarked(int player, Chest chest)
            => Main.netMode == 2 ? ServerMarkedChests.IsMarked(player, chest) : ExtremeStorageUI.IsChestMarked(chest);

        internal static IEnumerable<Chest> GetMarkedReadableChests(Player player)
            => Main.netMode == 2 ? ServerMarkedChests.GetReadable(player.whoAmI) : ExtremeStorageUI.GetMarkedReadableChests();

        internal static void ResetSession()
        {
            if (_initialized) { NetSync.OnStorageRequest -= _receiveRequest; NetSync.OnStorageResponse -= _receiveResponse; }
            _initialized = false; _lifecycle++;
            Peers.Clear(); ReceivedMarks.Clear(); ReceivedParts.Clear(); PartKeys.Clear(); SentMarks.Clear(); ServerMarkedChests.ResetSession();
            _hello = _characterId = _token = _world = _pending = _markPending = _openPending = null;
            _receivingToken = _receivingWorld = _receivingOwner = null; _receivingParts = 0;
            _before = null; _sequence = _pendingSequence = _openSequence = 0; _lastHello = _lastSend = _lastMarks = _lastOpen = 0; _remoteChest = -1;
            _recoveryPending = null; _lastRecovery = 0; _recoveredInventory = false;
            Status = StorageTransactionJournal.ClientLocked ? StorageTransactionJournal.Status : "等待服务器确认标记箱子";
        }
        internal static void Unload() => ResetSession();
    }
}
