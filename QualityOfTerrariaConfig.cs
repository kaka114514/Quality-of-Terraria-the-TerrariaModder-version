using System;
using TerrariaModder.Core.Config;

namespace QualityOfTerraria
{
    public class QualityOfTerrariaConfig : ModConfig
    {
        public override int Version => 1;

        // ================= 物品与收纳 =================
        [Client]
        [Label("物品最大堆叠")]
        [Description("将可堆叠物品的最大堆叠数量修改为9999（金钱除外）。")]
        public bool MaxStack9999 { get; set; } = true;

        [Client]
        [Label("无限药水增益")]
        [Description("当背包或随身收纳中药水达到指定数量时，自动获得该药水的无限增益。")]
        public bool InfinitePotions { get; set; } = true;

        [Client]
        [Label("无限药水所需数量")]
        [Range(1, 999)]
        [Description("药水堆叠数达到此值时激活无限增益（默认30瓶）。")]
        public int PotionStackRequirement { get; set; } = 30;

        [Client]
        [Label("随身增益站与旗帜")]
        [Description("背包或随身收纳中的增益站（灵视水晶球、施法桌、弹药箱、利刃站、蛋糕等）和敌怪旗帜随身生效。")]
        public bool InfiniteStationsAndBanners { get; set; } = true;

        [Client]
        [Label("便携制作站")]
        [Description("背包及随身收纳中的制作台（工作台、熔炉、铁砧、炼金台、工匠作坊等）以及水桶/岩浆桶/蜂蜜桶随身生效。")]
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
        [Label("任务鱼可堆叠")]
        [Description("渔夫任务鱼可无限堆叠（上限9999），且一天内允许垂钓多条任务鱼。")]
        public bool QuestFishStack { get; set; } = true;

        [Client]
        [Label("渔夫任务即时刷新")]
        [Description("提交渔夫任务后立即重置，无需等待第二天，可连续提交。")]
        public bool AnglerQuestNoCD { get; set; } = true;

        [Client]
        [Label("中键打开便携收纳")]
        [Description("在背包中鼠标中键点击存钱罐、钱币槽、保险箱、护卫熔炉、虚空袋直接打开。")]
        public bool MiddleClickOpenBank { get; set; } = true;

        [Client]
        [Label("便携收纳信息配件生效")]
        [Description("存钱罐、保险箱、虚空袋中的信息配件（GPS、雷达、生命体分析仪等）直接生效。")]
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

        [Client]
        [Label("工具与放置加速")]
        [Description("提升工具挥舞、方块放置与墙壁粉刷速度。")]
        public bool FastToolSpeed { get; set; } = true;

        [Client]
        [Label("工具触及距离扩展")]
        [Description("增加物块和工具的作用范围（手长+5格）。")]
        public bool ExtraTileReach { get; set; } = true;

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
        [Label("快速复活")]
        [Description("场上无 Boss 时复活时间缩短为 1.5 秒。")]
        public bool QuickRespawn { get; set; } = true;

        [Client]
        [Label("禁用伤害浮动")]
        [Description("固定伤害输出，移除 85% ~ 115% 随机伤害波动。")]
        public bool NoDamageVariance { get; set; } = false;

        [Client]
        [Label("炸弹不伤害玩家")]
        [Description("炸弹、雷管、炸药等爆炸物不再对玩家自身造成伤害。")]
        public bool BombsNoPlayerDamage { get; set; } = true;

        [Client]
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
        [Label("敌怪生成倍率")]
        [Range(0, 30)]
        [Description("调整生成频率和数量上限；0为和平模式，1为原版。联机由服务器配置。")]
        public float SpawnRateMultiplier { get; set; } = 1f;

        [Client]
        [Label("禁止邪恶群落蔓延")]
        [Description("彻底禁止腐化、猩红与神圣物块草皮自然扩散。")]
        public bool NoEvilSpread { get; set; } = true;

        [Client]
        [Label("全能晶塔")]
        [Description("晶塔随时随地可传：无需靠近、无视NPC数量、战斗中无视危险、无视环境不匹配。")]
        public bool UniversalPylons { get; set; } = true;

