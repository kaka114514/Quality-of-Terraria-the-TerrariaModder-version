using HarmonyLib;
using QualityOfTerraria.Features;
using QualityOfTerraria.Networking;
using Terraria;
using Terraria.ID;

namespace QualityOfTerraria.Patches
{
    [HarmonyPatch(typeof(Main), "NPCChatText_DoNurseHeal")]
    internal static class NurseTreatmentPatch
    {
        internal static bool Prefix(ref int __0)
        {
            if (RemoteStorageOperations.Busy || ToolNetwork.Busy)
            {
                Main.npcChatText = "正在等待服务器物品操作确认，请稍后治疗";
                return false;
            }
            Player player = Main.LocalPlayer;
            if (!NurseTreatment.NeedsTreatment(player))
            {
                Main.npcChatText = NurseTreatment.NoTreatment;
                return false;
            }
            __0 = NurseTreatment.Cost();
            if (__0 > 0) return true;
            // 原版价格忽略剩余不超过60刻的状态；免费清除且不调用BuyItem。
            int healed = System.Math.Max(0, player.statLifeMax2 - player.statLife);
            player.statLife = player.statLifeMax2;
            if (healed > 0) player.HealEffect(healed);
            NurseTreatment.ClearDebuffs(player);
            Main.npcChatText = "治疗已完成，本次无需收费";
            if (Main.netMode == 1)
            {
                NetMessage.SendData(MessageID.PlayerLifeMana, -1, -1, null, player.whoAmI);
                NetMessage.SendData(MessageID.PlayerBuffs, -1, -1, null, player.whoAmI);
            }
            return false;
        }
    }
}
