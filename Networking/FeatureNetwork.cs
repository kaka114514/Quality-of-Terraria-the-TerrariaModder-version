using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using QualityOfTerraria.Features;
using Terraria;
using TerrariaModder.Core.Net;
using TerrariaModder.Core.Permissions;

namespace QualityOfTerraria.Networking
{
    internal static class FeatureNetwork
    {
        private const string Operation = "quality-of-terraria.features.v110";
        private sealed class Peer
        {
            internal Player Player;
            internal NPC Dummy;
            internal string Guid, Nonce, Request, Response;
            internal long Sequence;
            internal bool Success, PendingScan;
        }
        private sealed class ScanRequest
        {
            internal int Client;
            internal Peer Peer;
            internal string Nonce, Token;
            internal long Sequence;
            internal StructureLocator.StructureType Type;
        }
        private static readonly Dictionary<int, Peer> Peers = new Dictionary<int, Peer>();
        private static readonly Queue<ScanRequest> Scans = new Queue<ScanRequest>();
        private static bool _initialized;
        private static int _generation;
        private static string _nonce, _pending, _action;
        private static long _sequence;
        private static ulong _sent;
        private static Action _afterHello;
        internal static bool Busy => _pending != null;

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
            Scans.Clear();
            _nonce = _pending = _action = null;
            _sequence = 0;
            _afterHello = null;
            StructureLocator.ResetSession();
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

        internal static void Update()
        {
            if (Main.netMode == 2)
            {
                foreach (int client in new List<int>(Peers.Keys))
                    if (!SamePeer(client, Peers[client]))
                    {
                        TargetDummySystem.RemoveDummy(Peers[client].Dummy);
                        Peers.Remove(client);
                    }
                // 所有玩家共享一个有帧预算的扫描器；排队时不分配世界副本或线程。
                if (!StructureLocator.IsScanning && Scans.Count > 0)
                {
                    ScanRequest job = Scans.Dequeue();
                    if (CurrentJob(job))
                        StructureLocator.StartScan(job.Type, job.Peer.Player.Center, target => CompleteScan(job, target));
                }
            }
            ulong timeout = _action == "locate" ? 36000UL : 600UL;
            if (Main.netMode != 1 || !Busy || Main.GameUpdateCount - _sent < timeout) return;
            _pending = _nonce = null;
            _afterHello = null;
            StructureLocator.ResetSession();
            Main.NewText("功能请求未收到服务器确认，请重试", 255, 120, 120);
        }

        internal static void RequestDummy(Vector2 position)
        {
            Request("dummy", (int)position.X, (int)position.Y);
        }

        internal static bool RequestLocator(StructureLocator.StructureType type)
        {
            if (!Request("locate", (int)type, 0)) return false;
            StructureLocator.AwaitServer();
            Main.NewText("【结构定位】正在等待服务器扫描结果…", 100, 240, 255);
            return true;
        }

        private static bool Request(string action, int x, int y)
        {
            if (Main.netMode != 1 || !Mod.WorldActive || Busy) return false;
            if (!NetSync.LocalPlayerHasModAccess("quality-of-terraria"))
            {
                Main.NewText("服务器未授予本模组功能权限", 255, 120, 120);
                return false;
            }
            if (_nonce == null)
            {
                string requestedAction = action;
                _afterHello = () => Request(requestedAction, x, y);
                action = "hello";
            }
            _pending = Guid.NewGuid().ToString("N");
            _action = action;
            _sent = Main.GameUpdateCount;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(_pending); writer.Write(_nonce ?? ""); writer.Write(++_sequence);
                writer.Write(action); writer.Write(x); writer.Write(y);
                NetSync.SendStorageRequest(Operation, Convert.ToBase64String(stream.ToArray()));
            }
            return true;
        }

        private static bool SamePeer(int client, Peer peer)
        {
            return client >= 0 && client < Main.maxPlayers && Main.player[client] != null && Main.player[client].active &&
                ReferenceEquals(peer.Player, Main.player[client]) && peer.Guid == PermissionService.GetGuid(client);
        }

