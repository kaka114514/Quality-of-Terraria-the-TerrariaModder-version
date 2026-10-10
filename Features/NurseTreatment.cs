using System;
using HarmonyLib;
using Terraria;
using Terraria.ID;

namespace QualityOfTerraria.Features
{
    internal static class NurseTreatment
    {
        internal const string NoTreatment = "生命值已满且无可清除的负面状态，无需治疗";
        private static readonly Func<int> GetOriginalCost = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), AccessTools.Method(typeof(Main), "GetNurseHealCost"));

        internal static bool CanRemove(Player player, int slot)
        {
            int type = player.buffType[slot];
            return type > 0 && type < Main.debuff.Length && Main.debuff[type] && player.buffTime[slot] > 0 &&
                   (type >= BuffID.Count || !BuffID.Sets.NurseCannotRemoveDebuff[type]);
        }

        internal static bool NeedsTreatment(Player player)
        {
            if (player.statLife < player.statLifeMax2) return true;
            for (int slot = 0; slot < player.buffType.Length; slot++)
                if (CanRemove(player, slot)) return true;
            return false;
        }

        // 原版方法读取本地玩家；此入口只用于本地快捷键和护士对话。
        internal static int Cost() => Math.Max(0, GetOriginalCost());

        internal static void ClearDebuffs(Player player)
        {
            for (int slot = player.buffType.Length - 1; slot >= 0; slot--)
                if (CanRemove(player, slot)) player.DelBuff(slot);
        }
    }
}
