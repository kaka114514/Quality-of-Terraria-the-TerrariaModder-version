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

            if (Mod.Config.MaxStack9999)
            {
                if (__instance.maxStack > 1 && !IsCoin(__instance.type))
                {
                    __instance.maxStack = 9999;
                }
            }

            // 任务鱼堆叠与非唯一堆叠
            if (Mod.Config.QuestFishStack && Main.anglerQuestItemNetIDs != null && Array.IndexOf(Main.anglerQuestItemNetIDs, __instance.type) >= 0)
            {
                __instance.maxStack = 9999;
                __instance.uniqueStack = false;
            }

            // 生命水晶、魔力水晶、生命果极速使用
            if (__instance.type == ItemID.LifeCrystal || __instance.type == ItemID.ManaCrystal || __instance.type == ItemID.LifeFruit)
            {
                __instance.autoReuse = true;
                __instance.useTime = 15;
                __instance.useAnimation = 15;
            }
        }

        private static bool IsCoin(int type)
        {
            return type == ItemID.CopperCoin ||
                   type == ItemID.SilverCoin ||
                   type == ItemID.GoldCoin ||
                   type == ItemID.PlatinumCoin;
        }
    }
}
