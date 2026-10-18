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
            internal bool JobStarted; // 连锁挖矿任务已启动，等待完成
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
        internal static int RecordBreakCalls, RecordBreakAccepted;
        internal static string LastRecordBreakReject;

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
            RecordBreakCalls = RecordBreakAccepted = 0; LastRecordBreakReject = null;
            Patches.VeinMiningOriginTilePatch.PacketKills = Patches.VeinMiningOriginTilePatch.PacketOreKills = 0;
            Patches.VeinMiningOriginTilePatch.PostfixCalls = Patches.VeinMiningOriginTilePatch.PostfixStillActive = Patches.VeinMiningOriginTilePatch.PostfixRecorded = 0;
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
            if (!_initialized || Main.netMode != 1 || !Mod.WorldActive || Mod.VeinMinerLegacyLoaded) return;
            if (!NetSync.LocalPlayerHasModAccess("quality-of-terraria")) return;
            // 如果有请求正在处理或队列已满，静默忽略新请求（避免服务器拒绝提示）
            if (_pendingPayload != null || ClientQueue.Count >= 4)
            {
                Mod.LogInfo($"[连锁挖矿客户端] 忽略新请求: _pendingPayload={(_pendingPayload != null ? "有" : "无")}, 队列={ClientQueue.Count}");
                return;
            }
            ClientQueue.Enqueue(new RequestData { X = x, Y = y, Type = type, Forced = forced, Frame = Main.GameUpdateCount });
            Mod.LogInfo($"[连锁挖矿客户端] 请求已加入队列: 队列={ClientQueue.Count}, 坐标=({x},{y})");
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
            Mod.LogInfo($"[连锁挖矿客户端] SendNext: hello={hello}, 队列={ClientQueue.Count}, 序号={_sequence + 1}");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(_pendingId); writer.Write(_nonce ?? ""); writer.Write(hello ? 0L : ++_sequence); writer.Write(hello);
                if (!hello) { writer.Write(request.X); writer.Write(request.Y); writer.Write(request.Type); writer.Write(request.Forced); }
                _pendingPayload = Convert.ToBase64String(stream.ToArray());
            }
            _started = _sent = Main.GameUpdateCount;
            NetSyncPayload.SendRequest(Operation, _pendingPayload);
        }

        internal static void Update()
        {
            if (Main.netMode == 2)
            {
                // 来源记录绑定 Player 实例，不在这里依据连接身份或 active 状态提前删除。
                // LAN 下 IdentityAnnounce 和玩家槽位状态可能比原版破块包晚一帧到达，
                // 提前清理会让已登记的来源在连锁请求到达前消失。
                Proofs.RemoveAll(p => Main.GameUpdateCount - p.Frame > 180);
                foreach (int slot in new List<int>(Peers.Keys))
                {
                    Peer peer = Peers[slot];
                    if (!SamePeer(slot, peer)) { Peers.Remove(slot); continue; }

                    // 检查等待完成的连锁挖矿任务
                    if (peer.JobStarted)
                    {
                        var completed = VeinMiningSystem.TakeCompletedJob(slot);
                        if (completed != null)
                        {
                            Mod.LogInfo($"[连锁挖矿服务器] 任务完成: slot={slot}, count={completed.Count}, ore={completed.OreName}");
                            // 任务完成，发送响应
                            string message = completed.Count > 1 ? $"{completed.Count}|{completed.OreName}" : "";
                            peer.Response = Response(peer.Waiting.Id, peer.Nonce, peer.Sequence, message);
                            peer.Result = true;
                            peer.Waiting = null;
                            peer.JobStarted = false;
                            NetSyncPayload.SendResponseTo(slot, true, Operation, peer.Response);
                        }
                        continue; // 跳过 TryStart，等任务完成
                    }

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
                NetSyncPayload.SendRequest(Operation, _pendingPayload);
            }
            SendNext();
        }

        internal static void RecordBreak(Player player, int x, int y, ushort type)
        {
            RecordBreakCalls++;
            if (Main.netMode != 2) { LastRecordBreakReject = "netMode"; return; }
            if (!Mod.WorldActive) { LastRecordBreakReject = "world"; return; }
            if (Mod.VeinMinerLegacyLoaded) { LastRecordBreakReject = "legacy"; return; }
            if (player == null) { LastRecordBreakReject = "player"; return; }
            // 原版破矿包可能早于 IdentityAnnounce 到达；来源先绑定当前 Player 实例，
            // 权限、身份和镐力在后续连锁请求处理时再次核验。
            string guid = PermissionService.GetGuid(player.whoAmI) ?? "";
            // 每人仅保留少量原版成功记录；来源记录不能由自定义请求生成。
            Proofs.RemoveAll(p => Main.GameUpdateCount - p.Frame > 180);
            int count = 0;
            foreach (Proof proof in Proofs) if (ReferenceEquals(proof.Player, player)) count++;
            if (count >= 8) { LastRecordBreakReject = "limit"; return; }
            // 手持工具信息只在服务端已同步到手时记录：物品同步可能晚于破矿包，
            // 缺失时留 0，由请求阶段自行校验镐力与权限。
            Item held = player.HeldItem;
            bool hasTool = held != null && !held.IsAir;
            Proofs.Add(new Proof { Player = player, Guid = guid, X = x, Y = y, Type = type, Frame = Main.GameUpdateCount,
                ToolType = hasTool ? held.type : 0, Prefix = hasTool ? held.prefix : (byte)0, Pick = hasTool ? held.pick : 0 });
            RecordBreakAccepted++;
        }

        private static bool SamePeer(int slot, Peer peer) => slot >= 0 && slot < Main.maxPlayers && Main.player[slot] != null && Main.player[slot].active && ReferenceEquals(peer.Player, Main.player[slot]) && peer.Guid == PermissionService.GetGuid(slot);

        // 破块最终没有生效时撤销刚登记的来源，保证只有真正被破坏的矿石才能启动连锁。
        internal static void DiscardBreak(Player player, int x, int y, ushort type)
        {
            if (player == null) return;
            for (int i = Proofs.Count - 1; i >= 0; i--)
            {
                Proof proof = Proofs[i];
                if (!ReferenceEquals(proof.Player, player) || proof.X != x || proof.Y != y || proof.Type != type) continue;
                Proofs.RemoveAt(i);
                return;
            }
        }

        private static void ReceiveRequest(int slot, string operation, string payload)
        {
            if (!_initialized || operation != Operation) return;
            string complete;
            if (!NetSyncPayload.TryAcceptRequest(slot, operation, payload, out complete)) return;
            int generation = _generation;
            NetworkRuntime.Enqueue(() => { if (_initialized && generation == _generation) HandleRequest(slot, complete); });
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
                        if (peer != null && peer.Hello == payload) { NetSyncPayload.SendResponseTo(slot, true, Operation, peer.HelloResponse); return; }
                        // 同一连接不因重发hello重置已执行序号。
                        if (peer == null) Peers[slot] = peer = new Peer { Player = player, Guid = PermissionService.GetGuid(slot), Nonce = Guid.NewGuid().ToString("N") };
                        peer.Hello = payload;
                        peer.HelloResponse = Response(id, peer.Nonce, 0, "");
                        NetSyncPayload.SendResponseTo(slot, true, Operation, peer.HelloResponse);
                        return;
                    }
                    var request = new RequestData { Id = id, Payload = payload, X = reader.ReadInt32(), Y = reader.ReadInt32(), Type = reader.ReadUInt16(), Forced = reader.ReadBoolean(), Frame = Main.GameUpdateCount };
                    if (reader.BaseStream.Position != reader.BaseStream.Length || sequence == 0) throw new InvalidDataException();
                    if (peer == null || nonce != peer.Nonce) throw new InvalidDataException("连锁挖矿会话已过期");
                    if (sequence == peer.Sequence && peer.Request == payload)
                    {
                        if (peer.Response != null) NetSyncPayload.SendResponseTo(slot, peer.Result, Operation, peer.Response);
                        return;
                    }
                    if (sequence <= peer.Sequence || peer.Waiting != null || peer.JobStarted)
                    {
                        Mod.LogInfo($"[连锁挖矿服务器] 拒绝请求: slot={slot}, sequence={sequence}, peer.Sequence={peer.Sequence}, peer.Waiting={(peer.Waiting != null ? "有" : "无")}, peer.JobStarted={peer.JobStarted}");
                        throw new InvalidDataException("连锁请求重复或已有待确认请求");
                    }
                    peer.Sequence = sequence; peer.Request = payload; peer.Response = null; peer.Waiting = request;
                    TryStart(slot, peer);
                }
            }
            catch (Exception error)
            {
                if (id.Length == 32) NetSyncPayload.SendResponseTo(slot, false, Operation, Response(id, peer == null ? "" : peer.Nonce, sequence, error is InvalidDataException && error.Message != "Invalid data." ? error.Message : "连锁请求无效"));
            }
        }

        private static void TryStart(int slot, Peer peer)
        {
            RequestData request = peer.Waiting;
            var config = SettingsSync.GetPlayerConfig(slot);

            // 权限检查：房主并开启模式下允许所有玩家（临时方案）
            bool hasAccess = PermissionService.HasModAccess(slot, "quality-of-terraria");
            bool isHost = Main.netMode == 1 || (Main.netMode == 2 && Netplay.Clients[slot].State == 10);
            if (!hasAccess && !isHost)
            {
                Mod.LogInfo($"[连锁挖矿服务端] 权限检查失败: slot={slot}, hasAccess={hasAccess}, isHost={isHost}");
                Finish(slot, peer, false, "服务器未授予本模组功能权限");
                return;
            }

            // 其他检查
            if (peer.Player.dead)
            {
                Finish(slot, peer, false, "角色已死亡");
                return;
            }
            if (config == null)
            {
                Finish(slot, peer, false, "配置未加载");
                return;
            }
            if (!config.VeinMiningEnabled && !request.Forced)
            {
                Finish(slot, peer, false, "连锁挖矿未启用");
                return;
            }
            if (peer.Player.HeldItem.pick <= 0)
            {
                Finish(slot, peer, false, "未持有有效镐子");
                return;
            }
            if (!VeinMiningSystem.IsOre(request.Type))
            {
                Mod.LogInfo($"[连锁挖矿服务端] 不是矿石: type={request.Type}");
                Finish(slot, peer, false, "目标方块不是矿石");
                return;
            }
            Proof proof = Proofs.Find(p => ReferenceEquals(p.Player, peer.Player) && p.X == request.X && p.Y == request.Y && p.Type == request.Type && Main.GameUpdateCount - p.Frame <= 180);
            if (proof == null)
            {
                if (Main.GameUpdateCount - request.Frame >= 15)
                {
                    // 保留诊断信息：联机失败时能区分"服务端没收到原版破矿"和"记录被其他请求用掉"。
                    Mod.LogInfo($"[连锁挖矿] 未匹配原版破矿来源：玩家={slot}，位置=({request.X}, {request.Y})，矿石={request.Type}，当前来源记录={Proofs.Count}，包内破块={Patches.VeinMiningOriginTilePatch.PacketKills}，包内破矿={Patches.VeinMiningOriginTilePatch.PacketOreKills}，Postfix={Patches.VeinMiningOriginTilePatch.PostfixCalls}/{Patches.VeinMiningOriginTilePatch.PostfixRecorded}，来源登记={RecordBreakCalls}/{RecordBreakAccepted}，最近拒绝={LastRecordBreakReject ?? "无"}");
                    // 不向客户端发送错误提示，因为这只是内部时序问题，不影响连锁功能整体工作
                    Finish(slot, peer, false, "");
                }
                return;
            }
            Proofs.Remove(proof);
            if (proof.ToolType != 0 && (proof.ToolType != peer.Player.HeldItem.type || proof.Prefix != peer.Player.HeldItem.prefix || proof.Pick != peer.Player.HeldItem.pick))
            { Finish(slot, peer, false, "原版挖矿后工具已改变"); return; }
            bool started = VeinMiningSystem.Start(peer.Player, request.X, request.Y, proof.Type, config);
            Mod.LogInfo($"[连锁挖矿服务器] TryStart: slot={slot}, started={started}, player.whoAmI={peer.Player.whoAmI}, 坐标=({request.X},{request.Y})");
            if (started)
            {
                // 任务已启动，标记等待完成，不立即发送响应
                peer.JobStarted = true;
            }
            else
            {
                // 任务启动失败（可能正在处理其他矿脉），立即返回失败
                Finish(slot, peer, false, "当前矿脉正在处理中或已失效");
            }
        }

        private static void Finish(int slot, Peer peer, bool ok, string message)
        {
            peer.Result = ok;
            // 成功时，尝试获取连锁结果（数量和矿石名称）
            if (ok)
            {
                var completed = VeinMiningSystem.TakeCompletedJob(slot);
                if (completed != null && completed.Count > 1)
                {
                    // 格式：count|oreName （例如："5|铜矿石"）
                    message = $"{completed.Count}|{completed.OreName}";
                }
            }
            peer.Response = Response(peer.Waiting.Id, peer.Nonce, peer.Sequence, message);
            peer.Waiting = null;
            NetSyncPayload.SendResponseTo(slot, ok, Operation, peer.Response);
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
            string complete;
            if (!NetSyncPayload.TryAcceptResponse(operation, payload, out complete)) return;
            int generation = _generation;
            NetworkRuntime.Enqueue(() => { if (_initialized && generation == _generation) HandleResponse(ok, complete); });
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

                    // 获取客户端配置，联机时使用本地玩家配置
                    var config = SettingsSync.GetPlayerConfig(Main.myPlayer) ?? Mod.Config;
                    bool showMessages = config != null && config.VeinMiningShowMessages;

                    if (!string.IsNullOrEmpty(message))
                    {
                        if (!ok)
                        {
                            // 失败：显示错误消息（如果开启了提示）
                            if (showMessages)
                                Main.NewText(message, 255, 180, 100);
                        }
                        else if (message.Contains("|"))
                        {
                            // 成功：解析 "count|oreName" 格式，始终显示（这是重要的游戏反馈）
                            string[] parts = message.Split('|');
                            Mod.LogInfo($"[连锁挖矿客户端] 收到成功响应: message='{message}', parts.Length={parts.Length}");
                            if (parts.Length == 2 && int.TryParse(parts[0], out int count))
                            {
                                Mod.LogInfo($"[连锁挖矿客户端] 解析成功: count={count}, oreName='{parts[1]}'");
                                if (count > 1)
                                {
                                    string text = $"【连锁挖矿】已连锁 {count} 个{parts[1]}";
                                    Mod.LogInfo($"[连锁挖矿客户端] 显示提示: '{text}'");
                                    Main.NewText(text, 100, 240, 255);
                                }
                                else
                                {
                                    Mod.LogInfo($"[连锁挖矿客户端] count <= 1，不显示提示");
                                }
                            }
                            else
                            {
                                Mod.LogInfo($"[连锁挖矿客户端] 解析失败或格式错误");
                            }
                        }
                        else
                        {
                            Mod.LogInfo($"[连锁挖矿客户端] message 不包含 '|': '{message}'");
                        }
                    }
                    else
                    {
                        Mod.LogInfo($"[连锁挖矿客户端] 收到空 message");
                    }
                    SendNext();
                }
            }
            catch (Exception) { }
        }
    }
}
