using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;

namespace QualityOfTerraria.Patches
{
    public static class WorldFeaturePatches
    {
        [ThreadStatic] internal static int NaturalUpdateDepth;

        internal static bool AllowNaturalInfection(bool original)
        {
            return original && (Main.netMode == 1 || WorldGen.generatingWorld || Mod.Config == null || !Mod.Config.NoEvilSpread);
        }

        // 不拦截 Convert，保留净化、溶液和世界生成；仅修改实际自然更新的感染门槛。
        [HarmonyPatch]
        public static class NaturalInfectionPatch
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(WorldGen), "hardUpdateWorld");
                yield return AccessTools.Method(typeof(WorldGen), "UpdateWorld_OvergroundTile");
                yield return AccessTools.Method(typeof(WorldGen), "UpdateWorld_UndergroundTile");
                yield return AccessTools.Method(typeof(WorldGen), "UpdateWorld_GrassGrowth");
            }

            public static void Prefix() { NaturalUpdateDepth++; }
            public static Exception Finalizer(Exception __exception) { NaturalUpdateDepth--; return __exception; }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                FieldInfo gate = AccessTools.Field(typeof(WorldGen), "AllowedToSpreadInfections");
                int count = 0;
                foreach (CodeInstruction code in instructions)
                {
                    yield return code;
                    if (!code.LoadsField(gate)) continue;
                    count++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WorldFeaturePatches), nameof(AllowNaturalInfection)));
                }
                if (count == 0) throw new InvalidOperationException("未找到1.4.5.8自然感染开关，停止应用蔓延补丁。");
            }
        }

        [HarmonyPatch(typeof(TeleportPylonsSystem), nameof(TeleportPylonsSystem.HasPylonOfType))]
        public static class PylonPlacementPatch
        {
            public static void Postfix(ref bool __result)
            {
                if (Mod.Config != null && Mod.Config.PylonPlacementUnrestricted) __result = false;
            }
        }

        [HarmonyPatch(typeof(TeleportPylonsSystem), "DoesPylonAcceptTeleportation")]
        public static class PylonBiomePatch
        {
            public static void Postfix(ref bool __result)
            {
                if (Main.netMode != 1 && Mod.Config != null && Mod.Config.PylonTeleportIgnoreBiome) __result = true;
            }
        }

        internal static bool PylonInteractionRange(Player player, int x, int y, int width, int height, TileReachCheckSettings settings)
        {
            return (Main.netMode != 1 && Mod.Config != null && Mod.Config.PylonTeleportWithoutNearbyPlayer) ||
                player.InTileEntityInteractionRange(x, y, width, height, settings);
        }

        [HarmonyPatch(typeof(TeleportPylonsSystem), "HandleTeleportRequest")]
        public static class PylonSourceRangePatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo range = AccessTools.Method(typeof(Player), nameof(Player.InTileEntityInteractionRange),
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(TileReachCheckSettings) });
                int count = 0;
                foreach (CodeInstruction code in instructions)
                {
                    if (code.Calls(range))
                    {
                        count++;
                        code.opcode = OpCodes.Call;
                        code.operand = AccessTools.Method(typeof(WorldFeaturePatches), nameof(PylonInteractionRange));
                    }
                    yield return code;
                }
                if (count != 1) throw new InvalidOperationException("未找到1.4.5.8晶塔源位置唯一范围判定。");
            }
        }

        internal static float ScaleCoinValue(float original)
        {
            return Main.netMode == 1 || Mod.Config == null ? original : original * Math.Max(1, Math.Min(25, Mod.Config.CoinDropMultiplier));
        }

        // 在掉落函数读基础价值时乘算，不写回 npc.value，也不改变NPC额外钱币/玩家死亡钱币。
        [HarmonyPatch(typeof(NPC), "NPCLoot_DropMoney")]
        public static class CoinDropPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                FieldInfo value = AccessTools.Field(typeof(NPC), nameof(NPC.value));
                int count = 0;
                foreach (CodeInstruction code in instructions)
                {
                    yield return code;
                    if (!code.LoadsField(value)) continue;
                    count++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WorldFeaturePatches), nameof(ScaleCoinValue)));
                }
                if (count != 1) throw new InvalidOperationException("未找到1.4.5.8敌怪钱币掉落唯一基础价值读取。");
            }
        }
    }

    internal static class WorldFeatureActions
    {
        internal static bool RefreshTravelShop()
        {
            if (Main.netMode == 1 || Mod.Config == null || !Mod.Config.RefreshTravelShop || !NPC.AnyNPCs(NPCID.TravellingMerchant)) return false;
            Chest.SetupTravelShop();
            if (Main.netMode == 2) NetMessage.SendData(72);
            return true;
        }
    }
}
