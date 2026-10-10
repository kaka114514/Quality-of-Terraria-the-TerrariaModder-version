using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using QualityOfTerraria.Networking;
using QualityOfTerraria.Patches;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;

namespace QualityOfTerraria.Features
{
    internal static class VeinMiningSystem
    {
        internal static bool IsForced { get; set; }
        internal static bool Suspended { get; set; }
        private const int FrameBudget = 32;
        private static readonly Queue<Job> Jobs = new Queue<Job>();
        private delegate int PickDamage(Player player, int x, int y, int pick, int index, Tile tile);
        private static readonly PickDamage GetDamage = AccessTools.MethodDelegate<PickDamage>(AccessTools.Method(typeof(Player), "GetPickaxeDamage"));
        private static readonly Action<Player, WorldItem> Pickup = AccessTools.MethodDelegate<Action<Player, WorldItem>>(AccessTools.Method(typeof(Player), "PickupItem"));
        [ThreadStatic] private static DropContext _drops;

        private sealed class Job
        {
            internal Player Player;
            internal Tile[,] World;
            internal int X, Y, Range, Limit, Count = 1;
            internal ushort Type;
            internal int LastDropItemType;
            internal bool Exact, CheckPower, AutoLoot, ShowMessages;
            internal readonly Queue<Point> Pending = new Queue<Point>();
            internal readonly HashSet<Point> Visited = new HashSet<Point>();
        }
        private sealed class DropContext
        {
            internal Player Player;
            internal int X, Y;
            internal Job Job;
            internal bool AutoLoot;
        }

        internal static bool IsOre(ushort type) => type < TileID.Sets.Ore.Length && TileID.Sets.Ore[type];

        internal static bool CanMine(Player player, int x, int y, QualityOfTerrariaConfig config)
            => config != null && CanMine(player, x, y, config.VeinMiningCheckPickaxePower);

        internal static bool CanMineOrigin(Player player, int x, int y) => CanMine(player, x, y, true);

        private static bool CanMine(Player player, int x, int y, bool checkPower)
        {
            if (player == null || !player.active || player.dead || !WorldGen.InWorld(x, y, 1)) return false;
            Tile tile = Main.tile[x, y];
            Item tool = player.HeldItem;
            if (tile == null || !tile.active() || !IsOre(tile.type) || tool == null || tool.pick <= 0 || !WorldGen.CanKillTile(x, y)) return false;
            return !checkPower || GetDamage(player, x, y, tool.pick, 0, tile) > 0;
        }

        internal static bool Start(Player player, int x, int y, ushort oreType, QualityOfTerrariaConfig config)
        {
            // 调用者已经核验原版成功破矿与个人开关/强制键；客户端绝不执行地形修改。
            if (Suspended || Main.netMode == 1 || !Mod.WorldActive || Main.gameMenu || player == null || !player.active || player.dead || config == null || !IsOre(oreType) || !WorldGen.InWorld(x, y, 1)) return false;
            if (player.whoAmI < 0 || player.whoAmI >= Main.player.Length || Main.player[player.whoAmI] != player || player.HeldItem == null || player.HeldItem.pick <= 0) return false;
            foreach (Job active in Jobs) if (active.Player == player) return false;
            Job job = new Job
            {
                Player = player, World = Main.tile, X = x, Y = y, Type = oreType,
                Range = Math.Max(1, Math.Min(30, config.VeinMiningRange)), Limit = Math.Max(1, Math.Min(500, config.VeinMiningMaxBlocks)),
                Exact = config.VeinMiningMatchExactType, CheckPower = config.VeinMiningCheckPickaxePower,
                AutoLoot = config.VeinMiningAutoLoot, ShowMessages = config.VeinMiningShowMessages
            };
            job.Visited.Add(new Point(x, y));
            AddNeighbors(job, x, y);
            Jobs.Enqueue(job);
            return true;
        }

        private static void AddNeighbors(Job job, int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Point point = new Point(x + dx, y + dy);
                    if (Math.Abs(point.X - job.X) > job.Range || Math.Abs(point.Y - job.Y) > job.Range || !WorldGen.InWorld(point.X, point.Y, 1)) continue;
                    if (job.Visited.Add(point)) job.Pending.Enqueue(point);
                }
        }