        private static bool CurrentJob(ScanRequest job)
        {
            Peer peer;
            return Peers.TryGetValue(job.Client, out peer) && ReferenceEquals(peer, job.Peer) && SamePeer(job.Client, peer) &&
                peer.Nonce == job.Nonce && peer.Sequence == job.Sequence;
        }

        private static void ReceiveRequest(int client, string operation, string payload)
        {
            if (!_initialized || operation != Operation) return;
            int generation = _generation;
            NetworkRuntime.Enqueue(() => { if (_initialized && generation == _generation) HandleRequest(client, payload); });
        }

        private static void HandleRequest(int client, string payload)
        {
            if (Main.netMode != 2 || !Mod.WorldActive || client < 0 || client >= Main.maxPlayers) return;
            Player player = Main.player[client];
            if (player == null || !player.active || player.dead) return;
            string token = "";
            Peer peer = null;
            bool remember = false;
            try
            {
                if (payload == null || payload.Length > 512) throw new InvalidDataException("功能请求无效");
                using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(payload))))
                {
                    token = reader.ReadString(); string nonce = reader.ReadString(); long sequence = reader.ReadInt64();
                    string action = reader.ReadString(); int x = reader.ReadInt32(), y = reader.ReadInt32();
                    if (token.Length != 32 || nonce.Length > 32 || sequence <= 0 || reader.BaseStream.Position != reader.BaseStream.Length)
                        throw new InvalidDataException("功能请求无效");
                    if (!PermissionService.HasModAccess(client, "quality-of-terraria") || string.IsNullOrEmpty(PermissionService.GetGuid(client)))
                        throw new InvalidDataException("服务器未确认玩家身份或功能权限");
                    if (Peers.TryGetValue(client, out peer) && !SamePeer(client, peer))
                    {
                        TargetDummySystem.RemoveDummy(peer.Dummy);
                        Peers.Remove(client); peer = null;
                    }
                    if (peer != null && peer.Request == payload)
                    {
                        if (peer.Response != null) NetSync.SendStorageResponseTo(client, peer.Success, Operation, peer.Response);
                        return;
                    }
                    if (action == "hello")
                    {
                        if (peer == null) peer = new Peer { Player = player, Guid = PermissionService.GetGuid(client) };
                        peer.Nonce = Guid.NewGuid().ToString("N"); peer.Sequence = 0; peer.PendingScan = false;
                        Peers[client] = peer;
                        peer.Request = payload; remember = true;
                        Reply(client, peer, token, true, "hello", -1, -1, true);
                        return;
                    }
                    if (peer != null && peer.PendingScan) throw new InvalidDataException("此玩家已有结构扫描排队，请等待完成");
                    if (peer == null || nonce != peer.Nonce || sequence <= peer.Sequence) throw new InvalidDataException("功能会话已过期，请重试");
                    peer.Sequence = sequence; peer.Request = payload; peer.Response = null; remember = true;
                    if (action == "dummy")
                    {
                        if (x < 16 || y < 16 || x >= (Main.maxTilesX - 1) * 16 || y >= (Main.maxTilesY - 1) * 16)
                            throw new InvalidDataException("假人坐标超出世界");
                        NPC existing = peer.Dummy;
                        bool removed = existing != null && existing.active && existing.type == Terraria.ID.NPCID.TargetDummy && existing.ai[0] == -1f;
                        int index = TargetDummySystem.ToggleForPlayer(player, new Vector2(x, y), existing);
                        if (index < 0 && !removed) throw new InvalidDataException("没有可用NPC槽位");
                        peer.Dummy = index >= 0 ? Main.npc[index] : null;
                        Reply(client, peer, token, true, "dummy", index, -1, true);
                    }
                    else if (action == "locate")
                    {
                        if (x < 0 || x > (int)StructureLocator.StructureType.GraniteCave) throw new InvalidDataException("结构类型无效");
                        if (Scans.Count >= Main.maxPlayers) throw new InvalidDataException("结构扫描队列已满，请稍后重试");
                        peer.PendingScan = true;
                        Scans.Enqueue(new ScanRequest { Client = client, Peer = peer, Nonce = nonce, Token = token, Sequence = sequence, Type = (StructureLocator.StructureType)x });
                    }
                    else throw new InvalidDataException("未知功能请求");
                }
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is FormatException || exception is ArgumentException)
            {
                Reply(client, peer, token, false, exception.Message, -1, -1, remember);
            }
        }

        private static void CompleteScan(ScanRequest job, Point? target)
        {
            if (!CurrentJob(job)) return;
            job.Peer.PendingScan = false;
            if (target.HasValue)
            {
                Point position = target.Value;
                // 先同步目标周围区块，再发送结果，客户端小地图拥有真实瓦片。
                int minX = Netplay.GetSectionX(Math.Max(0, position.X - 25)), maxX = Netplay.GetSectionX(Math.Min(Main.maxTilesX - 1, position.X + 25));
                int minY = Netplay.GetSectionY(Math.Max(0, position.Y - 25)), maxY = Netplay.GetSectionY(Math.Min(Main.maxTilesY - 1, position.Y + 25));
                for (int x = minX; x <= maxX; x++)
                    for (int y = minY; y <= maxY; y++) NetMessage.SendSection(job.Client, x, y);
            }
            Reply(job.Client, job.Peer, job.Token, true, "locate:" + (int)job.Type, target.HasValue ? target.Value.X : -1, target.HasValue ? target.Value.Y : -1, true);
        }

        private static void Reply(int client, Peer peer, string token, bool success, string action, int x, int y, bool remember)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(token); writer.Write(peer == null ? "" : peer.Nonce); writer.Write(action); writer.Write(x); writer.Write(y);
                string payload = Convert.ToBase64String(stream.ToArray());
                if (remember && peer != null) { peer.Response = payload; peer.Success = success; }
                NetSync.SendStorageResponseTo(client, success, Operation, payload);
            }
        }

        private static void ReceiveResponse(bool success, string operation, string payload)
        {
            if (!_initialized || operation != Operation) return;
            int generation = _generation;
            NetworkRuntime.Enqueue(() => { if (_initialized && generation == _generation) HandleResponse(success, payload); });
        }

        private static void HandleResponse(bool success, string payload)
        {
            if (Main.netMode != 1 || !Mod.WorldActive || !Busy) return;
            try
            {
                if (payload == null || payload.Length > 1024) return;
                using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(payload))))
                {
                    string token = reader.ReadString(), nonce = reader.ReadString(), action = reader.ReadString();
                    int x = reader.ReadInt32(), y = reader.ReadInt32();
                    if (token != _pending || reader.BaseStream.Position != reader.BaseStream.Length) return;
                    if (success && _action != "hello" && nonce != _nonce) return;
                    _pending = null;
                    if (!success)
                    {
                        _nonce = null; _afterHello = null;
                        StructureLocator.ResetSession();
                        Main.NewText(action, 255, 120, 120);
                    }
                    else if (_action == "hello" && action == "hello" && nonce.Length == 32)
                    {
                        _nonce = nonce;
                        Action next = _afterHello; _afterHello = null;
                        next?.Invoke();
                    }
                    else if (_action == "dummy" && action == "dummy" && x >= -1 && x < Main.maxNPCs) TargetDummySystem.BindDummy(x);
                    else if (_action == "locate" && action.StartsWith("locate:", StringComparison.Ordinal))
                    {
                        int type;
                        if (!int.TryParse(action.Substring(7), out type) || type < 0 || type > 9) return;
                        if (x >= Main.maxTilesX || y >= Main.maxTilesY || x < -1 || y < -1) return;
                        StructureLocator.ReceiveServerResult((StructureLocator.StructureType)type, x >= 0 && y >= 0 ? new Point?(new Point(x, y)) : null);
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is FormatException || exception is ArgumentException)
            { Mod.LogError("功能网络应答无效", exception); }
        }
    }
}
