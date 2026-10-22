using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.ID;

using Terraria.DataStructures;

namespace QualityOfTerraria.Patches
{
    public static class InfiniteItemsAndAmmoPatches
    {
        private static readonly HashSet<int> BossAndEventSummonItems = new HashSet<int>
        {
            ItemID.SuspiciousLookingEye,
            ItemID.WormFood,
            ItemID.BloodySpine,
            ItemID.Abeemination,
            ItemID.DeerThing,
            ItemID.SlimeCrown,
            ItemID.MechanicalEye,
            ItemID.MechanicalWorm,
            ItemID.MechanicalSkull,
            ItemID.LihzahrdPowerCell,
            ItemID.CelestialSigil,
            ItemID.ClothierVoodooDoll,
            ItemID.GuideVoodooDoll,
            ItemID.TruffleWorm,
            ItemID.SolarTablet,
            ItemID.SnowGlobe,
            ItemID.PumpkinMoonMedallion,
            ItemID.NaughtyPresent,
            ItemID.PirateMap,
            ItemID.EmpressButterfly
        };

        // 1. 无限弹药：堆叠 >= 3996 (或坠落之星 >= 999) 时射击不消耗弹药
        [HarmonyPatch(typeof(Player), nameof(Player.PickAmmo))]
        public static class PickAmmoPatch
        {
            public static bool ShouldPreventAmmoConsumption(Item ammo)
            {
                if (Mod.Config == null || !Mod.Config.InfiniteAmmo || ammo == null || ammo.IsAir)
                    return false;

                return ammo.stack >= 3996 || (ammo.type == ItemID.FallenStar && ammo.stack >= 999);
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);
                var consumableField = AccessTools.Field(typeof(Item), nameof(Item.consumable));
                int patched = 0;

                for (int i = 0; i < code.Count; i++)
                {
                    if (i + 3 < code.Count &&
                        code[i].opcode == OpCodes.Ldloc_3 &&
                        (code[i + 1].opcode == OpCodes.Brtrue || code[i + 1].opcode == OpCodes.Brtrue_S) &&
                        code[i + 2].opcode == OpCodes.Ldloc_0 &&
                        code[i + 3].opcode == OpCodes.Ldfld &&
                        Equals(code[i + 3].operand, consumableField))
                    {
                        code.InsertRange(i + 1, new[]
                        {
                            new CodeInstruction(OpCodes.Ldloc_0),
                            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PickAmmoPatch), nameof(ShouldPreventAmmoConsumption))),
                            new CodeInstruction(OpCodes.Or)
                        });
                        patched++;
                        i += 4;
                    }
                }

                if (patched != 1)
                    throw new InvalidOperationException("无法定位 PickAmmo 的最终弹药扣除分支");

                return code;
            }
        }

        // 2. 召唤物不消耗 & 投掷物堆叠无限 & 电线/促动器无限
        private static int _savedItemType;
        private static int _savedItemStack;
        private static int _savedItemSlot;
        private static int _savedWireStack;
        private static int _savedActuatorStack;

        [HarmonyPatch(typeof(Player), nameof(Player.ItemCheck))]
        public static class ItemCheckPatch
        {
            public static void Prefix(Player __instance)
            {
                if (Mod.Config == null || __instance == null || __instance.whoAmI != Main.myPlayer)
                    return;

                Item item = __instance.HeldItem;
                if (item != null && !item.IsAir)
                {
                    _savedItemType = item.type;
                    _savedItemStack = item.stack;
                    _savedItemSlot = __instance.selectedItem;
                }

                _savedWireStack = 0;
                _savedActuatorStack = 0;
                if (Mod.Config.InfiniteWire)
                {
                    for (int i = 0; i < 58; i++)
                    {
                        Item it = __instance.inventory[i];
                        if (it != null && !it.IsAir)
                        {
                            if (it.type == ItemID.Wire && it.stack >= 3996)
                                _savedWireStack = Math.Max(_savedWireStack, it.stack);
                            if (it.type == ItemID.Actuator && it.stack >= 3996)
                                _savedActuatorStack = Math.Max(_savedActuatorStack, it.stack);
                        }
                    }
                }
            }

            public static void Postfix(Player __instance)
            {
                if (Mod.Config == null || __instance == null || __instance.whoAmI != Main.myPlayer)
                    return;

                if (_savedItemSlot >= 0 && _savedItemSlot < __instance.inventory.Length)
                {
                    Item item = __instance.inventory[_savedItemSlot];

                    // 召唤物不消耗
                    if (Mod.Config.InfiniteSummonItems && BossAndEventSummonItems.Contains(_savedItemType))
                    {
                        if (item == null || item.IsAir || item.type != _savedItemType || item.stack < _savedItemStack)
                        {
                            if (item == null || item.IsAir)
                            {
                                __instance.inventory[_savedItemSlot] = new Item();
                                item = __instance.inventory[_savedItemSlot];
                                item.SetDefaults(_savedItemType);
                            }
                            item.stack = _savedItemStack;
                        }
                    }

                    // 投掷物 / 消耗武器堆叠 >= 3996 不消耗
                    if (Mod.Config.InfiniteThrowables && _savedItemStack >= 3996)
                    {
                        if (item != null && item.type == _savedItemType && item.shoot > ProjectileID.None && item.stack < _savedItemStack)
                        {
                            item.stack = _savedItemStack;
                        }
                    }
                }

                // 电线与促动器无限
                if (Mod.Config.InfiniteWire)
                {
                    if (_savedWireStack >= 3996)
                    {
                        for (int i = 0; i < 58; i++)
                        {
                            Item it = __instance.inventory[i];
                            if (it != null && it.type == ItemID.Wire && it.stack < _savedWireStack)
                            {
                                it.stack = _savedWireStack;
                                break;
                            }
                        }
                    }

                    if (_savedActuatorStack >= 3996)
                    {
                        for (int i = 0; i < 58; i++)
                        {
                            Item it = __instance.inventory[i];
                            if (it != null && it.type == ItemID.Actuator && it.stack < _savedActuatorStack)
                            {
                                it.stack = _savedActuatorStack;
                                break;
                            }
                        }
                    }
                }
            }
        }

        // 渔夫任务即时重置 (无需等隔天)
        [HarmonyPatch(typeof(Main), "UpdateTime")]
        public static class AnglerQuestResetPatch
        {
            public static void Postfix()
            {
                if (Mod.Config != null && Mod.Config.AnglerQuestNoCD)
                {
                    if (Main.anglerQuestFinished || (Main.anglerWhoFinishedToday != null && Main.anglerWhoFinishedToday.Count > 0))
                    {
                        Main.anglerQuestFinished = false;
                        if (Main.anglerWhoFinishedToday != null)
                            Main.anglerWhoFinishedToday.Clear();
                    }
                }
            }
        }
    }
}
