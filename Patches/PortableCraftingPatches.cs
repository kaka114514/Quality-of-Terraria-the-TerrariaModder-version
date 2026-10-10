using HarmonyLib;
using Terraria;
using Terraria.ID;
using System;
using System.Collections.Generic;
using System.Reflection;
using QualityOfTerraria.UI;
using QualityOfTerraria.Networking;

namespace QualityOfTerraria.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.AdjTiles))]
    public static class PortableCraftingPatches
    {
        public static void Postfix(Player __instance)
        {
            if (__instance == null || SettingsSync.GetPlayerConfig(__instance.whoAmI)?.PortableCraftingStations != true)
                return;

            foreach (var item in InfiniteBuffsPatches.GetAllPlayerItems(__instance))
            {
                if (item == null || item.IsAir)
                    continue;

                // 如果物品可放置为图格，则直接激活该图格作为制作环境
                if (item.createTile >= 0 && item.createTile < __instance.adjTile.Length)
                {
                    __instance.SetAdjTile(item.createTile);
                    if (TileID.Sets.CountsAsWaterForCrafting[item.createTile]) __instance.adjWaterSource = true;
                }

                // 炼金台特殊判定
                if (item.type == ItemID.AlchemyTable)
                {
                    __instance.alchemyTable = true;
                }

                // 液体制作环境支持
                if (item.type == ItemID.WaterBucket || item.type == ItemID.BottomlessBucket)
                {
                    __instance.adjWaterSource = true;
                }
                else if (item.type == ItemID.LavaBucket || item.type == ItemID.BottomlessLavaBucket)
                {
                    __instance.adjLava = true;
                }
                else if (item.type == ItemID.HoneyBucket || item.type == ItemID.BottomlessHoneyBucket)
                {
                    __instance.adjHoney = true;
                }
            }
        }
    }

    // 1.4.5.8 在此列表中同时查找和扣除材料；原版远程合成会等待服务器响应。
    [HarmonyPatch(typeof(Recipe), "CollectItemsFromChests")]
    public static class MarkedChestCraftingPatch
    {
        private static readonly FieldInfo RecipeChests = AccessTools.Field(typeof(Recipe), "_recipeChests");
        private static readonly Action<Item[], int> CollectItems = (Action<Item[], int>)Delegate.CreateDelegate(typeof(Action<Item[], int>), AccessTools.Method(typeof(Recipe), "CollectItems"));
        private static Chest _vault;

        public static void Postfix(Player player)
        {
            if (player == null || player.whoAmI != Main.myPlayer || !UIManager.CanRender) return;
            var chests = (List<Chest>)RecipeChests.GetValue(null);
            Add(chests, player.bank);
            Add(chests, player.bank2);
            Add(chests, player.bank3);
            Add(chests, player.bank4);
            SuperVaultUI.EnsureLoadedForCurrentPlayer();
            if (SuperVaultUI.CanStoreItems)
            {
                if (_vault == null || _vault.item != SuperVaultUI.VaultItems)
                {
                    _vault = Chest.CreateBank(-6);
                    _vault.item = SuperVaultUI.VaultItems;
                    _vault.maxItems = SuperVaultUI.VaultItems.Length;
                }
                Add(chests, _vault);
            }
            foreach (Chest chest in ExtremeStorageUI.GetMarkedReadableChests())
            {
                // 原版可能已加入附近同一个索引的箱子，不能把快照重复计算。
                bool exists = chests.Exists(c => !c.bankChest && c.index == chest.index);
                if (!exists) Add(chests, chest);
            }
        }

        private static void Add(List<Chest> chests, Chest chest)
        {
            if (chest == null || chests.Contains(chest)) return;
            chests.Add(chest);
            CollectItems(chest.item, chest.item.Length);
        }
    }

    [HarmonyPatch(typeof(Terraria.GameContent.CraftingRequests), "HandleResponse")]
    public static class RemoteCraftingSnapshotPatch
    {
        public static void Postfix()
        {
            RemoteStorageSnapshots.Invalidate();
        }
    }

    [HarmonyPatch(typeof(Terraria.GameContent.CraftingRequests), "CraftItem")]
    public static class VaultCraftingSavePatch
    {
        public static void Postfix()
        {
            if (Main.netMode != 2) SuperVaultUI.SaveVaultData();
        }
    }
}
