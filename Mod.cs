using System;
using TerrariaModder.Core;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.Events;
using Terraria;
using QualityOfTerraria.Networking;

namespace QualityOfTerraria
{
    public class Mod : ModBase, IModLifecycle
    {
        public static QualityOfTerrariaConfig Config { get; private set; }
        public static bool WorldActive { get; private set; }
        private static Mod _instance;
        private ModContext _context;
        private ILogger _logger;
        private bool _serverSession;
        private bool _contentReady, _patchesApplied;

        public override void Initialize(ModContext context)
        {
            _logger = context.Logger;
            _instance = this;
            _context = context;
            Config = context.GetConfig<QualityOfTerrariaConfig>();
            LocalWorldSettings.Initialize();

            // 注册常用快捷键（严格对应用户指定键位，杜绝占用 F6~F12）
            context.RegisterKeybind("space-wand", "快速放置", "切换快速放置，开启后手持方块/平台/墙按住鼠标左键拖拽铺设", "P", WandsAndTools.ExecuteQuickPlace);
            context.RegisterKeybind("liquid-wand", "液体魔杖模式切换", "循环切换关/水/岩浆/微光/蜂蜜模式，按住[;]吸收，按住[']生成", "L", WandsAndTools.ExecuteLiquidWand);
            context.RegisterKeybind("npc-home", "城镇NPC立即回家", "一键将所有城镇NPC召回各自房间", "N", WandsAndTools.ExecuteNPCHome);
            context.RegisterKeybind("quick-nurse", "快捷护士瞬间治疗", "一键护士回满血与魔力并清除Debuff(按原版收费)", "H", WandsAndTools.ExecuteQuickNurse);
            context.RegisterKeybind("open-bank", "随身猪猪钱罐", "无需放置直接打开随身钱罐面板", "PERIOD", WandsAndTools.ExecuteOpenBank);
            context.RegisterKeybind("super-vault", "随身大背包", "呼出100格额外随身独立仓库", "B", UI.SuperVaultUI.Toggle);

            // 工具与辅助功能
            context.RegisterKeybind("wall-wand", "快速铺墙", "使用背包中的背景墙，快速铺满周围无墙区域", "O", WandsAndTools.ExecuteWallWand);
            context.RegisterKeybind("weather-control", "切换晴雨", "切换下雨和放晴", "U", WandsAndTools.ExecuteWeatherController);

            // 大型 UI 界面与特色系统快捷键（已完全避开 F6~F12）
            context.RegisterKeybind("extreme-storage", "终极存储大面板", "呼出集中仓储大面板，支持分类浏览与存取", "K", UI.ExtremeStorageUI.Toggle);
            context.RegisterKeybind("player-stats", "角色实时属性面板", "实时监控各项攻防暴击穿透属性", "Y", UI.PlayerStatsUI.Toggle);
            context.RegisterKeybind("structure-locator", "小地图结构定位球", "打开小地图结构定位面板，快速扫描定位并点亮迷雾", "G", UI.StructureLocatorUI.Toggle);
            context.RegisterKeybind("spawn-rate-control", "刷怪率与节日控制", "调节0～100倍刷怪率及节日、世界特性；联机由服务器控制", "OPENBRACKET", UI.BuffAndSpawnRateUI.Toggle);
            context.RegisterKeybind("autofisher", "自动钓鱼机", "背包内有可用鱼竿及鱼饵，在液体附近启用，每10秒尝试一次原版钓鱼结算", "Z", Features.Autofisher.Toggle);
            context.RegisterKeybind("target-dummy", "测试假人桩", "在鼠标处放置或移除测试假人，实时统计DPS", "X", Features.TargetDummySystem.ToggleOrSpawnAtCursor);

            UI.UIManager.Initialize();
            FrameEvents.OnPreUpdate += CheckWorldSession;
            FrameEvents.OnPreUpdate += UpdateNetworkSession;
            GameEvents.OnWorldSave += SaveSession;
            GameEvents.OnReturnToMenu += EndSession;
        }

