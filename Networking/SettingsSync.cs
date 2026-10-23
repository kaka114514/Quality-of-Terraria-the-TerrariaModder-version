using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Terraria;
using TerrariaModder.Core.Config;
using TerrariaModder.Core.Net;
using TerrariaModder.Core.Permissions;

namespace QualityOfTerraria.Networking
{
    internal static class SettingsSync
    {
        private const string Prefix = "qot.settings.";
        private const string Version = "1.1.8";
        private const int MaxRequests = 16;
        private static string _clientNonce, _room;
        private static int _lifecycle, _helloFrames;
        private static Dictionary<string, object> _lastPersonal;
        private static Action<int, string, string> _requestHandler;
        private static Action<bool, string, string> _responseHandler;
        private static readonly Dictionary<int, Peer> Peers = new Dictionary<int, Peer>();
        private static readonly Dictionary<string, Proposal> Proposals = new Dictionary<string, Proposal>();
        private static readonly List<RequestRow> Rows = new List<RequestRow>();
        private static readonly HashSet<string> Applying = new HashSet<string>();
        internal static string Status { get; private set; } = "进入联机房间后可申请同步";
        internal static IReadOnlyList<RequestRow> Requests => Rows;

        internal sealed class RequestRow
        {
            internal string Id, TargetId, Name, State;
            internal bool Incoming, Pending;
            internal DateTime Expires;
        }
        private sealed class Peer
        {
            internal string Identity, Nonce, Connection;
            internal QualityOfTerrariaConfig Personal;
        }
        private sealed class Target
        {
            internal int Slot;
            internal string Connection, Name;
            internal bool Decided, AwaitingAck;
        }
        private sealed class Proposal
        {
            internal string Id, SourceConnection, Name;
            internal int Source;
            internal DateTime Expires;
            internal Dictionary<string, object> Snapshot;
            internal readonly List<Target> Targets = new List<Target>();
            internal bool WorldApplied;
        }

        internal static void Initialize()
        {
            Unload();
            UI.SettingsSyncUI.SetEnabled(true);
            _clientNonce = Guid.NewGuid().ToString("N");
            if (Main.netMode == 2) _room = Guid.NewGuid().ToString("N");
            int lifecycle = _lifecycle;
            _requestHandler = (slot, operation, payload) =>
            {
                if (operation == null || !operation.StartsWith(Prefix, StringComparison.Ordinal) || payload == null || payload.Length > 32000) return;
                string complete;
                if (!NetSyncPayload.TryAcceptRequest(slot, operation, payload, out complete)) return;
                NetworkRuntime.Enqueue(() => { if (lifecycle == _lifecycle && Mod.WorldActive) HandleRequest(slot, operation, complete); });
            };
            _responseHandler = (ok, operation, payload) =>
            {
                if (operation == null || !operation.StartsWith(Prefix, StringComparison.Ordinal) || payload == null || payload.Length > 32000) return;
                string complete;
                if (!NetSyncPayload.TryAcceptResponse(operation, payload, out complete)) return;
                NetworkRuntime.Enqueue(() => { if (lifecycle == _lifecycle && Mod.WorldActive) HandleResponse(ok, operation, complete); });
            };
            NetSync.OnStorageRequest += _requestHandler;
            NetSync.OnStorageResponse += _responseHandler;
            Status = Main.netMode == 1 ? "正在连接设置申请服务" : "仅联机房间可申请同步";
            if (Main.netMode == 1) Hello();
        }

