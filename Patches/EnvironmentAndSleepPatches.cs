using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.GameContent;
using Terraria.ID;

namespace QualityOfTerraria.Patches
{
    public static class EnvironmentAndSleepPatches
    {
        // 1. 无视睡觉限制 (怪物、白天、Boss战均可安睡)
        [HarmonyPatch(typeof(PlayerSleepingHelper), "DoesPlayerHaveReasonToActUpInBed")]
        public static class SleepRestrictionsPatch
        {
            public static bool Prefix(Player __0, ref bool __result)
            {
                var config = __0 == null ? null : Networking.SettingsSync.GetPlayerConfig(__0.whoAmI);
                if (config != null && config.NoSleepRestrictions)
                {
                    __result = false;
                    return false; // 直接允许安睡
                }
                return true;
            }
        }

        // 2. 睡觉时间流速大幅加速 (默认提升至 20 倍)
        [HarmonyPatch(typeof(Main), "UpdateTimeRate")]
        public static class BedTimeRatePatch
        {
            public static void Postfix()
            {
                if (Mod.Config != null && Mod.Config.BedTimeRate > 5)
                {
                    if (Main.CurrentFrameFlags.SleepingPlayersCount > 0)
                    {
                        Main.dayRate = Mod.Config.BedTimeRate;
                    }
                }
            }
        }

        // 3. 摇树必定掉落群落水果
        [HarmonyPatch(typeof(WorldGen), "ShakeTree")]
        public static class ShakeTreeFruitPatch
        {
            public static void Postfix(int i, int j)
            {
                if (Main.netMode == 1 || Mod.Config == null || !Mod.Config.ShakeTreeAlwaysFruit)
                    return;

                WorldGen.GetTreeBottom(i, j, out int x, out int y);
                TreeTypes treeType = WorldGen.GetTreeType(Main.tile[x, y].type);
                if (treeType == TreeTypes.None)
                    return;

                y--;
                while (y > 10 && Main.tile[x, y].active() && TileID.Sets.IsShakeable[Main.tile[x, y].type])
                {
                    y--;
                }

                y++;
                int fruit = -1;
                int[] c = null;
                switch (treeType)
                {
                    case TreeTypes.Forest:
                        c = new int[] { ItemID.Apple, ItemID.Apricot, ItemID.Grapefruit, ItemID.Lemon, ItemID.Peach };
                        break;
                    case TreeTypes.Jungle:
                        c = new int[] { ItemID.Mango, ItemID.Pineapple };
                        break;
                    case TreeTypes.Snow:
                        c = new int[] { ItemID.Plum, ItemID.Cherry };
                        break;
                    case TreeTypes.Hallowed:
                        c = new int[] { ItemID.Dragonfruit, ItemID.Starfruit };
                        break;
                    case TreeTypes.Corrupt:
                        c = new int[] { ItemID.BlackCurrant, ItemID.Elderberry };
                        break;
                    case TreeTypes.Crimson:
                        c = new int[] { ItemID.BloodOrange, ItemID.Rambutan };
                        break;
                    case TreeTypes.Palm:
                        c = new int[] { ItemID.Coconut, ItemID.Banana };
                        break;
                }

                if (c != null && c.Length > 0)
                {
                    fruit = c[Main.rand.Next(c.Length)];
#pragma warning disable CS0618 // 类型或成员已过时
                    Item.NewItem(WorldGen.GetItemSource_FromTreeShake(x, y), x * 16, y * 16, 16, 16, fruit);
#pragma warning restore CS0618
                }
            }
        }

        // 4. 宝石树砍伐必定掉落宝石
        [HarmonyPatch(typeof(WorldGen), "SetGemTreeDrops")]
        public static class GemTreeDropsPatch
        {
            public static void Postfix(int gemType, int seedType, Tile tileCache, ref int dropItem, ref int secondaryItem)
            {
                if (Main.netMode != 1 && Mod.Config != null && Mod.Config.GemTreeAlwaysGem)
                {
                    dropItem = gemType;
                    secondaryItem = seedType;
                }
            }
        }

        // 5. 南瓜 5 倍速生长 (严格防重入，彻底杜绝自调用无限递归引发 StackOverflowException)
        [HarmonyPatch(typeof(WorldGen), "GrowPumpkin")]
        public static class FastPumpkinPatch
        {
            private static bool _isGrowing;

            public static void Postfix(int i, int j, int type)
            {
                if (Main.netMode == 1 || _isGrowing) return;

                if (Mod.Config != null && Mod.Config.FastPumpkinGrowth)
                {
                    try
                    {
                        _isGrowing = true;
                        WorldGen.GrowPumpkin(i, j, type);
                        WorldGen.GrowPumpkin(i, j, type);
                        WorldGen.GrowPumpkin(i, j, type);
                        WorldGen.GrowPumpkin(i, j, type);
                    }
                    finally
                    {
                        _isGrowing = false;
                    }
                }
            }
        }

        // 6. 失焦后台持续运行
        [HarmonyPatch(typeof(Microsoft.Xna.Framework.Game), "get_IsActive")]
        public static class KeepFocusPatch
        {
            public static bool Prefix(ref bool __result)
            {
                if (Mod.Config != null && Mod.Config.KeepFocus)
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }

        // 8. 敌怪爆炸物不炸毁物块
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.ExplodeTiles))]
        public static class DisableNonPlayerBombPatch
        {
            public static bool Prefix(Projectile __instance)
            {
                if (Mod.Config != null && Mod.Config.DisableNonPlayerBombTileDamage && __instance != null)
                {
                    // 敌怪发射的炸弹 / 骷髅王炸弹
                    if (__instance.type == ProjectileID.BombSkeletronPrime || __instance.npcProj)
                    {
                        return false; // 拦截敌怪爆炸破坏地形
                    }
                }
                return true;
            }
        }



        // 10. 无条件队内传送 (虫洞药水不消耗且随时可用)
        [HarmonyPatch(typeof(Player), nameof(Player.HasUnityPotion))]
        public static class HasUnityPotionPatch
        {
            public static void Postfix(Player __instance, ref bool __result)
            {
                var config = __instance == null ? null : Networking.SettingsSync.GetPlayerConfig(__instance.whoAmI);
                if (config != null && config.TeamUnconditionalTeleport)
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TakeUnityPotion))]
        public static class TakeUnityPotionPatch
        {
            public static bool Prefix(Player __instance)
            {
                var config = __instance == null ? null : Networking.SettingsSync.GetPlayerConfig(__instance.whoAmI);
                if (config != null && config.TeamUnconditionalTeleport)
                {
                    return false; // 不消耗虫洞药水
                }
                return true;
            }
        }
    }
}
