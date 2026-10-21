using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerrariaModder.Core.Config;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("QualityOfTerraria.Server")]

namespace QualityOfTerraria
{
    public class QualityOfTerrariaConfig : ModConfig
    {
        internal bool NeedsVeinMinerImport;
        internal string VeinImportStateError;
        private bool _veinImportChecked;
        internal static readonly string[] VeinSettingKeys = {
            nameof(VeinMiningEnabled), nameof(VeinMiningAutoLoot), nameof(VeinMiningRange), nameof(VeinMiningMaxBlocks),
            nameof(VeinMiningCheckPickaxePower), nameof(VeinMiningMatchExactType), nameof(VeinMiningShowMessages),
            nameof(ToolSpeedMultiplier), nameof(PlacementSpeedMultiplier)
        };

        public override int Version
        {
            get
            {
                // Core 会在加载迁移后立即保存。首次读取版本时保留保存前的导入状态。
                if (!_veinImportChecked && ConfigManager.GetCurrentScope() == ConfigScope.Client && !string.IsNullOrEmpty(FilePath))
                {
                    _veinImportChecked = true;
                    try
                    {
                        if (File.Exists(FilePath + ".vein-import.pending") || !File.Exists(FilePath)) NeedsVeinMinerImport = true;
                        else
                        {
                            var raw = ConfigManager.ParseJsonRaw(File.ReadAllText(FilePath));
                            NeedsVeinMinerImport = !raw.TryGetValue("_version", out object version) || Convert.ToInt32(version) < 3
                                || VeinSettingKeys.Take(7).Any(key => !raw.ContainsKey(key));
                        }
                        // 自动保存前落盘，导入失败／中断重启后仍能重试；不向配置面板添加状态项。
                        if (NeedsVeinMinerImport) File.WriteAllText(FilePath + ".vein-import.pending", "3");
                    }
                    catch (Exception exception) { VeinImportStateError = exception.Message; }
                }
                return 4;
            }
        }

        protected override void Migrate(Dictionary<string, object> raw, int oldVersion)
        {
            if (!raw.ContainsKey(nameof(ToolSpeedMultiplier)))
            {
                if (raw.TryGetValue("DigSpeedBoost", out object dig)) ToolSpeedMultiplier = 1f + Convert.ToSingle(dig);
                else if (raw.TryGetValue("AxeSpeedBoost", out object axe)) ToolSpeedMultiplier = 1f + Convert.ToSingle(axe);
                else if (raw.TryGetValue("FastToolSpeed", out object fast)) ToolSpeedMultiplier = Convert.ToBoolean(fast) ? 2f : 1f;
            }
            if (!raw.ContainsKey(nameof(PlacementSpeedMultiplier)))
            {
                if (raw.TryGetValue("PlaceSpeedBoost", out object place)) PlacementSpeedMultiplier = 1f + Convert.ToSingle(place);
                else if (raw.TryGetValue("FastToolSpeed", out object fast)) PlacementSpeedMultiplier = Convert.ToBoolean(fast) ? 2f : 1f;
            }
            if (raw.TryGetValue(nameof(ExtraTileReach), out object reach) && reach is bool enabled)
                ExtraTileReach = enabled ? 5 : 0;
            if (raw.TryGetValue(nameof(UniversalPylons), out object pylons) && pylons is bool universal)
            {
                if (!raw.ContainsKey(nameof(PylonPlacementUnrestricted))) PylonPlacementUnrestricted = universal;
                if (!raw.ContainsKey(nameof(PylonTeleportWithoutNearbyPlayer))) PylonTeleportWithoutNearbyPlayer = universal;
                if (!raw.ContainsKey(nameof(PylonTeleportIgnoreDanger))) PylonTeleportIgnoreDanger = universal;
                if (!raw.ContainsKey(nameof(PylonTeleportIgnoreBiome))) PylonTeleportIgnoreBiome = universal;
            }
        }

        // ================= 物品与收纳 =================

