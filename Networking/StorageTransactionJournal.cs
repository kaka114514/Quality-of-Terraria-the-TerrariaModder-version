using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using HarmonyLib;
using Terraria;
using Terraria.IO;
using Terraria.ID;
using Terraria.Utilities;

namespace QualityOfTerraria.Networking
{
    internal static class StorageTransactionJournal
    {
        internal sealed class ClientRecord
        {
            internal string World, Id, Before, Request, After;
            // 已收到并应用服务器确认，但角色存档还没落盘；此时不再锁库存，记录保留到保存成功再清除。
            internal bool Confirmed;
        }
        private sealed class ServerRecords
        {
            internal readonly Dictionary<string, Dictionary<string, string>> Replies = new Dictionary<string, Dictionary<string, string>>();
            internal readonly Dictionary<string, HashSet<string>> Tokens = new Dictionary<string, HashSet<string>>();
            internal readonly Dictionary<string, string> CurrentTokens = new Dictionary<string, string>();
        }
        private sealed class CursorRecord
        {
            internal string CharacterHash;
            internal int Type, Stack;
            internal byte Prefix;
            internal bool Favorite;
        }

        private static readonly Dictionary<string, ServerRecords> Worlds = new Dictionary<string, ServerRecords>();
        private static string _character, _path, _serverWorld, _characterPath;
        private static bool _canSave, _saving, _characterCloud;
        // 角色整份保存（写入角色文件 + 读回校验 + 鼠标记录）是联机每次存取的主要卡顿来源，
        // 短时间内连续操作合并成一次；事务记录保留到角色真正落盘后再清除。
        private const int SaveCoalesceMs = 4000;
        private const int SaveOpsLimit = 8;
        private static int _lastSaveTick;
        private static int _opsSinceSave;
        internal static ClientRecord Pending { get; private set; }
        internal static bool ClientLocked => _character != null && (!_canSave || (Pending != null && !Pending.Confirmed));
        internal static string Status { get; private set; } = "";

