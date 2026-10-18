using System;
using Terraria;

namespace QualityOfTerraria.Features
{
    internal static class ToolSpeedSystem
    {
        internal static bool Enabled { get; private set; } = true;
        internal static bool SpeedActive => Enabled && !Mod.VeinMinerLegacyLoaded;

        internal static float GetMultiplier(int boost) => 1 + Math.Max(0, Math.Min(3, boost));

        internal static void Toggle()
        {
            if (!Mod.WorldActive || Mod.VeinMinerLegacyLoaded) return;
            Enabled = !Enabled;
            if (Mod.Config?.VeinMiningShowMessages == true)
                Main.NewText(Enabled ? "工具速度加成已开启" : "工具速度加成已关闭");
        }

        internal static void ResetSession() => Enabled = true;
    }
}
