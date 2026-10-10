using System;
using System.Reflection;
using HarmonyLib;
using QualityOfTerraria.Features;
using QualityOfTerraria.Networking;
using Terraria;
using Terraria.DataStructures;

namespace QualityOfTerraria.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.PickTile))]
    internal static class VeinMiningPickPatch
    {
        internal sealed class PickState
        {
            internal ushort Type;
            internal bool Forced;
            internal object Drops;
            internal QualityOfTerrariaConfig Config;
        }

        internal static void Prefix(Player __instance, int x, int y, out PickState __state)
        {
            __state = null;
            if (Main.netMode == 2 || !Mod.WorldActive || VeinMiningSystem.Suspended || __instance != Main.LocalPlayer || !WorldGen.InWorld(x, y, 1)) return;
            QualityOfTerrariaConfig config = SettingsSync.GetPlayerConfig(__instance.whoAmI);
            Tile tile = Main.tile[x, y];
            if (config == null || tile == null || !tile.active() || !VeinMiningSystem.IsOre(tile.type) || __instance.HeldItem == null || __instance.HeldItem.pick <= 0) return;
            __state = new PickState { Type = tile.type, Forced = Mod.IsForceVeinMiningHeld(), Config = config, Drops = VeinMiningSystem.BeginDrops(__instance, x, y) };
        }

        internal static void Postfix(Player __instance, int x, int y, PickState __state)
        {
            if (__state == null) return;
            VeinMiningSystem.EndDrops(__state.Drops);
            Tile after = Main.tile[x, y];
            if (after != null && after.active()) return;
            if (!__state.Config.VeinMiningEnabled && !__state.Forced) return;
            if (Main.netMode == 1) VeinMiningNetwork.Request(x, y, __state.Type, __state.Forced);
            else if (Main.netMode == 0) VeinMiningSystem.Start(__instance, x, y, __state.Type, __state.Config);
        }

        internal static void Finalizer(PickState __state)
        {
            if (__state != null) VeinMiningSystem.EndDrops(__state.Drops);
        }
    }

    [HarmonyPatch]
    internal static class VeinMiningDropPatch
    {
        internal static MethodBase TargetMethod()
        {
            foreach (MethodInfo method in typeof(Item).GetMethods(BindingFlags.Static | BindingFlags.Public))
                if (method.Name == nameof(Item.NewItem) && method.GetParameters().Length == 12 && method.GetParameters()[1].ParameterType == typeof(int)) return method;
            throw new MissingMethodException(typeof(Item).FullName, "NewItem(IEntitySource,int,int,...)");
        }
        internal static void Postfix(IEntitySource __0, int __result) => VeinMiningSystem.ReceiveDrop(__0, __result);
    }
}