        internal static void InitializeClient(string characterIdentity)
        {
            _character = characterIdentity; Pending = null; _canSave = false;
            _characterPath = Main.ActivePlayerFileData?.Path;
            _characterCloud = Main.ActivePlayerFileData?.IsCloudSave == true;
            if (string.IsNullOrEmpty(characterIdentity)) { Status = "无法确认角色身份，未发送物品操作"; return; }
            _path = Path.Combine(Main.SavePath, "TerrariaModder", "player-data", StoragePersistence.Identity(characterIdentity) + ".storage-transaction.dat");
            _lastSaveTick = 0; _opsSinceSave = 0;
            ClientRecord loaded;
            bool hasJournal = File.Exists(_path) || File.Exists(_path + ".bak");
            if (StoragePersistence.Read(_path, reader =>
            {
                int version = reader.ReadInt32();
                if (version != 1 && version != 2) throw new InvalidDataException("事务日志版本不支持");
                if (!reader.ReadBoolean()) return null;
                var record = new ClientRecord { World = reader.ReadString(), Id = reader.ReadString(), Before = reader.ReadString(), Request = reader.ReadString() };
                if (version >= 2)
                {
                    record.Confirmed = reader.ReadBoolean();
                    record.After = reader.ReadString();
                }
                Validate(record); return record;
            }, out loaded)) Pending = loaded;
            else if (hasJournal)
            {
                _canSave = false;
                Status = "事务日志主文件和备份均无法读取；库存保持锁定";
                Mod.LogError(Status, new InvalidDataException("事务日志记录损坏"));
                return;
            }
            _canSave = true;
            if (Pending != null)
            {
                if (Pending.Confirmed && !string.IsNullOrEmpty(Pending.After))
                {
                    try { ApplyInventorySnapshot(Pending.After); }
                    catch (Exception error) { _canSave = false; Status = "已确认库存快照无法恢复；库存保持锁定"; Mod.LogError(Status, error); return; }
                    Status = "已恢复上次确认的库存，等待角色存档落盘";
                }
                else Status = "有未确认的物品操作；返回原服务器等待恢复，未知结果不能自动取消";
                return;
            }
            string cursorPath = _path + ".cursor";
            if (!File.Exists(cursorPath) && !File.Exists(cursorPath + ".bak")) { Status = ""; return; }
            CursorRecord cursor;
            if (!StoragePersistence.Read(cursorPath, reader =>
            {
                if (reader.ReadInt32() != 1) throw new InvalidDataException("鼠标物品记录版本不支持");
                var value = new CursorRecord { CharacterHash = reader.ReadString(), Type = reader.ReadInt32(), Stack = reader.ReadInt32(), Prefix = reader.ReadByte(), Favorite = reader.ReadBoolean() };
                if (value.Type < 0 || value.Type >= ItemID.Count || value.Stack < 0 || value.Stack > 1000000 || (value.Type == 0) != (value.Stack == 0)) throw new InvalidDataException("鼠标物品记录不合法");
                return value;
            }, out cursor)) { _canSave = false; Status = "鼠标物品记录无法读取；库存保持锁定"; return; }
            try
            {
                string currentHash = CharacterHash(Main.ActivePlayerFileData);
                if (cursor.CharacterHash != currentHash)
                {
                    VerifyCurrentCharacter(Main.ActivePlayerFileData, currentHash);
                    // 原版会保存鼠标物品。只在没有待处理交易且当前库存与原版存档一致时，
                    // 接受当前存档；保留旧记录，不把旧鼠标物品再次恢复出来。
                    string archive = cursorPath + ".stale-" + Guid.NewGuid().ToString("N");
                    File.Copy(cursorPath, archive);
                    if (File.Exists(cursorPath + ".bak")) File.Copy(cursorPath + ".bak", archive + ".bak");
                    if (File.Exists(_path)) File.Copy(_path, archive + ".journal");
                    File.WriteAllBytes(archive + ".plr", FileUtilities.ReadAllBytes(Main.ActivePlayerFileData.Path, Main.ActivePlayerFileData.IsCloudSave));
                    Status = "角色存档已更新并通过核验；保留当前物品，过期鼠标记录已备份";
                    Mod.LogInfo(Status + "：" + archive);
                    return;
                }
                Item mouse = Main.mouseItem;
                if (mouse != null && !mouse.IsAir && (mouse.type != cursor.Type || mouse.stack != cursor.Stack || mouse.prefix != cursor.Prefix))
                    throw new InvalidDataException("当前鼠标物品与已保存记录不一致");
                var restored = new Item();
                if (cursor.Type > 0) { restored.SetDefaults(cursor.Type); restored.Prefix(cursor.Prefix); restored.stack = cursor.Stack; restored.favorited = cursor.Favorite; }
                Main.mouseItem = restored;
                Main.LocalPlayer.inventory[58] = restored;
                Status = "";
            }
            catch (Exception error) { _canSave = false; Status = "角色存档与鼠标记录未能核验；保留记录并锁定库存"; Mod.LogError(Status, error); }
        }

        private static void Validate(ClientRecord record)
        {
            Guid world;
            if (!Guid.TryParseExact(record.World, "N", out world) || world == Guid.Empty || string.IsNullOrEmpty(record.Id) || record.Id.Length > 128 ||
                string.IsNullOrEmpty(record.Before) || record.Before.Length > 32768 || string.IsNullOrEmpty(record.Request) || record.Request.Length > 32768)
                throw new InvalidDataException("未确认事务记录不合法");
            Convert.FromBase64String(record.Before); Convert.FromBase64String(record.Request);
            if (record.Confirmed)
            {
                if (string.IsNullOrEmpty(record.After) || record.After.Length > 32768) throw new InvalidDataException("已确认库存快照不合法");
                Convert.FromBase64String(record.After);
            }
        }