        internal static void Update()
        {
            if (!Mod.WorldActive) return;
            if (Main.netMode == 1 && _room == null && ++_helloFrames >= 180) { _helloFrames = 0; Hello(); }
            if (Main.netMode == 1 && _room != null) SendPersonalChanges();
            if (Main.netMode == 2)
            {
                foreach (var pair in Peers.ToArray())
                    if (!Active(pair.Key) || PermissionService.GetGuid(pair.Key) != pair.Value.Identity) ConnectionChanged(pair.Key);
                foreach (var proposal in Proposals.Values.ToArray())
                {
                    if (!Matches(proposal.Source, proposal.SourceConnection)) { Invalidate(proposal, "发起者已断线，申请失效"); continue; }
                    foreach (var target in proposal.Targets.Where(t => !t.Decided || t.AwaitingAck).ToArray())
                    {
                        if (!Matches(target.Slot, target.Connection)) Finish(proposal, target, "已断线，申请失效");
                        else if (DateTime.UtcNow >= proposal.Expires)
                        {
                            Send(target.Slot, "expired", w => { w.Write(proposal.Id); w.Write("申请超时失效"); });
                            Finish(proposal, target, "申请超时失效");
                        }
                    }
                    if (proposal.Targets.All(t => t.Decided && !t.AwaitingAck)) Proposals.Remove(proposal.Id);
                }
            }
            foreach (var row in Rows)
                if (row.Pending && DateTime.UtcNow >= row.Expires) { row.Pending = false; row.State = "申请超时失效"; }
        }

        internal static void ResetSession()
        {
            _lifecycle++;
            _room = _clientNonce = null;
            _helloFrames = 0;
            _lastPersonal = null;
            Peers.Clear(); Proposals.Clear(); Rows.Clear(); Applying.Clear();
            Status = "进入联机房间后可申请同步";
            UI.SettingsSyncUI.ResetSession();
        }

        internal static void Unload()
        {
            if (_requestHandler != null) NetSync.OnStorageRequest -= _requestHandler;
            if (_responseHandler != null) NetSync.OnStorageResponse -= _responseHandler;
            _requestHandler = null; _responseHandler = null;
            ResetSession();
            UI.SettingsSyncUI.SetEnabled(false);
        }

        internal static void ConnectionChanged(int slot)
        {
            Peers.Remove(slot);
        }

        internal static void QueueConnectionChanged(int slot)
        {
            int lifecycle = _lifecycle;
            NetworkRuntime.Enqueue(() => { if (lifecycle == _lifecycle) ConnectionChanged(slot); });
        }

        internal static QualityOfTerrariaConfig GetPlayerConfig(int slot)
        {
            if (!Mod.WorldActive) return null;
            if (Main.netMode != 2) return slot == Main.myPlayer ? Mod.Config : null;
            Peer peer;
            return Peers.TryGetValue(slot, out peer) && Matches(slot, peer.Connection) ? peer.Personal : null;
        }

        private static Dictionary<string, object> PersonalSnapshot()
            => Metadata().Where(p => p.Scope == ConfigScope.Client).ToDictionary(p => p.Key, p => p.GetValue(Mod.Config), StringComparer.Ordinal);

        private static void SendPersonalChanges()
        {
            var snapshot = PersonalSnapshot();
            if (_lastPersonal != null && snapshot.Count == _lastPersonal.Count && snapshot.All(p => _lastPersonal.ContainsKey(p.Key) && Equals(p.Value, _lastPersonal[p.Key]))) return;
            string error;
            if (!ValidateSnapshot(snapshot, out error)) { Status = error; return; }
            NetSyncPayload.SendRequest(Prefix + "personal", Write(w => { Header(w); WriteSnapshot(w, snapshot); }));
            _lastPersonal = snapshot;
        }

        private static QualityOfTerrariaConfig ReadPersonalConfig(Dictionary<string, object> snapshot)
        {
            string error;
            if (!ValidateSnapshot(snapshot, out error)) throw new FormatException();
            var personal = Metadata().Where(p => p.Scope == ConfigScope.Client).ToArray();
            if (snapshot.Count != personal.Length || personal.Any(p => !snapshot.ContainsKey(p.Key))) throw new FormatException();
            var config = new QualityOfTerrariaConfig();
            foreach (var property in personal) property.SetValue(config, snapshot[property.Key]);
            return config;
        }

