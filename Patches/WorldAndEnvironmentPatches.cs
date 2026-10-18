using HarmonyLib;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace QualityOfTerraria.Patches
{
    public static class WorldAndEnvironmentPatches
    {
        // 1. 禁止邪恶群落草皮蔓延
        [HarmonyPatch(typeof(WorldGen), nameof(WorldGen.SpreadGrass))]
        public static class NoEvilSpreadPatch
        {
            public static bool Prefix(int i, int j, int dirt, int grass)
            {
                if (Main.netMode != 1 && !WorldGen.generatingWorld && WorldFeaturePatches.NaturalUpdateDepth > 0 && Mod.Config != null && Mod.Config.NoEvilSpread)
                {
                    if (grass == TileID.CorruptGrass || grass == TileID.CrimsonGrass ||
                        grass == TileID.HallowedGrass || grass == 661 || grass == 662)
                    {
                        return false; // 拦截邪恶草皮自然蔓延
                    }
                }
                return true;
            }
        }

        // 2. 全能晶塔：随时随地可传送
        [HarmonyPatch(typeof(TeleportPylonsSystem), nameof(TeleportPylonsSystem.IsPlayerNearAPylon))]
        public static class PylonNearPatch
        {
            public static void Postfix(ref bool __result)
            {
                if (Mod.Config != null && Mod.Config.PylonTeleportWithoutNearbyPlayer)
                {
                    __result = true; // 玩家无需靠近晶塔即可传送
                }
            }
        }

        // 3. 全能晶塔：无视附近城镇 NPC 数量限制
        [HarmonyPatch(typeof(TeleportPylonsSystem), "HowManyNPCsDoesPylonNeed")]
        public static class PylonNPCRequirementPatch
        {
            public static void Postfix(ref int __result)
            {
                if (Main.netMode != 1 && Mod.Config != null && Mod.Config.UniversalPylons)
                {
                    __result = 0; // 晶塔传送无需任何 NPC
                }
            }
        }

        // 4. 任意放床设置重生点，无视房间完整性要求
        [HarmonyPatch(typeof(Player), nameof(Player.CheckSpawn))]
        public static class BedSpawnEverywherePatch
        {
            public static void Postfix(ref bool __result)
            {
                if (Mod.Config != null && Mod.Config.BedSpawnEverywhere)
                {
                    __result = true; // 任意床均可设为出生点
                }
            }
        }

        // 5. 钓鱼无水池尺寸渔力惩罚
        [HarmonyPatch(typeof(Player), "Fishing_GetPowerMultiplier")]
        public static class NoFishingPenaltyPatch
        {
            public static void Postfix(ref float __result)
            {
                if (Mod.Config != null && Mod.Config.NoFishingPenalty)
                {
                    if (__result < 1f)
                    {
                        __result = 1f; // 消除水体过小带来的渔力惩罚
                    }
                }
            }
        }

        // 6. 提炼机零冷却极速提炼
        [HarmonyPatch(typeof(Player), "ExtractinatorUse")]
        public static class FastExtractinatorPatch
        {
            public static void Postfix(Player __instance)
            {
                if (Mod.Config != null && Mod.Config.FastExtractinator && __instance != null)
                {
                    __instance.itemTime = 0;
                    __instance.itemAnimation = 0;
                }
            }
        }
        // 7. 草药随时开花并成熟掉落种子
        [HarmonyPatch(typeof(WorldGen), "IsHarvestableHerbWithSeed")]
        public static class FastHerbsPatch
        {
            public static void Postfix(ref bool __result)
            {
                if (Main.netMode != 1 && Mod.Config != null && Mod.Config.FastHerbsAndBloom)
                {
                    __result = true; // 草药无论何时采摘均掉落种子
                }
            }
        }
    }
}