        [Client]
        [Label("无限药水增益")]
        [Description("背包、便携收纳、大背包和自己标记的全世界箱子内，同种药水累计达到所需数量时持续生效，无需打开面板或靠近箱子。")]
        public bool InfinitePotions { get; set; } = true;

        [Client]
        [Label("无限药水所需数量")]
        [Range(1, 999)]
        [Description("同种药水跨格、跨箱累计达到此值时激活增益（默认30瓶）；不消耗药水。")]
        public int PotionStackRequirement { get; set; } = 30;

        [Client]
        [Label("无限红药水增益")]
        [Description("红药水达到所需数量时，获得原模组扩展的18种药水增益。特殊种子世界或开启随处可用时生效。")]
        public bool InfiniteRedPotion { get; set; } = true;

        [Client]
        [Label("红药水随处可用")]
        [Description("允许非特殊种子世界直接使用红药水获得原版正面增益，并允许无限红药水扩展。")]
        public bool RedPotionEverywhere { get; set; } = true;

        [Client]
        [Label("无限红药水所需数量")]
        [Range(1, 300)]
        public int RedPotionRequirement { get; set; } = 20;

        [Client]
        [Label("随身增益站")]
        [Description("背包、随身收纳或自己标记箱子中的增益站（灵视水晶球、施法桌、弹药箱、利刃站、蛋糕等）随身生效。")]
        public bool InfiniteStationsAndBanners { get; set; } = true;

        [Client]
        [Label("便携制作站")]
        [Description("开启后，背包、便携收纳、大背包及自己标记的全世界箱子中的制作站和水桶/岩浆桶/蜂蜜桶提供合成环境；关闭仅取消便携环境，箱内材料仍可远程合成。")]
        public bool PortableCraftingStations { get; set; } = true;

        [Client]
        [Label("金币自动存入猪猪钱罐")]
        [Description("拾取的金币自动转移至猪猪钱罐中，防止意外掉落。")]
        public bool AutoSaveCoins { get; set; } = true;

        [Client]
        [Label("无限弹药")]
        [Description("弹药堆叠达到3996（或坠落之星达到999）时不消耗弹药。")]
        public bool InfiniteAmmo { get; set; } = true;

        [Client]
        [Label("召唤物不消耗")]
        [Description("所有Boss与事件召唤物在使用时不消耗；松露虫不消耗；草蛉不消失。")]
        public bool InfiniteSummonItems { get; set; } = true;

        [Client]
        [Label("无限投掷消耗品")]
        [Description("投掷物/手榴弹/荧光棒等武器与消耗品堆叠达到3996时不消耗。")]
        public bool InfiniteThrowables { get; set; } = true;

        [Client]
        [Label("无限电线与促动器")]
        [Description("放置电线与促动器堆叠达到3996时不消耗。")]
        public bool InfiniteWire { get; set; } = true;

        [Client]
        [Label("渔夫任务即时刷新")]
        [Description("提交渔夫任务后立即重置，无需等待第二天，可连续提交。")]
        public bool AnglerQuestNoCD { get; set; } = true;

        [Client]
        [Label("便携收纳信息配件生效")]
        [Description("玩家背包、钱罐、保险箱、护卫熔炉、虚空袋和自己标记的箱子中的信息配件生效；标记箱不限距离，无需打开面板。")]
        public bool BankInfoAccessories { get; set; } = true;

        [Client]
        [Label("可追溯重铸 (词缀回滚)")]
        [Description("在哥布林工匠重铸装备时记录历史词缀，防止意外跳过神级/虚幻/传奇等极品。")]
        public bool ImprovePrefix { get; set; } = true;

        // ================= 玩家与战斗 =================
        [Client]
        [Label("物品拾取范围提升 (格)")]
        [Range(0, 50)]
        [Description("大幅扩大拾取物品的磁吸范围与吸引速度。")]
        public int ExtraGrabRange { get; set; } = 15;