        internal static bool BeginClient(string world, string id, string beforeBase64, string requestBase64)
        {
            if (!_canSave || _path == null || (Pending != null && !Pending.Confirmed)) { Status = "事务日志尚未恢复，不能开始新的物品操作"; return false; }
            try
            {
                var record = new ClientRecord { World = world, Id = id, Before = beforeBase64, Request = requestBase64 };
                Validate(record);
                StoragePersistence.WriteInPlace(_path, writer =>
                {
                    writer.Write(2); writer.Write(true); writer.Write(record.World); writer.Write(record.Id); writer.Write(record.Before); writer.Write(record.Request);
                    writer.Write(false); writer.Write("");
                });
                Pending = record; Status = "物品操作已记录，等待服务器确认"; return true;
            }
            catch (Exception error) { Status = "写入事务日志失败，未发送物品操作"; Mod.LogError(Status, error); return false; }
        }

        private static string CharacterHash(PlayerFileData file)
        {
            if (file == null || string.IsNullOrEmpty(file.Path) || file.ServerSideCharacter || Main.ServerSideCharacter)
                throw new InvalidDataException("角色存档无法核验");
            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(FileUtilities.ReadAllBytes(file.Path, file.IsCloudSave)));
        }

        private static bool MatchesCharacter(PlayerFileData file)
            => file?.Player == Main.LocalPlayer && file.IsCloudSave == _characterCloud && string.Equals(file.Path, _characterPath, StringComparison.OrdinalIgnoreCase);

        private static void VerifyCurrentCharacter(PlayerFileData file, string expectedHash)
        {
            if (!MatchesCharacter(file)) throw new InvalidDataException("当前角色与待核验存档不一致");
            Player saved = Player.LoadPlayer(file.Path, file.IsCloudSave)?.Player;
            if (saved == null || saved.loadStatus != StatusID.Ok) throw new InvalidDataException("当前原版角色存档无法读取");
            for (int i = 0; i < 58; i++)
                if (!SameSavedItem(file.Player.inventory[i], saved.inventory[i]))
                    throw new InvalidDataException("当前背包与原版存档不一致，不能自动接纳过期鼠标记录");
            var slots = (Item[])AccessTools.Field(typeof(Player), "_temporaryItemSlots").GetValue(saved);
            if (slots == null || slots.Length == 0 || !SameSavedItem(Main.mouseItem, slots[0]))
                throw new InvalidDataException("当前鼠标物品与原版存档不一致，不能自动接纳过期鼠标记录");
            if (CharacterHash(file) != expectedHash) throw new InvalidDataException("角色存档在核验期间发生变化");
        }

        private static bool SameSavedItem(Item actual, Item saved)
        {
            bool empty = actual == null || actual.IsAir, savedEmpty = saved == null || saved.IsAir;
            return empty == savedEmpty && (empty || (actual.type == saved.type && actual.stack == saved.stack
                && actual.prefix == saved.prefix && actual.favorited == saved.favorited));
        }

        private static void SaveCursor(PlayerFileData file)
        {
            Player saved = Player.LoadPlayer(file.Path, file.IsCloudSave)?.Player;
            if (saved == null) throw new InvalidDataException("无法读回角色库存");
            for (int i = 0; i < 58; i++)
            {
                Item actual = file.Player.inventory[i], persisted = saved.inventory[i];
                bool empty = actual == null || actual.IsAir, savedEmpty = persisted == null || persisted.IsAir;
                if (empty != savedEmpty || (!empty && (actual.type != persisted.type || actual.stack != persisted.stack || actual.prefix != persisted.prefix || actual.favorited != persisted.favorited)))
                    throw new InvalidDataException("角色保存后的库存与确认结果不一致");
            }
            string characterHash = CharacterHash(file);
            Item cursor = Main.mouseItem;
            bool air = cursor == null || cursor.IsAir;
            StoragePersistence.Write(_path + ".cursor", writer =>
            {
                writer.Write(1); writer.Write(characterHash); writer.Write(air ? 0 : cursor.type); writer.Write(air ? 0 : cursor.stack);
                writer.Write(air ? (byte)0 : cursor.prefix); writer.Write(!air && cursor.favorited);
            });
        }

