using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerrariaModder.Core;
using TerrariaModder.Core.Config;
using QualityOfTerraria.Networking;

namespace QualityOfTerraria.Features
{
    internal static class VeinConfigMigration
    {
        internal static void Apply(ModContext context)
        {
            var config = context?.GetConfig<QualityOfTerrariaConfig>();
            if (config?.VeinImportStateError != null)
            {
                context.Logger?.Warn("连锁配置导入状态读取失败：" + config.VeinImportStateError);
                return;
            }
            if (config == null || ConfigManager.GetCurrentScope() != ConfigScope.Client || !config.NeedsVeinMinerImport
                || string.IsNullOrEmpty(config.FilePath)) return;
            var properties = config.GetPropertyMetadata().Where(p => QualityOfTerrariaConfig.VeinSettingKeys.Contains(p.Key)).ToArray();
            var previous = properties.ToDictionary(p => p.Key, p => p.GetValue(config));
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(config.FilePath), "vein-miner.client.json");
                if (File.Exists(path))
                {
                    var raw = ConfigManager.ParseJsonRaw(File.ReadAllText(path));
                    foreach (var property in properties)
                    {
                        string key = property.Key;
                        if (key == nameof(QualityOfTerrariaConfig.VeinMiningAutoLoot)) key = "AutoLoot";
                        else if (key == nameof(QualityOfTerrariaConfig.VeinMiningCheckPickaxePower)) key = "CheckPickaxePower";
                        else if (key == nameof(QualityOfTerrariaConfig.VeinMiningMatchExactType)) key = "MatchExactTileType";
                        else if (key == nameof(QualityOfTerrariaConfig.VeinMiningShowMessages)) key = "ShowMessages";
                        if (!raw.TryGetValue(key, out object value)) continue;
                        if (property.PropertyType == typeof(bool) && !(value is bool)) throw new InvalidDataException("旧配置开关类型错误：" + key);
                        if (property.PropertyType == typeof(int) && (value == null || value is bool || value is string
                            || Convert.ToDouble(value) != Math.Truncate(Convert.ToDouble(value))))
                            throw new InvalidDataException("旧配置整数类型错误：" + key);
                        property.SetValue(config, value);
                    }
                }
                if (!SettingsSync.SaveVerified(config, out string error)) throw new IOException(error);
                File.Delete(config.FilePath + ".vein-import.pending");
                config.NeedsVeinMinerImport = false;
            }
            catch (Exception exception)
            {
                foreach (var property in properties) property.SetValue(config, previous[property.Key]);
                context.Logger?.Warn("旧连锁挖矿配置未导入，已恢复原设置：" + exception.Message);
            }
        }
    }
}