        private float _toolSpeedMultiplier = 3f, _placementSpeedMultiplier = 1f;
        internal static float NormalizeSpeed(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? 1f : (float)Math.Round((decimal)Math.Max(1f, Math.Min(4f, value)), 1, MidpointRounding.AwayFromZero);

        [Client]
        [Label("挖掘与伐木加速")]
        [Range(1, 4)]
        [Description("镐、斧、锤共用倍率，1.0至4.0倍，步长0.1；拖动滑条或点击右侧数值输入，回车确认。1.0为原版速度。")]
        public float ToolSpeedMultiplier { get => _toolSpeedMultiplier; set => _toolSpeedMultiplier = NormalizeSpeed(value); }

        [Client]
        [Label("放置加速")]
        [Range(1, 4)]
        [Description("方块和背景墙的放置倍率，1.0至4.0倍，步长0.1；拖动滑条或点击右侧数值输入，回车确认。")]
        public float PlacementSpeedMultiplier { get => _placementSpeedMultiplier; set => _placementSpeedMultiplier = NormalizeSpeed(value); }

        [Client]
        [Label("连锁挖矿")]
        [Description("挖掘矿石时连锁开采相连矿石；V切换，按住Shift临时强制启用。")]
        public bool VeinMiningEnabled { get; set; } = true;

        [Client]
        [Label("矿石自动入包")]
        [Description("开采的矿石按原版拾取规则收进背包，背包满时保留地面掉落。")]
        public bool VeinMiningAutoLoot { get; set; } = true;

        [Client]
        [Label("连锁挖矿范围（格）")]
        [Range(1, 30)]
        public int VeinMiningRange { get; set; } = 12;

        [Client]
        [Label("连锁挖矿最大数量")]
        [Range(1, 500)]
        public int VeinMiningMaxBlocks { get; set; } = 64;

        [Client]
        [Label("连锁挖矿检查镐力")]
        [Description("连锁开采时检查所持工具镐力；关闭仍保留原版地形保护。")]
        public bool VeinMiningCheckPickaxePower { get; set; } = true;

        [Client]
        [Label("连锁挖矿仅匹配同种矿石")]
        public bool VeinMiningMatchExactType { get; set; } = true;

        [Client]
        [Label("连锁挖矿操作提示")]
        public bool VeinMiningShowMessages { get; set; } = true;

        [Client]
        [Label("物块放置与工具触及距离增加量（格）")]
        [Range(0, 30)]
        [Description("在原版范围上增加指定格数，同时作用于物块放置与工具；0不扩展，默认5格。")]
        public int ExtraTileReach { get; set; } = 5;

        [Client]
        [Label("魔镜 / 回程瞬传")]
        [Description("使用魔镜、冰雪镜、手机、回忆药水时瞬间传送，移除等待前摇。")]
        public bool InstantRecall { get; set; } = true;

        [Client]
        [Label("死亡不生成墓碑")]
        [Description("玩家死亡时不再生成墓碑，保护建筑环境。")]
        public bool NoTombstones { get; set; } = true;

        [Client]
        [Label("死亡保存增益")]
        [Description("玩家死亡复活后维持增益效果不消失。")]
        public bool PreserveBuffsOnDeath { get; set; } = true;

        [Client]
        [Label("满血复活")]
        [Description("玩家重生时立即恢复全部生命值与魔力值。")]
        public bool RespawnFullHealth { get; set; } = true;

        [Client]
        [Label("重生加速（非BOSS战）")]
        [Description("场上无 Boss 时复活时间缩短为 1.5 秒。")]
        public bool QuickRespawn { get; set; } = true;

        [Client]
        [Label("重生加速（BOSS战）")]
        [Description("BOSS战中复活时间独立缩短为1.5秒，不受非BOSS战开关影响。")]
        public bool BossQuickRespawn { get; set; } = true;

        [Server]
        [Label("禁用伤害浮动")]
        [Description("固定伤害输出，移除 85% ~ 115% 随机伤害波动。")]
        public bool NoDamageVariance { get; set; } = false;

        [Client]
        [Label("炸弹不伤害玩家")]
        [Description("炸弹、雷管、炸药等爆炸物不再对玩家自身造成伤害。")]
        public bool BombsNoPlayerDamage { get; set; } = true;

        [Server]
        [Label("敌怪爆炸物不破坏物块")]
        [Description("小丑炸弹等非玩家爆炸物不再炸毁物块。")]
        public bool DisableNonPlayerBombTileDamage { get; set; } = true;

        [Client]
        [Label("失焦后台持续运行")]
        [Description("游戏窗口切换到后台时不暂停、不静音。")]
        public bool KeepFocus { get; set; } = false;

        [Client]
        [Label("无条件队内传送")]
        [Description("虫洞药水传送无条件，且传送时不消耗虫洞药水。")]
        public bool TeamUnconditionalTeleport { get; set; } = true;

        // ================= 世界与生态 =================
        [Server]
        [Label("敌怪生成倍率")]
        [Range(0, 100)]
        [Description("调整生成频率和数量上限；0为和平模式，1为原版。联机由服务器配置。")]
        public float SpawnRateMultiplier { get; set; } = 1f;

        [Server]
        [Label("禁止邪恶群落蔓延")]
        [Description("彻底禁止腐化、猩红与神圣物块草皮自然扩散。")]
        public bool NoEvilSpread { get; set; } = true;

        [Server]
        [Label("晶塔传送无视NPC数量")]
        [Description("保留旧全能晶塔的NPC数量豁免；其他晶塔限制由各自独立开关控制，联机由服务器执行。")]
        public bool UniversalPylons { get; set; } = true;

        [Server]
        [Label("晶塔放置无限制")]
        [Description("允许放置多个相同类型的晶塔，仍需正常的放置空间。")]
        public bool PylonPlacementUnrestricted { get; set; } = true;

        [Server]
        [Label("晶塔传送无需玩家在附近")]
        public bool PylonTeleportWithoutNearbyPlayer { get; set; } = true;

        [Server]
        [Label("晶塔传送无视危险环境")]
        [Description("兼容危险判定开关；Terraria 1.4.5.8原版晶塔传送已不检查战斗危险，此版本不额外改变该规则。")]
        public bool PylonTeleportIgnoreDanger { get; set; } = true;

        [Server]
        [Label("晶塔传送无视生物群系")]
        public bool PylonTeleportIgnoreBiome { get; set; } = true;

        [Server]
        [Label("敌人钱币掉落倍率")]
        [Range(1, 25)]
        [Description("服务器在敌怪实际钱币掉落计算时应用一次，不修改NPC基础价值；1为原版。")]
        public int CoinDropMultiplier { get; set; } = 1;

        [Server]
        [Label("旅商商店可刷新")]
        [Description("旅商商店内允许申请刷新货物，服务器生成并同步。")]
        public bool RefreshTravelShop { get; set; } = true;

        [Client]
        [Label("任意放床设重生点")]
        [Description("野外任意放置床即可直接设定为出生点，无需封闭房间要求。")]
        public bool BedSpawnEverywhere { get; set; } = true;

        [Client]
        [Label("无视睡觉限制")]
        [Description("随时可以上床睡觉，无视怪物接近、白天、事件或Boss战。")]
        public bool NoSleepRestrictions { get; set; } = true;

        [Server]
        [Label("睡觉时间加速倍率")]
        [Range(5, 50)]
        [Description("睡觉时游戏时间推进速度（原版为5倍，可提升至最高50倍）。")]
        public int BedTimeRate { get; set; } = 20;

        [Server]
        [Label("草药速生与随时开花")]
        [Description("草药生长速度大幅加快，且允许在任意时刻开花掉落种子。")]
        public bool FastHerbsAndBloom { get; set; } = true;

        [Server]
        [Label("再生之斧自动补种")]
        [Description("使用再生法杖或再生之斧收割成熟草药时自动补种种子。")]
        public bool StaffOfRegrowthReplant { get; set; } = true;

        [Server]
        [Label("树木速生与无视深度")]
        [Description("树苗极速生长为大树，且无视地下地层限制。")]
        public bool FastTreeGrowth { get; set; } = true;

        [Server]
        [Label("摇树必定掉落水果")]
        [Description("摇树时必定掉落对应的群落水果。")]
        public bool ShakeTreeAlwaysFruit { get; set; } = true;

        [Server]
        [Label("宝石树必定掉落宝石")]
        [Description("砍伐宝石树时必定掉落宝石。")]
        public bool GemTreeAlwaysGem { get; set; } = true;

        [Server]
        [Label("南瓜加速生长")]
        [Description("南瓜生长速度提升 5 倍。")]
        public bool FastPumpkinGrowth { get; set; } = true;

        [Server]
        [Label("生命果加速生长")]
        [Description("丛林生命果生成与生长速度加快。")]
        public bool FastLifeFruitGrowth { get; set; } = true;

        [Client]
        [Label("提炼机加速")]
        [Description("提炼机处理泥沙、雪泥、沙漠化石无冷却瞬间完成。")]
        public bool FastExtractinator { get; set; } = true;

        [Client]
        [Label("自动钓鱼间隔（秒）")]
        [Range(1, 60)]
        [Description("自动钓鱼每次尝试之间的间隔，默认10秒。")]
        public int AutoFishingIntervalSeconds { get; set; } = 10;

        [Client]
        [Label("钓鱼无水池大小惩罚")]
        [Description("任意大小水池（包括极小池塘）均具备满渔力，不再受到渔力削弱。")]
        public bool NoFishingPenalty { get; set; } = true;

        // ================= NPC 与图鉴 =================
        [Server]
        [Label("城镇NPC免伤无敌")]
        [Description("城镇NPC免疫怪物、熔岩和环境伤害，不再意外阵亡。")]
        public bool TownNPCImmortal { get; set; } = true;

        [Server]
        [Label("城镇NPC极速复活")]
        [Description("城镇NPC阵亡后无需长时间等待，满足条件瞬间重新入住。")]
        public bool TownNPCFastSpawn { get; set; } = true;

        [Server]
        [Label("城镇NPC自动与夜晚入住")]
        [Description("哥布林、巫师、机械师等满足条件直接入住无需寻找；NPC可在夜晚入住。")]
        public bool TownNPCConvenientMoveIn { get; set; } = true;

        [Server]
        [Label("城镇NPC可住邪恶环境")]
        [Description("房屋处于腐化或猩红之地时依然判定为有效住房。")]
        public bool TownNPCLiveInEvil { get; set; } = true;

        [Client]
        [Label("固定最高NPC快乐度")]
        [Description("所有NPC买卖价格维持在最优惠区间（买东西75折最低价，永久出售晶塔）。")]
        public bool MaxNPCHappiness { get; set; } = true;

        [Server]
        [Label("旅商永不离开")]
        [Description("旅商入住后除非死亡不会离开世界。")]
        public bool TravellingMerchantStays { get; set; } = true;

        [Client]
        [Label("一次击杀完全解锁图鉴")]
        [Description("击杀 1 次敌怪即完全解锁其所有图鉴数据（掉落物及概率）。")]
        public bool OneKillBestiary { get; set; } = true;

        [Client]
        [Label("发型师解锁全部发型")]
        [Description("发型师解锁游戏内所有发型供随意更换。")]
        public bool StylistUnlockAllHair { get; set; } = true;

        [Server]
        [Label("熔岩史莱姆不产生岩浆")]
        [Description("专家/大师模式下熔岩史莱姆死亡时不再生成熔岩。")]
        public bool LavalessLavaSlime { get; set; } = true;

        // ================= 快捷工具与魔杖 =================
        [Client]
        [Label("建筑魔杖不消耗材料")]
        [Description("快速放置时不消耗背包材料。")]
        public bool WandMaterialNoConsume { get; set; } = false;
    }
}