        private static string EncodeInventorySnapshot(Item[] inventory, Item cursor)
        {
            if (inventory == null || inventory.Length < 59) throw new InvalidDataException("角色库存槽位不足");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(59);
                for (int i = 0; i < 58; i++) WriteItem(writer, inventory[i]);
                WriteItem(writer, cursor);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static void WriteItem(BinaryWriter writer, Item item)
        {
            bool empty = item == null || item.IsAir;
            writer.Write(empty ? 0 : item.type);
            writer.Write(empty ? 0 : item.stack);
            writer.Write(empty ? (byte)0 : item.prefix);
            writer.Write(!empty && item.favorited);
        }

        private static void ApplyInventorySnapshot(string encoded)
        {
            using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(encoded))))
            {
                int count = reader.ReadInt32();
                if (count != 59) throw new InvalidDataException("库存快照槽位数不合法");
                for (int i = 0; i < 59; i++)
                {
                    int type = reader.ReadInt32(), stack = reader.ReadInt32();
                    byte prefix = reader.ReadByte(); bool favorite = reader.ReadBoolean();
                    if (type < 0 || type >= ItemID.Count || stack < 0 || stack > 1000000 || (type == 0) != (stack == 0))
                        throw new InvalidDataException("库存快照物品不合法");
                    var item = new Item();
                    if (type > 0) { item.SetDefaults(type); item.Prefix(prefix); item.stack = stack; item.favorited = favorite; }
                    if (i == 58) Main.mouseItem = item;
                    else Main.LocalPlayer.inventory[i] = item;
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("库存快照包含多余数据");
                Main.LocalPlayer.inventory[58] = Main.mouseItem;
            }
        }

        private static void WriteConfirmedRecord()
        {
            StoragePersistence.WriteInPlace(_path, writer =>
            {
                writer.Write(2); writer.Write(true); writer.Write(Pending.World); writer.Write(Pending.Id);
                writer.Write(Pending.Before); writer.Write(Pending.Request); writer.Write(Pending.Confirmed); writer.Write(Pending.After ?? "");
            });
        }

        private static bool CompleteWithoutSave(Item[] inventory, Item cursor)
        {
            try
            {
                Pending.After = EncodeInventorySnapshot(inventory, cursor);
                Pending.Confirmed = true;
                WriteConfirmedRecord();
                Status = "操作已确认；等待角色存档合并保存";
                return true;
            }
            catch (Exception error)
            {
                // 确认记录没有完整写入时不能把库存解锁；否则下一次操作可能覆盖未知结果。
                Pending.Confirmed = false;
                Pending.After = null;
                _canSave = false;
                Status = "确认库存快照保存失败；保留库存锁";
                Mod.LogError(Status, error);
                return false;
            }
        }

        // 终极存储使用此快速确认路径，避免点击响应中同步调用原版整份角色保存。
        internal static bool CompleteClient(Item[] inventory, Item cursor)
        {
            if (!_canSave || Pending == null) return false;
            return CompleteWithoutSave(inventory, cursor);
        }

        internal static bool CompleteClient()
        {
            if (!_canSave || Pending == null) return false;
            // 距上次落盘不足 1.5 秒且连续操作不超过 8 次时合并保存：记录保持到真正保存后再清除，
            // 期间不锁库存；崩溃恢复仍按原有流程向服务器核对结果。
            bool defer = _lastSaveTick != 0 && _opsSinceSave < SaveOpsLimit &&
                unchecked((uint)(Environment.TickCount - _lastSaveTick)) < SaveCoalesceMs;
            if (defer)
            {
                _opsSinceSave++;
                return CompleteWithoutSave(Main.LocalPlayer.inventory, Main.mouseItem);
            }
            try
            {
                _saving = true;
                PlayerFileData file = Main.ActivePlayerFileData;
                if (!MatchesCharacter(file) || file.ServerSideCharacter || Main.ServerSideCharacter || string.IsNullOrEmpty(file.Path))
                    throw new InvalidDataException("当前角色不能持久保存");
                Player.SavePlayer(file, false, false);
                SaveCursor(file);
                StoragePersistence.WriteInPlace(_path, writer => { writer.Write(2); writer.Write(false); });
                Pending = null; _lastSaveTick = Environment.TickCount; _opsSinceSave = 0;
                Status = "确认库存和鼠标物品已保存"; return true;
            }
            catch (Exception error) { Status = "确认库存尚未可靠保存；保留事务日志和库存锁"; Mod.LogError(Status, error); return false; }
            finally { _saving = false; }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SavePlayer), typeof(PlayerFileData), typeof(bool), typeof(bool))]
        internal static class PendingSavePatch
        {
            public static bool Prefix(PlayerFileData __0)
                => Main.netMode == 2 || !MatchesCharacter(__0) || _saving || !ClientLocked;
            public static void Postfix(PlayerFileData __0)
            {
                if (_saving || _path == null || !_canSave || Main.netMode == 2 || !MatchesCharacter(__0)) return;
                try
                {
                    if (Pending != null)
                    {
                        // 未确认的等待恢复流程；已确认的说明本次原版保存已经把结果落盘，可以清除事务记录。
                        if (!Pending.Confirmed) return;
                        SaveCursor(__0);
                        StoragePersistence.WriteInPlace(_path, writer => { writer.Write(2); writer.Write(false); });
                        Pending = null; _lastSaveTick = Environment.TickCount; _opsSinceSave = 0;
                        Status = "确认库存和鼠标物品已保存";
                        return;
                    }
                    SaveCursor(__0);
                }
                catch (Exception error) { _canSave = false; Status = "角色与鼠标物品未能共同保存；保留记录并锁定库存"; Mod.LogError(Status, error); }
            }
        }

        internal static void InitializeServer(string world)
        {
            if (string.IsNullOrEmpty(world)) throw new InvalidDataException("服务器世界身份不明确");
            _serverWorld = world;
            if (!Worlds.ContainsKey(world)) Worlds.Add(world, new ServerRecords());
        }
        internal static void RegisterServerToken(string guid, string token)
        {
            var records = Worlds[_serverWorld];
            HashSet<string> tokens;
            if (!records.Tokens.TryGetValue(guid, out tokens)) { tokens = new HashSet<string>(); records.Tokens.Add(guid, tokens); }
            tokens.Add(token); records.CurrentTokens[guid] = token;
        }
        internal static void RecordServer(string guid, string id, string response)
        {
            var records = Worlds[_serverWorld];
            Dictionary<string, string> replies;
            if (!records.Replies.TryGetValue(guid, out replies)) { replies = new Dictionary<string, string>(); records.Replies.Add(guid, replies); }
            string previous;
            if (replies.TryGetValue(id, out previous) && previous != response) throw new InvalidDataException("同一事务不能改写确认结果");
            replies[id] = response;
        }
        internal static bool TryGetServer(string guid, string id, out string response)
        {
            response = null;
            ServerRecords records; Dictionary<string, string> replies;
            if (_serverWorld == null || !Worlds.TryGetValue(_serverWorld, out records)) return false;
            if (records.Replies.TryGetValue(guid, out replies) && replies.TryGetValue(id, out response)) return true;
            int separator = id?.LastIndexOf('.') ?? -1;
            long sequence; HashSet<string> tokens; string current;
            if (separator <= 0 || !long.TryParse(id.Substring(separator + 1), out sequence) || sequence <= 0) return false;
            string old = id.Substring(0, separator);
            return records.Tokens.TryGetValue(guid, out tokens) && tokens.Contains(old) && records.CurrentTokens.TryGetValue(guid, out current) && current != old;
        }
        // 只在服务器世界真正结束时调用；客户端断线不能清除服务器结果。
        internal static void EndServerSession()
        { if (_serverWorld != null) Worlds.Remove(_serverWorld); _serverWorld = null; }
    }
}
