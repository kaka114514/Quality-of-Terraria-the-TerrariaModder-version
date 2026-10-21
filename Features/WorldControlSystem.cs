using System;
using HarmonyLib;
using Terraria;
using Terraria.ID;

namespace QualityOfTerraria.Features
{
    internal static class WorldControlSystem
    {
        internal static int GetClockMinute()
            => ((int)(Main.time / 60) + (Main.dayTime ? 270 : 1170)) % 1440;

        internal static string SetTime(int minute)
        {
            if (Main.netMode == 1) throw new InvalidOperationException("时间由服务器调整");
            if (minute < 0 || minute >= 1440) throw new ArgumentOutOfRangeException(nameof(minute));
            bool day = minute >= 270 && minute < 1170;
            if (day) { Main.bloodMoon = false; Main.stopMoonEvent(); }
            else Main.eclipse = false;
            Main.dayTime = day;
            Main.time = ((minute - (day ? 270 : 1170) + 1440) % 1440) * 60;
            Main.fastForwardTimeToDawn = Main.fastForwardTimeToDusk = false;
            Sync();
            return "时间已设为 " + (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00");
        }

        internal static bool EventActive(int id)
            => id == 0 ? Main.bloodMoon : id == 1 ? Main.eclipse : id == 2 ? Main.pumpkinMoon : id == 3 && Main.snowMoon;

        internal static string SetEvent(int id, bool enabled)
        {
            if (Main.netMode == 1) throw new InvalidOperationException("特殊事件由服务器调整");
            if (id < 0 || id > 3) throw new ArgumentOutOfRangeException(nameof(id));
            string name = new[] { "血月", "日食", "南瓜月", "霜月" }[id];
            if (enabled && (id == 1 ? !Main.dayTime : Main.dayTime))
                throw new InvalidOperationException(name + (id == 1 ? "只能在白天开启，请先调整时间" : "只能在夜晚开启，请先调整时间"));
            if (enabled && Main.invasionType != 0) throw new InvalidOperationException("请等待当前入侵结束后再开启特殊事件");
            if (id == 0) { if (enabled) Main.stopMoonEvent(); Main.bloodMoon = enabled; if (enabled && Main.moonPhase == 4) Main.moonPhase = 5; }
            else if (id == 1) Main.eclipse = enabled;
            else if (enabled) { if (id == 2) Main.startPumpkinMoon(); else Main.startSnowMoon(); }
            else if (EventActive(id)) Main.stopMoonEvent();
            if (Main.netMode == 2 && id >= 2) NetMessage.SendData(MessageID.InvasionProgressReport, -1, -1, null, Main.invasionProgress, 1, id == 2 ? 2 : 1, NPC.waveNumber);
            Sync();
            return name + (enabled ? "已开启" : "已结束；已生成的敌怪仍会保留");
        }

        internal static void SetHoliday(int id, bool enabled)
        {
            if (Main.netMode == 1) throw new InvalidOperationException("节日由服务器调整");
            if (id == 0) { Main.forceHalloweenForever = enabled; Main.halloween = enabled; }
            else if (id == 1) { Main.forceXMasForever = enabled; Main.xMas = enabled; }
            Sync();
        }

        internal static void OnWorldLoad()
        {
            if (Main.netMode != 1) { Main.forceHalloweenForever = false; Main.halloween = false; Sync(); }
            else Main.halloween = Main.forceHalloweenForever;
        }

        private static void Sync()
        {
            if (Main.netMode == 2) NetMessage.SendData(MessageID.WorldData);
        }

        [HarmonyPatch(typeof(Main), nameof(Main.checkHalloween))]
        private static class HalloweenPatch
        {
            private static void Postfix() { if (Mod.WorldActive) Main.halloween = Main.forceHalloweenForever; }
        }
    }
}
