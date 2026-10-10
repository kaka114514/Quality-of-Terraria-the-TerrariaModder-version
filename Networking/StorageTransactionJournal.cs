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
            internal string World, Id, Before, Request;
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
        internal static ClientRecord Pending { get; private set; }
        internal static bool ClientLocked => _character != null && (!_canSave || Pending != null);
        internal static string Status { get; private set; } = "";

        internal static void InitializeClient(string characterIdentity)
        {
            _character = characterIdentity; Pending = null; _canSave = false;
            _characterPath = Main.ActivePlayerFileData?.Path;
            _characterCloud = Main.ActivePlayerFileData?.IsCloudSave == true;
            if (string.IsNullOrEmpty(characterIdentity)) { Status = "无法确认角色身份，未发送物品操作"; return; }
            _path = Path.Combine(Main.SavePath, "TerrariaModder", "player-data", StoragePersistence.Identity(characterIdentity) + ".storage-transaction.dat");
            _canSave = !File.Exists(_path) && !File.Exists(_path + ".bak");
            ClientRecord loaded;
            if (StoragePersistence.Read(_path, reader =>
            {
                if (reader.ReadInt32() != 1) throw new InvalidDataException("事务日志版本不支持");
                if (!reader.ReadBoolean()) return null;
                var record = new ClientRecord { World = reader.ReadString(), Id = reader.ReadString(), Before = reader.ReadString(), Request = reader.ReadString() };
                Validate(record); return record;
            }, out loaded)) { Pending = loaded; _canSave = true; }
            if (!_canSave) { Status = "事务日志主文件与备份无法读取；库存保持锁定，请保留日志"; return; }
            if (Pending != null) { Status = "有未确认的物品操作；返回原服务器等待恢复，未知结果不能自动取消"; return; }
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
                if (cursor.CharacterHash != CharacterHash(Main.ActivePlayerFileData)) throw new InvalidDataException("角色存档与鼠标物品记录不一致");
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
        }

        internal static bool BeginClient(string world, string id, string beforeBase64, string requestBase64)
        {
            if (!_canSave || _path == null || Pending != null) { Status = "事务日志尚未恢复，不能开始新的物品操作"; return false; }
            try
            {
                var record = new ClientRecord { World = world, Id = id, Before = beforeBase64, Request = requestBase64 };
                Validate(record);
                StoragePersistence.Write(_path, writer =>
                {
                    writer.Write(1); writer.Write(true); writer.Write(record.World); writer.Write(record.Id); writer.Write(record.Before); writer.Write(record.Request);
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

        internal static bool CompleteClient()
        {
            if (!_canSave || Pending == null) return false;
            try
            {
                _saving = true;
                PlayerFileData file = Main.ActivePlayerFileData;
                if (!MatchesCharacter(file) || file.ServerSideCharacter || Main.ServerSideCharacter || string.IsNullOrEmpty(file.Path))
                    throw new InvalidDataException("当前角色不能持久保存");
                Player.SavePlayer(file, false, false);
                SaveCursor(file);
                // 完成标记也原子写入；不要先删除文件，让旧备份在下次启动时复活。
                StoragePersistence.Write(_path, writer => { writer.Write(1); writer.Write(false); });
                Pending = null; Status = "确认库存和鼠标物品已保存"; return true;
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
                if (_saving || _path == null || !_canSave || Pending != null || Main.netMode == 2 || !MatchesCharacter(__0)) return;
                try { SaveCursor(__0); }
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
