using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
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
            new ConfigPropertyMeta(), new ConfigPropertyMeta(), new ConfigPropertyMeta()
        };
        private static readonly FieldInfo BlockInput = AccessTools.Field(typeof(ModMenu), "_blockInput");
        private static int _offset;
        private static bool _enabled = true;
        internal static void ResetSession() { _offset = 0; }
        internal static void SetEnabled(bool enabled) { _enabled = enabled; }

        // Core 0.4.1 的配置页按 (kind,value,mod) 行绘制。只扩展本模组标题后的行，
        // 让 Core 原有可见行计算、页面滚动和其他模组绘制继续工作。
        internal static void AddConfigRow(List<ValueTuple<string, object, ModInfo>> rows, ValueTuple<string, object, ModInfo> row)
        {
            rows.Add(row);
            if (!_enabled || row.Item1 != "header" || row.Item3?.Manifest?.Id != ModId || row.Item3.Context?.Config != Mod.Config) return;
            foreach (var marker in Markers) rows.Add(ValueTuple.Create("property", (object)marker, row.Item3));
        }

        internal static bool DrawConfigRow(int x, int y, int width, ConfigPropertyMeta metadata, ModConfig config, string modId)
        {
            int marker = Array.IndexOf(Markers, metadata);
            if (marker < 0 || modId != ModId || !ReferenceEquals(config, Mod.Config)) return true;
            if (!_enabled) return false;
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
        private static bool Prefix(int __0, int __1, int __2, ConfigPropertyMeta __3, ModConfig __4, string __5)
            => SettingsSyncUI.DrawConfigRow(__0, __1, __2, __3, __4, __5);
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
