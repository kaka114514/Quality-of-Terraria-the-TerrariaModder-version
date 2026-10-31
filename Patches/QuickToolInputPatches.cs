using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;

namespace QualityOfTerraria.Patches
{
    [HarmonyPatch]
    internal static class QuickToolUseInputPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Player), nameof(Player.ItemCheck));
            yield return AccessTools.Method(typeof(Player), nameof(Player.ItemCheck_ManageRightClickFeatures));
            yield return AccessTools.Method(typeof(Player), nameof(Player.LookForTileInteractions));
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        internal static void Prefix(Player __instance)
        {
            if (WandsAndTools.ShouldBlockVanillaItemUse(__instance))
                WandsAndTools.SuppressVanillaItemUse(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "ItemCheck_CanUse")]
    internal static class QuickToolCanUsePatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(Player __instance, ref bool __result)
        {
            if (!WandsAndTools.ShouldBlockVanillaItemUse(__instance)) return true;
            __result = false;
            return false;
        }
    }
}
