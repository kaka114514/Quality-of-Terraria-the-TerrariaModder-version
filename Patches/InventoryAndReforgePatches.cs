using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.UI;
using QualityOfTerraria.UI;

namespace QualityOfTerraria.Patches
{
    public static class InventoryAndReforgePatches
    {
        // 玩家背包、便携收纳和自己的标记箱子中信息配件生效。
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateEquips))]
        public static class BankInfoAccessoriesPatch
        {
            public static void Postfix(Player __instance)
            {
                if (Mod.Config == null || !Mod.Config.BankInfoAccessories || __instance == null || __instance.whoAmI != Main.myPlayer)
                    return;

                CheckBankArray(__instance, __instance.inventory);
                CheckBankArray(__instance, __instance.bank?.item);
                CheckBankArray(__instance, __instance.bank2?.item);
                CheckBankArray(__instance, __instance.bank3?.item);
                CheckBankArray(__instance, __instance.bank4?.item);
                foreach (Chest chest in ExtremeStorageUI.GetMarkedReadableChests()) CheckBankArray(__instance, chest.item, true);
            }

            private static void CheckBankArray(Player player, Item[] items, bool markedChest = false)
            {
                if (items == null) return;
                for (int i = 0; i < items.Length; i++)
                {
                    Item item = items[i];
                    if (item != null && !item.IsAir && item.type != ItemID.EncumberingStone)
                    {
                        if (markedChest && !ShouldApplyMarkedChestInfoItem(item.type)) continue;
                        player.RefreshInfoAccsFromItemType(item.type);
                        player.RefreshMechanicalAccsFromItemType(item.type);
                    }
                }
            }

            internal static bool ShouldApplyMarkedChestInfoItem(int itemType)
            {
                // Terraria 1.4.5.8 IDs: The Grand Design, Laser Ruler, Mechanical Lens.
                return itemType != 3611 && itemType != ItemID.LaserRuler && itemType != ItemID.MechanicalLens;
            }
        }

        // 3. 哥布林重铸历史记录（使用 UI 按钮回滚）
        // 监测重铸发生 (context 5 为重铸槽)
        [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.LeftClick), typeof(Item[]), typeof(int), typeof(int))]
        public static class ReforgeRecordPatch
        {
            public static void Postfix(Item[] inv, int context, int slot)
            {
                if (Mod.Config == null || !Mod.Config.ImprovePrefix)
                    return;

                if (context == 5 && !Main.reforgeItem.IsAir)
                {
                    // 将记录逻辑委托给 UI 组件
                    ReforgeRollbackUI.RecordPrefix(Main.reforgeItem);
                }
            }
        }

        // 4. 打开大背包或终极存储时，Shift + 左键点击背包物品直接整组快速存入
        [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.LeftClick), typeof(Item[]), typeof(int), typeof(int))]
        public static class ShiftQuickDepositPatch
        {
            public static bool Prefix(Item[] inv, int context, int slot)
            {
                if (inv == null || slot < 0 || slot >= inv.Length)
                    return true;

                // 仅针对普通背包物品槽 (context 0: 物品栏, 1: 钱币, 2: 弹药)
                if (context == 0 || context == 1 || context == 2)
                {
                    bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
                    if (shift)
                    {
                        Item item = inv[slot];
                        if (item != null && !item.IsAir && !item.favorited)
                        {
                            if (QualityOfTerraria.UI.SuperVaultUI.IsOpen)
                            {
                                if (QualityOfTerraria.UI.SuperVaultUI.DepositItem(item))
                                {
                                    SoundEngine.PlaySound(SoundID.Grab);
                                    Recipe.UpdateRecipeList();
                                    return false; // 拦截默认逻辑
                                }
                            }
                            else if (QualityOfTerraria.UI.ExtremeStorageUI.IsOpen)
                            {
                                // 终极存储需要经过冷却检查，避免扫过物品重复触发
                                if (!QualityOfTerraria.UI.ExtremeStorageUI.BeginQuickDeposit())
                                    return false; // 冷却中，拦截原版逻辑但不执行存储

                                int origStack = item.stack;
                                QualityOfTerraria.UI.ExtremeStorageUI.PutItemIntoAnyChest(item);
                                if (item.IsAir || item.stack < origStack)
                                {
                                    SoundEngine.PlaySound(SoundID.Grab);
                                    QualityOfTerraria.UI.ExtremeStorageUI.RefreshItems();
                                    Recipe.UpdateRecipeList();
                                    QualityOfTerraria.UI.ExtremeStorageUI.ArmQuickDepositCooldown();
                                    return false; // 拦截默认逻辑
                                }
                            }
                        }
                    }
                }
                return true;
            }
        }
    }
}
