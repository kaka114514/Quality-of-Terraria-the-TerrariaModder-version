using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.ID;

namespace QualityOfTerraria.Patches
{
    public static class RedPotionPatches
    {
        // 与ImproveGame 1.4.4红药水扩展一致的18种标准药水增益。
        private static readonly int[] ExtendedBuffs = {
            BuffID.ObsidianSkin, BuffID.Regeneration, BuffID.Swiftness, BuffID.Ironskin,
            BuffID.ManaRegeneration, BuffID.MagicPower, BuffID.Featherfall, BuffID.Spelunker,
            BuffID.Archery, BuffID.Heartreach, BuffID.Hunter, BuffID.Endurance, BuffID.Lifeforce,
            BuffID.Inferno, BuffID.Mining, BuffID.Rage, BuffID.Wrath, BuffID.Dangersense
        };

        public static bool CanActivate(Item item)
        {
            return CanActivateForPlayer(item, Mod.Config);
        }

        public static bool CanActivateForPlayer(Item item, QualityOfTerrariaConfig config)
        {
            return config != null && config.InfiniteRedPotion && item != null && item.type == ItemID.RedPotion &&
                item.stack >= Math.Max(1, Math.Min(300, config.RedPotionRequirement)) &&
                (Main.getGoodWorld || config.RedPotionEverywhere);
        }

        public static void ApplyInfiniteBuffs(Player player, Item item)
        {
            ApplyInfiniteBuffsForPlayer(player, item, Mod.Config);
        }

        public static void ApplyInfiniteBuffsForPlayer(Player player, Item item, QualityOfTerrariaConfig config)
        {
            if (!CanActivateForPlayer(item, config)) return;
            foreach (int buff in ExtendedBuffs) player.AddBuff(buff, 120, false);
        }

        private static bool CanUsePositiveRedPotion(bool specialWorld)
        {
            return specialWorld || (Mod.Config != null && Mod.Config.RedPotionEverywhere);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ItemCheck))]
        public static class RedPotionEverywherePatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> code = new List<CodeInstruction>(instructions);
                FieldInfo specialWorld = AccessTools.Field(typeof(Main), nameof(Main.getGoodWorld));
                for (int i = 0; i + 4 < code.Count; i++)
                {
                    // 1.4.5.8只替换红药水678分支的特殊种子读取，不临时改共享世界种子。
                    if (code[i].opcode != OpCodes.Ldc_I4 || !Equals(code[i].operand, (int)ItemID.RedPotion) ||
                        code[i + 1].opcode.FlowControl != FlowControl.Cond_Branch || !code[i + 2].LoadsField(specialWorld)) continue;
                    code.Insert(i + 3, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RedPotionPatches), nameof(CanUsePositiveRedPotion))));
                    return code;
                }
                throw new InvalidOperationException("未找到1.4.5.8红药水特殊种子分支。");
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.CanConsumeConsumableItem))]
        public static class RedPotionConsumptionPatch
        {
            public static void Postfix(Player __instance, Item __0, ref bool __result)
            {
                QualityOfTerrariaConfig config = Networking.SettingsSync.GetPlayerConfig(__instance.whoAmI);
                if (CanActivateForPlayer(__0, config)) __result = false;
            }
        }
    }
}