        private void ApplyReadyPatches()
        {
            // 游戏内容与嵌入依赖已就绪；设置页通过反射桥读取本地化，不依赖其他模组的补丁顺序。
            if (!_contentReady || _patchesApplied) return;
            _patchesApplied = true;
            int patchedCount = 0;
            var harmony = new HarmonyLib.Harmony("QualityOfTerraria");
            foreach (var type in typeof(Mod).Assembly.GetTypes())
            {
                var attrs = type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false);
                if (attrs != null && attrs.Length > 0)
                {
                    try
                    {
                        var processor = harmony.CreateClassProcessor(type);
                        var patches = processor.Patch();
                        if (patches != null && patches.Count > 0)
                        {
                            patchedCount += patches.Count;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn($"[Harmony] 跳过补丁 {type.Name}: {ex}");
                    }
                }
            }

            _logger?.Info($"Quality of Terraria (更好的体验) 初始化完成！共激活 {patchedCount} 个 Harmony 补丁。");
        }

        public void OnContentReady(ModContext context)
        {
            _contentReady = true;
            ApplyReadyPatches();
            _logger?.Info("QualityOfTerraria: 内容已就绪。");
        }

        public void OnWorldLoad()
        {
            UI.UIManager.ResetSession();
            WorldActive = true;
            _serverSession = Main.netMode == 2;
            if (Main.netMode == 0) LocalWorldSettings.Restore();
            RemoteStorageOperations.Initialize();
            SettingsSync.Initialize();
            ToolNetwork.Initialize();
            FeatureNetwork.Initialize();
            UI.RemoteStorageSnapshots.Initialize();
            UI.SuperVaultUI.EnsureLoadedForCurrentPlayer();
            UI.ExtremeStorageUI.EnsureLoadedForCurrentWorld();
            _logger?.Info("QualityOfTerraria: 世界已加载，已按存档身份尝试读取大背包与箱子标记。");
        }

        public void OnWorldUnload()
        {
            EndSession();
        }

        public override void Unload()
        {
            EndSession();
            FrameEvents.OnPreUpdate -= CheckWorldSession;
            FrameEvents.OnPreUpdate -= UpdateNetworkSession;
            GameEvents.OnWorldSave -= SaveSession;
            GameEvents.OnReturnToMenu -= EndSession;
            UI.UIManager.Unload();
            RemoteStorageOperations.Unload();
            SettingsSync.Unload();
            ToolNetwork.Unload();
            FeatureNetwork.Unload();
            new HarmonyLib.Harmony("QualityOfTerraria").UnpatchAll("QualityOfTerraria");
            _contentReady = _patchesApplied = false;
            _context = null;
            _instance = null;
            _logger?.Info("QualityOfTerraria 已卸载。");
        }

        private void CheckWorldSession()
        {
            if (WorldActive && !Main.dedServ && Main.gameMenu) EndSession();
        }

        public void OnConfigChanged()
        {
            if (Config == null || Main.netMode != 0) return;
            string error;
            if (!LocalWorldSettings.Save(out error))
            {
                LocalWorldSettings.Restore();
                Main.NewText(error + "，世界设置已恢复原值。", 255, 140, 100);
            }
        }

        private void SaveSession()
        {
            if (!WorldActive) return;
            UI.SuperVaultUI.SaveVaultData();
            UI.ExtremeStorageUI.SaveMarkedChests();
        }

        private void EndSession()
        {
            SaveSession();
            WorldActive = false;
            UI.UIManager.ResetSession();
            if (_serverSession) StorageTransactionJournal.EndServerSession();
            _serverSession = false;
            RemoteStorageOperations.ResetSession();
            SettingsSync.ResetSession();
            ToolNetwork.ResetSession();
            FeatureNetwork.ResetSession();
            Patches.StorageInventoryGuard.ResetSession();
            Patches.WorldUIClickPatch.ResetSession();
            Patches.VanillaUIInputDiagnostics.ResetSession();
            NetworkRuntime.ResetSession();
        }

        private void UpdateNetworkSession()
        {
            if (!WorldActive) return;
            NetworkRuntime.Update();
            RemoteStorageOperations.Update();
            SettingsSync.Update();
            ToolNetwork.Update();
            FeatureNetwork.Update();
            Patches.StorageInventoryGuard.Update();
        }

        public static void LogError(string message, Exception exception)
        {
            _instance?._logger?.Error(message, exception);
        }

        internal static void LogInfo(string message) => _instance?._logger?.Info(message);

        public static string GetKeyLabel(string id, string fallback)
        {
            return _instance?._context?.GetKeybind(id)?.CurrentKey?.ToString() ?? fallback;
        }
    }
}



