using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;
using QualityOfTerraria.Networking;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.Patches
{
    internal static class StorageInventoryGuard
    {
        internal static bool Locked => Main.netMode != 2 && (RemoteStorageOperations.Busy || ToolNetwork.Busy);
        private static Player _deathPlayer;
        private static bool? _deathItems;
        private static bool _deathCoins;

        internal static void Update()
        {
            if (_deathPlayer == null) return;
            if (Locked) { if (_deathPlayer.dead) _deathPlayer.respawnTimer = Math.Max(2, _deathPlayer.respawnTimer); return; }
            Player player = _deathPlayer;
            bool? items = _deathItems; bool coins = _deathCoins;
            ResetSession();
            if (!player.dead) return;
            if (items.HasValue) player.DropItems(items.Value);
            if (coins) player.DropCoins();
        }

        internal static void ResetSession() { _deathPlayer = null; _deathItems = null; _deathCoins = false; }

        [HarmonyPatch(typeof(Player), nameof(Player.HasLockedInventory))]
        internal static class LockedInventoryPatch
        {
            public static void Postfix(Player __instance, ref bool __result)
            { if (Locked && __instance == Main.LocalPlayer) __result = true; }
        }

        [HarmonyPatch]
        internal static class InventoryInputPatch
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (string name in new[] { "LeftClick", "RightClick", "OverrideLeftClick", "PickupItemIntoMouse" })
                    foreach (MethodInfo method in typeof(Terraria.UI.ItemSlot).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                        if (method.Name == name) yield return method;
                yield return AccessTools.Method(typeof(Terraria.GameContent.CraftingRequests), "CraftItem");
                yield return AccessTools.Method(typeof(QualityOfTerraria.Features.Autofisher), "Update");
                yield return AccessTools.Method(typeof(WandsAndTools), "UpdateQuickPlace");
                yield return AccessTools.Method(typeof(WandsAndTools), "UpdateLiquidWand");
            }
            public static bool Prefix() => !Locked;
        }

        [HarmonyPatch]
        internal static class CustomUIClickPatch
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.PropertyGetter(typeof(UIRenderer), "MouseLeftClick");
                yield return AccessTools.PropertyGetter(typeof(UIRenderer), "MouseRightClick");
            }
            public static void Postfix(ref bool __result) { if (Locked) __result = false; }
        }

        [HarmonyPatch]
        internal static class PlayerActionsPatch
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (string name in new[] { "TryingToUseItem", "QuickHeal", "QuickMana", "QuickBuff", "dropItemCheck", "QuickStackAllChests", "QuickGrapple" })
                    yield return AccessTools.Method(typeof(Player), name);
            }
            public static bool Prefix(Player __instance) => !Locked || __instance != Main.LocalPlayer;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetItem), typeof(Item), typeof(GetItemSettings))]
        internal static class PickupPatch
        {
            public static bool Prefix(Player __instance, Item __0, ref Item __result)
            {
                if (!Locked || __instance != Main.LocalPlayer) return true;
                __result = __0; return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.DropItems))]
        internal static class DeathItemsPatch
        {
            public static bool Prefix(Player __instance, bool __0)
            {
                if (!Locked || __instance != Main.LocalPlayer) return true;
                _deathPlayer = __instance; _deathItems = __0; return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.DropCoins))]
        internal static class DeathCoinsPatch
        {
            public static bool Prefix(Player __instance, ref long __result)
            {
                if (!Locked || __instance != Main.LocalPlayer) return true;
                _deathPlayer = __instance; _deathCoins = true; __result = 0; return false;
            }
        }
    }
}
