using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using QualityOfTerraria.UI;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;

namespace QualityOfTerraria.Features
{
    public static class Autofisher
    {
        public static bool IsEnabled = false;
        private static int _timer;
        public static int TotalCatches;
        public static string LastCatchName = "暂无";
        public static string Status = "已关闭";
        [ThreadStatic] private static Player _receivingPlayer;
        private delegate bool ConsumeBait(Player player, Projectile bobber, out int baitType);
        private static readonly ConsumeBait Consume = AccessTools.MethodDelegate<ConsumeBait>(AccessTools.Method(typeof(Player), "ItemCheck_CheckFishingBobber_ConsumeBait"));
        private static readonly Action<Player, Projectile, int> Pull = AccessTools.MethodDelegate<Action<Player, Projectile, int>>(AccessTools.Method(typeof(Player), "ItemCheck_CheckFishingBobber_PullBobber"));
        private static readonly Action<Projectile, Player, int> GiveItem = AccessTools.MethodDelegate<Action<Projectile, Player, int>>(AccessTools.Method(typeof(Projectile), "AI_061_FishingBobber_GiveItemToPlayer"));

        public static void Toggle()
        {
            if (Main.gameMenu || Main.netMode == 2) return;
            IsEnabled = !IsEnabled;
            _timer = 0;
            Status = IsEnabled ? "等待原版钓鱼结算" : "已关闭";
            SoundEngine.PlaySound(IsEnabled ? SoundID.MenuOpen : SoundID.MenuClose);
            Main.NewText(IsEnabled ? "【自动钓鱼机】已启动，每" + IntervalSeconds + "秒尝试一次。需要鱼竿、鱼饵及有效水体。" : "【自动钓鱼机】已关闭。", 100, 240, 255);
        }

        public static void ResetSession()
        {
            IsEnabled = false;
            _timer = 0;
            TotalCatches = 0;
            LastCatchName = "暂无";
            Status = "已关闭";
            _receivingPlayer = null;
        }

        public static void Update()
        {
            if (!IsEnabled || Main.gameMenu || Main.netMode == 2) return;
            Player player = Main.LocalPlayer;
            if (player == null || !player.active || player.dead) return;
            if (++_timer < IntervalFrames) return;
            _timer = 0;
            PlayerFishingConditions conditions = player.GetFishingConditions();
            if (conditions.PoleItemType <= 0 || conditions.BaitItemType <= 0 || conditions.FinalFishingLevel <= 0)
            { SetStatus("缺少可用鱼竿或鱼饵"); return; }
            Item pole = null;
            foreach (Item item in player.inventory)
                if (item != null && !item.IsAir && item.type == conditions.PoleItemType) { pole = item; break; }
            if (pole == null || pole.shoot <= 0) { SetStatus("鱼竿没有有效浮标"); return; }
            Point? pool = FindPool(player);
            if (!pool.HasValue) { SetStatus("附近没有可垂钓水体"); return; }

            // 临时浮标不加入世界弹幕数组；只执行一次原版钓鱼判定，避免自动 AI 再次结算。
            Projectile bobber = new Projectile();
            bobber.SetDefaults(pole.shoot);
            bobber.owner = player.whoAmI;
            bobber.Center = new Vector2(pool.Value.X * 16 + 8, pool.Value.Y * 16 + 8);
            bobber.wet = true;
            bobber.FishingCheck();
            if (bobber.localAI[1] == 0f) { SetStatus("本次未钓获（原版水池、群落与渔力判定）"); return; }
            int baitType;
            if (!Consume(player, bobber, out baitType)) { SetStatus("原版鱼饵检查未通过"); return; }
            bobber.ai[0] = 1f;
            Pull(player, bobber, baitType);
            if (bobber.localAI[1] < 0f || baitType == ItemID.TruffleWorm)
            { SetStatus("已按原版规则钓出敌怪"); return; }
            if (bobber.ai[0] == 2f || bobber.ai[1] <= 0f) { SetStatus("原版钓线断裂，本次无渔获"); return; }
            _receivingPlayer = player;
            try { GiveItem(bobber, player, (int)bobber.ai[1]); }
            finally { _receivingPlayer = null; }
        }

