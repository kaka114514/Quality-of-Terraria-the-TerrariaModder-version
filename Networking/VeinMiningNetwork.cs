using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using TerrariaModder.Core.Net;
using TerrariaModder.Core.Permissions;
using QualityOfTerraria.Features;

namespace QualityOfTerraria.Networking
{
    internal static class VeinMiningNetwork
    {
        private const string Operation = "quality-of-terraria.veins.v120";
        private sealed class Peer
        {
            internal Player Player;
            internal string Guid, Nonce, Hello, HelloResponse, Request, Response;
            internal long Sequence;
            internal bool Result;
            internal RequestData Waiting;
        }
        private sealed class RequestData
        {
            internal string Id, Payload;
            internal int X, Y;
            internal ushort Type;
            internal bool Forced;
            internal ulong Frame;
        }
        private sealed class Proof
        {
            internal Player Player;
            internal string Guid;
            internal int X, Y, ToolType, Pick;
            internal byte Prefix;
            internal ushort Type;
            internal ulong Frame;
        }
        private static readonly Dictionary<int, Peer> Peers = new Dictionary<int, Peer>();
        private static readonly List<Proof> Proofs = new List<Proof>();
        private static readonly Queue<RequestData> ClientQueue = new Queue<RequestData>();
        private static bool _initialized;
        private static int _generation;
        private static string _nonce, _pendingId, _pendingPayload;
        private static long _sequence;
        private static ulong _sent, _started;

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
            Peers.Clear(); Proofs.Clear(); ClientQueue.Clear();
            _nonce = _pendingId = _pendingPayload = null;
            _sequence = 0; _sent = _started = 0;
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

        internal static void Request(int x, int y, ushort type, bool forced)
        {
            if (!_initialized || Main.netMode != 1 || !Mod.WorldActive || Mod.VeinMinerLegacyLoaded || ClientQueue.Count >= 4) return;
            if (!NetSync.LocalPlayerHasModAccess("quality-of-terraria")) return;
            ClientQueue.Enqueue(new RequestData { X = x, Y = y, Type = type, Forced = forced, Frame = Main.GameUpdateCount });
            SendNext();
        }

        private static void SendNext()
        {
            if (_pendingPayload != null || Main.netMode != 1 || !Mod.WorldActive || Mod.VeinMinerLegacyLoaded) return;
            if (!NetSync.LocalPlayerHasModAccess("quality-of-terraria")) return;
            while (ClientQueue.Count > 0 && Main.GameUpdateCount - ClientQueue.Peek().Frame > 180) ClientQueue.Dequeue();
            if (_nonce != null && ClientQueue.Count == 0) return;
            _pendingId = Guid.NewGuid().ToString("N");
            bool hello = _nonce == null;
            RequestData request = hello ? null : ClientQueue.Dequeue();
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(_pendingId); writer.Write(_nonce ?? ""); writer.Write(hello ? 0L : ++_sequence); writer.Write(hello);
                if (!hello) { writer.Write(request.X); writer.Write(request.Y); writer.Write(request.Type); writer.Write(request.Forced); }
                _pendingPayload = Convert.ToBase64String(stream.ToArray());
            }
            _started = _sent = Main.GameUpdateCount;
            NetSync.SendStorageRequest(Operation, _pendingPayload);
        }

        internal static void Update()
        {
            if (Main.netMode == 2)
            {
                Proofs.RemoveAll(p => Main.GameUpdateCount - p.Frame > 180 || !p.Player.active || !ReferenceEquals(Main.player[p.Player.whoAmI], p.Player) || p.Guid != PermissionService.GetGuid(p.Player.whoAmI));
                foreach (int slot in new List<int>(Peers.Keys))
                {
                    Peer peer = Peers[slot];
                    if (!SamePeer(slot, peer)) { Peers.Remove(slot); continue; }
                    if (peer.Waiting != null) TryStart(slot, peer);
                }
            }
            if (Main.netMode != 1 || !Mod.WorldActive || Mod.VeinMinerLegacyLoaded) return;
            if (_pendingPayload != null && Main.GameUpdateCount - _sent >= 120)
            {
                if (Main.GameUpdateCount - _started >= 600)
                {
                    _nonce = _pendingId = _pendingPayload = null;
                    ClientQueue.Clear();
                    return;
                }
                _sent = Main.GameUpdateCount;
                NetSync.SendStorageRequest(Operation, _pendingPayload);
            }
            SendNext();
        }

