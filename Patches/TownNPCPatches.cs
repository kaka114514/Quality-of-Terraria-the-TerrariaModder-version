using System;
using HarmonyLib;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using QualityOfTerraria.Features;
using Microsoft.Xna.Framework;

namespace QualityOfTerraria.Patches
{
    public static class TownNPCPatches
    {
        [ThreadStatic]
        private static Projectile _damageProjectile;

        // 保留弹幕上下文，使碰撞与爆炸的重复回调共享来源身份。
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Damage))]
        public static class DummyProjectileSourcePatch
        {
            public static void Prefix(Projectile __instance, out Projectile __state)
            {
                __state = _damageProjectile;
                _damageProjectile = __instance;
            }

            public static void Finalizer(Projectile __state)
            {
                _damageProjectile = __state;
            }
        }

        // 1. 固定 NPC 最优快乐度 (价格最低 75 折，卖出最高价，永久出售晶塔)
        [HarmonyPatch(typeof(Terraria.GameContent.ShopHelper), nameof(Terraria.GameContent.ShopHelper.GetShoppingSettings))]
        public static class MaxHappinessPatch
        {
            public static void Postfix(ref ShoppingSettings __result)
            {
                if (Mod.Config != null && Mod.Config.MaxNPCHappiness)
                {
                    __result.PriceAdjustment = 0.75f; // 最优价格折扣
                }
            }
        }

        // 2. 击杀一次即可完全解锁图鉴全条目及掉落物概率
        [HarmonyPatch(typeof(NPCKillsTracker), nameof(NPCKillsTracker.GetKillCount), typeof(string))]
        public static class BestiaryUnlockPatch
        {
            public static void Postfix(ref int __result)
            {
                if (Mod.Config != null && Mod.Config.OneKillBestiary)
                {
                    if (__result > 0)
                    {
                        __result = 50; // 返回足够多的击杀数，完全点亮图鉴与掉落物
                    }
                }
            }
        }

        // 3. 旅商永不离开 & 测试假人永不因距离消散
        [HarmonyPatch(typeof(NPC), nameof(NPC.CheckActive))]
        public static class TravellingMerchantStayPatch
        {
            public static bool Prefix(NPC __instance)
            {
                if (__instance == null || !__instance.active)
                    return true;

                // 测试假人永不自动消散
                if (__instance.type == NPCID.TargetDummy)
                {
                    return false;
                }

                if (Mod.Config != null && Mod.Config.TravellingMerchantStays)
                {
                    if (__instance.type == NPCID.TravellingMerchant)
                    {
                        return false; // 阻止旅商夜晚离开消失
                    }
                }
                return true;
            }
        }

        // 4. 城镇 NPC 满足条件直接入住无需搜寻，夜晚可入住 & 极速复活
        [HarmonyPatch(typeof(Main), "UpdateTime_SpawnTownNPCs")]
        public static class TownNPCSpawnPatch
        {
            private static readonly System.Reflection.MethodInfo _spawnTownNPCs = AccessTools.Method(typeof(Main), "UpdateTime_SpawnTownNPCs");
            private static bool _spawningExtra;

            public static void Prefix()
            {
                if (Mod.Config != null && Mod.Config.TownNPCConvenientMoveIn)
                {
                    if (NPC.downedGoblins) NPC.savedGoblin = true;
                    if (NPC.downedBoss3) NPC.savedMech = true;
                    if (Main.hardMode)
                    {
                        NPC.savedWizard = true;
                        NPC.savedTaxCollector = true;
                    }
                    if (NPC.downedQueenBee) NPC.savedStylist = true;
                }
            }

            public static void Postfix()
            {
                if (Mod.Config != null && Mod.Config.TownNPCFastSpawn && !_spawningExtra)
                {
                    _spawningExtra = true;
                    // 多次触发生成检查，快速填补空房屋
                    for (int i = 0; i < 4; i++)
                    {
                        _spawnTownNPCs?.Invoke(null, null);
                    }
                    _spawningExtra = false;
                }
            }
        }

        // 5. 城镇 NPC 免伤无敌 (免疫敌怪攻击、Boss横扫、熔岩)
        [HarmonyPatch(typeof(NPC), "StrikeNPC")]
        public static class TownNPCImmortalPatch
        {
            public static bool Prefix(NPC __instance, ref int __result)
            {
                if (Mod.Config != null && Mod.Config.TownNPCImmortal && __instance != null)
                {
                    if (__instance.townNPC || __instance.type == NPCID.OldMan || __instance.type == NPCID.TravellingMerchant)
                    {
                        __result = 0;
                        return false; // 城镇NPC完全免疫伤害
                    }
                }
                return true;
            }

            public static void Postfix(NPC __instance, bool fromNet, int owner, int __result)
            {
                // 必须严格过滤：
                // 1. 目标必须是测试假人
                // 2. 排除网络同步广播回包 (!fromNet)，杜绝联机或发包导致的伤害数倍重叠！
                // 3. 来源过滤：单机模式下 owner 往往是 255 或 Main.myPlayer；多人模式下必须为 Main.myPlayer
                if (__instance != null && __instance.type == NPCID.TargetDummy && __result > 0 && !fromNet)
                {
                    if (Main.netMode == 0 || owner == Main.myPlayer || owner == 255)
                    {
                        if (_damageProjectile != null)
                        {
                            // 1.4.5 使用含生成代次的 ProjectileKey，避免复用弹幕槽时合并不同攻击。
                            long source = (long)(uint)_damageProjectile.key + 1;
                            TargetDummySystem.RecordDamage(__instance.whoAmI, __result, source);
                        }
                        else
                        {
                            // 没有弹幕上下文的实际 StrikeNPC 调用分别计数，避免合并合法近战/额外攻击。
                            TargetDummySystem.RecordDamage(__instance.whoAmI, __result);
                        }
                    }
                }
            }
        }

        // 5.1 拦截 TargetDummy 的原版 AI，防止因未绑定物理 Tile 而被原版自动清除
        [HarmonyPatch(typeof(NPC), nameof(NPC.AI))]
        public static class TargetDummyAIPatch
        {
            public static bool Prefix(NPC __instance)
            {
                if (__instance != null && __instance.active && __instance.type == NPCID.TargetDummy)
                {
                    int tX = (int)__instance.ai[0];
                    int tY = (int)__instance.ai[1];
                    bool hasTile = tX >= 0 && tY >= 0 && tX < Main.maxTilesX && tY < Main.maxTilesY &&
                                   Main.tile[tX, tY] != null && Main.tile[tX, tY].active() &&
                                   Main.tile[tX, tY].type == TileID.TargetDummy;

                    if (!hasTile)
                    {
                        // 自定义假人逻辑：静止不动、可被攻击、不消散、固定血量
                        // 注意：保留原版自然递减的无敌帧(immune)，严禁每帧强置为0，否则多段弹幕将在单帧内造成数十倍伤害并引发无限递归栈溢出崩溃！
                        __instance.velocity = Vector2.Zero;
                        __instance.direction = 1;
                        __instance.spriteDirection = 1;
                        __instance.life = 1000;
                        __instance.lifeMax = 1000;
                        __instance.dontTakeDamage = false;
                        __instance.immortal = true;
                        __instance.timeLeft = 7200;
                        return false; // 拦截原版无 Tile 消散，保留正常存在
                    }
                }
                return true;
            }
        }

        // 6. 发型师解锁所有发型
        [HarmonyPatch(typeof(HairstyleUnlocksHelper), "RebuildList")]
        public static class UnlockHairstylesPatch
        {
            public static void Postfix(HairstyleUnlocksHelper __instance)
            {
                if (Mod.Config != null && Mod.Config.StylistUnlockAllHair && __instance != null)
                {
                    __instance.AvailableHairstyles.Clear();
                    for (int i = 0; i < 165; i++)
                    {
                        __instance.AvailableHairstyles.Add(i);
                    }
                }
            }
        }

        // 7. 熔岩史莱姆不产生岩浆
        [HarmonyPatch(typeof(NPC), "HitEffect")]
        public static class LavalessLavaSlimePatch
        {
            private static bool _prevGoodWorld;

            public static void Prefix(NPC __instance)
            {
                if (Mod.Config != null && Mod.Config.LavalessLavaSlime && __instance != null)
                {
                    if (__instance.life <= 0 && (__instance.type == NPCID.LavaSlime || __instance.type == NPCID.Hellbat || __instance.type == NPCID.Lavabat))
                    {
                        _prevGoodWorld = Main.getGoodWorld;
                        Main.getGoodWorld = false;
                    }
                }
            }

            public static void Postfix(NPC __instance)
            {
                if (Mod.Config != null && Mod.Config.LavalessLavaSlime && __instance != null)
                {
                    if (__instance.life <= 0 && (__instance.type == NPCID.LavaSlime || __instance.type == NPCID.Hellbat || __instance.type == NPCID.Lavabat))
                    {
                        Main.getGoodWorld = _prevGoodWorld;
                    }
                }
            }
        }

        // 8. 补全测试假人模型渲染 (原版 DrawNPCDirect 会跳过 NPCID.TargetDummy，在此补全木桩人偶绘制)
        [HarmonyPatch(typeof(Main), nameof(Main.DrawNPCDirect))]
        public static class TargetDummyDrawPatch
        {
            public static void Postfix(Microsoft.Xna.Framework.Graphics.SpriteBatch mySpriteBatch, NPC rCurrentNPC, bool behindTiles, Vector2 screenPos)
            {
                if (behindTiles || mySpriteBatch == null || rCurrentNPC == null || !rCurrentNPC.active || rCurrentNPC.type != NPCID.TargetDummy)
                    return;

                int tX = (int)rCurrentNPC.ai[0];
                int tY = (int)rCurrentNPC.ai[1];
                bool hasTile = tX >= 0 && tY >= 0 && tX < Main.maxTilesX && tY < Main.maxTilesY &&
                               Main.tile[tX, tY] != null && Main.tile[tX, tY].active() &&
                               Main.tile[tX, tY].type == TileID.TargetDummy;

                // 若原版已有地面瓦片家具，由瓦片系统自行绘制；若无瓦片绑定，则由本补丁补全人偶模型
                if (hasTile)
                    return;

                try
                {
                    Main.instance.LoadItem(ItemID.TargetDummy);
                    Microsoft.Xna.Framework.Graphics.Texture2D texture = TextureAssets.Item[ItemID.TargetDummy]?.Value;
                    if (texture == null)
                        return;

                    Vector2 drawPos = rCurrentNPC.Bottom - screenPos;
                    Vector2 origin = new Vector2(texture.Width / 2f, texture.Height);

                    Color lightColor = Lighting.GetColor((int)(rCurrentNPC.Center.X / 16f), (int)(rCurrentNPC.Center.Y / 16f));
                    if (rCurrentNPC.justHit)
                    {
                        lightColor = Color.White;
                    }

                    var effects = rCurrentNPC.spriteDirection == -1 ? Microsoft.Xna.Framework.Graphics.SpriteEffects.FlipHorizontally : Microsoft.Xna.Framework.Graphics.SpriteEffects.None;

                    mySpriteBatch.Draw(
                        texture,
                        drawPos,
                        null,
                        lightColor,
                        rCurrentNPC.rotation,
                        origin,
                        rCurrentNPC.scale,
                        effects,
                        0f
                    );
                }
                catch
                {
                    // 忽略绘图异常，保障游戏不闪退
                }
            }
        }

        // 9. 快速放置激活时，彻底拦截原版手持物块/工具的单格自动放置与挥舞
        [HarmonyPatch(typeof(Player), nameof(Player.TryingToUseItem))]
        public static class QuickPlaceBlockVanillaUsePatch
        {
            public static bool Prefix(Player __instance, ref bool __result)
            {
                if (Mod.WorldActive && !Main.gameMenu && WandsAndTools.QuickPlaceActive && __instance != null && __instance.whoAmI == Main.myPlayer && !Main.playerInventory)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }
    }
}