        internal static void Update()
        {
            if (Suspended || !Mod.WorldActive || Main.gameMenu) { ResetSession(); return; }
            if (Main.netMode == 1) return;
            HashSet<Point> changed = new HashSet<Point>();
            int budget = FrameBudget;
            while (Jobs.Count > 0 && budget > 0)
            {
                Job job = Jobs.Dequeue();
                Player player = job.Player;
                if (job.World != Main.tile || !player.active || player.dead || Main.player[player.whoAmI] != player || player.HeldItem == null || player.HeldItem.pick <= 0) continue;
                try
                {
                    int slice = Math.Min(8, budget);
                    while (slice-- > 0 && job.Pending.Count > 0 && job.Count < job.Limit)
                    {
                        budget--;
                        Point point = job.Pending.Dequeue();
                        Tile tile = Main.tile[point.X, point.Y];
                        if (tile == null || !tile.active() || !IsOre(tile.type) || (job.Exact && tile.type != job.Type) || !WorldGen.CanKillTile(point.X, point.Y)) continue;
                        if (job.CheckPower && GetDamage(player, point.X, point.Y, player.HeldItem.pick, 0, tile) <= 0) continue;
                        DropContext previous = _drops;
                        _drops = new DropContext { Player = player, X = point.X, Y = point.Y, Job = job, AutoLoot = job.AutoLoot };
                        try { WorldGen.KillTile(point.X, point.Y, false, false, false); }
                        finally { _drops = previous; }
                        Tile after = Main.tile[point.X, point.Y];
                        if (after != null && after.active()) continue;
                        job.Count++;
                        changed.Add(new Point(point.X / 20, point.Y / 20));
                        AddNeighbors(job, point.X, point.Y);
                    }
                    if (job.Pending.Count > 0 && job.Count < job.Limit) Jobs.Enqueue(job);
                    else if (job.ShowMessages && Main.netMode == 0 && job.Count > 1)
                    {
                        string oreName = job.LastDropItemType > 0 ? Lang.GetItemNameValue(job.LastDropItemType) : null;
                        if (string.IsNullOrEmpty(oreName)) oreName = "矿石";
                        Main.NewText("【连锁挖矿】连锁挖掉 " + job.Count + " 个" + oreName, 100, 240, 255);
                    }
                }
                catch (Exception exception) { Mod.LogError("连锁挖矿任务已停止。", exception); }
            }
            if (Main.netMode == 2)
                foreach (Point chunk in changed)
                    NetMessage.SendTileSquare(-1, chunk.X * 20, chunk.Y * 20, 20, 20);
        }

        internal static object BeginDrops(Player player, int x, int y)
        {
            DropContext previous = _drops;
            QualityOfTerrariaConfig config = player == null ? null : SettingsSync.GetPlayerConfig(player.whoAmI);
            if (!Suspended && config != null && config.VeinMiningAutoLoot && player.active && !player.dead)
                _drops = new DropContext { Player = player, X = x, Y = y, AutoLoot = true };
            return previous;
        }

        internal static void EndDrops(object previous) => _drops = previous as DropContext;

        internal static void ReceiveDrop(IEntitySource source, int index)
        {
            DropContext context = _drops;
            EntitySource_TileBreak tileSource = source as EntitySource_TileBreak;
            if (context == null || tileSource == null || tileSource.TileCoords.X != context.X || tileSource.TileCoords.Y != context.Y || index < 0 || index >= Main.item.Length) return;
            Player player = context.Player;
            WorldItem item = Main.item[index];
            if (item == null || !item.active) return;
            if (context.Job != null && context.Job.LastDropItemType == 0) context.Job.LastDropItemType = item.type;
            if (!context.AutoLoot || !player.active || player.dead || !player.ItemSpace(item).CanTakeItem) return;
            if (Main.netMode == 0)
            {
                if (!StorageInventoryGuard.Locked) Pickup(player, item);
            }
            else if (Main.netMode == 2)
            {
                int owner = item.playerIndexTheItemIsReservedFor;
                if (owner != 255 && owner != player.whoAmI) return;
                item.Center = player.Center;
                item.SyncItem();
                if (owner == 255) item.ReserveFor(player.whoAmI, 300);
            }
        }

        internal static void Toggle()
        {
            if (Suspended || !Mod.WorldActive || Main.gameMenu || Main.netMode == 2 || Mod.Config == null) return;
            bool previous = Mod.Config.VeinMiningEnabled;
            Mod.Config.VeinMiningEnabled = !previous;
            string error;
            if (!SettingsSync.SaveVerified(Mod.Config, out error)) { Mod.Config.VeinMiningEnabled = previous; Main.NewText("连锁挖矿设置保存失败：" + error, 255, 120, 100); return; }
            if (Mod.Config.VeinMiningShowMessages) Main.NewText("【连锁挖矿】" + (Mod.Config.VeinMiningEnabled ? "已启用" : "已关闭"), 100, 240, 255);
        }

        internal static void ResetSession() { Jobs.Clear(); _drops = null; IsForced = false; }
    }
}
