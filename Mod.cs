using System;
using TerrariaModder.Core;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.Events;
using Terraria;
using QualityOfTerraria.Networking;
using Terraria.GameInput;
using TerrariaModder.Core.Input;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria
{
    public class Mod : ModBase, IModLifecycle
    {
        public static QualityOfTerrariaConfig Config { get; private set; }
        public static bool WorldActive { get; private set; }
        internal static bool VeinMinerLegacyLoaded { get; private set; }
        private static Mod _instance;
        private ModContext _context;
        private ILogger _logger;
        private bool _serverSession;
        private bool _contentReady, _patchesApplied;
#if !QOT_SERVER
        private IMod _serverRuntime;
#endif
#if QOT_SERVER
        private const string HarmonyId = "QualityOfTerraria.Server";
#else
        private const string HarmonyId = "QualityOfTerraria";
#endif

        public override void Initialize(ModContext context)
        {
#if !QOT_SERVER
            if (context.IsServer)
            {
                _serverRuntime = ServerRuntime.Load(context);
                return;
            }
#endif
            _logger = context.Logger;
            _instance = this;
            _context = context;
            Config = context.GetConfig<QualityOfTerrariaConfig>();
            VeinMinerLegacyLoaded = context.IsModLoaded("vein-miner");
            LocalWorldSettings.Initialize();
            Features.VeinConfigMigration.Apply(context);

            // 注册常用快捷键（严格对应用户指定键位，杜绝占用 F6~F12）
            context.RegisterKeybind("space-wand", "快速放置", "切换快速放置，开启后手持方块/平台/墙按住鼠标左键拖拽铺设", "P", WandsAndTools.ExecuteQuickPlace);
            context.RegisterKeybind("liquid-wand", "液体魔杖模式切换", "循环切换关/水/岩浆/微光/蜂蜜模式，按住[;]吸收，按住[']生成", "L", WandsAndTools.ExecuteLiquidWand);
            context.RegisterKeybind("npc-home", "城镇NPC立即回家", "一键将所有城镇NPC召回各自房间", "N", WandsAndTools.ExecuteNPCHome);
            context.RegisterKeybind("quick-nurse", "快捷护士瞬间治疗", "一键护士回满血与魔力并清除Debuff(按原版收费)", "H", WandsAndTools.ExecuteQuickNurse);
            context.RegisterKeybind("open-bank", "打开快捷收纳", "选择猪猪钱罐、保险箱、护卫熔炉或虚空袋", "PERIOD", WandsAndTools.ExecuteOpenBank);
            context.RegisterKeybind("super-vault", "随身大背包", "呼出100格额外随身独立仓库", "B", UI.SuperVaultUI.Toggle);

            // 工具与辅助功能
            context.RegisterKeybind("weather-control", "切换晴雨", "切换下雨和放晴", "U", WandsAndTools.ExecuteWeatherController);

            // 大型 UI 界面与特色系统快捷键（已完全避开 F6~F12）
            context.RegisterKeybind("extreme-storage", "终极存储大面板", "呼出集中仓储大面板，支持分类浏览与存取", "K", UI.ExtremeStorageUI.Toggle);
            context.RegisterKeybind("player-stats", "角色实时属性面板", "实时监控各项攻防暴击穿透属性", "Y", UI.PlayerStatsUI.Toggle);
            context.RegisterKeybind("structure-locator", "小地图结构定位球", "打开小地图结构定位面板，快速扫描定位并点亮迷雾", "G", UI.StructureLocatorUI.Toggle);
            context.RegisterKeybind("spawn-rate-control", "世界控制", "调节刷怪率、时间、节日和特殊事件；联机由房主或管理员控制", "OPENBRACKET", UI.BuffAndSpawnRateUI.Toggle);
            context.RegisterKeybind("autofisher", "自动钓鱼机", "背包内有可用鱼竿及鱼饵，在液体附近启用，每10秒尝试一次原版钓鱼结算", "Z", Features.Autofisher.Toggle);
            context.RegisterKeybind("target-dummy", "测试假人桩", "在鼠标处放置或移除测试假人，实时统计DPS", "X", Features.TargetDummySystem.ToggleOrSpawnAtCursor);
            context.RegisterKeybind("toggle-vein-mining", "切换连锁挖矿", "开启或关闭矿石连锁挖掘", "V", Features.VeinMiningSystem.Toggle);
            context.RegisterKeybind("toggle-speed-boost", "切换工具加速", "切换挖掘、砍伐和放置加速，不改变各自速度设置", "Backslash", Features.ToolSpeedSystem.Toggle);
            context.RegisterKeybind("force-vein-mine", "强制连锁挖矿（按住）", "按住此键挖矿，临时启用连锁；松开恢复原开关", "LeftShift", null);

            if (!context.IsServer) UI.UIManager.Initialize();
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
            var harmony = new HarmonyLib.Harmony(HarmonyId);
            foreach (var type in typeof(Mod).Assembly.GetTypes())
            {
                // 矿石来源补丁在两种运行模块里都安装，由 Main.netMode==2 自行判断是否生效。
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
#if !QOT_SERVER
            if (_serverRuntime != null) { ((IModLifecycle)_serverRuntime).OnContentReady(context); return; }
#endif
            VeinMinerLegacyLoaded = context.IsModLoaded("vein-miner");
            Features.VeinMiningSystem.Suspended = VeinMinerLegacyLoaded;
            if (VeinMinerLegacyLoaded) _logger?.Warn("独立vein-miner仍在加载，更好的体验暂停重复连锁与速度功能；请只保留其中一个。");
            _contentReady = true;
            ApplyReadyPatches();
            _logger?.Info("QualityOfTerraria: 内容已就绪。");
        }

        public void OnWorldLoad()
        {
#if !QOT_SERVER
            if (_serverRuntime != null) { ((IModLifecycle)_serverRuntime).OnWorldLoad(); return; }
#endif
            if (!Main.dedServ) UI.UIManager.ResetSession();
            else UI.RemoteStorageSnapshots.ResetSession();
            WorldActive = true;
            Features.WorldControlSystem.OnWorldLoad();
            _serverSession = Main.netMode == 2;
            if (Main.netMode == 0) LocalWorldSettings.Restore();
            RemoteStorageOperations.Initialize();
            SettingsSync.Initialize();
            ToolNetwork.Initialize();
            FeatureNetwork.Initialize();
            VeinMiningNetwork.Initialize();
            Features.VeinMiningSystem.Suspended = VeinMinerLegacyLoaded;
            UI.RemoteStorageSnapshots.Initialize();
            if (!Main.dedServ)
            {
                UI.SuperVaultUI.EnsureLoadedForCurrentPlayer();
                UI.ExtremeStorageUI.EnsureLoadedForCurrentWorld();
            }
            _logger?.Info("QualityOfTerraria: 世界已加载，网络会话已启动。");
        }

        public void OnWorldUnload()
        {
#if !QOT_SERVER
            if (_serverRuntime != null) { ((IModLifecycle)_serverRuntime).OnWorldUnload(); return; }
#endif
            EndSession();
        }

        public override void Unload()
        {
#if !QOT_SERVER
            if (_serverRuntime != null) { _serverRuntime.Unload(); _serverRuntime = null; return; }
#endif
            EndSession();
            FrameEvents.OnPreUpdate -= CheckWorldSession;
            FrameEvents.OnPreUpdate -= UpdateNetworkSession;
            GameEvents.OnWorldSave -= SaveSession;
            GameEvents.OnReturnToMenu -= EndSession;
            if (_context?.IsServer != true) UI.UIManager.Unload();
            RemoteStorageOperations.Unload();
            SettingsSync.Unload();
            ToolNetwork.Unload();
            FeatureNetwork.Unload();
            VeinMiningNetwork.Unload();
            new HarmonyLib.Harmony(HarmonyId).UnpatchAll(HarmonyId);
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
#if !QOT_SERVER
            if (_serverRuntime != null) return;
#endif
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
            if (!WorldActive || Main.dedServ) return;
            UI.SuperVaultUI.SaveVaultData();
            UI.ExtremeStorageUI.SaveMarkedChests();
        }

        private void EndSession()
        {
            if (!Main.dedServ) UI.SpeedSettingsUI.Reset();
            SaveSession();
            WorldActive = false;
            if (!Main.dedServ) UI.UIManager.ResetSession();
            else
            {
                UI.RemoteStorageSnapshots.ResetSession();
                Features.StructureLocator.ResetSession();
                Features.TargetDummySystem.ResetSession();
            }
            if (_serverSession) StorageTransactionJournal.EndServerSession();
            _serverSession = false;
            RemoteStorageOperations.ResetSession();
            SettingsSync.ResetSession();
            ToolNetwork.ResetSession();
            FeatureNetwork.ResetSession();
            VeinMiningNetwork.ResetSession();
            Features.VeinMiningSystem.ResetSession();
            Features.ToolSpeedSystem.ResetSession();
            Patches.StorageInventoryGuard.ResetSession();
            Patches.WorldUIClickPatch.ResetSession();
            Patches.VanillaUIInputDiagnostics.ResetSession();
            NetSyncPayload.Reset();
            NetworkRuntime.ResetSession();
        }

        private void UpdateNetworkSession()
        {
            if (!WorldActive) return;
            NetworkRuntime.Update();
            WandsAndTools.UpdatePlacementJobs();
            RemoteStorageOperations.Update();
            SettingsSync.Update();
            ToolNetwork.Update();
            FeatureNetwork.Update();
            VeinMiningNetwork.Update();
            Features.VeinMiningSystem.IsForced = IsForceVeinMiningHeld();
            Features.VeinMiningSystem.Update();
            if (Main.dedServ)
            {
                Features.StructureLocator.Update();
                Features.TargetDummySystem.Update();
            }
            Patches.StorageInventoryGuard.Update();
        }

#if QOT_SERVER
        internal static void UpdateServerSession()
        {
            if (_instance == null || Main.netMode != 2) return;
            if (WorldGen.isGeneratingOrLoadingWorld || Main.ActiveWorldFileData == null)
            {
                if (WorldActive) _instance.EndSession();
                return;
            }
            if (!WorldActive) _instance.OnWorldLoad();
            // 专服模式下 Core 0.4.1 跳过 FrameEvents；网络回调必须在这里排空。
            _instance.UpdateNetworkSession();
        }
#endif

        internal static bool IsForceVeinMiningHeld()
        {
            if (!WorldActive || Main.gameMenu || Main.dedServ || Main.netMode == 2 || VeinMinerLegacyLoaded ||
                Main.LocalPlayer == null || !Main.LocalPlayer.active || Main.LocalPlayer.dead || Main.LocalPlayer.mouseInterface ||
                Main.drawingPlayerChat || Main.editSign || Main.editChest || PlayerInput.WritingText || UIRenderer.IsWaitingForKeyInput ||
                Patches.StorageInventoryGuard.Locked) return false;
            KeyCombo key = _instance?._context?.GetKeybind("force-vein-mine")?.CurrentKey;
            if (key == null) return false;
            // Core把单独Shift也与Shift修饰符作严格比较；处理修饰键自身，其他按键继续沿用改键规则。
            if (key.Key == 16 || key.Key == 160 || key.Key == 161)
                return (key.Key == 16 ? InputState.IsShiftDown() : InputState.IsKeyDown(key.Key)) &&
                    key.Ctrl == InputState.IsCtrlDown() && key.Alt == InputState.IsAltDown();
            return key.IsPressed();
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