        internal static void RecordBreak(Player player, int x, int y, ushort type)
        {
            if (!_initialized || Main.netMode != 2 || !Mod.WorldActive || Mod.VeinMinerLegacyLoaded) return;
            string guid = PermissionService.GetGuid(player.whoAmI);
            if (string.IsNullOrEmpty(guid)) return;
            // 每人仅保留少量原版成功记录；来源记录不能由自定义请求生成。
            Proofs.RemoveAll(p => Main.GameUpdateCount - p.Frame > 180);
            int count = 0;
            foreach (Proof proof in Proofs) if (ReferenceEquals(proof.Player, player)) count++;
            if (count >= 8) return;
            Proofs.Add(new Proof { Player = player, Guid = guid, X = x, Y = y, Type = type, Frame = Main.GameUpdateCount,
                ToolType = player.HeldItem.type, Prefix = player.HeldItem.prefix, Pick = player.HeldItem.pick });
        }

        private static bool SamePeer(int slot, Peer peer) => slot >= 0 && slot < Main.maxPlayers && Main.player[slot] != null && Main.player[slot].active && ReferenceEquals(peer.Player, Main.player[slot]) && peer.Guid == PermissionService.GetGuid(slot);

        private static void ReceiveRequest(int slot, string operation, string payload)
        {
            if (!_initialized || operation != Operation) return;
            int generation = _generation;
            NetworkRuntime.Enqueue(() => { if (_initialized && generation == _generation) HandleRequest(slot, payload); });
        }