        [Client]
        [Label("任意放床设重生点")]
        [Description("野外任意放置床即可直接设定为出生点，无需封闭房间要求。")]
        public bool BedSpawnEverywhere { get; set; } = true;

        [Client]
        [Label("无视睡觉限制")]
        [Description("随时可以上床睡觉，无视怪物接近、白天、事件或Boss战。")]
        public bool NoSleepRestrictions { get; set; } = true;

        [Client]
        [Label("睡觉时间加速倍率")]
        [Range(5, 50)]
        [Description("睡觉时游戏时间推进速度（原版为5倍，可提升至最高50倍）。")]
        public int BedTimeRate { get; set; } = 20;

        [Client]
        [Label("草药速生与随时开花")]
        [Description("草药生长速度大幅加快，且允许在任意时刻开花掉落种子。")]
        public bool FastHerbsAndBloom { get; set; } = true;

        [Client]
        [Label("再生之斧自动补种")]
        [Description("使用再生法杖或再生之斧收割成熟草药时自动补种种子。")]
        public bool StaffOfRegrowthReplant { get; set; } = true;

        [Client]
        [Label("树木速生与无视深度")]
        [Description("树苗极速生长为大树，且无视地下地层限制。")]
        public bool FastTreeGrowth { get; set; } = true;

        [Client]
        [Label("摇树必定掉落水果")]
        [Description("摇树时必定掉落对应的群落水果。")]
        public bool ShakeTreeAlwaysFruit { get; set; } = true;

        [Client]
        [Label("宝石树必定掉落宝石")]
        [Description("砍伐宝石树时必定掉落宝石。")]
        public bool GemTreeAlwaysGem { get; set; } = true;

        [Client]
        [Label("南瓜加速生长")]
        [Description("南瓜生长速度提升 5 倍。")]
        public bool FastPumpkinGrowth { get; set; } = true;

        [Client]
        [Label("生命果加速生长")]
        [Description("丛林生命果生成与生长速度加快。")]
        public bool FastLifeFruitGrowth { get; set; } = true;

        [Client]
        [Label("提炼机加速")]
        [Description("提炼机处理泥沙、雪泥、沙漠化石无冷却瞬间完成。")]
        public bool FastExtractinator { get; set; } = true;

        [Client]
        [Label("钓鱼无水池大小惩罚")]
        [Description("任意大小水池（包括极小池塘）均具备满渔力，不再受到渔力削弱。")]
        public bool NoFishingPenalty { get; set; } = true;

        // ================= NPC 与图鉴 =================
        [Client]
        [Label("城镇NPC免伤无敌")]
        [Description("城镇NPC免疫怪物、熔岩和环境伤害，不再意外阵亡。")]
        public bool TownNPCImmortal { get; set; } = true;

        [Client]
        [Label("城镇NPC极速复活")]
        [Description("城镇NPC阵亡后无需长时间等待，满足条件瞬间重新入住。")]
        public bool TownNPCFastSpawn { get; set; } = true;

        [Client]
        [Label("城镇NPC自动与夜晚入住")]
        [Description("哥布林、巫师、机械师等满足条件直接入住无需寻找；NPC可在夜晚入住。")]
        public bool TownNPCConvenientMoveIn { get; set; } = true;

        [Client]
        [Label("城镇NPC可住邪恶环境")]
        [Description("房屋处于腐化或猩红之地时依然判定为有效住房。")]
        public bool TownNPCLiveInEvil { get; set; } = true;

        [Client]
        [Label("固定最高NPC快乐度")]
        [Description("所有NPC买卖价格维持在最优惠区间（买东西75折最低价，永久出售晶塔）。")]
        public bool MaxNPCHappiness { get; set; } = true;

        [Client]
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

        [Client]
        [Label("熔岩史莱姆不产生岩浆")]
        [Description("专家/大师模式下熔岩史莱姆死亡时不再生成熔岩。")]
        public bool LavalessLavaSlime { get; set; } = true;

        // ================= 快捷工具与魔杖 =================
        [Client]
        [Label("建筑魔杖不消耗材料")]
        [Description("快速放置和快速铺墙时不消耗背包材料。")]
        public bool WandMaterialNoConsume { get; set; } = false;
    }
}

