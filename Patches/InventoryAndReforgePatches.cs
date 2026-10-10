using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.UI;

namespace QualityOfTerraria.Patches
{
    public static class InventoryAndReforgePatches
    {
        // 1. 中键点击背包内便携收纳道具直接打开
        private static bool _oldMouseMiddle;

        [HarmonyPatch(typeof(Main), "DrawInventory")]
        public static class InventoryMiddleClickPatch
        {
            public static void Postfix()
            {
                if (Mod.Config == null || !Mod.Config.MiddleClickOpenBank || !Main.playerInventory)
                    return;

                bool mouseMiddle = Mouse.GetState().MiddleButton == ButtonState.Pressed;
                bool middleJustPressed = mouseMiddle && !_oldMouseMiddle;
                _oldMouseMiddle = mouseMiddle;

                if (!middleJustPressed)
                    return;

                Player player = Main.LocalPlayer;
                if (player == null || !player.active || player.dead)
                    return;

                // 检查鼠标悬停的物品
                Item hover = Main.HoverItem;
                if (hover == null || hover.IsAir)
                    return;

                int bankType = 0;
                if (hover.type == ItemID.PiggyBank || hover.type == ItemID.MoneyTrough || hover.type == ItemID.ChesterPetItem)
                {
                    bankType = -2; // 猪猪钱罐
                }
                else if (hover.type == ItemID.Safe)
                {
                    bankType = -3; // 保险箱
                }
                else if (hover.type == ItemID.DefendersForge)
                {
                    bankType = -4; // 护卫熔炉
                }
                else if (hover.type == ItemID.VoidVault || hover.type == ItemID.VoidLens)
                {
                    bankType = -5; // 虚空保险库
                }

                if (bankType != 0)
                {
                    if (player.chest == bankType)
                    {
                        player.chest = -1;
                        SoundEngine.PlaySound(SoundID.MenuClose);
                    }
                    else
                    {
                        player.chest = bankType;
                        SoundEngine.PlaySound(SoundID.MenuOpen);
                    }
                }
            }
        }

        // 2. 存钱罐/保险箱/虚空袋中信息配件生效
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateEquips))]
        public static class BankInfoAccessoriesPatch
        {
            public static void Postfix(Player __instance)
            {
                if (Mod.Config == null || !Mod.Config.BankInfoAccessories || __instance == null || __instance.whoAmI != Main.myPlayer)
                    return;

                CheckBankArray(__instance, __instance.bank?.item);
                CheckBankArray(__instance, __instance.bank2?.item);
                CheckBankArray(__instance, __instance.bank3?.item);
                CheckBankArray(__instance, __instance.bank4?.item);
            }

            private static void CheckBankArray(Player player, Item[] items)
            {
                if (items == null) return;
                for (int i = 0; i < items.Length; i++)
                {
                    Item item = items[i];
                    if (item != null && !item.IsAir && item.type != ItemID.EncumberingStone)
                    {
                        player.RefreshInfoAccsFromItemType(item.type);
                        player.RefreshMechanicalAccsFromItemType(item.type);
                    }
                }
            }
        }

        // 3. 哥布林重铸历史记录与右键回滚
        // 记录物品与其曾经获得的词缀列表
        private static readonly Dictionary<string, List<byte>> _prefixHistory = new Dictionary<string, List<byte>>();
        private static readonly Dictionary<string, int> _historyIndex = new Dictionary<string, int>();

        private static string GetItemKey(Item item)
        {
            return $"{item.type}_{item.favorited}";
        }

        // 监测重铸发生 (context 5 为重铸槽)
        [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.LeftClick), typeof(Item[]), typeof(int), typeof(int))]
        public static class ReforgeRecordPatch
        {
            private static byte _prevPrefix;

            public static void Prefix(Item[] inv, int context, int slot)
            {
                if (Mod.Config == null || !Mod.Config.ImprovePrefix)
                    return;

                if (context == 5 && !Main.reforgeItem.IsAir)
                {
                    _prevPrefix = Main.reforgeItem.prefix;
                }
            }

            public static void Postfix(Item[] inv, int context, int slot)
            {
                if (Mod.Config == null || !Mod.Config.ImprovePrefix)
                    return;

                if (context == 5 && !Main.reforgeItem.IsAir)
                {
                    string key = GetItemKey(Main.reforgeItem);
                    if (!_prefixHistory.ContainsKey(key))
                    {
                        _prefixHistory[key] = new List<byte>();
                    }

                    if (_prevPrefix > 0 && !_prefixHistory[key].Contains(_prevPrefix))
                    {
                        _prefixHistory[key].Add(_prevPrefix);
                    }

                    if (Main.reforgeItem.prefix > 0 && !_prefixHistory[key].Contains(Main.reforgeItem.prefix))
                    {
                        _prefixHistory[key].Add(Main.reforgeItem.prefix);
                    }
                }
            }
        }

        // 右键重铸槽回滚到上一个词缀
        [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.RightClick), typeof(Item[]), typeof(int), typeof(int))]
        public static class ReforgeRollbackPatch
        {
            public static bool Prefix(Item[] inv, int context, int slot)
            {
                if (Mod.Config == null || !Mod.Config.ImprovePrefix)
                    return true;

                if (context == 5 && !Main.reforgeItem.IsAir)
                {
                    string key = GetItemKey(Main.reforgeItem);
                    if (_prefixHistory.ContainsKey(key) && _prefixHistory[key].Count > 1)
                    {
                        List<byte> list = _prefixHistory[key];
                        if (!_historyIndex.ContainsKey(key))
                            _historyIndex[key] = list.IndexOf(Main.reforgeItem.prefix);

                        int idx = (_historyIndex[key] - 1 + list.Count) % list.Count;
                        _historyIndex[key] = idx;
                        byte targetPrefix = list[idx];

                        Main.reforgeItem.Prefix(targetPrefix);
                        SoundEngine.PlaySound(SoundID.Item37);
                        Main.NewText($"[可追溯重铸] 已回滚词缀至: {Lang.prefix[targetPrefix].Value}", 100, 240, 255);
                        return false; // 拦截默认右键
                    }
                }
                return true;
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
                                int origStack = item.stack;
                                QualityOfTerraria.UI.ExtremeStorageUI.PutItemIntoAnyChest(item);
                                if (item.IsAir || item.stack < origStack)
                                {
                                    SoundEngine.PlaySound(SoundID.Grab);
                                    QualityOfTerraria.UI.ExtremeStorageUI.RefreshItems();
                                    Recipe.UpdateRecipeList();
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
