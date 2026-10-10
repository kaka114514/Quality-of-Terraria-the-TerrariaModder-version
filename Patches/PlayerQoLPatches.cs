using System;
using System.Collections.Generic;
using HarmonyLib;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace QualityOfTerraria.Patches
{
    public static class PlayerQoLPatches
    {
        // 死亡时暂存的 Buff 列表 (BuffType, BuffTime)
        private static readonly Dictionary<int, List<KeyValuePair<int, int>>> _savedBuffs = new Dictionary<int, List<KeyValuePair<int, int>>>();

        // 1. 金币拾取自动存入存钱罐
        [HarmonyPatch(typeof(Player), nameof(Player.DoCoins))]
        public static class DoCoinsPatch
        {
            public static void Postfix(Player __instance, int i)
            {
                if (StorageInventoryGuard.Locked) return;
                if (Mod.Config == null || !Mod.Config.AutoSaveCoins || __instance == null || __instance.bank?.item == null)
                    return;

                if (__instance.whoAmI != Main.myPlayer)
                    return;

                // 检查背包里的钱币格子 50..53
                for (int slot = 50; slot <= 53; slot++)
                {
                    Item coin = __instance.inventory[slot];
                    if (coin != null && !coin.IsAir && coin.stack > 0)
                    {
                        // 尝试合并存入存钱罐
                        for (int b = 0; b < __instance.bank.item.Length; b++)
                        {
                            Item bankItem = __instance.bank.item[b];
                            if (bankItem != null && bankItem.type == coin.type && bankItem.stack < bankItem.maxStack)
                            {
                                int canAdd = bankItem.maxStack - bankItem.stack;
                                int toAdd = coin.stack < canAdd ? coin.stack : canAdd;
                                bankItem.stack += toAdd;
                                coin.stack -= toAdd;
                                if (coin.stack <= 0)
                                {
                                    coin.TurnToAir();
                                    break;
                                }
                            }
                        }

                        // 如果还有剩余，放入空槽
                        if (!coin.IsAir && coin.stack > 0)
                        {
                            for (int b = 0; b < __instance.bank.item.Length; b++)
                            {
                                Item bankItem = __instance.bank.item[b];
                                if (bankItem == null || bankItem.IsAir)
                                {
                                    __instance.bank.item[b] = coin.Clone();
                                    coin.TurnToAir();
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // 2. 磁铁般的物品抓取范围
        [HarmonyPatch(typeof(Player), nameof(Player.GetItemGrabRange), typeof(Item))]
        public static class GrabRangePatch
        {
            public static void Postfix(Player __instance, Item item, ref int __result)
            {
                if (Mod.Config != null && Mod.Config.ExtraGrabRange > 0)
                {
                    __result += Mod.Config.ExtraGrabRange * 16;
                }
            }
        }

        // 3. 玩家常规属性与魔镜瞬传
        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        public static class PlayerUpdatePatch
        {
            public static void Postfix(Player __instance, int i)
            {
                if (Mod.Config == null || __instance == null || !__instance.active)
                    return;

                if (__instance.whoAmI != Main.myPlayer && Main.netMode != 2)
                    return;

                if (__instance.whoAmI == Main.myPlayer)
                {
                    UI.UIManager.UpdateAll();

                    // 玩家打开过一次箱子，自动标记该箱子纳入终极存储
                    if (__instance.chest >= 0 && __instance.chest < Main.maxChests)
                    {
                        UI.ExtremeStorageUI.MarkChest(__instance.chest);
                    }
                }

                // 消除伤害浮动 (原生支持，彻底避免对高频伤害函数进行拦截)
                if (Main.netMode != 1) Terraria.Testing.DebugOptions.NoDamageVar = Mod.Config.NoDamageVariance;
            }
        }

        // UpdateEquips 在本帧工具使用前执行；原版随后将放置速度换算为时间倍率。
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateEquips))]
        public static class ToolStatsPatch
        {
            public static void Postfix(Player __instance)
            {
                if (!Mod.WorldActive || Mod.Config == null || __instance == null || !__instance.active || __instance.whoAmI != Main.myPlayer)
                    return;
                if (Mod.Config.ExtraTileReach > 0)
                {
                    Player.tileRangeX += Math.Min(30, Mod.Config.ExtraTileReach);
                    Player.tileRangeY += Math.Min(30, Mod.Config.ExtraTileReach);
                }
                if (Mod.Config.FastToolSpeed)
                {
                    __instance.pickSpeed *= 0.5f;
                    __instance.tileSpeed *= 2f;
                    __instance.wallSpeed *= 2f;
                }
            }
        }

        [HarmonyPatch(typeof(Player), "ApplyItemAnimation", typeof(Item))]
        public static class ToolAnimationPatch
        {
            public static void Postfix(Player __instance, Item sItem)
            {
                if (!Mod.WorldActive || Mod.Config == null || !Mod.Config.FastToolSpeed || __instance.whoAmI != Main.myPlayer || sItem == null)
                    return;
                if (sItem.pick > 0 || sItem.axe > 0 || sItem.hammer > 0)
                {
                    __instance.itemAnimation = Math.Max(1, __instance.itemAnimation / 2);
                    __instance.itemAnimationMax = Math.Max(1, __instance.itemAnimationMax / 2);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ApplyItemTime), typeof(Item))]
        public static class InstantRecallPatch
        {
            public static void Postfix(Player __instance, Item sItem)
            {
                if (!Mod.WorldActive || Mod.Config == null || __instance == null || __instance.whoAmI != Main.myPlayer || sItem == null)
                    return;
                // 镐由原版 pickSpeed 计算使用间隔；斧和锤走 ApplyItemTime。
                if (Mod.Config.FastToolSpeed && (sItem.axe > 0 || sItem.hammer > 0))
                {
                    __instance.itemTime = Math.Max(1, __instance.itemTime / 2);
                    __instance.itemTimeMax = __instance.itemTime;
                }
                if (!Mod.Config.InstantRecall || __instance.itemAnimation <= 0) return;
                int trigger;
                int type = sItem.type;
                if (type == ItemID.RecallPotion || type == ItemID.PotionOfReturn)
                    trigger = 20;
                else if (type == ItemID.MagicMirror || type == ItemID.IceMirror || type == ItemID.CellPhone ||
                         type == ItemID.Shellphone || type == ItemID.ShellphoneOcean || type == ItemID.ShellphoneHell ||
                         type == ItemID.ShellphoneSpawn || type == ItemID.MagicConch || type == ItemID.DemonConch)
                    trigger = sItem.useTime / 2;
                else
                    return;
                // 下一帧递减后命中原版触发点，保留目的地、回程门、消耗与联机发包。
                if (__instance.itemTime > trigger + 1)
                {
                    int skipped = __instance.itemTime - trigger - 1;
                    __instance.itemTime = trigger + 1;
                    // 同步缩短动画，避免时间提前耗尽后在同一动画中再次传送/扣药水。
                    __instance.itemAnimation = Math.Max(1, __instance.itemAnimation - skipped);
                }
            }
        }

        // 4. 死亡不生成墓碑
        [HarmonyPatch(typeof(Player), nameof(Player.DropTombstone))]
        public static class NoTombstonePatch
        {
            public static bool Prefix()
            {
                if (Mod.Config != null && Mod.Config.NoTombstones)
                {
                    return false; // 拦截墓碑生成
                }
                return true;
            }
        }

        // 5. 快速复活
        [HarmonyPatch(typeof(Player), "GetRespawnTime")]
        public static class FastRespawnPatch
        {
            public static void Postfix(ref int __result)
            {
                if (Mod.Config != null && (AnyBossAlive() ? Mod.Config.BossQuickRespawn : Mod.Config.QuickRespawn))
                    __result = 90;
            }

            private static bool AnyBossAlive()
            {
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i] != null && Main.npc[i].active && (Main.npc[i].boss ||
                        Main.npc[i].type == NPCID.EaterofWorldsHead || Main.npc[i].type == NPCID.EaterofWorldsBody || Main.npc[i].type == NPCID.EaterofWorldsTail))
                        return true;
                }
                return false;
            }
        }

        // 6. 死亡保存增益
        [HarmonyPatch(typeof(Player), nameof(Player.KillMe))]
        public static class SaveBuffsOnDeathPatch
        {
            public static void Prefix(Player __instance)
            {
                if (Mod.Config == null || !Mod.Config.PreserveBuffsOnDeath || __instance == null)
                    return;

                var buffs = new List<KeyValuePair<int, int>>();
                for (int i = 0; i < __instance.buffType.Length; i++)
                {
                    if (__instance.buffType[i] > 0 && !Main.debuff[__instance.buffType[i]])
                    {
                        buffs.Add(new KeyValuePair<int, int>(__instance.buffType[i], __instance.buffTime[i]));
                    }
                }
                _savedBuffs[__instance.whoAmI] = buffs;
            }
        }

        // 7. 重生满血与恢复增益
        [HarmonyPatch(typeof(Player), nameof(Player.Spawn))]
        public static class RespawnPatch
        {
            public static void Postfix(Player __instance)
            {
                if (Mod.Config == null || __instance == null)
                    return;

                // 满血满蓝
                if (Mod.Config.RespawnFullHealth)
                {
                    __instance.statLife = __instance.statLifeMax2;
                    __instance.statMana = __instance.statManaMax2;
                }

                // 恢复死前保存的增益
                if (Mod.Config.PreserveBuffsOnDeath && _savedBuffs.TryGetValue(__instance.whoAmI, out var buffs))
                {
                    foreach (var pair in buffs)
                    {
                        __instance.AddBuff(pair.Key, pair.Value, false);
                    }
                    _savedBuffs.Remove(__instance.whoAmI);
                }
            }
        }
    }
}
