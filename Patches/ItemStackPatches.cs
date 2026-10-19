using System;
using HarmonyLib;
using Terraria;
using Terraria.ID;

namespace QualityOfTerraria.Patches
{
    [HarmonyPatch(typeof(Item), "SetDefaults")]
    public static class ItemStackPatches
    {
        public static void Postfix(Item __instance)
        {
            if (Mod.Config == null || __instance == null)
                return;

            // 生命水晶、魔力水晶、生命果极速使用
            if (__instance.type == ItemID.LifeCrystal || __instance.type == ItemID.ManaCrystal || __instance.type == ItemID.LifeFruit)
            {
                __instance.autoReuse = true;
                __instance.useTime = 15;
                __instance.useAnimation = 15;
            }
        }
    }
}
