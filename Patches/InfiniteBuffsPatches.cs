using System;
using System.Collections.Generic;
using HarmonyLib;
using Terraria;
using Terraria.ID;
using Terraria.GameContent;
using QualityOfTerraria.Networking;

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

            var config = SettingsSync.GetPlayerConfig(__instance.whoAmI);
            if (config == null) return;
            bool infinitePotions = config.InfinitePotions;
            bool infiniteStations = config.InfiniteStationsAndBanners;
            int req = config.PotionStackRequirement;

            if (!infinitePotions && !infiniteStations)
                return;

            var potionCounts = new Dictionary<int, int>();
            foreach (var item in GetAllPlayerItems(__instance))
            {
                if (item == null || item.IsAir)
                    continue;

                // 1. 无限药水
                if (infinitePotions && item.type != ItemID.RedPotion && !IsPotionBlacklisted(config, item.type) &&
                    item.buffType > 0 && !Main.debuff[item.buffType] && item.consumable)
                {
                    int previous;
                    potionCounts.TryGetValue(item.type, out previous);
                    int total = previous + System.Math.Min(item.stack, req);
                    potionCounts[item.type] = total;
                    // 同种药水跨格、跨箱累计；每轮达到门槛时只刷新一次。
                    if (previous < req && total >= req) __instance.AddBuff(item.buffType, 120, false);
                }

                if (infinitePotions && item.type == ItemID.RedPotion && !IsPotionBlacklisted(config, item.type))
                {
                    int previous;
                    potionCounts.TryGetValue(item.type, out previous);
                    int total = previous + System.Math.Min(item.stack, config.RedPotionRequirement);
                    potionCounts[item.type] = total;
                    if (previous < config.RedPotionRequirement)
                        RedPotionPatches.ApplyInfiniteBuffsForPlayer(__instance, item, config, total);
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

        internal static bool IsPotionBlacklisted(int itemType)
        {
            return IsPotionBlacklisted(Mod.Config, itemType);
        }

        internal static bool IsPotionBlacklisted(QualityOfTerrariaConfig config, int itemType)
        {
            if (config?.InfinitePotionBlacklist == null) return false;
            return Array.IndexOf(config.InfinitePotionBlacklist, itemType) >= 0;
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

            foreach (Chest chest in RemoteStorageOperations.GetMarkedReadableChests(player))
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
            if (__result || SettingsSync.GetPlayerConfig(__instance.whoAmI)?.InfiniteStationsAndBanners != true || bannerType <= 0 || bannerType >= BannerSystem.MaxBannerTypes) return;
            int itemType = BannerSystem.BannerToItem(bannerType);
            foreach (Item item in InfiniteBuffsPatches.GetAllPlayerItems(__instance))
                if (item.type == itemType) { __result = true; return; }
        }
    }
}
