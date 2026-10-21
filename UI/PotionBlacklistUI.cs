using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core;
using TerrariaModder.Core.Config;
using TerrariaModder.Core.UI;
using TerrariaModder.Core.UI.Widgets;

namespace QualityOfTerraria.UI
{
    internal static class PotionBlacklistUI
    {
        private const int IconsPerRow = 10;
        private static readonly FieldInfo BlockInput = AccessTools.Field(typeof(ModMenu), "_blockInput");
        private static readonly MethodInfo SaveConfig = AccessTools.Method(typeof(ModMenu), "AutoSaveConfig");
        internal static readonly ConfigPropertyMeta ToggleMarker = new ConfigPropertyMeta();
        private static readonly ConfigPropertyMeta[] RowMarkers = Enumerable.Range(0, 8).Select(_ => new ConfigPropertyMeta()).ToArray();
        private static readonly List<Item> Candidates = new List<Item>();
        private static bool _loaded;
        internal static bool Expanded;

        internal static IReadOnlyList<ConfigPropertyMeta> Markers
        {
            get
            {
                EnsureCandidates();
                int rows = Math.Max(1, (Candidates.Count + IconsPerRow - 1) / IconsPerRow);
                return new[] { ToggleMarker }.Concat(RowMarkers.Take(rows)).ToArray();
            }
        }

        internal static bool IsMarker(ConfigPropertyMeta metadata)
        {
            if (ReferenceEquals(metadata, ToggleMarker)) return true;
            return RowMarkers.Any(marker => ReferenceEquals(marker, metadata));
        }

        internal static int MarkerIndex(ConfigPropertyMeta metadata)
        {
            if (ReferenceEquals(metadata, ToggleMarker)) return 0;
            for (int i = 0; i < RowMarkers.Length; i++)
                if (ReferenceEquals(metadata, RowMarkers[i])) return i + 1;
            return -1;
        }

        private static void EnsureCandidates()
        {
            if (_loaded) return;
            _loaded = true;
            FieldInfo[] fields = typeof(ItemID).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => (field.FieldType == typeof(int) || field.FieldType == typeof(short)) && (field.Name.EndsWith("Potion", StringComparison.Ordinal) || field.Name == "RedPotion"))
                .OrderBy(field => Convert.ToInt32(field.GetValue(null))).ToArray();
            foreach (FieldInfo field in fields)
            {
                int type = Convert.ToInt32(field.GetValue(null));
                Item item = new Item();
                item.SetDefaults(type);
                if (type != ItemID.RedPotion && (item.buffType <= 0 || item.buffType >= Main.debuff.Length || Main.debuff[item.buffType] || !item.consumable)) continue;
                if (Candidates.Any(existing => existing.type == type)) continue;
                item.stack = 1;
                Candidates.Add(item);
            }
        }

        internal static void AddRows(List<ValueTuple<string, object, ModInfo>> rows, ValueTuple<string, object, ModInfo> source)
        {
            EnsureCandidates();
            rows.Add(ValueTuple.Create("property", (object)ToggleMarker, source.Item3));
            if (!Expanded) return;
            int count = Math.Max(1, (Candidates.Count + IconsPerRow - 1) / IconsPerRow);
            for (int i = 0; i < count; i++) rows.Add(ValueTuple.Create("property", (object)RowMarkers[i], source.Item3));
        }

        internal static bool Draw(int x, int y, int width, ConfigPropertyMeta metadata, ModConfig config)
        {
            EnsureCandidates();
            int index = MarkerIndex(metadata);
            if (index < 0) return true;
            if (index == 0)
            {
                bool hover = UIRenderer.MouseX >= x && UIRenderer.MouseX < x + width && UIRenderer.MouseY >= y && UIRenderer.MouseY < y + 28;
                bool blocked = (bool)(BlockInput?.GetValue(null) ?? false);
                if (hover) Main.LocalPlayer.mouseInterface = true;
                string state = Expanded ? "▲ 收起" : "▼ 展开";
                PanelLayout.DrawFittedText("药水增益黑名单（点击" + state + "）", x + 5, y + 5, width, 235, 240, 255);
                if (hover && !blocked && UIRenderer.MouseLeftClick)
                {
                    Expanded = !Expanded;
                    UIRenderer.ConsumeClick();
                }
                return false;
            }

            int start = (index - 1) * IconsPerRow;
            int end = Math.Min(Candidates.Count, start + IconsPerRow);
            bool inputBlocked = (bool)(BlockInput?.GetValue(null) ?? false);
            int iconSize = Math.Min(26, Math.Max(20, width / (IconsPerRow + 1)));
            for (int i = start; i < end; i++)
            {
                int slot = i - start;
                int iconX = x + 5 + slot * (iconSize + 3);
                int iconY = y + 1;
                Item item = Candidates[i];
                bool hover = UIRenderer.MouseX >= iconX && UIRenderer.MouseX < iconX + iconSize &&
                             UIRenderer.MouseY >= iconY && UIRenderer.MouseY < iconY + iconSize;
                bool blocked = IsBlacklisted(config, item.type);
                if (hover)
                {
                    Main.LocalPlayer.mouseInterface = true;
                    Tooltip.Set(item.Name);
                }
                UIRenderer.DrawRect(iconX, iconY, iconSize, iconSize, blocked ? (byte)45 : (byte)25, blocked ? (byte)45 : (byte)40, blocked ? (byte)45 : (byte)55, 255);
                StorageItemDrawing.DrawIcon(item, iconX + 1, iconY + 1, iconSize - 2, iconSize - 2);
                if (blocked)
                {
                    UIRenderer.DrawRect(iconX, iconY, iconSize, iconSize, 20, 20, 20, 155);
                    UIRenderer.DrawRect(iconX + 2, iconY + iconSize / 2 - 1, iconSize - 4, 3, 220, 70, 70, 230);
                    UIRenderer.DrawRectOutline(iconX, iconY, iconSize, iconSize, 230, 80, 80, 255, 1);
                }
                else UIRenderer.DrawRectOutline(iconX, iconY, iconSize, iconSize, 90, 180, 240, 220, 1);
                if (hover && !inputBlocked && UIRenderer.MouseLeftClick)
                {
                    Toggle(config, item.type);
                    UIRenderer.ConsumeClick();
                }
            }
            return false;
        }

        private static bool IsBlacklisted(ModConfig config, int type)
        {
            QualityOfTerrariaConfig qot = config as QualityOfTerrariaConfig;
            return qot?.InfinitePotionBlacklist != null && Array.IndexOf(qot.InfinitePotionBlacklist, type) >= 0;
        }

        private static void Toggle(ModConfig config, int type)
        {
            QualityOfTerrariaConfig qot = config as QualityOfTerrariaConfig;
            if (qot == null) return;
            var values = new HashSet<int>(qot.InfinitePotionBlacklist ?? Array.Empty<int>());
            if (!values.Add(type)) values.Remove(type);
            qot.InfinitePotionBlacklist = values.ToArray();
            ConfigPropertyMeta property = qot.GetPropertyMetadata().FirstOrDefault(meta => meta.Key == nameof(QualityOfTerrariaConfig.InfinitePotionBlacklist));
            if (property != null) SaveConfig?.Invoke(null, new object[] { "quality-of-terraria", property, qot });
        }
    }
}
