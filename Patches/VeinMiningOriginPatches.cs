using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using QualityOfTerraria.Features;
using QualityOfTerraria.Networking;
using TerrariaModder.Core.Permissions;

namespace QualityOfTerraria.Patches
{
    // 避免直接重写含有缺失可选 Steamworks 程序集引用的 GetData；只在原版 CheckBytes 调用点包裹它。
    [HarmonyPatch(typeof(NetMessage), "CheckBytes", new[] { typeof(int) })]
    internal static class VeinMiningOriginMessagePatch
    {
        internal sealed class Context
        {
            internal int Sender, X, Y;
        }
        [ThreadStatic] internal static Context Current;

        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            System.Reflection.MethodInfo original = typeof(MessageBuffer).GetMethod("GetData", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, new[] { typeof(int), typeof(int), typeof(int).MakeByRefType() }, null);
            System.Reflection.MethodInfo replacement = typeof(VeinMiningOriginMessagePatch).GetMethod(nameof(ProcessGetData), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            bool replaced = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is System.Reflection.MethodInfo target && target == original)
                {
                    instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                    instruction.operand = replacement;
                    replaced = true;
                }
                yield return instruction;
            }
            if (!replaced) throw new MissingMethodException("NetMessage.CheckBytes 中找不到 MessageBuffer.GetData 调用点");
        }

        internal static void ProcessGetData(MessageBuffer buffer, int start, int length, out int messageType)
        {
            Context previous = Current;
            Current = null;
            try
            {
                if (Main.netMode == 2 && Mod.WorldActive && !Mod.VeinMinerLegacyLoaded && buffer != null &&
                    buffer.whoAmI >= 0 && buffer.whoAmI < Main.maxPlayers)
                {
                    byte[] data = buffer.readBuffer;
                    if (data != null && start >= 0 && length >= 9 && start <= data.Length - 9 &&
                        data[start] == MessageID.TileManipulation && data[start + 1] == 0 && BitConverter.ToInt16(data, start + 6) == 0)
                    {
                        Current = new Context { Sender = buffer.whoAmI, X = BitConverter.ToInt16(data, start + 2), Y = BitConverter.ToInt16(data, start + 4) };
                    }
                }
                buffer.GetData(start, length, out messageType);
            }
            finally { Current = previous; }
        }
    }

    [HarmonyPatch(typeof(WorldGen), "KillTile")]
    internal static class VeinMiningOriginTilePatch
    {
        internal sealed class State
        {
            internal Player Player;
            internal ushort Type;
            internal object PreviousDrops;
        }

        [HarmonyPrefix]
        internal static void Prefix(int i, int j, bool fail, bool effectOnly, out State __state)
        {
            __state = null;
            var origin = VeinMiningOriginMessagePatch.Current;
            if (origin == null || fail || effectOnly || origin.X != i || origin.Y != j || !WorldGen.InWorld(i, j, 3) || !PermissionService.HasModAccess(origin.Sender, "quality-of-terraria")) return;
            Player player = Main.player[origin.Sender];
            if (player == null || !player.active || player.dead || player.HeldItem == null || player.HeldItem.pick <= 0) return;
            Tile tile = Main.tile[i, j];
            var config = SettingsSync.GetPlayerConfig(origin.Sender);
            if (tile == null || !tile.active() || !VeinMiningSystem.IsOre(tile.type) || config == null ||
                !player.IsInTileInteractionRange(i, j, TileReachCheckSettings.Simple, player.HeldItem.tileBoost) || !VeinMiningSystem.CanMineOrigin(player, i, j)) return;
            __state = new State { Player = player, Type = tile.type, PreviousDrops = VeinMiningSystem.BeginDrops(player, i, j) };
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, int i, int j, State __state)
        {
            if (__state == null) return __exception;
            try
            {
                Tile after = Main.tile[i, j];
                if (__exception == null && after != null && !after.active()) VeinMiningNetwork.RecordBreak(__state.Player, i, j, __state.Type);
            }
            finally { VeinMiningSystem.EndDrops(__state.PreviousDrops); }
            return __exception;
        }
    }
}
