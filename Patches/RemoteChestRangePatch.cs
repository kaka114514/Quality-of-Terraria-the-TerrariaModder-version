using HarmonyLib;
using Terraria;
using QualityOfTerraria.Networking;

namespace QualityOfTerraria.Patches
{
    [HarmonyPatch(typeof(Player), "HandleBeingInChestRange")]
    internal static class RemoteChestRangePatch
    {
        public static bool Prefix(Player __instance)
            => !UI.QuickStorageUI.KeepOpen(__instance) && !RemoteStorageOperations.KeepRemoteChest(__instance);
    }
}