        internal static void RequestSync()
        {
            if (Main.netMode != 1 || _room == null) { Status = "请在联机房间等待设置服务连接完成"; return; }
            var snapshot = Metadata().ToDictionary(p => p.Key, p => p.GetValue(Mod.Config), StringComparer.Ordinal);
            string error;
            if (!ValidateSnapshot(snapshot, out error)) { Status = error; return; }
            NetSyncPayload.SendRequest(Prefix + "create", Write(w => { Header(w); WriteSnapshot(w, snapshot); }));
            Status = "申请已发送，等待其他玩家同意";
        }

        internal static void Decide(RequestRow row, bool approve)
        {
            if (!row.Incoming || !row.Pending || _room == null) return;
            row.Pending = false;
            row.State = approve ? "已同意，等待服务器核验" : "已拒绝";
            NetSyncPayload.SendRequest(Prefix + "decision", Write(w => { Header(w); w.Write(row.Id); w.Write(approve); }));
        }

        private static IEnumerable<ConfigPropertyMeta> Metadata()
        {
            return Mod.Config.GetPropertyMetadata().Where(p => p.Property.DeclaringType == typeof(QualityOfTerrariaConfig)
                && p.Property.CanWrite && Attribute.IsDefined(p.Property, typeof(LabelAttribute))
                && (Attribute.IsDefined(p.Property, typeof(ClientAttribute)) || Attribute.IsDefined(p.Property, typeof(ServerAttribute)))
                && !p.RestartRequired && (p.PropertyType == typeof(bool) || p.PropertyType == typeof(int)
                    || p.PropertyType == typeof(float) || p.PropertyType == typeof(double)));
        }

        internal static bool ValidateSnapshot(Dictionary<string, object> snapshot, out string error)
        {
            error = null;
            if (snapshot == null || snapshot.Count == 0 || snapshot.Count > 256) { error = "配置快照大小无效"; return false; }
            var metadata = Metadata().ToDictionary(p => p.Key, StringComparer.Ordinal);
            foreach (var pair in snapshot)
            {
                ConfigPropertyMeta property;
                if (!metadata.TryGetValue(pair.Key, out property)) { error = "不支持的配置项：" + pair.Key; return false; }
                if (pair.Value == null || pair.Value.GetType() != property.PropertyType) { error = "配置类型不匹配：" + pair.Key; return false; }
                if (property.PropertyType == typeof(bool)) continue;
                double number = Convert.ToDouble(pair.Value);
                if (double.IsNaN(number) || double.IsInfinity(number) || (property.Min.HasValue && number < property.Min.Value)
                    || (property.Max.HasValue && number > property.Max.Value)) { error = "配置超出允许范围：" + pair.Key; return false; }
            }
            return true;
        }

        private static void Hello()
        {
            if (_clientNonce != null) NetSyncPayload.SendRequest(Prefix + "hello", Write(w => { w.Write(Version); w.Write(_clientNonce); WriteSnapshot(w, PersonalSnapshot()); }));
        }

