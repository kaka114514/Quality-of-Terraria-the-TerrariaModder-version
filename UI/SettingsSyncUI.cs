using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;
using QualityOfTerraria.Networking;
using Terraria;
using TerrariaModder.Core;
using TerrariaModder.Core.Config;
using TerrariaModder.Core.Permissions;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.UI
{
    internal static class SettingsSyncUI
    {
        private const string ModId = "quality-of-terraria";
        private static readonly ConfigPropertyMeta[] Markers = {
            new ConfigPropertyMeta(), new ConfigPropertyMeta(), new ConfigPropertyMeta(),
            new ConfigPropertyMeta(), new ConfigPropertyMeta(), new ConfigPropertyMeta(),
            new ConfigPropertyMeta(), new ConfigPropertyMeta()
        };
        internal static readonly ConfigPropertyMeta[] CategoryMarkers = {
            new ConfigPropertyMeta(), new ConfigPropertyMeta(), new ConfigPropertyMeta(),
            new ConfigPropertyMeta(), new ConfigPropertyMeta()
        };
        private static readonly string[] CategoryNames = {
            "物品与增益", "挖掘、建造与工具", "玩家便利与生存", "世界与环境", "NPC与图鉴"
        };
        private static readonly IEnumerable<string>[] CategoryKeys = {
            new[] {
                nameof(QualityOfTerrariaConfig.InfinitePotions), nameof(QualityOfTerrariaConfig.PotionStackRequirement),
                nameof(QualityOfTerrariaConfig.InfiniteWellFed), nameof(QualityOfTerrariaConfig.WellFedStackRequirement),
                nameof(QualityOfTerrariaConfig.RedPotionEverywhere), nameof(QualityOfTerrariaConfig.RedPotionRequirement),
                nameof(QualityOfTerrariaConfig.InfiniteStationsAndBanners), nameof(QualityOfTerrariaConfig.PortableCraftingStations),
                nameof(QualityOfTerrariaConfig.BankInfoAccessories), nameof(QualityOfTerrariaConfig.AutoSaveCoins),
                nameof(QualityOfTerrariaConfig.AnglerQuestNoCD), nameof(QualityOfTerrariaConfig.ImprovePrefix),
                nameof(QualityOfTerrariaConfig.InfiniteAmmo), nameof(QualityOfTerrariaConfig.InfiniteSummonItems),
                nameof(QualityOfTerrariaConfig.InfiniteThrowables), nameof(QualityOfTerrariaConfig.InfiniteWire),
                nameof(QualityOfTerrariaConfig.WandMaterialNoConsume)
            },
            new[] {
                nameof(QualityOfTerrariaConfig.ToolSpeedMultiplier), nameof(QualityOfTerrariaConfig.PlacementSpeedMultiplier),
                nameof(QualityOfTerrariaConfig.VeinMiningEnabled), nameof(QualityOfTerrariaConfig.VeinMiningAutoLoot),
                nameof(QualityOfTerrariaConfig.VeinMiningRange), nameof(QualityOfTerrariaConfig.VeinMiningMaxBlocks),
                nameof(QualityOfTerrariaConfig.VeinMiningCheckPickaxePower), nameof(QualityOfTerrariaConfig.VeinMiningMatchExactType),
                nameof(QualityOfTerrariaConfig.VeinMiningShowMessages), nameof(QualityOfTerrariaConfig.ExtraTileReach),
                nameof(QualityOfTerrariaConfig.FastExtractinator)
            },
            new[] {
                nameof(QualityOfTerrariaConfig.ExtraGrabRange), nameof(QualityOfTerrariaConfig.InstantRecall),
                nameof(QualityOfTerrariaConfig.AutoFishingIntervalSeconds),
                nameof(QualityOfTerrariaConfig.NoTombstones), nameof(QualityOfTerrariaConfig.PreserveBuffsOnDeath),
                nameof(QualityOfTerrariaConfig.RespawnFullHealth), nameof(QualityOfTerrariaConfig.QuickRespawn),
                nameof(QualityOfTerrariaConfig.BossQuickRespawn), nameof(QualityOfTerrariaConfig.NoDamageVariance),
                nameof(QualityOfTerrariaConfig.BombsNoPlayerDamage), nameof(QualityOfTerrariaConfig.DisableNonPlayerBombTileDamage),
                nameof(QualityOfTerrariaConfig.KeepFocus), nameof(QualityOfTerrariaConfig.TeamUnconditionalTeleport),
                nameof(QualityOfTerrariaConfig.BedSpawnEverywhere), nameof(QualityOfTerrariaConfig.NoSleepRestrictions),
                nameof(QualityOfTerrariaConfig.LavalessLavaSlime)
            },
            new[] {
                nameof(QualityOfTerrariaConfig.HostileSpawnRateMultiplier), nameof(QualityOfTerrariaConfig.CritterSpawnRateMultiplier),
                nameof(QualityOfTerrariaConfig.NoEvilSpread), nameof(QualityOfTerrariaConfig.UniversalPylons),
                nameof(QualityOfTerrariaConfig.PylonPlacementUnrestricted), nameof(QualityOfTerrariaConfig.PylonTeleportWithoutNearbyPlayer),
                nameof(QualityOfTerrariaConfig.PylonTeleportIgnoreBiome), nameof(QualityOfTerrariaConfig.CoinDropMultiplier),
                nameof(QualityOfTerrariaConfig.RefreshTravelShop), nameof(QualityOfTerrariaConfig.BedTimeRate),
                nameof(QualityOfTerrariaConfig.HerbInstantBloom), nameof(QualityOfTerrariaConfig.HerbMaturationSpeed),
                nameof(QualityOfTerrariaConfig.HerbIgnoreEnvironment), nameof(QualityOfTerrariaConfig.StaffOfRegrowthReplant),
                nameof(QualityOfTerrariaConfig.FastTreeGrowth), nameof(QualityOfTerrariaConfig.ShakeTreeAlwaysFruit),
                nameof(QualityOfTerrariaConfig.GemTreeAlwaysGem), nameof(QualityOfTerrariaConfig.FastPumpkinGrowth),
                nameof(QualityOfTerrariaConfig.FastLifeFruitGrowth)
            },
            new[] {
                nameof(QualityOfTerrariaConfig.TownNPCImmortal), nameof(QualityOfTerrariaConfig.TownNPCFastSpawn),
                nameof(QualityOfTerrariaConfig.TownNPCConvenientMoveIn), nameof(QualityOfTerrariaConfig.TownNPCLiveInEvil),
                nameof(QualityOfTerrariaConfig.TravellingMerchantStays), nameof(QualityOfTerrariaConfig.MaxNPCHappiness),
                nameof(QualityOfTerrariaConfig.OneKillBestiary), nameof(QualityOfTerrariaConfig.StylistUnlockAllHair)
            }
        };
        private static readonly HashSet<int> ExpandedCategories = new HashSet<int>();
        private static readonly FieldInfo BlockInput = AccessTools.Field(typeof(ModMenu), "_blockInput");
        private static readonly FieldInfo ConfigInnerTab = AccessTools.Field(typeof(ModMenu), "_configInnerTab");
        private static readonly FieldInfo InputTextEnter = AccessTools.Field(typeof(Main), "inputTextEnter");
        private static int _offset;
        private static int _profileOffset;
        private static bool _profileRenaming;
        private static string _profileRenameText;
        private const string ProfileInputId = "QualityOfTerraria.ProfileRename";
        private static bool _enabled = true;
        internal static bool IsProfileRenaming => _profileRenaming;
        internal static void ResetSession()
        {
            _offset = 0; _profileOffset = 0; ExpandedCategories.Clear(); ResetProfileInput();
        }
        internal static void SetEnabled(bool enabled) { _enabled = enabled; }
        internal static string[] GetCategoryNames() => (string[])CategoryNames.Clone();
        internal static bool IsCategoryExpanded(int index) => ExpandedCategories.Contains(index);
        internal static void ToggleCategory(int index)
        {
            if (index < 0 || index >= CategoryNames.Length) return;
            if (!ExpandedCategories.Add(index)) ExpandedCategories.Remove(index);
        }

        internal static bool ShouldMoveServerPropertyToClientTab(ConfigPropertyMeta metadata)
        {
            return metadata != null && metadata.Scope == ConfigScope.Server &&
                metadata.Property?.DeclaringType == typeof(QualityOfTerrariaConfig);
        }

        internal static bool ShouldHideServerPropertyInServerTab(ConfigPropertyMeta metadata, int selectedScope)
        {
            return selectedScope == (int)ConfigScope.Server && ShouldMoveServerPropertyToClientTab(metadata);
        }

        private static bool IsClientServerSettingLocked(bool isClient, ConfigPropertyMeta metadata, ModConfig config, string modId)
        {
            if (!isClient || modId != ModId || !ReferenceEquals(config, Mod.Config) || !ShouldMoveServerPropertyToClientTab(metadata))
                return isClient;
            return Main.netMode != 0 && !Netplay.IsHostAndPlay;
        }

        // Core 0.4.1 的配置页按 (kind,value,mod) 行绘制。只扩展本模组标题后的行，
        // 让 Core 原有可见行计算、页面滚动和其他模组绘制继续工作。
        internal static void AddConfigRow(List<ValueTuple<string, object, ModInfo>> rows, ValueTuple<string, object, ModInfo> row)
        {
            bool ownConfig = row.Item3?.Manifest?.Id == ModId && row.Item3.Context?.Config == Mod.Config;
            int selectedScope = (int)(ConfigInnerTab?.GetValue(null) ?? 0);
            if (ownConfig && selectedScope != (int)ConfigScope.Client)
            {
                if (row.Item1 == "header")
                {
                    rows.Add(row);
                    AddProfileRows(rows, row.Item3);
                }
                return;
            }
            if (ownConfig && row.Item1 == "property") return;
            if (ownConfig && row.Item1 == "header")
            {
                rows.Add(row);
                AddCustomRows(rows, row.Item3);
                return;
            }
            rows.Add(row);
        }

        private static void AddCustomRows(List<ValueTuple<string, object, ModInfo>> rows, ModInfo source)
        {
            AddProfileRows(rows, source);

            var metadata = Mod.Config.GetPropertyMetadata().ToDictionary(meta => meta.Key, StringComparer.Ordinal);
            for (int category = 0; category < CategoryNames.Length; category++)
            {
                rows.Add(ValueTuple.Create("property", (object)CategoryMarkers[category], source));
                if (!ExpandedCategories.Contains(category)) continue;
                foreach (string key in CategoryKeys[category])
                {
                    ConfigPropertyMeta property;
                    if (!metadata.TryGetValue(key, out property)) continue;
                    var propertyRow = ValueTuple.Create("property", (object)property, source);
                    rows.Add(propertyRow);
                    if (key == nameof(QualityOfTerrariaConfig.RedPotionRequirement) && _enabled)
                        PotionBlacklistUI.AddRows(rows, propertyRow);
                }
            }
        }

        private static void AddProfileRows(List<ValueTuple<string, object, ModInfo>> rows, ModInfo source)
        {
            if (!_enabled) return;
            int count = 2 + Math.Min(4, SettingsSync.Requests.Count);
            for (int i = 0; i < count; i++) rows.Add(ValueTuple.Create("property", (object)Markers[i], source));
            rows.Add(ValueTuple.Create("property", (object)Markers[6], source));
            rows.Add(ValueTuple.Create("property", (object)Markers[7], source));
        }

        internal static bool DrawConfigRow(int x, int y, int width, ConfigPropertyMeta metadata, ModConfig config, string modId)
        {
            if (modId != ModId || !ReferenceEquals(config, Mod.Config)) return true;
            if (PotionBlacklistUI.IsMarker(metadata)) return PotionBlacklistUI.Draw(x, y, width, metadata, config);
            int category = Array.IndexOf(CategoryMarkers, metadata);
            if (category >= 0) return DrawCategory(x, y, width, category);
            int marker = Array.IndexOf(Markers, metadata);
            // 同步按钮和状态行没有 Property，必须先识别，不能送入真实配置控件读取 Key。
            if (marker < 0) return SpeedSettingsUI.Draw(x, y, width, metadata, config, modId);
            if (!_enabled) return false;
            if (marker == 6) return DrawProfiles(x, y, width);
            if (marker == 7) return DrawProfileActions(x, y, width);
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + 28;
            bool blocked = (bool)(BlockInput?.GetValue(null) ?? false);
            if (hover && Mod.WorldActive) Main.player[Main.myPlayer].mouseInterface = true;
            if (marker == 0)
            {
                if (Button("将设置同步给房间内其他玩家", x, y, width, hover && !blocked)) SettingsSync.RequestSync();
            }
            else if (marker == 1) PanelLayout.DrawFittedText(SettingsSync.Status, x + 4, y + 6, width - 8, 255, 220, 120);
            else
            {
                int maximum = Math.Max(0, SettingsSync.Requests.Count - 4);
                if (hover && !blocked && UIRenderer.ScrollWheel != 0)
                {
                    _offset = Math.Max(0, Math.Min(maximum, _offset + (UIRenderer.ScrollWheel < 0 ? 1 : -1)));
                    UIRenderer.ConsumeScroll();
                }
                _offset = Math.Max(0, Math.Min(maximum, _offset));
                int index = _offset + marker - 2;
                if (index < SettingsSync.Requests.Count)
                {
                    var row = SettingsSync.Requests[index];
                    int actionWidth = row.Incoming && row.Pending ? 104 : 0;
                    string text = row.Name + "：" + row.State;
                    PanelLayout.DrawFittedText(text, x + 4, y + 6, Math.Max(1, width - actionWidth - 8), 220, 230, 245);
                    if (actionWidth > 0)
                    {
                        int approveX = x + width - 100, declineX = x + width - 48;
                        if (Button("同意", approveX, y, 48, !blocked && hover && UIRenderer.MouseX >= approveX && UIRenderer.MouseX < approveX + 48)) SettingsSync.Decide(row, true);
                        if (Button("拒绝", declineX, y, 48, !blocked && hover && UIRenderer.MouseX >= declineX)) SettingsSync.Decide(row, false);
                    }
                }
                else if (marker == 2) PanelLayout.DrawFittedText("暂无申请；列表内滚轮可查看其他玩家", x + 4, y + 6, width - 8, 160, 170, 190);
            }
            return false;
        }

        private static bool DrawProfiles(int x, int y, int width)
        {
            bool rowHover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + 28;
            if (rowHover && Mod.WorldActive) Main.player[Main.myPlayer].mouseInterface = true;
            var names = ConfigProfiles.Names;
            int plusWidth = 30, slotWidth = 86;
            int visible = Math.Max(1, (width - plusWidth - 4) / slotWidth);
            int maximum = Math.Max(0, names.Count - visible);
            if (rowHover && UIRenderer.ScrollWheel != 0)
            {
                _profileOffset = Math.Max(0, Math.Min(maximum, _profileOffset + (UIRenderer.ScrollWheel < 0 ? 1 : -1)));
                UIRenderer.ConsumeScroll();
            }
            _profileOffset = Math.Max(0, Math.Min(maximum, _profileOffset));
            int start = _profileOffset;
            int shown = Math.Min(visible, names.Count - start);
            for (int i = 0; i < shown; i++)
            {
                string name = names[start + i];
                int buttonX = x + i * slotWidth;
                bool hover = UIRenderer.MouseX >= buttonX && UIRenderer.MouseX < buttonX + slotWidth && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + 28;
                bool custom = name != "默认" && name != "上一次";
                bool closeHover = custom && UIRenderer.MouseX >= buttonX + slotWidth - 18 && UIRenderer.MouseX < buttonX + slotWidth && hover;
                byte red = name == ConfigProfiles.SelectedName ? (byte)70 : (byte)30;
                UIRenderer.DrawRect(buttonX, y + 2, slotWidth - 4, 24, (byte)(hover ? red + 20 : red), 65, 100, 255);
                PanelLayout.DrawFittedText(name, buttonX + 5, y + 6, slotWidth - (custom ? 24 : 10), 235, 240, 255);
                if (custom) PanelLayout.DrawFittedText("×", buttonX + slotWidth - 19, y + 4, 16, 255, 90, 90);
                if (hover && UIRenderer.MouseLeftClick)
                {
                    ConfigProfiles.Select(name);
                    if (closeHover)
                    {
                        string error;
                        if (!ConfigProfiles.DeleteSelected(out error)) Main.NewText(error, 255, 150, 120);
                        else Main.NewText("配置已删除", 150, 230, 255);
                    }
                    UIRenderer.ConsumeClick();
                }
            }
            int plusX = x + width - plusWidth;
            if (Button("+", plusX, y, plusWidth, UIRenderer.MouseX >= plusX && UIRenderer.MouseX < plusX + plusWidth && rowHover))
            {
                string name, error;
                if (!ConfigProfiles.SaveCurrentAsNew(out name, out error)) Main.NewText(error, 255, 150, 120);
                else Main.NewText("已保存配置：" + name, 150, 230, 255);
            }
            return false;
        }

        private static bool DrawProfileActions(int x, int y, int width)
        {
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + 28;
            if (hover && Mod.WorldActive) Main.player[Main.myPlayer].mouseInterface = true;
            string selected = ConfigProfiles.SelectedName ?? "默认";
            PanelLayout.DrawFittedText("当前配置：" + selected, x + 5, y + 6, 150, 220, 230, 245);
            bool renaming = _profileRenaming;
            if (renaming)
            {
                int inputX = x + 155;
                bool inputHover = UIRenderer.MouseX >= inputX && UIRenderer.MouseX < applyActionX(width) && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + 28;
                UIRenderer.EnableTextInput();
                UIRenderer.HandleIME();
                _profileRenameText = UIRenderer.GetInputText(_profileRenameText ?? "");
                if (_profileRenameText.Length > 24) _profileRenameText = _profileRenameText.Substring(0, 24);
                PanelLayout.DrawFittedText("名称：" + (_profileRenameText.Length == 0 ? "请输入名称" : _profileRenameText) + "|", inputX, y + 6, Math.Max(80, width - 310), 235, 240, 255);
                bool enter = Main.keyState.IsKeyDown(Keys.Enter) || (bool)(InputTextEnter?.GetValue(null) ?? false);
                if (UIRenderer.CheckInputEscape()) ResetProfileInput();
                else if (enter || (UIRenderer.MouseLeftClick && !inputHover))
                {
                    CommitProfileRename();
                }
                return false;
            }
            int applyX = x + width - 170, renameX = x + width - 82;
            if (Button("应用", applyX, y, 76, UIRenderer.MouseX >= applyX && UIRenderer.MouseX < applyX + 76 && hover))
            {
                string message;
                if (!ConfigProfiles.ApplySelected(out message)) Main.NewText(message, 255, 150, 120);
                else Main.NewText(message, 150, 230, 255);
            }
            bool canRename = selected != "默认" && selected != "上一次";
            if (canRename && Button("重命名", renameX, y, 76, UIRenderer.MouseX >= renameX && UIRenderer.MouseX < renameX + 76 && hover))
            {
                _profileRenaming = true; _profileRenameText = string.Empty;
                UIRenderer.ClearInput(); UIRenderer.EnableTextInput(); UIRenderer.RegisterKeyInputBlock(ProfileInputId);
            }
            if (!canRename) PanelLayout.DrawFittedText("不可重命名", renameX, y + 6, 76, 155, 165, 180);
            return false;
        }

        private static void ResetProfileInput()
        {
            _profileRenaming = false; _profileRenameText = null;
            UIRenderer.DisableTextInput(); UIRenderer.UnregisterKeyInputBlock(ProfileInputId);
        }

        private static int applyActionX(int width) => width - 170;

        private static void CommitProfileRename()
        {
            string error;
            if (_profileRenameText.Length == 0) Main.NewText("配置名称不能为空", 255, 150, 120);
            else if (!ConfigProfiles.RenameSelected(_profileRenameText, out error)) Main.NewText(error, 255, 150, 120);
            else Main.NewText("配置已重命名", 150, 230, 255);
            ResetProfileInput();
        }

        internal static bool ShouldResetSpeedInput(bool activeRowDrawn)
            => !activeRowDrawn && !_profileRenaming;

        private static bool DrawCategory(int x, int y, int width, int category)
        {
            bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + 28;
            bool blocked = (bool)(BlockInput?.GetValue(null) ?? false);
            if (hover && Mod.WorldActive) Main.player[Main.myPlayer].mouseInterface = true;
            string prefix = ExpandedCategories.Contains(category) ? "▼ " : "▶ ";
            UIRenderer.DrawRect(x, y + 2, width, 24, (byte)(hover ? 55 : 30), 65, 100, 255);
            PanelLayout.DrawFittedText(prefix + CategoryNames[category], x + 7, y + 6, width - 14, 255, 255, 255);
            if (hover && !blocked && UIRenderer.MouseLeftClick)
            {
                ToggleCategory(category);
                UIRenderer.ConsumeClick();
            }
            return false;
        }

        private static bool Button(string text, int x, int y, int width, bool hover)
        {
            UIRenderer.DrawRect(x, y + 2, width, 24, (byte)(hover ? 55 : 30), 65, 100, 255);
            PanelLayout.DrawFittedText(text, x + 6, y + 6, width - 12, 255, 255, 255);
            if (!hover || !UIRenderer.MouseLeftClick) return false;
            UIRenderer.ConsumeClick();
            return true;
        }
    }

    [HarmonyPatch(typeof(ModMenu), "DrawConfigTab")]
    internal static class SettingsSyncRowsPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var add = AccessTools.Method(typeof(List<ValueTuple<string, object, ModInfo>>), "Add");
            var replacement = AccessTools.Method(typeof(SettingsSyncUI), "AddConfigRow");
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Callvirt && Equals(instruction.operand, add))
                { instruction.opcode = OpCodes.Call; instruction.operand = replacement; }
                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(ModMenu), "DrawConfigProperty")]
    internal static class SettingsSyncRowDrawPatch
    {
        private static readonly MethodInfo LabelGetter = AccessTools.PropertyGetter(typeof(ConfigPropertyMeta), "Label");
        private static readonly MethodInfo DescriptionGetter = AccessTools.PropertyGetter(typeof(ConfigPropertyMeta), "Description");

        private static bool Prefix(int __0, int __1, int __2, ConfigPropertyMeta __3, ModConfig __4, string __5)
            => SettingsSyncUI.DrawConfigRow(__0, __1, __2, __3, __4, __5);

        // Core 的自动属性 getter 会被 JIT 内联，绕过后来安装的本地化补丁。
        // 反射调用保留所有模组的 getter 补丁，不自行解释标签或选择语言。
        private static string ReadLabel(ConfigPropertyMeta metadata) => (string)LabelGetter.Invoke(metadata, null);
        private static string ReadDescription(ConfigPropertyMeta metadata) => (string)DescriptionGetter.Invoke(metadata, null);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var readLabel = AccessTools.Method(typeof(SettingsSyncRowDrawPatch), nameof(ReadLabel));
            var readDescription = AccessTools.Method(typeof(SettingsSyncRowDrawPatch), nameof(ReadDescription));
            var isClientGetter = AccessTools.PropertyGetter(typeof(TerrariaModder.Core.Reflection.Game), "IsClient");
            var clientLock = AccessTools.Method(typeof(SettingsSyncUI), "IsClientServerSettingLocked", new[] {
                typeof(bool), typeof(ConfigPropertyMeta), typeof(ModConfig), typeof(string)
            });
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(LabelGetter))
                { instruction.opcode = OpCodes.Call; instruction.operand = readLabel; }
                else if (instruction.Calls(DescriptionGetter))
                { instruction.opcode = OpCodes.Call; instruction.operand = readDescription; }
                yield return instruction;
                if (instruction.Calls(isClientGetter))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Ldarg_S, (byte)4);
                    yield return new CodeInstruction(OpCodes.Ldarg_S, (byte)5);
                    yield return new CodeInstruction(OpCodes.Call, clientLock);
                }
            }
        }
    }

    [HarmonyPatch(typeof(PermissionService), "OnClientConnect")]
    internal static class SettingsSyncConnectPatch
    {
        private static void Postfix(int __0) { SettingsSync.QueueConnectionChanged(__0); }
    }

    [HarmonyPatch(typeof(PermissionService), "OnClientDisconnect")]
    internal static class SettingsSyncDisconnectPatch
    {
        private static void Postfix(int __0) { SettingsSync.QueueConnectionChanged(__0); }
    }
}
