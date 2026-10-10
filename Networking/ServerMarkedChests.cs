using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using TerrariaModder.Core.Permissions;

namespace QualityOfTerraria.Networking
{
    internal static class ServerMarkedChests
    {
        private sealed class Marks
        {
            internal readonly HashSet<long> Keys = new HashSet<long>();
            internal string Path;
            internal bool CanSave;
        }
        private static readonly Dictionary<string, Marks> Players = new Dictionary<string, Marks>();

        internal static long Key(Chest chest) => ((long)chest.x << 32) | (uint)chest.y;
        internal static string WorldIdentity => Main.ActiveWorldFileData?.UniqueId.ToString("N");

        private static Marks Get(int player)
        {
            string identity = PermissionService.GetGuid(player);
            string world = WorldIdentity;
            if (string.IsNullOrEmpty(identity) || string.IsNullOrEmpty(world) || world == Guid.Empty.ToString("N")) return null;
            string key = world + ":" + identity;
            Marks marks;
            if (Players.TryGetValue(key, out marks)) return marks;
            marks = new Marks { Path = Path.Combine(Main.SavePath, "TerrariaModder", "world-data", world,
                StoragePersistence.Identity(identity) + ".player-markedchests.dat") };
            marks.CanSave = !File.Exists(marks.Path) && !File.Exists(marks.Path + ".bak");
            HashSet<long> loaded;
            if (StoragePersistence.Read(marks.Path, reader =>
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > Main.maxChests) throw new InvalidDataException("玩家箱子标记数量不合法");
                var keys = new HashSet<long>();
                for (int i = 0; i < count; i++)
                {
                    long coordinate = reader.ReadInt64();
                    if (!ValidCoordinate(coordinate)) throw new InvalidDataException("玩家箱子标记坐标不合法");
                    keys.Add(coordinate);
                }
                return keys;
            }, out loaded)) { marks.Keys.UnionWith(loaded); marks.CanSave = true; }
            Players.Add(key, marks);
            return marks;
        }

        internal static bool ValidCoordinate(long key)
        {
            int x = (int)(key >> 32), y = (int)key;
            return x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY;
        }

        internal static HashSet<long> GetKeys(int player) => Get(player)?.Keys;
        internal static bool IsMarked(int player, Chest chest) => chest != null && GetKeys(player)?.Contains(Key(chest)) == true;

        internal static bool Change(int player, long key, bool marked)
        {
            return ChangeBatch(player, new[] { new KeyValuePair<long, bool>(key, marked) }).Count == 0;
        }

        internal static List<long> ChangeBatch(int player, IEnumerable<KeyValuePair<long, bool>> changes)
        {
            var rejected = new List<long>();
            Marks marks = Get(player);
            var next = marks == null ? new HashSet<long>() : new HashSet<long>(marks.Keys);
            var accepted = new List<long>();
            foreach (var change in changes)
            {
                if (marks == null || !marks.CanSave || !ValidCoordinate(change.Key) ||
                    (change.Value && (Chest.FindChest((int)(change.Key >> 32), (int)change.Key) < 0 || (next.Count >= Main.maxChests && !next.Contains(change.Key)))))
                { rejected.Add(change.Key); continue; }
                if (change.Value) next.Add(change.Key); else next.Remove(change.Key);
                accepted.Add(change.Key);
            }
            if (marks == null || next.SetEquals(marks.Keys)) return rejected;
            try
            {
                StoragePersistence.Write(marks.Path, writer =>
                {
                    writer.Write(next.Count);
                    foreach (long coordinate in next) writer.Write(coordinate);
                });
                marks.Keys.Clear(); marks.Keys.UnionWith(next);
            }
            catch (Exception error)
            { Mod.LogError("保存玩家箱子标记失败", error); rejected.AddRange(accepted); }
            return rejected;
        }

        internal static IEnumerable<Chest> GetReadable(int player)
        {
            HashSet<long> marks = GetKeys(player);
            if (marks == null) yield break;
            foreach (long key in marks)
            {
                int index = Chest.FindChest((int)(key >> 32), (int)key);
                if (CanAccess(player, index)) yield return Main.chest[index];
            }
        }

        internal static bool CanAccess(int player, int index)
        {
            if (index < 0 || index >= Main.maxChests || Main.chest == null) return false;
            Chest chest = Main.chest[index];
            if (chest == null || chest.item == null || !IsMarked(player, chest) || Chest.IsLocked(chest.x, chest.y)) return false;
            int owner = Chest.UsingChest(index);
            return owner < 0 || owner == player;
        }

        internal static void ResetSession() => Players.Clear();
    }
}
