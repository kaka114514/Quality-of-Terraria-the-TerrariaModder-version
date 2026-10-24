using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
                    // ItemCheck consumes the input only when itemTime == itemTimeMax.
                    __instance.itemTimeMax = 1;
                    __instance.itemTime = 1;
                }
            }
        }
        // 7. 草药成熟阶段加速；只改变原版成熟随机判定，不改变种植和生长触发频率。
        [HarmonyPatch]
        public static class HerbMaturationPatch
        {
            public static MethodBase TargetMethod() => AccessTools.Method(typeof(WorldGen), "MatureTheHerbPlants");

            internal static int MaturityRollLimit(int vanillaLimit)
            {
                if (Mod.Config == null) return vanillaLimit;
                if (Mod.Config.HerbInstantBloom) return 1;
                if (Mod.Config.HerbMaturationSpeed) return Math.Max(1, (vanillaLimit + 4) / 5);
                return vanillaLimit;
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var source = instructions.ToList();
                var nextInt = AccessTools.Method(typeof(Terraria.Utilities.UnifiedRandom), "Next", new[] { typeof(int) });
                var adjust = AccessTools.Method(typeof(HerbMaturationPatch), nameof(MaturityRollLimit));
                int patched = 0;
                for (int i = 0; i < source.Count; i++)
                {
                    yield return source[i];
                    if (i > 0 && source[i].Calls(nextInt) && IsHerbRollLimit(source[i - 1]))
                    {
                        yield return new CodeInstruction(OpCodes.Call, adjust);
                        patched++;
                    }
                }
                if (patched != 4) throw new InvalidOperationException("未能定位原版草药四个成熟随机判定");
            }

            private static bool IsHerbRollLimit(CodeInstruction instruction)
            {
                if (instruction.opcode == OpCodes.Ldc_I4_1 || instruction.opcode == OpCodes.Ldc_I4_2 ||
                    instruction.opcode == OpCodes.Ldc_I4_3) return true;
                if (instruction.opcode == OpCodes.Ldc_I4_S)
                {
                    int value = Convert.ToInt32(instruction.operand);
                    return value == 2 || value == 3 || value == 25;
                }
                return instruction.opcode == OpCodes.Ldc_I4 &&
                    (Convert.ToInt32(instruction.operand) == 2 || Convert.ToInt32(instruction.operand) == 3 || Convert.ToInt32(instruction.operand) == 25);
            }
        }

        // 8. 草药无环境限制开花；成熟后采摘始终掉落种子。
        [HarmonyPatch]
        public static class HerbEnvironmentPatch
        {
            public static MethodBase TargetMethod() => AccessTools.Method(typeof(WorldGen), "IsAlchemyPlantHarvestable");

            internal static bool IgnoreEnvironmentLimit(bool vanillaResult)
                => Mod.Config != null && Mod.Config.HerbIgnoreEnvironment || vanillaResult;

            public static void Postfix(int style, ref bool __result)
            {
                if (Main.netMode == 1 || style < 0 || style > 5) return;
                __result = IgnoreEnvironmentLimit(__result);
            }
        }

        // 9. 草药成熟后采摘必定掉落种子。
        [HarmonyPatch(typeof(WorldGen), "IsHarvestableHerbWithSeed")]
        public static class FastHerbsPatch
        {
            public static void Postfix(ref bool __result)
            {
                if (Main.netMode != 1 && Mod.Config != null && Mod.Config.HerbIgnoreEnvironment)
                {
                    __result = true;
                }
            }
        }
    }
}
