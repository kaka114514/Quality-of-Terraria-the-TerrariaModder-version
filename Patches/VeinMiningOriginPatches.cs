using System;
using HarmonyLib;
using Terraria;
using QualityOfTerraria.Features;
using QualityOfTerraria.Networking;
using TerrariaModder.Core.Permissions;

namespace QualityOfTerraria.Patches
{
    // 服务端按客户端逐个处理入站包，原版破矿就发生在这次调用里；
    // 用线程内的发送者即可定位来源，不再依赖具体包的字节布局。
    [HarmonyPatch(typeof(NetMessage), "CheckBytes")]
    internal static class VeinMiningOriginSessionPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(int bufferIndex, out int __state)
        {
            __state = VeinMiningOriginContext.Sender;
            VeinMiningOriginContext.Sender = Main.netMode == 2 && !Mod.VeinMinerLegacyLoaded && bufferIndex >= 0 && bufferIndex < Main.maxPlayers ? bufferIndex : -1;
        }

        [HarmonyPostfix]
        internal static void Postfix(int __state) => VeinMiningOriginContext.Sender = __state;
    }

    internal static class VeinMiningOriginContext
    {
        [ThreadStatic] internal static int Sender = -1;
    }

    // 只记录服务端确实破坏掉的矿石：掉落在原版流程内收集，来源证明供本玩家的连锁请求消费一次。
    [HarmonyPatch(typeof(WorldGen), "KillTile")]
    internal static class VeinMiningOriginTilePatch
    {
        // 诊断计数：玩家数据包处理期间的破块次数与其中破掉的矿石次数。
        internal static int PacketKills, PacketOreKills, PostfixCalls, PostfixStillActive, PostfixRecorded;

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
            if (fail || effectOnly || Main.netMode != 2 || !Mod.WorldActive) return;
            int sender = VeinMiningOriginContext.Sender;
            if (sender < 0) return;
            PacketKills++;
            if (!PermissionService.HasModAccess(sender, "quality-of-terraria")) return;
            Player player = Main.player[sender];
            // 不在这里要求手持镐子：物品同步可能晚于破矿包，请求阶段会重新校验镐力与权限。
            if (player == null || !player.active || player.dead) return;
            if (!WorldGen.InWorld(i, j, 3)) return;
            Tile tile = Main.tile[i, j];
            if (tile == null || !tile.active() || !VeinMiningSystem.IsOre(tile.type)) return;
            PacketOreKills++;
            __state = new State { Player = player, Type = tile.type, PreviousDrops = VeinMiningSystem.BeginDrops(player, i, j) };
            // 先保存掉落捕获上下文；原版 KillTile 只有返回后才确定瓦片确实被破坏。
        }

        [HarmonyPostfix]
        internal static void Postfix(int i, int j, State __state)
        {
            if (__state == null) return;
            PostfixCalls++;
            Tile after = Main.tile[i, j];
            if (after != null && after.active()) { PostfixStillActive++; return; }
            PostfixRecorded++;
            VeinMiningNetwork.RecordBreak(__state.Player, i, j, __state.Type);
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, int i, int j, State __state)
        {
            if (__state == null) return __exception;
            try
            {
                // 来源证明只在 Postfix 确认实际破坏后写入；异常或失败时没有本次证明可撤销，
                // 也不能误删同坐标上之前一次合法原版破块留下的证明。
            }
            finally { VeinMiningSystem.EndDrops(__state.PreviousDrops); }
            return __exception;
        }
    }
}