        private static int IntervalSeconds => Math.Max(1, Math.Min(60, Mod.Config?.AutoFishingIntervalSeconds ?? 10));
        private static int IntervalFrames => IntervalSeconds * 60;

        private static void SetStatus(string value)
        {
            if (Status == value) return;
            Status = value;
            Main.NewText("【自动钓鱼机】" + value, 100, 240, 255);
        }

        private static Point? FindPool(Player player)
        {
            int px = (int)(player.Center.X / 16), py = (int)(player.Center.Y / 16);
            Point? result = null;
            int closest = int.MaxValue;
            for (int dx = -25; dx <= 25; dx++)
                for (int dy = -10; dy <= 25; dy++)
                {
                    int x = px + dx, y = py + dy;
                    if (!WorldGen.InWorld(x, y, 2)) continue;
                    Tile tile = Main.tile[x, y];
                    if (tile == null || tile.liquid <= 120 || tile.liquidType() == 3 || (tile.active() && Main.tileSolid[tile.type])) continue;
                    int distance = dx * dx + dy * dy;
                    if (distance < closest) { closest = distance; result = new Point(x, y); }
                }
            return result;
        }

        public static void StoreFishingCatch(Player player, IEntitySource source, Item item)
        {
            if (_receivingPlayer != player) { player.QuickSpawnItem(source, item); return; }
            string name = item.Name;
            // 存储模块只允许权威箱子写入；客户端远程箱子不会被本地修改。
            ExtremeStorageUI.PutItemIntoNearbyChest(item, player, 25f);
            Item remaining = item.IsAir ? item : player.GetItem(item, GetItemSettings.PickupItemFromWorld);
            if (remaining != null && !remaining.IsAir)
            {
                SuperVaultUI.EnsureLoadedForCurrentPlayer();
                if (SuperVaultUI.CanStoreItems)
                {
                    bool vaultChanged = false;
                    foreach (Item slot in SuperVaultUI.VaultItems)
                    {
                        if (slot == null || slot.IsAir || slot.type != remaining.type || slot.prefix != remaining.prefix || slot.stack >= slot.maxStack) continue;
                        int moved = Math.Min(remaining.stack, slot.maxStack - slot.stack);
                        slot.stack += moved;
                        remaining.stack -= moved;
                        if (moved > 0) vaultChanged = true;
                        if (remaining.stack <= 0) break;
                    }
                    if (remaining.stack > 0)
                        for (int i = 0; i < SuperVaultUI.VaultItems.Length; i++)
                            if (SuperVaultUI.VaultItems[i] == null || SuperVaultUI.VaultItems[i].IsAir)
                            { SuperVaultUI.VaultItems[i] = remaining.Clone(); remaining.TurnToAir(); vaultChanged = true; break; }
                    if (vaultChanged) SuperVaultUI.SaveVaultData();
                }
                if (!remaining.IsAir) player.QuickSpawnItem(source, remaining);
            }
            TotalCatches++;
            LastCatchName = name;
            Status = "已钓获 " + name;
            SoundEngine.PlaySound(SoundID.Splash);
        }

        [HarmonyPatch(typeof(Projectile), "AI_061_FishingBobber_GiveItemToPlayer")]
        public static class FishingStoragePatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo give = AccessTools.Method(typeof(Player), "QuickSpawnItem", new[] { typeof(IEntitySource), typeof(Item) });
                MethodInfo store = AccessTools.Method(typeof(Autofisher), nameof(StoreFishingCatch));
                bool found = false;
                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.Calls(give)) { instruction.opcode = OpCodes.Call; instruction.operand = store; found = true; }
                    yield return instruction;
                }
                if (!found) throw new InvalidOperationException("未找到原版钓鱼给予物品入口，停止应用自动收纳补丁。");
            }
        }
    }
}