        private static void HandleRequest(int slot, string operation, string payload)
        {
            if (Main.netMode != 2 || !Active(slot)) return;
            try
            {
                using (var reader = Read(payload))
                {
                    if (reader.ReadString() != Version) { Reject(slot, "模组版本不一致，需使用 v" + Version); return; }
                    string nonce = reader.ReadString();
                    if (!Nonce(nonce)) return;
                    if (operation == Prefix + "hello")
                    {
                        var personal = ReadPersonalConfig(ReadSnapshot(reader));
                        End(reader);
                        string identity = PermissionService.GetGuid(slot);
                        if (string.IsNullOrEmpty(identity)) { Reject(slot, "玩家身份尚未就绪"); return; }
                        Peer old;
                        if (!Peers.TryGetValue(slot, out old) || old.Identity != identity || old.Nonce != nonce)
                            Peers[slot] = new Peer { Identity = identity, Nonce = nonce, Connection = Guid.NewGuid().ToString("N"), Personal = personal };
                        else old.Personal = personal;
                        Send(slot, "hello", null);
                        return;
                    }
                    Peer peer;
                    if (!Peers.TryGetValue(slot, out peer) || peer.Nonce != nonce || peer.Identity != PermissionService.GetGuid(slot)
                        || reader.ReadString() != _room) { Reject(slot, "身份或房间会话已失效"); return; }
                    if (operation == Prefix + "personal")
                    {
                        var personal = ReadPersonalConfig(ReadSnapshot(reader)); End(reader);
                        peer.Personal = personal;
                    }
                    else if (operation == Prefix + "create") Create(slot, peer, ReadSnapshot(reader), reader);
                    else if (operation == Prefix + "decision")
                    {
                        string id = reader.ReadString(); bool approve = reader.ReadBoolean(); End(reader);
                        Proposal proposal; Target target;
                        if (!FindDecision(slot, peer, id, out proposal, out target) || target.Decided) { Reject(slot, "申请不存在、已处理或已失效"); return; }
                        if (!approve) { Finish(proposal, target, "已拒绝"); return; }
                        target.Decided = true; target.AwaitingAck = true;
                        string worldResult = ApplyWorld(proposal, slot);
                        Send(slot, "apply", w => { w.Write(id); w.Write(worldResult); WriteSnapshot(w, proposal.Snapshot); });
                    }
                    else if (operation == Prefix + "applied")
                    {
                        string id = reader.ReadString(); string result = reader.ReadString(); End(reader);
                        Proposal proposal; Target target;
                        if (result.Length > 500 || !FindDecision(slot, peer, id, out proposal, out target) || !target.AwaitingAck) return;
                        Finish(proposal, target, result);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException || ex is OverflowException)
            { Reject(slot, "申请数据格式无效"); }
        }

        private static void Create(int slot, Peer peer, Dictionary<string, object> snapshot, BinaryReader reader)
        {
            End(reader);
            string error;
            if (!ValidateSnapshot(snapshot, out error)) { Reject(slot, error); return; }
            if (Proposals.Count >= MaxRequests || Proposals.Values.Count(p => p.SourceConnection == peer.Connection) >= 2)
            { Reject(slot, "待处理申请过多，请等待已有申请完成"); return; }
            var proposal = new Proposal { Id = Guid.NewGuid().ToString("N"), Source = slot, SourceConnection = peer.Connection,
                Name = Main.player[slot].name, Snapshot = snapshot, Expires = DateTime.UtcNow.AddMinutes(2) };
            foreach (var pair in Peers)
                if (pair.Key != slot && Matches(pair.Key, pair.Value.Connection))
                    proposal.Targets.Add(new Target { Slot = pair.Key, Connection = pair.Value.Connection, Name = Main.player[pair.Key].name });
            if (proposal.Targets.Count == 0) { Reject(slot, "没有已连接同版本设置服务的其他玩家"); return; }
            Proposals.Add(proposal.Id, proposal);
            foreach (var target in proposal.Targets)
            {
                Send(target.Slot, "offer", w => { w.Write(proposal.Id); w.Write(proposal.Name); WriteSnapshot(w, snapshot); });
                Send(slot, "result", w => { w.Write(proposal.Id); w.Write(target.Connection); w.Write(target.Name); w.Write("等待同意"); });
            }
        }

        private static bool FindDecision(int slot, Peer peer, string id, out Proposal proposal, out Target target)
        {
            target = null;
            if (!Proposals.TryGetValue(id, out proposal) || DateTime.UtcNow >= proposal.Expires || !Matches(proposal.Source, proposal.SourceConnection)) return false;
            target = proposal.Targets.FirstOrDefault(t => t.Slot == slot && t.Connection == peer.Connection);
            return target != null;
        }

        private static string ApplyWorld(Proposal proposal, int acceptingSlot)
        {
            var world = Metadata().Where(p => p.Scope == ConfigScope.Server && proposal.Snapshot.ContainsKey(p.Key)).ToArray();
            if (world.Length == 0) return "无世界项";
            if (proposal.WorldApplied) return "世界项已由管理员同意并应用";
            string names = string.Join("、", world.Select(p => p.Label));
            if (!PermissionService.IsAdmin(acceptingSlot)) return "未应用世界项（需要房主/管理员同意）：" + names;
            if (ConfigManager.GetCurrentScope() != ConfigScope.Server) return "未应用世界项（Core 当前进程无法保存服务器配置）：" + names;
            var previous = world.ToDictionary(p => p.Key, p => p.GetValue(Mod.Config));
            try
            {
                foreach (var property in world) property.SetValue(Mod.Config, proposal.Snapshot[property.Key]);
                string saveError;
                if (!SaveVerified(Mod.Config, out saveError)) throw new IOException(saveError);
            }
            catch (Exception)
            {
                foreach (var property in world) property.SetValue(Mod.Config, previous[property.Key]);
                return "未应用世界项（服务器保存失败）：" + names;
            }
            foreach (var property in world) NetSync.BroadcastConfigChange("quality-of-terraria", property, Mod.Config, -1);
            proposal.WorldApplied = true;
            return "世界项已由管理员同意并保存、广播";
        }

        private static void HandleResponse(bool ok, string operation, string payload)
        {
            if (Main.netMode != 1 || _clientNonce == null) return;
            try
            {
                using (var reader = Read(payload))
                {
                    if (reader.ReadString() != Version || reader.ReadString() != _clientNonce) return;
                    string room = reader.ReadString();
                    if (operation == Prefix + "hello")
                    {
                        End(reader); if (!Nonce(room)) return;
                        _room = room; Status = "设置服务已连接，可发送同步申请"; return;
                    }
                    if (_room == null || room != _room) return;
                    if (operation == Prefix + "error") { Status = reader.ReadString(); End(reader); return; }
                    string id = reader.ReadString();
                    if (!Nonce(id)) return;
                    if (operation == Prefix + "offer")
                    {
                        string name = reader.ReadString(); var snapshot = ReadSnapshot(reader); End(reader);
                        string error;
                        if (!ValidateSnapshot(snapshot, out error)) { Status = error; return; }
                        if (Rows.Any(r => r.Id == id && r.Incoming)) return;
                        AddRow(new RequestRow { Id = id, Name = name, State = "等待同意", Incoming = true, Pending = true, Expires = DateTime.UtcNow.AddMinutes(2) });
                        Main.NewText(name + " 申请同步设置，请在更好的体验设置中同意或拒绝", 255, 220, 100);
                    }
                    else if (operation == Prefix + "apply")
                    {
                        string worldResult = reader.ReadString(); var snapshot = ReadSnapshot(reader); End(reader);
                        if (!Applying.Add(id)) return;
                        // 服务器只对本连接已同意的申请发送 apply；本地也要求存在对应申请。
                        var row = Rows.FirstOrDefault(r => r.Id == id && r.Incoming && !r.Pending && r.State == "已同意，等待服务器核验");
                        if (row == null) { Applying.Remove(id); return; }
                        string result = ApplyPersonal(snapshot) + "；" + worldResult;
                        row.State = result; Status = result;
                        NetSyncPayload.SendRequest(Prefix + "applied", Write(w => { Header(w); w.Write(id); w.Write(result.Length > 500 ? result.Substring(0, 500) : result); }));
                    }
                    else if (operation == Prefix + "result")
                    {
                        string targetId = reader.ReadString(), name = reader.ReadString(), result = reader.ReadString(); End(reader);
                        if (!Nonce(targetId)) return;
                        var row = Rows.FirstOrDefault(r => r.Id == id && !r.Incoming && r.TargetId == targetId);
                        if (row == null) AddRow(row = new RequestRow { Id = id, TargetId = targetId, Name = name, Expires = DateTime.UtcNow.AddMinutes(2) });
                        bool changed = row.State != result;
                        row.State = result; row.Pending = result == "等待同意";
                        Status = name + "：" + result;
                        if (changed && !row.Pending) Main.NewText("【设置同步】" + Status, 150, 230, 255);
                    }
                    else if (operation == Prefix + "expired")
                    {
                        string result = reader.ReadString(); End(reader);
                        foreach (var row in Rows.Where(r => r.Id == id)) { row.Pending = false; row.State = result; }
                        Status = result;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException || ex is OverflowException)
            { Status = "收到无效设置申请数据"; }
        }

        private static string ApplyPersonal(Dictionary<string, object> snapshot)
        {
            string error;
            if (!ValidateSnapshot(snapshot, out error)) return "个人设置未应用：" + error;
            if (ConfigManager.GetCurrentScope() != ConfigScope.Client) return "个人设置未应用：Core 当前进程无法保存个人配置";
            var personal = Metadata().Where(p => p.Scope == ConfigScope.Client && snapshot.ContainsKey(p.Key)).ToArray();
            var previous = personal.ToDictionary(p => p.Key, p => p.GetValue(Mod.Config));
            try
            {
                foreach (var property in personal) property.SetValue(Mod.Config, snapshot[property.Key]);
                string saveError;
                if (!SaveVerified(Mod.Config, out saveError)) throw new IOException(saveError);
                return "个人设置已保存";
            }
            catch (Exception)
            {
                foreach (var property in personal) property.SetValue(Mod.Config, previous[property.Key]);
                return "个人设置未应用：保存失败";
            }
        }

        // Core 0.4.1 的 Save() 吞下写盘异常。返回成功前检查它实际保存的 scope 文件，
        // 避免内存已改、磁盘未改时仍报告保存完成。调用者负责回滚本次变更。
        internal static bool SaveVerified(ModConfig config, out string error)
        {
            error = null;
            try
            {
                var properties = config.GetPropertyMetadata().Where(p => p.Scope == ConfigManager.GetCurrentScope()).ToArray();
                var expected = properties.ToDictionary(p => p.Key, p => p.GetValue(config), StringComparer.Ordinal);
                config.Save();
                var saved = ConfigManager.ParseJsonRaw(File.ReadAllText(config.FilePath));
                object version;
                if (!saved.TryGetValue("_version", out version) || Convert.ToDouble(version) != config.Version)
                    throw new InvalidDataException("保存文件版本不一致");
                foreach (var property in properties)
                {
                    object value;
                    if (!saved.TryGetValue(property.Key, out value) || value == null) throw new InvalidDataException("保存文件缺少配置项：" + property.Key);
                    if (property.PropertyType == typeof(bool) && !(value is bool)) throw new InvalidDataException("保存文件配置类型不一致：" + property.Key);
                    if (property.PropertyType == typeof(int) || property.PropertyType == typeof(float) || property.PropertyType == typeof(double))
                    {
                        if (value is string || value is bool) throw new InvalidDataException("保存文件配置类型不一致：" + property.Key);
                        double number = Convert.ToDouble(value);
                        if (double.IsNaN(number) || double.IsInfinity(number) || (property.Min.HasValue && number < property.Min.Value)
                            || (property.Max.HasValue && number > property.Max.Value)) throw new InvalidDataException("保存文件配置范围无效：" + property.Key);
                    }
                    if (!Equals(Convert.ChangeType(value, property.PropertyType, System.Globalization.CultureInfo.InvariantCulture), expected[property.Key]))
                        throw new InvalidDataException("保存文件配置值不一致：" + property.Key);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "配置保存未确认：" + exception.Message;
                return false;
            }
        }

        private static void AddRow(RequestRow row)
        {
            if (Rows.Count >= 544)
            {
                var completed = Rows.FirstOrDefault(r => !r.Pending);
                if (completed != null) { Rows.Remove(completed); Applying.Remove(completed.Id); }
                else { Status = "申请列表已满"; return; }
            }
            Rows.Add(row);
        }

        private static void Finish(Proposal proposal, Target target, string result)
        {
            target.Decided = true; target.AwaitingAck = false;
            if (Matches(proposal.Source, proposal.SourceConnection))
                Send(proposal.Source, "result", w => { w.Write(proposal.Id); w.Write(target.Connection); w.Write(target.Name); w.Write(result); });
        }

        private static void Invalidate(Proposal proposal, string reason)
        {
            foreach (var target in proposal.Targets.Where(t => Matches(t.Slot, t.Connection)))
                Send(target.Slot, "expired", w => { w.Write(proposal.Id); w.Write(reason); });
            Proposals.Remove(proposal.Id);
        }

        private static bool Active(int slot) => slot >= 0 && slot < Main.player.Length && Main.player[slot] != null && Main.player[slot].active;
        private static bool Matches(int slot, string connection)
        {
            Peer peer;
            return Active(slot) && Peers.TryGetValue(slot, out peer) && peer.Connection == connection && peer.Identity == PermissionService.GetGuid(slot);
        }
        private static bool Nonce(string value) => value != null && value.Length == 32 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        private static void Reject(int slot, string reason) { if (Peers.ContainsKey(slot)) Send(slot, "error", w => w.Write(reason), false); }
        private static void Send(int slot, string operation, Action<BinaryWriter> body, bool ok = true)
        {
            Peer peer;
            if (!Peers.TryGetValue(slot, out peer)) return;
            NetSyncPayload.SendResponseTo(slot, ok, Prefix + operation, Write(w => { w.Write(Version); w.Write(peer.Nonce); w.Write(_room); body?.Invoke(w); }));
        }
        private static void Header(BinaryWriter writer) { writer.Write(Version); writer.Write(_clientNonce); writer.Write(_room); }
        private static string Write(Action<BinaryWriter> action)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) action(writer);
                return Convert.ToBase64String(stream.ToArray());
            }
        }
        private static BinaryReader Read(string payload) => new BinaryReader(new MemoryStream(Convert.FromBase64String(payload)), Encoding.UTF8);
        private static void End(BinaryReader reader) { if (reader.BaseStream.Position != reader.BaseStream.Length) throw new FormatException(); }
        private static void WriteSnapshot(BinaryWriter writer, Dictionary<string, object> snapshot)
        {
            writer.Write(Mod.Config.Version); writer.Write(snapshot.Count);
            foreach (var pair in snapshot)
            {
                writer.Write(pair.Key);
                if (pair.Value is bool) { writer.Write((byte)1); writer.Write((bool)pair.Value); }
                else if (pair.Value is int) { writer.Write((byte)2); writer.Write((int)pair.Value); }
                else if (pair.Value is float) { writer.Write((byte)3); writer.Write((float)pair.Value); }
                else if (pair.Value is double) { writer.Write((byte)4); writer.Write((double)pair.Value); }
                else throw new FormatException();
            }
        }
        private static Dictionary<string, object> ReadSnapshot(BinaryReader reader)
        {
            if (reader.ReadInt32() != Mod.Config.Version) throw new FormatException();
            int count = reader.ReadInt32(); if (count < 1 || count > 256) throw new FormatException();
            var snapshot = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString(); if (key.Length > 128 || snapshot.ContainsKey(key)) throw new FormatException();
                object value;
                switch (reader.ReadByte())
                {
                    case 1: value = reader.ReadBoolean(); break;
                    case 2: value = reader.ReadInt32(); break;
                    case 3: value = reader.ReadSingle(); break;
                    case 4: value = reader.ReadDouble(); break;
                    default: throw new FormatException();
                }
                snapshot.Add(key, value);
            }
            return snapshot;
        }
    }
}