        private static void HandleRequest(int slot, string payload)
        {
            if (Main.netMode != 2 || !Mod.WorldActive || Mod.VeinMinerLegacyLoaded || slot < 0 || slot >= Main.maxPlayers) return;
            Player player = Main.player[slot];
            if (player == null || !player.active) return;
            Peer peer;
            Peers.TryGetValue(slot, out peer);
            if (peer != null && !SamePeer(slot, peer)) { Peers.Remove(slot); peer = null; }
            string id = "";
            long sequence = 0;
            try
            {
                if (payload == null || payload.Length > 256) throw new InvalidDataException();
                using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(payload))))
                {
                    id = reader.ReadString(); string nonce = reader.ReadString(); sequence = reader.ReadInt64(); bool hello = reader.ReadBoolean();
                    if (id.Length != 32 || nonce.Length > 32 || sequence < 0) throw new InvalidDataException();
                    if (!PermissionService.HasModAccess(slot, "quality-of-terraria") || string.IsNullOrEmpty(PermissionService.GetGuid(slot))) throw new InvalidDataException("服务器未授予连锁挖矿权限");
                    if (hello)
                    {
                        if (nonce.Length != 0 || sequence != 0 || reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException();
                        if (peer != null && peer.Hello == payload) { NetSync.SendStorageResponseTo(slot, true, Operation, peer.HelloResponse); return; }
                        // 同一连接不因重发hello重置已执行序号。
                        if (peer == null) Peers[slot] = peer = new Peer { Player = player, Guid = PermissionService.GetGuid(slot), Nonce = Guid.NewGuid().ToString("N") };
                        peer.Hello = payload;
                        peer.HelloResponse = Response(id, peer.Nonce, 0, "");
                        NetSync.SendStorageResponseTo(slot, true, Operation, peer.HelloResponse);
                        return;
                    }
                    var request = new RequestData { Id = id, Payload = payload, X = reader.ReadInt32(), Y = reader.ReadInt32(), Type = reader.ReadUInt16(), Forced = reader.ReadBoolean(), Frame = Main.GameUpdateCount };
                    if (reader.BaseStream.Position != reader.BaseStream.Length || sequence == 0) throw new InvalidDataException();
                    if (peer == null || nonce != peer.Nonce) throw new InvalidDataException("连锁挖矿会话已过期");
                    if (sequence == peer.Sequence && peer.Request == payload)
                    {
                        if (peer.Response != null) NetSync.SendStorageResponseTo(slot, peer.Result, Operation, peer.Response);
                        return;
                    }
                    if (sequence <= peer.Sequence || peer.Waiting != null) throw new InvalidDataException("连锁请求重复或已有待确认请求");
                    peer.Sequence = sequence; peer.Request = payload; peer.Response = null; peer.Waiting = request;
                    TryStart(slot, peer);
                }
            }
            catch (Exception error)
            {
                if (id.Length == 32) NetSync.SendStorageResponseTo(slot, false, Operation, Response(id, peer == null ? "" : peer.Nonce, sequence, error is InvalidDataException && error.Message != "Invalid data." ? error.Message : "连锁请求无效"));
            }
        }

        private static void TryStart(int slot, Peer peer)
        {
            RequestData request = peer.Waiting;
            var config = SettingsSync.GetPlayerConfig(slot);
            if (!PermissionService.HasModAccess(slot, "quality-of-terraria") || peer.Player.dead || config == null || (!config.VeinMiningEnabled && !request.Forced) || peer.Player.HeldItem.pick <= 0 || !VeinMiningSystem.IsOre(request.Type))
            { Finish(slot, peer, false, "连锁挖矿设置、权限或手持镐无效"); return; }
            Proof proof = Proofs.Find(p => ReferenceEquals(p.Player, peer.Player) && p.Guid == peer.Guid && p.X == request.X && p.Y == request.Y && p.Type == request.Type && Main.GameUpdateCount - p.Frame <= 180);
            if (proof == null)
            {
                if (Main.GameUpdateCount - request.Frame >= 15) Finish(slot, peer, false, "未确认原版成功挖矿来源");
                return;
            }
            Proofs.Remove(proof);
            if (proof.ToolType != peer.Player.HeldItem.type || proof.Prefix != peer.Player.HeldItem.prefix || proof.Pick != peer.Player.HeldItem.pick)
            { Finish(slot, peer, false, "原版挖矿后工具已改变"); return; }
            bool started = VeinMiningSystem.Start(peer.Player, request.X, request.Y, proof.Type, config);
            Finish(slot, peer, started, started ? "" : "当前矿脉正在处理中或已失效");
        }

        private static void Finish(int slot, Peer peer, bool ok, string message)
        {
            peer.Result = ok;
            peer.Response = Response(peer.Waiting.Id, peer.Nonce, peer.Sequence, message);
            peer.Waiting = null;
            NetSync.SendStorageResponseTo(slot, ok, Operation, peer.Response);
        }

        private static string Response(string id, string nonce, long sequence, string message)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            { writer.Write(id); writer.Write(nonce); writer.Write(sequence); writer.Write(message); return Convert.ToBase64String(stream.ToArray()); }
        }

        private static void ReceiveResponse(bool ok, string operation, string payload)
        {
            if (!_initialized || operation != Operation) return;
            int generation = _generation;
            NetworkRuntime.Enqueue(() => { if (_initialized && generation == _generation) HandleResponse(ok, payload); });
        }

        private static void HandleResponse(bool ok, string payload)
        {
            if (Main.netMode != 1 || _pendingId == null || payload == null || payload.Length > 1024) return;
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(payload))))
                {
                    string id = reader.ReadString(), nonce = reader.ReadString(); long sequence = reader.ReadInt64(); string message = reader.ReadString();
                    if (id != _pendingId || reader.BaseStream.Position != reader.BaseStream.Length) return;
                    if (_nonce == null) { if (!ok || nonce.Length != 32 || sequence != 0) return; _nonce = nonce; }
                    else if (nonce != _nonce || sequence != _sequence) return;
                    _pendingId = _pendingPayload = null;
                    if (!ok && Mod.Config.VeinMiningShowMessages && !string.IsNullOrEmpty(message)) Main.NewText(message, 255, 180, 100);
                    SendNext();
                }
            }
            catch (Exception) { }
        }
    }
}
