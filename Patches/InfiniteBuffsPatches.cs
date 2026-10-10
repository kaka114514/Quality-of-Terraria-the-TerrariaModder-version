using System.Collections.Generic;
using HarmonyLib;
using Terraria;
using Terraria.ID;
using Terraria.GameContent;

namespace QualityOfTerraria.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.UpdateBuffs))]
    public static class InfiniteBuffsPatches
    {
        public static void Postfix(Player __instance, int i)
        {
            if (Mod.Config == null || __instance == null || !__instance.active)
                return;

            // Only run for the local client player or server instances (netMode == 2 is dedicated server)
            if (__instance.whoAmI != Main.myPlayer && Main.netMode != 2)
                return;

            bool infinitePotions = Mod.Config.InfinitePotions;
            bool infiniteStations = Mod.Config.InfiniteStationsAndBanners;
            int req = Mod.Config.PotionStackRequirement;

            if (!infinitePotions && !infiniteStations)
                return;

            foreach (var item in GetAllPlayerItems(__instance))
            {
                if (item == null || item.IsAir)
                    continue;

                // 1. 无限药水
                if (infinitePotions && item.buffType > 0 && item.consumable && item.stack >= req)
                {
                    // 给玩家施加 2 秒增益，每帧刷新
                    __instance.AddBuff(item.buffType, 120, false);
                }

                // 2. 随身增益站与旗帜
                if (infiniteStations)
                {
                    switch (item.type)
                    {
                        case ItemID.BewitchingTable:
                            __instance.AddBuff(BuffID.Bewitched, 120, false);
                            break;
                        case ItemID.SharpeningStation:
                            __instance.AddBuff(BuffID.Sharpened, 120, false);
                            break;
                        case ItemID.AmmoBox:
                            __instance.AddBuff(BuffID.AmmoBox, 120, false);
                            break;
                        case ItemID.CrystalBall:
                            __instance.AddBuff(BuffID.Clairvoyance, 120, false);
                            break;
                        case ItemID.SliceOfCake:
                            __instance.AddBuff(BuffID.SugarRush, 120, false);
                            break;
                        case ItemID.WarTable:
                            __instance.AddBuff(BuffID.WarTable, 120, false);
                            break;
                        case ItemID.CatBast:
                            __instance.AddBuff(BuffID.CatBast, 120, false);
                            break;
                        case ItemID.HeartLantern:
                            __instance.AddBuff(BuffID.HeartLamp, 120, false);
                            break;
                        case ItemID.StarinaBottle:
                            __instance.AddBuff(BuffID.StarInBottle, 120, false);
                            break;
                    }

                    if (item.createTile == TileID.Campfire)
                    {
                        __instance.AddBuff(BuffID.Campfire, 120, false);
                    }

                    if (item.createTile == TileID.Banners)
                    {
                        __instance.AddBuff(BuffID.MonsterBanner, 120, false);
                    }
                }
            }
        }

        public static IEnumerable<Item> GetAllPlayerItems(Player player)
        {
            if (player.inventory != null)
            {
                for (int i = 0; i < System.Math.Min(58, player.inventory.Length); i++)
                {
                    if (player.inventory[i] != null && !player.inventory[i].IsAir)
                        yield return player.inventory[i];
                }
            }

            if (player.bank?.item != null)
            {
                for (int i = 0; i < player.bank.item.Length; i++)
                {
                    if (player.bank.item[i] != null && !player.bank.item[i].IsAir)
                        yield return player.bank.item[i];
                }
            }

            if (player.bank2?.item != null)
            {
                for (int i = 0; i < player.bank2.item.Length; i++)
                {
                    if (player.bank2.item[i] != null && !player.bank2.item[i].IsAir)
                        yield return player.bank2.item[i];
                }
            }

            if (player.bank3?.item != null)
            {
                for (int i = 0; i < player.bank3.item.Length; i++)
                {
                    if (player.bank3.item[i] != null && !player.bank3.item[i].IsAir)
                        yield return player.bank3.item[i];
                }
            }

            if (player.bank4?.item != null)
            {
                for (int i = 0; i < player.bank4.item.Length; i++)
                {
                    if (player.bank4.item[i] != null && !player.bank4.item[i].IsAir)
                        yield return player.bank4.item[i];
                }
            }

            if (player.whoAmI == Main.myPlayer && Main.netMode != 2)
            {
                QualityOfTerraria.UI.SuperVaultUI.EnsureLoadedForCurrentPlayer();
                for (int i = 0; QualityOfTerraria.UI.SuperVaultUI.CanStoreItems && i < QualityOfTerraria.UI.SuperVaultUI.VaultItems.Length; i++)
                {
                    Item vItem = QualityOfTerraria.UI.SuperVaultUI.VaultItems[i];
                    if (vItem != null && !vItem.IsAir)
                        yield return vItem;
                }
            }

            foreach (Chest chest in QualityOfTerraria.UI.ExtremeStorageUI.GetMarkedReadableChests())
                foreach (Item item in chest.item)
                    if (item != null && !item.IsAir) yield return item;
        }
    }

    // MonsterBanner 图标自身不会设置对应怪物的伤害加成。
    [HarmonyPatch(typeof(Player), nameof(Player.HasNPCBannerBuff))]
    public static class PortableBannerEffectPatch
    {
        public static void Postfix(Player __instance, int bannerType, ref bool __result)
        {
            if (__result || Mod.Config?.InfiniteStationsAndBanners != true || bannerType <= 0 || bannerType >= BannerSystem.MaxBannerTypes) return;
            int itemType = BannerSystem.BannerToItem(bannerType);
            foreach (Item item in InfiniteBuffsPatches.GetAllPlayerItems(__instance))
                if (item.type == itemType) { __result = true; return; }
        }
    }
}
