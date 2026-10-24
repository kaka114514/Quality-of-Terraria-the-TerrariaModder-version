using Terraria;

namespace QualityOfTerraria.Features
{
    internal static class ToolSpeedSystem
    {
        internal static bool Enabled { get; private set; } = true;
        internal static bool SpeedActive => Enabled && !Mod.VeinMinerLegacyLoaded;

        internal static void Toggle()
        {
            if (!Mod.WorldActive || Mod.VeinMinerLegacyLoaded) return;
            Enabled = !Enabled;
            if (Mod.Config?.VeinMiningShowMessages == true)
                Main.NewText(Enabled ? "工具与放置加速已开启" : "工具与放置加速已关闭");
        }

        internal static void ResetSession() => Enabled = true;
    }
}
